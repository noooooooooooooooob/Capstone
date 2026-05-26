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

        [Header("로컬 시점에서 scale=0으로 수축할 본 (예: Head, Neck) — 본 회전/위치는 그대로 유지되어 IK·NetworkTransform 동기화에 영향 없음")]
        [SerializeField] Transform[] hideBonesOnLocal;

        [Header("IK / 입력 컴포넌트 (비우면 자식에서 자동 탐색)")]
        [SerializeField] IKTargetFollowVRRig ikRig;
        [SerializeField] AnimateOnInput animateOnInput;

        [Header("스폰 시 로컬 카메라 미세 조정 — spawnPoint 로컬축 기준 (x=오른쪽, y=위, z=앞)")]
        [SerializeField] Vector3 spawnCameraOffset;

        Transform _xrOrigin;   // 상대좌표 계산용 기준 프레임
        XROrigin _xrOriginComp; // 카메라 정렬용 (MoveCameraToWorldLocation 등)
        Transform _xrHead;
        Transform _xrLeftHand;
        Transform _xrRightHand;

        public PlayerRef Owner => Object.StateAuthority;

        /// <summary>
        /// 0 = Player 1 (먼저 입장), 1 = Player 2. RoomLauncher가 스폰 직전에 세팅.
        /// 비대칭 환경(방 A/B), 역할 배정, 통신 채널 권한 등에서 분기 키로 사용.
        /// </summary>
        [Networked] public int Slot { get; set; }

        public override void Spawned()
        {
            Debug.Log($"[NetworkPlayer] Spawned slot={Slot} hasAuthority={HasStateAuthority} pos={transform.position} rot={transform.eulerAngles}");

            if (ikRig == null) ikRig = GetComponentInChildren<IKTargetFollowVRRig>();
            if (animateOnInput == null) animateOnInput = GetComponentInChildren<AnimateOnInput>();

            if (HasStateAuthority)
            {
                // 로컬 사이드 캐시 — OwnerSelectFilter / OwnerVisualCue 등이 이 값으로 분기.
                LocalPlayerSide.Set(LocalPlayerSide.FromSlot(Slot));

                BindLocalRig();
                AlignLocalCameraToSpawn();

                // 로컬은 IK가 씬의 XR Rig를 직접 추적 → NetworkTransform 보간으로 인한 지연 우회.
                if (ikRig != null)
                {
                    if (_xrHead      != null) ikRig.head.vrTarget      = _xrHead;
                    if (_xrLeftHand  != null) ikRig.leftHand.vrTarget  = _xrLeftHand;
                    if (_xrRightHand != null) ikRig.rightHand.vrTarget = _xrRightHand;
                }

                foreach (var go in hideOnLocal)
                    if (go != null) go.SetActive(false);

                foreach (var bone in hideBonesOnLocal)
                    if (bone != null) bone.localScale = Vector3.zero;
            }
            else
            {
                if (ikRig != null)
                {
                    if (headAnchor      != null) ikRig.head.vrTarget      = headAnchor;
                    if (leftHandAnchor  != null) ikRig.leftHand.vrTarget  = leftHandAnchor;
                    if (rightHandAnchor != null) ikRig.rightHand.vrTarget = rightHandAnchor;
                }
                // 원격에서 로컬 InputAction 값으로 손가락이 움직이지 않도록 차단.
                if (animateOnInput != null) animateOnInput.enabled = false;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (HasStateAuthority)
                LocalPlayerSide.Clear();
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

            _xrOriginComp = origin;
            _xrOrigin = origin.transform;
            _xrHead = origin.Camera != null ? origin.Camera.transform : null;

            // XRI 기본 리그 명명 규약 우선, 폴백으로 짧은 이름.
            var rigRoot = origin.transform;
            _xrLeftHand  = FindDeep(rigRoot, "Left Controller")  ?? FindDeep(rigRoot, "LeftHand Controller")  ?? FindDeep(rigRoot, "LeftHand");
            _xrRightHand = FindDeep(rigRoot, "Right Controller") ?? FindDeep(rigRoot, "RightHand Controller") ?? FindDeep(rigRoot, "RightHand");
        }

        /// <summary>
        /// 단일 공유 방 디자인: 로컬 카메라(=내 머리)가 NetworkPlayer 스폰 위치/회전에 도착하도록
        /// 로컬 XROrigin을 옮긴다. 그렇게 하지 않으면 RoomLauncher.spawnPoints는 NetworkPlayer
        /// 프리팹 루트만 옮길 뿐, head/hand anchor는 _xrOrigin 기준으로 매 프레임 월드 좌표가 다시
        /// 산출되어 결국 모든 플레이어가 자기 XROrigin 자리에 겹쳐 그려진다 → spawnPoint 무의미.
        ///
        /// spawnPoint는 "발을 두는 바닥 위치"로 해석한다. 사용자의 헤드셋 높이(cam.y - origin.y)를
        /// 보존해야 신장이 다른 사용자에게 자연스럽고, 디자이너가 spawnPoint.y=0(바닥)을 그대로 쓸 수 있다.
        /// MoveCameraToWorldLocation은 사용자가 자기 플레이스페이스의 어디에 서 있든
        /// 카메라가 desiredWorldLocation에 정확히 위치하도록 origin 변위를 보정한다.
        /// </summary>
        void AlignLocalCameraToSpawn()
        {
            if (_xrOriginComp == null) { Debug.LogWarning("[NetworkPlayer] AlignLocalCameraToSpawn: _xrOriginComp null"); return; }
            var cam = _xrOriginComp.Camera != null ? _xrOriginComp.Camera.transform : null;
            if (cam == null) { Debug.LogWarning("[NetworkPlayer] AlignLocalCameraToSpawn: cam null"); return; }
            Debug.Log($"[NetworkPlayer] AlignLocalCameraToSpawn spawn={transform.position} offset={spawnCameraOffset} camBefore={cam.position}");

            // 사용자의 현재 머리 높이(origin 기준 상대). VR 룸스케일에서는 보통 1.4~1.9m.
            float headHeightAboveOrigin = cam.position.y - _xrOriginComp.transform.position.y;

            // 1) XZ는 spawnPoint에 스냅, Y는 spawnPoint.y + 사용자 머리 높이 (= 자연스러운 신장 유지)
            Vector3 targetHead = new Vector3(
                transform.position.x,
                transform.position.y + headHeightAboveOrigin,
                transform.position.z);

            // spawnPoint 로컬축 기준 미세 조정 — yaw 반영해서 "앞"이 spawnPoint의 forward와 일치
            targetHead += transform.right   * spawnCameraOffset.x
                        + Vector3.up        * spawnCameraOffset.y
                        + transform.forward * spawnCameraOffset.z;

            _xrOriginComp.MoveCameraToWorldLocation(targetHead);

            // 2) 카메라 yaw → 스폰 yaw. origin을 카메라 주위로 회전 → 사용자의 물리 위치 보존.
            float currentYaw = cam.eulerAngles.y;
            float targetYaw  = transform.eulerAngles.y;
            float deltaYaw   = Mathf.DeltaAngle(currentYaw, targetYaw);
            if (Mathf.Abs(deltaYaw) > 0.01f)
                _xrOriginComp.RotateAroundCameraUsingOriginUp(deltaYaw);

            Debug.Log($"[NetworkPlayer] AlignLocalCameraToSpawn AFTER origin={_xrOriginComp.transform.position} cam={cam.position} target={targetHead}");
            _debugLogTicks = 60; // 1초 동안 LateUpdate에서 추가 로그 — 덮어쓰기 추적
        }

        int _debugLogTicks;
        void LateUpdate()
        {
            if (_debugLogTicks <= 0 || _xrOriginComp == null) return;
            _debugLogTicks--;
            if (_debugLogTicks % 10 == 0)
            {
                var c = _xrOriginComp.Camera != null ? _xrOriginComp.Camera.transform.position : Vector3.zero;
                Debug.Log($"[NetworkPlayer] LateUpdate origin={_xrOriginComp.transform.position} cam={c}");
            }
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
