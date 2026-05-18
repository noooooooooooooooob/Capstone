using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 광선을 받는 target. <see cref="LightBeamController"/> 가 매 프레임 SetBeamHit(true/false) 호출.
    /// </summary>
    public class LightBeamReceiver : MonoBehaviour
    {
        [Header("Visual feedback")]
        public Renderer GlowRenderer;
        public Color HitColor = new Color(0.4f, 1f, 0.5f);
        public Color IdleColor = new Color(0.4f, 0.4f, 0.4f);
        public float HitEmissionIntensity = 2f;
        public float IdleEmissionIntensity = 0.15f;

        [Header("Events")]
        public UnityEvent OnFirstHit;
        public UnityEvent<bool> OnHitChanged;

        public bool IsHit { get; private set; }
        bool _everHit;
        Material _matInstance;

        void Awake()
        {
            if (GlowRenderer != null) _matInstance = GlowRenderer.material;
            UpdateVisual();
        }

        public void SetBeamHit(bool hit)
        {
            if (hit == IsHit) return;
            IsHit = hit;
            OnHitChanged?.Invoke(hit);
            if (hit && !_everHit)
            {
                _everHit = true;
                OnFirstHit?.Invoke();
                Debug.Log($"[LightBeam] Receiver '{name}' first hit!");
            }
            UpdateVisual();
        }

        void UpdateVisual()
        {
            if (_matInstance == null) return;
            Color baseC = IsHit ? HitColor : IdleColor;
            _matInstance.color = baseC;
            if (_matInstance.HasProperty("_BaseColor")) _matInstance.SetColor("_BaseColor", baseC);
            if (_matInstance.HasProperty("_EmissionColor"))
            {
                float intensity = IsHit ? HitEmissionIntensity : IdleEmissionIntensity;
                _matInstance.SetColor("_EmissionColor", baseC * intensity);
                _matInstance.EnableKeyword("_EMISSION");
            }
        }
    }
}
