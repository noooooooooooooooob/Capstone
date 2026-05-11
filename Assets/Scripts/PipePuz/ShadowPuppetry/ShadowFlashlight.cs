using UnityEngine;

namespace PipePuz.ShadowPuppetry
{
    /// <summary>
    /// 사용자가 잡고 흔드는 손전등. XRGrabInteractable + Rigidbody 는 GameObject 에 별도로 붙는다.
    /// 이 컴포넌트는 광원의 출발점(Tip)과 방향만 노출하는 단순 wrapper.
    /// </summary>
    public class ShadowFlashlight : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("빛이 시작되는 점(보통 손전등 헤드 끝의 빈 Transform).")]
        public Transform Tip;

        [Tooltip("실제 Light 컴포넌트(시각용). null 이어도 그림자 계산엔 영향 없음.")]
        public Light SpotLight;

        /// <summary>그림자 계산에 사용되는 광원의 world 위치.</summary>
        public Vector3 LightPosition => Tip != null ? Tip.position : transform.position;

        /// <summary>광원이 가리키는 forward 방향(시각용; 그림자 계산엔 사용하지 않음).</summary>
        public Vector3 LightDirection => Tip != null ? Tip.forward : transform.forward;
    }
}
