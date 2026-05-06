using UnityEngine;
using Fusion;

namespace Stage1
{
    public enum PlayerRole
    {
        None,
        PlayerA, // Dark room, Machine
        PlayerB  // Light room, Diary
    }

    public class PlayerRoleManager : NetworkBehaviour
    {
        [Networked] public PlayerRole AssignedRole { get; set; }
        
        // Logic to claim or assign role upon spawn
    }
}
