using UnityEngine;

namespace Capstone.Puzzle
{
    /// <summary>
    /// "고장난 파이프(Pipe broke)에서 새는 증기" 비주얼.
    /// <see cref="RadiatorFogVisual"/>의 자매 컴포넌트지만 다음과 같이 다르다:
    ///
    ///   - 단순히 ValveAngle 만이 아니라 <see cref="RadiatorPipeSocket"/>의 상태도 게이트로 사용
    ///   - Pipe broke 가 끼워져 있을 때만 연기가 나옴
    ///   - Valve 를 잠글수록(NormalizedClose ↑) 연기 강도는 줄어듬
    ///   - 완전 잠금 → 연기 0
    ///   - Pipe new 로 교체 → 무조건 연기 0
    ///   - 두 가지 모드: 불투명(시야 가리기, RadiatorB 측) / 반투명(RadiatorA 측)
    ///
    /// 강도 계산식
    ///     base   = (브로크 연결됨) ? (1 - normalizedClose) : 0
    ///     visual = EMA(base) 로 부드럽게 페이드
    /// </summary>
    [DisallowMultipleComponent]
    public class PipeLeakFog : MonoBehaviour
    {
        public enum FogStyle
        {
            /// <summary>RadiatorB 측 — 알파/방출량 모두 강하게, 시야를 가린다.</summary>
            Opaque,
            /// <summary>RadiatorA 측 — 알파를 낮춰 반투명한 안개.</summary>
            Translucent,
        }

        [Header("연결")]
        [Tooltip("Broke / New 상태를 읽어올 RadiatorB의 PipeSocket. 비워두면 부모에서 탐색.")]
        [SerializeField] RadiatorPipeSocket socket;

        [Tooltip("진행도(NormalizedClose)를 읽어올 RadiatorValve. 비워두면 부모에서 탐색.")]
        [SerializeField] RadiatorValve valve;

        [Tooltip("연기를 뿜을 중심점. 비워두면 이 컴포넌트의 Transform 사용.")]
        [SerializeField] Transform fogOrigin;

        [Header("스타일")]
        [SerializeField] FogStyle style = FogStyle.Opaque;

        [Header("형태")]
        [SerializeField] float maxRadius = 1.5f;
        [SerializeField] float lifetime = 4f;
        [SerializeField] float startSize = 0.6f;
        [SerializeField] float startSizeRandom = 0.4f;
        [SerializeField] float upwardDrift = 0.15f;

        [Header("강도")]
        [Tooltip("최대 강도일 때 초당 입자 수")]
        [SerializeField] float maxEmissionRate = 80f;

        [Tooltip("Opaque 모드의 입자 시작 알파")]
        [Range(0f, 1f)]
        [SerializeField] float opaqueAlpha = 0.85f;

        [Tooltip("Translucent 모드의 입자 시작 알파")]
        [Range(0f, 1f)]
        [SerializeField] float translucentAlpha = 0.25f;

        [Tooltip("연기 색")]
        [SerializeField] Color fogColor = new Color(0.85f, 0.88f, 0.92f, 1f);

        [Tooltip("페이드 부드러움 (0=즉시, 1에 가까울수록 느림)")]
        [Range(0f, 0.99f)]
        [SerializeField] float smoothing = 0.12f;

        [Tooltip("이 강도 미만이면 emission 정지")]
        [SerializeField] float stopThreshold = 0.01f;

        ParticleSystem _ps;
        ParticleSystem.EmissionModule _emission;
        ParticleSystem.MainModule _main;
        ParticleSystem.ShapeModule _shape;
        ParticleSystem.VelocityOverLifetimeModule _vel;
        ParticleSystem.ColorOverLifetimeModule _colorOverLife;
        ParticleSystemRenderer _renderer;

        float _smoothedIntensity;

        void Reset()
        {
            if (valve == null)  valve  = GetComponentInParent<RadiatorValve>();
            if (socket == null) socket = GetComponentInParent<RadiatorPipeSocket>();
            if (fogOrigin == null) fogOrigin = transform;
        }

        void Awake()
        {
            if (valve == null)  valve  = GetComponentInParent<RadiatorValve>();
            if (socket == null) socket = GetComponentInParent<RadiatorPipeSocket>();
            if (fogOrigin == null) fogOrigin = transform;
            BuildParticleSystem();
        }

        void Update()
        {
            if (_ps == null) return;

            float target = ReadIntensity();

            float k = Mathf.Clamp01(1f - smoothing);
            _smoothedIntensity = Mathf.Lerp(_smoothedIntensity, target, k);

            ApplyIntensity(_smoothedIntensity);
        }

        float ReadIntensity()
        {
            // 게이트 1: 소켓에 broke 가 연결되어 있어야 함. socket 이 없거나 new/None 이면 0.
            if (socket == null) return 0f;
            if (socket.Object == null || !socket.Object.IsValid) return 0f;
            if (!socket.IsBrokeConnected) return 0f;

            // 게이트 2: Valve 가 잠긴 만큼 강도 감소
            float closeT = 0f;
            if (valve != null && valve.Object != null && valve.Object.IsValid)
                closeT = valve.NormalizedClose;

            return 1f - Mathf.Clamp01(closeT);
        }

        void ApplyIntensity(float t)
        {
            _emission.rateOverTime = Mathf.Lerp(0f, maxEmissionRate, t);

            float alpha = (style == FogStyle.Opaque) ? opaqueAlpha : translucentAlpha;
            var c = fogColor;
            c.a = alpha * t;
            _main.startColor = c;

            _shape.radius = Mathf.Lerp(maxRadius * 0.25f, maxRadius, t);

            if (t < stopThreshold)
            {
                if (_ps.isPlaying) _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            else
            {
                if (!_ps.isPlaying) _ps.Play(false);
            }
        }

        void BuildParticleSystem()
        {
            var psGo = new GameObject("PipeLeakFog_PS_" + style);
            psGo.transform.SetParent(fogOrigin != null ? fogOrigin : transform, false);
            psGo.transform.localPosition = Vector3.zero;
            psGo.transform.localRotation = Quaternion.identity;
            psGo.transform.localScale = Vector3.one;

            _ps = psGo.AddComponent<ParticleSystem>();
            _renderer = psGo.GetComponent<ParticleSystemRenderer>();

            _main = _ps.main;
            _main.duration = 1f;
            _main.loop = true;
            _main.startLifetime = lifetime;
            _main.startSpeed = upwardDrift;
            _main.simulationSpace = ParticleSystemSimulationSpace.World;
            _main.scalingMode = ParticleSystemScalingMode.Local;
            _main.maxParticles = 600;
            _main.startColor = fogColor;
            _main.gravityModifier = 0f;
            _main.playOnAwake = false;

            var sizeRange = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.05f, startSize - startSizeRandom * 0.5f),
                startSize + startSizeRandom * 0.5f);
            _main.startSize = sizeRange;

            _emission = _ps.emission;
            _emission.enabled = true;
            _emission.rateOverTime = 0f;

            _shape = _ps.shape;
            _shape.enabled = true;
            _shape.shapeType = ParticleSystemShapeType.Sphere;
            _shape.radius = maxRadius * 0.25f;

            _vel = _ps.velocityOverLifetime;
            _vel.enabled = true;
            _vel.space = ParticleSystemSimulationSpace.World;
            _vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            _vel.y = new ParticleSystem.MinMaxCurve(upwardDrift * 0.5f, upwardDrift * 1.5f);
            _vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            _colorOverLife = _ps.colorOverLifetime;
            _colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            _colorOverLife.color = grad;

            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.alignment = ParticleSystemRenderSpace.View;
            _renderer.material = CreateFogMaterial();
            _renderer.sortingFudge = 0f;
            _renderer.minParticleSize = 0f;
            _renderer.maxParticleSize = 4f;

            _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        static Material CreateFogMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
            mat.name = "PipeLeakFog_Material";

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");

            mat.color = Color.white;
            return mat;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 origin = (fogOrigin != null ? fogOrigin : transform).position;
            Color c = (style == FogStyle.Opaque)
                ? new Color(0.4f, 0.4f, 0.4f, 0.6f)
                : new Color(0.7f, 0.85f, 1f, 0.4f);
            Gizmos.color = c;
            Gizmos.DrawWireSphere(origin, maxRadius);
        }
#endif
    }
}
