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
        // 목적지 거의 도착하면 바로 다음 목적지
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
            SetNewDestination();
    }

    void SetNewDestination()
    {
        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += transform.position;
        
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, 1))
            agent.SetDestination(hit.position);
    }
}