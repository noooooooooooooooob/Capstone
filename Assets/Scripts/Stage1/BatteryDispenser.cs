using UnityEngine;

public class BatteryDispenser : MonoBehaviour
{
    [Header("배터리 설정")]
    public GameObject batteryPrefab;        // 배터리 Prefab 연결
    public Transform spawnPoint;            // 배터리 나오는 위치

    [Header("설정")]
    public int maxDispense = 3;             // 최대 배출 횟수
    public float spawnCooldown = 1f;        // 연속 클릭 방지 (초)

    private int dispenseCount = 0;
    private float lastDispenseTime = -999f;

    // UI Button OnClick 또는 PhysicalButton에서 호출
    public void OnDispenseButtonPressed()
    {
        // 쿨다운 체크
        if (Time.time - lastDispenseTime < spawnCooldown) return;

        // 최대 횟수 체크
        if (dispenseCount >= maxDispense)
        {
            Debug.Log("배터리 없음!");
            return;
        }

        SpawnBattery();
    }

    void SpawnBattery()
    {
        if (batteryPrefab == null)
        {
            Debug.LogError("Battery Prefab이 연결되지 않았습니다!");
            return;
        }

        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : transform.position + transform.forward * 0.2f;
        Quaternion spawnRot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        GameObject battery = Instantiate(batteryPrefab, spawnPos, spawnRot);

        // Rigidbody 있으면 살짝 앞으로 튀어나오게
        Rigidbody rb = battery.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = transform.forward * 0.3f;
        }

        dispenseCount++;
        lastDispenseTime = Time.time;

        Debug.Log($"배터리 배출! ({dispenseCount}/{maxDispense})");
    }
}