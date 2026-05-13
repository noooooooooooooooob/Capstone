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

        /// <summary>파이프를 이 slot 에 안착시킨다.</summary>
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
