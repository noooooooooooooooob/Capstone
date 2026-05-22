using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class BoxerCreature : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 25f;
    public float stunDuration = 2f;

    private NavMeshAgent agent;
    private int currentTarget = 0;
    private int direction = 1;
    private bool isStunned = false;
    private XRGrabInteractable grab;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = speed;
        agent.angularSpeed = 9999f;
        agent.acceleration = 9999f;
        agent.autoBraking = false;
        agent.SetDestination(waypoints[0].position);
        grab = GetComponent<XRGrabInteractable>();
        if (grab != null) grab.enabled = false;
    }

    void Update()
    {
        if (transform.position.y < -5f)
{
    transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
}
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

        yield return new WaitForSeconds(stunDuration);

        if (grab != null && grab.isSelected)
            yield return new WaitUntil(() => !grab.isSelected);

        if (!agent.enabled) yield break; // 케이지 안에 있으면 복귀 안함
        
        isStunned = false;
        agent.speed = speed;
        if (grab != null) grab.enabled = false;
        agent.SetDestination(waypoints[currentTarget].position);
    }
}