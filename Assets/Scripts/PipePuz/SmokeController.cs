using UnityEngine;

namespace PipePuz
{
    /// <summary>
    /// PipeSocket 위치에 붙어 있는 연기 ParticleSystem 의 강도(0~1)를 조절한다.
    /// 0 이면 멈추고, 1 이면 최대 반경/방출률로 시야를 가린다.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class SmokeController : MonoBehaviour
    {
        [Header("Tuning")]
        [Tooltip("최대 강도일 때 ParticleSystem 의 emission rate. 시야를 가릴 만큼 진하게.")]
        public float MaxEmissionRate = 220f;

        [Tooltip("최소 강도(>0)일 때의 shape radius.")]
        public float MinRadius = 0.1f;
        [Tooltip("최대 강도일 때의 shape radius. 기본 1.5 → 3.0 으로 2 배 확장.")]
        public float MaxRadius = 3.0f;

        [Tooltip("최소 강도(>0)일 때의 startSize.")]
        public float MinStartSize = 0.4f;
        [Tooltip("최대 강도일 때의 startSize. 입자 자체도 크게 키워 진해 보이게.")]
        public float MaxStartSize = 2.5f;

        [Range(0f, 1f)]
        [Tooltip("0 이면 연기 없음, 1 이면 최대.")]
        public float Intensity = 0f;

        ParticleSystem _ps;

        void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            Apply();
        }

        void OnEnable() => Apply();

        public void SetIntensity(float value)
        {
            Intensity = Mathf.Clamp01(value);
            Apply();
        }

        void Apply()
        {
            if (_ps == null) _ps = GetComponent<ParticleSystem>();
            if (_ps == null) return;

            var emission = _ps.emission;
            var shape = _ps.shape;
            var main = _ps.main;

            if (Intensity <= 0.001f)
            {
                emission.rateOverTime = 0f;
                if (_ps.isPlaying) _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            emission.rateOverTime = MaxEmissionRate * Intensity;
            shape.radius = Mathf.Lerp(MinRadius, MaxRadius, Intensity);
            main.startSize = Mathf.Lerp(MinStartSize, MaxStartSize, Intensity);

            if (!_ps.isPlaying) _ps.Play(true);
        }
    }
}
