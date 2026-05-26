using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Stage1
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class Stage1DoorDebugButton : MonoBehaviour
    {
        [SerializeField] private Stage1SlidingDoor door;
        [SerializeField] private bool toggleInsteadOfOpen;
        [SerializeField] private float pressDepth = 0.02f;
        [SerializeField] private float cooldown = 0.35f;
        [SerializeField] private Color normalColor = new Color(0.2f, 0.4f, 1f, 1f);
        [SerializeField] private Color pressedColor = Color.white;

        private XRSimpleInteractable interactable;
        private Renderer buttonRenderer;
        private Vector3 originalLocalPosition;
        private bool coolingDown;

        private void Awake()
        {
            originalLocalPosition = transform.localPosition;
            interactable = GetComponent<XRSimpleInteractable>();
            buttonRenderer = GetComponent<Renderer>();
            ApplyColor(normalColor);
        }

        private void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelected);
        }

        private void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelected);
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            if (!coolingDown)
            {
                StartCoroutine(PressRoutine());
            }
        }

        private IEnumerator PressRoutine()
        {
            coolingDown = true;
            transform.localPosition = originalLocalPosition - transform.up * pressDepth;
            ApplyColor(pressedColor);

            if (door != null)
            {
                if (toggleInsteadOfOpen)
                {
                    door.ToggleDoor();
                }
                else
                {
                    door.OpenDoor();
                }
            }

            yield return new WaitForSeconds(0.12f);

            transform.localPosition = originalLocalPosition;
            ApplyColor(normalColor);

            yield return new WaitForSeconds(cooldown);
            coolingDown = false;
        }

        private void ApplyColor(Color color)
        {
            if (buttonRenderer != null)
            {
                buttonRenderer.material.color = color;
            }
        }
    }
}
