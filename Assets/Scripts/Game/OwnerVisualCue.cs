using UnityEngine;

/// <summary>
/// OwnerSide와 같은 GameObject에 부착해 비소유자에게 시각 단서를 부여한다.
/// LocalPlayerSide.Changed를 구독하므로 NetworkPlayer가 늦게 스폰돼도 자동 갱신된다.
///
/// 두 메커니즘을 동시에 또는 선택적으로 사용 가능:
/// 1) nonOwnerOverlays — 비소유 시 활성화될 GameObject들(글리치 메시·파티클·아이콘 등)
/// 2) tintRenderers — Renderer들의 _BaseColor를 MaterialPropertyBlock으로 톤다운
///                    (URP Lit/Unlit 공통, 별도 셰이더 작업 불필요)
/// </summary>
[RequireComponent(typeof(OwnerSide))]
[DisallowMultipleComponent]
public class OwnerVisualCue : MonoBehaviour
{
    [Header("비소유 시 활성화할 자식 GameObject들")]
    [Tooltip("글리치 셰이더를 입힌 메시 복제본·파티클·UI 마커 등을 미리 만들어 비활성 상태로 자식에 두고 여기에 등록하면, " +
             "로컬 사이드가 비소유로 판정될 때 자동으로 활성화된다.")]
    [SerializeField] GameObject[] nonOwnerOverlays;

    [Header("MaterialPropertyBlock으로 _BaseColor 톤다운 (선택)")]
    [SerializeField] bool tintRenderers = false;

    [Tooltip("비소유 시 적용할 색상 변조 (URP _BaseColor에 곱해진다고 생각하면 됨).")]
    [SerializeField] Color nonOwnerTint = new Color(0.45f, 0.45f, 0.5f, 1f);

    [Tooltip("로컬 사이드가 아직 결정되지 않았을 때(NetworkPlayer Spawned 이전) 비소유로 간주할지. " +
             "true면 단서가 미리 보임(보수적), false면 결정 전엔 평소 모습.")]
    [SerializeField] bool treatUnknownAsNonOwner = true;

    OwnerSide _owner;
    Renderer[] _renderers;
    MaterialPropertyBlock _mpb;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        _owner = GetComponent<OwnerSide>();
        if (tintRenderers)
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _mpb = new MaterialPropertyBlock();
        }
    }

    void OnEnable()
    {
        LocalPlayerSide.Changed += OnLocalSideChanged;
        Apply();
    }

    void OnDisable()
    {
        LocalPlayerSide.Changed -= OnLocalSideChanged;
    }

    void OnLocalSideChanged(PlayerSide? side) => Apply();

    /// <summary>현재 LocalPlayerSide 상태에 맞춰 단서 표시를 갱신.</summary>
    void Apply()
    {
        bool isMine = ResolveIsMine();

        // 1) 오버레이 GameObject 토글
        if (nonOwnerOverlays != null)
        {
            for (int i = 0; i < nonOwnerOverlays.Length; i++)
            {
                var go = nonOwnerOverlays[i];
                if (go != null) go.SetActive(!isMine);
            }
        }

        // 2) Renderer _BaseColor 톤다운
        if (tintRenderers && _renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                if (isMine) _mpb.Clear();
                else        _mpb.SetColor(BaseColorId, nonOwnerTint);
                r.SetPropertyBlock(_mpb);
            }
        }
    }

    bool ResolveIsMine()
    {
        if (_owner == null) return true; // 안전 폴백: 마커 누락 시 평소 모습
        var local = LocalPlayerSide.Current;
        if (!local.HasValue) return !treatUnknownAsNonOwner;
        return local.Value == _owner.Owner;
    }
}
