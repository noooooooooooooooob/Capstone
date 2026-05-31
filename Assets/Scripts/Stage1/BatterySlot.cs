using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Fusion;
using Stage1;

/// <summary>
/// Attach to BSlot_R, BSlot_Y, BSlot_B (cube with isTrigger BoxCollider).
///
/// When a melted battery of the correct color enters the trigger and is released,
/// it snaps to this slot's position/rotation and the MultiBatterySlotPanel is notified.
///
/// Networking: only the MainControlSystem's StateAuthority peer processes the snap.
/// OnTriggerEnter/Exit simply tracks which batteries are inside — actual work
/// happens in Update() under authority guard.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BatterySlot : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Color this slot accepts. Match the slot name: BSlot_R=Red, BSlot_Y=Yellow, BSlot_B=Blue")]
    public LightBallColor expectedColor;

    [Header("References")]
    [Tooltip("Auto-found if empty (searches parents then scene).")]
    public MultiBatterySlotPanel panel;

    // ── State ──────────────────────────────────────────────
    public bool IsFilled { get; private set; }

    // Batteries currently overlapping this trigger collider
    readonly HashSet<GameObject> _inTrigger = new HashSet<GameObject>();

    // ── Lifecycle ──────────────────────────────────────────

    void Awake()
    {
        // Auto-find panel
        if (panel == null) panel = GetComponentInParent<MultiBatterySlotPanel>();
        if (panel == null) panel = Object.FindFirstObjectByType<MultiBatterySlotPanel>();

        // Ensure the collider is a trigger
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning($"[BatterySlot] {name}: BoxCollider was not a trigger — fixed automatically.");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsFilled) return;
        var go = GetBatteryRoot(other);
        if (go != null) _inTrigger.Add(go);
    }

    void OnTriggerExit(Collider other)
    {
        var go = GetBatteryRoot(other);
        if (go != null) _inTrigger.Remove(go);
    }

    void Update()
    {
        if (IsFilled) return;
        if (_inTrigger.Count == 0) return;

        // Only the MainControlSystem's StateAuthority processes snapping
        // so InstalledBatteries++ stays consistent on the network.
        if (panel != null && panel.mainControl != null)
        {
            var no = panel.mainControl.Object;
            if (no == null || !no.IsValid || !no.HasStateAuthority) return;
        }

        // Clean up destroyed objects
        _inTrigger.RemoveWhere(g => g == null);

        foreach (var bat in _inTrigger)
        {
            if (!IsCorrectMeltedBattery(bat)) continue;
            if (IsHeld(bat)) continue;   // wait until the player lets go

            // Need authority over the battery's NetworkObject before moving it
            var batNo = bat.GetComponent<NetworkObject>();
            if (batNo != null && batNo.IsValid && !batNo.HasStateAuthority)
            {
                batNo.RequestStateAuthority();
                continue; // will retry next frame once authority transfers
            }

            SnapAndFill(bat);
            return; // only one battery per frame
        }
    }

    // ── Core ───────────────────────────────────────────────

    void SnapAndFill(GameObject bat)
    {
        // Disable XRGrabInteractable so the player can't pick it back up
        var grab = bat.GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            grab.enabled = false;
        }

        // Freeze physics
        var rb = bat.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic     = true;
            rb.constraints     = RigidbodyConstraints.FreezeAll;
        }

        // Snap transform
        bat.transform.SetPositionAndRotation(transform.position, transform.rotation);

        IsFilled = true;
        _inTrigger.Clear();

        // Notify panel → increments InstalledBatteries, locks dispenser
        panel?.NotifySlotFilled(expectedColor, bat);

        Debug.Log($"[BatterySlot] {name}: {expectedColor} battery snapped and filled.");
    }

    // ── Helpers ────────────────────────────────────────────

    static GameObject GetBatteryRoot(Collider col)
    {
        // Walk up to the rigidbody root; confirm it has the Battery tag
        var go = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.gameObject;
        return go.CompareTag("Battery") ? go : null;
    }

    bool IsCorrectMeltedBattery(GameObject go)
    {
        var bState = go.GetComponent<BatteryState>();
        bool isMelted;
        LightBallColor bColor;

        if (bState != null)
        {
            isMelted = bState.IsMelted;
            bColor   = bState.Color;
        }
        else
        {
            isMelted = go.GetComponent<MeltedBattery>() != null;
            var tag  = go.GetComponent<BatteryColorTag>();
            bColor   = tag != null ? tag.color : LightBallColor.Red;
        }

        return isMelted && bColor == expectedColor;
    }

    static bool IsHeld(GameObject obj)
    {
        // NetworkGrabbableSync.IsGrabbed is [Networked] → consistent on all peers
        var ngs = obj.GetComponent<NetworkGrabbableSync>();
        if (ngs != null) return ngs.IsGrabbed;

        var grab = obj.GetComponent<XRGrabInteractable>();
        return grab != null && grab.isSelected;
    }
}
