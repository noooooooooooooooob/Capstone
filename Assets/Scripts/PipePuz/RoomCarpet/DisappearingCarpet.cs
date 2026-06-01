using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 카펫 한 장의 라이프사이클을 관리한다.
    ///
    /// 상태 머신:
    ///   Spawned → (잡힘) → Held → (놓아짐) → Flying → (CarpetFloor 충돌) → Anchored
    ///   Anchored 후 <see cref="Lifetime"/> 초 동안 유효, 마지막 <see cref="WarningSeconds"/> 동안 알파 깜빡,
    ///   그 후 Destroy.
    ///
    /// 카펫은 텔레포트 대상이 아닌 물리적 발판으로 사용된다 —
    /// <see cref="DisappearingCarpetController"/> 가 사용자의 카메라 위치를 카펫 BoxCollider 범위와 비교해
    /// "걸쳐있음" 을 판정한다.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class DisappearingCarpet : MonoBehaviour
    {
        public enum State { Spawned, Held, Flying, Anchored }

        [Header("Refs (Dispenser 가 빌드 시 채움)")]
        public Renderer VisualRenderer;
        public CarpetDispenser Dispenser;

        [Header("Timing")]
        [Tooltip("Anchored 가 된 뒤 사라지기까지의 총 시간(s).")]
        public float Lifetime = 5f;

        [Tooltip("Lifetime 마지막 N 초 동안 알파 깜빡임 경고.")]
        public float WarningSeconds = 1.5f;

        [Tooltip("Flying 상태로 이 시간을 넘기면 (CarpetFloor 에 안착 못 함) 자동 Destroy. " +
                 "사용자가 빈 곳에 던졌을 때 영구 떠도는 카펫 방지.")]
        public float FlyingTimeout = 8f;

        [Header("Flight")]
        [Tooltip("Flying 상태일 때 카펫에 적용할 상향 가속도(m/s^2). " +
                 "9.81 보다 작은 양수면 실효 중력이 줄어 살짝 양력을 받음 — 던지기 사거리·체공시간이 늘어남.")]
        public float LiftAcceleration = 5.5f;

        [Header("Floating mode (Cliff variant)")]
        [Tooltip("켜면 CarpetFloor 충돌 대신 카펫이 일정 Y 에 도달했을 때 그 자리에 anchor. " +
                 "RoomCliff 같은 절벽 모드에서 카펫이 임시 발판으로 공중에 떠있게 하는 데 사용.")]
        public bool UseFloatingMode = false;

        [Tooltip("UseFloatingMode 가 true 일 때 카펫이 멈출 월드 Y 위치(m). 발판 윗면과 같거나 살짝 위.")]
        public float FloatingY = 0.05f;

        [Header("Read-only state")]
        [SerializeField] State _state = State.Spawned;
        public State CurrentState => _state;

        // ── 네트워크 연동 (CarpetNetworkSync 가 채움; 비네트워크 씬에서는 모두 무시됨) ──────────────
        /// <summary>모든 활성 카펫의 전역 레지스트리. 컨트롤러의 안전 검사가 부모 계층 대신 이걸 순회한다
        /// (네트워크 카펫은 NetworkGrabbableSync 가 부모를 떼어 ActiveCarpetsRoot 자식이 아니게 되기 때문).</summary>
        public static readonly List<DisappearingCarpet> Active = new List<DisappearingCarpet>();

        /// <summary>프록시(비권위) 피어에서 true — 로컬 물리/상태전이/자동삭제를 멈추고 NetworkTransform 수신만 따른다.</summary>
        [System.NonSerialized] public bool SuspendSimulation = false;

        /// <summary>네트워크 제거 핸들러. 반환값 true 면 로컬 Destroy 를 하지 않는다(권위가 Runner.Despawn 으로 전 피어 제거).
        /// CarpetNetworkSync 가 설정. 비네트워크면 null → 평소대로 Destroy.</summary>
        [System.NonSerialized] public System.Func<bool> NetworkRemovalHandler;

        XRGrabInteractable _grab;
        Rigidbody _rb;
        Material _matInstance;
        Color _baseColor;
        float _anchoredTime;
        float _flyingTime;
        bool _notifiedDispenser;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
            if (_grab != null)
            {
                _grab.selectEntered.AddListener(OnGrabbed);
                _grab.selectExited.AddListener(OnReleased);
            }
            if (VisualRenderer != null)
            {
                _matInstance = VisualRenderer.material; // 자동 instance
                _baseColor = ReadColor(_matInstance);
            }
            RefreshPhysics(); // 초기 대기 상태 물리 적용(비네트워크 씬 포함).
        }

        void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        void OnDestroy()
        {
            Active.Remove(this);
            if (_grab != null)
            {
                _grab.selectEntered.RemoveListener(OnGrabbed);
                _grab.selectExited.RemoveListener(OnReleased);
            }
        }

        /// <summary>삭제 요청. 네트워크 핸들러가 처리하면(권위 Despawn) 로컬 Destroy 하지 않는다.</summary>
        void RequestRemoval()
        {
            if (NetworkRemovalHandler != null && NetworkRemovalHandler()) return;
            Destroy(gameObject);
        }

        /// <summary>프록시 피어에서 네트워크로 받은 상태를 시각/물리에 반영한다(상태 전이 로직은 돌리지 않음).</summary>
        public void ApplyNetworkState(State s)
        {
            if (_state == s) return;
            _state = s;
            RefreshPhysics(); // SuspendSimulation(프록시)면 kinematic, 위치는 NetworkTransform 이 구동.
            if (s == State.Anchored) _anchoredTime = 0f; // 깜빡임 타이머 로컬 시작(시각용).
        }

        /// <summary>
        /// 현재 상태와 프록시 여부에 맞춰 Rigidbody 의 kinematic/gravity 를 설정한다.
        ///   - 프록시(SuspendSimulation): 항상 kinematic — NetworkTransform 이 위치 구동.
        ///   - Spawned(대기): kinematic — 디스펜서 위에 그대로 떠 있음(드리프트 없음).
        ///   - Held: dynamic, 중력 OFF — 손을 따라 이동(VelocityTracking).
        ///   - Flying: dynamic, 중력 ON — 던져져 날아감.
        ///   - Anchored: kinematic — 바닥에 고정.
        /// </summary>
        public void RefreshPhysics()
        {
            if (_rb == null) return;
            if (SuspendSimulation)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
                return;
            }
            switch (_state)
            {
                case State.Spawned:  _rb.isKinematic = true;  _rb.useGravity = false; break;
                case State.Held:     _rb.isKinematic = false; _rb.useGravity = false; break;
                case State.Flying:   _rb.isKinematic = false; _rb.useGravity = true;  break;
                case State.Anchored: _rb.isKinematic = true;  _rb.useGravity = false; break;
            }
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            _state = State.Held;
            RefreshPhysics();
            if (!_notifiedDispenser && Dispenser != null)
            {
                _notifiedDispenser = true;
                Dispenser.OnCarpetTaken(this);
            }
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (_state != State.Held) return;
            _state = State.Flying;
            // throwOnDetach 가 XR Grab 에서 자동 velocity 적용. RefreshPhysics 가 dynamic + 중력 ON.
            RefreshPhysics();
        }

        /// <summary>
        /// 외부(런처)에서 카펫을 발사 모드로 진입시킬 때 사용.
        /// Held 단계를 건너뛰고 곧바로 Flying 상태가 되며 지정된 속도/각속도가 부여된다.
        /// 디스펜서가 아닌 런처가 만든 카펫이므로 Dispenser 통지는 일어나지 않는다.
        /// </summary>
        public void Launch(Vector3 worldVelocity, Vector3 worldAngularVelocity = default)
        {
            // 이미 비행/안착했다면 무시.
            if (_state == State.Flying || _state == State.Anchored) return;
            _state = State.Flying;
            _flyingTime = 0f;
            _notifiedDispenser = true; // 런처 발사는 디스펜서 연쇄 spawn 없음.
            RefreshPhysics(); // dynamic + 중력 ON.
            if (_rb != null)
            {
                _rb.linearVelocity = worldVelocity;
                _rb.angularVelocity = worldAngularVelocity;
            }
        }

        void FixedUpdate()
        {
            if (SuspendSimulation) return; // 프록시: 물리 비구동.
            if (_state != State.Flying || _rb == null) return;
            // Flying 상태 동안 살짝 양력.
            if (LiftAcceleration > 0f)
            {
                _rb.AddForce(Vector3.up * LiftAcceleration, ForceMode.Acceleration);
            }
            // Floating mode: y 가 FloatingY 이하로 내려가면서 하강 중이면 그 자리에 anchor.
            if (UseFloatingMode && transform.position.y <= FloatingY && _rb.linearVelocity.y < 0f)
            {
                AnchorFloating();
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (SuspendSimulation) return; // 프록시: 안착 판정은 권위만.
            if (_state != State.Flying) return;
            if (UseFloatingMode) return; // Floating 모드에선 CarpetFloor 충돌 대신 Y 기반 anchor.
            var floor = collision.gameObject.GetComponent<CarpetFloor>()
                     ?? collision.gameObject.GetComponentInParent<CarpetFloor>();
            if (floor == null) return;
            var contact = collision.contacts[0];
            AnchorTo(contact.point, contact.normal);
        }

        /// <summary>Floating mode anchor — 카펫을 y=FloatingY 에 평탄 고정.</summary>
        void AnchorFloating()
        {
            _state = State.Anchored;
            _anchoredTime = 0f;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            RefreshPhysics(); // kinematic 고정.
            Vector3 pos = transform.position;
            pos.y = FloatingY;
            transform.position = pos;
            // yaw 만 유지, pitch/roll 제거.
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        void AnchorTo(Vector3 point, Vector3 normal)
        {
            _state = State.Anchored;
            _anchoredTime = 0f;

            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            RefreshPhysics(); // kinematic 고정.

            // 카펫을 표면 normal 에 맞춰 눕히기. 살짝 위로 떠서 z-fight 방지.
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, normal);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, normal);
            transform.position = point + normal.normalized * 0.005f;
            transform.rotation = Quaternion.LookRotation(fwd.normalized, normal.normalized);
        }

        void Update()
        {
            // Flying 상태로 너무 오래 머물면 (CarpetFloor 에 안착 못 함) 폐기.
            if (_state == State.Flying)
            {
                if (SuspendSimulation) return; // 프록시: 폐기 판정은 권위만.
                _flyingTime += Time.deltaTime;
                if (_flyingTime > FlyingTimeout)
                {
                    RequestRemoval();
                }
                return;
            }

            if (_state != State.Anchored) return;
            _anchoredTime += Time.deltaTime;
            float remaining = Lifetime - _anchoredTime;
            if (remaining <= 0f)
            {
                if (!SuspendSimulation) RequestRemoval(); // 프록시는 권위의 Despawn 을 기다린다.
                return;
            }
            if (remaining < WarningSeconds && _matInstance != null)
            {
                // 1.5초 동안 알파 깜빡 (0.95 ↔ 0.3).
                float phase = Mathf.PingPong(_anchoredTime * 4f, 1f);
                float alpha = Mathf.Lerp(0.95f, 0.3f, phase);
                ApplyAlpha(alpha);
            }
        }

        static Color ReadColor(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            return m.color;
        }

        void ApplyAlpha(float a)
        {
            if (_matInstance == null) return;
            Color c = _baseColor;
            c.a = _baseColor.a * a;
            _matInstance.color = c;
            if (_matInstance.HasProperty("_BaseColor")) _matInstance.SetColor("_BaseColor", c);
        }
    }
}
