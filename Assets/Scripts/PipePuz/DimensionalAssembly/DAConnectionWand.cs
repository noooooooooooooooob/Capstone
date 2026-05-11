using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.DimensionalAssembly
{
    /// <summary>
    /// 사용자가 잡고 사용하는 연결 도구(wand).
    /// - XRGrabInteractable 로 잡기.
    /// - 잡혀있는 동안 매 프레임 Tip 의 forward 로 레이캐스트해 hovered node 갱신, Laser 라인 그림.
    /// - activated (트리거 누름) 시: 현재 hovered node 를 source 로 저장.
    /// - 그 동안 PreviewLine 이 source → tip 으로 그려진다.
    /// - deactivated (트리거 뗌) 시: hovered node 가 source 와 다르면 controller.TryAddConnection 호출.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class DAConnectionWand : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("레이의 출발점 (보통 wand 끝).")]
        public Transform Tip;

        [Tooltip("Tip 으로부터 항상 그려지는 가이드 레이저 라인.")]
        public LineRenderer LaserLine;

        [Tooltip("source → tip 으로 그려지는 그리기 중 미리보기 라인.")]
        public LineRenderer PreviewLine;

        [Tooltip("연결을 등록할 메인 컨트롤러.")]
        public DAAssemblyController Controller;

        [Header("Raycast")]
        public LayerMask NodeMask = ~0;
        public float MaxRayDistance = 4f;
        [Tooltip("Trigger collider 도 hit 으로 받을지.")]
        public bool HitTriggers = true;

        XRGrabInteractable _grab;
        DAEnergyNode _hoveredNode;
        DAEnergyNode _sourceNode;

        public bool IsHeld => _grab != null && _grab.isSelected;
        public bool IsDrawing => _sourceNode != null;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            if (_grab != null)
            {
                _grab.activated.AddListener(OnActivated);
                _grab.deactivated.AddListener(OnDeactivated);
                _grab.selectExited.AddListener(OnReleased);
            }
            if (PreviewLine != null) PreviewLine.enabled = false;
        }

        void OnDestroy()
        {
            if (_grab != null)
            {
                _grab.activated.RemoveListener(OnActivated);
                _grab.deactivated.RemoveListener(OnDeactivated);
                _grab.selectExited.RemoveListener(OnReleased);
            }
        }

        void Update()
        {
            UpdateHover();
            UpdateLaser();
            UpdatePreview();
        }

        void UpdateHover()
        {
            _hoveredNode = null;
            if (Tip == null) return;
            var triggerQuery = HitTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
            if (Physics.Raycast(Tip.position, Tip.forward, out var hit, MaxRayDistance, NodeMask, triggerQuery))
            {
                _hoveredNode = hit.collider.GetComponentInParent<DAEnergyNode>();
            }
        }

        void UpdateLaser()
        {
            if (LaserLine == null || Tip == null) return;
            LaserLine.enabled = IsHeld; // 잡았을 때만 표시
            if (!IsHeld) return;
            Vector3 from = Tip.position;
            Vector3 to;
            if (_hoveredNode != null)
                to = _hoveredNode.transform.position;
            else
                to = from + Tip.forward * MaxRayDistance;
            LaserLine.SetPosition(0, from);
            LaserLine.SetPosition(1, to);
        }

        void UpdatePreview()
        {
            if (PreviewLine == null) return;
            bool show = IsDrawing && Tip != null;
            PreviewLine.enabled = show;
            if (!show) return;
            PreviewLine.SetPosition(0, _sourceNode.transform.position);
            PreviewLine.SetPosition(1, Tip.position);
        }

        void OnActivated(ActivateEventArgs args)
        {
            // 트리거 누르는 순간 호버된 노드를 source 로 캡처.
            if (_hoveredNode == null) return;
            _sourceNode = _hoveredNode;
        }

        void OnDeactivated(DeactivateEventArgs args)
        {
            // 트리거 떼는 순간 호버된 노드가 source 와 다르면 연결 시도.
            if (_sourceNode != null && _hoveredNode != null && _hoveredNode != _sourceNode && Controller != null)
            {
                Controller.TryAddConnection(_sourceNode, _hoveredNode);
            }
            _sourceNode = null;
        }

        void OnReleased(SelectExitEventArgs args)
        {
            // wand 자체를 놓으면 그리기 취소.
            _sourceNode = null;
        }
    }
}
