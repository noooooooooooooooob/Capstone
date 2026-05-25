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

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponent<Animator>();
        agent.speed = speed;
        agent.angularSpeed = 9999f;
        agent.acceleration = 9999f;
        agent.autoBraking = false;
        agent.SetDestination(waypoints[0].position);

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
    }

    void Update()
    {
        if (transform.position.y < -5f)
            transform.position = new Vector3(transform.position.x, 0f, transform.position.z);

        if (!agent.enabled) return;
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
        if (audioSource != null && grabSound != null) audioSource.Play();
    }

    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        if (audioSource != null) audioSource.Stop();
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
        agent.SetDestination(waypoints[currentTarget].position);
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
        agent.SetDestination(waypoints[currentTarget].position);
    }
}