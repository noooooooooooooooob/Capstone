using UnityEngine;

namespace Capstone.Puzzle
{
    /// <summary>
    /// 라디에이터 밸브의 진행도(<see cref="RadiatorValve.NormalizedClose"/>)에 따라
    /// 주변에 안개(스팀 누출) 파티클을 짙게/옅게 만드는 비주얼 컴포넌트.
    ///
    /// 동작:
    ///   - ValveAngle 0(열림)   → 안개 없음
    ///   - ValveAngle 720(잠김) → 최대 밀도/반경의 안개
    ///   - 반대로 돌리면 자연스럽게 다시 사라진다.
    ///
    /// ParticleSystem은 Awake에서 자동으로 생성되므로 씬에서 별도 셋업이 필요 없다.
    /// 안개 강도는 <see cref="RadiatorValve.NormalizedClose"/>를 보간(EMA)한 값으로 사용해
    /// 회전을 멈춰도 부드럽게 페이드 인/아웃 된다.
    ///
    /// 네트워크 동기화는 ValveAngle 자체가 [Networked]이므로 추가 코드가 필요 없다.
    /// 양 클라이언트가 같은 ValveAngle을 보면 같은 강도의 안개를 본다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RadiatorFogVisual : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("진행도(NormalizedClose)를 읽어올 RadiatorValve. 비워두면 부모 계층에서 자동 탐색.")]
        [SerializeField] RadiatorValve valve;

        [Tooltip("안개를 뿜을 중심점. 비워두면 이 컴포넌트가 붙은 Transform을 사용.")]
        [SerializeField] Transform fogOrigin;

        [Header("형태")]
        [Tooltip("최대 강도일 때 안개가 퍼질 반경(미터). 파티클 Shape의 sphere radius로 사용.")]
        [SerializeField] float maxRadius = 1.5f;

        [Tooltip("안개 입자 하나의 평균 수명(초)")]
        [SerializeField] float lifetime = 4f;

        [Tooltip("입자 시작 크기(미터)")]
        [SerializeField] float startSize = 0.6f;

        [Tooltip("입자 시작 크기 랜덤 가산치(미터)")]
        [SerializeField] float startSizeRandom = 0.4f;

        [Tooltip("위로 떠오르는 평균 속도(m/s)")]
        [SerializeField] float upwardDrift = 0.15f;

        [Header("강도")]
        [Tooltip("정규화 강도(0~1) → 초당 emission rate")]
        [SerializeField] float maxEmissionRate = 80f;

        [Tooltip("최대 강도일 때 입자 시작 알파(0~1)")]
        [Range(0f, 1f)]
        [SerializeField] float maxAlpha = 0.35f;

        [Tooltip("안개 색상(알파는 maxAlpha로 덮어씀)")]
        [SerializeField] Color fogColor = new Color(0.85f, 0.88f, 0.92f, 1f);

        [Tooltip("강도 변화 부드러움(0=즉시, 1에 가까울수록 느린 페이드). 0.1 권장.")]
        [Range(0f, 0.99f)]
        [SerializeField] float smoothing = 0.1f;

        [Tooltip("이 강도 미만이면 시스템을 정지시켜 빈 파티클이 안 나오게 한다.")]
        [SerializeField] float stopThreshold = 0.01f;

        ParticleSystem _ps;
        ParticleSystem.EmissionModule _emission;
        ParticleSystem.MainModule _main;
        ParticleSystem.ShapeModule _shape;
        ParticleSystem.VelocityOverLifetimeModule _vel;
        ParticleSystem.ColorOverLifetimeModule _colorOverLife;
        ParticleSystemRenderer _renderer;

        // 부드럽게 페이드 인/아웃 되도록 내부 EMA 상태
        float _smoothedIntensity;

        void Reset()
        {
            if (valve == null) valve = FindValve();
            if (fogOrigin == null) fogOrigin = transform;
        }

        void Awake()
        {
            if (valve == null) valve = FindValve();
            if (fogOrigin == null) fogOrigin = transform;
            BuildParticleSystem();
        }

        RadiatorValve FindValve()
        {
            // Radiator(부모)에 붙였든 Valve(자식)에 붙였든 양쪽에서 자동 탐색.
            var v = GetComponentInParent<RadiatorValve>();
            if (v == null) v = GetComponentInChildren<RadiatorValve>(true);
            return v;
        }

        void Update()
        {
            if (_ps == null || valve == null) return;

            float target = ReadIntensity();

            // 단순 EMA. smoothing=0 이면 즉시 반영.
            float k = Mathf.Clamp01(1f - smoothing);
            _smoothedIntensity = Mathf.Lerp(_smoothedIntensity, target, k);

            ApplyIntensity(_smoothedIntensity);
        }

        float ReadIntensity()
        {
            // 네트워크 객체가 아직 valid 하지 않으면 0(안개 없음)
            if (valve.Object == null || !valve.Object.IsValid) return 0f;
            return valve.NormalizedClose;
        }

        void ApplyIntensity(float t)
        {
            // 1. 방출량
            _emission.rateOverTime = Mathf.Lerp(0f, maxEmissionRate, t);

            // 2. 시작 컬러(알파만 변조)
            var c = fogColor;
            c.a = maxAlpha * t;
            _main.startColor = c;

            // 3. 반경(약하게 퍼지다 강할 때 maxRadius)
            _shape.radius = Mathf.Lerp(maxRadius * 0.25f, maxRadius, t);

            // 4. 강도가 매우 낮으면 emission 중단
            if (t < stopThreshold)
            {
                if (_ps.isPlaying) _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            else
            {
                if (!_ps.isPlaying) _ps.Play(false);
            }
        }

        // ---------------------------------------------------------------------
        // ParticleSystem 자동 구성
        // ---------------------------------------------------------------------

        void BuildParticleSystem()
        {
            // 자식 GameObject로 PS를 만들어 라디에이터와 분리.
            var psGo = new GameObject("RadiatorFog_PS");
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
            _main.startSize = startSize;
            _main.startSize3D = false;
            _main.simulationSpace = ParticleSystemSimulationSpace.World;
            _main.scalingMode = ParticleSystemScalingMode.Local;
            _main.maxParticles = 400;
            _main.startColor = fogColor;
            _main.gravityModifier = 0f;
            _main.playOnAwake = false;

            // start size를 [size, size+random]으로 변동
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

            // 입자 수명에 따라 알파 페이드 in → out
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

            // 렌더러: URP/Built-in 어느 쪽이든 가능한 셰이더로 폴백.
            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.alignment = ParticleSystemRenderSpace.View;
            _renderer.material = CreateFogMaterial();
            _renderer.sortingFudge = 0f;
            _renderer.minParticleSize = 0f;
            _renderer.maxParticleSize = 4f;

            // 처음에는 강도 0이므로 멈춰 있게.
            _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        static Material CreateFogMaterial()
        {
            // URP의 Particles/Unlit를 우선 시도, 실패 시 일반 Particles/Standard Unlit, 최후엔 Sprites/Default.
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
            mat.name = "RadiatorFog_Material";

            // 가능한 키워드들을 안전하게 토글 (셰이더에 없으면 무시됨)
            mat.SetFloat("_Surface", 1f);            // URP: 0 = Opaque, 1 = Transparent
            mat.SetFloat("_Blend", 0f);              // URP: 0 = Alpha, 2 = Additive
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");

            // 텍스처가 없으면 뽀얗고 단조롭게 보이므로 기본 흰색 텍스처라도 할당.
            mat.color = Color.white;
            return mat;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 origin = (fogOrigin != null ? fogOrigin : transform).position;
            Gizmos.color = new Color(0.7f, 0.85f, 1f, 0.4f);
            Gizmos.DrawWireSphere(origin, maxRadius);
        }
#endif
    }
}
