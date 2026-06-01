using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// LightBeamMirror 의 yaw 회전을 네트워크로 동기화 (Fusion Shared Mode) — SuppressionWheel 과 동일 패턴.
    ///
    /// 왜 NetworkTransform 을 안 쓰나:
    ///   거울은 LightBeamMirror.Update 가 "잡고 있을 때만" localEulerAngles.y 를 직접 회전시킨다.
    ///   NetworkTransform 을 붙이면 권위 측에서도 보간/렌더가 매 프레임 트랜스폼을 덮어써(특히 거울은
    ///   knob 처럼 LateUpdate 에서 재적용을 안 하므로) "연결 후 회전이 안 되는" 증상이 났다.
    ///   그래서 트랜스폼 대신 yaw 각도만 [Networked] 로 싣는다(이 프로젝트의 SuppressionWheelNetworkSync 와 동일).
    ///
    /// 권위 모델:
    ///   - 기본 권위자(보통 호스트=마스터 클라이언트)가 자기 거울 yaw 를 네트워크에 싣는다.
    ///   - 거울을 잡는 순간 그 피어가 StateAuthority 를 요청 → 권위자가 되어 자기 회전을 전파.
    ///   - 비권위 피어는 받은 yaw 를 그대로 적용(거울은 안 잡고 있으니 로컬 회전 로직은 no-op).
    ///
    /// 요구: 같은 GameObject 에 NetworkObject(AllowStateAuthorityOverride+버전비트=786433) + LightBeamMirror
    ///       + XRSimpleInteractable. NetworkTransform 은 두지 않는다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(LightBeamMirror))]
    [DisallowMultipleComponent]
    public class LightBeamMirrorNetworkSync : NetworkBehaviour
    {
        [Networked] public float NetYaw { get; set; }

        XRBaseInteractable _interactable;

        void Awake()
        {
            _interactable = GetComponent<XRBaseInteractable>();
            if (_interactable != null) _interactable.selectEntered.AddListener(OnSelect);
        }

        void OnDestroy()
        {
            if (_interactable != null) _interactable.selectEntered.RemoveListener(OnSelect);
        }

        // 거울을 잡는 순간 권위 확보 → 이후 내 회전이 전파된다.
        void OnSelect(SelectEnterEventArgs _)
        {
            if (Object != null && Object.IsValid && !Object.HasStateAuthority)
                Object.RequestStateAuthority();
        }

        public override void Spawned()
        {
            if (Object != null && Object.HasStateAuthority)
                NetYaw = transform.localEulerAngles.y;
        }

        public override void FixedUpdateNetwork()
        {
            // 권위 측: 현재 거울 yaw 를 네트워크에 싣는다(LightBeamMirror 가 회전을 구동).
            if (Object != null && Object.IsValid && Object.HasStateAuthority)
                NetYaw = transform.localEulerAngles.y;
        }

        public override void Render()
        {
            // 비권위(프록시) 측만 받은 yaw 를 적용. 권위 측은 LightBeamMirror 가 직접 회전하므로 건드리지 않는다.
            if (Object == null || !Object.IsValid || Object.HasStateAuthority) return;
            var e = transform.localEulerAngles;
            transform.localEulerAngles = new Vector3(e.x, NetYaw, e.z);
        }
    }
}
