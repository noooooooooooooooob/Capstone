using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.MiniGame
{
    /// <summary>
    /// 파이프 미니게임의 한 칸.
    /// XRSimpleInteractable 의 SelectEntered 가 들어올 때마다 90° 시계방향 회전.
    /// 시각 회전은 <see cref="PipeRoot"/> 의 localRotation 으로만 처리.
    /// 보드(<see cref="Board"/>)에 변경을 알리면 보드가 BFS 로 색을 갱신한다.
    /// </summary>
    public class PipeMiniGameCell : MonoBehaviour
    {
        [Header("Identity")]
        public int X;
        public int Y;
        public PipeShape Shape;

        [Range(0, 3)]
        [Tooltip("0..3, 시계방향 90° 단위.")]
        public int Rotation;

        [Tooltip("true 면 회전 불가 (Source / Sink 등 고정 셀).")]
        public bool IsFixed;

        [Header("Refs")]
        [Tooltip("회전이 적용될 자식 Transform.")]
        public Transform PipeRoot;

        [Tooltip("연결 여부에 따라 색이 바뀔 Renderer 들 (보통 Hub + Arms).")]
        public Renderer[] PipeRenderers;

        [Tooltip("부모 보드. 빌드 시 에디터 스크립트가 채운다.")]
        public PipeMiniGameBoard Board;

        public Direction CurrentMask => PipeShapeDef.GetMask(Shape, Rotation);

        XRSimpleInteractable _interactable;

        void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
            if (_interactable != null)
            {
                _interactable.selectEntered.AddListener(OnSelectEntered);
            }
            ApplyVisual();
        }

        void OnDestroy()
        {
            if (_interactable != null)
            {
                _interactable.selectEntered.RemoveListener(OnSelectEntered);
            }
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            Rotate();
        }

        /// <summary>한 번 호출하면 90° 시계방향 회전.</summary>
        public void Rotate()
        {
            if (IsFixed) return;
            Rotation = (Rotation + 1) % 4;
            ApplyVisual();
            if (Board != null) Board.OnCellChanged();
        }

        public void ApplyVisual()
        {
            if (PipeRoot != null)
            {
                // 화면 평면(Z 축)을 기준으로 시계방향(=음의 각도) 회전.
                PipeRoot.localRotation = Quaternion.Euler(0f, 0f, -90f * Rotation);
            }
        }

        /// <summary>BFS 결과에 따라 호출. 연결됨 → 빨강 / 끊김 → 노랑 머티리얼 적용.</summary>
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
