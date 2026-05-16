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

        [Header("Read-only state")]
        [SerializeField] State _state = State.Spawned;
        public State CurrentState => _state;

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
            _state = State.Held;
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
            // throwOnDetach 가 XR Grab 에서 자동 velocity 적용. 명시적으로 중력/물리 활성.
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
            }
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
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
                _rb.linearVelocity = worldVelocity;
                _rb.angularVelocity = worldAngularVelocity;
            }
        }

        void FixedUpdate()
        {
            // Flying 상태 동안 살짝 양력 — 효과적으로 중력을 줄여 사거리 확보.
            if (_state != State.Flying || _rb == null) return;
            if (LiftAcceleration > 0f)
            {
                _rb.AddForce(Vector3.up * LiftAcceleration, ForceMode.Acceleration);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (_state != State.Flying) return;
            var floor = collision.gameObject.GetComponent<CarpetFloor>()
                     ?? collision.gameObject.GetComponentInParent<CarpetFloor>();
            if (floor == null) return;
            var contact = collision.contacts[0];
            AnchorTo(contact.point, contact.normal);
        }

        void AnchorTo(Vector3 point, Vector3 normal)
        {
            _state = State.Anchored;
            _anchoredTime = 0f;

            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }

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
                _flyingTime += Time.deltaTime;
                if (_flyingTime > FlyingTimeout)
                {
                    Destroy(gameObject);
                }
                return;
            }

            if (_state != State.Anchored) return;
            _anchoredTime += Time.deltaTime;
            float remaining = Lifetime - _anchoredTime;
            if (remaining <= 0f)
            {
                Destroy(gameObject);
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
