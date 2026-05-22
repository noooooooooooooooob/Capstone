using UnityEngine;

public class FlyingCreature : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 2f;
    
    private int currentTarget = 0;
    private int direction = 1; // 1: 정방향, -1: 역방향

    void Update()
    {
        Transform target = waypoints[currentTarget];
        
        float hover = Mathf.Sin(Time.time * 2f) * 0.3f;
        Vector3 targetPos = target.position + new Vector3(0, hover, 0);
        
        transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);
        
        // 이동 방향으로 회전 (목표와 거리 충분할 때만)
        Vector3 dir = targetPos - transform.position;
        if (dir.magnitude > 0.2f)
            transform.rotation = Quaternion.LookRotation(dir);
        
        // 도착하면 다음 웨이포인트
        if (Vector3.Distance(transform.position, target.position) < 0.1f)
        {
            currentTarget += direction;
            
            // 끝에 도달하면 방향 반전
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
}