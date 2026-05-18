using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 광선 emitter. <see cref="LightBeamController"/> 가 Origin / Direction / IsOn 을 읽음.
    /// </summary>
    public class LightBeamEmitter : MonoBehaviour
    {
        [Tooltip("켜져 있으면 광선 발사.")]
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
