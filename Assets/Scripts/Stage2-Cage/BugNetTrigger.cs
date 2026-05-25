using UnityEngine;

public class BugNetTrigger : MonoBehaviour
{
    // Drag the center-of-net Transform here in Inspector
    public Transform netCenter;

    private void OnTriggerEnter(Collider other)
    {
        FlyingCreature creature = other.GetComponent<FlyingCreature>();
        if (creature == null) return;
        if (creature.currentState != FlyingCreature.State.Flying) return;

        creature.transform.position = netCenter.position;
        creature.SetNetCaught(netCenter);
    }
}