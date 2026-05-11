using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.EMStabilizer
{
    /// <summary>
    /// EM Stabilizer 퍼즐 본체 컨트롤러.
    ///
    /// 매 프레임 조건을 평가:
    ///   angleOk    = |handle.CurrentAngle - TargetAngle| < AngleTolerance
    ///   slider1Ok  = |slider1.Value - TargetSlider1| < SliderTolerance
    ///   slider2Ok  = |slider2.Value - TargetSlider2| < SliderTolerance
    ///   handleHeld = handle.IsHeld
    ///
    /// 모든 조건이 동시에 충족되면 LockProgress 가 (dt / LockDuration) 만큼 증가,
    /// 하나라도 깨지면 (dt / DecayDuration) 만큼 감소. [0,1] 클램프.
    /// LockProgress >= 1.0 시 OnSolved 한 번 발행.
    ///
    /// 시각 갱신은 <see cref="Tablet"/> 에 위임. TuningQuality(노이즈 감소량) 는 슬라이더 오차 평균으로 계산.
    /// </summary>
    public class EMStabilizerController : MonoBehaviour
    {
        [Header("Refs")]
        public EMHandle Handle;
        public EMSlider Slider1;
        public EMSlider Slider2;
        public EMTablet Tablet;

        [Header("Targets")]
        [Tooltip("핸들이 도달해야 하는 목표 각도(°). 안테나 베이스의 마커 위치도 이 값에 맞춰 둔다.")]
        public float TargetAngle = 30f;

        [Tooltip("Slider1 이 맞춰야 하는 목표 정규화 값(0~1).")]
        [Range(0f, 1f)] public float TargetSlider1 = 0.7f;

        [Tooltip("Slider2 이 맞춰야 하는 목표 정규화 값(0~1).")]
        [Range(0f, 1f)] public float TargetSlider2 = 0.3f;

        [Header("Tolerances")]
        [Tooltip("핸들 각도 허용 오차(°).")]
        public float AngleTolerance = 5f;

        [Tooltip("슬라이더 허용 오차(0~1 스케일).")]
        public float SliderTolerance = 0.08f;

        [Header("Lock Timing")]
        [Tooltip("모든 조건이 만족된 채로 LockProgress 가 0→1 까지 차오르는 데 걸리는 시간(s).")]
        public float LockDuration = 3.0f;

        [Tooltip("조건이 깨졌을 때 LockProgress 가 1→0 까지 감소하는 데 걸리는 시간(s).")]
        public float DecayDuration = 1.5f;

        [Header("Events")]
        public UnityEvent OnAllConditionsMet;
        public UnityEvent OnAnyConditionLost;
        public UnityEvent<float> OnLockProgressChanged;
        public UnityEvent OnSolved;
        public UnityEvent OnReset;

        [Header("Read-only state")]
        [SerializeField, Range(0f, 1f)] float _lockProgress;
        public float LockProgress => _lockProgress;
        public bool IsSolved { get; private set; }

        bool _wasAllOk;

        void Update()
        {
            float dt = Time.deltaTime;

            bool handleHeld = Handle != null && Handle.IsHeld;
            float angle = Handle != null ? Handle.CurrentAngle : 0f;
            float s1 = Slider1 != null ? Slider1.Value : 0f;
            float s2 = Slider2 != null ? Slider2.Value : 0f;

            float angleErr = Mathf.Abs(angle - TargetAngle);
            float s1Err = Mathf.Abs(s1 - TargetSlider1);
            float s2Err = Mathf.Abs(s2 - TargetSlider2);

            bool angleOk = angleErr < AngleTolerance;
            bool slider1Ok = s1Err < SliderTolerance;
            bool slider2Ok = s2Err < SliderTolerance;
            bool allOk = handleHeld && angleOk && slider1Ok && slider2Ok;

            // LockProgress 업데이트
            float prev = _lockProgress;
            if (allOk)
            {
                _lockProgress = Mathf.Min(1f, _lockProgress + dt / Mathf.Max(0.01f, LockDuration));
            }
            else
            {
                _lockProgress = Mathf.Max(0f, _lockProgress - dt / Mathf.Max(0.01f, DecayDuration));
            }

            if (!Mathf.Approximately(prev, _lockProgress))
            {
                OnLockProgressChanged?.Invoke(_lockProgress);
            }

            // 상태 전이 이벤트
            if (allOk && !_wasAllOk) OnAllConditionsMet?.Invoke();
            if (!allOk && _wasAllOk) OnAnyConditionLost?.Invoke();
            _wasAllOk = allOk;

            // 풀이 완료
            if (_lockProgress >= 1f && !IsSolved)
            {
                IsSolved = true;
                OnSolved?.Invoke();
                Debug.Log("[EMStabilizer] Solved!");
            }

            // 태블릿 시각 갱신
            if (Tablet != null)
            {
                // TuningQuality = 슬라이더 오차의 평균 → 1 - mean(err / tolerance, clamp01)
                float q1 = 1f - Mathf.Clamp01(s1Err / Mathf.Max(SliderTolerance, 0.0001f));
                float q2 = 1f - Mathf.Clamp01(s2Err / Mathf.Max(SliderTolerance, 0.0001f));
                float tuningQuality = 0.5f * (q1 + q2);
                Tablet.Tick(tuningQuality, _lockProgress, angleOk, slider1Ok, slider2Ok);
            }
        }

        /// <summary>외부에서 호출 — 퍼즐을 초기 상태로 되돌린다.</summary>
        public void ResetPuzzle()
        {
            _lockProgress = 0f;
            IsSolved = false;
            _wasAllOk = false;
            OnLockProgressChanged?.Invoke(0f);
            OnReset?.Invoke();
        }
    }
}
