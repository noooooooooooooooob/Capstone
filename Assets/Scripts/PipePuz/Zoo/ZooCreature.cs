using UnityEngine;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 4종 생명체의 공통 베이스. 1차 테스트 단계에서는 단일 플레이어 가정 — MonoBehaviour.
    /// 멀티플레이 전환 시 NetworkBehaviour 로 승급하고 [Networked] State 등을 도입할 예정.
    /// </summary>
    public abstract class ZooCreature : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("이 생명체의 종류. CreatureCage 의 AcceptedKind 와 일치할 때만 정답 처리.")]
        [SerializeField] CreatureKind kind;
        public CreatureKind Kind => kind;

        [Header("Wiring")]
        [Tooltip("이 생명체가 속한 퍼즐 컨트롤러. 비워두면 씬에서 자동 검색.")]
        [SerializeField] protected ZooPuzzleController controller;

        [Tooltip("플레이어 위협 감지 반경. 이 안에 들어온 가까운 위협 손/도구를 Fleeing 트리거로 사용.")]
        [SerializeField] protected float threatRadius = 0.6f;

        [Tooltip("Wander 시 무작위 이동 시도 반경(평면 m).")]
        [SerializeField] protected float wanderRadius = 1.5f;

        [Tooltip("기본 이동 속도(m/s).")]
        [SerializeField] protected float moveSpeed = 1.0f;

        [Tooltip("이 생명체의 wander 가 머무를 중심. 비워두면 Awake 의 자기 위치를 사용.")]
        [SerializeField] protected Transform wanderCenter;

        [Header("State (read-only)")]
        [SerializeField] protected CreatureState state = CreatureState.Wander;
        public CreatureState State { get => state; protected set => state = value; }

        /// <summary>현재 이 생명체를 잡고 있는 도구/손의 트랜스폼. 잡혀있지 않으면 null.</summary>
        protected Transform _captor;

        protected Rigidbody _rb;
        protected Vector3 _homePosition;

        public bool IsCaptured => state == CreatureState.Captured;
        public bool IsCaged    => state == CreatureState.Caged;

        public void SetKind(CreatureKind k) => kind = k;

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _homePosition = transform.position;
            if (controller == null) controller = FindFirstObjectByType<ZooPuzzleController>();
        }

        protected virtual void Start()
        {
            if (state == CreatureState.Idle) state = CreatureState.Wander;
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            switch (state)
            {
                case CreatureState.Wander:
                case CreatureState.Fleeing:
                case CreatureState.Stunned:
                    TickAI(dt);
                    break;
                case CreatureState.Captured:
                    FollowCaptor();
                    break;
                // Idle / Caged 는 아무것도 안 함
            }
        }

        protected abstract void TickAI(float dt);

        protected virtual void FollowCaptor()
        {
            if (_captor == null) return;
            transform.position = _captor.position;
            transform.rotation = _captor.rotation;
        }

        /// <summary>외부(도구/손)에서 이 생명체를 잡았을 때 호출.</summary>
        public virtual void TryCapture(Transform captor)
        {
            if (!CanBeCapturedBy(captor)) return;

            _captor = captor;
            state = CreatureState.Captured;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }
            OnCaptured(captor);
        }

        public abstract bool CanBeCapturedBy(Transform captor);

        protected virtual void OnCaptured(Transform captor) { }

        public virtual void Release()
        {
            _captor = null;
            if (_rb != null) _rb.isKinematic = false;
            state = CreatureState.Wander;
        }

        /// <summary>케이지에 안착되었을 때 호출.</summary>
        public virtual void NotifyCaged(CreatureCage cage)
        {
            transform.SetParent(cage.transform, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            _captor = null;
            if (_rb != null) _rb.isKinematic = true;
            state = CreatureState.Caged;
        }

        // ---- AI 공용 유틸 -------------------------------------------------

        protected Vector3 GetWanderCenter() => wanderCenter != null ? wanderCenter.position : _homePosition;

        protected Vector3 PickWanderTarget()
        {
            Vector2 r = Random.insideUnitCircle * wanderRadius;
            Vector3 c = GetWanderCenter();
            return new Vector3(c.x + r.x, transform.position.y, c.z + r.y);
        }

        protected void MoveTowards(Vector3 target, float speed, float dt)
        {
            Vector3 to = target - transform.position;
            to.y = 0f;
            float step = speed * dt;
            if (to.sqrMagnitude <= step * step)
            {
                transform.position = new Vector3(target.x, transform.position.y, target.z);
                return;
            }
            Vector3 dir = to.normalized;
            transform.position += dir * step;
            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 8f * dt);
        }

        /// <summary>가장 가까운 위협(태그 "PlayerHand" 또는 카메라) 트랜스폼 위치 반환.</summary>
        protected bool FindNearestThreat(out Vector3 threatPos)
        {
            threatPos = Vector3.zero;
            float bestSq = threatRadius * threatRadius;
            bool found = false;

            // 1) PlayerHand 태그
            var hands = GameObject.FindGameObjectsWithTag("PlayerHand");
            for (int i = 0; i < hands.Length; i++)
            {
                var t = hands[i].transform;
                float sq = (t.position - transform.position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; threatPos = t.position; found = true; }
            }

            // 2) 메인 카메라 (단독 테스트 환경에서 손 태그가 없을 때 머리 위치를 위협으로 사용)
            if (!found && Camera.main != null)
            {
                float sq = (Camera.main.transform.position - transform.position).sqrMagnitude;
                if (sq < bestSq) { threatPos = Camera.main.transform.position; found = true; }
            }
            return found;
        }
    }
}
