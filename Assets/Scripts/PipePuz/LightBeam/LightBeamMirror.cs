using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 광선을 반사하는 거울.
    ///
    /// 반사면: 거울의 local <see cref="ReflectAxisLocal"/> (기본 +Z = forward) 와 그 반대(-forward).
    /// 측면(±X) / 상하(±Y) 면은 흡수.
    ///
    /// **인터랙션 방식**: <see cref="XRSimpleInteractable"/> 사용 (XRGrabInteractable 아님).
    /// XRGrab 은 잡힌 객체를 컨트롤러 grip 위치로 끌어당기는 부작용이 있어, 거울처럼
    /// "위치 고정 + 회전만 변경" 이 필요한 경우 부적합. 대신 select 이벤트만 받고
    /// <see cref="RotationMode"/> 에 따라 거울 yaw 를 직접 계산. 객체 position 은
    /// 절대 안 움직임.
    ///
    /// 회전 모드:
    ///   - PointTowardHand (기본): 거울의 reflect 면 normal 이 손 위치를 향함 — 손을
    ///     거울 주변으로 이동하면 그 방향으로 거울이 향함. 손목 회전 안 해도 됨.
    ///   - PointAwayFromHand: PointTowardHand 의 반대 방향.
    ///   - WristYawDelta: 컨트롤러 wrist yaw 변화량을 거울 yaw 에 적용 (이전 방식).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class LightBeamMirror : MonoBehaviour
    {
        public enum RotationMode
        {
            /// <summary>거울의 reflect 면 normal 이 손 위치 방향을 향함.</summary>
            PointTowardHand,
            /// <summary>거울의 reflect 면 normal 이 손 위치의 반대 방향.</summary>
            PointAwayFromHand,
            /// <summary>컨트롤러 wrist yaw 변화량을 거울 yaw 에 그대로 적용.</summary>
            WristYawDelta,
        }

        [Header("Reflect surface")]
        [Tooltip("거울이 반사하는 face 의 normal axis (local). 기본 forward = +Z 면.")]
        public Vector3 ReflectAxisLocal = Vector3.forward;

        [Tooltip("hit.normal 과 거울 normal 의 |dot| 가 이 값 이상이면 반사. 0.7≈45° 이내.")]
        [Range(0.5f, 1f)]
        public float ReflectDotThreshold = 0.7f;

        [Header("Rotation control")]
        [Tooltip("회전 방식. PointTowardHand 가 VR 에서 가장 직관적.")]
        public RotationMode Mode = RotationMode.PointTowardHand;

        [Tooltip("[WristYawDelta only] 컨트롤러 yaw 변화량을 거울 yaw 에 적용하는 비율.")]
        public float RotationSensitivity = 1.0f;

        [Tooltip("[PointToward/AwayFromHand only] 손이 거울 pivot 에 이 거리 이내면 jitter 방지 위해 회전 안 함.")]
        public float MinHandDistance = 0.08f;

        [Header("Safety locks")]
        [Tooltip("true 면 위치가 절대 안 움직이도록 매 LateUpdate 에 강제 복원.")]
        public bool LockPosition = true;

        [Tooltip("true 면 X/Z 회전을 매 LateUpdate 에서 0으로 강제 (yaw 만 유지).")]
        public bool LockToYawOnly = true;

        XRSimpleInteractable _interactable;
        IXRSelectInteractor _holdingInteractor;
        float _anchorMirrorYaw;
        float _anchorInteractorYaw;
        Vector3 _initialLocalPos;
        bool _initialized;

        public bool IsHeld => _holdingInteractor != null;

        void Awake()
        {
            _initialLocalPos = transform.localPosition;
            _initialized = true;

            _interactable = GetComponent<XRSimpleInteractable>();
            if (_interactable != null)
            {
                _interactable.selectEntered.AddListener(OnSelected);
                _interactable.selectExited.AddListener(OnDeselected);
            }
        }

        void OnDestroy()
        {
            if (_interactable != null)
            {
                _interactable.selectEntered.RemoveListener(OnSelected);
                _interactable.selectExited.RemoveListener(OnDeselected);
            }
        }

        void OnSelected(SelectEnterEventArgs args)
        {
            _holdingInteractor = args.interactorObject;
            // WristYawDelta 모드용 anchor — 다른 모드에선 안 쓰임.
            _anchorMirrorYaw = transform.localEulerAngles.y;
            _anchorInteractorYaw = GetInteractorYaw();
        }

        void OnDeselected(SelectExitEventArgs args)
        {
            _holdingInteractor = null;
        }

        void Update()
        {
            if (_holdingInteractor == null) return;
            switch (Mode)
            {
                case RotationMode.PointTowardHand:
                    ApplyPointFromHand(reverseDir: false);
                    break;
                case RotationMode.PointAwayFromHand:
                    ApplyPointFromHand(reverseDir: true);
                    break;
                case RotationMode.WristYawDelta:
                    ApplyWristYawDelta();
                    break;
            }
        }

        /// <summary>거울의 reflect 면이 손 위치를 향하도록 (또는 반대 방향) 회전 설정.</summary>
        void ApplyPointFromHand(bool reverseDir)
        {
            Vector3 handPos = _holdingInteractor.transform.position;
            Vector3 toHand = handPos - transform.position;
            toHand.y = 0f; // 수평 plane 만 사용
            if (toHand.sqrMagnitude < MinHandDistance * MinHandDistance) return;
            Vector3 dir = toHand.normalized;
            if (reverseDir) dir = -dir;

            // World yaw 계산: dir 이 +Z 방향이면 yaw=0, +X 방향이면 yaw=90.
            // ReflectAxisLocal 이 forward(+Z) 가 아닌 경우를 고려해 추가 보정.
            float worldYawForForward = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            // ReflectAxisLocal 이 +Z(forward) 가 아니면 그 차이만큼 보정.
            // 기본 (0,0,1) 이면 보정 0 — 거울의 transform.forward 가 dir 이 되도록.
            float reflectAxisYawOffset = Mathf.Atan2(ReflectAxisLocal.x, ReflectAxisLocal.z) * Mathf.Rad2Deg;
            float worldYaw = worldYawForForward - reflectAxisYawOffset;

            // parent 의 world yaw 를 빼서 local yaw 계산 (parent 가 회전돼 있을 수 있음).
            float parentYaw = transform.parent != null ? transform.parent.eulerAngles.y : 0f;
            float localYaw = Mathf.DeltaAngle(0f, worldYaw - parentYaw);

            transform.localEulerAngles = new Vector3(0f, localYaw, 0f);
        }

        /// <summary>이전 방식 — 컨트롤러 wrist yaw 변화량을 거울 yaw 에 그대로 적용.</summary>
        void ApplyWristYawDelta()
        {
            float currentInteractorYaw = GetInteractorYaw();
            float delta = Mathf.DeltaAngle(_anchorInteractorYaw, currentInteractorYaw);
            float newYaw = _anchorMirrorYaw + delta * RotationSensitivity;
            transform.localEulerAngles = new Vector3(0f, newYaw, 0f);
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            if (LockPosition) transform.localPosition = _initialLocalPos;
            if (LockToYawOnly)
            {
                Vector3 euler = transform.localEulerAngles;
                if (Mathf.Abs(euler.x) > 0.01f || Mathf.Abs(euler.z) > 0.01f)
                    transform.localEulerAngles = new Vector3(0f, euler.y, 0f);
            }
        }

        float GetInteractorYaw()
        {
            if (_holdingInteractor == null) return 0f;
            var t = _holdingInteractor.transform;
            return t != null ? t.eulerAngles.y : 0f;
        }

        /// <summary>현재 거울의 world space 반사 normal (+축).</summary>
        public Vector3 GetReflectNormalWorld()
            => transform.TransformDirection(ReflectAxisLocal).normalized;

        /// <summary>주어진 hit.normal 이 거울의 반사 face 인지 검사 (양면 모두 반사).</summary>
        public bool IsReflectFace(Vector3 hitNormalWorld)
        {
            Vector3 mirrorN = GetReflectNormalWorld();
            float d = Mathf.Abs(Vector3.Dot(hitNormalWorld.normalized, mirrorN));
            return d >= ReflectDotThreshold;
        }
    }
}
