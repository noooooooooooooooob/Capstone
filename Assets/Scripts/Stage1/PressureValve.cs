using UnityEngine;
using Fusion;

namespace Stage1
{
    public class PressureValve : NetworkBehaviour
    {
        [SerializeField] private PipeSystemManager pipeManager;

        // Called by Oculus Interaction Knob/Valve
        public void OnValveTurned(float value)
        {
            if (pipeManager != null && Object.HasStateAuthority)
            {
                pipeManager.CurrentPressure -= value; // Reduce pressure
            }
        }
    }
}
