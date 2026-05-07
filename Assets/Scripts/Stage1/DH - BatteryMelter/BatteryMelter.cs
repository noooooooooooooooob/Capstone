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
        // LightBall 스냅/해제
        if (snappedLightBall == null)
            CheckLightBallSnap();
        else
            CheckLightBallRelease();

        // 배터리: 유리 열렸을 때만 스냅/해제
        if (isOpen && !isAnimating)
        {
            if (snappedBattery == null)
                CheckBatterySnap();
            else
                CheckBatteryRelease();
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
        Debug.Log($"Closest battery dist: {closestDist:F2}");
        if (closestDist < snapDistance)
            SnapObject(closest, batterySlot, ref snappedBattery, "Battery snapped!");
    }

    void CheckBatteryRelease()
    {
        if (snappedBattery == null) return;
        var grab = snappedBattery.GetComponent<XRGrabInteractable>();
        if (grab != null && grab.isSelected)
            ReleaseObject(ref snappedBattery, "Battery released!");
    }

    // ── 공통 Snap/Release ──────────────────────────────────

    void SnapObject(GameObject obj, Transform slot, ref GameObject snapRef, string log)
    {
        snapRef = obj;

        var rb = obj.GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;

        Vector3 worldScale = obj.transform.lossyScale;
        obj.transform.SetParent(slot, true);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;

        // Scale 왜곡 방지
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
        snapRef.transform.SetParent(null, true);
        var rb = snapRef.GetComponent<Rigidbody>();
        if (rb) { rb.isKinematic = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        snapRef = null;
        Debug.Log(log);
    }

    // ── Glass Button: 열고 닫기만 ─────────────────────────

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

    // ── Activate Button: 조건 충족 시 색상 변경만 ─────────

    public void OnActivateButtonPressed()
    {
        // 조건: 유리 닫혀있고 + 배터리 + 공 둘 다 있어야
        if (isOpen)
        {
            Debug.Log("Glass must be closed to activate!"); return;
        }
        if (snappedBattery == null)
        {
            Debug.Log("No battery inside!"); return;
        }
        if (snappedLightBall == null)
        {
            Debug.Log("No light ball!"); return;
        }

        StartCoroutine(PressButton(activateButton, activateButtonOrigin));
        MeltBatteryInstant();
    }

    void MeltBatteryInstant()
    {
        // Battery_Core 찾아서 즉시 머티리얼 교체
        foreach (Transform child in snappedBattery.GetComponentsInChildren<Transform>())
        {
            if (child.name.ToLower().Contains("core"))
            {
                var rend = child.GetComponent<Renderer>();
                if (rend && meltedBatteryCore != null)
                {
                    rend.material = meltedBatteryCore;
                    Debug.Log("Battery core turned green!");
                }
                break;
            }
        }
    }

    // ── Button Press Animation ─────────────────────────────

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