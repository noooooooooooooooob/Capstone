using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 단서 공 슬롯들의 컨테이너. 빈 슬롯을 차례대로 캐처에 제공하고,
    /// 모든 슬롯이 채워지면 <see cref="OnSolved"/> 발행.
    ///
    /// 슬롯 채움 순서는 <see cref="Slots"/> 배열 순서 그대로.
    /// 공의 색이나 ID 매칭이 필요해지면 ReserveNextEmptySlot 을 색별 검색으로 확장.
    /// </summary>
    public class HintPuzzleBoard : MonoBehaviour
    {
        [Tooltip("이 보드가 관리하는 슬롯들. 캐처는 앞에서부터 빈 자리를 채움.")]
        public List<HintSlot> Slots = new List<HintSlot>();

        [Header("Events")]
        public UnityEvent OnSlotFilled;
        public UnityEvent OnSolved;

        public bool IsSolved { get; private set; }

        public int FilledCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Slots[i] != null && Slots[i].State == HintSlot.SlotState.Filled) n++;
                }
                return n;
            }
        }

        public int TotalCount => Slots.Count;

        public HintSlot ReserveNextEmptySlot()
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                if (s == null) continue;
                if (s.IsAvailable)
                {
                    s.Reserve();
                    return s;
                }
            }
            return null;
        }

        public void NotifySlotFilled(HintSlot slot)
        {
            OnSlotFilled?.Invoke();
            Debug.Log($"[RoomCarpet] Hint slot filled ({FilledCount}/{TotalCount}).");
            if (!IsSolved && FilledCount >= TotalCount && TotalCount > 0)
            {
                IsSolved = true;
                OnSolved?.Invoke();
                Debug.Log("[RoomCarpet] HintPuzzleBoard solved!");
            }
        }
    }
}
