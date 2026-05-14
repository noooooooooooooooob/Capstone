using UnityEngine;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 도마뱀의 도주 루트 상에 놓인 트리거 영역.
    /// 셸 모드의 게가 이 트리거 안에 머무는 동안 Blocked = true 가 되어,
    /// LizardCreature 의 도주 속도가 slowMultiplier 로 감쇠한다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class LizardEscapeHole : MonoBehaviour
    {
        CrabCreature _blocker;

        public bool Blocked => _blocker != null && _blocker.InShell;

        void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        void OnTriggerStay(Collider other)
        {
            var crab = other.GetComponentInParent<CrabCreature>();
            if (crab != null) _blocker = crab;
        }

        void OnTriggerExit(Collider other)
        {
            var crab = other.GetComponentInParent<CrabCreature>();
            if (crab != null && crab == _blocker) _blocker = null;
        }
    }
}
