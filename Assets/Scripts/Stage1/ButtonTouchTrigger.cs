using UnityEngine;
using UnityEngine.Events;

// Name changed to match filename: ButtonTouchTrigger.cs
public class ButtonTouchTrigger : MonoBehaviour
{
    [Tooltip("The event that will run when touched or clicked")]
    public UnityEvent OnActivated;

    // 1. TOUCH: Works when your VR hand physically enters the box
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Triggered by " + other.gameObject.name);
        OnActivated?.Invoke();
    }

    // 2. CLICK: Call this method from your VR Raycaster
    public void ManualTrigger()
    {
        Debug.Log("Box Clicked from distance!");
        OnActivated?.Invoke();
    }
}
