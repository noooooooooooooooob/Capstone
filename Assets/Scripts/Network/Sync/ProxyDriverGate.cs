using Fusion;
using UnityEngine;

namespace Capstone.Network.Sync
{
    /// <summary>
    /// 비권위(proxy) 피어에서 지정한 "구동 로직" 컴포넌트를 비활성화한다 (Fusion Shared Mode).
    ///
    /// 문/발판/회전체처럼 로컬 로직이 Update 에서 transform 을 직접 움직이는 오브젝트는,
    /// 두 피어에서 독립적으로 돌면 NetworkTransform 수신값과 충돌해 떨림/되감김이 생긴다.
    /// 이 게이트는 "권위 측만 로직을 돌리고, 나머지는 NetworkTransform 수신만" 하도록 만든다.
    ///
    /// 사용:
    ///   - driversDisabledOnProxy 에 그 오브젝트의 이동 스크립트(예: AutoSlidingDoor)를 넣는다.
    ///   - NetworkAuthorityClaim(근접/조작 시 권위 이전)과 함께 쓰면, 조작하는 쪽이 권위를 갖고
    ///     로직을 돌리며 그 결과가 상대에게 전파된다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class ProxyDriverGate : NetworkBehaviour, IStateAuthorityChanged
    {
        [Tooltip("비권위 측에서 enabled=false 로 끌 구동 컴포넌트들(예: AutoSlidingDoor, 회전 로직 등).")]
        // Fusion 네임스페이스에도 Behaviour 가 있어 모호성 방지를 위해 정규화.
        public UnityEngine.Behaviour[] driversDisabledOnProxy;

        [Tooltip("진단 로그 출력.")]
        public bool verboseLog = false;

        public override void Spawned() => Apply();

        // Fusion: 이 오브젝트의 State Authority 가 바뀔 때 호출.
        public void StateAuthorityChanged() => Apply();

        void Apply()
        {
            if (driversDisabledOnProxy == null) return;
            bool auth = Object != null && Object.IsValid && HasStateAuthority;
            foreach (var b in driversDisabledOnProxy)
                if (b != null && b.enabled != auth) b.enabled = auth;

            if (verboseLog) Debug.Log($"[ProxyDriverGate:{name}] drivers enabled={auth} (HasStateAuthority={auth})", this);
        }
    }
}
