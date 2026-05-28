using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Stage1;
using Fusion;

public class BatteryMelter : NetworkBehaviour
{
    [Header("References")]
    public Transform glassHinge;
    public Transform glassButton;
    public Transform activateButton;
    public Transform lightBallHole;
    public Transform batterySlot;

    public Material frozenBatteryCore;
    public Material meltedBatteryCore;

    [Header("Settings")]
    public float snapDistance = 0.55f;
    public float glassOpenAngle = 90f;
    public float buttonPressDepth = 0.015f;
    public float animSpeed = 2.5f;

    [Header("Battery Snap Alignment")]
    [Tooltip("Local position offset applied on top of batterySlot so the battery sits flush inside the device.")]
    public Vector3 batterySnapPositionOffset = Vector3.zero;
    [Tooltip("Euler rotation offset applied on top of batterySlot so the battery lies horizontally.")]
    public Vector3 batterySnapRotationOffset = new Vector3(0f, 0f, 90f);

    [Header("Light Ball Snap Alignment")]
    [Tooltip("Local position offset applied on top of lightBallHole so the ball sits snug.")]
    public Vector3 lightBallSnapPositionOffset = Vector3.zero;

    [Networked]
    public NetworkBool IsOpen { get; set; }

    bool isAnimating = false;

    GameObject snappedBattery = null;
    GameObject snappedLightBall = null;

    Quaternion hingeClosedRot;
    Quaternion hingeOpenedRot;
    Vector3 glassButtonOrigin;
    Vector3 activateButtonOrigin;

    public override void Spawned()
    {
        hingeClosedRot = glassHinge.localRotation;
        hingeOpenedRot = Quaternion.Euler(
            hingeClosedRot.eulerAngles.x + glassOpenAngle,
            hingeClosedRot.eulerAngles.y,
            hingeClosedRot.eulerAngles.z
        );
        glassButtonOrigin    = glassButton.localPosition;
        activateButtonOrigin = activateButton.localPosition;

        // Initial state
        glassHinge.localRotation = IsOpen ? hingeOpenedRot : hingeClosedRot;

        // Button colors: glass/open = red, activate/thaw = green
        SetButtonColor(glassButton,    new Color(0.85f, 0.12f, 0.12f));
        SetButtonColor(activateButton, new Color(0.12f, 0.75f, 0.22f));
    }

    static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    void SetButtonColor(Transform btn, Color color)
    {
        if (btn == null) return;
        var rend = btn.GetComponent<Renderer>();
        if (rend == null) return;
        var mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorID, color);
        rend.SetPropertyBlock(mpb);
    }

    public override void Render()
    {
        // Smoothly animate glass based on networked state if not locally animating
        if (!isAnimating)
        {
            Quaternion targetRot = IsOpen ? hingeOpenedRot : hingeClosedRot;
            if (Quaternion.Angle(glassHinge.localRotation, targetRot) > 0.01f)
            {
                glassHinge.localRotation = Quaternion.Slerp(glassHinge.localRotation, targetRot, Time.deltaTime * animSpeed * 2f);
            }
        }
    }

    void Update()
    {
        // Snapping logic should ideally only run on State Authority to avoid jitter/conflicts
        if (Object != null && Object.HasStateAuthority)
        {
            HandleLightBall();
            HandleBattery();
        }
    }

    // ── LightBall ──────────────────────────────────────────

    void HandleLightBall()
    {
        GameObject lb = snappedLightBall;
        if (lb == null)
        {
            GameObject[] all = GameObject.FindGameObjectsWithTag("LightBall");
            float bestD = snapDistance;
            foreach (var b in all)
            {
                if (b == null) continue;
                var bg = b.GetComponent<XRGrabInteractable>();
                if (bg != null && bg.isSelected) continue;
                float d = Vector3.Distance(b.transform.position, lightBallHole.position);
                if (d < bestD) { bestD = d; lb = b; }
            }
        }
        if (lb == null) return;

        var grab = lb.GetComponent<XRGrabInteractable>();
        bool isHeld = grab != null && grab.isSelected;

        if (snappedLightBall != null)
        {
            if (isHeld)
            {
                StartCoroutine(UnsnapNextFrame(snappedLightBall, false));
            }
            else
            {
                ApplySnap(lb, lightBallHole, lightBallSnapPositionOffset, Vector3.zero);
            }
        }
        else
        {
            if (!isHeld && Vector3.Distance(lb.transform.position, lightBallHole.position) < snapDistance)
            {
                Snap(lb, lightBallHole, lightBallSnapPositionOffset, Vector3.zero, ref snappedLightBall);
            }
        }
    }

    // ── Battery ────────────────────────────────────────────

    void HandleBattery()
    {
        if (!IsOpen || isAnimating) return;

        if (snappedBattery != null)
        {
            var grab = snappedBattery.GetComponent<XRGrabInteractable>();
            bool isHeld = grab != null && grab.isSelected;

            if (isHeld)
            {
                StartCoroutine(UnsnapNextFrame(snappedBattery, true));
            }
            else
            {
                ApplySnap(snappedBattery, batterySlot, batterySnapPositionOffset, batterySnapRotationOffset);
            }
            return;
        }

        GameObject[] allBatteries = GameObject.FindGameObjectsWithTag("Battery");
        foreach (var bat in allBatteries)
        {
            var grab = bat.GetComponent<XRGrabInteractable>();
            if (grab != null && grab.isSelected) continue;

            if (Vector3.Distance(bat.transform.position, batterySlot.position) < snapDistance)
            {
                Snap(bat, batterySlot, batterySnapPositionOffset, batterySnapRotationOffset, ref snappedBattery);
                break;
            }
        }
    }

    // ── Snap / Unsnap ──────────────────────────────────────

    // Applies position+rotation to an already-snapped object every frame.
    void ApplySnap(GameObject obj, Transform slot, Vector3 posOffset, Vector3 rotOffset)
    {
        obj.transform.position = slot.TransformPoint(posOffset);
        obj.transform.rotation = slot.rotation * Quaternion.Euler(rotOffset);
    }

    void Snap(GameObject obj, Transform slot, Vector3 posOffset, Vector3 rotOffset, ref GameObject snapRef)
    {
        snapRef = obj;

        var rb = obj.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.constraints     = RigidbodyConstraints.FreezeAll;
        }

        ApplySnap(obj, slot, posOffset, rotOffset);
        Debug.Log($"{obj.name} snapped!");
    }

    IEnumerator UnsnapNextFrame(GameObject obj, bool isBattery)
    {
        if (isBattery) snappedBattery = null;
        else snappedLightBall = null;

        var grab = obj.GetComponent<XRGrabInteractable>();
        if (grab) grab.throwOnDetach = false;

        yield return null;

        var rb = obj.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.constraints = RigidbodyConstraints.None;
            rb.useGravity = true;
        }

        Debug.Log($"{obj.name} unsnapped!");
    }

    // ── Glass Button ───────────────────────────────────────

    public void OnGlassButtonPressed()
    {
        if (isAnimating) return;
        
        if (Object.HasStateAuthority)
        {
            ToggleGlass();
        }
        else
        {
            RpcRequestToggleGlass();
        }
        
        StartCoroutine(PressButtonVisual(glassButton, glassButtonOrigin));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcRequestToggleGlass()
    {
        ToggleGlass();
    }

    void ToggleGlass()
    {
        IsOpen = !IsOpen;
        Debug.Log($"Glass {(IsOpen ? "OPEN" : "CLOSED")}");
    }

    // ── Activate Button ────────────────────────────────────

    public void OnActivateButtonPressed()
    {
        if (IsOpen) { Debug.Log("Close glass first!"); return; }
        if (snappedBattery == null) { Debug.Log("No battery!"); return; }
        if (snappedLightBall == null) { Debug.Log("No light ball!"); return; }

        // Color validation
        var bState = snappedBattery.GetComponent<BatteryState>();
        var lTag = snappedLightBall.GetComponent<LightBallColorTag>();
        
        LightBallColor bColor = LightBallColor.Red;
        if (bState != null) bColor = bState.Color;
        else {
            var bTag = snappedBattery.GetComponent<BatteryColorTag>();
            if (bTag != null) bColor = bTag.color;
        }

        if (lTag != null && bColor != lTag.color)
        {
            Debug.Log($"[BatteryMelter] Color mismatch ({bColor} vs {lTag.color}) — opening lid and ejecting battery.");
            if (Object.HasStateAuthority)
                EjectBattery();
            else
                RpcRequestEject();
            StartCoroutine(PressButtonVisual(activateButton, activateButtonOrigin));
            return;
        }

        if (Object.HasStateAuthority)
        {
            MeltBatteryLogic();
        }
        else
        {
            RpcRequestMelt();
        }

        StartCoroutine(PressButtonVisual(activateButton, activateButtonOrigin));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcRequestMelt()
    {
        MeltBatteryLogic();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcRequestEject()
    {
        EjectBattery();
    }

    void EjectBattery()
    {
        if (snappedBattery == null) return;

        IsOpen = true;

        var bat = snappedBattery;
        snappedBattery = null;

        var rb = bat.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.None;
            rb.useGravity   = true;
            // Toss forward and slightly upward so it lands in front of the machine.
            rb.linearVelocity = transform.forward * 2f + Vector3.up * 1f;
        }
    }

    void MeltBatteryLogic()
    {
        if (snappedBattery == null) return;
        
        var bState = snappedBattery.GetComponent<BatteryState>();
        if (bState != null)
        {
            // Pass materials from the machine if the battery doesn't have them assigned
            if (bState.frozenMaterial == null) bState.frozenMaterial = frozenBatteryCore;
            if (bState.meltedMaterial == null) bState.meltedMaterial = meltedBatteryCore;

            bState.Melt();
        }
        
        // Legacy support
        if (snappedBattery.GetComponent<MeltedBattery>() == null)
        {
            snappedBattery.AddComponent<MeltedBattery>();
        }
    }

    // ── Button Press ───────────────────────────────────────

    IEnumerator PressButtonVisual(Transform btn, Vector3 origin)
    {
        Vector3 pressed = origin - new Vector3(0, buttonPressDepth, 0);
        float t = 0f;
        while (t < 1f) { t += Time.deltaTime * 10f; btn.localPosition = Vector3.Lerp(origin, pressed, Mathf.Clamp01(t)); yield return null; }
        yield return new WaitForSeconds(0.12f);
        t = 0f;
        while (t < 1f) { t += Time.deltaTime * 10f; btn.localPosition = Vector3.Lerp(pressed, origin, Mathf.Clamp01(t)); yield return null; }
        btn.localPosition = origin;
    }
}