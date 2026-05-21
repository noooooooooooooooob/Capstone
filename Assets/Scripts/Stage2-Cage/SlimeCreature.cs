using UnityEngine;
using UnityEngine.AI;

public class SlimeCreature : MonoBehaviour
{
    public float followDistance = 3f; // 이 거리 안으로 안 들어감
    public float speed = 2f; // 전갈보다 느리게
    
    private NavMeshAgent agent;
    private Transform target;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = speed;
        target = GameObject.FindWithTag("Scorpion").transform;
    }

    void Update()
    {
        if (target == null) return;
        
        float dist = Vector3.Distance(transform.position, target.position);
        
        // 일정 거리 이상이면 따라감
        if (dist > followDistance)
            agent.SetDestination(target.position);
        else
            agent.ResetPath(); // 너무 가까우면 멈춤
    }
}