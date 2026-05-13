using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PipePuz.MiniGame;

namespace PipePuz.MiniGame2
{
    /// <summary>
    /// XRGrabInteractable 로 잡고 옮길 수 있는 파이프.
    /// - selectEntered (잡힘) → 현재 slot 에서 lift
    /// - activated (잡힌 채 트리거 누름) → Rotation += 1 (90° 회전)
    /// - selectExited (놓음) → 가까운 빈 slot 에 snap (없으면 이전 slot 으로, 그것도 없으면 그 자리)
    ///
    /// IsFixed 인 경우 (Source/Sink) 잡기 동작 없음 — 고정.
    /// </summary>
    public class PipeMiniGame2Pipe : MonoBehaviour
    {
        [Header("Identity")]
        public PipeShape Shape;
        [Range(0, 3)] public int Rotation;
        public bool IsFixed;

        [Header("Refs")]
        public Transform PipeRoot;
        public Renderer[] PipeRenderers;

        [Header("Runtime")]
        public PipeMiniGame2Slot CurrentSlot;
        public PipeMiniGame2Board Board;

        public Direction CurrentMask => PipeShapeDef.GetMask(Shape, Rotation);

        XRGrabInteractable _grab;
        PipeMiniGame2Slot _previousSlot;

        void Awake()
        {
            if (!IsFixed)
            {
                _grab = GetComponent<XRGrabInteractable>();
                if (_grab != null)
                {
                    _grab.selectEntered.AddListener(OnGrabbed);
                    _grab.selectExited.AddListener(OnReleased);
                    _grab.activated.AddListener(OnActivated);
                }
            }
            ApplyVisual();
        }

        void OnDestroy()
        {
            if (_grab != null)
            {
                _grab.selectEntered.RemoveListener(OnGrabbed);
                _grab.selectExited.RemoveListener(OnReleased);
                _grab.activated.RemoveListener(OnActivated);
            }
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            _previousSlot = CurrentSlot;
            if (CurrentSlot != null)
            {
                CurrentSlot.ReleasePipe();
                CurrentSlot = null;
                if (Board != null) Board.OnFlowChanged();
            }
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (Board == null)
            {
                _previousSlot = null;
                return;
            }

            // 가장 가까운 빈 slot 찾기.
            var slot = Board.FindNearestEmptySlot(transform.position);
            if (slot != null)
            {
                slot.AcceptPipe(this);
            }
            else if (_previousSlot != null && _previousSlot.IsEmpty)
            {
                // fallback: 직전에 있던 slot 으로 (트리거로 회전만 하려고 잡았다 놓은 케이스).
                _previousSlot.AcceptPipe(this);
            }
            // else: 어디에도 안 들어감 — 그 자리에 정지 (kinematic 유지)

            Board.OnFlowChanged();
            _previousSlot = null;
        }

        void OnActivated(ActivateEventArgs args)
        {
            // 잡고 있는 상태에서 트리거 → 90° CW 회전.
            Rotation = (Rotation + 1) % 4;
            ApplyVisual();
            // 잡힌 상태라 CurrentSlot 은 보통 null. release 시 BFS 재계산되니 여기서는 visual 만.
            if (CurrentSlot != null && Board != null) Board.OnFlowChanged();
        }

        public void ApplyVisual()
        {
            if (PipeRoot != null)
            {
                PipeRoot.localRotation = Quaternion.Euler(0f, 0f, -90f * Rotation);
            }
        }

        /// <summary>BFS 결과에 따라 호출. 연결됨 = 빨강 / 끊김 = 노랑.</summary>
        public void SetConnected(bool connected)
        {
            if (PipeRenderers == null || Board == null) return;
            var mat = connected ? Board.ConnectedMaterial : Board.DisconnectedMaterial;
            if (mat == null) return;
            for (int i = 0; i < PipeRenderers.Length; i++)
            {
                if (PipeRenderers[i] != null) PipeRenderers[i].sharedMaterial = mat;
            }
        }
    }
}
