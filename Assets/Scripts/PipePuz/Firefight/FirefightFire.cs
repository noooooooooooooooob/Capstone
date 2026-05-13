using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.Firefight
{
    /// <summary>
    /// 한 곳의 불. Strength 0..MaxStrength 보유.
    /// Update 에서 GrowthRate * dt 만큼 자란다. ApplyDamage(amount) 가 호출되면 그만큼 줄어든다.
    /// LateUpdate 에서 임계값 검사:
    ///   - Strength &lt;= 0 → 꺼짐 (OnExtinguished + 비활성)
    ///   - Strength &gt;= MaxStrength → 폭주 (OnOverloaded, 게임 실패)
    /// 시각은 ParticleSystem 의 emission rate / startSize 를 Strength 비례로 갱신.
    /// </summary>
    public class FirefightFire : MonoBehaviour
    {
        [Header("Refs")]
        public ParticleSystem FireParticles;

        [Header("Tuning")]
        [Tooltip("시작 Strength.")]
        [Range(0f, 1f)]
        public float StartStrength = 0.2f;

        [Tooltip("초당 자라는 양.")]
        public float GrowthRate = 0.08f;

        [Tooltip("폭주 임계값.")]
        public float MaxStrength = 1.0f;

        [Header("Visual tuning")]
        public float MaxEmissionRate = 35f;
        public float MinStartSize = 0.15f;
        public float MaxStartSize = 0.80f;

        [Header("Events")]
        public UnityEvent OnExtinguished;

        [Header("Read-only state")]
        [SerializeField] float _strength;
        public float CurrentStrength => _strength;
        public bool IsActive { get; private set; } = true;

        void Awake()
        {
            _strength = Mathf.Clamp(StartStrength, 0.01f, MaxStrength - 0.01f);
            ApplyVisual();
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
            // Visual 은 LateUpdate 직전 다시 한 번 적용 (Update 와 합쳐서).
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
                if (FireParticles != null) FireParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                OnExtinguished?.Invoke();
                Debug.Log($"[FirefightFire {name}] EXTINGUISHED!");
                gameObject.SetActive(false);
            }
            // Overload (strength >= MaxStrength) 는 더 이상 fire 를 비활성화시키지 않는다.
            // Update 의 clamp 가 strength 를 MaxStrength 로 묶어두므로 시각만 최대치로 보일 뿐
            // ApplyDamage 는 계속 작동 → 언제든 끌 수 있음.
        }

        void ApplyVisual()
        {
            if (FireParticles == null) return;
            float vis = Mathf.Clamp01(_strength / MaxStrength);
            var emission = FireParticles.emission;
            var main = FireParticles.main;
            emission.rateOverTime = MaxEmissionRate * vis;
            main.startSize = Mathf.Lerp(MinStartSize, MaxStartSize, vis);
        }
    }
}
