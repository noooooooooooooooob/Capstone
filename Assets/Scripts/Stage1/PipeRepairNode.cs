using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace Stage1
{
    public class PipeRepairNode : NetworkBehaviour
    {
        [SerializeField] private PipeSystemManager pipeManager;
        public UnityEvent OnRepairSuccess;
        public UnityEvent OnRepairFailed;

        // Triggered by Player B interaction
        public void AttemptRepair()
        {
            if (pipeManager != null)
            {
                if (pipeManager.CurrentPressure <= pipeManager.safePressureThreshold)
                {
                    if (Object.HasStateAuthority) pipeManager.IsRepaired = true;
                    OnRepairSuccess?.Invoke();
                }
                else
                {
                    OnRepairFailed?.Invoke();
                }
            }
        }
    }
}
