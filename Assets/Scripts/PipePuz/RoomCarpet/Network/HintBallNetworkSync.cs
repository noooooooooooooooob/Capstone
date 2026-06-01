using Fusion;
using UnityEngine;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 단서 공(HintBall)의 퍼즐 상태를 네트워크로 동기화하는 컴패니언 (Fusion Shared Mode).
    ///
    /// 잡기/던지기 위치는 같은 오브젝트의 NetworkGrabbableSync + NetworkTransform 이 담당한다.
    /// 이 컴포넌트는 그 위에 "자석 캡처 → 슬롯 안착" 같은 퍼즐 상태를 싣는다:
    ///   - <see cref="NetState"/> : HintBall.State (Idle/Held/Flying/Captured/Slotted)
    ///   - <see cref="NetSlot"/>  : Slotted 일 때 보드의 슬롯 인덱스(아니면 -1)
    ///
    /// 권위(공을 마지막으로 잡은/던진 피어)가 캡처 모션을 구동하면 NetworkTransform 으로 위치가 전파되고,
    /// 안착 결과(어느 슬롯에 꽂혔는지)는 NetSlot 으로 전파되어 모든 피어가 동일하게 슬롯을 채운다 →
    /// 보드의 OnSolved 가 양쪽에서 발행된다.
    ///
    /// 요구: 같은 GameObject 에 NetworkObject(AllowStateAuthorityOverride ON) + HintBall +
    ///       NetworkTransform + NetworkGrabbableSync. 씬에 HintPuzzleBoard 1개.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(HintBall))]
    [DisallowMultipleComponent]
    public class HintBallNetworkSync : NetworkBehaviour, IStateAuthorityChanged
    {
        [Networked, OnChangedRender(nameof(OnReplicated))]
        public int NetState { get; set; }   // (int)HintBall.State

        [Networked, OnChangedRender(nameof(OnReplicated))]
        public int NetSlot { get; set; }    // Slotted 슬롯 인덱스, 아니면 -1

        HintBall _ball;
        HintPuzzleBoard _board;

        void Awake()
        {
            _ball = GetComponent<HintBall>();
        }

        public override void Spawned()
        {
            _board = FindFirstObjectByType<HintPuzzleBoard>();
            ApplyGate();
            if (HasStateAuthority)
            {
                NetState = (int)_ball.CurrentState;
                NetSlot = -1;
            }
            else
            {
                ApplyProxy();
            }
        }

        public void StateAuthorityChanged() => ApplyGate();

        void ApplyGate()
        {
            bool authority = Object != null && Object.IsValid && HasStateAuthority;
            _ball.NetworkProxy = !authority;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            int s = (int)_ball.CurrentState;
            if (s != NetState) NetState = s;

            // 안착했으면 어느 슬롯인지 보드에서 역산해 싣는다.
            if (_ball.CurrentState == HintBall.State.Slotted && NetSlot < 0 && _board != null)
            {
                int idx = -1;
                for (int i = 0; i < _board.Slots.Count; i++)
                {
                    var sl = _board.Slots[i];
                    if (sl != null && sl.ContainedBall == _ball) { idx = i; break; }
                }
                if (idx >= 0) NetSlot = idx;
            }
        }

        void OnReplicated()
        {
            if (HasStateAuthority) return;
            ApplyProxy();
        }

        void ApplyProxy()
        {
            var s = (HintBall.State)NetState;

            if (s == HintBall.State.Slotted)
            {
                if (NetSlot < 0) return; // 슬롯 인덱스 도착을 기다린다(다음 OnReplicated 에서 처리).
                if (_board == null || NetSlot >= _board.Slots.Count) return;
                if (_ball.CurrentState == HintBall.State.Slotted) return; // 이미 처리됨.

                var slot = _board.Slots[NetSlot];
                if (slot == null) return;
                slot.AcceptBall(_ball);          // 슬롯 Filled + ball.Slot(dock) + grab 비활성.
                _board.NotifySlotFilled(slot);   // 이 피어의 카운트 갱신 + 완료 시 OnSolved 발행.
                return;
            }

            _ball.SetStateExternal(s);
        }
    }
}
