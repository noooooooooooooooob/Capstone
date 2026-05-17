using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 광선 굴절 퍼즐의 중앙 컨트롤러.
    ///
    /// 알고리즘 (매 frame):
    ///   1. Emitter.IsOn 이면 raycast 시작 — 아니면 모두 clear.
    ///   2. 현재 origin/dir 에서 Physics.Raycast.
    ///   3. Hit 결과에 따라:
    ///      - <see cref="LightBeamReceiver"/> : 그 receiver SetBeamHit(true), 광선 종료.
    ///      - <see cref="LightBeamMirror"/> 반사 face : Vector3.Reflect(dir, hit.normal)
    ///        으로 새 방향, hit.point + offset 에서 다음 segment.
    ///      - 그 외 (벽/플랫폼/거울 측면) : 광선 흡수.
    ///   4. 최대 <see cref="MaxBounces"/> 회까지 반복.
    ///   5. 모든 segment 끝점들을 LineRenderer 에 push.
    ///   6. 이번 frame 에 hit 안 된 receiver 들은 SetBeamHit(false).
    ///   7. 모든 receiver 동시 hit 되면 <see cref="OnAllReceiversHit"/> (1회).
    /// </summary>
    [DisallowMultipleComponent]
    public class LightBeamController : MonoBehaviour
    {
        [Header("Refs")]
        public LightBeamEmitter Emitter;

        [Tooltip("광선 경로 시각화용 LineRenderer. 보통 같은 GameObject 에 부착.")]
        public LineRenderer BeamRenderer;

        [Tooltip("hit 추적할 receiver들. 비워두면 Start 시 씬 전체에서 검색.")]
        public List<LightBeamReceiver> Receivers = new List<LightBeamReceiver>();

        [Header("Beam tuning")]
        [Tooltip("각 segment 의 raycast 최대 거리(m).")]
        public float MaxSegmentDistance = 60f;

        [Tooltip("광선이 반사할 수 있는 최대 횟수 — 무한 루프 방지.")]
        public int MaxBounces = 12;

        [Tooltip("반사 후 hit.point 에서 새 origin 으로 옮길 때 self-hit 방지 offset(m).")]
        public float ReflectOffset = 0.001f;

        [Tooltip("이 LayerMask 에 포함된 콜라이더만 광선과 상호작용. 보통 -1(everything).")]
        public LayerMask BeamMask = ~0;

        [Header("Events")]
        public UnityEvent OnAllReceiversHit;
        public UnityEvent OnAllReceiversLost;

        bool _allHitFiredPrev;
        readonly List<Vector3> _pathBuffer = new List<Vector3>(64);

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
                        Vector3 newDir = Vector3.Reflect(dir, hit.normal).normalized;
                        Vector3 newOrigin = hit.point + newDir * ReflectOffset;
                        if (Vector3.Dot(newDir, dir) > 0.9999f) break; // 무한 루프 방지
                        dir = newDir;
                        origin = newOrigin;
                        continue;
                    }

                    break; // 벽/플랫폼/거울 측면 — 흡수
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

            foreach (var r in Receivers)
            {
                if (r == null) continue;
                r.SetBeamHit(hitReceiversThisFrame.Contains(r));
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

            if (allHit && !_allHitFiredPrev)
            {
                _allHitFiredPrev = true;
                OnAllReceiversHit?.Invoke();
                Debug.Log("[LightBeam] All receivers hit — puzzle solved!");
            }
            else if (!allHit && _allHitFiredPrev)
            {
                _allHitFiredPrev = false;
                OnAllReceiversLost?.Invoke();
            }
        }
    }
}
