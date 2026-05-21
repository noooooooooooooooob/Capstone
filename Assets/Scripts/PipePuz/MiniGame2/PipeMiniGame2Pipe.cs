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

        [Header("Visual Adjustment")]
        [Tooltip("Pipe mesh visual offset (pipeRoot 자식 prefab inst 의 localPosition). " +
                 "회전 중심에 메시 visual center 가 안 맞을 때 인스펙터에서 직접 조정. " +
                 "값 바꾸면 OnValidate 로 라이브 반영.")]
        public Vector3 VisualOffset = Vector3.zero;

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

        /// <summary>
        /// 잡힌 동안 매 프레임 — 충돌 기반: 파이프 transform 위치가 slot 의 박스 영역 안에 들어오면
        /// 그 slot 에 logical attach (transform 은 손 따라 free, CurrentPipe 만 설정).
        /// 박스 영역 밖이면 logical detach. BFS 가 즉시 동작 → "기능 실행".
        /// 실제 transform lock 은 OnReleased 에서.
        /// </summary>
        void Update()
        {
            if (_grab == null || !_grab.isSelected) return;
            if (Board == null) return;
            if (IsFixed) return;

            var slot = Board.FindContainingSlot(transform.position);
            if (slot != CurrentSlot)
            {
                if (CurrentSlot != null)
                {
                    CurrentSlot.ReleasePipe();
                }
                if (slot != null)
                {
                    slot.AcceptPipeLogical(this);
                }
                Board.OnFlowChanged();
            }
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (Board == null)
            {
                _previousSlot = null;
                return;
            }

            // Hover 중 logical attach 된 slot 이 있으면 → transform 까지 lock.
            // 없으면 → 그 자리에 detached 로 떨어짐 (slot 박스 영역 밖).
            if (CurrentSlot != null)
            {
                CurrentSlot.AcceptPipe(this);
            }
            else
            {
                // 안전장치: hover 추적이 안 됐을 경우 OnReleased 시점에 한 번 더 충돌 검사.
                var slot = Board.FindContainingSlot(transform.position);
                if (slot != null)
                {
                    slot.AcceptPipe(this);
                }
            }

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

                // VisualOffset 적용 — PipeRoot 첫 자식(prefab inst)의 localPosition 갱신.
                // 사용자가 인스펙터에서 메시 위치 조정해서 회전 중심에 visual center 를 맞춤.
                if (PipeRoot.childCount > 0)
                {
                    var inst = PipeRoot.GetChild(0);
                    if (inst != null) inst.localPosition = VisualOffset;
                }
            }
        }

        // 인스펙터 라이브 반영.
        void OnValidate()
        {
            ApplyVisual();
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
