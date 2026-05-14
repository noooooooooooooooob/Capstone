using UnityEngine;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 잠자리채. 본체는 별도의 XRGrabInteractable 로 잡을 수 있다(셋업에서 자동 부착).
    /// 헤드(자식 트리거 콜라이더)에 잠자리(Dragonfly)가 들어오면 자동 캡처한다.
    /// </summary>
    public class CatchNet : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("그물 헤드 트랜스폼(자식). 잠자리가 부착될 위치이자 트리거의 부모.")]
        [SerializeField] Transform netHead;

        [Tooltip("헤드 트리거 콜라이더(보통 SphereCollider isTrigger). " +
                 "비워두면 netHead 의 첫 번째 Collider 를 자동으로 사용.")]
        [SerializeField] Collider headTrigger;

        DragonflyCreature _held;

        public Transform Head => netHead;
        public bool IsHoldingDragonfly => _held != null;

        void Awake()
        {
            if (netHead == null) netHead = transform;
            if (headTrigger == null) headTrigger = netHead.GetComponent<Collider>();
        }

        void OnEnable()
        {
            if (headTrigger != null && !headTrigger.isTrigger) headTrigger.isTrigger = true;
            var relay = headTrigger != null ? headTrigger.GetComponent<NetHeadTriggerRelay>() : null;
            if (headTrigger != null && relay == null)
                relay = headTrigger.gameObject.AddComponent<NetHeadTriggerRelay>();
            if (relay != null) relay.Net = this;
        }

        /// <summary>NetHeadTriggerRelay 에서 호출 — 헤드 트리거에 무언가 들어옴.</summary>
        internal void OnHeadEnter(Collider other)
        {
            if (_held != null) return;
            var dragon = other.GetComponentInParent<DragonflyCreature>();
            if (dragon == null) return;
            // 권위 측에서 캡처 적용 — TryCapture 내부가 권한 요청과 상태 전이를 처리.
            dragon.TryCapture(netHead);
            _held = dragon;
        }

        /// <summary>외부 호출(예: 컨트롤러 버튼) — 잡고 있던 잠자리를 떨어뜨린다.</summary>
        public void ReleaseHeld()
        {
            if (_held == null) return;
            _held.Release();
            _held = null;
        }
    }

    /// <summary>그물 헤드 콜라이더에 부착되어 OnTriggerEnter 를 CatchNet 로 릴레이.</summary>
    public class NetHeadTriggerRelay : MonoBehaviour
    {
        public CatchNet Net;
        void OnTriggerEnter(Collider other)
        {
            if (Net != null) Net.OnHeadEnter(other);
        }
    }
}
