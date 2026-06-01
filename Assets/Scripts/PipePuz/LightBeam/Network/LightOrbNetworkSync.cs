using Fusion;
using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// LightOrb 의 도킹 상태를 네트워크로 동기화하는 컴패니언 (Fusion Shared Mode).
    ///
    /// 잡기·이동·낙하 위치는 같은 오브젝트의 NetworkGrabbableSync + NetworkTransform 이 담당한다.
    /// 이 컴포넌트는 "어느 LightOrbSocket 에 꽂혔는가"를 [Networked] 로 전파해, 한쪽에서 orb 가
    /// socket 에 스냅되거나 빠지면 상대 피어에서도 동일하게 스냅/분리되고 OnOrbInserted/OnOrbRemoved
    /// 이벤트(LED·빛줄기 등)가 양쪽에서 발동되게 한다.
    ///
    /// 권위(orb 를 마지막으로 잡은 피어)만 도킹을 판정하고, 결과(소켓 id)를 실어 보낸다.
    /// 프록시는 그 소켓을 찾아 ForceInsert/ForceRemove 로 동일 상태를 재현한다.
    ///
    /// 요구: 같은 GameObject 에 NetworkObject(AllowStateAuthorityOverride + 버전비트 ON) +
    ///       NetworkTransform + NetworkGrabbableSync + LightOrb.
    ///       대상 LightOrbSocket 들에는 NetworkObject 가 있어야 한다(id 로 참조).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(LightOrb))]
    [DisallowMultipleComponent]
    public class LightOrbNetworkSync : NetworkBehaviour, IStateAuthorityChanged
    {
        [Networked, OnChangedRender(nameof(OnDockChanged))]
        public NetworkBool Docked { get; set; }

        [Networked, OnChangedRender(nameof(OnDockChanged))]
        public NetworkId DockSocket { get; set; }

        LightOrb _orb;
        LightOrbSocket _appliedSocket; // 프록시에서 현재 반영 중인 소켓.

        void Awake()
        {
            _orb = GetComponent<LightOrb>();
        }

        public override void Spawned()
        {
            ApplyGate();
            if (!HasStateAuthority) OnDockChanged(); // 늦게 합류 시 현재 상태 반영.
        }

        public void StateAuthorityChanged() => ApplyGate();

        void ApplyGate()
        {
            bool authority = Object != null && Object.IsValid && HasStateAuthority;
            _orb.SetNetworkProxy(!authority);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var host = _orb.HostSocket;
            bool docked = host != null;
            if (docked != (bool)Docked) Docked = docked;

            NetworkId id = default;
            if (host != null)
            {
                var no = host.GetComponent<NetworkObject>();
                if (no != null) id = no.Id;
            }
            if (DockSocket != id) DockSocket = id;
        }

        void OnDockChanged()
        {
            if (HasStateAuthority) return; // 권위는 로컬 로직(소켓 TryAccept/NotifyOrbGrabbed)이 이미 처리.

            if (Docked)
            {
                if (Runner != null && Runner.TryFindObject(DockSocket, out var no) && no != null)
                {
                    var sock = no.GetComponent<LightOrbSocket>();
                    if (sock != null)
                    {
                        sock.ForceInsert(_orb);
                        _appliedSocket = sock;
                    }
                }
            }
            else
            {
                if (_appliedSocket != null)
                {
                    _appliedSocket.ForceRemove(_orb);
                    _appliedSocket = null;
                }
            }
        }
    }
}
