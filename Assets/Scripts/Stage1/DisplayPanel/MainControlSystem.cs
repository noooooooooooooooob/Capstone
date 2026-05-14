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

    void Awake() 
    { 
        Instance = this; 
        if (stabilityBar) 
        {
            stabilityBar.maxValue = maxStability;
            stabilityBar.value = 0f;
        }
        if (stabilityText) stabilityText.text = "STABILITY: 0%";
        if (statusText) statusText.text = "OFFLINE";
        if (batteryWarningPanel) batteryWarningPanel.SetActive(false);
    }

    void Start()
    {
        // UI 버튼 리스너는 네트워크 여부와 관계없이 등록 (테스트 편의성)
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnStabilizeButtonPressed);
        }
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentState = SystemState.Idle;
            Stability = 0f;
        }
        
        UpdateVisuals();
    }

    public override void Render()
    {
        if (!Object || !Object.IsValid) return;
        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        if (stabilityBar) stabilityBar.value = Stability;
        int percent = Mathf.RoundToInt(Stability);
        if (stabilityText) stabilityText.text = $"STABILITY: {percent}%";
        if (barFill)
            barFill.color = Color.Lerp(Color.red, Color.green, Stability / maxStability);

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
        if (!Object || !Object.IsValid || !Object.HasStateAuthority) return;

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
        // 1. 네트워크 연결 체크
        if (!Object || !Object.IsValid)
        {
            Debug.LogWarning("[MainControlSystem] Network not ready. Please connect to a room first.");
            return;
        }

        // 2. 상태 체크
        if (CurrentState != SystemState.Idle) 
        {
            Debug.Log($"[MainControlSystem] Cannot start: Current state is {CurrentState}");
            return;
        }
        
        // 3. 권한 획득 및 시작
        StartCoroutine(RequestAuthorityAndStart());
    }

    IEnumerator RequestAuthorityAndStart()
    {
        if (!Object.HasStateAuthority)
        {
            Debug.Log("[MainControlSystem] Requesting State Authority...");
            Object.RequestStateAuthority();
            
            // 권한이 넘어올 때까지 잠시 대기 (Shared Mode)
            float timeout = 2f;
            while (!Object.HasStateAuthority && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        if (Object.HasStateAuthority)
        {
            Debug.Log("[MainControlSystem] Starting Stabilize Sequence.");
            StartCoroutine(StabilizeSequence());
        }
        else
        {
            Debug.LogError("[MainControlSystem] Failed to get State Authority.");
        }
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
        if (!Object || !Object.IsValid) return;
        if (CurrentState != SystemState.PowerOff) return;
        
        StartCoroutine(RequestAuthorityAndReboot());
    }

    IEnumerator RequestAuthorityAndReboot()
    {
        if (!Object.HasStateAuthority)
        {
            Object.RequestStateAuthority();
            while (!Object.HasStateAuthority) yield return null;
        }
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
