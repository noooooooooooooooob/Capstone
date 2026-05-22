using UnityEngine;
using UnityEngine.AI;

public class BoxerCreature : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 25f;
    public float stunDuration = 2f;

    private NavMeshAgent agent;
    private int currentTarget = 0;
    private int direction = 1;
    private bool isStunned = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = speed;
        agent.angularSpeed = 9999f;
        agent.acceleration = 9999f;
        agent.autoBraking = false;
        agent.SetDestination(waypoints[0].position);
    }

    void Update()
    {
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
        yield return new WaitForSeconds(stunDuration);
        isStunned = false;
        agent.speed = speed;
        agent.SetDestination(waypoints[currentTarget].position);
    }
}