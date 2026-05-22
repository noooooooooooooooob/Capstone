using UnityEngine;
using PipePuz.MiniGame;

namespace PipePuz.MiniGame2
{
    /// <summary>
    /// 벽 패널 그리드의 한 자리. 파이프를 받거나 비워둘 수 있다.
    /// </summary>
    public class PipeMiniGame2Slot : MonoBehaviour
    {
        [Header("Identity")]
        public int X;
        public int Y;

        [Header("Runtime")]
        public PipeMiniGame2Pipe CurrentPipe;
        public PipeMiniGame2Board Board;

        [Header("Visual")]
        [Tooltip("비어있을 때만 보이는 outline 시각.")]
        public GameObject EmptyOutline;

        public bool IsEmpty => CurrentPipe == null;

        public Direction CurrentMask
        {
            get
            {
                if (CurrentPipe == null) return Direction.None;
                return CurrentPipe.CurrentMask;
            }
        }

        void Start()
        {
            UpdateOutline();
        }

        // ===== LightOrbSocket 패턴 — Trigger 영역 기반 자동 흡수 =====
        // Slot 자체에 SphereCollider(isTrigger=true) 가 부착되어 있고, 잡혀있지 않은 pipe 가
        // 그 영역에 진입하면 자동으로 AcceptPipe (위치/회전/물리 lock).
        // Pipe 가 잡히면 Pipe.OnGrabbed 가 ReleasePipe 를 호출 — slot 비움.

        void OnTriggerEnter(Collider other) { TryAcceptFromTrigger(other); }
        void OnTriggerStay(Collider other)  { TryAcceptFromTrigger(other); }

        void TryAcceptFromTrigger(Collider other)
        {
            if (!IsEmpty) return; // 이미 다른 pipe 들어 있음

            var pipe = other.GetComponent<PipeMiniGame2Pipe>()
                       ?? other.GetComponentInParent<PipeMiniGame2Pipe>();
            if (pipe == null) return;
            if (pipe.IsFixed) return;                 // Source/Sink 등 고정 pipe 무시
            if (pipe.IsHeld) return;                  // 손에 잡혀 있으면 받지 않음
            if (pipe.CurrentSlot != null) return;     // 이미 다른 slot 에 있음

            AcceptPipe(pipe);
            if (Board != null) Board.OnFlowChanged();
        }

        /// <summary>파이프를 이 slot 에 안착시킨다 (transform 포함).</summary>
        public void AcceptPipe(PipeMiniGame2Pipe pipe)
        {
            if (pipe == null) return;

            // 다른 파이프가 들어있으면 먼저 제거 (보통 일어나지 않지만 안전장치).
            if (CurrentPipe != null && CurrentPipe != pipe)
            {
                CurrentPipe.CurrentSlot = null;
            }

            CurrentPipe = pipe;
            pipe.CurrentSlot = this;
            if (Board != null) pipe.Board = Board;

            // 위치/회전 정렬.
            pipe.transform.SetParent(transform, false);
            pipe.transform.localPosition = Vector3.zero;
            pipe.transform.localRotation = Quaternion.identity;

            // 물리 고정.
            var rb = pipe.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            UpdateOutline();
        }

        /// <summary>
        /// Hover 중 (잡힌 채 SnapDistance 안) 사용 — reference 만 설정해 BFS 가 인식하게 함.
        /// transform 은 옮기지 않음 — 손이 따라가는 동안 visual 은 free.
        /// 진짜 release 시점에 AcceptPipe 가 transform 까지 lock.
        /// </summary>
        public void AcceptPipeLogical(PipeMiniGame2Pipe pipe)
        {
            if (pipe == null) return;
            if (CurrentPipe == pipe) return;

            if (CurrentPipe != null && CurrentPipe != pipe)
            {
                CurrentPipe.CurrentSlot = null;
            }

            CurrentPipe = pipe;
            pipe.CurrentSlot = this;
            if (Board != null) pipe.Board = Board;

            UpdateOutline();
        }

        /// <summary>파이프가 빠져나갈 때 호출. CurrentPipe = null 만 비우고 transform 은 그대로 둔다.</summary>
        public void ReleasePipe()
        {
            if (CurrentPipe == null) return;
            var pipe = CurrentPipe;
            CurrentPipe = null;
            if (pipe.CurrentSlot == this) pipe.CurrentSlot = null;
            UpdateOutline();
        }

        void UpdateOutline()
        {
            if (EmptyOutline != null) EmptyOutline.SetActive(IsEmpty);
        }
    }
}
