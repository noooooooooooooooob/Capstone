using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Fusion;

namespace Stage1
{
    /// <summary>
    /// MainControlSystem과 같은 GameObject(또는 자식 어디든)에 부착하는 외부 다중 슬롯 모듈.
    ///
    /// 동작 (소멸 + 카운트 방식):
    ///   - PowerOff 상태에서 매 프레임 폴링 (권한자 피어에서만)
    ///   - 각 슬롯 색상에 매칭하는 "해동(충전)된" 배터리가 슬롯 근처에 오면
    ///     배터리를 네트워크에서 소멸(Despawn)시키고 카운트를 1 올린다.
    ///     (물리적으로 끼워 넣지 않는다 — 부모/스케일/권위 충돌 없이 양쪽 동기화가 깔끔하다.)
    ///   - 필요한 개수(슬롯 수)가 모두 채워지면 MainControlSystem.OnBatteryInserted() → Reboot(복구/안정화).
    ///
    /// 소멸 권한:
    ///   Despawn 은 배터리의 State Authority 가 필요하다. 슬롯 폴링은 MainControl 권한자에서 돌지만
    ///   배터리 권한은 마지막에 잡은 사람에게 있을 수 있다. 그래서 권한이 없으면 RequestStateAuthority 로
    ///   먼저 요청하고, 권한이 넘어온 다음 프레임에 Despawn 한다.
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
        [Tooltip("배터리가 이 거리 안에 들어오면 소멸시키고 카운트한다.")]
        public float snapDistance = 0.35f;

        bool[] slotFilled;
        bool rebootTriggered;

        void Awake()
        {
            if (mainControl == null) mainControl = GetComponent<MainControlSystem>();
            if (mainControl == null) mainControl = Object.FindFirstObjectByType<MainControlSystem>();

            int n = slots != null ? slots.Length : 0;
            slotFilled = new bool[n];

            // 복구에 필요한 배터리 수 = 슬롯 수. (양쪽 피어가 동일 슬롯 구성을 가지므로 동일.)
            if (mainControl != null && n > 0) mainControl.requiredBatteries = n;
        }

        void Update()
        {
            if (mainControl == null) return;
            if (slots == null || slots.Length == 0) return;
            if (slotFilled == null || slotFilled.Length != slots.Length)
                slotFilled = new bool[slots.Length];

            if (mainControl.Object == null || !mainControl.Object.IsValid) return;

            // 권한 있는 피어만 처리 (네트워크 정합성 — Despawn/카운트 쓰기는 권한자만).
            if (!mainControl.Object.HasStateAuthority) return;

            var state = mainControl.CurrentState;

            if (state != MainControlSystem.SystemState.PowerOff)
            {
                // 복구 후 Idle 로 돌아오면 다음 정전을 위해 리셋.
                if (rebootTriggered && state == MainControlSystem.SystemState.Idle)
                {
                    for (int i = 0; i < slotFilled.Length; i++) slotFilled[i] = false;
                    rebootTriggered = false;
                }
                return;
            }

            GameObject[] allBatteries = GameObject.FindGameObjectsWithTag("Battery");

            for (int i = 0; i < slots.Length; i++)
            {
                if (slotFilled[i]) continue;
                if (slots[i] == null) continue;

                LightBallColor expectedColor = (slotColors != null && i < slotColors.Length)
                    ? slotColors[i]
                    : (LightBallColor)i;

                GameObject closest = null;
                float closestDist = snapDistance;

                foreach (var bat in allBatteries)
                {
                    if (bat == null) continue;

                    // 해동(충전) 여부 + 색상 매칭 체크.
                    var bState = bat.GetComponent<BatteryState>();
                    bool isMelted;
                    LightBallColor bColor;

                    if (bState != null)
                    {
                        isMelted = bState.IsMelted;
                        bColor = bState.Color;
                    }
                    else
                    {
                        isMelted = bat.GetComponent<MeltedBattery>() != null;
                        var bTag = bat.GetComponent<BatteryColorTag>();
                        bColor = bTag != null ? bTag.color : LightBallColor.Red;
                    }

                    if (!isMelted) continue;
                    if (bColor != expectedColor) continue;

                    // 잡고 있는 중이면 아직 소멸하지 않는다(놓을 때 카운트).
                    var grab = bat.GetComponent<XRGrabInteractable>();
                    if (grab != null && grab.isSelected) continue;

                    float d = Vector3.Distance(bat.transform.position, slots[i].position);
                    if (d < closestDist) { closestDist = d; closest = bat; }
                }

                if (closest != null)
                    TryConsume(closest, i, expectedColor);
            }

            // 모든 슬롯이 채워졌으면 복구(안정화) 트리거.
            if (rebootTriggered) return;
            for (int i = 0; i < slotFilled.Length; i++)
                if (!slotFilled[i]) return;

            rebootTriggered = true;
            Debug.Log("[MultiBatterySlotPanel] 모든 배터리 설치 완료 → 복구(안정화) 트리거.");
            mainControl.OnBatteryInserted();
        }

        /// <summary>
        /// 매칭 배터리를 소멸시키고 카운트를 올린다. 배터리 권한이 없으면 먼저 요청하고
        /// (권한 이전은 비동기 ~1RTT) 다음 프레임에 다시 시도한다.
        /// </summary>
        void TryConsume(GameObject bat, int slotIndex, LightBallColor color)
        {
            var no = bat.GetComponent<NetworkObject>();
            if (no == null || !no.IsValid)
            {
                // 네트워크 오브젝트가 아니면(예외) 그냥 비활성 처리하고 카운트.
                bat.SetActive(false);
                MarkFilled(slotIndex, color);
                return;
            }

            if (!no.HasStateAuthority)
            {
                // 배터리 권한을 끌어온다. 다음 프레임에 권한이 넘어오면 Despawn.
                no.RequestStateAuthority();
                return;
            }

            mainControl.Runner.Despawn(no);
            MarkFilled(slotIndex, color);
        }

        void MarkFilled(int slotIndex, LightBallColor color)
        {
            slotFilled[slotIndex] = true;
            mainControl.InstalledBatteries++;
            LockDispenser(color);

            int filled = 0;
            for (int i = 0; i < slotFilled.Length; i++) if (slotFilled[i]) filled++;
            Debug.Log($"[MultiBatterySlotPanel] Slot {slotIndex} 설치(소멸) — ({filled}/{slots.Length}).");
        }

        void LockDispenser(LightBallColor color)
        {
            var dispensers = Object.FindObjectsByType<BatteryDispenser>(FindObjectsSortMode.None);
            foreach (var d in dispensers)
                if (d.batteryColor == color) { d.Lock(); break; }
        }
    }
}
