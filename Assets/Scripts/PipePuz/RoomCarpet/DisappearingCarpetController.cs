using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// RoomCarpet 퍼즐의 메인 컨트롤러. Dispenser 와 Goal 을 묶고 OnSolved 이벤트를 발행한다.
    /// 별도의 매 프레임 로직은 없다 — 각 컴포넌트가 알아서 작동하므로 thin wrapper.
    /// </summary>
    public class DisappearingCarpetController : MonoBehaviour
    {
        [Header("Refs")]
        public CarpetDispenser Dispenser;
        public CarpetGoalZone Goal;

        [Header("Events")]
        public UnityEvent OnSolved;

        public bool IsSolved { get; private set; }

        void Start()
        {
            if (Goal != null) Goal.OnReached.AddListener(HandleSolved);
        }

        void OnDestroy()
        {
            if (Goal != null) Goal.OnReached.RemoveListener(HandleSolved);
        }

        void HandleSolved()
        {
            if (IsSolved) return;
            IsSolved = true;
            OnSolved?.Invoke();
            Debug.Log("[RoomCarpet] Solved!");
        }
    }
}
