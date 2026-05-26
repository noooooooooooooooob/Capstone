using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class HintPaper : MonoBehaviour
{
    public Transform attachPoint;
    private bool isDropped = false;

    void Awake()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        XRGrabInteractable grab = GetComponent<XRGrabInteractable>();
        if (grab != null) grab.enabled = false;
    }

    void Update()
    {
        if (!isDropped && attachPoint != null)
        {
            transform.position = attachPoint.position;
            transform.rotation = attachPoint.rotation;
        }
    }

    public void Drop()
    {
        if (isDropped) return;
        isDropped = true;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.WakeUp();
        }

        XRGrabInteractable grab = GetComponent<XRGrabInteractable>();
        if (grab != null) grab.enabled = true;
    }
}