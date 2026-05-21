using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using PipePuz.MiniGame;

namespace PipePuz.MiniGame2
{
    /// <summary>
    /// PipeMiniGame2 의 메인 보드. 빈 slot 들과 사용자가 가져다 둔 파이프들을 관리한다.
    ///
    /// 파이프가 slot 에 들어가거나 회전하거나 빠지면 <see cref="OnFlowChanged"/> 가 호출되고,
    /// BFS 로 Source 에서 Sink 까지 흐름을 다시 계산한다.
    /// 모든 Sink 가 도달되면 <see cref="OnSolved"/> 발행.
    /// </summary>
    public class PipeMiniGame2Board : MonoBehaviour
    {
        [Header("Grid")]
        public int Width = 5;
        public int Height = 3;

        [Tooltip("flat 1D 배열, 인덱스 = x + y * Width.")]
        public PipeMiniGame2Slot[] Slots;

        [Header("Pipes")]
        public List<PipeMiniGame2Pipe> AllPipes = new List<PipeMiniGame2Pipe>();

        [Header("Snap")]
        [Tooltip("Slot 의 박스 영역 half-size (m). 파이프 위치가 slot 의 ±SnapDistance 박스 안에 들어오면 부착. " +
                 "cellSize/2 권장 (= 0.25 for cellSize 0.5). 충돌 기반 부착.")]
        public float SnapDistance = 0.25f;

        [Header("Materials")]
        public Material ConnectedMaterial;
        public Material DisconnectedMaterial;

        [Header("Events")]
        public UnityEvent OnSolved;
        public UnityEvent OnUnsolved;

        bool _isSolved;
        public bool IsSolved => _isSolved;

        void Start()
        {
            // 모든 파이프와 slot 의 Board 참조 확정.
            if (AllPipes != null)
            {
                for (int i = 0; i < AllPipes.Count; i++)
                {
                    if (AllPipes[i] != null) AllPipes[i].Board = this;
                }
            }
            if (Slots != null)
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    if (Slots[i] != null) Slots[i].Board = this;
                }
            }
            UpdateFlow();
        }

        public PipeMiniGame2Slot Get(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return null;
            int idx = x + y * Width;
            if (Slots == null || idx < 0 || idx >= Slots.Length) return null;
            return Slots[idx];
        }

        /// <summary>주어진 world 위치에서 SnapDistance 안에 있는 가장 가까운 빈 slot 반환. 없으면 null. (거리 기반 — 레거시)</summary>
        public PipeMiniGame2Slot FindNearestEmptySlot(Vector3 worldPos)
        {
            if (Slots == null) return null;
            PipeMiniGame2Slot best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot == null || !slot.IsEmpty) continue;
                float d = Vector3.Distance(worldPos, slot.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = slot;
                }
            }
            if (best != null && bestDist <= SnapDistance) return best;
            return null;
        }

        /// <summary>
        /// 충돌 기반 — worldPos 가 slot 의 ±SnapDistance 박스 안에 들어오면 그 slot 반환.
        /// 빈 slot 만 검사. 동시에 여러 박스 안에 있으면 가장 가까운 slot 반환.
        /// </summary>
        public PipeMiniGame2Slot FindContainingSlot(Vector3 worldPos)
        {
            if (Slots == null) return null;
            float half = SnapDistance;
            PipeMiniGame2Slot best = null;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot == null || !slot.IsEmpty) continue;
                Vector3 local = slot.transform.InverseTransformPoint(worldPos);
                if (Mathf.Abs(local.x) > half) continue;
                if (Mathf.Abs(local.y) > half) continue;
                if (Mathf.Abs(local.z) > half) continue;
                float dsq = local.sqrMagnitude;
                if (dsq < bestDistSq)
                {
                    bestDistSq = dsq;
                    best = slot;
                }
            }
            return best;
        }

        /// <summary>외부 (Pipe / Slot) 에서 호출 — 흐름 재계산.</summary>
        public void OnFlowChanged()
        {
            UpdateFlow();
        }

        void UpdateFlow()
        {
            var visited = new HashSet<PipeMiniGame2Slot>();
            var queue = new Queue<PipeMiniGame2Slot>();

            // 모든 Source slot 에서 BFS 시작.
            if (Slots != null)
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    var s = Slots[i];
                    if (s != null && s.CurrentPipe != null && s.CurrentPipe.Shape == PipeShape.Source)
                    {
                        queue.Enqueue(s);
                        visited.Add(s);
                    }
                }
            }

            var dirs = new[] { Direction.N, Direction.E, Direction.S, Direction.W };
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var mask = cur.CurrentMask;
                foreach (var dir in dirs)
                {
                    if ((mask & dir) == 0) continue;
                    var step = PipeShapeDef.Step(dir);
                    var next = Get(cur.X + step.dx, cur.Y + step.dy);
                    if (next == null || visited.Contains(next)) continue;
                    if (next.CurrentPipe == null) continue;
                    var opp = PipeShapeDef.Opposite(dir);
                    if ((next.CurrentMask & opp) == 0) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            // 색 갱신.
            if (Slots != null)
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    var s = Slots[i];
                    if (s == null || s.CurrentPipe == null) continue;
                    s.CurrentPipe.SetConnected(visited.Contains(s));
                }
            }
            // slot 에 안 들어가있는 파이프들은 disconnected 색.
            if (AllPipes != null)
            {
                for (int i = 0; i < AllPipes.Count; i++)
                {
                    var p = AllPipes[i];
                    if (p != null && p.CurrentSlot == null) p.SetConnected(false);
                }
            }

            // 클리어 판정.
            bool allSinksReached = true;
            int sinkCount = 0;
            if (Slots != null)
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    var s = Slots[i];
                    if (s != null && s.CurrentPipe != null && s.CurrentPipe.Shape == PipeShape.Sink)
                    {
                        sinkCount++;
                        if (!visited.Contains(s)) allSinksReached = false;
                    }
                }
            }
            if (sinkCount == 0) allSinksReached = false;

            if (allSinksReached && !_isSolved)
            {
                _isSolved = true;
                OnSolved?.Invoke();
                Debug.Log("[PipeMiniGame2] Solved!");
            }
            else if (!allSinksReached && _isSolved)
            {
                _isSolved = false;
                OnUnsolved?.Invoke();
            }
        }
    }
}
