using UnityEngine;
using UnityEngine.Events;
using Fusion;
using UnityEngine.InputSystem;

namespace Stage1
{
    public class DimensionalMachine : NetworkBehaviour
    {
        public UnityEvent OnMachineStarted;
        public UnityEvent OnMachineRepairStart;
        public UnityEvent OnMachineBlackout;

        [Tooltip("How long to wait before running Blackout automatically")]
        public float recoveryDuration = 5f;

        private bool hasBeenTriggered = false;

        public void StartRecovery()
        {
            if (Object != null && Object.HasStateAuthority)
            {
                // Trigger visual events via RPC or networked variable
                TriggerEventsRpc();
            }
            else if (Object == null)
            {
                // Offline fallback for testing
                OnMachineStarted?.Invoke();
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void TriggerEventsRpc()
        {
            OnMachineStarted?.Invoke();
        }

        public void TriggerBlackout()
        {
            if (Object != null && Object.HasStateAuthority)
            {
                BlackoutRpc();
            }
            else if (Object == null)
            {
                // Offline fallback for testing
                OnMachineBlackout?.Invoke();
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void BlackoutRpc()
        {
            OnMachineBlackout?.Invoke();
        }


        public void StartRepair()
        {
            if (Object != null && Object.HasStateAuthority)
            {
                RepairEventsRpc();
            }
            else if (Object == null)
            {
                OnMachineRepairStart?.Invoke();
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RepairEventsRpc()
        {
            OnMachineRepairStart?.Invoke();
        }

        private void OnTriggerEnter(Collider other) 
        {
            if (!hasBeenTriggered)
            {
                hasBeenTriggered = true;
                StartRecovery();
            }
            else
            {
                StartCoroutine(RepairSequence());
            }
        }

        private System.Collections.IEnumerator RepairSequence()
        {
            StartRepair();
            yield return new WaitForSeconds(recoveryDuration);
            TriggerBlackout();
        }
    }
}
