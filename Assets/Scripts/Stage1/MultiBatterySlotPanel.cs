using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Fusion;

namespace Stage1
{
    /// <summary>
    /// MainControlSystem과 같은 GameObject(또는 자식 어디든)에 부착하는 외부 다중 슬롯 모듈.
    /// 동작:
    ///   - PowerOff 상태에서 매 프레임 폴링
    ///   - 각 슬롯은 자기 색상에 매칭하는 (BatteryState.Color == slotColors[i]) 해동된 배터리만 받음
    ///   - 모든 슬롯이 채워지면 MainControlSystem.OnBatteryInserted() 호출 → Reboot 트리거
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

            if (mainControl.Object == null || !mainControl.Object.IsValid) return;

            // 권한이 있는 피어만 스냅 처리 (네트워크 정합성 유지)
            if (!mainControl.Object.HasStateAuthority) return;

            var state = mainControl.CurrentState;

            if (state != MainControlSystem.SystemState.PowerOff)
            {
                if (rebootTriggered && state == MainControlSystem.SystemState.Idle)
                {
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

                    // 해동 여부 체크 (Networked state 우선)
                    var bState = bat.GetComponent<BatteryState>();
                    bool isMelted = false;
                    LightBallColor bColor = LightBallColor.Red;

                    if (bState != null)
                    {
                        isMelted = bState.IsMelted;
                        bColor = bState.Color;
                    }
                    else
                    {
                        // Legacy fallback
                        isMelted = bat.GetComponent<MeltedBattery>() != null;
                        var bTag = bat.GetComponent<BatteryColorTag>();
                        if (bTag != null) bColor = bTag.color;
                    }

                    if (!isMelted) continue;
                    if (bColor != expectedColor) continue;

                    // 중복 방지
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

            Debug.Log($"[MultiBatterySlotPanel] Slot {slotIndex} ({slot.name}) 채움 ({filled}/{slots.Length}).");

            if (mainControl != null && mainControl.statusText != null)
                mainControl.statusText.text = $"INSERT BATTERY ({filled}/{slots.Length})";
        }
    }
}

