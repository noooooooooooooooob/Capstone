using Fusion;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Capstone.Network.Sync
{
    /// <summary>
    /// 범용 "상호작용하면 State Authority를 로컬로 가져온다" 컴포넌트 (Fusion Shared Mode).
    ///
    /// 왜 필요한가:
    ///   Shared 모드에서 씬에 미리 배치된 NetworkObject는 한 피어(보통 P1)에 권위가 묶여 있다.
    ///   비권위 피어(P2)가 잡거나/돌리거나/근접해서 움직여도, 매 틱 NetworkTransform이 권위 좌표로
    ///   되감아 "내 화면에서만 움직이고 상대에겐 안 보이는" 증상이 생긴다.
    ///   조작하는 순간 권위를 끌어오면, 그때부터 NetworkTransform이 "내 위치"를 상대에게 전파한다.
    ///
    /// 무엇을 커버하나 (그랩 전용 GrabAuthorityHandover의 일반화):
    ///   - 잡기/클릭(select), 트리거 당김(activate)  → XR Interactable 이벤트 자동 후킹
    ///   - 근접 이동(문/발판)                         → 로컬 리그가 트리거 콜라이더에 들어오면 Claim
    ///   - 그 외 임의 로컬 로직                        → 코드에서 Claim() 직접 호출
    ///
    /// 요구: NetworkObject.AllowStateAuthorityOverride 가 켜져 있어야 권위 이전이 동작.
    ///       (셋업 툴 Tools/Network/Auto-Sync 가 자동 설정)
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class NetworkAuthorityClaim : MonoBehaviour
    {
        [Header("자동 트리거")]
        [Tooltip("같은 GameObject의 XR Interactable select(잡기/클릭) 시 권위 획득.")]
        public bool claimOnSelect = true;

        [Tooltip("같은 GameObject의 XR Interactable activate(트리거 당김) 시 권위 획득.")]
        public bool claimOnActivate = true;

        [Tooltip("로컬 플레이어(머리/손 = XR Origin 하위)가 이 오브젝트의 트리거 콜라이더에 들어오면 권위 획득. " +
                 "문/발판처럼 '가까이 가면 움직이는' 오브젝트용.")]
        public bool claimOnLocalProximity = false;

        [Tooltip("진단 로그 출력.")]
        public bool verboseLog = false;

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
            if (claimOnSelect) _interactable.selectEntered.AddListener(OnSelect);
            if (claimOnActivate) _interactable.activated.AddListener(OnActivate);
        }

        void OnDisable()
        {
            if (_interactable == null) return;
            if (claimOnSelect) _interactable.selectEntered.RemoveListener(OnSelect);
            if (claimOnActivate) _interactable.activated.RemoveListener(OnActivate);
        }

        void OnSelect(SelectEnterEventArgs _) => Claim();
        void OnActivate(ActivateEventArgs _) => Claim();

        void OnTriggerEnter(Collider other)
        {
            if (!claimOnLocalProximity) return;
            if (IsLocalRig(other)) Claim();
        }

        // 로컬 XR 리그(카메라/손)에 속하면 true. 원격 아바타(NetworkPlayer)는 XROrigin 하위가 아니므로 false.
        static bool IsLocalRig(Collider other)
        {
            if (other == null) return false;
            return other.GetComponentInParent<XROrigin>() != null;
        }

        /// <summary>이 오브젝트의 State Authority를 로컬로 가져온다(이미 보유 시 무시).</summary>
        public void Claim()
        {
            if (_no == null || !_no.IsValid) return;
            if (_no.HasStateAuthority) return;

            bool allowed = (_no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride)
                           == NetworkObjectFlags.AllowStateAuthorityOverride;
            if (!allowed && verboseLog)
                Debug.LogWarning($"[NetworkAuthorityClaim:{name}] AllowStateAuthorityOverride 가 꺼져 있어 권위 이전이 거부될 수 있음.", this);

            _no.RequestStateAuthority();
            if (verboseLog) Debug.Log($"[NetworkAuthorityClaim:{name}] RequestStateAuthority() 호출", this);
        }
    }
}
