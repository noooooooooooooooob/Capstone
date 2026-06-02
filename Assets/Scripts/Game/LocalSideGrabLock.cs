using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 같은 GameObject 의 XRBaseInteractable 에 자동 등록되어 지정 사이드(기본 P2/Guest)의
/// select(잡기)를 차단한다.
///
/// OwnerSelectFilter 가 "소유자 사이드만 허용(나머지 차단)" 이라면,
/// 이쪽은 반대로 "지정 사이드만 차단(나머지 통과)" 이다.
/// → P2 는 LightBall 을 잡아 옮길 수 없고, P1 과 Spectator 는 영향 없음(그대로).
///
/// 효과는 로컬 전용 — 각 디바이스가 자기 LocalPlayerSide 만 보고 판단한다.
/// XRI 의 IXRSelectFilter 표준(OwnerSelectFilter 와 동일 패턴)을 따른다.
/// </summary>
[RequireComponent(typeof(XRBaseInteractable))]
[DisallowMultipleComponent]
public class LocalSideGrabLock : MonoBehaviour, IXRSelectFilter
{
    [Tooltip("이 로컬 사이드일 때 잡기를 차단한다. 기본 P2(Guest).")]
    [SerializeField] PlayerSide lockForSide = PlayerSide.P2;

    XRBaseInteractable _interactable;

    public bool canProcess => isActiveAndEnabled;

    void Awake()
    {
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
        var local = LocalPlayerSide.Current;
        if (!local.HasValue) return true;          // 사이드 결정 전엔 통과
        return local.Value != lockForSide;          // 잠금 사이드면 false(차단), 그 외 통과
    }
}
