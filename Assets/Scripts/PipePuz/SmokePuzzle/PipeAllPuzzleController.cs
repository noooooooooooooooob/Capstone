using UnityEngine;
using UnityEngine.Events;
using PipePuz.MiniGame2;

namespace PipePuz.SmokePuzzle
{
    /// <summary>
    /// PipeAll 안의 "Radiator + PipeMiniGame2" 복합 퍼즐 매니저.
    ///
    /// 매 프레임 smoke (0~1) 를 갱신:
    ///   if (MiniGameBoard.IsSolved):
    ///       smoke → 0 (즉시), 더 이상 갱신 없음
    ///   else:
    ///       smoke += (RecoveryRate - SuppressionPerDegPerSec * Wheel.CurrentCloseDegPerSec) * dt
    ///       clamp [0,1]
    ///
    /// 즉:
    /// - 사용자가 휠을 안 잡고 있으면 RecoveryRate 만큼 매초 회복(연기 늘어남)
    /// - 휠을 닫힘 방향으로 빠르게 돌리면 그 속도 × 계수 만큼 매초 감소(연기 줄어듦)
    /// - 회전 속도와 회복률이 균형 → 어떤 속도 이상 돌리면 점차 줄어드는 형태
    /// - MiniGame2 가 해결되면 0 으로 강제 + 매니저는 그 후 갱신 안 함
    /// </summary>
    public class PipeAllPuzzleController : MonoBehaviour
    {
        [Header("Refs")]
        public SuppressionWheel Wheel;
        public PipePuz.SmokeController Smoke;
        public PipeMiniGame2Board MiniGameBoard;

        [Header("Tuning")]
        [Tooltip("자연 회복률 (초당 smoke 증가). 휠을 안 돌리면 0→1까지 1/RecoveryRate 초가 걸린다.")]
        public float RecoveryRate = 0.18f;

        [Tooltip("닫힘 회전 속도 1°/s 당 초당 smoke 감소량. " +
                 "예: 0.0015 면 100°/s 돌리는 동안 0.15/s 감소.")]
        public float SuppressionPerDegPerSec = 0.0015f;

        [Header("Initial / Limits")]
        [Range(0f, 1f)]
        [Tooltip("씬 시작 시 smoke 초기값. MaxSmoke 보다 크면 MaxSmoke 로 클램프된다.")]
        public float InitialSmoke = 0.85f;

        [Range(0f, 1f)]
        [Tooltip("연기 강도의 최대 캡. 자연 회복으로도 이 값을 넘지 않는다 (시야가 완전히 가려지는 것 방지).")]
        public float MaxSmoke = 0.85f;

        [Header("Events")]
        public UnityEvent<float> OnSmokeChanged;
        public UnityEvent OnSolved;

        [Header("Read-only state")]
        [SerializeField, Range(0f, 1f)] float _smoke;
        public float CurrentSmoke => _smoke;

        bool _solvedFired;

        /// <summary>이 클라이언트의 보드가 로컬 기준으로 풀렸는지.</summary>
        public bool LocalBoardSolved => MiniGameBoard != null && MiniGameBoard.IsSolved;

        /// <summary>
        /// 네트워크로 전파된 "풀림" 래치. 다른 플레이어가 먼저 풀어서 RPC 가 들어오면 true 가 된다.
        /// SmokeSolveNetworkSync 가 모든 피어에서 이 값을 세팅 → 누가 풀든 양쪽 다 연기가 사라진다.
        /// (싱글/오프라인에서는 항상 false 이고 LocalBoardSolved 로만 판정.)
        /// </summary>
        [System.NonSerialized] public bool ExternalSolvedLatch;

        void Awake()
        {
            // SmokeController.Awake 가 Intensity=0(default)으로 Apply 하면서 ParticleSystem 을 Stop 시키는
            // 1프레임 공백을 막기 위해 가능한 가장 일찍 InitialSmoke 적용한다.
            _smoke = Mathf.Clamp(InitialSmoke, 0f, MaxSmoke);
            ApplySmoke();
        }

        void OnEnable()
        {
            // 도메인 리로드 / 재활성화 직후에도 시작부터 연기가 보이도록.
            _smoke = Mathf.Clamp(InitialSmoke, 0f, MaxSmoke);
            ApplySmoke();
            OnSmokeChanged?.Invoke(_smoke);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // 로컬에서 풀렸거나, 다른 플레이어가 풀어 네트워크 래치가 켜졌으면 풀린 것으로 처리.
            bool solved = LocalBoardSolved || ExternalSolvedLatch;
            if (solved)
            {
                if (_smoke > 0f)
                {
                    _smoke = 0f;
                    ApplySmoke();
                    OnSmokeChanged?.Invoke(_smoke);
                }
                if (!_solvedFired)
                {
                    _solvedFired = true;
                    OnSolved?.Invoke();
                    Debug.Log("[PipeAllPuzzle] MiniGame2 Solved → smoke locked off");
                }
                return;
            }

            float closeRate = Wheel != null ? Wheel.CurrentCloseDegPerSec : 0f;
            float delta = (RecoveryRate - SuppressionPerDegPerSec * closeRate) * dt;
            // 자연 회복은 MaxSmoke 까지만, 감소는 0 까지.
            float next = Mathf.Clamp(_smoke + delta, 0f, MaxSmoke);

            if (!Mathf.Approximately(next, _smoke))
            {
                _smoke = next;
                ApplySmoke();
                OnSmokeChanged?.Invoke(_smoke);
            }
        }

        void ApplySmoke()
        {
            if (Smoke != null) Smoke.SetIntensity(_smoke);
        }
    }
}
