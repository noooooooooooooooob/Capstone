using UnityEngine;
using UnityEngine.Events;
using Capstone.Network.Sync;

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
        public bool LatchUnlock = false;

        [Header("Proximity (잠금 해제 후 근접 시 열림)")]
        public bool RequireProximity = false;
        public float ProximityRadius = 2.5f;
        public Transform ProximityCenter;

        [Tooltip("켜면 '두 플레이어(모든 플레이어)가 모두' 반경 안에 있어야 열린다. " +
                 "PlayerHeadRegistry(로컬+원격 머리)를 보며, 아바타 머리는 NetworkTransform 으로 동기화돼 " +
                 "양쪽 클라이언트가 동일하게 판정한다. 끄면 기존처럼 로컬 플레이어 1명 근접으로 열린다.")]
        public bool RequireBothPlayers = false;

        [Header("Force Open (편의 버튼 등)")]
        public bool ForceOpenOverride = false;

        [Header("Debug")]
        [Tooltip("SetBeamConnected 호출 및 구독 시 Console 로그.")]
        public bool LogSignal = true;

        [Header("Events")]
        public UnityEvent OnFirstOpen;

        // ── 네트워크 동기화 연동 (BeamGatedDoorNetworkSync 가 채움) ─────────────────────────────
        /// <summary>이번 프레임 문이 열려있어야 하는지(로컬 계산 결과). 권위 측 컴패니언이 읽어 전파한다.</summary>
        public bool ShouldBeOpen { get; private set; }
        /// <summary>true 면 로컬 계산 대신 <see cref="ExternalOpenValue"/>(네트워크로 받은 값)를 따른다. 프록시에서 설정.</summary>
        [System.NonSerialized] public bool UseExternalOpen;
        /// <summary>네트워크로 받은 열림 상태(권위가 결정).</summary>
        [System.NonSerialized] public bool ExternalOpenValue;

        Quaternion _leftClosedRot;
        Quaternion _rightClosedRot;
        bool _initialized;
        bool _signaled;
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

            bool shouldOpen;
            if (UseExternalOpen)
            {
                // 네트워크 동기화: 권위(호스트)가 결정한 열림 상태를 그대로 따른다.
                shouldOpen = ExternalOpenValue;
            }
            else
            {
                bool signalActive = _signaled || (Time.time - _lastSignalTime < CloseDelay);
                if (_signaled && LatchUnlock) _latched = true;
                bool unlocked = LatchUnlock ? _latched : signalActive;
                if (RequireProximity)
                    _playerNear = unlocked && IsPlayerNear();
                else
                    _playerNear = unlocked;
                shouldOpen = ForceOpenOverride || (unlocked && (!RequireProximity || _playerNear));
            }
            ShouldBeOpen = shouldOpen; // 컴패니언(BeamGatedDoorNetworkSync)이 권위 측에서 읽어 전파.
            float speed = shouldOpen ? OpenSpeedDegPerSec : CloseSpeedDegPerSec;

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

        public void ForceOpen()
        {
            ForceOpenOverride = true;
            if (LogSignal) Debug.Log($"[BeamGatedDoor:{name}] ForceOpen() — 강제 영구 열림.");
        }

        public void ResetForceOpen() { ForceOpenOverride = false; }

        bool IsPlayerNear()
        {
            Transform center = ProximityCenter != null ? ProximityCenter : transform;
            Vector3 c = center.position;
            float r2 = ProximityRadius * ProximityRadius;

            if (RequireBothPlayers)
            {
                // 등록된 모든 플레이어(로컬+원격) 머리가 반경 안에 있어야 열림.
                var heads = PlayerHeadRegistry.Heads;
                if (heads != null && heads.Count > 0)
                {
                    for (int i = 0; i < heads.Count; i++)
                    {
                        var h = heads[i];
                        if (h == null) continue;
                        if ((h.position - c).sqrMagnitude > r2) return false; // 한 명이라도 멀면 안 열림
                    }
                    return true; // 모든 플레이어가 반경 안
                }
                // 머리 미등록(에디터 단독/비네트워크) → 아래 로컬 카메라 폴백.
            }

            var cam = Camera.main;
            if (cam == null) return false;
            return (cam.transform.position - c).sqrMagnitude <= r2;
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
