using System.Collections.Generic;
using UnityEngine;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// P1 쪽 자석 캐처.
    ///
    /// P2 가 던진 단서 공이 트리거 영역에 진입하면, 보드의 다음 빈 슬롯을
    /// dock target 으로 지정해 <see cref="HintBall.BeginCapture"/> 호출.
    /// 공이 dock 에 도착하면 슬롯에 lock.
    ///
    /// 트리거는 BoxCollider/SphereCollider (isTrigger=true) 사용. Setup 이 SphereCollider 부착.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HintCatcher : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("이 캐처가 라우팅할 퍼즐 보드. 빈 슬롯을 차례대로 채움.")]
        public HintPuzzleBoard Board;

        [Header("Tuning")]
        [Tooltip("Captured 가 풀린 직후 같은 공이 재진입할 때 일정 시간 무시 — 무한 캡처 방지.")]
        public float ReentryGuard = 0.3f;

        readonly Dictionary<HintBall, float> _suppressUntil = new Dictionary<HintBall, float>();
        readonly Dictionary<HintBall, HintSlot> _claimedSlots = new Dictionary<HintBall, HintSlot>();

        void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            TryCapture(other);
        }

        void OnTriggerStay(Collider other)
        {
            // OnTriggerEnter 만으로 놓치는 경우 (천천히 굴러들어옴) 대비.
            TryCapture(other);
        }

        void TryCapture(Collider other)
        {
            if (Board == null) return;
            var ball = other.GetComponent<HintBall>() ?? other.GetComponentInParent<HintBall>();
            if (ball == null) return;
            if (!ball.IsAvailableForCapture) return;
            if (_suppressUntil.TryGetValue(ball, out float until) && Time.time < until) return;

            var slot = Board.ReserveNextEmptySlot();
            if (slot == null) return; // 모든 슬롯이 차 있음.

            _claimedSlots[ball] = slot;
            ball.BeginCapture(this, slot.DockPoint != null ? slot.DockPoint : slot.transform);
        }

        public void OnBallArrivedAtDock(HintBall ball)
        {
            if (!_claimedSlots.TryGetValue(ball, out var slot)) return;
            _claimedSlots.Remove(ball);
            slot.AcceptBall(ball);
            if (Board != null) Board.NotifySlotFilled(slot);
        }

        public void OnBallRemoved(HintBall ball)
        {
            // 사용자가 캡처 중인 공을 다시 잡아간 경우 — 슬롯 예약 해제.
            if (_claimedSlots.TryGetValue(ball, out var slot))
            {
                slot.Release();
                _claimedSlots.Remove(ball);
            }
            _suppressUntil[ball] = Time.time + ReentryGuard;
        }
    }
}
