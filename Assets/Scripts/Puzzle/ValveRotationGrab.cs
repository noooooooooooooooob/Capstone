using UnityEngine;

namespace Capstone.Puzzle
{
    /// <summary>
    /// VR 손이 밸브 핸들을 잡고 있을 때, 손의 회전 변화를 측정해서
    /// <see cref="RadiatorValve"/>로 시계/반시계 회전 델타를 전달한다.
    ///
    /// 이 컴포넌트는 어떤 그랩 시스템(XRI, Meta Interaction SDK 등)에도
    /// 직접 의존하지 않고 BeginGrab / EndGrab 메서드만 노출한다.
    /// 그랩 시스템 측에서 UnityEvent로 손쉽게 연결할 수 있다.
    ///
    /// XR Interaction Toolkit 연결 예) XRBaseInteractable의:
    ///   selectEntered → XRControllerValveGrabber.OnSelectEntered (또는 BeginGrab(Transform))
    ///   selectExited  → XRControllerValveGrabber.OnSelectExited  (또는 EndGrab())
    /// </summary>
    [DisallowMultipleComponent]
    public class ValveRotationGrab : MonoBehaviour
    {
        [SerializeField] RadiatorValve valve;

        [Tooltip("회전 측정 기준 피벗. 일반적으로 핸들(휠) Transform과 동일.")]
        [SerializeField] Transform pivot;

        [Tooltip("회전 축. RadiatorValve.rotationAxisLocal과 동일하게 둔다.")]
        [SerializeField] Vector3 rotationAxisLocal = new Vector3(0f, 0f, 1f);

        [Tooltip("그랩 손이 너무 회전축에 가까우면 부정확하므로 무시할 최소 거리(미터)")]
        [SerializeField] float minPlanarRadius = 0.02f;

        Transform _grabber;
        float _lastAngle;
        bool _isGrabbing;

        /// <summary>그랩 시작 시 호출. 인터랙터(손 컨트롤러) Transform을 넘긴다.</summary>
        public void BeginGrab(Transform grabberTransform)
        {
            if (grabberTransform == null || pivot == null) return;
            _grabber = grabberTransform;
            _lastAngle = ComputeGrabberAngle();
            _isGrabbing = true;
        }

        /// <summary>UnityEvent 시그니처 호환을 위해 GameObject도 받아주는 오버로드.</summary>
        public void BeginGrab(GameObject grabberGo)
        {
            if (grabberGo != null) BeginGrab(grabberGo.transform);
        }

        /// <summary>그랩 종료 시 호출.</summary>
        public void EndGrab()
        {
            _isGrabbing = false;
            _grabber = null;
        }

        void Update()
        {
            if (!_isGrabbing || _grabber == null || pivot == null || valve == null) return;

            float currentAngle = ComputeGrabberAngle();
            float delta = Mathf.DeltaAngle(_lastAngle, currentAngle);
            _lastAngle = currentAngle;

            // 부호 규약 정합:
            // - Unity의 SignedAngle은 회전축을 마주봤을 때 반시계가 +
            // - RadiatorValve는 시계방향(잠금)이 + 이기를 기대
            // → 부호를 반전시킨다.
            valve.ApplyRotationDelta(-delta);
        }

        /// <summary>
        /// 그랩한 손의 현재 위치를 회전축에 수직인 평면에 투영해 각도(도)를 반환.
        /// </summary>
        float ComputeGrabberAngle()
        {
            Vector3 axisWorld = pivot.TransformDirection(rotationAxisLocal.normalized);
            Vector3 fromPivot = _grabber.position - pivot.position;

            Vector3 planar = Vector3.ProjectOnPlane(fromPivot, axisWorld);
            if (planar.sqrMagnitude < minPlanarRadius * minPlanarRadius) return _lastAngle;

            // 평면 내 기준 방향: pivot.right를 평면에 투영. 평행하면 up으로 폴백.
            Vector3 reference = Vector3.ProjectOnPlane(pivot.right, axisWorld);
            if (reference.sqrMagnitude < 1e-6f)
                reference = Vector3.ProjectOnPlane(pivot.up, axisWorld);

            return Vector3.SignedAngle(reference, planar, axisWorld);
        }
    }
}
