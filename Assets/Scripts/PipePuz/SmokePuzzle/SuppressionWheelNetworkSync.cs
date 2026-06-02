using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PipePuz.SmokePuzzle
{
    /// <summary>
    /// SuppressionWheel("Valve")의 회전을 Photon Fusion 으로 동기화한다.
    /// 한 플레이어가 휠을 돌리면 상대 플레이어 화면에서도 같은 각도로 돌아간다.
    ///
    /// 휠의 시각 회전은 자식 Wheel 의 localRotation(누적각 AccumulatedCloseDeg)으로 표현되므로
    /// 루트 NetworkTransform 으로는 동기화되지 않는다. 이 컴포넌트가 그 각도(및 닫힘 속도)를 [Networked] 로 싣는다.
    ///
    /// 권위 모델(Shared Mode):
    ///   - 기본 권위자(보통 호스트)가 자기 휠 상태를 네트워크에 싣는다.
    ///   - 휠을 잡는 순간 그 피어가 StateAuthority 를 요청 → 권위자가 되어 자기 입력을 전파.
    ///   - 비권위 피어는 ExternallyDriven=true 로 로컬 입력을 멈추고 받은 값으로 휠을 돌린다.
    ///
    /// 요구: 같은 GameObject 에 NetworkObject(AllowStateAuthorityOverride ON) + SuppressionWheel.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(SuppressionWheel))]
    [DisallowMultipleComponent]
    public class SuppressionWheelNetworkSync : NetworkBehaviour
    {
        [Networked] public float NetAngle { get; set; }
        [Networked] public float NetRate { get; set; }

        SuppressionWheel _wheel;
        SmokeGauge _gauge;

        void Awake()
        {
            _wheel = GetComponent<SuppressionWheel>();
            if (_wheel != null) _wheel.selectEntered.AddListener(OnSelectEnter);
            // 이 휠을 읽는 게이지. 프록시에서 휠 각도를 주입한 직후 Pointer 를 같이 갱신하기 위해 참조.
            _gauge = FindFirstObjectByType<SmokeGauge>();
        }

        void OnDestroy()
        {
            if (_wheel != null) _wheel.selectEntered.RemoveListener(OnSelectEnter);
        }

        public override void Spawned()
        {
            if (Object != null && Object.HasStateAuthority && _wheel != null)
            {
                NetAngle = _wheel.AccumulatedCloseDeg;
                NetRate = _wheel.CurrentCloseDegPerSec;
            }
        }

        // 휠을 잡는 순간 권위 확보 — 이후 내 입력이 전파된다.
        void OnSelectEnter(SelectEnterEventArgs _)
        {
            if (Object != null && Object.IsValid && !Object.HasStateAuthority)
                Object.RequestStateAuthority();
        }

        public override void FixedUpdateNetwork()
        {
            if (_wheel == null) return;
            if (Object.HasStateAuthority)
            {
                NetAngle = _wheel.AccumulatedCloseDeg;
                NetRate = _wheel.CurrentCloseDegPerSec;
            }
        }

        public override void Render()
        {
            if (_wheel == null) return;

            bool proxy = Object == null || !Object.HasStateAuthority;
            _wheel.ExternallyDriven = proxy;
            if (proxy)
                _wheel.ApplyNetworkState(NetAngle, NetRate);

            // 휠 각도가 (권위=로컬 입력, 프록시=네트워크 주입) 확정된 직후 같은 프레임에 Pointer 도 갱신.
            // → 휠이 도는 모든 피어에서 Pointer 가 휠과 정확히 같이 움직인다.
            if (_gauge == null) _gauge = FindFirstObjectByType<SmokeGauge>();
            if (_gauge != null) _gauge.RefreshFromValve();
        }
    }
}
