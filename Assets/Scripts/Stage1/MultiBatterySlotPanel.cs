using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Stage1
{
    /// <summary>
    /// MainControlSystem과 같은 GameObject(또는 자식 어디든)에 부착하는 외부 다중 슬롯 모듈.
    /// MainControlSystem 코드는 한 줄도 안 건드림.
    ///
    /// 동작:
    ///   - PowerOff 상태에서 매 프레임 폴링
    ///   - 각 슬롯은 자기 색상에 매칭하는 (BatteryColorTag.color == slotColors[i]) 해동된 배터리만 받음
    ///   - 같은 색이 이미 다른 슬롯에 들어가 있으면 추가 받지 않음 (중복 카운트 방지)
    ///   - 모든 슬롯이 채워지면 MainControlSystem.OnBatteryInserted() 호출 → Reboot 트리거
    ///
    /// 세팅:
    ///   - mainControl: 비워두면 GetComponent / FindFirstObjectByType로 자동 검출
    ///   - slots[N], slotColors[N]: 1:1 매칭. 보통 3개 (Red/Yellow/Blue)
    ///   - 활성 시 mainControl.batterySlot 은 None으로 비워두는 걸 권장 (Legacy 단일 슬롯과 충돌 방지)
    /// </summary>
    [DisallowMultipleComponent]
    public class MultiBatterySlotPanel : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("비우면 같은 GO 또는 씬에서 자동 검출.")]
        public MainControlSystem mainControl;

        [Header("Slots (인덱스가 slotColors와 1:1 매칭)")]
        public Transform[] slots;
        public LightBallColor[] slotColors;

        [Header("Settings")]
        public float snapDistance = 0.35f;

        GameObject[] snappedBatteries;
        bool rebootTriggered;

        void Awake()
        {
            if (mainControl == null) mainControl = GetComponent<MainControlSystem>();
            if (mainControl == null) mainControl = Object.FindFirstObjectByType<MainControlSystem>();

            int n = slots != null ? slots.Length : 0;
            snappedBatteries = new GameObject[n];
        }

        void Update()
        {
            if (mainControl == null) return;
            if (slots == null || slots.Length == 0) return;
            if (snappedBatteries == null || snappedBatteries.Length != slots.Length)
                snappedBatteries = new GameObject[slots.Length];

            // [Networked] 프로퍼티는 Spawned() 호출 후에만 접근 가능 —
            // NetworkObject가 valid 아니면 즉시 return (CurrentState 접근 전에 차단).
            if (mainControl.Object == null || !mainControl.Object.IsValid) return;

            // 권한이 있는 피어만 스냅 처리
            if (!mainControl.Object.HasStateAuthority) return;

            var state = mainControl.CurrentState;

            // PowerOff 사이클 새로 시작되면 트리거 + 슬롯 리셋
            if (state != MainControlSystem.SystemState.PowerOff)
            {
                if (rebootTriggered && state == MainControlSystem.SystemState.Idle)
                {
                    // Reboot 완료 후 Idle 복귀 — 다음 사이클 대비 리셋
                    for (int i = 0; i < snappedBatteries.Length; i++) snappedBatteries[i] = null;
                    rebootTriggered = false;
                }
                return;
            }

            GameObject[] allBatteries = GameObject.FindGameObjectsWithTag("Battery");

            for (int i = 0; i < slots.Length; i++)
            {
                if (snappedBatteries[i] != null) continue;
                if (slots[i] == null) continue;

                LightBallColor expectedColor = (slotColors != null && i < slotColors.Length)
                    ? slotColors[i]
                    : (LightBallColor)i;

                GameObject closest = null;
                float closestDist = snapDistance;

                foreach (var bat in allBatteries)
                {
                    if (bat == null) continue;

                    // 해동 필수
                    if (bat.GetComponent<MeltedBattery>() == null) continue;

                    // 색상 매칭
                    var ct = bat.GetComponent<BatteryColorTag>();
                    if (ct == null || ct.color != expectedColor) continue;

                    // 중복 방지 — 이미 다른 슬롯에 들어간 배터리
                    bool claimed = false;
                    for (int j = 0; j < snappedBatteries.Length; j++)
                        if (snappedBatteries[j] == bat) { claimed = true; break; }
                    if (claimed) continue;

                    var grab = bat.GetComponent<XRGrabInteractable>();
                    if (grab != null && grab.isSelected) continue;

                    float d = Vector3.Distance(bat.transform.position, slots[i].position);
                    if (d < closestDist) { closestDist = d; closest = bat; }
                }

                if (closest != null)
                    SnapToSlot(closest, i);
            }

            // 3개 다 채워졌는지
            if (rebootTriggered) return;
            for (int i = 0; i < snappedBatteries.Length; i++)
                if (snappedBatteries[i] == null) return;

            rebootTriggered = true;
            Debug.Log("[MultiBatterySlotPanel] 모든 슬롯 채워짐 → Reboot 트리거.");
            mainControl.OnBatteryInserted();
        }

        void SnapToSlot(GameObject bat, int slotIndex)
        {
            snappedBatteries[slotIndex] = bat;
            Transform slot = slots[slotIndex];

            var grab = bat.GetComponent<XRGrabInteractable>();
            if (grab) grab.throwOnDetach = false;

            var rb = bat.GetComponent<Rigidbody>();
            if (rb) rb.isKinematic = true;

            Vector3 worldScale = bat.transform.lossyScale;
            bat.transform.SetParent(slot, true);
            bat.transform.localPosition = Vector3.zero;
            bat.transform.localRotation = Quaternion.identity;

            Vector3 parentLossy = slot.lossyScale;
            bat.transform.localScale = new Vector3(
                worldScale.x / (parentLossy.x != 0 ? parentLossy.x : 1),
                worldScale.y / (parentLossy.y != 0 ? parentLossy.y : 1),
                worldScale.z / (parentLossy.z != 0 ? parentLossy.z : 1)
            );

            int filled = 0;
            for (int i = 0; i < snappedBatteries.Length; i++)
                if (snappedBatteries[i] != null) filled++;

            Debug.Log($"[MultiBatterySlotPanel] Slot {slotIndex} ({slot.name}, color={(slotColors != null && slotIndex < slotColors.Length ? slotColors[slotIndex].ToString() : slotIndex.ToString())}) 채움 ({filled}/{slots.Length}).");

            if (mainControl != null && mainControl.statusText != null)
                mainControl.statusText.text = $"INSERT BATTERY ({filled}/{slots.Length})";
        }
    }
}
