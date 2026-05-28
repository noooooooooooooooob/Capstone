using UnityEngine;
using Fusion;

namespace Stage1
{
    /// <summary>
    /// Synchronizes the state of a battery across the network.
    /// </summary>
    public class BatteryState : NetworkBehaviour
    {
        [Networked]
        public LightBallColor Color { get; set; }

        [Networked]
        public NetworkBool IsMelted { get; set; }

        [Header("References")]
        public Renderer coreRenderer;
        public Material frozenMaterial;
        public Material meltedMaterial;

        public override void Spawned()
        {
            // Auto-detect if references are missing
            if (coreRenderer == null)
            {
                foreach (var rend in GetComponentsInChildren<Renderer>())
                {
                    if (rend.name.ToLower().Contains("core"))
                    {
                        coreRenderer = rend;
                        break;
                    }
                }
            }

            // Try to find materials in resources if null
            if (frozenMaterial == null) frozenMaterial = Resources.Load<Material>("FrozenBatteryCore");
            if (meltedMaterial == null) meltedMaterial = Resources.Load<Material>("BatteryCore");

            UpdateVisuals();
        }

        public override void Render()
        {
            UpdateVisuals();
        }

        void UpdateVisuals()
        {
            if (coreRenderer == null) return;
            
            Material targetMat = IsMelted ? meltedMaterial : frozenMaterial;
            if (targetMat != null && coreRenderer.sharedMaterial != targetMat)
            {
                coreRenderer.sharedMaterial = targetMat;
            }
        }

        public void Melt()
        {
            if (Object.HasStateAuthority)
            {
                IsMelted = true;
            }
            else
            {
                RpcRequestMelt();
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RpcRequestMelt()
        {
            IsMelted = true;
        }
    }
}

