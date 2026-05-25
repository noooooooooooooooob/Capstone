using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class AudioButton : MonoBehaviour
{
    public AudioClip clip;
    public float pressDepth = 0.02f;
    public float pressSpeed = 10f;

    private XRSimpleInteractable interactable;
    private AudioSource audioSource;
    private Vector3 restPosition;
    private Vector3 pressedPosition;
    private bool isPressed = false;

    void Start()
    {
        restPosition = transform.localPosition;
        pressedPosition = restPosition - new Vector3(0, pressDepth, 0);

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = clip;
        audioSource.playOnAwake = false;

        interactable = GetComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(OnPressed);
    }

    void Update()
    {
        Vector3 target = isPressed ? pressedPosition : restPosition;
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, Time.deltaTime * pressSpeed);
    }

    void OnPressed(SelectEnterEventArgs args)
    {
        if (isPressed) return;
        isPressed = true;
        if (clip != null) audioSource.Play();
        StartCoroutine(ReleaseAfterDelay());
    }

    System.Collections.IEnumerator ReleaseAfterDelay()
    {
        yield return new WaitForSeconds(0.2f);
        isPressed = false;
    }

    void OnDestroy()
    {
        if (interactable != null)
            interactable.selectEntered.RemoveListener(OnPressed);
    }
}