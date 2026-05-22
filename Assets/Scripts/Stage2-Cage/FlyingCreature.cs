using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class FlyingCreature : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 2f;
    
    private int currentTarget = 0;
    private int direction = 1;
    [HideInInspector] public bool isStunned = false;
    private XRGrabInteractable grab;

    void Start()
    {
        grab = GetComponent<XRGrabInteractable>();
    }

    void Update()
    {
        if (transform.position.y < -5f)
{
    transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
}
        if (isStunned) return;
        if (grab != null && grab.isSelected) return; // 잡혀있으면 멈춤

        Transform target = waypoints[currentTarget];
        
        float hover = Mathf.Sin(Time.time * 2f) * 0.3f;
        Vector3 targetPos = target.position + new Vector3(0, hover, 0);
        
        transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);
        
        Vector3 dir = targetPos - transform.position;
        if (dir.magnitude > 0.2f)
            transform.rotation = Quaternion.LookRotation(dir);
        
        if (Vector3.Distance(transform.position, target.position) < 0.1f)
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
        }
    }

    public void SetCaged()
    {
        isStunned = true;
    }

    public void SetFree()
    {
        isStunned = false;
    }
}