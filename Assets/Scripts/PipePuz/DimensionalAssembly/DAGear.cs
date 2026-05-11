using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz.DimensionalAssembly
{
    /// <summary>
    /// 차원 분할 조립 퍼즐의 톱니바퀴.
    /// EMHandle 과 거의 같지만, 회전축 basis 를 PivotYaw 의 부모 frame 에서 계산해
    /// drift 누적이 정상 작동하도록 했다 (PivotYaw 자신의 회전이 변해도 basis 는 고정).
    ///
    /// 사용자가 톱니바퀴의 grip 콜라이더를 잡고 손을 휘두르면 PivotYaw 의 local Y 축 회전이 바뀐다.
    /// 놓으면 강한 drift, 잡혀있어도 약한 drift 로 중립(0°)으로 끌린다.
    /// </summary>
    public class DAGear : MonoBehaviour
    {
        [Header("Refs")]
        public Transform PivotYaw;
        public XRBaseInteractable GripInteractable;

        [Header("Range")]
        public float MinAngle = -180f;
        public float MaxAngle = +180f;

        [Header("Drift")]
        [Tooltip("잡혀있는 동안 중립으로 약하게 끌려가는 각속도(°/s). 너무 크면 1차 단일 플레이가 어려움.")]
        public float HeldDriftDegPerSec = 5f;

        [Tooltip("놓았을 때 중립으로 빠르게 복귀하는 각속도(°/s).")]
        public float ReleasedDriftDegPerSec = 35f;

        public float CurrentAngle { get; private set; }
        public bool IsHeld => _activeInteractor != null;
        public event Action<float> AngleChanged;

        IXRSelectInteractor _activeInteractor;
        float _lastHandAngle;
        bool _firstFrame;

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
            _firstFrame = true;
        }

        void OnReleased(SelectExitEventArgs args)
        {
            _activeInteractor = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float prev = CurrentAngle;

            if (IsHeld && PivotYaw != null)
            {
                float handAngle = ComputeHandAngle();
                if (_firstFrame) { _lastHandAngle = handAngle; _firstFrame = false; }
                float delta = Mathf.DeltaAngle(_lastHandAngle, handAngle);
                _lastHandAngle = handAngle;
                CurrentAngle += delta;
                CurrentAngle = Mathf.MoveTowards(CurrentAngle, 0f, HeldDriftDegPerSec * dt);
            }
            else
            {
                CurrentAngle = Mathf.MoveTowards(CurrentAngle, 0f, ReleasedDriftDegPerSec * dt);
                _firstFrame = true;
            }

            CurrentAngle = Mathf.Clamp(CurrentAngle, MinAngle, MaxAngle);
            if (PivotYaw != null)
                PivotYaw.localRotation = Quaternion.AngleAxis(CurrentAngle, Vector3.up);

            if (!Mathf.Approximately(prev, CurrentAngle))
                AngleChanged?.Invoke(CurrentAngle);
        }

        float ComputeHandAngle()
        {
            // basis 를 PivotYaw 의 부모 frame 에서 계산 — PivotYaw 가 회전해도 basis 는 고정.
            // (PivotYaw 자체의 forward/up 을 쓰면 drift 가 basis 변화로 상쇄돼 절반밖에 안 먹힘.)
            Transform basisRoot = PivotYaw.parent != null ? PivotYaw.parent : PivotYaw;
            Vector3 axisWorld = basisRoot.up.normalized;
            Vector3 attachPos = _activeInteractor.GetAttachTransform(GripInteractable).position;
            Vector3 toHand = attachPos - PivotYaw.position;
            toHand = Vector3.ProjectOnPlane(toHand, axisWorld);

            Vector3 basisForward = Vector3.ProjectOnPlane(basisRoot.forward, axisWorld);
            if (basisForward.sqrMagnitude < 1e-6f)
                basisForward = Vector3.ProjectOnPlane(basisRoot.right, axisWorld);
            basisForward.Normalize();
            Vector3 basisRight = Vector3.Cross(axisWorld, basisForward);

            float fwd = Vector3.Dot(toHand, basisForward);
            float rgt = Vector3.Dot(toHand, basisRight);
            return Mathf.Atan2(rgt, fwd) * Mathf.Rad2Deg;
        }
    }
}
