using UnityEngine;

namespace PipePuz.ShadowPuppetry
{
    /// <summary>
    /// 그림자를 만드는 캐스터. Renderer.bounds 의 8 코너를 노출 — 컨트롤러가 이 점들을
    /// 광원으로부터 벽평면으로 투영해 그림자 bounding rectangle 을 만든다.
    /// </summary>
    public class ShadowCaster : MonoBehaviour
    {
        [Tooltip("bounds 추출용 Renderer. 일반적으로 캐스터의 mesh renderer.")]
        public Renderer CasterRenderer;

        public Bounds WorldBounds
        {
            get
            {
                if (CasterRenderer != null) return CasterRenderer.bounds;
                return new Bounds(transform.position, Vector3.one * 0.2f);
            }
        }

        /// <summary>월드 좌표계의 AABB 8 코너.</summary>
        public Vector3[] GetBoundsCorners()
        {
            var b = WorldBounds;
            var min = b.min;
            var max = b.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z),
            };
        }
    }
}
