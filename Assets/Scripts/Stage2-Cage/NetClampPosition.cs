using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class NetClampPosition : MonoBehaviour
{
    public float maxY = 2.5f; // adjust to just below your ceiling
    public float minY = 0.3f; // adjust to just above your floor

    private XRGrabInteractable grab;

    void Start()
    {
        grab = GetComponent<XRGrabInteractable>();
    }

    void Update()
    {
        Vector3 pos = transform.position;
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        transform.position = pos;
    }
}