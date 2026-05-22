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

        /// <summary>손에 잡혀 있는지 — LightOrb.IsHeld 와 동일 의미. Slot 의 trigger 가 검사.</summary>
        public bool IsHeld => _grab != null && _grab.isSelected;

        XRGrabInteractable _grab;

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

        // LightOrb 패턴 — 잡으면 slot 에서 즉시 detach.
        // 자동 attach 는 PipeMiniGame2Slot 의 OnTriggerStay 가 처리 (잡혀있지 않은 pipe 가 sphere 영역 진입 시).
        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (CurrentSlot != null)
            {
                CurrentSlot.ReleasePipe();
                CurrentSlot = null;
                if (Board != null) Board.OnFlowChanged();
            }
        }

        // 손 놓는 순간엔 별도 처리 없음 — Slot 의 OnTriggerStay 가 다음 tick 에 IsHeld=false 를 감지하고 자동 흡수.
        // BFS 만 갱신.
        void OnReleased(SelectExitEventArgs args)
        {
            if (Board != null) Board.OnFlowChanged();
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
