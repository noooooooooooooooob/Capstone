using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace Stage1
{
    public class MainPowerSocket : NetworkBehaviour
    {
        public UnityEvent OnPowerRestored;

        public void OnBatteryInserted(GameObject battery)
        {
            if (battery != null && battery.GetComponent<MeltedBattery>() != null)
            {
                if (Object.HasStateAuthority)
                {
                    TriggerPowerRestoredRpc();
                }
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void TriggerPowerRestoredRpc()
        {
            OnPowerRestored?.Invoke();
        }
    }
}
