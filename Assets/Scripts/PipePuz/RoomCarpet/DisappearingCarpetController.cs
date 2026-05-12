using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// RoomCarpet 퍼즐의 메인 컨트롤러.
    ///
    /// 매 프레임 안전 검사:
    ///   - 카메라(머리)의 X/Z 위치가 Floor X/Z 범위 안인가? (즉, 위험 바닥 위에 있는가)
    ///   - 위에 있다면, StartZone / GoalZone / Anchored 카펫 중 하나의 X/Z 범위 (± Overlap 반경) 에 걸쳐있는가?
    ///   - 어디에도 걸쳐있지 않으면 → XR Origin 을 StartPoint 로 즉시 이동 (카메라 X/Z 오프셋 보정).
    /// IsSolved (GoalZone 도달) 이후엔 안전 검사를 건너뛴다 — 자유로운 탐험 허용.
    /// </summary>
    public class DisappearingCarpetController : MonoBehaviour
    {
        [Header("Refs — 퍼즐 요소")]
        public CarpetDispenser Dispenser;
        public CarpetGoalZone Goal;
        public Transform ActiveCarpetsRoot;

        [Header("Refs — 안전 검사용 콜라이더")]
        public Collider FloorCollider;
        public Collider StartZoneCollider;
        public Collider GoalZoneCollider;

        [Header("Refs — 리스폰")]
        [Tooltip("리스폰 위치 (보통 StartZone 의 Transform).")]
        public Transform StartPoint;

        [Tooltip("XR Origin (Unity.XR.CoreUtils.XROrigin). 비워두면 Start 에서 자동 검색.")]
        public XROrigin XROriginRef;

        [Header("Tuning")]
        [Tooltip("safe zone 의 AABB 가장자리에서 이 거리(m) 안에 카메라가 있으면 '걸쳐있는 것' 으로 간주. " +
                 "0 이면 정확히 AABB 안에 있어야 safe.")]
        public float OverlapRadius = 0.15f;

        [Tooltip("리스폰 직후 다시 안전 검사가 활성화되기까지의 시간(s). 깜빡임 방지.")]
        public float RespawnCooldown = 1.0f;

        [Header("Events")]
        public UnityEvent OnSolved;
        public UnityEvent OnRespawned;

        public bool IsSolved { get; private set; }
        public bool IsInSafeArea { get; private set; }

        float _cooldownTimer;

        void Start()
        {
            if (Goal != null) Goal.OnReached.AddListener(HandleSolved);
            if (XROriginRef == null) XROriginRef = FindFirstObjectByType<XROrigin>();
        }

        void OnDestroy()
        {
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

            CheckSafety();
        }

        void CheckSafety()
        {
            var cam = GetCamera();
            if (cam == null || FloorCollider == null) return;

            Vector3 head = cam.transform.position;

            // 위험 바닥 위에 있지 않으면 안전 검사 통과 (단, IsInSafeArea 갱신).
            if (!IsWithinXZ(head, FloorCollider.bounds, 0f))
            {
                IsInSafeArea = true;
                return;
            }

            // 안전 영역에 걸쳐있는가?
            if (IsNearAny(head))
            {
                IsInSafeArea = true;
                return;
            }

            // 어디에도 걸쳐있지 않음 → 리스폰.
            IsInSafeArea = false;
            Respawn();
        }

        bool IsNearAny(Vector3 head)
        {
            if (StartZoneCollider != null && IsWithinXZ(head, StartZoneCollider.bounds, OverlapRadius))
                return true;
            if (GoalZoneCollider != null && IsWithinXZ(head, GoalZoneCollider.bounds, OverlapRadius))
                return true;

            if (ActiveCarpetsRoot != null)
            {
                int n = ActiveCarpetsRoot.childCount;
                for (int i = 0; i < n; i++)
                {
                    var child = ActiveCarpetsRoot.GetChild(i);
                    var carpet = child.GetComponent<DisappearingCarpet>();
                    if (carpet == null) continue;
                    if (carpet.CurrentState != DisappearingCarpet.State.Anchored) continue;
                    var col = child.GetComponent<Collider>();
                    if (col == null) continue;
                    if (IsWithinXZ(head, col.bounds, OverlapRadius)) return true;
                }
            }
            return false;
        }

        static bool IsWithinXZ(Vector3 p, Bounds b, float padding)
        {
            // X/Z AABB 안에 있는지 검사. padding > 0 이면 AABB 가 그만큼 확장된 것처럼 취급.
            return p.x >= (b.min.x - padding) && p.x <= (b.max.x + padding)
                && p.z >= (b.min.z - padding) && p.z <= (b.max.z + padding);
        }

        void Respawn()
        {
            if (StartPoint == null) return;
            if (XROriginRef == null)
            {
                Debug.LogWarning("[RoomCarpet] XR Origin 참조가 없어 리스폰을 수행할 수 없습니다.");
                _cooldownTimer = RespawnCooldown;
                return;
            }

            Transform origin = XROriginRef.transform;
            Camera cam = GetCamera();
            if (cam == null)
            {
                origin.position = StartPoint.position;
            }
            else
            {
                // 카메라(머리) 의 horizontal 위치가 StartPoint X/Z 가 되도록 XR Origin 위치를 보정.
                Vector3 offset = cam.transform.position - origin.position;
                offset.y = 0f;
                origin.position = StartPoint.position - offset;
            }

            _cooldownTimer = RespawnCooldown;
            OnRespawned?.Invoke();
            Debug.Log("[RoomCarpet] Respawned to StartZone.");
        }

        Camera GetCamera()
        {
            if (XROriginRef != null && XROriginRef.Camera != null) return XROriginRef.Camera;
            return Camera.main;
        }

        void HandleSolved()
        {
            if (IsSolved) return;
            IsSolved = true;
            OnSolved?.Invoke();
            Debug.Log("[RoomCarpet] Solved!");
        }
    }
}
