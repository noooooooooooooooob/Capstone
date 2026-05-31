using Fusion;
using UnityEngine;

namespace PipePuz.MiniGame2
{
    /// <summary>
    /// PipeMiniGame2Pipe 의 90° 회전(Rotation 0~3)을 Photon Fusion 으로 동기화한다.
    ///
    /// NetworkGrabbableSync 는 오브젝트의 "위치/회전(루트 Transform)"만 동기화하므로,
    /// 파이프 메시의 90° 단계 회전(PipeRoot 자식의 localRotation)은 상대 피어에게 보이지 않았다.
    /// 이 컴포넌트가 그 누락을 메운다 — 한 플레이어가 트리거로 회전시키면 상대도 동일하게 회전한 모습을 본다.
    ///
    /// 동작:
    ///   - 잡고 회전할 때 PipeMiniGame2Pipe.OnActivated → RotationSync.RequestRotate() 로 위임.
    ///   - RequestRotate 는 권위(StateAuthority)를 확보한 뒤 [Networked] NetRotation 을 갱신.
    ///     (파이프를 잡는 순간 NetworkGrabbableSync 가 이미 권위를 요청하므로 회전 시점엔 보통 권위 보유)
    ///   - NetRotation 이 바뀌면 OnChangedRender 가 모든 피어에서 메시 비주얼을 적용.
    ///
    /// 요구: 같은 GameObject 에 NetworkObject + PipeMiniGame2Pipe. (NetworkObject 의
    ///       AllowStateAuthorityOverride 는 NetworkGrabbableSync 셋업에서 이미 켜져 있음)
    /// </summary>
    [RequireComponent(typeof(PipeMiniGame2Pipe))]
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class PipeRotationSync : NetworkBehaviour
    {
        [Networked, OnChangedRender(nameof(OnNetRotationChanged))]
        public int NetRotation { get; set; }

        PipeMiniGame2Pipe _pipe;

        // 권위를 아직 못 받았을 때 회전 요청을 보류했다가, 권위 확보 후 네트워크에 싣는다.
        int _pendingRotation = -1;

        void Awake()
        {
            _pipe = GetComponent<PipeMiniGame2Pipe>();
            if (_pipe != null) _pipe.RotationSync = this;
        }

        public override void Spawned()
        {
            if (Object != null && Object.HasStateAuthority)
            {
                // 권위자는 현재 로컬 회전값을 네트워크 초기값으로 싣는다.
                NetRotation = _pipe != null ? _pipe.Rotation : 0;
            }
            else
            {
                // 프록시는 받은 값으로 비주얼을 맞춘다(늦게 들어온 피어 포함).
                ApplyFromNet();
            }
        }

        /// <summary>로컬에서 트리거로 회전 요청 — 90° CW. 권위 확보 후 네트워크 값 갱신.</summary>
        public void RequestRotate()
        {
            int current = _pipe != null ? _pipe.Rotation : NetRotation;
            int next = (current + 1) % 4;

            // 로컬 즉시 반영(반응성). 네트워크 확정은 아래에서.
            ApplyRotation(next);

            if (Object == null || !Object.IsValid)
                return;

            if (Object.HasStateAuthority)
            {
                NetRotation = next;
                _pendingRotation = -1;
            }
            else
            {
                // 권위 요청 후, 확보되면 FixedUpdateNetwork 에서 싣는다.
                _pendingRotation = next;
                Object.RequestStateAuthority();
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (_pendingRotation >= 0 && Object.HasStateAuthority)
            {
                NetRotation = _pendingRotation;
                _pendingRotation = -1;
            }
        }

        void OnNetRotationChanged() => ApplyFromNet();

        void ApplyFromNet() => ApplyRotation(NetRotation);

        void ApplyRotation(int rotation)
        {
            if (_pipe != null) _pipe.SetRotationVisual(rotation);
        }
    }
}
