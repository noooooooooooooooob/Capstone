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
    private NetworkGrabbableSync netGrab;

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

        // 그랩 애니메이션/사운드를 네트워크 그랩 상태에 연동 → 모든 피어에서 재생.
        netGrab = GetComponent<NetworkGrabbableSync>();
        if (netGrab != null)
        {
            netGrab.onGrab.AddListener(ApplyGrabbed);
            netGrab.onUngrab.AddListener(ApplyUngrabbed);
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
        if (netGrab == null) ApplyGrabbed();
    }

    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        if (netGrab == null) ApplyUngrabbed();

        if (isCaged) return;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        agent.enabled = true;
        SetNewDestination();
    }

    // 네트워크 그랩 → 모든 피어에서 호출. 그랩 애니메이션 + 사운드 동기.
    void ApplyGrabbed()
    {
        if (anim != null) anim.SetInteger("Grabbed", 1);
        if (audioSource != null && grabSound != null && !audioSource.isPlaying)
            audioSource.Play();
    }

    void ApplyUngrabbed()
    {
        if (anim != null) anim.SetInteger("Grabbed", 0);
        if (audioSource != null) audioSource.Stop();
    }

    void OnDestroy()
    {
        if (netGrab != null)
        {
            netGrab.onGrab.RemoveListener(ApplyGrabbed);
            netGrab.onUngrab.RemoveListener(ApplyUngrabbed);
        }
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