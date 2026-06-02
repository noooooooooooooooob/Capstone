using UnityEngine;

/// <summary>
/// 지정한 로컬 사이드(기본 P1/Host)에게만 이 오브젝트(+자식)의 Renderer 를 꺼서 "안 보이게" 한다.
///
/// 중요: GameObject 와 컴포넌트는 그대로 활성 상태로 둔다 — Renderer.enabled 만 토글.
/// 따라서 시각만 숨길 뿐, 로직(예: SmokeGauge 의 Pointer/PointerInRedZone 계산)은 계속 돈다.
/// → 한 피어에서 게이지를 숨겨도 연기 억제 판정 등 게임플레이 상태는 깨지지 않는다.
///
/// 효과는 로컬 전용 — 각 디바이스가 자기 LocalPlayerSide 만 보고 판단하며 네트워크 동기화하지 않는다.
/// LocalPlayerSide.Changed 를 구독하므로 NetworkPlayer 가 늦게 Spawned 돼도 자동 갱신된다.
/// </summary>
[DisallowMultipleComponent]
public class LocalSideHide : MonoBehaviour
{
    [Tooltip("이 로컬 사이드일 때만 숨긴다. 기본 P1(Host).")]
    [SerializeField] PlayerSide hideForSide = PlayerSide.P1;

    [Header("숨길 Renderer (비우면 자식 포함 자동 수집)")]
    [SerializeField] Renderer[] renderers;

    [Tooltip("로컬 사이드가 아직 결정되지 않았을 때(NetworkPlayer Spawned 이전) 숨길지. " +
             "true=보수적으로 미리 숨김, false=결정 전엔 평소대로 보임.")]
    [SerializeField] bool hideWhenUnknown = false;

    void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
    }

    void OnEnable()
    {
        LocalPlayerSide.Changed += OnSideChanged;
        Apply();
    }

    void OnDisable()
    {
        LocalPlayerSide.Changed -= OnSideChanged;
        SetHidden(false); // 컴포넌트 비활성 시 다시 보이게 복원
    }

    void OnSideChanged(PlayerSide? side) => Apply();

    void Apply() => SetHidden(ResolveHide());

    bool ResolveHide()
    {
        var local = LocalPlayerSide.Current;
        if (!local.HasValue) return hideWhenUnknown;
        return local.Value == hideForSide;
    }

    void SetHidden(bool hidden)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = !hidden;
    }
}
