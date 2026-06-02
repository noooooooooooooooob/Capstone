using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz.SmokePuzzle
{
    /// <summary>
    /// 무한 회전 가능한 휠. Valve 와 같은 잡힘·각도 추적 방식을 쓰지만 누적 각도에
    /// 클램프가 없고, 매 프레임 "닫힘 방향 회전 속도(deg/s)" 를 노출한다.
    ///
    /// 사용자가 닫힘 쪽(시계 방향)으로 돌리는 동안 <see cref="CurrentCloseDegPerSec"/> > 0,
    /// 손을 놓거나 멈추면 ReleaseDecayTime 안에 0 으로 떨어진다.
    /// PipeAllPuzzleController 가 이 값을 읽어 연기 강도를 누른다.
    /// </summary>
    public class SuppressionWheel : XRBaseInteractable
    {
        [Header("Rotation")]
        [Tooltip("휠의 회전축(로컬 좌표). 보통 파이프 축과 평행한 방향.")]
        public Vector3 LocalAxis = Vector3.forward;

        [Tooltip("LocalAxis 양의 방향에서 본 CCW = 열림이 되도록 부호를 뒤집어야 할 때 켠다.")]
        public bool InvertDirection = false;

        [Tooltip("밸브의 총 가동 범위(°). 0°(시작)에서 이 각도까지만 돌아간다. " +
                 "Normalized01 = AccumulatedCloseDeg / ValveRangeDeg 로 게이지 Pointer 위치에 매핑된다. " +
                 "예: 360 이면 한 바퀴로 게이지를 끝에서 끝까지 훑는다.")]
        public float ValveRangeDeg = 360f;

        [Tooltip("시각 회전이 적용될 자식 Transform. 비우면 자기 자신.")]
        public Transform Wheel;

        [Header("Grab Region")]
        [Tooltip("이 거리(m) 보다 손이 휠 중심에 가까우면 잡히지 않는다. 0 이면 비활성.")]
        public float MinGrabRadius = 0.15f;

        [Tooltip("이 거리(m) 보다 손이 휠 중심에서 멀면 잡히지 않는다. 0 이하면 비활성.")]
        public float MaxGrabRadius = 0.4f;

        [Header("Speed Smoothing")]
        [Range(0f, 0.95f)]
        [Tooltip("회전 속도 EMA 의 historical weight. 0=즉시 반영(떨림), 0.95=매우 부드러움.")]
        public float SmoothingWeight = 0.55f;

        [Tooltip("손을 놓았을 때 CurrentCloseDegPerSec 이 0 으로 감쇠하는 시간 상수(s).")]
        public float ReleaseDecayTime = 0.4f;

        /// <summary>닫힘 방향(사용자 시야 기준 시계 방향) 회전 속도. 손을 놓거나 멈추면 0 으로 감쇠.</summary>
        public float CurrentCloseDegPerSec { get; private set; }

        /// <summary>누적 회전 각도(°). 양방향으로 누적되며 [0, ValveRangeDeg] 로 클램프된다.
        /// CW(시계) 로 돌리면 증가, CCW(반시계) 로 돌리면 감소.</summary>
        public float AccumulatedCloseDeg { get; private set; }

        /// <summary>밸브 위치를 0~1 로 정규화한 값. 0=시작(완전 반시계), 1=ValveRangeDeg(완전 시계).
        /// SmokeGauge 가 이 값을 읽어 Pointer 를 회전시킨다.</summary>
        public float Normalized01 =>
            ValveRangeDeg > 0.0001f ? Mathf.Clamp01(AccumulatedCloseDeg / ValveRangeDeg) : 0f;

        /// <summary>
        /// true 면 로컬 입력 처리를 멈추고 네트워크(SuppressionWheelNetworkSync)가
        /// 회전/속도를 직접 주입한다. 권위가 없는(상대가 돌리고 있는) 피어에서 켜진다.
        /// </summary>
        [System.NonSerialized] public bool ExternallyDriven;

        IXRSelectInteractor _activeInteractor;
        float _lastAngle;
        Quaternion _wheelBaseRot;

        protected override void Awake()
        {
            base.Awake();
            if (Wheel == null) Wheel = transform;
            _wheelBaseRot = Wheel.localRotation;
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor)) return false;
            Vector3 axisWorld = transform.TransformDirection(LocalAxis).normalized;
            Vector3 attachPos = interactor.GetAttachTransform(this).position;
            Vector3 toI = attachPos - transform.position;
            float radial = Vector3.ProjectOnPlane(toI, axisWorld).magnitude;
            if (MinGrabRadius > 0f && radial < MinGrabRadius) return false;
            if (MaxGrabRadius > 0f && radial > MaxGrabRadius) return false;
            return true;
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            _activeInteractor = args.interactorObject;
            _lastAngle = ComputeInteractorAngle();
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

            // 네트워크 권위가 없는 피어 — 회전/속도는 ApplyNetworkState 로 주입되므로 로컬 처리 생략.
            if (ExternallyDriven) return;

            float dt = Time.deltaTime;
            if (dt < 1e-5f) return;

            float closeDelta = 0f;
            if (_activeInteractor != null)
            {
                float cur = ComputeInteractorAngle();
                float delta = Mathf.DeltaAngle(_lastAngle, cur);
                _lastAngle = cur;
                if (InvertDirection) delta = -delta;
                // 규약: delta > 0 == 사용자 시야 기준 시계 방향(CW). 양방향 모두 부호 그대로 누적.
                closeDelta = delta;
                // [0, ValveRangeDeg] 로 클램프 — 밸브가 양끝에서 멈추고 게이지 sweep 과 1:1 매핑.
                AccumulatedCloseDeg = Mathf.Clamp(AccumulatedCloseDeg + delta, 0f, ValveRangeDeg);
            }

            // 회전 속도(부호 포함). 손을 놓으면 0 으로 감쇠.
            float instantDegPerSec = closeDelta / dt;
            if (_activeInteractor == null)
            {
                float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, ReleaseDecayTime));
                CurrentCloseDegPerSec = Mathf.Lerp(CurrentCloseDegPerSec, 0f, k);
            }
            else
            {
                CurrentCloseDegPerSec = Mathf.Lerp(instantDegPerSec, CurrentCloseDegPerSec, SmoothingWeight);
            }

            // 시각 회전 — 누적각(클램프됨)을 그대로 적용. 양방향으로 돌리면 휠도 양방향으로 회전.
            if (Wheel != null)
                Wheel.localRotation = Quaternion.AngleAxis(AccumulatedCloseDeg, LocalAxis) * _wheelBaseRot;
        }

        /// <summary>
        /// 네트워크(권위 피어)에서 받은 누적 회전각·닫힘 속도를 그대로 주입한다.
        /// 비권위 피어에서 매 Render 마다 호출되어 휠이 상대와 동일하게 돌아간다.
        /// </summary>
        public void ApplyNetworkState(float accumulatedDeg, float closeDegPerSec)
        {
            AccumulatedCloseDeg = accumulatedDeg;
            CurrentCloseDegPerSec = closeDegPerSec;
            if (Wheel != null)
                Wheel.localRotation = Quaternion.AngleAxis(AccumulatedCloseDeg, LocalAxis) * _wheelBaseRot;
        }

        float ComputeInteractorAngle()
        {
            Vector3 axisWorld = transform.TransformDirection(LocalAxis).normalized;
            Vector3 attachPos = _activeInteractor.GetAttachTransform(this).position;
            Vector3 toI = attachPos - transform.position;
            toI = Vector3.ProjectOnPlane(toI, axisWorld);

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
