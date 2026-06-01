using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 단서 공.
    ///
    /// 흐름:
    ///   Idle (위험 바닥 어딘가에 멈춰 있음)
    ///     → 사용자가 잡으면 Held
    ///     → 놓으면 Flying (Rigidbody 물리)
    ///     → 충돌 후 잠시 후 다시 Idle (높은 drag 로 굴러가지 않게 즉시 정지)
    ///     → 자석 캐처 트리거 진입 시 Captured (P1 쪽 dock 위치로 흡인)
    ///     → 슬롯에 안착하면 Slotted (kinematic, 부모 변경)
    ///
    /// 위험 바닥에 떨어져도 사라지지 않는다 — 다시 주워서 던질 수 있음.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class HintBall : MonoBehaviour
    {
        public enum State { Idle, Held, Flying, Captured, Slotted }

        [Header("Identity")]
        [Tooltip("시각 구분용 ID. 슬롯 색 매칭이나 디버그에 사용.")]
        public int ColorId;
        public Color BaseColor = Color.white;

        [Header("Refs (Setup 가 빌드 시 채움)")]
        public Renderer VisualRenderer;

        [Header("Idle settle")]
        [Tooltip("Flying 중 속도가 이 값 이하로 떨어지고 일정 시간 유지되면 Idle 로 전환.")]
        public float SettleSpeedThreshold = 0.1f;

        [Tooltip("속도가 임계치 아래로 유지돼야 하는 시간(s). 짧으면 빨리 안정화.")]
        public float SettleHoldTime = 0.25f;

        [Header("Magnet behavior")]
        [Tooltip("Captured 상태일 때 dock 위치로 부드럽게 끌리는 속도(m/s).")]
        public float DockApproachSpeed = 3.5f;

        [Tooltip("Dock 위치로부터 이 거리 이내면 snap.")]
        public float DockSnapDistance = 0.05f;

        public State CurrentState { get; private set; } = State.Idle;
        public bool IsAvailableForCapture =>
            CurrentState == State.Idle || CurrentState == State.Flying;

        /// <summary>프록시(비권위) 피어에서 true — 로컬 상태전이/정착 로직을 멈추고 네트워크 수신값만 따른다.
        /// HintBallNetworkSync 가 설정. 비네트워크면 항상 false.</summary>
        [System.NonSerialized] public bool NetworkProxy;

        XRGrabInteractable _grab;
        Rigidbody _rb;
        Transform _dockTarget;
        HintCatcher _activeCatcher;
        float _settleTimer;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
            if (_grab != null)
            {
                _grab.selectEntered.AddListener(OnGrabbed);
                _grab.selectExited.AddListener(OnReleased);
            }
        }

        void OnDestroy()
        {
            if (_grab != null)
            {
                _grab.selectEntered.RemoveListener(OnGrabbed);
                _grab.selectExited.RemoveListener(OnReleased);
            }
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            // Captured 상태에서 사용자가 다시 집어가면 캡처 해제.
            if (_activeCatcher != null)
            {
                _activeCatcher.OnBallRemoved(this);
                _activeCatcher = null;
            }
            _dockTarget = null;
            CurrentState = State.Held;
        }

        void OnReleased(SelectExitEventArgs args)
        {
            // XRGrabInteractable 이 throwOnDetach 로 속도 부여, 중력 ON 으로 비행 시작.
            CurrentState = State.Flying;
            _settleTimer = 0f;
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
            }
        }

        void Update()
        {
            if (NetworkProxy) return; // 프록시: 상태는 네트워크가, 위치는 NetworkTransform 이 구동.
            switch (CurrentState)
            {
                case State.Flying:
                    UpdateFlying();
                    break;
                case State.Captured:
                    UpdateCaptured();
                    break;
            }
        }

        /// <summary>프록시 피어에서 네트워크로 받은 상태를 반영(전이 로직은 돌리지 않음).
        /// Slotted 는 슬롯 인덱스가 필요하므로 HintBallNetworkSync 가 슬롯 경유로 처리한다.</summary>
        public void SetStateExternal(State s)
        {
            CurrentState = s;
            if (_rb != null)
            {
                bool dynamic = (s == State.Idle || s == State.Flying);
                _rb.isKinematic = !dynamic;
                _rb.useGravity = false; // 프록시는 NetworkTransform 이 위치를 구동.
            }
        }

        void UpdateFlying()
        {
            if (_rb == null) return;
            // 속도가 임계 이하로 일정 시간 유지되면 Idle 로 전환 — 위험 바닥 위에서 즉시 정지.
            if (_rb.linearVelocity.sqrMagnitude < SettleSpeedThreshold * SettleSpeedThreshold &&
                _rb.angularVelocity.sqrMagnitude < SettleSpeedThreshold * SettleSpeedThreshold * 4f)
            {
                _settleTimer += Time.deltaTime;
                if (_settleTimer >= SettleHoldTime)
                {
                    CurrentState = State.Idle;
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
            }
            else
            {
                _settleTimer = 0f;
            }
        }

        void UpdateCaptured()
        {
            if (_dockTarget == null) return;
            transform.position = Vector3.MoveTowards(
                transform.position,
                _dockTarget.position,
                DockApproachSpeed * Time.deltaTime);
            if ((transform.position - _dockTarget.position).sqrMagnitude
                < DockSnapDistance * DockSnapDistance)
            {
                transform.position = _dockTarget.position;
                if (_rb != null)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                // 보드/슬롯에 도착 통지 — Catcher 가 slot lock 을 처리.
                if (_activeCatcher != null)
                {
                    _activeCatcher.OnBallArrivedAtDock(this);
                }
            }
        }

        public void BeginCapture(HintCatcher catcher, Transform dockTarget)
        {
            _activeCatcher = catcher;
            _dockTarget = dockTarget;
            CurrentState = State.Captured;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.useGravity = false;
                _rb.isKinematic = true; // Update 에서 transform 직접 보간하므로 kinematic.
            }
        }

        public void EndCapture()
        {
            if (CurrentState == State.Slotted) return; // 이미 안착했으면 무시.
            _activeCatcher = null;
            _dockTarget = null;
            if (CurrentState == State.Captured)
            {
                CurrentState = State.Idle;
                if (_rb != null)
                {
                    _rb.useGravity = true;
                    _rb.isKinematic = false;
                }
            }
        }

        public void Slot(Transform anchor)
        {
            CurrentState = State.Slotted;
            transform.SetParent(anchor, true);
            transform.position = anchor.position;
            transform.rotation = anchor.rotation;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
            // Slotted 후엔 다시 잡히지 않게 — XRGrabInteractable 비활성.
            if (_grab != null) _grab.enabled = false;
        }

        public void ApplyVisualColor(Color color)
        {
            BaseColor = color;
            if (VisualRenderer == null) return;
            var mat = VisualRenderer.material; // instance.
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", color * 0.4f);
                mat.EnableKeyword("_EMISSION");
            }
        }
    }
}
