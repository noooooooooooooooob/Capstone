using UnityEngine;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 뱀 — 사인파 슬리더 이동. 절연되지 않은 손이 닿으면 감전 피드백(햅틱·일시 비활성),
    /// 장갑이 부착된 손에만 잡힌다.
    /// </summary>
    public class SnakeCreature : ZooCreature
    {
        [Header("Snake")]
        [Tooltip("진행 방향에 직교한 좌우 사인파 진폭(m).")]
        [SerializeField] float slitherAmplitude = 0.15f;

        [Tooltip("사인파 주파수(Hz).")]
        [SerializeField] float slitherFrequency = 1.2f;

        [Tooltip("진행 방향 재선택 주기(s).")]
        [SerializeField] float retargetInterval = 2.5f;

        [Header("Shock VFX")]
        [Tooltip("감전 시 손 위치에서 burst 되는 ParticleSystem. World simulation 권장. " +
                 "비워두면 자동으로 씬에서 'ShockEmitter' 라는 GameObject 의 ParticleSystem 을 찾는다.")]
        [SerializeField] ParticleSystem shockEmitter;

        [Tooltip("한 번 감전 시 emit 할 파티클 수.")]
        [SerializeField] int shockParticles = 32;

        Vector3 _forward = Vector3.forward;
        float _retargetTimer;
        float _spawnTime;

        protected override void Start()
        {
            base.Start();
            _forward = transform.forward;
            _spawnTime = Time.time;
        }

        public override bool CanBeCapturedBy(Transform captor)
        {
            if (captor == null) return false;
            var insulation = captor.GetComponentInParent<HandInsulation>();
            return insulation != null && insulation.IsInsulated;
        }

        protected override void TickAI(float dt)
        {
            _retargetTimer -= dt;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = retargetInterval;
                _forward = Quaternion.Euler(0f, Random.Range(-90f, 90f), 0f) * _forward;
                _forward.y = 0f;
                if (_forward.sqrMagnitude < 0.01f) _forward = Vector3.forward;
                _forward.Normalize();
            }

            // wander 중심에서 너무 멀어지면 중심 쪽으로 진행 방향 조정
            Vector3 toCenter = GetWanderCenter() - transform.position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > wanderRadius * wanderRadius)
                _forward = Vector3.Slerp(_forward, toCenter.normalized, 0.3f);

            float speed = (State == CreatureState.Fleeing) ? moveSpeed * 1.4f : moveSpeed;
            transform.position += _forward * speed * dt;

            float t = Time.time - _spawnTime;
            Quaternion baseRot = Quaternion.LookRotation(_forward, Vector3.up);
            float yawWobble = Mathf.Sin(t * slitherFrequency * Mathf.PI * 2f) * 12f;
            transform.rotation = baseRot * Quaternion.Euler(0f, yawWobble, 0f);

            // slitherAmplitude 는 시각용 — 현재는 yaw wobble 로만 표현, 추후 메시 자식 시프트로 확장 가능
            _ = slitherAmplitude;

            if (FindNearestThreat(out _)) State = CreatureState.Fleeing;
            else                          State = CreatureState.Wander;
        }

        /// <summary>비절연 손이 닿았을 때 외부에서 호출. 손 측에 햅틱/락아웃을 발생시킨다.</summary>
        public void OnElectrocute(Transform hand)
        {
            var insulation = hand != null ? hand.GetComponentInParent<HandInsulation>() : null;
            if (insulation != null) insulation.OnShock();
            EmitShock(hand != null ? hand.position : transform.position);
        }

        void EmitShock(Vector3 worldPos)
        {
            // 인스펙터에서 안 채워졌으면 씬에서 1회 검색해 캐싱.
            if (shockEmitter == null)
            {
                var go = GameObject.Find("ShockEmitter");
                if (go != null) shockEmitter = go.GetComponent<ParticleSystem>();
            }
            if (shockEmitter == null) return;
            var p = new ParticleSystem.EmitParams { position = worldPos };
            shockEmitter.Emit(p, shockParticles);
        }
    }
}
