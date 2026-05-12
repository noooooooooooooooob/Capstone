using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

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
    /// TeleportationArea 는 Anchored 가 되어야 활성화되어 B 가 텔레포트할 수 있다.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class DisappearingCarpet : MonoBehaviour
    {
        public enum State { Spawned, Held, Flying, Anchored }

        [Header("Refs (Dispenser 가 빌드 시 채움)")]
        public Renderer VisualRenderer;
        public BaseTeleportationInteractable TeleportArea;
        public CarpetDispenser Dispenser;

        [Header("Timing")]
        [Tooltip("Anchored 가 된 뒤 사라지기까지의 총 시간(s).")]
        public float Lifetime = 5f;

        [Tooltip("Lifetime 마지막 N 초 동안 알파 깜빡임 경고.")]
        public float WarningSeconds = 1.5f;

        [Tooltip("Flying 상태로 이 시간을 넘기면 (CarpetFloor 에 안착 못 함) 자동 Destroy. " +
                 "사용자가 빈 곳에 던졌을 때 영구 떠도는 카펫 방지.")]
        public float FlyingTimeout = 8f;

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
            if (TeleportArea != null) TeleportArea.enabled = false;
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

            if (TeleportArea != null) TeleportArea.enabled = true;
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
