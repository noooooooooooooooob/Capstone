using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GlassBreak : MonoBehaviour
{
    public GameObject hintPaper;
    private bool isBroken = false;

    void Start()
    {
        // Shard들 전부 비활성화
        foreach (Transform child in transform)
            if (child.name.Contains("Shard"))
                child.gameObject.SetActive(false);
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

        // 병 메시 숨기기
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // Shard 활성화
        foreach (Transform child in transform)
            if (child.name.Contains("Shard"))
                child.gameObject.SetActive(true);

        // 힌트 페이퍼 활성화
        if (hintPaper != null)
        {
            hintPaper.SetActive(true);
            XRGrabInteractable grab = hintPaper.GetComponent<XRGrabInteractable>();
            if (grab != null) grab.enabled = true;
        }
    }
}