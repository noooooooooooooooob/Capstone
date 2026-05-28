using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class BoxerCreature : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 25f;
    public float stunDuration = 2f;
    public AudioClip grabSound;

    private NavMeshAgent agent;
    private int currentTarget = 0;
    private int direction = 1;
    private bool isStunned = false;
    private XRGrabInteractable grab;
    private Animator anim;
    private AudioSource audioSource;
    private bool isCaged = false;
    private NetworkGrabbableSync netGrab;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponent<Animator>();
        agent.speed = speed;
        agent.angularSpeed = 9999f;
        agent.acceleration = 9999f;
        agent.autoBraking = false;
        if (agent.isOnNavMesh) agent.SetDestination(waypoints[0].position);

        grab = GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            grab.enabled = false;
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = grabSound;
        audioSource.loop = true;
        audioSource.playOnAwake = false;

        // 그랩 사운드를 네트워크 그랩 상태에 연동 → 모든 피어에서 재생.
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
        if (isStunned) return;

        if (Vector3.Distance(transform.position, waypoints[currentTarget].position) < 2f)
        {
            currentTarget += direction;
            if (currentTarget >= waypoints.Length)
            {
                currentTarget = waypoints.Length - 2;
                direction = -1;
            }
            else if (currentTarget < 0)
            {
                currentTarget = 1;
                direction = 1;
            }
            agent.SetDestination(waypoints[currentTarget].position);
        }
    }

    void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        if (netGrab == null) ApplyGrabbed();
    }

    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        if (netGrab == null) ApplyUngrabbed();
    }

    // onGrab(네트워크 IsGrabbed=true) → 모든 피어에서 호출. 그랩 사운드 동기.
    void ApplyGrabbed()
    {
        if (audioSource != null && grabSound != null && !audioSource.isPlaying)
            audioSource.Play();
    }

    void ApplyUngrabbed()
    {
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

    public void GetStunned()
    {
        if (!isStunned)
            StartCoroutine(Stun());
    }

    System.Collections.IEnumerator Stun()
    {
        isStunned = true;
        agent.ResetPath();
        agent.speed = 0;
        if (grab != null) grab.enabled = true;

        // play GetHit once, then transition to Dizzy
        if (anim != null) anim.SetTrigger("Hit");
        yield return new WaitForSeconds(stunDuration);

        if (anim != null) anim.SetTrigger("Stunned");
        if (grab != null && grab.isSelected)
            yield return new WaitUntil(() => !grab.isSelected);

        if (isCaged) yield break;

        isStunned = false;
        agent.speed = speed;
        if (grab != null) grab.enabled = false;
        if (anim != null) anim.SetTrigger("Recovered");
        if (agent.isOnNavMesh) agent.SetDestination(waypoints[currentTarget].position);
    }

    public void SetCaged()
    {
        isCaged = true;
        if (anim != null) anim.SetTrigger("Caged");
        if (audioSource != null) audioSource.Stop();
    }

    public void SetFree()
    {
        isCaged = false;
        isStunned = false;
        agent.enabled = true;
        agent.speed = speed;
        if (grab != null) grab.enabled = false;
        if (agent.isOnNavMesh) agent.SetDestination(waypoints[currentTarget].position);
    }
}