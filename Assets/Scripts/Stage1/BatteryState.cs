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

        // Colors matching LightBallColor enum order: Red, Yellow, Blue
        static readonly Color[] BallColors =
        {
            new Color(1f,  0.2f, 0.2f), // Red
            new Color(1f,  0.9f, 0.1f), // Yellow
            new Color(0.2f, 0.5f, 1f),  // Blue
        };

        static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock _mpb;

        public override void Spawned()
        {
            _mpb = new MaterialPropertyBlock();

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

            // Swap frozen / melted material
            Material targetMat = IsMelted ? meltedMaterial : frozenMaterial;
            if (targetMat != null && coreRenderer.sharedMaterial != targetMat)
            {
                coreRenderer.sharedMaterial = targetMat;
            }

            // Thawed = green so the player can see it's ready; frozen = color-coded to required ball
            int idx = (int)Color;
            UnityEngine.Color tint = IsMelted
                ? new UnityEngine.Color(0.1f, 0.95f, 0.2f)
                : (idx >= 0 && idx < BallColors.Length ? BallColors[idx] : UnityEngine.Color.white);

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            coreRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorID, tint);
            coreRenderer.SetPropertyBlock(_mpb);
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

