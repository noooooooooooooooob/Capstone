using Fusion;
using UnityEngine;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 런타임 스폰 카펫(Carpet_Net 프리팹)의 네트워크 컴패니언 (Fusion Shared Mode).
    ///
    /// 잡기·이동·던지기·권위이전은 같은 오브젝트의 NetworkGrabbableSync(배터리와 동일한 검증된 경로) +
    /// NetworkTransform 이 담당한다. 이 컴포넌트는 그 위에 카펫 고유의 것을 얹는다:
    ///   1) 카펫 상태(Spawned/Held/Flying/Anchored)를 [Networked] 로 전파(스폰 매니저의 대기 카펫 판정용).
    ///   2) 권위만 물리/안착/수명을 돌리고(SuspendSimulation) 프록시는 NetworkTransform 수신.
    ///   3) 삭제는 권위의 Runner.Despawn → 전 피어 동시 제거.
    ///   4) 런처 발사 속도/자기충돌무시를 Spawned 시 권위가 적용.
    ///
    /// 요구: 같은 GameObject 에 NetworkObject(AllowStateAuthorityOverride ON) + NetworkTransform +
    ///       NetworkGrabbableSync + XRGrabInteractable + Rigidbody + DisappearingCarpet.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(DisappearingCarpet))]
    [DisallowMultipleComponent]
    public class CarpetNetworkSync : NetworkBehaviour, IStateAuthorityChanged
    {
        [Networked, OnChangedRender(nameof(OnNetStateChanged))]
        public int NetState { get; set; }   // (int)DisappearingCarpet.State

        DisappearingCarpet _carpet;

        // onBeforeSpawned 가 채우는 발사 큐(권위 측에서만 의미 있음).
        [System.NonSerialized] bool _pendingLaunch;
        [System.NonSerialized] Vector3 _pendingVel;
        [System.NonSerialized] Vector3 _pendingSpin;
        [System.NonSerialized] Collider[] _pendingIgnore;

        void Awake()
        {
            _carpet = GetComponent<DisappearingCarpet>();
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

        public void StateAuthorityChanged() => ApplyAuthorityGate();

        void ApplyAuthorityGate()
        {
            bool authority = Object != null && Object.IsValid && HasStateAuthority;
            _carpet.SuspendSimulation = !authority;
            _carpet.RefreshPhysics(); // 프록시=중력off/제약없음(NT구동), 권위=상태별 물리.
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
