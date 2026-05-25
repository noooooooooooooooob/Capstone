using UnityEngine;

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

        [Header("Debug")]
        [Tooltip("SetBeamConnected 호출 및 구독 시 Console 로그.")]
        public bool LogSignal = true;

        Quaternion _leftClosedRot;
        Quaternion _rightClosedRot;
        bool _initialized;
        bool _signaled;
        float _lastSignalTime;
        bool _runtimeSubscribed;
        float _autoFindCooldown;

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

            // 닫힘 지연
            bool shouldOpen = _signaled || (Time.time - _lastSignalTime < CloseDelay);
            float speed = shouldOpen ? OpenSpeedDegPerSec : CloseSpeedDegPerSec;

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
    }
}
