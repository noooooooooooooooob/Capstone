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

    // 슬롯에 소멸시켜 넣은 충전 배터리 개수 (네트워크 동기 — 양쪽 피어가 같은 카운트를 본다).
    [Networked]
    public int InstalledBatteries { get; set; }

    [Header("Battery Recovery")]
    [Tooltip("복구(안정화)에 필요한 충전 배터리 개수. 보통 슬롯 수와 동일(3).")]
    public int requiredBatteries = 3;

    public static MainControlSystem Instance;

    public GameObject snappedBattery = null;
    TemporarySoundPlayer _alarmSound;

    // Initialised to an out-of-range sentinel so the first Render() always runs UpdateStateVisuals.
    private SystemState _lastState = (SystemState)(-1);

    void Awake()
    {
        Instance = this;
        _multiSlotPanelPresent = FindFirstObjectByType<Stage1.MultiBatterySlotPanel>() != null;
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
        // 자동 복구 제거: 라이트 복구는 오직 CRT 버튼 안정화(배터리 3개 + 버튼) 경로로만 이뤄진다.
        // 예전엔 퍼즐이 다음 단계로 넘어가거나 디버그 스킵 시 여기서 강제로 Idle로 되돌려
        // 불을 켰지만, 이제는 PowerOff 상태를 그대로 유지한다.
        // (복구는 OnStabilizeButtonPressed → RpcRequestRecovery → Reboot 경로가 전담)
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

        // PowerOff 동안 배터리 설치 카운트를 매 프레임 갱신(네트워크 카운트라 양쪽 동일 표시).
        if (CurrentState == SystemState.PowerOff && statusText != null)
            statusText.text = $"INSERT BATTERY ({InstalledBatteries}/{requiredBatteries})";
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

    // 다중 슬롯 패널(MultiBatterySlotPanel)이 있으면 그쪽의 '소멸+카운트(3개)' 방식이 복구를
    // 담당하므로, 배터리 1개로 즉시 복구하는 레거시 단일 스냅은 끈다(조기 복구 방지).
    bool _multiSlotPanelPresent;

    void Update()
    {
        if (!Object || !Object.IsValid || !Object.HasStateAuthority) return;

        if (CurrentState == SystemState.PowerOff && snappedBattery == null
            && !_multiSlotPanelPresent)
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

        // 2. 상태별 동작
        //    Idle: 안정화 시퀀스 시작(기존 — 결국 정전 PowerOff 로 이어짐).
        //    PowerOff: 충전 배터리 3개가 모였으면 버튼으로 복구(안정화).
        if (CurrentState == SystemState.Idle)
        {
            // 권한 이전 없이 현재 State Authority에게 시작 요청 (P1/P2 누구나 가능).
            RpcStartStabilize();
            return;
        }

        if (CurrentState == SystemState.PowerOff)
        {
            RpcRequestRecovery();
            return;
        }

        Debug.Log($"[MainControlSystem] Cannot act: Current state is {CurrentState}");
    }

    // PowerOff 에서 배터리 3개가 모였을 때 버튼으로 복구(안정화)를 요청.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcRequestRecovery()
    {
        if (CurrentState != SystemState.PowerOff) return;
        if (InstalledBatteries < requiredBatteries)
        {
            Debug.Log($"[MainControlSystem] 안정화 불가 — 배터리 {InstalledBatteries}/{requiredBatteries}.");
            return;
        }
        Debug.Log("[MainControlSystem] 배터리 3개 + 버튼 → 복구(안정화) 시작.");
        StartCoroutine(Reboot());
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
        InstalledBatteries = 0; // 배터리 설치 카운트 초기화 (이번 정전 복구 시작).
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

        // 자동 복구 제거: 배터리가 스냅돼도 곧바로 리부트(불 복구)하지 않는다.
        // 설치 요건만 충족시키고, 실제 안정화는 CRT 버튼 입력
        // (OnStabilizeButtonPressed → RpcRequestRecovery)이 전담한다.
        // 레거시 단일 스냅은 슬롯이 1개뿐이므로, 이 배터리를 복구 요건 충족으로 처리한다.
        if (Object.HasStateAuthority)
            InstalledBatteries = Mathf.Max(InstalledBatteries, requiredBatteries);
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
