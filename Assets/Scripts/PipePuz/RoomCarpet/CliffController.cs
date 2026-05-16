using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 절벽(Cliff) 변종 메인 컨트롤러.
    ///
    /// <see cref="DisappearingCarpetController"/> 와 달리:
    ///   - 위험 바닥(CarpetFloor) 의 X/Z 영역 검사 없음. 챔버 바닥 자체가 비어있어 추락 가능.
    ///   - 매 프레임 카메라 아래로 raycast → <see cref="CliffPlatform"/> 에 닿으면 LastPlatform 갱신.
    ///   - 카메라 Y 가 <see cref="FallThresholdY"/> 미만이면 LastPlatform 의 dock 위치로 XR Origin 이동.
    ///   - HintPuzzleBoard 가 솔브되면 IsSolved 처리.
    /// </summary>
    public class CliffController : MonoBehaviour
    {
        [Header("Refs")]
        public XROrigin XROriginRef;
        public HintPuzzleBoard HintBoard;
        public CarpetGoalZone Goal;

        [Tooltip("처음에 어떤 발판도 안 밟았을 때의 fallback 리스폰 위치 (보통 StartZone).")]
        public Transform DefaultSpawnPoint;

        [Tooltip("선택적 head 트랜스폼 직접 지정 — XROrigin / Camera.main 모두 못 찾을 때 수동 backup. " +
                 "비워두면 자동 탐지.")]
        public Transform HeadTransformOverride;

        [Header("Tuning")]
        [Tooltip("카메라(머리) Y 가 이 값 미만이 되면 LastPlatform 으로 리스폰. 발판 윗면(y=0) 기준으로 약 -3m.")]
        public float FallThresholdY = -3f;

        [Tooltip("카메라 아래로 raycast 할 때 최대 거리(m). 머리에서 발판 윗면까지 거리 + 여유.")]
        public float PlatformDetectMaxDist = 3f;

        [Tooltip("Platform 감지 raycast 의 LayerMask. -1(=everything) 이면 모든 레이어. " +
                 "Hit 된 콜라이더에 CliffPlatform 컴포넌트가 있어야 갱신됨.")]
        public LayerMask PlatformDetectMask = ~0;

        [Tooltip("리스폰 직후 다시 안전 검사가 활성화되기까지의 시간(s).")]
        public float RespawnCooldown = 1.0f;

        [Header("Events")]
        public UnityEvent OnSolved;
        public UnityEvent OnRespawned;

        public bool IsSolved { get; private set; }
        public Transform LastPlatformDock { get; private set; }

        float _cooldownTimer;
        bool _warnedNoCamera; // 첫 한번만 경고 로그

        void Start()
        {
            if (XROriginRef == null) XROriginRef = FindFirstObjectByType<XROrigin>();
            if (HintBoard != null) HintBoard.OnSolved.AddListener(HandleSolved);
            if (Goal != null) Goal.OnReached.AddListener(HandleSolved);

            if (XROriginRef == null && HeadTransformOverride == null)
            {
                Debug.LogWarning("[Cliff] XR Origin 을 찾지 못했고 HeadTransformOverride 도 비어있음. " +
                                 "runtime 에 Camera.main 또는 첫 활성 Camera 로 fallback 시도. " +
                                 "여전히 동작 안 하면 인스펙터에서 HeadTransformOverride 에 플레이어 카메라/머리 트랜스폼 직접 지정.");
            }
        }

        void OnDestroy()
        {
            if (HintBoard != null) HintBoard.OnSolved.RemoveListener(HandleSolved);
            if (Goal != null) Goal.OnReached.RemoveListener(HandleSolved);
        }

        void Update()
        {
            if (IsSolved) return;

            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                return;
            }

            // head 위치 결정 — HeadTransformOverride → XROrigin.Camera → Camera.main → 첫 활성 Camera
            Vector3? headOpt = ResolveHeadPosition();
            if (!headOpt.HasValue)
            {
                if (!_warnedNoCamera)
                {
                    Debug.LogWarning("[Cliff] 매 프레임 head 위치를 결정 못 함 (카메라/override 모두 null). " +
                                     "낙하 감지 비활성.");
                    _warnedNoCamera = true;
                }
                return;
            }
            Vector3 head = headOpt.Value;

            UpdateLastPlatform(head);

            if (head.y < FallThresholdY)
            {
                Respawn();
            }
        }

        /// <summary>여러 경로로 head world position 을 시도. 다 실패하면 null.</summary>
        Vector3? ResolveHeadPosition()
        {
            if (HeadTransformOverride != null) return HeadTransformOverride.position;
            if (XROriginRef != null && XROriginRef.Camera != null) return XROriginRef.Camera.transform.position;
            if (Camera.main != null) return Camera.main.transform.position;
            // 최후의 수단 — 씬에서 첫 활성 Camera 검색 (Camera.main 이 tag 없을 때)
            var anyCam = Object.FindFirstObjectByType<Camera>();
            if (anyCam != null) return anyCam.transform.position;
            return null;
        }

        void UpdateLastPlatform(Vector3 head)
        {
            if (Physics.Raycast(head, Vector3.down, out var hit,
                PlatformDetectMaxDist, PlatformDetectMask, QueryTriggerInteraction.Ignore))
            {
                var platform = hit.collider.GetComponent<CliffPlatform>()
                            ?? hit.collider.GetComponentInParent<CliffPlatform>();
                if (platform != null)
                {
                    LastPlatformDock = platform.GetDock();
                }
            }
        }

        void Respawn()
        {
            Transform target = LastPlatformDock != null ? LastPlatformDock : DefaultSpawnPoint;
            if (target == null)
            {
                Debug.LogWarning("[Cliff] 리스폰 대상 없음 — LastPlatform 도 DefaultSpawnPoint 도 null.");
                return;
            }

            // 이동할 player root 결정 — XROrigin > HeadTransformOverride 의 parent > camera 의 parent or itself.
            Transform playerRoot = ResolvePlayerRoot();
            if (playerRoot == null)
            {
                Debug.LogWarning("[Cliff] 이동할 player root 못 찾음 — 리스폰 불가. " +
                                 "XR Origin 또는 HeadTransformOverride 또는 Camera 가 씬에 있어야 함.");
                _cooldownTimer = RespawnCooldown;
                return;
            }

            // 카메라(머리) horizontal 위치가 target X/Z 가 되도록 player root 보정. head 못 찾으면 root 자체를 target 으로.
            Vector3? headOpt = ResolveHeadPosition();
            if (!headOpt.HasValue)
            {
                playerRoot.position = target.position;
            }
            else
            {
                Vector3 offset = headOpt.Value - playerRoot.position;
                offset.y = 0f;
                playerRoot.position = target.position - offset;
            }

            _cooldownTimer = RespawnCooldown;
            OnRespawned?.Invoke();
            Debug.Log($"[Cliff] Respawned to {target.name} (playerRoot={playerRoot.name}).");
        }

        /// <summary>리스폰 시 이동시킬 transform — XROrigin > HeadOverride.parent > Camera.parent > Camera.transform.</summary>
        Transform ResolvePlayerRoot()
        {
            if (XROriginRef != null) return XROriginRef.transform;
            if (HeadTransformOverride != null)
                return HeadTransformOverride.parent != null ? HeadTransformOverride.parent : HeadTransformOverride;
            // camera 의 parent (rig 가 있다면)
            var cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null)
                return cam.transform.parent != null ? cam.transform.parent : cam.transform;
            return null;
        }

        void HandleSolved()
        {
            if (IsSolved) return;
            IsSolved = true;
            OnSolved?.Invoke();
            Debug.Log("[Cliff] Solved!");
        }
    }
}
