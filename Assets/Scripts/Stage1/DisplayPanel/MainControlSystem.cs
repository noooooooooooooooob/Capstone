using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class MainControlSystem : MonoBehaviour
{
    [Header("UI References")]
    public Slider stabilityBar;
    public TextMeshProUGUI stabilityText;
    public TextMeshProUGUI statusText;
    public GameObject batteryWarningPanel;
    public Image barFill;
    public Button startButton;

    [Header("Settings")]
    public float stabilityDuration = 5f;
    public float startStability = 0f;
    public float maxStability = 100f;

    [Header("Lighting")]
    public Light[] roomLights;

    [Header("Battery Slot")]
    public Transform batterySlot;          // MainControlPanel 배터리 슬롯 위치
    public float snapDistance = 0.35f;     // 스냅 거리

    public enum SystemState { Idle, Stabilizing, BatteryLow, PowerOff, Rebooting }
    public SystemState currentState = SystemState.Idle;

    public static MainControlSystem Instance;

    GameObject snappedBattery = null;

    void Awake() { Instance = this; }

    void Start()
    {
        stabilityBar.value = 0f;
        stabilityBar.maxValue = maxStability;
        UpdateUI(0f);
        if (batteryWarningPanel) batteryWarningPanel.SetActive(false);

        if (startButton != null)
            startButton.onClick.AddListener(OnStabilizeButtonPressed);
    }

    void Update()
    {
        // PowerOff 상태일 때만 배터리 슬롯 스냅 체크
        if (currentState == SystemState.PowerOff && snappedBattery == null)
            CheckBatterySnap();
    }

    // ── Battery Slot Snap ──────────────────────────────────

    void CheckBatterySnap()
    {
        if (batterySlot == null) return;

        // 해동된 배터리 (초록) 찾기 - 태그로
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
            SnapBattery(closest);
    }

    void SnapBattery(GameObject bat)
    {
        snappedBattery = bat;

        var grab = bat.GetComponent<XRGrabInteractable>();
        if (grab) grab.throwOnDetach = false;

        var rb = bat.GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;

        Vector3 worldScale = bat.transform.lossyScale;
        bat.transform.SetParent(batterySlot, true);
        bat.transform.localPosition = Vector3.zero;
        bat.transform.localRotation = Quaternion.identity;

        Vector3 parentLossy = batterySlot.lossyScale;
        bat.transform.localScale = new Vector3(
            worldScale.x / (parentLossy.x != 0 ? parentLossy.x : 1),
            worldScale.y / (parentLossy.y != 0 ? parentLossy.y : 1),
            worldScale.z / (parentLossy.z != 0 ? parentLossy.z : 1)
        );

        Debug.Log("Melted battery inserted into main panel!");
        OnBatteryInserted();
    }

    // ── Stabilize Button ───────────────────────────────────

    public void OnStabilizeButtonPressed()
    {
        if (currentState != SystemState.Idle) return;
        StartCoroutine(StabilizeSequence());
    }

    IEnumerator StabilizeSequence()
    {
        currentState = SystemState.Stabilizing;
        if (statusText) statusText.text = "STABILIZING...";
        float elapsed = 0f;

        while (elapsed < stabilityDuration)
        {
            elapsed += Time.deltaTime;
            float value = Mathf.Lerp(startStability, maxStability, elapsed / stabilityDuration);
            stabilityBar.value = value;
            UpdateUI(value);
            yield return null;
        }

        // 배터리 부족
        currentState = SystemState.BatteryLow;
        stabilityBar.value = maxStability;
        UpdateUI(maxStability);

        yield return new WaitForSeconds(1f);
        if (statusText) statusText.text = "BATTERY CRITICAL!";
        if (batteryWarningPanel) batteryWarningPanel.SetActive(true);

        yield return new WaitForSeconds(1.5f);

        // 전원 꺼짐
        currentState = SystemState.PowerOff;
        TurnOffLights();
        if (statusText) statusText.text = "POWER OFFLINE";
        stabilityBar.value = 0f;
        UpdateUI(0f);
    }

    void UpdateUI(float value)
    {
        int percent = Mathf.RoundToInt(value);
        if (stabilityText) stabilityText.text = $"STABILITY: {percent}%";
        if (barFill)
            barFill.color = Color.Lerp(Color.red, Color.green, value / maxStability);
    }

    void TurnOffLights()
    {
        foreach (var light in roomLights)
            if (light) light.enabled = false;
    }

    // ── Battery Inserted → Reboot ──────────────────────────

    public void OnBatteryInserted()
    {
        if (currentState != SystemState.PowerOff) return;
        StartCoroutine(Reboot());
    }

    IEnumerator Reboot()
    {
        currentState = SystemState.Rebooting;
        if (statusText) statusText.text = "REBOOTING...";

        // 조명 켜기
        foreach (var light in roomLights)
            if (light) light.enabled = true;

        if (batteryWarningPanel) batteryWarningPanel.SetActive(false);

        // Stability 0 → 100 채우기 (초록색으로)
        float elapsed = 0f;
        float rebootDuration = 2f;
        while (elapsed < rebootDuration)
        {
            elapsed += Time.deltaTime;
            float value = Mathf.Lerp(0f, maxStability, elapsed / rebootDuration);
            stabilityBar.value = value;
            UpdateUI(value);
            yield return null;
        }

        stabilityBar.value = maxStability;
        UpdateUI(maxStability);

        currentState = SystemState.Idle;
        if (statusText) statusText.text = "SYSTEM ONLINE";
        Debug.Log("System rebooted!");
    }
}