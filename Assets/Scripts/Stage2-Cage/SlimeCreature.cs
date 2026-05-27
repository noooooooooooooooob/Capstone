using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class SlimeCreature : MonoBehaviour
{
    public float followDistance = 3f;
    public float speed = 2f;
    public AudioClip grabSound;

    private NavMeshAgent agent;
    private Transform target;
    private Animator anim;
    private AudioSource audioSource;
    private XRGrabInteractable grab;
    private bool isCaged = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = speed;
        target = GameObject.FindWithTag("Scorpion").transform;
        anim = GetComponent<Animator>();
        grab = GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = grabSound;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
    }

    void Update()
    {
        if (transform.position.y < -5f)
            transform.position = new Vector3(transform.position.x, 0f, transform.position.z);

        if (!agent.enabled || !agent.isOnNavMesh) return;
        if (target == null) return;

        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > followDistance)
            agent.SetDestination(target.position);
        else
            agent.ResetPath();
    }

void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
{
    agent.enabled = false;
    if (anim != null) anim.SetInteger("Grabbed", 1);
    if (audioSource != null && grabSound != null) audioSource.Play();
}

    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        if (anim != null) anim.SetInteger("Grabbed", 0);
        if (audioSource != null) audioSource.Stop();

        if (isCaged) return;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        agent.enabled = true;
    }

    public void SetCaged()
    {
        isCaged = true;
        agent.enabled = false;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        if (anim != null) anim.SetTrigger("Caged");
        if (audioSource != null) audioSource.Stop();
    }

    public void SetFree()
    {
        isCaged = false;
        agent.enabled = true;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;
        if (anim != null) anim.SetInteger("Grabbed", 0);
    }
}