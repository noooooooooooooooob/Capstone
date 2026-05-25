using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GlassBreak : MonoBehaviour
{
    public GameObject hintPaper;
    private bool isBroken = false;

    void Start()
    {
        foreach (Transform child in transform)
            if (child.name.Contains("Shard"))
                child.gameObject.SetActive(false);

        if (hintPaper != null)
        {
            hintPaper.SetActive(false);
            XRGrabInteractable grab = hintPaper.GetComponent<XRGrabInteractable>();
            if (grab != null) grab.enabled = false;
        }

        // make sure jar starts kinematic and collider enabled
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isBroken) return;
        if (collision.relativeVelocity.magnitude < 2f) return;
        Break();
    }

    void Break()
    {
        isBroken = true;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        foreach (Transform child in transform)
        {
            if (child.name.Contains("Shard"))
            {
                child.gameObject.SetActive(true);
                Rigidbody shardRb = child.GetComponent<Rigidbody>();
                if (shardRb != null)
                {
                    shardRb.isKinematic = false;
                    shardRb.useGravity = true;
                    shardRb.AddExplosionForce(150f, transform.position, 0.5f);
                }
            }
        }

        if (hintPaper != null)
        {
            hintPaper.SetActive(true);
            hintPaper.transform.SetParent(null);

            Rigidbody paperRb = hintPaper.GetComponent<Rigidbody>();
            if (paperRb != null)
            {
                paperRb.isKinematic = false;
                paperRb.useGravity = true;
            }

            Collider paperCol = hintPaper.GetComponent<Collider>();
            if (paperCol != null) paperCol.enabled = true;

            XRGrabInteractable grab = hintPaper.GetComponent<XRGrabInteractable>();
            if (grab != null) grab.enabled = true;
        }
    }
}