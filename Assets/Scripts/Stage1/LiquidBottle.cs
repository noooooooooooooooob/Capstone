using UnityEngine;
using Fusion;

namespace Stage1
{
    public class LiquidBottle : NetworkBehaviour
    {
        [SerializeField] private bool isFull;
        public bool IsFull => isFull;

        // Add shake detection logic (Velocity checking)
    }
}
