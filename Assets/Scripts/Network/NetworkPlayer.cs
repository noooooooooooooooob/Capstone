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

        Transform _xrOrigin;   // 상대좌표 계산용 기준 프레임
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

            _xrOrigin = origin.transform;
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

            CopyPoseRelativeToOrigin(_xrHead, headAnchor);
            CopyPoseRelativeToOrigin(_xrLeftHand, leftHandAnchor);
            CopyPoseRelativeToOrigin(_xrRightHand, rightHandAnchor);
        }

        /// <summary>
        /// XR Origin 기준 상대 pose를 프리팹 루트 기준 local pose로 매핑.
        /// 이렇게 동기화하면 양쪽 피어의 XROrigin 월드 좌표/회전이 달라도
        /// 각자의 spawn 위치 기준으로 일관되게 재구성됨.
        /// </summary>
        void CopyPoseRelativeToOrigin(Transform src, Transform dst)
        {
            if (src == null || dst == null || _xrOrigin == null) return;

            // src의 월드 pose를 _xrOrigin의 로컬(상대) 좌표계로 변환.
            var relPos = _xrOrigin.InverseTransformPoint(src.position);
            var relRot = Quaternion.Inverse(_xrOrigin.rotation) * src.rotation;

            // dst가 _xrOrigin의 자식이면 local 값을 그대로 적용.
            // 그렇지 않으면 수신자(로컬)에서 재구성한 월드 포즈를 직접 할당한다.
            if (dst.parent == _xrOrigin)
            {
                dst.localPosition = relPos;
                dst.localRotation = relRot;
            }
            else
            {
                dst.position = _xrOrigin.TransformPoint(relPos);
                dst.rotation = _xrOrigin.rotation * relRot;
            }
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
