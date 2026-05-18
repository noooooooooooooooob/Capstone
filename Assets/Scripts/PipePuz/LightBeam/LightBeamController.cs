using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 광선 굴절 퍼즐의 중앙 컨트롤러. 매 프레임 raycast + reflect 루프로 광선 경로 계산,
    /// LineRenderer 갱신, receiver 상태 업데이트.
    /// </summary>
    [DisallowMultipleComponent]
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
