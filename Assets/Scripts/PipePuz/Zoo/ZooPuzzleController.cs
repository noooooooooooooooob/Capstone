using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.Zoo
{
    /// <summary>
    /// Zoo 퍼즐의 전역 컨트롤러. 1차 테스트 — 단일 플레이어 / 네트워크 없음.
    /// 4 마리 케이지 카운트 누적, 모두 차면 OnSolved 발행.
    /// </summary>
    public class ZooPuzzleController : MonoBehaviour
    {
        [Header("Events")]
        public UnityEvent<int> OnProgress;   // arg: 현재 caged count (0..4)
        public UnityEvent OnSolved;

        [SerializeField] int totalToCage = 4;

        int _cagedCount;
        public int CagedCount => _cagedCount;
        public bool IsSolved { get; private set; }

        /// <summary>CreatureCage 가 호출. 카운트 누적.</summary>
        public void NotifyCagedOne(ZooCreature c)
        {
            if (IsSolved) return;
            _cagedCount = Mathf.Min(totalToCage, _cagedCount + 1);
            OnProgress?.Invoke(_cagedCount);
            Debug.Log($"[Zoo] {c.Kind} caged. Progress {_cagedCount}/{totalToCage}");

            if (_cagedCount >= totalToCage)
            {
                IsSolved = true;
                OnSolved?.Invoke();
                Debug.Log("[Zoo] Solved!");
            }
        }
    }
}
