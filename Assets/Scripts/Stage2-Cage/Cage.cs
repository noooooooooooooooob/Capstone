using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class Cage : MonoBehaviour
{
    public string correctCreatureTag;
    public TextMeshProUGUI label;
    public GameObject door;

    private GameObject capturedCreature;
    private bool isLocked = false;

    void OnTriggerEnter(Collider other)
    {
        if (isLocked) return;

        string[] creatureTags = { "Flying", "Scorpion", "Slime", "Boxer" };
        bool isCreature = false;
        foreach (string tag in creatureTags)
        {
            if (other.CompareTag(tag)) { isCreature = true; break; }
        }
        if (!isCreature) return;

        capturedCreature = other.transform.root.gameObject;

        capturedCreature.transform.position = transform.position;

        NavMeshAgent agent = capturedCreature.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        Rigidbody rb = capturedCreature.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        Animator anim = capturedCreature.GetComponent<Animator>();
        if (anim != null) anim.SetTrigger("Idle");

        FlyingCreature flying = capturedCreature.GetComponent<FlyingCreature>();
        if (flying != null) flying.SetCaged();

        if (other.transform.root.CompareTag(correctCreatureTag))
        {
            label.color = Color.green;
            isLocked = true;
        }
        else
        {
            label.color = Color.red;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (isLocked) return;
        if (capturedCreature == null) return;
        if (other.transform.root.gameObject != capturedCreature) return;

        label.color = Color.white;

        NavMeshAgent agent = capturedCreature.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = true;

        Rigidbody rb = capturedCreature.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        FlyingCreature flying = capturedCreature.GetComponent<FlyingCreature>();
        if (flying != null) flying.SetFree();

        capturedCreature = null;
    }
}