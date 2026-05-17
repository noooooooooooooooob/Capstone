using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 광선 emitter. <see cref="LightBeamController"/> 가 매 프레임 이 컴포넌트의
    /// Origin / Direction / IsOn 을 읽어 raycast 시작점으로 사용.
    ///
    /// 기본적으로 transform.forward 방향, transform.position 위치. 별도 <see cref="EmissionPoint"/>
    /// 자식을 지정하면 그쪽 좌표/방향 사용 (예: 총구 끝, 빔 출구 lens 등).
    /// </summary>
    public class LightBeamEmitter : MonoBehaviour
    {
        [Tooltip("켜져 있으면 광선 발사. 토글 가능하게 외부에서 호출.")]
        public bool IsOn = true;

        [Tooltip("이 자식 트랜스폼의 position/forward 를 사용. 비워두면 emitter 자기 transform.")]
        public Transform EmissionPoint;

        public Vector3 Origin
            => EmissionPoint != null ? EmissionPoint.position : transform.position;

        public Vector3 Direction
            => (EmissionPoint != null ? EmissionPoint.forward : transform.forward).normalized;

        public void Toggle() => IsOn = !IsOn;
        public void TurnOn() => IsOn = true;
        public void TurnOff() => IsOn = false;
    }
}
