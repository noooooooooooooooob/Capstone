using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace Stage1
{
    public class LightReceptacle : NetworkBehaviour
    {
        public UnityEvent OnFireSphereInserted;

        // Called by Oculus Interaction Socket when item is placed
        public void SphereInserted()
        {
            OnFireSphereInserted?.Invoke();
        }
    }
}
