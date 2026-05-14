using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 케이지 — 트리거 콜라이더 안에 정답 생명체가 들어오면 카운트 누적.
    /// 1차 테스트에서는 ZooHintTable 없이 AcceptedKind 를 인스펙터에서 직접 지정.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CreatureCage : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] CageId id;
        public CageId Id => id;

        [Tooltip("이 케이지가 받아야 하는 생명체 종.")]
        [SerializeField] CreatureKind acceptedKind;
        public CreatureKind AcceptedKind { get => acceptedKind; set => acceptedKind = value; }

        [Header("Wiring")]
        [SerializeField] ZooPuzzleController controller;

        [Header("Events")]
        public UnityEvent OnAccept;
        public UnityEvent OnReject;

        bool _caged; // 한 번 정답을 받으면 더 이상 트리거 받지 않음

        void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            if (controller == null) controller = FindFirstObjectByType<ZooPuzzleController>();
        }

        void OnTriggerEnter(Collider other)
        {
            if (_caged) return;
            var c = other.GetComponentInParent<ZooCreature>();
            if (c == null) return;

            // 게는 InShell == true 일 때만 케이지 진입 허용. 다른 생명체는 Captured 상태에서 진입.
            if (c is CrabCreature crab)
            {
                if (!crab.InShell) return;
            }
            else
            {
                if (c.State != CreatureState.Captured) return;
            }

            bool ok = (c.Kind == acceptedKind);

            if (ok)
            {
                c.NotifyCaged(this);
                _caged = true;
                if (controller != null) controller.NotifyCagedOne(c);
                OnAccept?.Invoke();
            }
            else
            {
                OnReject?.Invoke();
                c.Release();
            }
        }
    }
}
