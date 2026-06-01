using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 런타임 스폰 카펫(Carpet_Net 프리팹)의 네트워크 컴패니언 (Fusion Shared Mode).
    ///
    /// NetworkGrabbableSync 를 쓰지 않고 카펫 전용으로 잡기·던지기·상태·삭제를 직접 처리한다.
    /// (NGS 는 "놓을 때 throwOnDetach 강제 OFF + 보간 비활성" 이라 던지는 카펫에는 맞지 않아 던져지지 않고
    ///  비행이 끊겨 보였다. 그래서 카펫은 이 컴포넌트가 담당한다.)
    ///
    /// 역할:
    ///   1) 잡는 순간 그 피어가 StateAuthority 를 가져온다(양쪽이 서로의 잡기/던지기를 본다).
    ///   2) 권위만 물리를 돌리고(SuspendSimulation 토글 + DisappearingCarpet.RefreshPhysics),
    ///      프록시는 kinematic + NetworkTransform 수신(보간 ON → 부드러운 비행).
    ///   3) 카펫 상태(Spawned/Held/Flying/Anchored)를 [Networked] 로 전파.
    ///   4) 삭제는 권위의 Runner.Despawn → 전 피어 동시 제거.
    ///   5) 런처 발사 속도/자기충돌무시를 onBeforeSpawned 큐에서 Spawned 시 권위가 적용.
    ///
    /// 요구: 같은 GameObject 에 NetworkObject(AllowStateAuthorityOverride ON) + NetworkTransform +
    ///       XRGrabInteractable + Rigidbody + DisappearingCarpet.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(DisappearingCarpet))]
    [DisallowMultipleComponent]
    public class CarpetNetworkSync : NetworkBehaviour, IStateAuthorityChanged
    {
        [Networked, OnChangedRender(nameof(OnNetStateChanged))]
        public int NetState { get; set; }   // (int)DisappearingCarpet.State

        DisappearingCarpet _carpet;
        XRGrabInteractable _grab;
        NetworkTransform _nt;

        // onBeforeSpawned 가 채우는 발사 큐(권위 측에서만 의미 있음).
        [System.NonSerialized] bool _pendingLaunch;
        [System.NonSerialized] Vector3 _pendingVel;
        [System.NonSerialized] Vector3 _pendingSpin;
        [System.NonSerialized] Collider[] _pendingIgnore;

        void Awake()
        {
            _carpet = GetComponent<DisappearingCarpet>();
            _grab = GetComponent<XRGrabInteractable>();
            _nt = GetComponent<NetworkTransform>();
            if (_grab != null)
            {
                _grab.selectEntered.AddListener(OnSelectEntered);
            }
            // 카펫은 루트에서 월드 공간으로 움직이므로 부모 동기화 불필요.
            if (_nt != null) _nt.SyncParent = false;
        }

        void OnDestroy()
        {
            if (_grab != null) _grab.selectEntered.RemoveListener(OnSelectEntered);
        }

        public override void Spawned()
        {
            _carpet.NetworkRemovalHandler = HandleRemoval;
            ApplyAuthorityGate();

            if (HasStateAuthority)
            {
                if (_pendingLaunch)
                {
                    if (_pendingIgnore != null)
                    {
                        var col = GetComponent<Collider>();
                        if (col != null)
                            foreach (var o in _pendingIgnore)
                                if (o != null) Physics.IgnoreCollision(col, o, true);
                    }
                    _carpet.Launch(_pendingVel, _pendingSpin);
                    _pendingLaunch = false;
                }
                NetState = (int)_carpet.CurrentState;
            }
            else
            {
                _carpet.ApplyNetworkState((DisappearingCarpet.State)NetState);
            }
        }

        // 잡는 순간 권위 확보 → 이후 내 잡기/이동/던지기가 전 피어에 전파된다.
        void OnSelectEntered(SelectEnterEventArgs _)
        {
            if (Object != null && Object.IsValid && !HasStateAuthority)
                Object.RequestStateAuthority();
        }

        public void StateAuthorityChanged()
        {
            ApplyAuthorityGate();

            // 권위를 잃었는데 아직 로컬에서 잡고 있다면 강제로 놓는다(권위 강탈 대응).
            if (Object != null && Object.IsValid && !HasStateAuthority && _grab != null && _grab.isSelected)
            {
                var interactors = new List<UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor>(_grab.interactorsSelecting);
                foreach (var it in interactors)
                    _grab.interactionManager.SelectCancel(it, _grab);
            }
        }

        void ApplyAuthorityGate()
        {
            bool authority = Object != null && Object.IsValid && HasStateAuthority;
            _carpet.SuspendSimulation = !authority;
            _carpet.RefreshPhysics(); // 프록시=kinematic, 권위=상태에 맞는 물리.
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            int s = (int)_carpet.CurrentState;
            if (s != NetState) NetState = s;
        }

        void OnNetStateChanged()
        {
            if (HasStateAuthority) return;
            _carpet.ApplyNetworkState((DisappearingCarpet.State)NetState);
        }

        bool HandleRemoval()
        {
            if (Object == null || !Object.IsValid) return false; // 비네트워크 → 로컬 Destroy 허용.
            if (HasStateAuthority && Runner != null) Runner.Despawn(Object);
            return true; // 네트워크: 로컬 Destroy 금지(권위 Despawn 이 전 피어에 전파).
        }

        // ── 스폰 측(Stage3CarpetNetwork)이 onBeforeSpawned 에서 호출 ──────────────────────────────
        public void ConfigureFloating(bool floating, float floatingY)
        {
            _carpet.UseFloatingMode = floating;
            _carpet.FloatingY = floatingY;
        }

        public void QueueLaunch(Vector3 velocity, Vector3 spin, Collider[] ignoreWith)
        {
            _pendingLaunch = true;
            _pendingVel = velocity;
            _pendingSpin = spin;
            _pendingIgnore = ignoreWith;
        }
    }
}
