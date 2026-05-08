using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Attach to GlassButton or ActivateButton cube
// Also add XRSimpleInteractable to the same object
[RequireComponent(typeof(XRSimpleInteractable))]
public class MelterButton : MonoBehaviour
{
    public enum ButtonType { Glass, Activate }

    [Header("Settings")]
    public ButtonType buttonType;
    public BatteryMelter melter;
    public float cooldown = 1.0f;

    XRSimpleInteractable interactable;
    bool onCooldown = false;

    void Start()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        // selectEntered = 트리거 당겨야 반응
        // hoverEntered로 바꾸면 가리키기만 해도 즉각 반응 (딜레이 거의 없음)
        interactable.selectEntered.AddListener(OnPressed);
    }

    void OnPressed(SelectEnterEventArgs args)
    {
        if (onCooldown || melter == null) return;
        StartCoroutine(CooldownRoutine());

        if (buttonType == ButtonType.Glass)
            melter.OnGlassButtonPressed();
        else
            melter.OnActivateButtonPressed();
    }

    System.Collections.IEnumerator CooldownRoutine()
    {
        onCooldown = true;
        yield return new WaitForSeconds(cooldown);
        onCooldown = false;
    }

    void OnDestroy()
    {
        if (interactable != null)
            interactable.selectEntered.RemoveListener(OnPressed);
    }
}