using UnityEngine;
using Fusion;

namespace Stage1
{
    public enum StageState 
    {
        Intro,
        Illumination,
        BatteryThaw,
        PipeRepair,
        Password,
        Outro
    }

    public class Stage1Manager : NetworkBehaviour
    {
        [Networked] public StageState CurrentState { get; set; }

        public void TransitionToState(StageState newState)
        {
            if (Object.HasStateAuthority)
            {
                CurrentState = newState;
                Debug.Log($"[Stage1Manager] Transitioned to {newState}");
            }
        }
    }
}
