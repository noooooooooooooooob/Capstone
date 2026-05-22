using UnityEngine;
using UnityEngine.AI;

public class ScorpionCreature : MonoBehaviour
{
    public float wanderRadius = 5f;
    
    private NavMeshAgent agent;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        SetNewDestination();
    }

    void Update()
    {
        if (transform.position.y < -5f)
{
    transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
}
        if (!agent.enabled) return;
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
            SetNewDestination();
    }

    void SetNewDestination()
    {
        if (!agent.enabled) return;
        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, 1))
            agent.SetDestination(hit.position);
    }
}