using UnityEngine;

namespace Capstone.Puzzle
{
    /// <summary>
    /// 라디에이터에 끼우는 보조 파이프(고장난/새 것)에 부착하는 마커 컴포넌트.
    /// 별다른 로직은 없고, <see cref="RadiatorPipeSocket"/>이 어떤 종류의 파이프가 들어왔는지
    /// 판별할 수 있게 종류만 노출한다.
    ///
    /// 그랩 자체는 XRGrabInteractable(XRI 3.4)이 처리한다.
    /// </summary>
    public enum PipeKind
    {
        Broke,
        New,
    }

    [DisallowMultipleComponent]
    public class Pipe : MonoBehaviour
    {
        [Tooltip("이 파이프가 어떤 종류인지. RadiatorPipeSocket이 [Networked] 상태를 결정할 때 사용.")]
        [SerializeField] PipeKind kind = PipeKind.Broke;

        [Tooltip("스냅 후 색상을 바꿀 대상 Renderer들 (보통 파이프 본체의 MeshRenderer). 비워두면 자식에서 자동 탐색.")]
        [SerializeField] Renderer[] coloredRenderers;

        public PipeKind Kind => kind;

        void Reset()
        {
            if (coloredRenderers == null || coloredRenderers.Length == 0)
                coloredRenderers = GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>외부(RadiatorPipeSocket)에서 호출 — 모든 머티리얼의 _BaseColor / color 를 일괄 변경.</summary>
        public void SetTint(Color color)
        {
            if (coloredRenderers == null || coloredRenderers.Length == 0)
                coloredRenderers = GetComponentsInChildren<Renderer>(true);

            foreach (var r in coloredRenderers)
            {
                if (r == null) continue;
                // sharedMaterial 을 쓰면 다른 인스턴스도 영향을 받으므로 instance(.material) 사용
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;

                    // URP / Built-in 어느 쪽이든 동작하도록 가능한 모든 컬러 슬롯에 시도
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                }
                r.materials = mats;
            }
        }
    }
}
