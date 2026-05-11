using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.ShadowPuppetry
{
    /// <summary>
    /// 벽 높은 위치의 단순 푸쉬 버튼. XRSimpleInteractable 의 selectEntered 가
    /// 한 번이라도 들어오면 OnPressed 가 발행되고 머티리얼이 Active 로 바뀐다.
    /// </summary>
    public class ShadowSwitch : MonoBehaviour
    {
        [Header("Refs")]
        public XRBaseInteractable Interactable;
        public Renderer ButtonRenderer;

        [Header("Materials")]
        public Material InactiveMaterial;
        public Material ActiveMaterial;

        [Header("Events")]
        public UnityEvent OnPressed;

        public bool IsPressed { get; private set; }

        void Awake()
        {
            if (Interactable != null)
                Interactable.selectEntered.AddListener(OnSelectEntered);
            ApplyMaterial();
        }

        void OnDestroy()
        {
            if (Interactable != null)
                Interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (IsPressed) return;
            IsPressed = true;
            ApplyMaterial();
            OnPressed?.Invoke();
        }

        void ApplyMaterial()
        {
            if (ButtonRenderer == null) return;
            var mat = IsPressed ? ActiveMaterial : InactiveMaterial;
            if (mat != null) ButtonRenderer.sharedMaterial = mat;
        }
    }
}
