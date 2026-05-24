// BoxStun.cs - 박스에 붙이기
using UnityEngine;

public class BoxStun : MonoBehaviour
{
    void OnCollisionEnter(Collision collision)
    {
        BoxerCreature boxer = collision.gameObject.GetComponent<BoxerCreature>();
        if (boxer != null)
            boxer.GetStunned();
    }
}