using Fusion;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections.Generic;

/// <summary>
/// Shared 모드에서 XRGrabInteractable 을 네트워크로 안전하게 잡는 단일 컴포넌트.
/// Photon Fusion 공식 XRITNetworkGrabbable 의 권위-이전 패턴을 우리 환경(XRI 리그 +
/// kinematic grabbable)에 맞게 리그/그래버 의존성 없이 압축한 것.
///
/// 기존 GrabAuthorityHandover + GrabNetworkSyncPause 를 대체한다. 차이점:
///   - NetworkTransform 을 통째로 끄지 않고 "보간만" 끈다(DisableSharedModeInterpolation).
///     → 잡고 있는 동안에도 상대 피어에게 위치가 계속 동기화된다(예전엔 얼어붙어 보였음).
///   - 권위 이전(~1 RTT) 갭 동안 로컬 위치를 저장/재적용(extrapolate)해 되감김을 막는다.
///   - 권위 강탈 시 로컬 grab 을 강제 취소한다(StateAuthorityChanged).
///   - grab 여부를 [Networked] 로 동기화한다.
///
/// 요구: NetworkObject 의 AllowStateAuthorityOverride 가 켜져 있어야 권위 이전이 동작.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[DisallowMultipleComponent]
public class NetworkGrabbableSync : NetworkBehaviour, IStateAuthorityChanged
{
    [Header("Options")]
    [Tooltip("놓을 때 throwOnDetach 강제 OFF. 던짐 속도로 시야 밖 텔레포트 방지.")]
    public bool forceNoThrowOnDetach = true;

    [Tooltip("권위 이전 가능 설정 검증을 건너뛴다(특수 케이스용).")]
    public bool allowNonTransferableObject = false;

    [Tooltip("진단 로그 출력.")]
    public bool verboseLog = false;

    [Header("Events")]
    public UnityEvent onGrab = new UnityEvent();
    public UnityEvent onUngrab = new UnityEvent();

    [Networked, OnChangedRender(nameof(OnIsGrabbedChanged))]
    public NetworkBool IsGrabbed { get; set; }

    XRGrabInteractable _grab;
    NetworkTransform _nt;
    Transform _originalParent;

    bool _isReceivingAuthority;
    Vector3 _transferPosition;
    Quaternion _transferRotation;

    void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _nt = GetComponent<NetworkTransform>();
        _originalParent = transform.parent;

        _grab.selectEntered.AddListener(OnSelectEnter);
        _grab.selectExited.AddListener(OnSelectExit);

        // XRIT 이 Update 에서 위치를 직접 갱신하므로 보간이 이를 되돌리지 않도록 끈다.
#if FUSION_2_1_OR_NEWER
        try { _nt.ConfigFlags = NetworkTransform.NetworkTransformFlags.DisableSharedModeInterpolation; }
        catch { _nt.DisableSharedModeInterpolation = true; }
#else
        _nt.DisableSharedModeInterpolation = true;
#endif

        // XRIT 가 grab 중에 parent 변환으로 좌표를 건드리므로 네트워크 동기화에서 제외.
        _nt.SyncParent = false;

        if (forceNoThrowOnDetach) _grab.throwOnDetach = false;
    }

    void OnDestroy()
    {
        if (_grab == null) return;
        _grab.selectEntered.RemoveListener(OnSelectEnter);
        _grab.selectExited.RemoveListener(OnSelectExit);
    }

    void OnSelectEnter(SelectEnterEventArgs _)
    {
        if (Object == null || !Object.IsValid) return;

        // Grab 중에 parent 제거 (XRIT 좌표 변환 방지)
        if (transform.parent != null)
            transform.parent = null;

        if (!Object.HasStateAuthority)
        {
            Object.RequestStateAuthority();
            _isReceivingAuthority = true;
            StoreState();
            if (verboseLog) Debug.Log($"[NGS:{name}] RequestStateAuthority — auth gap 시작", this);
        }
    }

    void OnSelectExit(SelectExitEventArgs _)
    {
        // Ungrab 후 원래 parent로 복원
        if (transform.parent == null && _originalParent != null)
            transform.parent = _originalParent;
    }

    // 권위가 아직 안 넘어온 동안, XRIT 가 매 프레임 손 위치로 옮겨 둔 트랜스폼을 저장.
    // 권위 측 스냅샷이 이 위치를 덮어쓰기 전에 다시 적용하기 위함.
    void FixedUpdate()
    {
        if (Object != null && Object.IsValid && !Object.HasStateAuthority && _isReceivingAuthority)
            StoreState();
    }

    public override void FixedUpdateNetwork()
    {
        bool selected = _grab.isSelected;

        // grab 상태가 바뀌면 XRIT 의 위치 점프 구간이라 보간 금지.
        if (IsGrabbed != selected) _nt.Teleport();
        IsGrabbed = selected;

        // 권위를 막 받았으면 갭 동안 따라가던 위치를 확정.
        if (Object.HasStateAuthority && _isReceivingAuthority)
        {
            transform.SetPositionAndRotation(_transferPosition, _transferRotation);
            _isReceivingAuthority = false;
        }
    }

    public override void Render()
    {
        // 권위 받는 중인 비권위 측: 스냅샷이 되감기로 보이지 않도록 저장 위치 유지.
        // 또한 모든 비권위 측이 권위 측과 동기화되도록 함.
        if (Object != null && Object.IsValid && !Object.HasStateAuthority && _isReceivingAuthority)
            transform.SetPositionAndRotation(_transferPosition, _transferRotation);
    }

    void StoreState()
    {
        _transferPosition = transform.position;
        _transferRotation = transform.rotation;
    }

    // 권위 강탈 대응: 누군가 이 오브젝트를 가져가 내가 권위를 잃었는데 아직 잡고 있으면 강제로 놓는다.
    public void StateAuthorityChanged()
    {
        if (Object.HasStateAuthority || !_grab.isSelected) return;

        var interactors = new List<UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor>(_grab.interactorsSelecting);
        foreach (var interactor in interactors)
            _grab.interactionManager.SelectCancel(interactor, _grab);

        if (verboseLog) Debug.Log($"[NGS:{name}] 권위 강탈됨 — 로컬 grab 취소", this);
    }

    void OnIsGrabbedChanged()
    {
        if (IsGrabbed) onGrab?.Invoke();
        else onUngrab?.Invoke();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (allowNonTransferableObject) return;
        var no = GetComponent<NetworkObject>();
        if (no == null) return;
        bool allow = (no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) == NetworkObjectFlags.AllowStateAuthorityOverride;
        if (!allow)
            Debug.LogWarning($"[NetworkGrabbableSync] '{name}' NetworkObject.AllowStateAuthorityOverride 가 꺼져 있어 권위 이전이 거부됩니다. 인스펙터에서 켜거나 셋업 툴을 사용하세요.", this);
    }
#endif
}
