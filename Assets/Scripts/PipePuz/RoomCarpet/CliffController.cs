using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 절벽(Cliff) 변종 메인 컨트롤러.
    ///
    /// 매 프레임 카메라 아래로 raycast → <see cref="CliffPlatform"/> 에 닿으면 LastPlatform 갱신.
    /// 카메라 Y 가 <see cref="FallThresholdY"/> 미만이면 LastPlatform 의 dock 위치로 XR Origin 이동.
    ///
    /// XR Origin 없으면 Camera.main 또는 첫 활성 Camera 의 transform/parent 로 fallback.
    /// </summary>
    public class CliffController : MonoBehaviour
    {
        [Header("Refs")]
        public XROrigin XROriginRef;

        [Tooltip("처음에 어떤 발판도 안 밟았을 때의 fallback 리스폰 위치 (보통 entry 플랫폼).")]
        public Transform DefaultSpawnPoint;

        [Tooltip("선택적 head 트랜스폼 직접 지정 — XROrigin / Camera.main 모두 못 찾을 때 수동 backup.")]
        public Transform HeadTransformOverride;

        [Header("Tuning")]
        [Tooltip("카메라 Y 가 이 값 미만이 되면 LastPlatform 으로 리스폰 (월드 Y).")]
        public float FallThresholdY = -3f;

        [Tooltip("카메라 아래로 raycast 할 때 최대 거리(m).")]
        public float PlatformDetectMaxDist = 3f;

        [Tooltip("Platform 감지 raycast LayerMask. -1 이면 모든 레이어.")]
        public LayerMask PlatformDetectMask = ~0;

        [Tooltip("리스폰 직후 다시 안전 검사가 활성화되기까지의 시간(s).")]
        public float RespawnCooldown = 1.0f;

        [Header("Events")]
        public UnityEvent OnRespawned;

        public Transform LastPlatformDock { get; private set; }

        float _cooldownTimer;
        bool _warnedNoCamera;

        void Start()
        {
            if (XROriginRef == null) XROriginRef = FindFirstObjectByType<XROrigin>();
            if (XROriginRef == null && HeadTransformOverride == null)
            {
                Debug.LogWarning("[Cliff] XR Origin 못 찾았고 HeadTransformOverride 도 비어있음. " +
                                 "runtime Camera.main fallback 시도. 안 되면 인스펙터에서 수동 지정 필요.");
            }
        }

        void Update()
        {
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                return;
            }

            Vector3? headOpt = ResolveHeadPosition();
            if (!headOpt.HasValue)
            {
                if (!_warnedNoCamera)
                {
                    Debug.LogWarning("[Cliff] head 위치 결정 불가 — 낙하 감지 비활성.");
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

        Vector3? ResolveHeadPosition()
        {
            if (HeadTransformOverride != null) return HeadTransformOverride.position;
            if (XROriginRef != null && XROriginRef.Camera != null) return XROriginRef.Camera.transform.position;
            if (Camera.main != null) return Camera.main.transform.position;
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

            Transform playerRoot = ResolvePlayerRoot();
            if (playerRoot == null)
            {
                Debug.LogWarning("[Cliff] 이동할 player root 못 찾음 — 리스폰 불가.");
                _cooldownTimer = RespawnCooldown;
                return;
            }

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

        Transform ResolvePlayerRoot()
        {
            if (XROriginRef != null) return XROriginRef.transform;
            if (HeadTransformOverride != null)
                return HeadTransformOverride.parent != null ? HeadTransformOverride.parent : HeadTransformOverride;
            var cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null)
                return cam.transform.parent != null ? cam.transform.parent : cam.transform;
            return null;
        }
    }
}
