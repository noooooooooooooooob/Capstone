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

    [Header("Snap 위치")]
    [Tooltip("켜면 배터리의 (메시 렌더러/콜라이더 기준) 중앙이 슬롯 중앙에 정확히 오도록 보정한다.\n" +
             "배터리 피벗이 중앙이 아니어도 시각적으로 슬롯 한가운데에 들어간다.\n" +
             "끄면 기존처럼 배터리 피벗을 슬롯 피벗(transform.position)에 맞춘다.")]
    public bool centerBatteryOnSlot = true;

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

        // Snap transform — 회전을 먼저 슬롯에 맞춘 뒤 위치 정렬.
        bat.transform.rotation = transform.rotation;

        if (centerBatteryOnSlot)
        {
            // 배터리의 '중앙'(메시/콜라이더 기준)이 슬롯 '중앙'에 오도록 피벗 보정.
            // center = pivot + (center-pivot) 이므로, pivot = slotCenter - (center-pivot).
            Vector3 slotCenter = SlotCenter();
            Vector3 pivotToCenter = WorldCenter(bat) - bat.transform.position;
            bat.transform.position = slotCenter - pivotToCenter;
        }
        else
        {
            bat.transform.position = transform.position;
        }

        IsFilled = true;
        _inTrigger.Clear();

        // Notify panel → increments InstalledBatteries, locks dispenser
        panel?.NotifySlotFilled(expectedColor, bat);

        Debug.Log($"[BatterySlot] {name}: {expectedColor} battery snapped and filled.");
    }

    // ── Helpers ────────────────────────────────────────────

    /// <summary>슬롯 중앙(트리거 콜라이더 bounds 중심). 콜라이더가 없으면 transform.position.</summary>
    Vector3 SlotCenter()
    {
        var col = GetComponent<Collider>();
        return col != null ? col.bounds.center : transform.position;
    }

    /// <summary>오브젝트의 시각적 중앙(자식 MeshRenderer 들의 합산 bounds 중심). 없으면 콜라이더, 그것도 없으면 피벗.</summary>
    static Vector3 WorldCenter(GameObject go)
    {
        var rends = go.GetComponentsInChildren<MeshRenderer>();
        if (rends != null && rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b.center;
        }

        var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (skinned != null && skinned.Length > 0)
        {
            Bounds b = skinned[0].bounds;
            for (int i = 1; i < skinned.Length; i++) b.Encapsulate(skinned[i].bounds);
            return b.center;
        }

        var cols = go.GetComponentsInChildren<Collider>();
        if (cols != null && cols.Length > 0)
        {
            Bounds b = cols[0].bounds;
            for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
            return b.center;
        }

        return go.transform.position;
    }

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
