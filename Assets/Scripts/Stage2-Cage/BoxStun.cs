// BoxStun.cs - 박스에 붙이기
using UnityEngine;

public class BoxStun : MonoBehaviour
{
    void OnCollisionEnter(Collision collision)
    {
        Debug.Log("박스에 닿은 오브젝트: " + collision.gameObject.name);
        BoxerCreature boxer = collision.gameObject.GetComponent<BoxerCreature>();
        if (boxer != null)
            boxer.GetStunned();
    }
}