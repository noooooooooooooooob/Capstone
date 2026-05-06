using UnityEngine;
using UnityEngine.Events;
using Fusion;

namespace Stage1
{
    public class MainPowerSocket : NetworkBehaviour
    {
        public UnityEvent OnPowerRestored;

        public void OnBatteryInserted(FrozenBattery battery)
        {
            if (battery != null && battery.IsThawed)
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
