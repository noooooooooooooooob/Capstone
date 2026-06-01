using Fusion;
using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// BeamGatedDoor 의 열림/닫힘 상태를 네트워크로 동기화 (Fusion Shared Mode).
    ///
    /// 문 개폐 조건(빔이 Receiver 에 닿음 + 두 플레이어 근접)은 권위(호스트)가 결정하고,
    /// 그 결과(열림 여부)를 [Networked] 로 실어 모든 피어가 동일하게 문을 연다.
    ///   - 권위(호스트): BeamGatedDoor 가 로컬 계산한 ShouldBeOpen 을 매 틱 NetOpen 에 싣는다.
    ///   - 프록시(게스트): BeamGatedDoor.UseExternalOpen=true 로 두고 NetOpen 을 ExternalOpenValue 로 적용
    ///     → 빔 적중 타이밍이 피어마다 미세하게 달라도 문은 항상 호스트 판정대로 양쪽이 함께 열린다.
    ///
    /// 게스트의 권위 이전이 필요 없다(호스트가 단독 결정 → 전파). 따라서 AllowStateAuthorityOverride/버전비트와 무관.
    ///
    /// 요구: 같은 GameObject 에 NetworkObject + BeamGatedDoor.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(BeamGatedDoor))]
    [DisallowMultipleComponent]
    public class BeamGatedDoorNetworkSync : NetworkBehaviour
    {
        [Networked, OnChangedRender(nameof(OnNetOpenChanged))]
        public NetworkBool NetOpen { get; set; }

        BeamGatedDoor _door;

        void Awake()
        {
            _door = GetComponent<BeamGatedDoor>();
        }

        public override void Spawned()
        {
            ApplyGate();
            if (!HasStateAuthority) ApplyToDoor(); // 늦게 합류 시 현재 상태 즉시 반영.
        }

        public void StateAuthorityChanged() => ApplyGate();

        void ApplyGate()
        {
            bool authority = Object != null && Object.IsValid && HasStateAuthority;
            // 권위는 로컬 계산, 프록시는 네트워크 값(ExternalOpen)을 따른다.
            if (_door != null) _door.UseExternalOpen = !authority;
        }

        public override void FixedUpdateNetwork()
        {
            if (_door == null) return;
            // 권위: 로컬 계산 결과를 네트워크에 싣는다.
            if (Object != null && Object.IsValid && HasStateAuthority)
            {
                bool open = _door.ShouldBeOpen;
                if (open != (bool)NetOpen) NetOpen = open;
            }
        }

        void OnNetOpenChanged()
        {
            if (HasStateAuthority) return;
            ApplyToDoor();
        }

        void ApplyToDoor()
        {
            if (_door == null) return;
            _door.UseExternalOpen = true;
            _door.ExternalOpenValue = NetOpen;
        }
    }
}
