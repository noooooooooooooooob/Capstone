using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 빛 구체를 받는 dock. Trigger 콜라이더(보통 SphereCollider isTrigger=true) 안에
    /// 잡혀있지 않은 <see cref="LightOrb"/> 가 들어오면 자동 스냅 → <see cref="OnOrbInserted"/> 발동.
    /// orb 가 다시 잡히면 socket 에서 빠지고 <see cref="OnOrbRemoved"/> 발동.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class LightOrbSocket : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("orb 가 정확히 스냅될 위치/회전. 비워두면 socket 자기 transform.")]
        public Transform DockPoint;

        [Header("Events")]
        public UnityEvent OnOrbInserted;
        public UnityEvent OnOrbRemoved;

        public LightOrb InsertedOrb { get; private set; }
        public bool HasOrb => InsertedOrb != null;

        void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other) { TryAccept(other); }
        void OnTriggerStay(Collider other)  { TryAccept(other); }

        void TryAccept(Collider other)
        {
            if (InsertedOrb != null) return;
            var orb = other.GetComponent<LightOrb>() ?? other.GetComponentInParent<LightOrb>();
            if (orb == null) return;
            if (orb.IsHeld) return;                 // 잡혀 있으면 받지 않음
            if (orb.HostSocket != null) return;     // 이미 다른 socket 에 있음

            // 스냅 — orb 의 위치/물리 상태는 LightOrb.AttachToSocket 이 처리.
            InsertedOrb = orb;
            orb.AttachToSocket(this, DockPoint != null ? DockPoint : transform);
            OnOrbInserted?.Invoke();
            Debug.Log($"[LightOrbSocket] '{name}' 가 orb '{orb.name}' 를 받음.");
        }

        /// <summary>LightOrb 가 잡힐 때 자기 자신을 호스트하던 socket 에 통지.</summary>
        public void NotifyOrbGrabbed(LightOrb orb)
        {
            if (InsertedOrb != orb) return;
            InsertedOrb = null;
            OnOrbRemoved?.Invoke();
            Debug.Log($"[LightOrbSocket] '{name}' 에서 orb 가 빠짐.");
        }
    }
}
