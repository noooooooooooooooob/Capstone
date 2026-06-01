using Fusion;
using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// LightBeamMirror 의 yaw 회전을 네트워크로 동기화 (Fusion Shared Mode).
    ///
    /// 설계:
    ///   - NetworkTransform 을 쓰면 권위 측에서도 보간이 회전을 덮어써 회전이 안 됐다 → yaw 각도만 [Networked].
    ///   - 게스트의 StateAuthority 요청이 이 프로젝트에선 잘 안 먹히므로, 권위 이전에 의존하지 않는다:
    ///       · 지금 미러를 잡은(LightBeamMirror.IsHeld) 피어가 로컬에서 회전(LightBeamMirror 가 구동).
    ///       · 그 피어가 권위면 NetYaw 에 직접 쓰고, 권위가 아니면(게스트) RPC 로 권위(호스트)에 yaw 를 보냄.
    ///       · 권위가 NetYaw 에 실어 전파 → 잡고 있지 않은 모든 피어는 NetYaw 를 부드럽게 따라간다.
    ///   - "잡고 있는지"는 컴패니언 자체 후킹이 아니라 LightBeamMirror.IsHeld 를 본다(회전을 구동하는 바로 그 상태라 확실).
    ///
    /// 요구: 같은 GameObject 에 NetworkObject + LightBeamMirror. NetworkTransform 은 두지 않는다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(LightBeamMirror))]
    [DisallowMultipleComponent]
    public class LightBeamMirrorNetworkSync : NetworkBehaviour
    {
        [Networked] public float NetYaw { get; set; }

        [Tooltip("프록시(상대)에서 받은 yaw 로 부드럽게 따라가는 속도. 클수록 빠르고 덜 부드러움.")]
        public float SmoothSpeed = 16f;

        [Tooltip("yaw 가 이 각도(도) 이상 변했을 때만 RPC 전송 — 불필요한 네트워크 트래픽 방지.")]
        public float SendThreshold = 0.1f;

        LightBeamMirror _mirror;
        float _lastSentYaw;

        void Awake()
        {
            _mirror = GetComponent<LightBeamMirror>();
        }

        bool Held => _mirror != null && _mirror.IsHeld;

        public override void Spawned()
        {
            if (Object != null && Object.HasStateAuthority)
                NetYaw = transform.localEulerAngles.y;
        }

        public override void FixedUpdateNetwork()
        {
            if (Object == null || !Object.IsValid) return;

            if (HasStateAuthority)
            {
                // 권위가 잡고 있으면 자기 회전을 직접 싣는다. (안 잡고 있으면 프록시의 RpcPushYaw 가 NetYaw 를 갱신.)
                if (Held) NetYaw = transform.localEulerAngles.y;
            }
            else
            {
                // 게스트(프록시)가 잡고 있으면 자기 회전을 권위에 보낸다(권위 이전 불필요).
                if (Held)
                {
                    float yaw = transform.localEulerAngles.y;
                    if (Mathf.Abs(Mathf.DeltaAngle(_lastSentYaw, yaw)) >= SendThreshold)
                    {
                        _lastSentYaw = yaw;
                        RpcPushYaw(yaw);
                    }
                }
            }
        }

        public override void Render()
        {
            if (Object == null || !Object.IsValid) return;
            if (Held) return; // 내가 잡고 회전 중이면 LightBeamMirror 가 구동 — 덮어쓰지 않음.

            // 잡고 있지 않은 피어는 받은 yaw 를 부드럽게 따라간다(매 틱 스냅 시 렉 방지).
            var e = transform.localEulerAngles;
            float t = 1f - Mathf.Exp(-SmoothSpeed * Time.deltaTime);
            float y = Mathf.LerpAngle(e.y, NetYaw, t);
            transform.localEulerAngles = new Vector3(e.x, y, e.z);
        }

        // 잡고 있는 피어 → 권위(호스트). 권위가 NetYaw 에 실어 전 피어로 전파.
        // RpcSources.All = 이 프로젝트에서 검증된 패턴(BatteryDispenser.RpcRequestSpawn 과 동일).
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RpcPushYaw(float yaw)
        {
            NetYaw = yaw;
        }
    }
}
