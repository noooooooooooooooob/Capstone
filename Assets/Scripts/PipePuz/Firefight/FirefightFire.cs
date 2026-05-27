using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.Firefight
{
    /// <summary>
    /// 한 곳의 불. Strength 0..MaxStrength 보유.
    /// Update 에서 GrowthRate * dt 만큼 자란다. ApplyDamage(amount) 가 호출되면 그만큼 줄어든다.
    /// LateUpdate 에서 임계값 검사:
    ///   - Strength &lt;= 0 → 꺼짐 (OnExtinguished + 비활성)
    ///   - Strength &gt;= MaxStrength → 폭주 (clamp 만, 강제 실패는 없음)
    ///
    /// 시각은 Strength 비례로 다음 세 레이어를 함께 구동:
    ///   1) FireParticles — 메인 불꽃. 색/난기류는 Awake 시 위협적 톤으로 자동 보강 (AutoEnhanceFlames=true).
    ///   2) EmberParticles — 위로 튀는 ember 스파크. null 이면 자동 생성.
    ///   3) FireLight — 깜박이는 점광. null 이면 자동 생성. Quest 성능 위해 그림자는 OFF.
    /// </summary>
    public class FirefightFire : MonoBehaviour
    {
        [Header("Refs")]
        public ParticleSystem FireParticles;

        [Header("Refs (선택 — 비워두면 런타임에 자동 생성)")]
        [Tooltip("위로 튀는 ember 스파크. null 이면 자식으로 자동 생성.")]
        public ParticleSystem EmberParticles;

        [Tooltip("벽/바닥을 비추는 깜박이는 점광. null 이면 자식으로 자동 생성.")]
        public Light FireLight;

        [Header("Tuning")]
        [Tooltip("시작 Strength.")]
        [Range(0f, 1f)]
        public float StartStrength = 0.2f;

        [Tooltip("초당 자라는 양.")]
        public float GrowthRate = 0.08f;

        [Tooltip("폭주 임계값.")]
        public float MaxStrength = 1.0f;

        [Header("Visual tuning — Flames")]
        [Tooltip("Awake 시 FireParticles 의 colorOverLifetime / noise / 속도 / 라이프타임 등을 위협적 톤으로 덮어쓴다.\n" +
                 "커스텀 불꽃 프리팹(예: LargeFlames)을 사용 중이면 끄세요.")]
        public bool AutoEnhanceFlames = true;

        [Tooltip("매 프레임 FireParticles 의 emission rate / startSize 를 strength 비례로 덮어쓴다.\n" +
                 "커스텀 불꽃 프리팹의 emission/사이즈를 그대로 두고 싶으면 끄세요. (그 경우 strength 비례 시각 변화는 라이트+ember 만 적용됨)")]
        public bool DriveFlameEmissionAndSize = true;

        public float MaxEmissionRate = 60f;
        public float MinStartSize = 0.20f;
        public float MaxStartSize = 1.10f;

        [Tooltip("최대 강도일 때 불꽃이 흔들리는 Noise 모듈 강도. (AutoEnhanceFlames 가 켜진 경우에만 적용)")]
        public float TurbulenceStrength = 0.8f;

        [Header("Visual tuning — Threat layer (Light)")]
        [Tooltip("최대 강도일 때 점광 밝기 (Intensity).")]
        public float MaxLightIntensity = 4.5f;

        [Tooltip("점광 최대 도달거리 (Range).")]
        public float LightRange = 5f;

        [Tooltip("점광 깜박임 주파수 (Hz).")]
        public float LightFlickerSpeed = 18f;

        [Header("Visual tuning — Threat layer (Embers)")]
        [Tooltip("최대 강도일 때 ember 분출량 (개/초).")]
        public float MaxEmberRate = 70f;

        [Header("Sound")]
        [Tooltip("AudioManager에 등록된 불 루프 사운드 클립 이름")]
        public string fireSoundName;
        [Tooltip("시작 랜덤 딜레이 최대값(초)")]
        public float maxStartDelay = 0.5f;

        [Header("Events")]
        public UnityEvent OnExtinguished;

        [Header("Read-only state")]
        [SerializeField] float _strength;
        public float CurrentStrength => _strength;
        public bool IsActive { get; private set; } = true;

        // 깜박임 위상이 fire 마다 다르도록 랜덤 시드.
        float _flickerSeed;
        TemporarySoundPlayer _fireSound;

        void Awake()
        {
            _strength = Mathf.Clamp(StartStrength, 0.01f, MaxStrength - 0.01f);
            _flickerSeed = Random.value * 100f;
            if (AutoEnhanceFlames) EnhanceFlameVisuals();
            EnsureSupplementalVisuals();
            EnsureFireParticlesPlaying();
            ApplyVisual();
            DumpDiagnostics();
            StartFireSound();
        }

        /// <summary>
        /// FireParticles 가 어떤 이유든 정지 / 비활성 / 렌더러 OFF 상태로 들어와도 강제로 살림.
        /// 아티스트가 playOnAwake=false 인 프리팹을 만들었거나, 다른 스크립트가 Stop 했거나,
        /// GameObject 가 inactive 인 케이스를 모두 방어.
        /// </summary>
        void EnsureFireParticlesPlaying()
        {
            if (FireParticles == null) return;
            if (!FireParticles.gameObject.activeSelf) FireParticles.gameObject.SetActive(true);

            var renderer = FireParticles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && !renderer.enabled) renderer.enabled = true;

            // 자식(서브) ParticleSystem 까지 포함해서 모두 재생.
            if (!FireParticles.isPlaying) FireParticles.Play(true);
        }

        void DumpDiagnostics()
        {
            // 한 번만 — 인스펙터에서 진단용. 콘솔에서 Fire 별 상태를 확인할 수 있음.
            string fpInfo;
            if (FireParticles == null)
            {
                fpInfo = "FireParticles=NULL (참조 없음! 인스펙터에서 메인 불꽃 ParticleSystem 연결 필요)";
            }
            else
            {
                var r = FireParticles.GetComponent<ParticleSystemRenderer>();
                var matName = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "<null mat>";
                fpInfo = $"FireParticles='{FireParticles.name}' "
                       + $"active={FireParticles.gameObject.activeInHierarchy} "
                       + $"isPlaying={FireParticles.isPlaying} "
                       + $"rendererEnabled={(r != null ? r.enabled : false)} "
                       + $"material={matName} "
                       + $"startSize={FireParticles.main.startSize.constant:F2} "
                       + $"rate={FireParticles.emission.rateOverTime.constant:F1}";
            }
            Debug.Log($"[FirefightFire {name}] AutoEnhance={AutoEnhanceFlames} DriveFlame={DriveFlameEmissionAndSize} | {fpInfo}");
        }

        void Update()
        {
            if (!IsActive) return;
            // MaxStrength 로 hard clamp — Overload 로 fire 가 invincible 해지지 않도록.
            _strength = Mathf.Min(MaxStrength, _strength + GrowthRate * Time.deltaTime);
            ApplyVisual();
        }

        public void ApplyDamage(float amount)
        {
            if (!IsActive) return;
            _strength -= amount;
            // 매 30 프레임 (~0.5 초) 마다 한 번씩만 로그 — 스팸 방지.
            if (Time.frameCount % 30 == 0)
            {
                Debug.Log($"[FirefightFire {name}] taking damage → strength = {_strength:F2}");
            }
            ApplyVisual();
        }

        void LateUpdate()
        {
            if (!IsActive) return;
            if (_strength <= 0f)
            {
                _strength = 0f;
                IsActive = false;
                ApplyVisual();
                if (FireParticles != null)
                    FireParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (EmberParticles != null)
                    EmberParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (FireLight != null) FireLight.enabled = false;
                StopFireSound();
                OnExtinguished?.Invoke();
                Debug.Log($"[FirefightFire {name}] EXTINGUISHED!");
                gameObject.SetActive(false);
            }
        }

        void ApplyVisual()
        {
            float vis = Mathf.Clamp01(_strength / MaxStrength);
            // 최소 보장값 — strength 가 낮아도 메인 불꽃이 항상 눈에 띄게 보이도록.
            // (Ember 와 달리 메인 불꽃은 lifetime 동안 alpha 페이드인 + 위로 솟구치므로
            //  emission/size 가 너무 낮으면 발원지에서 거의 안 보임.)
            float flameVis = Mathf.Max(0.35f, vis);

            if (FireParticles != null && DriveFlameEmissionAndSize)
            {
                var emission = FireParticles.emission;
                var main = FireParticles.main;
                emission.rateOverTime = MaxEmissionRate * flameVis;
                main.startSize = Mathf.Lerp(MinStartSize, MaxStartSize, flameVis);
            }

            if (EmberParticles != null)
            {
                var emEmission = EmberParticles.emission;
                emEmission.rateOverTime = MaxEmberRate * vis;
            }

            if (FireLight != null && IsActive)
            {
                // Perlin noise + sin 합성으로 자연스러운 화염 깜박임.
                float t = Time.time * LightFlickerSpeed;
                float perlin = Mathf.PerlinNoise(_flickerSeed + t * 0.18f, 0f); // 0..1
                float flicker = 0.65f + 0.35f * perlin + 0.12f * Mathf.Sin(t);
                FireLight.intensity = MaxLightIntensity * vis * Mathf.Clamp(flicker, 0.35f, 1.35f);
                FireLight.range = Mathf.Lerp(LightRange * 0.4f, LightRange, vis);
            }
        }

        // --- 런타임 자동 보강: 기존 씬에 이미 배치된 옛 Fire 도 더 위협적으로 ---

        /// <summary>
        /// FireParticles 의 colorOverLifetime / noise / main 모듈을 위협적 톤으로 덮어쓴다.
        /// AutoEnhanceFlames = false 면 호출되지 않으므로 인스펙터에서 직접 손본 그라데이션을 보존 가능.
        /// </summary>
        void EnhanceFlameVisuals()
        {
            if (FireParticles == null) return;

            // 색: 흰열 코어 → 진한 적색 → 그을음.
            // 시작 알파를 0이 아니라 0.4로 — 입자가 spawn 되자마자 보이게 (페이드인 too 느리면 발원지가 텅 빈다).
            var color = FireParticles.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.4f, 1.1f, 0.55f), 0f),    // 흰열
                    new GradientColorKey(new Color(1.0f, 0.45f, 0.08f), 0.35f), // 핫 오렌지
                    new GradientColorKey(new Color(0.7f, 0.12f, 0.03f), 0.75f), // 진한 적
                    new GradientColorKey(new Color(0.12f, 0.04f, 0.02f), 1f),   // 그을음
                },
                new[]
                {
                    new GradientAlphaKey(0.4f, 0f),    // 시작부터 어느 정도 진하게
                    new GradientAlphaKey(1.0f, 0.2f),  // 빠르게 풀 알파
                    new GradientAlphaKey(0.9f, 0.7f),  // 길게 유지
                    new GradientAlphaKey(0f, 1f),      // 끝에서만 페이드 아웃
                });
            color.color = grad;

            // 난기류 — 불꽃이 일정하지 않게 흔들림.
            var noise = FireParticles.noise;
            noise.enabled = true;
            noise.strength = TurbulenceStrength;
            noise.frequency = 1.2f;
            noise.scrollSpeed = 1.5f;
            noise.damping = true;
            noise.octaveCount = 2;

            // 메인 모듈 — 발원지 근처에서 충분히 보이도록 속도/중력 약화.
            // 원래는 (1.6, 3.0) + gravity -0.55 였는데 lifetime 0.7s 동안 3m 까지 솟구쳐
            // 발원지가 거의 비어 있었음. 천천히 솟구치되 충분한 lifetime 유지.
            var main = FireParticles.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.gravityModifier = -0.35f;
            main.maxParticles = Mathf.Max(main.maxParticles, 400);

            // 모듈 변경 후 Play 명시 — 모듈 수정 직후 일시 정지되는 케이스 방어.
            if (!FireParticles.isPlaying) FireParticles.Play(true);
        }

        void EnsureSupplementalVisuals()
        {
            // 깜박이는 점광
            if (FireLight == null)
            {
                var go = new GameObject("FireLight");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                FireLight = go.AddComponent<Light>();
                FireLight.type = LightType.Point;
                FireLight.color = new Color(1f, 0.55f, 0.18f);
                FireLight.range = LightRange;
                FireLight.intensity = 0f;
                FireLight.shadows = LightShadows.None; // Quest 성능 — 그림자 OFF
                FireLight.bounceIntensity = 0f;
                FireLight.renderMode = LightRenderMode.Auto;
            }

            // Ember 스파크 — 위로 튀어오르는 작은 입자.
            if (EmberParticles == null && FireParticles != null)
            {
                var go = new GameObject("Embers");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                var ps = go.AddComponent<ParticleSystem>();
                ConfigureEmbers(ps);
                EmberParticles = ps;
            }
        }

        void ConfigureEmbers(ParticleSystem ps)
        {
            // ParticleSystem은 AddComponent 직후 자동 재생 상태일 수 있다.
            // 재생 중에는 main.duration을 못 바꾸므로 반드시 먼저 Stop+Clear.
            if (ps.isPlaying || ps.particleCount > 0)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 2f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.04f);
            main.startColor = new Color(1.2f, 0.7f, 0.2f, 1f);
            main.maxParticles = 120;
            main.gravityModifier = -0.7f; // 위로 솟음
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;

            var emission = ps.emission;
            emission.rateOverTime = 0f; // ApplyVisual 가 매 프레임 갱신

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.06f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.4f, 0.9f, 0.3f), 0f),
                    new GradientColorKey(new Color(1f, 0.4f, 0.1f), 0.55f),
                    new GradientColorKey(new Color(0.3f, 0.08f, 0.03f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = grad;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 1f);
            sizeCurve.AddKey(1f, 0.3f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 1.5f;
            noise.scrollSpeed = 1.0f;

            // 메인 불꽃과 동일 머티리얼 공유 — 별도 import 없이 동일한 입자 셰이더 사용.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var srcRenderer = FireParticles != null ? FireParticles.GetComponent<ParticleSystemRenderer>() : null;
            if (renderer != null && srcRenderer != null)
                renderer.sharedMaterial = srcRenderer.sharedMaterial;

            // 설정 끝 — 다시 재생 (Stop했었기 때문).
            ps.Play(true);
        }

        void StartFireSound()
        {
            if (string.IsNullOrEmpty(fireSoundName) || AudioManager.Instance == null) return;
            float delay = Random.Range(0f, maxStartDelay);
            _fireSound = AudioManager.Instance.PlaySoundAt(
                fireSoundName, transform.position, delay: delay, isLoop: true);
        }

        void StopFireSound()
        {
            if (_fireSound == null || AudioManager.Instance == null) return;
            AudioManager.Instance.StopLoopSound(_fireSound);
            _fireSound = null;
        }
    }
}
