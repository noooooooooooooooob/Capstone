using UnityEngine;

/// <summary>
/// Single-panel proximity sliding door for Stage 2.
///
/// Add this component to stage2DoorLeft and stage2DoorRight separately.
/// Each door slides in its own local-space direction when a player steps
/// into the detection zone, then closes after they leave.
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

    [Tooltip("Fallback sphere radius when no trigger collider is found (m).")]
    public float DetectionRadius = 2.0f;

    [Tooltip("Extra tracked transforms (e.g. second player's head). Optional.")]
    public Transform[] AdditionalTargets;

    // ── internals ──────────────────────────────────────────────────────────
    Vector3 _closedPos;
    Vector3 _openPos;
    bool    _initialized;
    bool    _shouldOpen;
    float   _lastInsideTime;

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
        _initialized = true;
    }

    void Update()
    {
        if (!_initialized) CachePositions();

        // ── detection ──
        bool inside = IsAnyTargetInside();
        if (inside)
        {
            _lastInsideTime = Time.time;
            _shouldOpen = true;
        }
        else if (_shouldOpen && Time.time - _lastInsideTime >= CloseDelay)
        {
            _shouldOpen = false;
        }

        // ── slide ──
        Vector3 target = _shouldOpen ? _openPos : _closedPos;
        float   speed  = _shouldOpen ? OpenSpeed : CloseSpeed;
        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition, target, speed * Time.deltaTime);
    }

    bool IsAnyTargetInside()
    {
        Collider vol = ResolveDetectionVolume();

        // Camera.main (local player head)
        Camera cam = Camera.main;
        if (cam != null)
        {
            if (vol != null)
            {
                if (vol.bounds.Contains(cam.transform.position)) return true;
            }
            else
            {
                if (Vector3.Distance(cam.transform.position, transform.position) <= DetectionRadius)
                    return true;
            }
        }

        // Additional targets (e.g. second player's tracked head)
        if (AdditionalTargets != null)
        {
            foreach (var t in AdditionalTargets)
            {
                if (t == null) continue;
                if (vol != null)
                {
                    if (vol.bounds.Contains(t.position)) return true;
                }
                else
                {
                    if (Vector3.Distance(t.position, transform.position) <= DetectionRadius)
                        return true;
                }
            }
        }

        return false;
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

    void OnDrawGizmosSelected()
    {
        // Show open/closed positions and detection radius in Scene view
        CachePositions();
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.parent != null
            ? transform.parent.TransformPoint(_openPos)
            : (Vector3)_openPos + transform.position - _closedPos,
            Vector3.one * 0.1f);

        if (DetectionVolume == null)
        {
            Gizmos.color = new Color(0, 1, 1, 0.25f);
            Gizmos.DrawWireSphere(transform.position, DetectionRadius);
        }
    }
}
