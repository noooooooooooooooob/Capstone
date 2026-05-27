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
        HandleLightBall();
        HandleBattery();
    }

    // ── LightBall ──────────────────────────────────────────

    void HandleLightBall()
    {
        // 변경: 단일 → 다중 LightBall 지원.
        // 이미 스냅된 게 있으면 그것을 추적. 없으면 lightBallHole에 가장 가까운 미파지 LightBall 선택.
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
                lb.transform.position = lightBallHole.position;
                lb.transform.rotation = lightBallHole.rotation;
            }
        }
        else
        {
            if (!isHeld && Vector3.Distance(lb.transform.position, lightBallHole.position) < snapDistance)
            {
                Snap(lb, lightBallHole, ref snappedLightBall);
            }
        }
    }

    // ── Battery ────────────────────────────────────────────

    void HandleBattery()
    {
        if (!isOpen || isAnimating) return;

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
                snappedBattery.transform.position = batterySlot.position;
                snappedBattery.transform.rotation = batterySlot.rotation;
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
                Snap(bat, batterySlot, ref snappedBattery);
                break;
            }
        }
    }

    // ── Snap / Unsnap ──────────────────────────────────────

    void Snap(GameObject obj, Transform slot, ref GameObject snapRef)
    {
        snapRef = obj;

        var rb = obj.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.constraints = RigidbodyConstraints.FreezeAll;  // kinematic 대신 이걸로
        }

        obj.transform.position = slot.position;
        obj.transform.rotation = slot.rotation;

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
            rb.constraints = RigidbodyConstraints.None;  // constraints 해제
            rb.useGravity = true;
        }

        Debug.Log($"{obj.name} unsnapped!");
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
                Debug.Log("Battery core melted!");
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