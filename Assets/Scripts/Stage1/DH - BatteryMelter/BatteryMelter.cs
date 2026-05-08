using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class BatteryMelter : MonoBehaviour
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
    public float snapDistance = 0.3f;
    public float glassOpenAngle = 90f;
    public float buttonPressDepth = 0.015f;
    public float animSpeed = 2.5f;

    bool isOpen = false;
    bool isAnimating = false;

    GameObject snappedBattery = null;
    GameObject snappedLightBall = null;

    Quaternion hingeClosedRot;
    Quaternion hingeOpenedRot;
    Vector3 glassButtonOrigin;
    Vector3 activateButtonOrigin;

    void Start()
    {
        hingeClosedRot = glassHinge.localRotation;
        hingeOpenedRot = Quaternion.Euler(
            hingeClosedRot.eulerAngles.x + glassOpenAngle,
            hingeClosedRot.eulerAngles.y,
            hingeClosedRot.eulerAngles.z
        );
        glassButtonOrigin    = glassButton.localPosition;
        activateButtonOrigin = activateButton.localPosition;
    }

    void Update()
    {
        if (snappedLightBall == null) CheckLightBallSnap();
        else CheckLightBallRelease();

        if (isOpen && !isAnimating)
        {
            if (snappedBattery == null) CheckBatterySnap();
            else CheckBatteryRelease();
        }
    }

    // ── LightBall ──────────────────────────────────────────

    void CheckLightBallSnap()
    {
        GameObject lb = GameObject.FindGameObjectWithTag("LightBall");
        if (lb == null) return;
        var grab = lb.GetComponent<XRGrabInteractable>();
        if (grab != null && grab.isSelected) return;
        if (Vector3.Distance(lb.transform.position, lightBallHole.position) < snapDistance)
            SnapObject(lb, lightBallHole, ref snappedLightBall, "LightBall snapped!");
    }

    void CheckLightBallRelease()
    {
        if (snappedLightBall == null) return;
        var grab = snappedLightBall.GetComponent<XRGrabInteractable>();
        if (grab != null && grab.isSelected)
            ReleaseObject(ref snappedLightBall, "LightBall released!");
    }

    // ── Battery ────────────────────────────────────────────

    void CheckBatterySnap()
    {
        GameObject[] allBatteries = GameObject.FindGameObjectsWithTag("Battery");
        GameObject closest = null;
        float closestDist = float.MaxValue;

        foreach (var bat in allBatteries)
        {
            var grab = bat.GetComponent<XRGrabInteractable>();
            if (grab != null && grab.isSelected) continue;
            float dist = Vector3.Distance(bat.transform.position, batterySlot.position);
            if (dist < closestDist) { closestDist = dist; closest = bat; }
        }

        if (closest == null) return;
        if (closestDist < snapDistance)
            SnapObject(closest, batterySlot, ref snappedBattery, "Battery snapped into melter!");
    }

    void CheckBatteryRelease()
    {
        if (snappedBattery == null) return;
        var grab = snappedBattery.GetComponent<XRGrabInteractable>();
        if (grab != null && grab.isSelected)
            ReleaseObject(ref snappedBattery, "Battery released from melter!");
    }

    // ── Snap/Release ───────────────────────────────────────

    void SnapObject(GameObject obj, Transform slot, ref GameObject snapRef, string log)
    {
        snapRef = obj;

        // Throw On Detach 끄기 (kinematic 에러 방지)
        var grab = obj.GetComponent<XRGrabInteractable>();
        if (grab) grab.throwOnDetach = false;

        var rb = obj.GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;

        Vector3 worldScale = obj.transform.lossyScale;
        obj.transform.SetParent(slot, true);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;

        Vector3 parentLossy = slot.lossyScale;
        obj.transform.localScale = new Vector3(
            worldScale.x / (parentLossy.x != 0 ? parentLossy.x : 1),
            worldScale.y / (parentLossy.y != 0 ? parentLossy.y : 1),
            worldScale.z / (parentLossy.z != 0 ? parentLossy.z : 1)
        );

        Debug.Log(log);
    }

    void ReleaseObject(ref GameObject snapRef, string log)
    {
        if (snapRef == null) return;

        var grab = snapRef.GetComponent<XRGrabInteractable>();
        if (grab) grab.throwOnDetach = true; // 다시 켜줌

        snapRef.transform.SetParent(null, true);

        var rb = snapRef.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        snapRef = null;
        Debug.Log(log);
    }

    // ── Glass Button ───────────────────────────────────────

    public void OnGlassButtonPressed()
    {
        if (isAnimating) return;
        StartCoroutine(PressButton(glassButton, glassButtonOrigin));
        StartCoroutine(AnimateGlass());
    }

    IEnumerator AnimateGlass()
    {
        isAnimating = true;
        isOpen = !isOpen;

        Quaternion startRot  = glassHinge.localRotation;
        Quaternion targetRot = isOpen ? hingeOpenedRot : hingeClosedRot;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime * animSpeed;
            glassHinge.localRotation = Quaternion.Slerp(startRot, targetRot, Mathf.Clamp01(t));
            yield return null;
        }
        glassHinge.localRotation = targetRot;
        isAnimating = false;
        Debug.Log($"Glass {(isOpen ? "OPEN" : "CLOSED")}");
    }

    // ── Activate Button ────────────────────────────────────

    public void OnActivateButtonPressed()
    {
        if (isOpen) { Debug.Log("Close glass first!"); return; }
        if (snappedBattery == null) { Debug.Log("No battery!"); return; }
        if (snappedLightBall == null) { Debug.Log("No light ball!"); return; }

        StartCoroutine(PressButton(activateButton, activateButtonOrigin));
        MeltBatteryInstant();
    }

    void MeltBatteryInstant()
    {
        foreach (Transform child in snappedBattery.GetComponentsInChildren<Transform>())
        {
            if (child.name.ToLower().Contains("core"))
            {
                var rend = child.GetComponent<Renderer>();
                if (rend && meltedBatteryCore != null)
                    rend.material = meltedBatteryCore;
                Debug.Log("Battery core turned green!");
                break;
            }
        }
    }

    // ── Button Press ───────────────────────────────────────

    IEnumerator PressButton(Transform btn, Vector3 origin)
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