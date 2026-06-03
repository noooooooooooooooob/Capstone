using UnityEngine;

/// <summary>
/// 게임 이벤트(퍼즐 해결·구체 도킹·문 열림·방 진입 등)가 발생하면 연구원(이 박사) NPC 대사를
/// <see cref="GameManager.TriggerNpcCue"/> 로 재생시키는 어댑터 컴포넌트.
///
/// - 자기 자신(또는 자식/부모)에 붙어 있는 대상 컴포넌트의 이벤트에 런타임으로 구독한다.
///   따라서 인스펙터에서 UnityEvent 를 일일이 연결할 필요가 없다(부착 + cueIds 만 지정).
/// - 실제 재생은 GameManager 가 StateAuthority 로 라우팅하고 1회만 브로드캐스트하므로
///   P1/P2 어느 쪽에서 이벤트가 발생해도 양쪽 헤드셋에 동일한 자막·음성이 한 번만 나온다.
///
/// 트리거별 대상 컴포넌트:
///   PipeMiniGame2Solved → PipePuz.MiniGame2.PipeMiniGame2Board.OnSolved   (A4)
///   Stage2Solved        → ClearSoundMaker.OnSolved + PipePuz.Zoo.ZooPuzzleController.OnSolved (B2, 둘 다 지원)
///   LightOrbInserted    → PipePuz.LightBeam.LightOrbSocket.OnOrbInserted   (C2)
///   LightBeamSolved     → PipePuz.LightBeam.LightBeamController.OnAllReceiversHit (C3)
///   DoorOpened          → AutoSlidingDoor.OnOpened / Stage1SlidingDoor.OnOpened (C1)
///   TriggerVolumeEnter  → 이 오브젝트의 isTrigger 콜라이더에 플레이어 머리가 들어오면 (B1)
/// </summary>
[DisallowMultipleComponent]
public class NpcCueBinder : MonoBehaviour
{
    public enum TriggerSource
    {
        PipeMiniGame2Solved,
        Stage2Solved,
        LightOrbInserted,
        LightBeamSolved,
        DoorOpened,
        TriggerVolumeEnter,
    }

    [Header("언제 재생할지")]
    public TriggerSource source = TriggerSource.TriggerVolumeEnter;

    [Header("재생할 대사 id (순서대로) — ResearcherDialogue 자산의 id 와 일치")]
    public string[] cueIds;

    [Tooltip("한 번만 재생(권장). GameManager 에 전역 중복방지가 있어 양쪽 피어 합산 1회지만, 로컬에서도 가드.")]
    public bool fireOnce = true;

    [Header("대상 탐색")]
    [Tooltip("대상 컴포넌트를 자기 자신뿐 아니라 자식/부모에서도 찾는다. (Door/DoorCenter 처럼 한 단계 떨어진 경우 대비)")]
    public bool searchChildrenAndParent = true;

    bool _fired;
    bool _subscribed;

    void Start()
    {
        Subscribe();
    }

    void Subscribe()
    {
        if (_subscribed) return;

        switch (source)
        {
            case TriggerSource.PipeMiniGame2Solved:
            {
                var board = Find<PipePuz.MiniGame2.PipeMiniGame2Board>();
                if (board != null) { board.OnSolved.AddListener(Fire); _subscribed = true; }
                break;
            }
            case TriggerSource.Stage2Solved:
            {
                var clear = Find<ClearSoundMaker>();
                if (clear != null) { clear.OnSolved.AddListener(Fire); _subscribed = true; }

                var zoo = Find<PipePuz.Zoo.ZooPuzzleController>();
                if (zoo != null) { zoo.OnSolved.AddListener(Fire); _subscribed = true; }
                break;
            }
            case TriggerSource.LightOrbInserted:
            {
                var socket = Find<PipePuz.LightBeam.LightOrbSocket>();
                if (socket != null) { socket.OnOrbInserted.AddListener(Fire); _subscribed = true; }
                break;
            }
            case TriggerSource.LightBeamSolved:
            {
                var beam = Find<PipePuz.LightBeam.LightBeamController>();
                if (beam != null) { beam.OnAllReceiversHit.AddListener(Fire); _subscribed = true; }
                break;
            }
            case TriggerSource.DoorOpened:
            {
                var autoDoor = Find<PipePuz.RoomCarpet.AutoSlidingDoor>();
                if (autoDoor != null) { autoDoor.OnOpened.AddListener(Fire); _subscribed = true; }

                var stage1Door = Find<Stage1.Stage1SlidingDoor>();
                if (stage1Door != null) { stage1Door.OnOpened.AddListener(Fire); _subscribed = true; }
                break;
            }
            case TriggerSource.TriggerVolumeEnter:
                // OnTriggerEnter 에서 처리. (콜라이더 isTrigger 필요)
                _subscribed = true;
                break;
        }

        if (!_subscribed)
            Debug.LogWarning($"[NpcCueBinder] '{name}': source={source} 에 맞는 대상 컴포넌트를 찾지 못했습니다.", this);
    }

    T Find<T>() where T : Component
    {
        var c = GetComponent<T>();
        if (c == null && searchChildrenAndParent) c = GetComponentInChildren<T>(true);
        if (c == null && searchChildrenAndParent) c = GetComponentInParent<T>();
        return c;
    }

    void OnTriggerEnter(Collider other)
    {
        if (source != TriggerSource.TriggerVolumeEnter) return;
        if (!IsPlayerCollider(other)) return;
        Fire();
    }

    /// <summary>UnityEvent / 이벤트에서 직접 호출 가능한 진입점.</summary>
    public void Fire()
    {
        if (fireOnce && _fired) return;

        if (GameManager.Instance == null)
        {
            Debug.LogWarning($"[NpcCueBinder] '{name}': GameManager 가 아직 없어 cue 재생을 건너뜀.", this);
            return; // _fired 를 세우지 않아 다음 발생 때 재시도
        }
        if (cueIds == null || cueIds.Length == 0)
        {
            Debug.LogWarning($"[NpcCueBinder] '{name}': cueIds 가 비어 있습니다.", this);
            return;
        }

        _fired = true;
        GameManager.Instance.TriggerNpcCue(cueIds);
    }

    static bool IsPlayerCollider(Collider other)
    {
        if (other == null) return false;
        if (other.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null) return true;
        return other.CompareTag("Player") || other.transform.root.CompareTag("Player");
    }
}
