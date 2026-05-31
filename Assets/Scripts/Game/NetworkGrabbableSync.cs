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

        // XRGrab 이 놓을 때 원래 부모로 되돌리지 못하게 한다(되돌리면 그래버만 부모가 생겨 어긋남).
        _grab.retainTransformParent = false;
    }

    public override void Spawned()
    {
        // ── 좌표계 일치의 핵심 ──────────────────────────────────────────────
        // XRGrab 은 "잡는 순간" 그래버 쪽 오브젝트의 부모를 null 로 바꾼다(프록시는 안 바뀜).
        // NetworkTransform 은 localPosition(부모 기준)을 동기화하므로, 한쪽만 부모가 떨어지면
        // 프록시는 부모 오프셋만큼 순간이동해 보인다(잡는 순간 -X 등으로 튐 → 놓으면 복귀).
        //
        // 해결: 모든 피어에서 처음부터 부모를 떼어 항상 root(월드 공간)에 둔다. 그러면
        //   · 잡을 때 XRGrab 이 그래버 부모를 떼도 이미 양쪽 다 부모가 없어 어긋나지 않고,
        //   · 놓을 때 부모 복원이 없어(위 retainTransformParent=false) 깜빡임도 없다.
        // SetParent(null, worldPositionStays:true) 라 위치·회전·스케일이 모두 보존돼 시각 변화 없음.
        if (transform.parent != null)
            transform.parent = null;
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

        // parent 분리/복원은 OnIsGrabbedChanged(네트워크 IsGrabbed 기반)에서 모든 피어가
        // 동일하게 수행한다. 여기(잡는 피어)서만 떼면 프록시는 부모 밑에서 받은 로컬좌표를
        // 적용해 부모 오프셋만큼 순간이동해 보였다.

        if (!Object.HasStateAuthority)
        {
            Object.RequestStateAuthority();
            _isReceivingAuthority = true;
            StoreState();
            if (verboseLog) Debug.Log($"[NGS:{name}] RequestStateAuthority 요청", this);
        }
    }

    void OnSelectExit(SelectExitEventArgs _)
    {
        // parent 복원은 OnIsGrabbedChanged(네트워크 IsGrabbed 기반)에서 모든 피어 동일 처리.
    }

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
        // parent 는 어느 피어에서도 절대 바꾸지 않는다(원래 씬 부모 유지).
        //   · XRGrab 은 VelocityTracking/Kinematic 으로 Rigidbody 만 움직이고 부모를 안 건드린다.
        //   · 양쪽 피어가 같은 부모를 유지하면 NetworkTransform 의 local-space 동기화가 항상 월드 기준
        //     으로 일치한다 → 잡을 때 순간이동도, 놓을 때 한 프레임 깜빡임도 없다.
        //   (예전엔 잡는 피어만 부모를 떼서 어긋났고, 부모 토글은 놓는 순간 1프레임 깜빡임을 만들었다.)
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
