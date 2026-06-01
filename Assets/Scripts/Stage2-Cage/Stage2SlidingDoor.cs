using UnityEngine;

/// <summary>
/// Single-panel proximity sliding door for Stage 2.
///
/// Add this component to stage2DoorLeft and stage2DoorRight separately.
/// Each door slides in its own local-space direction when a player steps
/// into the detection zone, then closes after they leave.
///
/// 근접 감지는 로컬 카메라뿐 아니라 PlayerHeadRegistry(로컬+원격 플레이어 머리)를
/// 함께 검사하므로 두 피어에서 동일하게 열고 닫힌다.
/// OpenOnClear 가 켜져 있으면 케이지 퍼즐 클리어 시 추가로 영구 열림으로 래치된다.
///
/// Setup per door:
///   stage2DoorLeft  — SlideDirection = (-1, 0, 0)   slides left
///   stage2DoorRight — SlideDirection = ( 1, 0, 0)   slides right
///
/// Detection zone options (in order of priority):
///   1. Assign a Collider to DetectionVolume in the Inspector.
///   2. Leave it empty — the script finds the first child trigger collider.
///   3. Fall back to a sphere of radius DetectionRadius around the door.
/// </summary>
[DisallowMultipleComponent]
public class Stage2SlidingDoor : MonoBehaviour
{
    [Header("Slide")]
    [Tooltip("Direction (in this object's local space) the door slides when opening.\n" +
             "Left door: (-1,0,0)   Right door: (1,0,0)")]
    public Vector3 SlideDirection = Vector3.left;

    [Tooltip("How far (m) the door travels from its closed position.")]
    public float SlideDistance = 1.5f;

    [Tooltip("Opening speed (m/s).")]
    public float OpenSpeed = 2.5f;

    [Tooltip("Closing speed (m/s).")]
    public float CloseSpeed = 1.8f;

    [Tooltip("Seconds the door stays open after the player leaves the zone.")]
    public float CloseDelay = 0.6f;

    [Header("Detection")]
    [Tooltip("Trigger collider used as the detection zone.\n" +
             "Leave empty to auto-find the first child trigger collider.")]
    public Collider DetectionVolume;

    [Tooltip("트리거 볼륨이 있으면 그 박스를 이만큼(m) 부풀린 영역에서 감지(양쪽 측면 포함),\n" +
             "없으면 문(닫힌 위치) 중심 반경. 안쪽에서 안 열리면 이 값을 키워라.")]
    public float DetectionRadius = 2.0f;

    [Tooltip("Extra tracked transforms (e.g. second player's head). Optional.")]
    public Transform[] AdditionalTargets;

    [Header("Clear Latch (선택)")]
    [Tooltip("근접(다가가면 열림)은 항상 작동한다. 이 옵션을 켜면 추가로,\n" +
             "Stage 2 케이지 퍼즐 클리어(ClearSoundMaker.Solved) 시 문이 영구히 열린 채 고정된다\n" +
             "(이후 근접과 무관). 클리어 상태는 네트워크 동기화되므로 양쪽 피어에서 동일하게 열린다.")]
    public bool OpenOnClear = true;

    // ── internals ──────────────────────────────────────────────────────────
    Vector3 _closedPos;
    Vector3 _openPos;
    bool    _initialized;
    bool    _shouldOpen;
    float   _lastInsideTime;
    bool    _proximityOpen;
    bool    _clearedLatched;
    Vector3 _closedWorldPos;

    void Awake()  => CachePositions();
    void OnValidate() { _initialized = false; CachePositions(); }

    void CachePositions()
    {
        if (_initialized) return;
        _closedPos = transform.localPosition;
        Vector3 dir = SlideDirection.sqrMagnitude > 1e-6f
            ? SlideDirection.normalized
            : Vector3.left;
        _openPos = _closedPos + dir * SlideDistance;
        // 닫힌 위치의 월드 좌표 — 문이 슬라이드해도 고정된 감지 기준점(양쪽 측면 대칭 감지용).
        _closedWorldPos = transform.parent != null
            ? transform.parent.TransformPoint(_closedPos)
            : _closedPos;
        _initialized = true;
    }

    void Update()
    {
        if (!_initialized) CachePositions();

        // ── clear latch (선택) ──
        // 클리어 시(네트워크 동기화된 Solved) 영구 열림으로 래치 — 이후 근접과 무관하게 열린 채 유지.
        if (OpenOnClear && !_clearedLatched && ClearSoundMaker.IsSolved)
            _clearedLatched = true;

        // ── proximity detection (항상 작동) ──
        // 로컬·원격 플레이어 머리를 모두 감지하므로 양쪽 피어가 동일하게 개폐한다.
        bool inside = IsAnyTargetInside();
        if (inside)
        {
            _lastInsideTime = Time.time;
            _proximityOpen = true;
        }
        else if (_proximityOpen && Time.time - _lastInsideTime >= CloseDelay)
        {
            _proximityOpen = false;
        }

        // 다가가면 열리고(_proximityOpen), 클리어했으면 계속 열린다(_clearedLatched).
        _shouldOpen = _proximityOpen || _clearedLatched;

        // ── slide ──
        Vector3 target = _shouldOpen ? _openPos : _closedPos;
        float   speed  = _shouldOpen ? OpenSpeed : CloseSpeed;
        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition, target, speed * Time.deltaTime);
    }

    bool IsAnyTargetInside()
    {
        Collider vol = ResolveDetectionVolume();

        // Camera.main (local player head) — 빠른 로컬 우선 검사.
        Camera cam = Camera.main;
        if (cam != null && IsInside(cam.transform.position, vol)) return true;

        // Additional targets (수동 지정 트랜스폼 — AI, 소품 등)
        if (AdditionalTargets != null)
        {
            foreach (var t in AdditionalTargets)
            {
                if (t != null && IsInside(t.position, vol)) return true;
            }
        }

        // 네트워크 동기화: 로컬·원격 플레이어 머리를 모두 감지 → 두 클라이언트가 동일하게 개폐 판단.
        // (원격 머리는 NetworkTransform 으로 위치가 동기화되므로 양쪽에서 결정론적으로 일치한다.)
        var heads = Capstone.Network.Sync.PlayerHeadRegistry.Heads;
        for (int i = 0; i < heads.Count; i++)
        {
            var h = heads[i];
            if (h != null && IsInside(h.position, vol)) return true;
        }

        return false;
    }

    // 트리거 볼륨이 있으면 그 박스를 DetectionRadius 만큼 부풀린 영역(= 박스 표면까지의 거리),
    // 없으면 닫힌 위치 중심 반경으로 판정. 박스가 한쪽에 치우쳐 있어도 양쪽에서 열린다.
    bool IsInside(Vector3 pos, Collider vol)
    {
        if (vol != null)
        {
            // ClosestPoint: 박스 안이면 pos 자신(거리 0), 밖이면 가장 가까운 표면점.
            Vector3 nearest = vol.ClosestPoint(pos);
            return (pos - nearest).sqrMagnitude <= DetectionRadius * DetectionRadius;
        }
        return (pos - _closedWorldPos).sqrMagnitude <= DetectionRadius * DetectionRadius;
    }

    Collider ResolveDetectionVolume()
    {
        if (DetectionVolume != null) return DetectionVolume;

        // Auto-find first child trigger collider
        foreach (var col in GetComponentsInChildren<Collider>(includeInactive: false))
        {
            if (col.isTrigger)
            {
                DetectionVolume = col;
                return col;
            }
        }

        return null; // will fall back to sphere distance check
    }

    void OnDrawGizmos()
    {
        // 감지 영역을 Scene 뷰에 항상 표시 — 안쪽/바깥쪽이 덮이는지 직접 확인용.
        CachePositions();

        // 열림 위치 마커 (초록)
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.parent != null
            ? transform.parent.TransformPoint(_openPos)
            : _openPos + transform.position - _closedPos,
            Vector3.one * 0.1f);

        if (DetectionVolume != null)
        {
            // 원본 트리거 박스 (주황) — 디자이너가 배치한 위치.
            var b = DetectionVolume.bounds;
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.6f);
            Gizmos.DrawWireCube(b.center, b.size);

            // 실제 감지 영역 = 박스를 DetectionRadius 만큼 부풀린 것 (시안). 안쪽까지 덮이는지 확인용.
            Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
            Gizmos.DrawWireCube(b.center, b.size + Vector3.one * (2f * DetectionRadius));
        }
        else
        {
            // 볼륨이 없으면 닫힌 위치 중심 반경 (시안).
            Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
            Gizmos.DrawWireSphere(_closedWorldPos, DetectionRadius);
        }
    }
}
