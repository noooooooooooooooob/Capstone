using Fusion;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Capstone.Network
{
    /// <summary>
    /// 비대칭 협력 VR — 한 플레이어를 네트워크에 등록.
    /// Shared Mode 전제: 각 피어가 자기 NetworkPlayer의 State Authority를 가짐.
    /// 머리/양손 트랜스폼은 자식 GameObject에 NetworkTransform을 붙여 동기화하고,
    /// 이 스크립트는 권한자 측에서 그 자식들이 로컬 XR 리그를 따라가도록만 합니다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayer : NetworkBehaviour
    {
        [Header("Networked rig anchors (자식에 NetworkTransform 부착)")]
        [SerializeField] Transform headAnchor;
        [SerializeField] Transform leftHandAnchor;
        [SerializeField] Transform rightHandAnchor;

        [Header("로컬(자기 자신) 시점에서 숨길 비주얼 — 머리·손 메시 등")]
        [SerializeField] GameObject[] hideOnLocal;

        Transform _xrHead;
        Transform _xrLeftHand;
        Transform _xrRightHand;

        public PlayerRef Owner => Object.StateAuthority;

        public override void Spawned()
        {
            if (HasStateAuthority)
            {
                BindLocalRig();
                foreach (var go in hideOnLocal)
                    if (go != null) go.SetActive(false);
            }
        }

        void BindLocalRig()
        {
            // 씬에 단일 XR Origin이 있다고 가정.
            var origin = FindFirstObjectByType<XROrigin>();
            if (origin == null)
            {
                Debug.LogWarning("[NetworkPlayer] XROrigin을 찾지 못해 로컬 리그 바인딩 실패.");
                return;
            }

            _xrHead = origin.Camera != null ? origin.Camera.transform : null;

            // XRI 기본 리그 명명 규약 우선, 폴백으로 짧은 이름.
            var rigRoot = origin.transform;
            _xrLeftHand  = FindDeep(rigRoot, "Left Controller")  ?? FindDeep(rigRoot, "LeftHand Controller")  ?? FindDeep(rigRoot, "LeftHand");
            _xrRightHand = FindDeep(rigRoot, "Right Controller") ?? FindDeep(rigRoot, "RightHand Controller") ?? FindDeep(rigRoot, "RightHand");
        }

        public override void FixedUpdateNetwork()
        {
            // 권한자만 자기 포즈를 갱신 — 자식 NetworkTransform이 원격 피어로 전파.
            if (!HasStateAuthority) return;

            CopyPose(_xrHead, headAnchor);
            CopyPose(_xrLeftHand, leftHandAnchor);
            CopyPose(_xrRightHand, rightHandAnchor);
        }

        static void CopyPose(Transform src, Transform dst)
        {
            if (src == null || dst == null) return;
            dst.SetPositionAndRotation(src.position, src.rotation);
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
