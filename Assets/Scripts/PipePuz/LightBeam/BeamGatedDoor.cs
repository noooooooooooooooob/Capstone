using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 양쪽 hinge 문 — 외부 bool 신호(LightBeamReceiver.OnHitChanged 등) 에 따라
    /// LeftPivot / RightPivot 을 각각 LeftOpenAngle / RightOpenAngle 만큼 회전.
    ///
    /// 사용:
    ///   1. LeftPivot, RightPivot Transform 지정 (보통 도어 양 끝 hinge 위치).
    ///      각 pivot 의 자식으로 panel cube 가 한쪽 방향 offset 되어 있어야 함.
    ///   2. Receiver 필드에 LightBeamReceiver 지정 → Awake 에서 자동 구독.
    ///      (UnityEvent persistent listener 보다 더 안정적.)
    ///   3. LeftOpenAngle, RightOpenAngle 로 회전 방향/크기 조정.
    ///
    /// Awake 에서 Receiver 못 찾으면 매 Update 에서 재시도 — 늦게 활성화돼도 자동 복구.
    /// </summary>
    [DisallowMultipleComponent]
    public class BeamGatedDoor : MonoBehaviour
    {
        [Header("Pivots (회전식 hinge)")]
        [Tooltip("좌측 pivot — 이 transform 의 localRotation 이 0 → LeftOpenAngle 로 회전.")]
        public Transform LeftPivot;
        [Tooltip("우측 pivot — 이 transform 의 localRotation 이 0 → RightOpenAngle 로 회전.")]
        public Transform RightPivot;

        [Header("Open angles (Y 축 기준)")]
        [Tooltip("LeftPivot 의 열림 각도 (Y 축, 도) — 음수면 -X 방향, 양수면 +X 방향.")]
        public float LeftOpenAngle = -90f;
        [Tooltip("RightPivot 의 열림 각도 (Y 축, 도) — 보통 LeftOpenAngle 의 반대 부호.")]
        public float RightOpenAngle = 90f;

        [Header("Speed")]
        [Tooltip("열림 속도 (도/초).")]
        public float OpenSpeedDegPerSec = 180f;
        [Tooltip("닫힘 속도 (도/초).")]
        public float CloseSpeedDegPerSec = 120f;
        [Tooltip("신호 해제 후 닫히기까지 지연(초).")]
        public float CloseDelay = 0.3f;

        [Header("Receiver 자동 구독 (권장)")]
        [Tooltip("이 Receiver 가 정해져 있으면 Awake 에서 OnHitChanged 자동 구독. " +
                 "비어있으면 매 Update 에서 씬에서 첫 활성 Receiver 자동 검색해 fallback 연결.")]
        public LightBeamReceiver Receiver;

        [Header("Latch (퍼즐 완료 = 잠금 해제, 영구)")]
        [Tooltip("true 면 빔이 한 번이라도 receiver 에 도달하면 unlocked 로 latch — " +
                 "이후 빔이 끊겨도 잠금 해제 상태 유지.")]
        public bool LatchUnlock = false;

        [Header("Proximity (잠금 해제 후 근접 시 열림)")]
        [Tooltip("true 면 unlocked 상태에서 player 가 ProximityRadius 안에 들어와야 문이 열림. " +
                 "false 면 unlocked 즉시 열림 (기존 동작).")]
        public bool RequireProximity = false;
        [Tooltip("player 감지 반경 (m). Camera.main(=로컬 player head) 와의 거리.")]
        public float ProximityRadius = 2.5f;
        [Tooltip("거리 측정의 기준점. 비어있으면 이 GameObject 의 transform 사용.")]
        public Transform ProximityCenter;

        [Header("Force Open (편의 버튼 등)")]
        [Tooltip("true 면 Latch/Proximity 무시하고 강제 영구 열림. " +
                 "외부에서 ForceOpen() 호출 시 자동으로 켜짐. 인스펙터로 미리 토글하면 게임 시작부터 열려있음.")]
        public bool ForceOpenOverride = false;

        [Header("Debug")]
        [Tooltip("SetBeamConnected 호출 및 구독 시 Console 로그.")]
        public bool LogSignal = true;

        [Header("Events")]
        [Tooltip("문이 *처음으로* 열림 시작하는 순간 한 번 발화. Clear 시퀀스 등 외부 트리거용.")]
        public UnityEvent OnFirstOpen;

        Quaternion _leftClosedRot;
        Quaternion _rightClosedRot;
        bool _initialized;
        bool _signaled;
        // 게임 시작 시 (_lastSignalTime=0, Time.time≈0) closeDelay 윈도가 가짜로 active 되는 걸 방지.
        // SetBeamConnected(true) 가 한 번도 호출 안 됐으면 NegativeInfinity 유지 → signalActive=false 보장.
        float _lastSignalTime = float.NegativeInfinity;
        bool _runtimeSubscribed;
        float _autoFindCooldown;
        bool _latched;
        bool _playerNear;
        bool _firstOpenFired;

        void Awake()
        {
            Cache();
            TrySubscribeToReceiver();
        }
        void OnValidate() { Cache(); }

        void OnDestroy()
        {
            if (_runtimeSubscribed && Receiver != null && Receiver.OnHitChanged != null)
            {
                Receiver.OnHitChanged.RemoveListener(SetBeamConnected);
                _runtimeSubscribed = false;
            }
        }

        void Cache()
        {
            if (_initialized) return;
            if (LeftPivot != null)  _leftClosedRot = LeftPivot.localRotation;
            if (RightPivot != null) _rightClosedRot = RightPivot.localRotation;
            _initialized = true;
        }

        public void RecacheClosedRotations()
        {
            _initialized = false;
            Cache();
        }

        void TrySubscribeToReceiver()
        {
            if (_runtimeSubscribed) return;
            if (Receiver == null) return;
            if (Receiver.OnHitChanged == null) return;
            Receiver.OnHitChanged.AddListener(SetBeamConnected);
            _runtimeSubscribed = true;
            if (LogSignal)
                Debug.Log($"[BeamGatedDoor:{name}] Receiver '{Receiver.name}'.OnHitChanged 런타임 구독 완료.");
        }

        /// <summary>
        /// 외부에서 호출 — 빔 hit 상태. LightBeamReceiver.OnHitChanged 에 wire.
        /// </summary>
        public void SetBeamConnected(bool connected)
        {
            _signaled = connected;
            if (connected) _lastSignalTime = Time.time;
            if (LogSignal)
                Debug.Log($"[BeamGatedDoor:{name}] SetBeamConnected({connected}) — 문 {(connected ? "열림" : "닫힘 예약")}");
        }

        /// <summary>
        /// 모든 조건(Latch/Proximity)을 무시하고 영구 강제 열림.
        /// 편의 버튼(XRSimpleInteractable.selectEntered) 같은 곳에서 호출.
        /// </summary>
        public void ForceOpen()
        {
            ForceOpenOverride = true;
            if (LogSignal)
                Debug.Log($"[BeamGatedDoor:{name}] ForceOpen() — 강제 영구 열림.");
        }

        /// <summary>강제 열림 해제 (원래 잠금/근접 로직으로 복귀).</summary>
        public void ResetForceOpen()
        {
            ForceOpenOverride = false;
            if (LogSignal)
                Debug.Log($"[BeamGatedDoor:{name}] ResetForceOpen().");
        }

        void Update()
        {
            if (!_initialized) Cache();

            // 안전망: Receiver 가 처음엔 없었어도 나중에 등장하거나 인스펙터에서 할당하면 자동 구독.
            if (!_runtimeSubscribed)
            {
                _autoFindCooldown -= Time.deltaTime;
                if (_autoFindCooldown <= 0f)
                {
                    _autoFindCooldown = 1.0f; // 1초마다 재시도
                    if (Receiver == null)
                    {
                        // 씬에서 첫 활성 Receiver 자동 검색 (응급 fallback)
                        var found = FindFirstObjectByType<LightBeamReceiver>();
                        if (found != null)
                        {
                            Receiver = found;
                            if (LogSignal)
                                Debug.LogWarning($"[BeamGatedDoor:{name}] Receiver 필드 비어있어서 자동 검색 → '{Receiver.name}' 사용.");
                        }
                    }
                    TrySubscribeToReceiver();
                }
            }

            // 빔 신호의 활성 여부 (CloseDelay 동안 active 유지) — 단지 닫힘 지연용.
            bool signalActive = _signaled || (Time.time - _lastSignalTime < CloseDelay);

            // **Latch 게이트** — 빛이 실제로 receiver 에 닿은 프레임(_signaled==true) 에만 latch.
            // closeDelay 잔여시간이나 게임 시작 직후 가짜 윈도로는 절대 latch 되지 않음.
            if (_signaled && LatchUnlock) _latched = true;

            // 잠금 해제 상태:
            //   - LatchUnlock=true: 한 번이라도 빔이 닿은 적 있어야 unlocked (영구)
            //   - LatchUnlock=false: 빔이 현재 닿아있을 때만 unlocked (기존 동작)
            bool unlocked = LatchUnlock ? _latched : signalActive;

            // Proximity: unlocked 상태에서만 player 거리 검사 의미가 있음.
            // **빔이 한 번도 닿지 않은 상태에서는 unlocked=false 이므로 가까이 가도 절대 안 열림.**
            if (RequireProximity)
                _playerNear = unlocked && IsPlayerNear();
            else
                _playerNear = unlocked;

            bool shouldOpen = ForceOpenOverride || (unlocked && (!RequireProximity || _playerNear));
            float speed = shouldOpen ? OpenSpeedDegPerSec : CloseSpeedDegPerSec;

            // 첫 열림 발화 (한 번만) — Clear 시퀀스 등 외부 트리거에 사용.
            if (shouldOpen && !_firstOpenFired)
            {
                _firstOpenFired = true;
                if (LogSignal) Debug.Log($"[BeamGatedDoor:{name}] OnFirstOpen 발화.");
                OnFirstOpen?.Invoke();
            }

            if (LeftPivot != null)
            {
                Quaternion target = shouldOpen
                    ? _leftClosedRot * Quaternion.Euler(0f, LeftOpenAngle, 0f)
                    : _leftClosedRot;
                LeftPivot.localRotation = Quaternion.RotateTowards(
                    LeftPivot.localRotation, target, speed * Time.deltaTime);
            }
            if (RightPivot != null)
            {
                Quaternion target = shouldOpen
                    ? _rightClosedRot * Quaternion.Euler(0f, RightOpenAngle, 0f)
                    : _rightClosedRot;
                RightPivot.localRotation = Quaternion.RotateTowards(
                    RightPivot.localRotation, target, speed * Time.deltaTime);
            }
        }

        /// <summary>
        /// 로컬 player(Camera.main, 즉 VR head) 가 ProximityRadius 안에 있는지.
        /// 각 클라이언트는 자기 카메라 기준으로 평가하므로, 자기가 가까이 가면 자기 화면에서 열림.
        /// </summary>
        bool IsPlayerNear()
        {
            var cam = Camera.main;
            if (cam == null) return false;
            Transform center = ProximityCenter != null ? ProximityCenter : transform;
            float sqr = (cam.transform.position - center.position).sqrMagnitude;
            return sqr <= ProximityRadius * ProximityRadius;
        }

        void OnDrawGizmosSelected()
        {
            if (!RequireProximity) return;
            Transform center = ProximityCenter != null ? ProximityCenter : transform;
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.35f);
            Gizmos.DrawWireSphere(center.position, ProximityRadius);
        }
    }
}
