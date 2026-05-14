using UnityEngine;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 게 — 무거운 Rigidbody. 잡히지 않고 손/도구의 PhysX 임팩트로 밀린다.
    /// 강한 임펄스를 받으면 셸 모드 토글: 정지 + AI off + 자식 시각 교체 + 마찰 ↑.
    /// 셸 모드일 때 LizardEscapeHole 트리거 안에 들어가 있으면 hole.Blocked = true 가 된다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CrabCreature : ZooCreature
    {
        [Header("Crab")]
        [Tooltip("셸 모드 토글을 일으키는 충돌 임펄스 임계값(Ns). 클수록 잘 안 토글된다.")]
        [SerializeField] float shellImpulseThreshold = 4.0f;

        [Tooltip("셸 모드 진입/해제 후 잠시 임펄스를 무시하는 시간(s) — 연속 토글 방지.")]
        [SerializeField] float toggleCooldown = 0.5f;

        [Tooltip("셸 모드 일 때 활성화할 자식. 비셸 모델은 자동으로 반대 상태가 된다.")]
        [SerializeField] GameObject shellModel;
        [SerializeField] GameObject normalModel;

        [Tooltip("셸 모드 마찰계수(드래그).")]
        [SerializeField] float shellDrag = 6.0f;
        [SerializeField] float normalDrag = 0.5f;

        [SerializeField] bool inShell;
        public bool InShell => inShell;

        float _cooldown;

        protected override void Start()
        {
            base.Start();
            ApplyShellVisuals();
        }

        public override bool CanBeCapturedBy(Transform captor)
        {
            // 손/도구로 "잡지" 못한다. 케이지 진입은 CreatureCage 가 처리.
            return false;
        }

        protected override void TickAI(float dt)
        {
            _cooldown = Mathf.Max(0f, _cooldown - dt);

            if (inShell)
            {
                // 셸 모드는 자체 이동 없음(외부 임펄스로만 움직임).
                State = CreatureState.Stunned;
                return;
            }

            if (FindNearestThreat(out var threat))
            {
                Vector3 away = transform.position - threat;
                away.y = 0f;
                if (away.sqrMagnitude > 0.001f)
                {
                    Vector3 target = transform.position + away.normalized * wanderRadius;
                    MoveTowards(target, moveSpeed, dt);
                }
                State = CreatureState.Fleeing;
            }
            else
            {
                State = CreatureState.Wander;
            }
        }

        void OnCollisionEnter(Collision col)
        {
            if (_cooldown > 0f) return;
            if (col.impulse.magnitude < shellImpulseThreshold) return;
            ToggleShell();
        }

        public void ToggleShell()
        {
            inShell = !inShell;
            _cooldown = toggleCooldown;
            if (_rb != null)
            {
                _rb.linearDamping = inShell ? shellDrag : normalDrag;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            ApplyShellVisuals();
        }

        void ApplyShellVisuals()
        {
            if (shellModel != null && shellModel.activeSelf != inShell) shellModel.SetActive(inShell);
            if (normalModel != null && normalModel.activeSelf == inShell) normalModel.SetActive(!inShell);
        }
    }
}
