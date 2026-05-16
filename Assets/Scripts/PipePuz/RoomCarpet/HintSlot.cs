using UnityEngine;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 단서 공이 안착하는 자리.
    ///
    /// 상태:
    ///   Empty → Reserved (캐처가 예약함, 비행 중) → Filled
    /// Reserved 중 사용자가 공을 다시 잡아가면 <see cref="Release"/> 로 Empty 복귀.
    /// </summary>
    public class HintSlot : MonoBehaviour
    {
        public enum SlotState { Empty, Reserved, Filled }

        [Tooltip("공이 도착할 정확한 위치/회전. 비워두면 슬롯 자기 transform.")]
        public Transform DockPoint;

        public SlotState State { get; private set; } = SlotState.Empty;
        public HintBall ContainedBall { get; private set; }

        public bool IsAvailable => State == SlotState.Empty;

        public void Reserve()
        {
            if (State == SlotState.Empty) State = SlotState.Reserved;
        }

        public void Release()
        {
            if (State == SlotState.Reserved) State = SlotState.Empty;
            ContainedBall = null;
        }

        public void AcceptBall(HintBall ball)
        {
            ContainedBall = ball;
            State = SlotState.Filled;
            ball.Slot(DockPoint != null ? DockPoint : transform);
        }
    }
}
