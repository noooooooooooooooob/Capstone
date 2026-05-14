using UnityEngine;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 도마뱀 — 지면을 빠르게 기어다님. 손으로 직접 잡힌다.
    /// LizardEscapeHole 가 Blocked 상태이면 fleeSpeed 가 둔화되어 잡기 쉬워진다.
    /// </summary>
    public class LizardCreature : ZooCreature
    {
        [Header("Lizard")]
        [Tooltip("도주 시 속도 배율 (게의 셸이 hole 을 막지 않은 평소 상태).")]
        [SerializeField] float fleeMultiplier = 3.0f;

        [Tooltip("도주 경로 상에 놓인 hole. Blocked 상태이면 도주 속도가 slowMultiplier 로 감쇠.")]
        [SerializeField] LizardEscapeHole hole;

        [Tooltip("Hole 이 막혔을 때 적용되는 속도 배율(0.2 = 80% 느림).")]
        [SerializeField] float slowMultiplier = 0.2f;

        [Tooltip("Wander 목표 재선택 주기(s).")]
        [SerializeField] float retargetInterval = 1.2f;

        Vector3 _target;
        float _retargetTimer;

        protected override void Start()
        {
            base.Start();
            _target = transform.position;
        }

        public override bool CanBeCapturedBy(Transform captor)
        {
            if (captor == null) return false;
            // 손(HandInsulation 보유 트랜스폼) 이면 OK. 도구로는 잡을 수 없음.
            return captor.GetComponentInParent<HandInsulation>() != null;
        }

        protected override void TickAI(float dt)
        {
            _retargetTimer -= dt;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = retargetInterval;
                _target = PickWanderTarget();
            }

            float speed = moveSpeed;
            bool blocked = hole != null && hole.Blocked;

            if (FindNearestThreat(out var threat))
            {
                Vector3 away = transform.position - threat;
                away.y = 0f;
                if (away.sqrMagnitude > 0.001f)
                {
                    _target = transform.position + away.normalized * wanderRadius;
                    _target.y = transform.position.y;
                }
                State = CreatureState.Fleeing;
                speed *= blocked ? slowMultiplier : fleeMultiplier;
            }
            else
            {
                State = CreatureState.Wander;
            }

            MoveTowards(_target, speed, dt);
        }
    }
}
