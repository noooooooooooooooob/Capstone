using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz.Firefight
{
    /// <summary>
    /// 수직 펌프. Valve.cs / EMHandle.cs 와 같은 패턴 —
    /// XRBaseInteractable 의 select 이벤트만 사용하고, 핸들은 직접 손을 따라가지 않는다.
    /// 매 LateUpdate 에서 잡힌 손의 위치를 TrackTop → TrackBottom 선분에 투영해
    /// 그 t 값만큼만 Handle 의 position 을 갱신 — 수평 방향은 절대 안 움직임.
    ///
    /// Stroke 감지: 위/아래 양 끝 (StrokeThreshold 안쪽) 모두 touch 시 한 stroke 완료.
    /// </summary>
    public class FirefightPump : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("실제로 위아래로 이동할 Transform (HandleAssembly).")]
        public Transform Handle;

        [Tooltip("잡기 감지용 XRBaseInteractable. Handle 의 자식 또는 같은 GO 에 부착.")]
        public XRBaseInteractable GripInteractable;

        [Tooltip("트랙의 위쪽 끝 (handle 이 여기 있을 때 value = 0).")]
        public Transform TrackTop;

        [Tooltip("트랙의 아래쪽 끝 (handle 이 여기 있을 때 value = 1).")]
        public Transform TrackBottom;

        [Tooltip("압력 적립 대상.")]
        public FirefightController Controller;

        [Header("Stroke")]
        [Range(0.05f, 0.4f)]
        [Tooltip("value 가 이 값보다 작거나 (1 - 이 값) 보다 크면 양 끝 touch 로 간주.")]
        public float StrokeThreshold = 0.15f;

        [Tooltip("한 stroke 당 압력 보너스 (0..1).")]
        public float PumpBoost = 0.3f;

        [Tooltip("연속 stroke 사이의 최소 간격(s). spam 방지.")]
        public float MinStrokeInterval = 0.2f;

        [Header("Read-only state")]
        [Range(0f, 1f)]
        [SerializeField] float _value;
        public float CurrentValue => _value;
        public bool IsHeld => _activeInteractor != null;

        IXRSelectInteractor _activeInteractor;
        bool _topReached;
        bool _bottomReached;
        float _lastStrokeTime;

        void Awake()
        {
            if (GripInteractable != null)
            {
                GripInteractable.selectEntered.AddListener(OnGrabbed);
                GripInteractable.selectExited.AddListener(OnReleased);
            }
        }

        void OnDestroy()
        {
            if (GripInteractable != null)
            {
                GripInteractable.selectEntered.RemoveListener(OnGrabbed);
                GripInteractable.selectExited.RemoveListener(OnReleased);
            }
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            _activeInteractor = args.interactorObject;
            Debug.Log($"[FirefightPump] GRABBED by {args.interactorObject}");
        }

        void OnReleased(SelectExitEventArgs args)
        {
            _activeInteractor = null;
            Debug.Log("[FirefightPump] RELEASED");
        }

        void LateUpdate()
        {
            if (Handle == null || TrackTop == null || TrackBottom == null) return;

            Vector3 a = TrackTop.position;
            Vector3 b = TrackBottom.position;
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-6f) return;
            Vector3 dir = ab / len;

            float t;
            if (IsHeld)
            {
                // 잡힌 손의 attach 위치를 트랙 선분에 투영. 손이 좌우로 움직여도 t 는 변화 없음.
                Vector3 attachPos = _activeInteractor.GetAttachTransform(GripInteractable).position;
                Vector3 rel = attachPos - a;
                t = Mathf.Clamp(Vector3.Dot(rel, dir), 0f, len);
            }
            else
            {
                // 안 잡혔으면 마지막 위치 유지.
                t = _value * len;
            }

            Handle.position = a + dir * t;
            // 회전은 트랙 회전 따라 고정 — 손목 비틀어도 핸들 안 돌아감.
            Handle.rotation = TrackTop.rotation;
            _value = t / len;

            // Stroke 감지.
            if (_value < StrokeThreshold) _topReached = true;
            if (_value > 1f - StrokeThreshold) _bottomReached = true;

            if (_topReached && _bottomReached)
            {
                if (Time.time - _lastStrokeTime >= MinStrokeInterval)
                {
                    _lastStrokeTime = Time.time;
                    if (Controller != null)
                    {
                        Controller.PumpBoost(PumpBoost);
                        Debug.Log($"[FirefightPump] STROKE — value={_value:F2}, pressure now {Controller.CurrentPressure:F2}");
                    }
                    else
                    {
                        Debug.LogWarning("[FirefightPump] STROKE but Controller is null!");
                    }
                }
                _topReached = false;
                _bottomReached = false;
            }
        }
    }
}
