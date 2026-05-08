using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Fusion Shared Mode 전용 — XRGrabInteractable을 로컬에서 잡는 순간
/// 자기 자신에게 State Authority를 가져온다(Pull). 그렇게 하지 않으면
/// 씬에 미리 배치된 NetworkObject(NetworkTransform 동기)는 호스트(=P1) 쪽에
/// 권위가 묶여 있어, 비권위 피어(P2)가 잡아도 매 tick 권위 좌표로 되감겨
/// "잡히긴 하지만 손을 따라오지 않는" 증상이 발생한다.
///
/// OwnerSelectFilter는 XRI 레벨의 select만 통과시키는 권한 게이팅이고,
/// 실제 트랜스폼 권위 이전은 Fusion API 호출이 필요하기 때문에 별도 컴포넌트로 둔다.
/// NetworkObject의 AllowStateAuthorityOverride 플래그가 켜져 있어야 동작한다.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(XRBaseInteractable))]
[DisallowMultipleComponent]
public class GrabAuthorityHandover : MonoBehaviour
{
    [Tooltip("Select 종료 시 권위를 명시적으로 놓을지 여부. " +
             "Shared Mode에서는 보통 그대로 들고 있어도 무방하지만, " +
             "다음 잡는 사람이 끊김 없이 가져가도록 하려면 켠다.")]
    [SerializeField] bool releaseOnDeselect = false;

    [Tooltip("문제 진단용 로그를 콘솔에 출력. 문제 해결 후 끄세요.")]
    [SerializeField] bool verboseLog = true;

    NetworkObject _no;
    XRBaseInteractable _interactable;

    void Awake()
    {
        _no = GetComponent<NetworkObject>();
        _interactable = GetComponent<XRBaseInteractable>();
    }

    void OnEnable()
    {
        if (_interactable == null) return;
        _interactable.selectEntered.AddListener(OnSelectEntered);
        _interactable.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        if (_interactable == null) return;
        _interactable.selectEntered.RemoveListener(OnSelectEntered);
        _interactable.selectExited.RemoveListener(OnSelectExited);
    }

    void OnSelectEntered(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs _)
    {
        if (_no == null)
        {
            if (verboseLog) Debug.LogWarning($"[GAH:{name}] NetworkObject 없음", this);
            return;
        }
        if (!_no.IsValid)
        {
            if (verboseLog) Debug.LogWarning($"[GAH:{name}] NetworkObject !IsValid (Runner 미초기화 / 미스폰)", this);
            return;
        }
        if (_no.HasStateAuthority)
        {
            if (verboseLog) Debug.Log($"[GAH:{name}] 이미 StateAuthority 보유 — 요청 스킵", this);
            return;
        }
        bool allowed = (_no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) == NetworkObjectFlags.AllowStateAuthorityOverride;
        if (verboseLog)
            Debug.Log($"[GAH:{name}] RequestStateAuthority() 호출 — AllowOverride={allowed}, currentAuth={_no.StateAuthority}, localPlayer={_no.Runner?.LocalPlayer}", this);
        _no.RequestStateAuthority();
        if (verboseLog)
            Debug.Log($"[GAH:{name}] 호출 직후 HasStateAuthority={_no.HasStateAuthority}, currentAuth={_no.StateAuthority}", this);
    }

    void OnSelectExited(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs _)
    {
        if (!releaseOnDeselect) return;
        if (_no == null || !_no.IsValid) return;
        if (!_no.HasStateAuthority) return;
        _no.ReleaseStateAuthority();
    }
}
