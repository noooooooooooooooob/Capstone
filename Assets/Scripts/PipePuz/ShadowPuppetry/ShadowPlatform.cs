using UnityEngine;

namespace PipePuz.ShadowPuppetry
{
    /// <summary>
    /// 한 그림자에 대응되는 플랫폼. 매 프레임 컨트롤러가 Apply(...) 를 호출해
    /// PlatformBody (BoxCollider + 시각) 와 ShadowVisualQuad (벽에 그려질 다크 quad) 의
    /// 위치/회전/크기를 갱신한다.
    /// </summary>
    public class ShadowPlatform : MonoBehaviour
    {
        [Tooltip("실제로 사용자가 올라설 박스 콜라이더가 붙은 자식. TeleportationArea 도 같이.")]
        public Transform PlatformBody;

        [Tooltip("벽 위에 그려질 검은 quad 자식 (Quad mesh).")]
        public Transform ShadowVisualQuad;

        public void Apply(Vector3 platformPos, Quaternion platformRot, Vector3 platformSize,
                          Vector3 visualPos, Quaternion visualRot, Vector3 visualSize)
        {
            if (PlatformBody != null)
            {
                PlatformBody.SetPositionAndRotation(platformPos, platformRot);
                PlatformBody.localScale = platformSize;
            }
            if (ShadowVisualQuad != null)
            {
                ShadowVisualQuad.SetPositionAndRotation(visualPos, visualRot);
                ShadowVisualQuad.localScale = visualSize;
            }
        }

        public void SetActive(bool active)
        {
            if (PlatformBody != null && PlatformBody.gameObject.activeSelf != active)
                PlatformBody.gameObject.SetActive(active);
            if (ShadowVisualQuad != null && ShadowVisualQuad.gameObject.activeSelf != active)
                ShadowVisualQuad.gameObject.SetActive(active);
        }
    }
}
