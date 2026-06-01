using Fusion;
using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// LightBeamMirror 의 yaw 회전을 네트워크로 동기화 (Fusion Shared Mode), 권위 이전 없이.
    ///
    ///   · 지금 미러를 잡은(LightBeamMirror.IsHeld) 피어가 로컬에서 회전(LightBeamMirror 가 구동).
    ///   · 그 피어가 권위면 NetYaw 에 직접 쓰고, 권위가 아니면(게스트) RPC 로 권위(호스트)에 yaw 를 보냄.
    ///   · 권위가 NetYaw 에 실어 전파 → 잡고 있지 않은 모든 피어는 NetYaw 를 부드럽게 따라간다.
    ///
    /// RPC 는 시뮬레이션(FixedUpdateNetwork) 이 아니라 일반 Update 컨텍스트에서 보낸다(Fusion 권장 — 안정적).
    ///
    /// 요구: 같은 GameObject 에 NetworkObject + LightBeamMirror. NetworkTransform 은 두지 않는다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(LightBeamMirror))]
    [DisallowMultipleComponent]
    public class LightBeamMirrorNetworkSync : NetworkBehaviour
    {
        [Networked] public float NetYaw { get; set; }

        [Tooltip("프록시(상대)에서 받은 yaw 로 부드럽게 따라가는 속도.")]
        public float SmoothSpeed = 16f;

        [Tooltip("yaw 가 이 각도(도) 이상 변했을 때만 전송.")]
        public float SendThreshold = 0.1f;

        [Tooltip("RPC 전송 최소 간격(초). 0.04 ≈ 초당 25회.")]
        public float SendInterval = 0.04f;

        [Tooltip("진단 로그 — 호스트 콘솔에서 RPC 수신/적용을 확인.")]
        public bool verboseLog = false;

        LightBeamMirror _mirror;
        float _lastSentYaw;
        float _nextSend;

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

        // 게스트(프록시)가 잡고 있을 때 자기 yaw 를 권위에 보낸다. (일반 Update 컨텍스트에서 RPC 송신.)
        void Update()
        {
            if (Object == null || !Object.IsValid) return;
            if (Object.HasStateAuthority) return; // 권위는 FixedUpdateNetwork 에서 직접 NetYaw 기록.
            if (!Held) return;
            if (Time.time < _nextSend) return;

            float yaw = transform.localEulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(_lastSentYaw, yaw)) < SendThreshold) return;

            _lastSentYaw = yaw;
            _nextSend = Time.time + SendInterval;
            RpcPushYaw(yaw);
            if (verboseLog) Debug.Log($"[MirrorSync:{name}] 게스트 RPC 송신 yaw={yaw:F1}", this);
        }

        public override void FixedUpdateNetwork()
        {
            // 권위가 잡고 있으면 자기 회전을 직접 싣는다.
            if (Object != null && Object.IsValid && HasStateAuthority && Held)
                NetYaw = transform.localEulerAngles.y;
        }

        public override void Render()
        {
            if (Object == null || !Object.IsValid) return;
            if (Held) return; // 내가 잡고 회전 중이면 LightBeamMirror 가 구동 — 덮어쓰지 않음.

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
            if (verboseLog) Debug.Log($"[MirrorSync:{name}] 호스트 RPC 수신 → NetYaw={yaw:F1}", this);
        }
    }
}
