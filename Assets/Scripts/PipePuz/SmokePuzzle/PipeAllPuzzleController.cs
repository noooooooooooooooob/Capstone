using UnityEngine;
using UnityEngine.Events;
using PipePuz.MiniGame2;

namespace PipePuz.SmokePuzzle
{
    /// <summary>
    /// PipeAll 안의 "Radiator + PipeMiniGame2" 복합 퍼즐 매니저.
    ///
    /// 매 프레임 smoke (0~1) 를 갱신:
    ///   if (MiniGameBoard.IsSolved || ExternalSolvedLatch):
    ///       smoke → 0 (즉시 영구), 더 이상 갱신 없음   (별도 해결 경로 — 유지)
    ///   else if (Gauge.PointerInRedZone):
    ///       smoke -= SuppressRate * dt    (밸브로 Pointer 를 빨간 영역에 맞추면 연기가 사라짐)
    ///   else:
    ///       smoke += RecoveryRate * dt    (영역을 벗어나면 다시 차오름 — "유지해야 멈춤")
    ///
    /// 즉 새 메커니즘:
    /// - 사용자가 Valve 를 돌려 SmokeGauge 의 Pointer 를 특정 빨간 영역에 위치시키면 연기가 줄어든다.
    /// - Pointer 가 영역을 벗어나면 연기가 다시 회복된다(영구 정지 아님).
    /// - MiniGame2 board 해결은 독립적인 영구 해제 경로로 남아 있다(완료 이벤트용).
    /// </summary>
    public class PipeAllPuzzleController : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Valve(SuppressionWheel). 회전은 SmokeGauge.Pointer 를 구동한다. (smoke 억제는 Gauge 가 판정)")]
        public SuppressionWheel Wheel;
        public PipePuz.SmokeController Smoke;
        public PipeMiniGame2Board MiniGameBoard;

        [Tooltip("Pointer 가 빨간 영역에 있는지 판정하는 게이지. 비우면 자식에서 자동 검출.")]
        public SmokeGauge Gauge;

        [Header("Tuning")]
        [Tooltip("자연 회복률 (초당 smoke 증가). Pointer 가 빨간 영역 밖이면 이 속도로 차오른다.")]
        public float RecoveryRate = 0.18f;

        [Tooltip("Pointer 가 빨간 영역 안에 있을 때 초당 smoke 감소량. 클수록 빨리 사라진다.")]
        public float SuppressRate = 0.6f;

        [Tooltip("[레거시] 예전 속도기반 억제 계수 — 새 메커니즘에서는 사용하지 않음(필드만 유지).")]
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
            // Gauge 자동 연결 — 보통 자식에 SmokeGauge 가 하나 있다.
            if (Gauge == null) Gauge = GetComponentInChildren<SmokeGauge>(true);

            // PathLine 힌트는 시작 시 숨김 — Pointer 가 빨간 영역에 들어와야 보인다.
            ApplyPathLineVisible(false);

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

            // Pointer 가 빨간 영역 안에 있는지 — "연기 억제 + PathLine 힌트 표시" 의 공통 트리거.
            // 네트워크: 밸브 각도가 SuppressionWheelNetworkSync 로 동기화되고, 그 직후 Render 에서
            //   SmokeGauge.RefreshFromValve 가 호출돼 PointerInRedZone 이 모든 피어에서 갱신된다.
            //   → 각 피어가 같은 값을 로컬 판정하므로 PathLine 가시성도 네트워크에서 일관된다.
            bool inRedZone = Gauge != null && Gauge.PointerInRedZone;

            // PathLine 힌트: 빨간 영역에 Pointer 있을 때만 보이게. (풀림 상태와 무관하게 매 프레임 반영)
            ApplyPathLineVisible(inRedZone);

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

            // 새 메커니즘: Pointer 가 빨간 영역 안이면 감소, 밖이면 회복("유지해야 멈춤").
            float delta = inRedZone ? (-SuppressRate * dt) : (RecoveryRate * dt);
            // 회복은 MaxSmoke 까지만, 감소는 0 까지.
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

        bool _pathLineVisible = true; // 시작값을 true 로 두어 Awake 의 첫 false 적용이 반드시 반영되게.

        /// <summary>MiniGame2 보드의 PathLine(LineRenderer) 표시/숨김. 값이 바뀔 때만 적용.</summary>
        void ApplyPathLineVisible(bool visible)
        {
            if (_pathLineVisible == visible) return;
            _pathLineVisible = visible;
            if (MiniGameBoard != null && MiniGameBoard.PathLine != null)
                MiniGameBoard.PathLine.enabled = visible;
        }
    }
}
