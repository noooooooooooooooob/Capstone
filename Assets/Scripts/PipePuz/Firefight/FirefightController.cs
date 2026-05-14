using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.Firefight
{
    /// <summary>
    /// Firefight 퍼즐의 메인 컨트롤러.
    /// - CurrentPressure 를 관리. Pump.PumpBoost(amount) 가 호출되면 누적,
    ///   Update 에서 PressureDecayRate 만큼 매초 감쇠.
    /// - Fire 들의 OnExtinguished / OnOverloaded 를 구독해서 게임 상태 판정.
    /// - 모든 Fire 꺼짐 → OnSolved, 어떤 Fire 라도 폭주 → OnFailed.
    /// </summary>
    public class FirefightController : MonoBehaviour
    {
        [Header("Refs")]
        public FirefightPump Pump;
        public FirefightHose Hose;
        public FirefightFire[] Fires;

        [Header("Pressure")]
        [Tooltip("최대 압력값.")]
        public float MaxPressure = 1.0f;

        [Tooltip("초당 압력 감쇠량. 펌프 안 할 때 1.0 → 0 까지 (1/decay) 초.")]
        public float PressureDecayRate = 0.15f;

        [Header("Events")]
        public UnityEvent<float> OnPressureChanged;
        public UnityEvent OnSolved;
        public UnityEvent OnFailed;

        [Header("Read-only state (인스펙터에서 디버깅용)")]
        [SerializeField, Range(0f, 1f)] float _currentPressure;
        public float CurrentPressure { get => _currentPressure; private set => _currentPressure = value; }

        public bool IsSolved { get; private set; }
        public bool IsFailed { get; private set; }

        void Start()
        {
            if (Fires != null)
            {
                for (int i = 0; i < Fires.Length; i++)
                {
                    var f = Fires[i];
                    if (f == null) continue;
                    f.OnExtinguished.AddListener(OnFireExtinguished);
                }
            }
        }

        void OnDestroy()
        {
            if (Fires != null)
            {
                for (int i = 0; i < Fires.Length; i++)
                {
                    var f = Fires[i];
                    if (f == null) continue;
                    f.OnExtinguished.RemoveListener(OnFireExtinguished);
                }
            }
        }

        void Update()
        {
            // 압력 감쇠 — 게임 상태 (IsSolved/IsFailed) 와 무관하게 항상 작동.
            float prev = CurrentPressure;
            CurrentPressure = Mathf.Max(0f, CurrentPressure - PressureDecayRate * Time.deltaTime);
            if (!Mathf.Approximately(prev, CurrentPressure))
                OnPressureChanged?.Invoke(CurrentPressure);
        }

        public void PumpBoost(float amount)
        {
            // 펌프는 게임 상태와 무관하게 항상 작동 (실패 후에도 펌핑 가능).
            CurrentPressure = Mathf.Min(MaxPressure, CurrentPressure + amount);
            OnPressureChanged?.Invoke(CurrentPressure);
            Debug.Log($"[FirefightController] PumpBoost(+{amount:F2}) → pressure = {CurrentPressure:F2}");
        }

        void OnFireExtinguished()
        {
            if (IsSolved || IsFailed) return;
            if (AllFiresOut())
            {
                IsSolved = true;
                OnSolved?.Invoke();
                Debug.Log("[Firefight] All fires extinguished — Solved!");
            }
        }

        // Fire 의 Overload 메커니즘은 제거됨. OnFailed UnityEvent 는 외부에서 다른 조건으로 hook 가능
        // (예: 타이머 초과). 자동으로는 더 이상 fire 되지 않는다.

        bool AllFiresOut()
        {
            if (Fires == null) return false;
            for (int i = 0; i < Fires.Length; i++)
            {
                var f = Fires[i];
                if (f != null && f.IsActive) return false;
            }
            return true;
        }
    }
}
