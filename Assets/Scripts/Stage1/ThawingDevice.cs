using UnityEngine;
using Fusion;

namespace Stage1
{
    public class ThawingDevice : NetworkBehaviour
    {
        [SerializeField] private FrozenBattery targetBattery;
        
        private bool hasFireSphere = false;
        private bool hasBattery = false;

        public void OnFireSphereSlotted()
        {
            hasFireSphere = true;
            CheckThawConditions();
        }

        public void OnBatterySlotted()
        {
            hasBattery = true;
            CheckThawConditions();
        }

        private void CheckThawConditions()
        {
            if (hasFireSphere && hasBattery && Object.HasStateAuthority)
            {
                if (targetBattery != null)
                {
                    targetBattery.Thaw();
                }
            }
        }
    }
}
