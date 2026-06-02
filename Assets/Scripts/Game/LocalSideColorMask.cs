using UnityEngine;

/// <summary>
/// 지정한 로컬 사이드(기본 P1/Host)에게만 이 오브젝트의 "색"을 숨긴다.
/// 공의 형태·위치·존재는 그대로 두고 색상 정보만 제거 — 비대칭 협력 퍼즐용.
/// (예: P1은 LightBall을 잡아 옮길 수 있지만 무슨 색인지 모름 → 색 정보는 P2에게 의존)
///
/// LocalPlayerSide.Changed를 구독하므로 NetworkPlayer가 늦게 Spawned 돼도 자동 갱신된다.
/// 이 효과는 "로컬 전용" — 각 디바이스가 자기 LocalPlayerSide 만 보고 판단하며 네트워크 동기화하지 않는다.
///
/// 두 색 소스를 모두 중성화:
///   1) Renderer 의 _BaseColor / _EmissionColor 를 MaterialPropertyBlock 으로 중성색(회색)으로 덮음
///      (URP Lit/Unlit 공통, 머티리얼 원본은 건드리지 않아 다른 피어/공유 머티리얼에 영향 없음)
///   2) 자식 Light 의 color 를 중성색으로 변경 (enabled/intensity 는 건드리지 않으므로
///      LightBallLightSync 의 정전 연출과 충돌 없이 공존 — 색만 회색으로 비춤)
/// </summary>
[DisallowMultipleComponent]
public class LocalSideColorMask : MonoBehaviour
{
    [Tooltip("이 로컬 사이드일 때만 색을 숨긴다. 기본 P1(Host).")]
    [SerializeField] PlayerSide maskForSide = PlayerSide.P1;

    [Header("색을 가릴 대상 (비우면 자식 포함 자동 수집)")]
    [SerializeField] Renderer[] renderers;
    [SerializeField] Light[] lights;

    [Header("색 숨길 때 적용할 중성색 (색상 정보 제거, 밝기는 유지)")]
    [SerializeField] Color neutralColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Tooltip("로컬 사이드가 아직 결정되지 않았을 때(NetworkPlayer Spawned 이전) 색을 숨길지. " +
             "true=보수적으로 미리 숨김, false=결정 전엔 평소 색.")]
    [SerializeField] bool maskWhenUnknown = false;

    MaterialPropertyBlock _mpb;
    Color[] _origLightColors;
    bool _masked;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
        if (lights == null || lights.Length == 0)
            lights = GetComponentsInChildren<Light>(true);

        _mpb = new MaterialPropertyBlock();

        // 자식 Light 의 원본 색을 캐시 — 마스크 해제 시 복원용.
        _origLightColors = new Color[lights != null ? lights.Length : 0];
        if (lights != null)
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) _origLightColors[i] = lights[i].color;
    }

    void OnEnable()
    {
        LocalPlayerSide.Changed += OnSideChanged;
        Apply();
    }

    void OnDisable()
    {
        LocalPlayerSide.Changed -= OnSideChanged;
        SetMasked(false); // 컴포넌트 비활성 시 원래 색 복원
    }

    void OnSideChanged(PlayerSide? side) => Apply();

    void Apply() => SetMasked(ResolveMask());

    bool ResolveMask()
    {
        var local = LocalPlayerSide.Current;
        if (!local.HasValue) return maskWhenUnknown;
        return local.Value == maskForSide;
    }

    void SetMasked(bool masked)
    {
        _masked = masked;

        // 1) Renderer 색 (_BaseColor + _EmissionColor)
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                if (masked)
                {
                    _mpb.SetColor(BaseColorId, neutralColor);
                    _mpb.SetColor(EmissionColorId, neutralColor); // 발광도 회색으로 (색상 단서 제거)
                }
                else
                {
                    _mpb.Clear(); // 원본 머티리얼 색으로 복원
                }
                r.SetPropertyBlock(_mpb);
            }
        }

        // 2) 자식 Light 색 (enabled/intensity 는 LightBallLightSync 소관이라 건드리지 않음)
        if (lights != null)
        {
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                l.color = masked ? neutralColor : _origLightColors[i];
            }
        }
    }
}
