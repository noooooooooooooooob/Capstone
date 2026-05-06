using UnityEngine;
using Fusion;

namespace Stage1
{
    public class PipeSystemManager : NetworkBehaviour
    {
        [Networked] public float CurrentPressure { get; set; }
        [Networked] public NetworkBool IsRepaired { get; set; }

        public float safePressureThreshold = 30f;

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                CurrentPressure = 100f; // Burst pressure
                IsRepaired = false;
            }
        }
    }
}
