using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 같은 GameObject의 XRBaseInteractable에 자동 등록되어 비소유자 사이드의 select를 차단한다.
/// 기존 인터랙터블(XRGrabInteractable 등)은 코드 수정 없이 OwnerSide + OwnerSelectFilter
/// 두 컴포넌트만 추가하면 권한 게이팅이 활성된다.
///
/// 패턴은 com.unity.xr.interaction.toolkit 의 TouchscreenHoverFilter / IXRSelectFilter 표준을 따른다.
/// </summary>
[RequireComponent(typeof(OwnerSide))]
[RequireComponent(typeof(XRBaseInteractable))]
[DisallowMultipleComponent]
public class OwnerSelectFilter : MonoBehaviour, IXRSelectFilter
{
    [Tooltip("로컬 사이드가 아직 결정되지 않았을 때(NetworkPlayer Spawned 이전) select를 어떻게 처리할지. " +
             "true면 차단(안전 기본값), false면 통과(에디터 단독 테스트 편의).")]
    [SerializeField] bool denyWhenLocalSideUnknown = true;

    OwnerSide _owner;
    XRBaseInteractable _interactable;

    public bool canProcess => isActiveAndEnabled;

    void Awake()
    {
        _owner = GetComponent<OwnerSide>();
        _interactable = GetComponent<XRBaseInteractable>();
    }

    void OnEnable()
    {
        if (_interactable != null)
            _interactable.selectFilters.Add(this);
    }

    void OnDisable()
    {
        if (_interactable != null)
            _interactable.selectFilters.Remove(this);
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        if (_owner == null) return true; // 안전 폴백 — 마커 누락 시 권한 무시(통과)
        var local = LocalPlayerSide.Current;
        if (!local.HasValue) return !denyWhenLocalSideUnknown;
        return local.Value == _owner.Owner;
    }
}
