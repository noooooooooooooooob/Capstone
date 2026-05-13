using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Fusion;

public class MainControlSystem : NetworkBehaviour
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
    public Stage1.RoomLightingController roomLightingController;

    [Header("Battery Slot")]
    public Transform batterySlot;          // MainControlPanel 배터리 슬롯 위치
    public float snapDistance = 0.35f;     // 스냅 거리

    public enum SystemState { Idle, Stabilizing, BatteryLow, PowerOff, Rebooting }
    
    [Networked]
    public SystemState CurrentState { get; set; } = SystemState.Idle;

    [Networked]
    public float Stability { get; set; }

    public static MainControlSystem Instance;

    GameObject snappedBattery = null;

    private SystemState _lastState = SystemState.Idle;

    void Awake() { Instance = this; }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentState = SystemState.Idle;
            Stability = 0f;
        }
        
        stabilityBar.maxValue = maxStability;
        UpdateVisuals();
        
        if (startButton != null)
            startButton.onClick.AddListener(OnStabilizeButtonPressed);
    }

    public override void Render()
    {
        // 시각적 동기화
        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        // Stability UI 업데이트
        stabilityBar.value = Stability;
        int percent = Mathf.RoundToInt(Stability);
        if (stabilityText) stabilityText.text = $"STABILITY: {percent}%";
        if (barFill)
            barFill.color = Color.Lerp(Color.red, Color.green, Stability / maxStability);

        // 상태 기반 UI 및 조명 업데이트
        if (_lastState != CurrentState)
        {
            UpdateStateVisuals(CurrentState);
            _lastState = CurrentState;
        }
    }

    void UpdateStateVisuals(SystemState state)
    {
        switch (state)
        {
            case SystemState.Idle:
                if (statusText) statusText.text = "SYSTEM ONLINE";
                if (batteryWarningPanel) batteryWarningPanel.SetActive(false);
                RestoreLights();
                break;
            case SystemState.Stabilizing:
                if (statusText) statusText.text = "STABILIZING...";
                break;
            case SystemState.BatteryLow:
                if (statusText) statusText.text = "BATTERY CRITICAL!";
                if (batteryWarningPanel) batteryWarningPanel.SetActive(true);
                break;
            case SystemState.PowerOff:
                if (statusText) statusText.text = "POWER OFFLINE";
                if (batteryWarningPanel) batteryWarningPanel.SetActive(true);
                TurnOffLights();
                break;
            case SystemState.Rebooting:
                if (statusText) statusText.text = "REBOOTING...";
                if (batteryWarningPanel) batteryWarningPanel.SetActive(false);
                RestoreLights();
                break;
        }
    }

    void Update()
    {
        if (!Object || !Object.HasStateAuthority) return;

        if (CurrentState == SystemState.PowerOff && snappedBattery == null)
            CheckBatterySnap();
    }

    void CheckBatterySnap()
    {
        if (batterySlot == null) return;

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

        OnBatteryInserted();
    }

    public void OnStabilizeButtonPressed()
    {
        if (CurrentState != SystemState.Idle) return;
        
        if (!Object.HasStateAuthority)
            Object.RequestStateAuthority();
        
        StartCoroutine(StabilizeSequence());
    }

    IEnumerator StabilizeSequence()
    {
        CurrentState = SystemState.Stabilizing;
        float elapsed = 0f;

        while (elapsed < stabilityDuration)
        {
            elapsed += Time.deltaTime;
            Stability = Mathf.Lerp(startStability, maxStability, elapsed / stabilityDuration);
            yield return null;
        }

        CurrentState = SystemState.BatteryLow;
        Stability = maxStability;
        yield return new WaitForSeconds(1f);
        yield return new WaitForSeconds(1.5f);
        CurrentState = SystemState.PowerOff;
        Stability = 0f;
    }

    void TurnOffLights()
    {
        if (roomLightingController != null) roomLightingController.DimLights();
    }

    void RestoreLights()
    {
        if (roomLightingController != null) roomLightingController.RestoreLights();
    }

    public void OnBatteryInserted()
    {
        if (CurrentState != SystemState.PowerOff) return;
        
        if (!Object.HasStateAuthority)
            Object.RequestStateAuthority();
        
        StartCoroutine(Reboot());
    }

    IEnumerator Reboot()
    {
        CurrentState = SystemState.Rebooting;
        float elapsed = 0f;
        float rebootDuration = 2f;
        while (elapsed < rebootDuration)
        {
            elapsed += Time.deltaTime;
            Stability = Mathf.Lerp(0f, maxStability, elapsed / rebootDuration);
            yield return null;
        }

        Stability = maxStability;
        CurrentState = SystemState.Idle;
    }
}
