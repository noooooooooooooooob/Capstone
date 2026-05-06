using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MainControlSystem : MonoBehaviour
{
    [Header("UI References")]
    public Slider stabilityBar;
    public TextMeshProUGUI stabilityText;
    public TextMeshProUGUI statusText;
    public GameObject batteryWarningPanel;
    public Image barFill;

    [Header("Settings")]
    public float stabilityDuration = 5f;   // 안정화 진행 시간 (초)
    public float startStability = 0f;
    public float maxStability = 100f;

    [Header("Lighting")]
    public Light[] roomLights;             // 방 조명들 연결

    // 상태
    public enum SystemState { Idle, Stabilizing, BatteryLow, PowerOff }
    public SystemState currentState = SystemState.Idle;

    // 외부에서 배터리 삽입 완료 시 호출
    public static MainControlSystem Instance;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        stabilityBar.value = 0f;
        stabilityBar.maxValue = maxStability;
        UpdateUI(0f);
        if (batteryWarningPanel) batteryWarningPanel.SetActive(false);
    }

    // 안정화 버튼 OnClick에 연결
    public void OnStabilizeButtonPressed()
    {
        if (currentState != SystemState.Idle) return;
        StartCoroutine(StabilizeSequence());
    }

    IEnumerator StabilizeSequence()
    {
        // 1. 안정화 진행
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

        // 2. 배터리 부족 경고
        currentState = SystemState.BatteryLow;
        stabilityBar.value = maxStability;
        UpdateUI(maxStability);

        yield return new WaitForSeconds(1f);

        if (statusText) statusText.text = "BATTERY CRITICAL!";
        if (batteryWarningPanel) batteryWarningPanel.SetActive(true);

        yield return new WaitForSeconds(1.5f);

        // 3. 전원 꺼짐
        currentState = SystemState.PowerOff;
        TurnOffLights();
        if (statusText) statusText.text = "POWER OFFLINE";
    }

    void UpdateUI(float value)
    {
        int percent = Mathf.RoundToInt(value);
        if (stabilityText) stabilityText.text = $"STABILITY: {percent}%";

        // 바 색상: 낮으면 빨강 → 높으면 초록
        if (barFill)
        {
            barFill.color = Color.Lerp(Color.red, Color.green, value / maxStability);
        }
    }

    void TurnOffLights()
    {
        foreach (var light in roomLights)
        {
            if (light) light.enabled = false;
        }
    }

    // 배터리 삽입 완료 시 외부 스크립트에서 호출
    public void OnBatteryInserted()
    {
        if (currentState != SystemState.PowerOff) return;
        StartCoroutine(Reboot());
    }

    IEnumerator Reboot()
    {
        // 조명 다시 켜기
        foreach (var light in roomLights)
        {
            if (light) light.enabled = true;
        }

        if (batteryWarningPanel) batteryWarningPanel.SetActive(false);
        stabilityBar.value = 0f;
        UpdateUI(0f);
        currentState = SystemState.Idle;

        if (statusText) statusText.text = "SYSTEM ONLINE";
        yield return null;
    }
}