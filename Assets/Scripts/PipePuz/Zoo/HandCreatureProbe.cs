using UnityEngine;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 손 GameObject(보통 HandInsulation 가 붙은 트랜스폼)의 자식 트리거 콜라이더에 부착.
    /// 트리거 내부에 들어온 ZooCreature 종류에 따라 적절한 캡처/감전 처리를 한다.
    ///
    /// 잠자리(Dragonfly)와 게(Crab)는 손으로 직접 잡지 못한다(잠자리채/밀기 전용).
    /// 도마뱀(Lizard)은 항상 손에 잡힘.
    /// 뱀(Snake)은 HandInsulation.IsInsulated 일 때만 잡히고, 그렇지 않으면 감전 콜백.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HandCreatureProbe : MonoBehaviour
    {
        [Tooltip("기본은 부모에서 자동 검색. 인스펙터에서 명시도 가능.")]
        [SerializeField] HandInsulation hand;

        void Awake()
        {
            if (hand == null) hand = GetComponentInParent<HandInsulation>();
            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger) col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (hand == null) return;
            if (hand.IsShocked) return;

            var creature = other.GetComponentInParent<ZooCreature>();
            if (creature == null) return;

            switch (creature)
            {
                case LizardCreature lizard:
                    lizard.TryCapture(transform);
                    break;

                case SnakeCreature snake:
                    if (hand.IsInsulated) snake.TryCapture(transform);
                    else                  snake.OnElectrocute(transform);
                    break;

                // Dragonfly / Crab — 손으로는 처리 안 함. CatchNet, 임펄스로 해결.
            }
        }
    }
}
