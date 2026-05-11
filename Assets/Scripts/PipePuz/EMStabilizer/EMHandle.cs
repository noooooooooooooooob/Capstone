using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz.EMStabilizer
{
    /// <summary>
    /// EM Stabilizer 의 물리 핸들. 안테나 PivotYaw 의 Y(수직) 축 회전을 사용자의 손으로 제어한다.
    ///
    /// 동작 요약:
    /// - <see cref="GripInteractable"/> (XRBaseInteractable) 의 selectEntered/Exited 로 잡기/놓기 감지.
    /// - 잡혀있는 동안: 손 위치를 PivotYaw 의 수평면에 투영해 각도 변화량(delta)을 누적, 현재 각도를 갱신한다.
    /// - 잡혀있어도 <see cref="HeldDriftDegPerSec"/> 만큼 천천히 중립(0°)으로 끌려간다 — 사용자가 계속 미세 조정해야 함.
    /// - 놓으면 <see cref="DriftSpeedDegPerSec"/> 만큼 빠르게 중립으로 복귀.
    /// - 각도는 [<see cref="MinAngle"/>, <see cref="MaxAngle"/>] 로 클램프.
    ///
    /// 핸들은 PivotYaw 의 자식이므로, PivotYaw 회전에 따라 자연스럽게 함께 움직인다.
    /// </summary>
    public class EMHandle : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Y 축 회전을 적용할 Transform. 보통 Antenna 의 'PivotYaw' 자식.")]
        public Transform PivotYaw;

        [Tooltip("실제로 잡기 가능한 그립 (자체 콜라이더를 가지는 XRBaseInteractable). " +
                 "보통 PivotYaw 의 자식 중 HandleGrip.")]
        public XRBaseInteractable GripInteractable;

        [Header("Range")]
        [Tooltip("최소 각도(°). 음수면 좌측 끝.")]
        public float MinAngle = -75f;

        [Tooltip("최대 각도(°). 양수면 우측 끝.")]
        public float MaxAngle = +75f;

        [Header("Drift")]
        [Tooltip("잡혀있는 동안에도 중립으로 끌어당기는 각속도(°/s). 사용자가 계속 미세 조정하게 만드는 부담.")]
        public float HeldDriftDegPerSec = 8f;

        [Tooltip("놓았을 때 중립으로 복귀하는 각속도(°/s).")]
        public float DriftSpeedDegPerSec = 35f;

        [Header("Read-only state")]
        [SerializeField] float _currentAngle;
        /// <summary>현재 핸들 각도(°), 중립=0.</summary>
        public float CurrentAngle => _currentAngle;
        public bool IsHeld => _activeInteractor != null;

        /// <summary>각도가 갱신될 때마다 발행.</summary>
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
            ApplyVisual();
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
            float prevAngle = _currentAngle;

            if (IsHeld && PivotYaw != null)
            {
                float handAngle = ComputeHandAngle();
                if (_firstFrame)
                {
                    _lastHandAngle = handAngle;
                    _firstFrame = false;
                }
                float delta = Mathf.DeltaAngle(_lastHandAngle, handAngle);
                _lastHandAngle = handAngle;

                _currentAngle += delta;
                // 잡혀있을 때도 약한 중립 끌림.
                _currentAngle = Mathf.MoveTowards(_currentAngle, 0f, HeldDriftDegPerSec * dt);
            }
            else
            {
                // 놓으면 강한 drift-back.
                _currentAngle = Mathf.MoveTowards(_currentAngle, 0f, DriftSpeedDegPerSec * dt);
                _firstFrame = true;
            }

            _currentAngle = Mathf.Clamp(_currentAngle, MinAngle, MaxAngle);
            ApplyVisual();

            if (!Mathf.Approximately(prevAngle, _currentAngle))
            {
                AngleChanged?.Invoke(_currentAngle);
            }
        }

        void ApplyVisual()
        {
            if (PivotYaw == null) return;
            PivotYaw.localRotation = Quaternion.AngleAxis(_currentAngle, Vector3.up);
        }

        float ComputeHandAngle()
        {
            // PivotYaw 의 위쪽(=Y) 축을 회전축으로 사용. 손 위치를 그 평면에 투영.
            Vector3 axisWorld = PivotYaw.up.normalized;
            Vector3 attachPos = _activeInteractor.GetAttachTransform(GripInteractable).position;
            Vector3 toHand = attachPos - PivotYaw.position;
            toHand = Vector3.ProjectOnPlane(toHand, axisWorld);

            // 기준 벡터: PivotYaw 의 forward 를 평면에 투영.
            Vector3 basisForward = Vector3.ProjectOnPlane(PivotYaw.forward, axisWorld);
            if (basisForward.sqrMagnitude < 1e-6f)
                basisForward = Vector3.ProjectOnPlane(PivotYaw.right, axisWorld);
            basisForward.Normalize();
            Vector3 basisRight = Vector3.Cross(axisWorld, basisForward);

            float fwd = Vector3.Dot(toHand, basisForward);
            float rgt = Vector3.Dot(toHand, basisRight);
            // atan2(right, forward): right(+X)→+, forward(+Z)→0. 즉 시계방향(우측)이 양수.
            return Mathf.Atan2(rgt, fwd) * Mathf.Rad2Deg;
        }
    }
}
