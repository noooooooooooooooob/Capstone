using UnityEngine;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 잠자리 — 공중을 Y 보빙 + XZ wander 로 부유. 잠자리채에만 잡힘.
    /// </summary>
    public class DragonflyCreature : ZooCreature
    {
        [Header("Dragonfly")]
        [Tooltip("기본 비행 높이(월드 Y).")]
        [SerializeField] float flightHeight = 1.6f;

        [Tooltip("Y 보빙 진폭(m). 0 이면 보빙 없음.")]
        [SerializeField] float bobAmplitude = 0.15f;

        [Tooltip("Y 보빙 주파수(Hz).")]
        [SerializeField] float bobFrequency = 0.8f;

        [Tooltip("Wander 목표 재선택 주기(s).")]
        [SerializeField] float retargetInterval = 2.0f;

        Vector3 _target;
        float _retargetTimer;
        float _spawnTime;

        protected override void Start()
        {
            base.Start();
            _spawnTime = Time.time;
            var p = transform.position; p.y = flightHeight; transform.position = p;
            _target = transform.position;
        }

        public override bool CanBeCapturedBy(Transform captor)
        {
            if (captor == null) return false;
            return captor.GetComponentInParent<CatchNet>() != null;
        }

        protected override void TickAI(float dt)
        {
            _retargetTimer -= dt;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = retargetInterval;
                _target = PickWanderTarget();
                _target.y = flightHeight;
            }

            if (FindNearestThreat(out var threat))
            {
                Vector3 away = transform.position - threat;
                away.y = 0f;
                if (away.sqrMagnitude > 0.001f)
                {
                    _target = transform.position + away.normalized * wanderRadius;
                    _target.y = flightHeight;
                    State = CreatureState.Fleeing;
                }
            }
            else if (State == CreatureState.Fleeing)
            {
                State = CreatureState.Wander;
            }

            float speed = (State == CreatureState.Fleeing) ? moveSpeed * 1.8f : moveSpeed;
            MoveTowards(_target, speed, dt);

            float y = flightHeight + Mathf.Sin((Time.time - _spawnTime) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
            var p = transform.position; p.y = y; transform.position = p;
        }
    }
}
