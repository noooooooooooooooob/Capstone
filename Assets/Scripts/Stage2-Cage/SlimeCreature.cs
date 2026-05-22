using UnityEngine;
using UnityEngine.AI;

public class SlimeCreature : MonoBehaviour
{
    public float followDistance = 3f;
    public float speed = 2f;
    
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
        if (transform.position.y < -5f)
{
    transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
}
        if (!agent.enabled) return;
        if (target == null) return;
        
        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > followDistance)
            agent.SetDestination(target.position);
        else
            agent.ResetPath();
    }
}