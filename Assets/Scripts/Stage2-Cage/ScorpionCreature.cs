using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ScorpionCreature : MonoBehaviour
{
    public float wanderRadius = 5f;
    public AudioClip grabSound;

    private NavMeshAgent agent;
    private Animator anim;
    private AudioSource audioSource;
    private XRGrabInteractable grab;
    private bool isCaged = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponent<Animator>();
        SetNewDestination();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = grabSound;
        audioSource.loop = true;
        audioSource.playOnAwake = false;

        grab = GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }
    }

    void Update()
    {
        if (transform.position.y < -5f)
            transform.position = new Vector3(transform.position.x, 0f, transform.position.z);

        if (!agent.enabled || !agent.isOnNavMesh) return;
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
            SetNewDestination();
    }

    void SetNewDestination()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, 1))
            agent.SetDestination(hit.position);
    }

    void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        agent.enabled = false;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
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
        SetNewDestination();
    }

    public void SetCaged()
    {
        isCaged = true;
        agent.enabled = false;
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
        SetNewDestination();
    }
}