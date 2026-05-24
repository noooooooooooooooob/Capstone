using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 빛 구체 — 플레이어가 잡아 옮길 수 있는 빛나는 sphere.
    /// <see cref="LightOrbSocket"/> 에 떨어뜨리면 자동 스냅되어 socket 의 이벤트를 발동.
    /// 잡혀 있지 않은 상태에서 너무 아래로 떨어지면 초기 위치로 리스폰.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class LightOrb : MonoBehaviour
    {
        [Header("Visual")]
        public Renderer GlowRenderer;

        [Header("Fall safety")]
        [Tooltip("초기 위치 (월드). 비워두면 Awake 시점 transform.position 사용.")]
        public Vector3 RespawnPosition;
        public bool HasExplicitRespawn = false;

        [Tooltip("orb 가 이 Y 아래로 떨어지면 RespawnPosition 으로 즉시 복귀.")]
        public float FallThresholdY = -3f;

        public bool IsHeld { get; private set; }
        public LightOrbSocket HostSocket { get; private set; }

        XRGrabInteractable _grab;
        Rigidbody _rb;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
            if (!HasExplicitRespawn) RespawnPosition = transform.position;

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
            IsHeld = true;
            // 잡힌 순간 socket 에서 떼어냄 — socket 에 통지해서 이벤트도 발동.
            if (HostSocket != null)
            {
                HostSocket.NotifyOrbGrabbed(this);
                HostSocket = null;
            }
        }

        void OnReleased(SelectExitEventArgs args)
        {
            IsHeld = false;
            // XRGrab 이 Rigidbody 의 isKinematic 을 원래 값으로 복원하지만, 우리는 자유 낙하시키고 싶음.
            // 다음 프레임에 force-off 해서 중력 적용되도록.
            StartCoroutine(ForceFallingNextFrame());
        }

        IEnumerator ForceFallingNextFrame()
        {
            yield return null;
            if (_rb == null) yield break;
            if (IsHeld) yield break;          // 이미 다시 잡혔으면 무시
            if (HostSocket != null) yield break; // socket 에 스냅됐으면 그대로
            _rb.isKinematic = false;
            _rb.useGravity = true;
        }

        void Update()
        {
            // 떨어졌으면 리스폰 (socket 에 박혀있지 않고 안 잡혀 있을 때만).
            if (IsHeld || HostSocket != null) return;
            if (transform.position.y < FallThresholdY)
            {
                transform.position = RespawnPosition;
                if (_rb != null)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
            }
        }

        /// <summary>Socket 이 orb 를 받았을 때 호출. orb 의 위치/물리를 socket 에 맞춤.</summary>
        public void AttachToSocket(LightOrbSocket socket, Transform dockPoint)
        {
            HostSocket = socket;
            if (dockPoint != null)
            {
                transform.position = dockPoint.position;
                transform.rotation = dockPoint.rotation;
            }
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
        }
    }
}
