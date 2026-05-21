using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 광선 굴절 퍼즐의 중앙 컨트롤러. 매 프레임 raycast + reflect 루프로 광선 경로 계산,
    /// LineRenderer 갱신, receiver 상태 업데이트.
    ///
    /// RequiredOrderPanel 이 연결되면 빔이 거울들을 ColorId 기준 정확한 순서로 거치고
    /// receiver 에 도달해야만 OnAllReceiversHit 가 발행됨 (색상 순서 검증).
    ///
    /// DefaultExecutionOrder=100 — BeamAimController(order 50) 가 emitter 위치를 갱신한 뒤에
    /// 같은 프레임 안에서 빔이 새 위치로 재계산되도록 실행 순서를 강제.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class LightBeamController : MonoBehaviour
    {
        [Header("Refs")]
        public LightBeamEmitter Emitter;
        public LineRenderer BeamRenderer;
        public List<LightBeamReceiver> Receivers = new List<LightBeamReceiver>();

        [Header("Beam tuning")]
        public float MaxSegmentDistance = 60f;
        public int MaxBounces = 12;
        public float ReflectOffset = 0.001f;
        public LayerMask BeamMask = ~0;

        [Header("Color order (optional)")]
        [Tooltip("연결되면 빔이 거울 ColorId 들을 이 패널의 RequiredSequence 와 정확히 일치하는 순서로 " +
                 "거쳐야 OnAllReceiversHit 가 발행됨. 비워두면 순서 검증 무시.")]
        public ColorOrderPanel RequiredOrderPanel;

        [Header("Events")]
        public UnityEvent OnAllReceiversHit;
        public UnityEvent OnAllReceiversLost;

        bool _allHitFiredPrev;
        readonly List<Vector3> _pathBuffer = new List<Vector3>(64);
        readonly List<int> _hitColorIds = new List<int>(8);

        void Start()
        {
            if (Receivers == null || Receivers.Count == 0)
            {
                Receivers = new List<LightBeamReceiver>(
                    FindObjectsByType<LightBeamReceiver>(FindObjectsSortMode.None));
            }
        }

        void Update()
        {
            if (Emitter == null || !Emitter.IsOn)
            {
                ClearBeam();
                CheckAllReceiversEvent();
                return;
            }
            CastBeam();
            CheckAllReceiversEvent();
        }

        void CastBeam()
        {
            _pathBuffer.Clear();
            _hitColorIds.Clear();
            Vector3 origin = Emitter.Origin;
            Vector3 dir = Emitter.Direction;
            _pathBuffer.Add(origin);

            var hitReceiversThisFrame = new HashSet<LightBeamReceiver>();

            for (int bounce = 0; bounce <= MaxBounces; bounce++)
            {
                if (Physics.Raycast(origin, dir, out RaycastHit hit,
                    MaxSegmentDistance, BeamMask, QueryTriggerInteraction.Collide))
                {
                    _pathBuffer.Add(hit.point);

                    var receiver = hit.collider.GetComponent<LightBeamReceiver>()
                                ?? hit.collider.GetComponentInParent<LightBeamReceiver>();
                    if (receiver != null)
                    {
                        hitReceiversThisFrame.Add(receiver);
                        break;
                    }

                    var mirror = hit.collider.GetComponent<LightBeamMirror>()
                              ?? hit.collider.GetComponentInParent<LightBeamMirror>();
                    if (mirror != null && mirror.IsReflectFace(hit.normal))
                    {
                        // 색상 ID 가 부여된 거울이면 hit 시퀀스에 기록 — 순서 검증의 입력.
                        if (mirror.ColorId >= 0) _hitColorIds.Add(mirror.ColorId);

                        Vector3 newDir = Vector3.Reflect(dir, hit.normal).normalized;
                        Vector3 newOrigin = hit.point + newDir * ReflectOffset;
                        if (Vector3.Dot(newDir, dir) > 0.9999f) break;
                        dir = newDir;
                        origin = newOrigin;
                        continue;
                    }

                    break;
                }
                else
                {
                    _pathBuffer.Add(origin + dir * MaxSegmentDistance);
                    break;
                }
            }

            if (BeamRenderer != null)
            {
                BeamRenderer.positionCount = _pathBuffer.Count;
                for (int i = 0; i < _pathBuffer.Count; i++)
                    BeamRenderer.SetPosition(i, _pathBuffer[i]);
            }

            // 순서 검증 — 거울들을 정해진 순서로 모두 거쳤어야 receiver 시각·이벤트가 발동.
            // 순서 틀리거나 거울을 빠뜨리면 빔이 receiver 에 닿아도 idle 로 표시됨.
            bool orderOk = IsOrderSatisfied();

            foreach (var r in Receivers)
            {
                if (r == null) continue;
                r.SetBeamHit(hitReceiversThisFrame.Contains(r) && orderOk);
            }
        }

        void ClearBeam()
        {
            if (BeamRenderer != null) BeamRenderer.positionCount = 0;
            foreach (var r in Receivers)
                if (r != null) r.SetBeamHit(false);
        }

        void CheckAllReceiversEvent()
        {
            if (Receivers == null || Receivers.Count == 0) return;
            bool allHit = true;
            foreach (var r in Receivers)
            {
                if (r == null || !r.IsHit) { allHit = false; break; }
            }
            bool orderOk = IsOrderSatisfied();
            bool solved = allHit && orderOk;
            if (solved && !_allHitFiredPrev)
            {
                _allHitFiredPrev = true;
                OnAllReceiversHit?.Invoke();
                Debug.Log("[LightBeam] All receivers hit AND order matches — puzzle solved!");
            }
            else if (!solved && _allHitFiredPrev)
            {
                _allHitFiredPrev = false;
                OnAllReceiversLost?.Invoke();
            }
        }

        /// <summary>
        /// 빔이 거친 ColorId 시퀀스가 RequiredOrderPanel.RequiredSequence 와 같은지 검사.
        /// 패널이 없으면 통과(검증 무시), 미완성 시퀀스면 false.
        /// </summary>
        bool IsOrderSatisfied()
        {
            if (RequiredOrderPanel == null) return true;
            if (!RequiredOrderPanel.IsComplete) return false;
            var required = RequiredOrderPanel.RequiredSequence;
            if (_hitColorIds.Count != required.Count) return false;
            for (int i = 0; i < required.Count; i++)
                if (_hitColorIds[i] != required[i]) return false;
            return true;
        }

        /// <summary>이번 프레임에 빔이 거친 거울 ColorId 시퀀스 (외부 디버그/UI 노출).</summary>
        public IReadOnlyList<int> CurrentHitColorIds => _hitColorIds;
    }
}
