using UnityEngine;
using Fusion;

namespace Stage1
{
    public class FrozenBattery : NetworkBehaviour
    {
        [Networked] public NetworkBool IsThawed { get; set; }

        // Triggered by ThawingDevice
        public void Thaw()
        {
            if (Object.HasStateAuthority)
            {
                IsThawed = true;
            }
        }
    }
}
