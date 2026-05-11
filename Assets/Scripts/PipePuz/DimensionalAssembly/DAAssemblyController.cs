using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.DimensionalAssembly
{
    /// <summary>
    /// 차원 분할 조립 퍼즐의 메인 컨트롤러.
    ///
    /// 매 프레임:
    ///   aligned = gear.IsHeld && |gear.CurrentAngle - TargetAngle| < AngleTolerance
    ///   - 정렬 깨지는 순간: 모든 활성 연결 Break + 양 끝점에 쇼크 VFX
    ///   - aligned 일 때 노드들 모두 Active 시각으로
    ///   - aligned && 모든 RequiredPairs 가 활성 연결 안에 있음 → LockProgress 누적, 100% 시 OnSolved
    /// </summary>
    public class DAAssemblyController : MonoBehaviour
    {
        [Header("Refs")]
        public DAGear Gear;
        public DAEnergyNode[] Nodes;
        public DAConnectionWand Wand;

        [Header("Connection visual")]
        public Material ConnectionMaterial;
        public Color ConnectionColor = new Color(0.4f, 0.85f, 1.4f);
        public float ConnectionWidth = 0.012f;
        public Transform ConnectionsRoot;

        [Header("Shock VFX")]
        [Tooltip("정렬이 깨질 때 끊긴 연결의 양 끝점에서 burst 되는 ParticleSystem. World simulation 권장.")]
        public ParticleSystem ShockEmitter;
        public int ShockParticlesPerEndpoint = 24;

        [Header("Targets")]
        public float TargetAngle = 45f;
        public float AngleTolerance = 6f;

        [Tooltip("필요한 연결 쌍을 노드 Id 로 지정. 1차 단순 버전 기본: 0-1, 1-2, 2-3.")]
        public Vector2Int[] RequiredPairs = new[]
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 2),
            new Vector2Int(2, 3),
        };

        [Header("Lock timing")]
        public float LockDuration = 2.5f;
        public float DecayDuration = 1.0f;

        [Header("Events")]
        public UnityEvent OnAligned;
        public UnityEvent OnMisaligned;
        public UnityEvent OnConnectionAdded;
        public UnityEvent OnConnectionBroken;
        public UnityEvent<float> OnLockProgressChanged;
        public UnityEvent OnSolved;

        public float LockProgress { get; private set; }
        public bool IsSolved { get; private set; }
        public bool IsAligned { get; private set; }
        public IReadOnlyList<DAConnection> ActiveConnections => _connections;

        readonly List<DAConnection> _connections = new List<DAConnection>();
        bool _wasAligned;

        void Start()
        {
            UpdateNodeActivation(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // 정렬 검사 — gear 가 잡혀있어야 의미가 있음 (놓으면 drift 로 0 으로 떨어지니까).
            float angle = Gear != null ? Gear.CurrentAngle : 0f;
            bool gearHeld = Gear != null && Gear.IsHeld;
            float angleErr = Mathf.Abs(Mathf.DeltaAngle(angle, TargetAngle));
            bool aligned = gearHeld && angleErr < AngleTolerance;

            // 상태 전이.
            if (aligned && !_wasAligned)
            {
                OnAligned?.Invoke();
            }
            if (!aligned && _wasAligned)
            {
                OnMisaligned?.Invoke();
                BreakAllConnections(withShock: true);
            }
            _wasAligned = aligned;
            IsAligned = aligned;

            UpdateNodeActivation(aligned);

            // 필요 연결 모두 만족?
            bool allRequired = AllRequiredSatisfied();

            // Lock progress.
            float prev = LockProgress;
            if (aligned && allRequired)
                LockProgress = Mathf.Min(1f, LockProgress + dt / Mathf.Max(0.01f, LockDuration));
            else
                LockProgress = Mathf.Max(0f, LockProgress - dt / Mathf.Max(0.01f, DecayDuration));

            if (!Mathf.Approximately(prev, LockProgress))
                OnLockProgressChanged?.Invoke(LockProgress);

            if (LockProgress >= 1f && !IsSolved)
            {
                IsSolved = true;
                OnSolved?.Invoke();
                Debug.Log("[DAAssembly] Solved!");
            }
        }

        void UpdateNodeActivation(bool aligned)
        {
            if (Nodes == null) return;
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i] != null) Nodes[i].SetActive(aligned);
        }

        bool AllRequiredSatisfied()
        {
            if (RequiredPairs == null || RequiredPairs.Length == 0) return false;
            for (int i = 0; i < RequiredPairs.Length; i++)
            {
                var p = RequiredPairs[i];
                var a = GetNodeById(p.x);
                var b = GetNodeById(p.y);
                if (a == null || b == null) return false;
                bool found = false;
                for (int j = 0; j < _connections.Count; j++)
                {
                    if (_connections[j] != null && _connections[j].MatchesPair(a, b)) { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }

        DAEnergyNode GetNodeById(int id)
        {
            if (Nodes == null) return null;
            for (int i = 0; i < Nodes.Length; i++)
                if (Nodes[i] != null && Nodes[i].Id == id) return Nodes[i];
            return null;
        }

        /// <summary>Wand 가 호출. 두 노드 사이에 와이어 추가. 이미 존재하거나 비정렬 상태면 거부.</summary>
        public bool TryAddConnection(DAEnergyNode a, DAEnergyNode b)
        {
            if (a == null || b == null || a == b) return false;
            if (!IsAligned) return false;
            for (int i = 0; i < _connections.Count; i++)
                if (_connections[i] != null && _connections[i].MatchesPair(a, b)) return false;

            var go = new GameObject($"Connection_{a.Id}_{b.Id}");
            go.transform.SetParent(ConnectionsRoot != null ? ConnectionsRoot : transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = ConnectionWidth;
            line.endWidth = ConnectionWidth;
            line.numCapVertices = 2;
            line.numCornerVertices = 0;
            line.material = ConnectionMaterial;
            line.startColor = ConnectionColor;
            line.endColor = ConnectionColor;
            line.SetPosition(0, a.transform.position);
            line.SetPosition(1, b.transform.position);

            var conn = go.AddComponent<DAConnection>();
            conn.NodeA = a;
            conn.NodeB = b;
            conn.Line = line;
            _connections.Add(conn);

            OnConnectionAdded?.Invoke();
            return true;
        }

        void BreakAllConnections(bool withShock)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                var c = _connections[i];
                if (c == null) continue;
                if (withShock)
                {
                    if (c.NodeA != null) EmitShock(c.NodeA.transform.position);
                    if (c.NodeB != null) EmitShock(c.NodeB.transform.position);
                }
                c.Break();
            }
            if (_connections.Count > 0) OnConnectionBroken?.Invoke();
            _connections.Clear();
        }

        void EmitShock(Vector3 pos)
        {
            if (ShockEmitter == null) return;
            var p = new ParticleSystem.EmitParams { position = pos };
            ShockEmitter.Emit(p, ShockParticlesPerEndpoint);
        }

        /// <summary>외부 hook: 퍼즐 초기화. 모든 연결 끊기 + lock 0.</summary>
        public void ResetPuzzle()
        {
            BreakAllConnections(withShock: false);
            LockProgress = 0f;
            IsSolved = false;
            _wasAligned = false;
        }
    }
}
