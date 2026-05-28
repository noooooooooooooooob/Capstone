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
    public Light lightBallLight;

    [Header("Battery Slot")]
    public Transform batterySlot;          // MainControlPanel 배터리 슬롯 위치
    public float snapDistance = 0.35f;     // 스냅 거리

    [Header("Starting Doors")]
    [Tooltip("Doors that slide open when power goes off and INSERT BATTERY is shown.")]
    public Stage1.Stage1SlidingDoor[] startingDoors;

    [Header("Sound")]
    [Tooltip("AudioManager에 등록된 비상 알람 루프 클립 이름")]
    public string alarmSoundName;
    [Range(0f, 1f)]
    [Tooltip("알람 볼륨 (0~1)")]
    public float alarmVolume = 0.4f;

    [Header("Debug")]
    public Button debugSkipCurrentButton;

    public enum SystemState { Idle, Stabilizing, BatteryLow, PowerOff, Rebooting }
    
    [Networked]
    public SystemState CurrentState { get; set; } = SystemState.Idle;

    [Networked]
    public float Stability { get; set; }

    public static MainControlSystem Instance;

    public GameObject snappedBattery = null;
    TemporarySoundPlayer _alarmSound;

    // Initialised to an out-of-range sentinel so the first Render() always runs UpdateStateVisuals.
    private SystemState _lastState = (SystemState)(-1);

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
        SetLightBallBrightness(0f);
    }

    void Start()
    {
        // UI 버튼 리스너는 네트워크 여부와 관계없이 등록 (테스트 편의성)
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnStabilizeButtonPressed);
        }

        if (debugSkipCurrentButton != null)
        {
            debugSkipCurrentButton.onClick.RemoveAllListeners();
            debugSkipCurrentButton.onClick.AddListener(OnDebugSkipPressed);
        }
    }

    void OnDebugSkipPressed()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.DebugCompleteCurrentPuzzle();
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentState = SystemState.Idle;
            Stability = 0f;
        }
        
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPuzzleActivated += OnPuzzleChanged;
            GameManager.Instance.OnAllPuzzlesCompleted += OnAllFixed;
        }

        UpdateVisuals();
    }

    void OnPuzzleChanged(int index)
    {
        // When the game advances to a new puzzle the previous one is considered solved.
        // If this system is still in a non-Idle state (e.g. debug skip bypassed the
        // normal battery-insert reboot path), force it back to Idle so lights restore.
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            if (CurrentState == SystemState.PowerOff ||
                CurrentState == SystemState.BatteryLow ||
                CurrentState == SystemState.Rebooting)
            {
                StopAlarm();
                snappedBattery = null;
                Stability = maxStability;
                CurrentState = SystemState.Idle;
            }
        }
        UpdateVisuals();
    }

    void OnAllFixed()
    {
        if (statusText) statusText.text = "ALL SYSTEMS NOMINAL - PROCEED TO STAGE 2";
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
                if (statusText)
                {
                    if (GameManager.Instance != null && GameManager.Instance.CurrentPuzzleIndex >= 0)
                    {
                        int idx = GameManager.Instance.CurrentPuzzleIndex;
                        string hint = GameManager.Instance.CurrentPuzzleHint;
                        statusText.text = $"PUZZLE {idx + 1}: {hint.ToUpper()}";
                    }
                    else
                    {
                        statusText.text = "SYSTEM ONLINE";
                    }
                }
                if (batteryWarningPanel) batteryWarningPanel.SetActive(false);
                StopAlarm();
                RestoreLights();
                break;
            case SystemState.Stabilizing:
                if (statusText) statusText.text = "STABILIZING...";
                break;
            case SystemState.BatteryLow:
                if (statusText) statusText.text = "BATTERY CRITICAL!";
                if (batteryWarningPanel) batteryWarningPanel.SetActive(true);
                StartAlarm();
                break;
            case SystemState.PowerOff:
                if (statusText) statusText.text = "INSERT BATTERY";
                if (batteryWarningPanel) batteryWarningPanel.SetActive(true);
                TurnOffLights();
                // 불 점화는 네트워크 상태(PowerOff)에 연동 — Render()가 모든 피어에서 실행되므로
                // 권한자/프록시 모두 동일하게 점화된다. (예전엔 권한자 전용 StabilizeSequence에서만
                // 호출해 P2엔 불이 안 났음. 늦게 합류한 피어도 상태 전이 감지로 점화됨.)
                if (Stage1.FireHazardController.Instance != null)
                    Stage1.FireHazardController.Instance.ActivateFires();
                break;
            case SystemState.Rebooting:
                if (statusText) statusText.text = "REBOOTING...";
                if (batteryWarningPanel) batteryWarningPanel.SetActive(false);
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

        // 3. 권한 이전 없이 현재 State Authority에게 시작 요청 (P1/P2 누구나 가능).
        //    RequestStateAuthority 방식은 AllowStateAuthorityOverride가 꺼져 있으면
        //    비권한 피어(P2)에서 거부돼 "Failed to get State Authority"로 실패했음.
        RpcStartStabilize();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcStartStabilize()
    {
        // 권한자에서만 실행 — [Networked] 상태는 권한자만 쓸 수 있다.
        if (CurrentState != SystemState.Idle) return; // 동시 입력 가드
        Debug.Log("[MainControlSystem] Starting Stabilize Sequence.");
        StartCoroutine(StabilizeSequence());
    }

    IEnumerator StabilizeSequence()
    {
        CurrentState = SystemState.Stabilizing;
        float elapsed = 0f;
        float failurePoint = maxStability * 0.8f; // Lose power at 80%

        while (elapsed < stabilityDuration)
        {
            elapsed += Time.deltaTime;
            float currentProgress = Mathf.Lerp(startStability, maxStability, elapsed / stabilityDuration);
            
            if (currentProgress >= failurePoint)
            {
                Stability = failurePoint;
                break; 
            }
            
            Stability = currentProgress;
            yield return null;
        }

        CurrentState = SystemState.BatteryLow;
        yield return new WaitForSeconds(1f);
        yield return new WaitForSeconds(1.5f);
        CurrentState = SystemState.PowerOff;
        Stability = 0f;
        // 불 점화는 UpdateStateVisuals(PowerOff)에서 네트워크 상태 기반으로 처리 → 모든 피어 동기화.
    }

    void TurnOffLights()
    {
        if (roomLightingController != null) roomLightingController.DimLights();
        SetLightBallBrightness(2f);
        OpenStartingDoors();
    }

    void OpenStartingDoors()
    {
        if (startingDoors == null) return;
        foreach (var door in startingDoors)
            if (door != null) door.OpenDoor();
    }

    void RestoreLights()
    {
        if (roomLightingController != null) roomLightingController.RestoreLights();
        SetLightBallBrightness(0f);
    }

    void SetLightBallBrightness(float intensity)
    {
        if (lightBallLight == null) return;

        lightBallLight.enabled = true;
        lightBallLight.intensity = intensity;
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

    void StartAlarm()
    {
        if (_alarmSound != null) return;
        if (string.IsNullOrEmpty(alarmSoundName) || AudioManager.Instance == null) return;
        _alarmSound = AudioManager.Instance.PlaySoundAt(alarmSoundName, transform.position, isLoop: true);
        _alarmSound.SetVolume(alarmVolume);
    }

    void StopAlarm()
    {
        if (_alarmSound == null || AudioManager.Instance == null) return;
        AudioManager.Instance.StopLoopSound(_alarmSound);
        _alarmSound = null;
    }
}
