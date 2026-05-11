using UnityEngine;

namespace PipePuz.DimensionalAssembly
{
    /// <summary>
    /// 허공에 떠 있는 에너지 노드. 톱니바퀴가 정렬됐을 때만 Active 상태가 되어 밝게 빛난다.
    /// Active 상태에서만 와이어 연결이 가능하다 (실제 검사는 DAAssemblyController 에서).
    /// </summary>
    public class DAEnergyNode : MonoBehaviour
    {
        [Header("Identity")]
        public int Id;

        [Header("Visual")]
        [Tooltip("발광 머티리얼이 적용된 시각 sphere 의 Renderer.")]
        public Renderer VisualRenderer;

        [Tooltip("비활성 상태에서의 emission (약하게).")]
        public Color InactiveEmission = new Color(0.08f, 0.12f, 0.28f);

        [Tooltip("Active 상태에서의 emission (강하게).")]
        public Color ActiveEmission = new Color(0.4f, 0.85f, 1.4f) * 1.6f;

        public bool IsActive { get; private set; }

        Material _matInstance;

        void Awake()
        {
            if (VisualRenderer != null)
            {
                // 각 노드마다 고유 머티리얼 인스턴스를 가지도록 .material 로 자동 instantiate.
                _matInstance = VisualRenderer.material;
            }
            ApplyEmission();
        }

        public void SetActive(bool active)
        {
            if (IsActive == active) return;
            IsActive = active;
            ApplyEmission();
        }

        void ApplyEmission()
        {
            if (_matInstance == null || !_matInstance.HasProperty("_EmissionColor")) return;
            _matInstance.SetColor("_EmissionColor", IsActive ? ActiveEmission : InactiveEmission);
            _matInstance.EnableKeyword("_EMISSION");
        }
    }
}
