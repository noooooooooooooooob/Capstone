using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 광선을 반사하는 거울. <see cref="XRSimpleInteractable"/> 사용 (XRGrabInteractable 아님 —
    /// XRGrab 은 잡힌 객체 끌어당기는 부작용 있음).
    ///
    /// 회전 모드:
    ///   - PointTowardHand (기본): 거울의 reflect 면 normal 이 손 위치를 향함 — 손을 거울 주변으로
    ///     이동하면 그 방향으로 거울이 향함. 손목 회전 안 해도 됨.
    ///   - PointAwayFromHand: PointTowardHand 의 반대 방향.
    ///   - WristYawDelta: 컨트롤러 wrist yaw 변화량을 거울 yaw 에 적용.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class LightBeamMirror : MonoBehaviour
    {
        public enum RotationMode
        {
            PointTowardHand,
            PointAwayFromHand,
            WristYawDelta,
        }

        [Header("Reflect surface")]
        [Tooltip("거울이 반사하는 face 의 normal axis (local). 기본 forward = +Z 면.")]
        public Vector3 ReflectAxisLocal = Vector3.forward;

        [Tooltip("hit.normal 과 거울 normal 의 |dot| 가 이 값 이상이면 반사. 0.7≈45° 이내.")]
        [Range(0.5f, 1f)]
        public float ReflectDotThreshold = 0.7f;

        [Header("Rotation control")]
        public RotationMode Mode = RotationMode.PointTowardHand;

        [Tooltip("[WristYawDelta only] 컨트롤러 yaw 변화량을 거울 yaw 에 적용하는 비율.")]
        public float RotationSensitivity = 1.0f;

        [Tooltip("[PointToward/AwayFromHand only] 손이 거울 pivot 에 이 거리 이내면 jitter 방지 위해 회전 안 함.")]
        public float MinHandDistance = 0.08f;

        [Header("Safety locks")]
        public bool LockPosition = true;
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
                case RotationMode.PointTowardHand:  ApplyPointFromHand(false); break;
                case RotationMode.PointAwayFromHand: ApplyPointFromHand(true);  break;
                case RotationMode.WristYawDelta:    ApplyWristYawDelta();      break;
            }
        }

        void ApplyPointFromHand(bool reverseDir)
        {
            Vector3 handPos = _holdingInteractor.transform.position;
            Vector3 toHand = handPos - transform.position;
            toHand.y = 0f;
            if (toHand.sqrMagnitude < MinHandDistance * MinHandDistance) return;
            Vector3 dir = toHand.normalized;
            if (reverseDir) dir = -dir;

            float worldYawForForward = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float reflectAxisYawOffset = Mathf.Atan2(ReflectAxisLocal.x, ReflectAxisLocal.z) * Mathf.Rad2Deg;
            float worldYaw = worldYawForForward - reflectAxisYawOffset;

            float parentYaw = transform.parent != null ? transform.parent.eulerAngles.y : 0f;
            float localYaw = Mathf.DeltaAngle(0f, worldYaw - parentYaw);

            transform.localEulerAngles = new Vector3(0f, localYaw, 0f);
        }

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

        public Vector3 GetReflectNormalWorld()
            => transform.TransformDirection(ReflectAxisLocal).normalized;

        public bool IsReflectFace(Vector3 hitNormalWorld)
        {
            Vector3 mirrorN = GetReflectNormalWorld();
            float d = Mathf.Abs(Vector3.Dot(hitNormalWorld.normalized, mirrorN));
            return d >= ReflectDotThreshold;
        }
    }
}
