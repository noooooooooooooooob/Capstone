using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz
{
    /// <summary>
    /// 원형 밸브. 잡고 회전시키면 Openness(0~1)가 바뀐다.
    /// CCW(반시계, 손잡이 시점) → 열림, CW(시계) → 닫힘.
    /// 초기값 1f(완전 열림). PairedValve 가 지정되면 양쪽 Openness 가 동기화된다.
    ///
    /// XRI 3.4 의 XRBaseInteractable 을 상속해서, 잡힌 손의 위치를
    /// 밸브의 회전축 평면에 투영해 각도 변화량을 누적한다.
    /// 시각 회전은 <see cref="Wheel"/> Transform 에 적용한다(없으면 self).
    /// </summary>
    public class Valve : XRBaseInteractable
    {
        [Header("State")]
        [Range(0f, 1f)]
        [Tooltip("1 = 완전 열림, 0 = 완전 닫힘. 초기값 1.")]
        public float Openness = 1f;

        [Header("Rotation")]
        [Tooltip("밸브의 회전축(로컬 좌표). 보통 파이프 축과 평행한 방향.")]
        public Vector3 LocalAxis = Vector3.forward;

        [Tooltip("열림에서 닫힘까지 필요한 총 회전 각도(°).")]
        public float MaxAngle = 720f;

        [Tooltip("LocalAxis 양의 방향에서 봤을 때 CCW 가 열림이 되도록 해야 할 경우 켠다. " +
                 "현실 밸브 동작에 맞게 미러된 RadiatorB 쪽에서 부호를 뒤집을 때 사용.")]
        public bool InvertDirection = false;

        [Tooltip("밸브 시각이 적용될 자식 Transform. 비워두면 자기 자신을 사용한다.")]
        public Transform Wheel;

        [Header("Grab Region")]
        [Tooltip("이 거리(m) 보다 손이 밸브 중심에 가까우면 잡히지 않는다. " +
                 "중앙(허브) 잡기를 막기 위해 사용. 0 이면 비활성.")]
        public float MinGrabRadius = 0.15f;

        [Tooltip("이 거리(m) 보다 손이 밸브 중심에서 멀면 잡히지 않는다. " +
                 "0 이하면 비활성.")]
        public float MaxGrabRadius = 0.4f;

        [Header("Sync")]
        [Tooltip("반대편 라디에이터의 Valve. 양쪽이 서로를 가리켜야 한다.")]
        public Valve PairedValve;

        /// <summary>Openness 가 바뀔 때마다 호출 (사용자 조작·동기화 모두 포함).</summary>
        public event Action<float> OpennessChanged;

        IXRSelectInteractor _activeInteractor;
        float _lastAngle;
        float _accumulatedClose; // 0(열림) ~ MaxAngle(닫힘)
        Quaternion _wheelBaseRot;
        bool _suppressPropagate;

        protected override void Awake()
        {
            base.Awake();
            if (Wheel == null) Wheel = transform;
            _wheelBaseRot = Wheel.localRotation;
            _accumulatedClose = (1f - Mathf.Clamp01(Openness)) * MaxAngle;
            ApplyWheelVisual();
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            _activeInteractor = args.interactorObject;
            _lastAngle = ComputeInteractorAngle();
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor)) return false;
            // 손의 위치를 휠 평면에 투영해 반경 거리 체크.
            // 중앙(허브) 부근에서는 잡히지 않게, 즉 가장자리 영역에서만 select 허용.
            Vector3 axisWorld = transform.TransformDirection(LocalAxis).normalized;
            Vector3 attachPos = interactor.GetAttachTransform(this).position;
            Vector3 toI = attachPos - transform.position;
            float radial = Vector3.ProjectOnPlane(toI, axisWorld).magnitude;

            if (MinGrabRadius > 0f && radial < MinGrabRadius) return false;
            if (MaxGrabRadius > 0f && radial > MaxGrabRadius) return false;
            return true;
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            _activeInteractor = null;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
            if (_activeInteractor == null) return;

            float current = ComputeInteractorAngle();
            float delta = Mathf.DeltaAngle(_lastAngle, current);
            _lastAngle = current;
            if (InvertDirection) delta = -delta;

            // delta 의 부호 규약:
            //   atan2 + 오른손 좌표 표준에서 delta > 0 == LocalAxis(+Z) 축 기준 수학적 CCW.
            //   하지만 플레이어는 +Z 쪽에서 -Z 를 바라보는 자세 → Unity 카메라 기준 right = -X.
            //   이 시점에서 수학적 CCW 는 플레이어 시야엔 CW 로 보인다.
            //   즉 "사용자가 시계방향으로 돌리면(닫음)" delta > 0. 그래서 close 누적값을 + 시킨다.
            _accumulatedClose = Mathf.Clamp(_accumulatedClose + delta, 0f, MaxAngle);
            float newOpenness = 1f - _accumulatedClose / MaxAngle;
            if (!Mathf.Approximately(newOpenness, Openness))
            {
                SetOpennessInternal(newOpenness, propagate: true);
            }
        }

        /// <summary>외부에서 직접 Openness 를 설정. 페어드 밸브에도 전파한다.</summary>
        public void SetOpenness(float value)
        {
            SetOpennessInternal(Mathf.Clamp01(value), propagate: true);
        }

        /// <summary>페어 동기화 시 호출. 다시 페어로 전파하지 않는다.</summary>
        public void ApplySyncedOpenness(float value)
        {
            SetOpennessInternal(Mathf.Clamp01(value), propagate: false);
        }

        void SetOpennessInternal(float value, bool propagate)
        {
            Openness = value;
            _accumulatedClose = (1f - Openness) * MaxAngle;
            ApplyWheelVisual();
            OpennessChanged?.Invoke(Openness);

            if (propagate && PairedValve != null && !_suppressPropagate)
            {
                _suppressPropagate = true;
                PairedValve.ApplySyncedOpenness(Openness);
                _suppressPropagate = false;
            }
        }

        void ApplyWheelVisual()
        {
            // 닫힘이 진행될수록 양의 각도로 회전. (CCW 가 열림이라는 정의를 따른다.)
            // delta * base 순서: base 의 결과물(예: Euler(90,0,0)에 의해 디스크가 세워진 상태)
            // 위에서 LocalAxis(Z) 축으로 회전 → 실제 "바퀴가 자기 노멀 축으로 도는" 모양이 된다.
            // base * delta 로 두면 mesh-Z 가 먼저 회전한 뒤 base 가 적용되어 디스크가 헛돌게 된다.
            float angle = (1f - Openness) * MaxAngle;
            Wheel.localRotation = Quaternion.AngleAxis(angle, LocalAxis) * _wheelBaseRot;
        }

        float ComputeInteractorAngle()
        {
            // 잡힌 손(Attach Transform)의 위치를 밸브의 회전축에 수직인 평면에 투영하여 각도 계산.
            Vector3 axisWorld = transform.TransformDirection(LocalAxis).normalized;
            Vector3 attachPos = _activeInteractor.GetAttachTransform(this).position;
            Vector3 toI = attachPos - transform.position;
            toI = Vector3.ProjectOnPlane(toI, axisWorld);

            // 안정적인 기준 벡터를 위해 transform.up → transform.forward → transform.right 순으로 시도.
            Vector3 basisRight = Vector3.ProjectOnPlane(transform.up, axisWorld);
            if (basisRight.sqrMagnitude < 1e-6f)
                basisRight = Vector3.ProjectOnPlane(transform.forward, axisWorld);
            if (basisRight.sqrMagnitude < 1e-6f)
                basisRight = Vector3.ProjectOnPlane(transform.right, axisWorld);
            basisRight.Normalize();
            Vector3 basisUp = Vector3.Cross(axisWorld, basisRight);

            float x = Vector3.Dot(toI, basisRight);
            float y = Vector3.Dot(toI, basisUp);
            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }
    }
}
