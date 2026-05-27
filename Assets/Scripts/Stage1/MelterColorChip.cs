using System.Collections.Generic;
using UnityEngine;

namespace Stage1
{
    /// <summary>
    /// BatteryMelter와 같은 GameObject에 부착하는 외부 색상 모듈.
    /// BatteryMelter 코드는 한 줄도 안 건드림 (HandleLightBall의 다중 LightBall 지원 제외).
    ///
    /// 동작:
    ///   1) BatteryMelter.batterySlot 근처 Battery 폴링 → 코어 메시가 melted로 바뀌면
    ///      BatteryColorTag + MeltedBattery 자동 부착
    ///   2) lightBallHole 근처 LightBall 색상 검사 → 머신 색(color)과 다르면
    ///      warningRenderers를 깜빡여 시각 경고
    /// </summary>
    [RequireComponent(typeof(BatteryMelter))]
    [DisallowMultipleComponent]
    public class MelterColorChip : MonoBehaviour
    {
        [Header("Machine Color")]
        [Tooltip("이 머신이 받는 LightBall 색 (= 만들어내는 배터리 색).")]
        public LightBallColor color = LightBallColor.Red;

        [Tooltip("true: 배터리는 LightBall의 색을 따라감 (머신 1개 모드). " +
                 "false: 배터리는 머신 고유 색을 따라감 (머신 3개 모드 — 색 매칭 강제).")]
        public bool preferLightBallColor = false;

        [Header("Color Mismatch Warning")]
        [Tooltip("색이 안 맞는 LightBall이 hole에 놓이면 빨강으로 깜빡일 Renderer들 (보통 LightBallHole의 Renderer).")]
        public Renderer[] warningRenderers;
        public Color warningColor = new Color(1f, 0.2f, 0.2f, 1f);
        [Tooltip("초당 깜빡임 주기 (Hz).")]
        public float blinkSpeed = 4f;

        BatteryMelter melter;
        readonly HashSet<GameObject> alreadyTagged = new HashSet<GameObject>();

        // 경고 상태 캐싱
        Color[] originalColors;
        bool warningCached;
        bool warningActive;

        void Awake()
        {
            melter = GetComponent<BatteryMelter>();
        }

        void Update()
        {
            if (melter == null) return;
            HandleMeltDetection();
            HandleColorWarning();
        }

        // ── 해동 감지 + 색 태그 부착 ────────────────────────

        void HandleMeltDetection()
        {
            if (melter.batterySlot == null || melter.meltedBatteryCore == null) return;

            float range = Mathf.Max(melter.snapDistance, 0.1f) + 0.1f;

            GameObject[] all = GameObject.FindGameObjectsWithTag("Battery");
            foreach (var bat in all)
            {
                if (bat == null) continue;
                if (alreadyTagged.Contains(bat)) continue;

                float d = Vector3.Distance(bat.transform.position, melter.batterySlot.position);
                if (d > range) continue;
                if (!IsMelted(bat)) continue;

                LightBallColor effective = ResolveEffectiveColor();

                var ct = bat.GetComponent<BatteryColorTag>();
                if (ct == null) ct = bat.AddComponent<BatteryColorTag>();
                ct.color = effective;

                if (bat.GetComponent<MeltedBattery>() == null)
                    bat.AddComponent<MeltedBattery>();

                alreadyTagged.Add(bat);
                Debug.Log($"[MelterColorChip:{name}] Battery {bat.name} 해동 감지 → 색상 {effective} 태그");
            }
            alreadyTagged.RemoveWhere(b => b == null);
        }

        LightBallColor ResolveEffectiveColor()
        {
            if (!preferLightBallColor) return color;
            if (melter.lightBallHole == null) return color;

            float range = Mathf.Max(melter.snapDistance, 0.2f);
            GameObject[] balls = GameObject.FindGameObjectsWithTag("LightBall");
            float bestD = range;
            LightBallColorTag bestTag = null;
            foreach (var b in balls)
            {
                if (b == null) continue;
                float d = Vector3.Distance(b.transform.position, melter.lightBallHole.position);
                if (d > bestD) continue;
                var t = b.GetComponent<LightBallColorTag>();
                if (t == null) continue;
                bestD = d;
                bestTag = t;
            }
            return bestTag != null ? bestTag.color : color;
        }

        bool IsMelted(GameObject bat)
        {
            foreach (Transform child in bat.GetComponentsInChildren<Transform>())
            {
                if (!child.name.ToLower().Contains("core")) continue;
                var rend = child.GetComponent<Renderer>();
                if (rend == null) continue;
                return rend.sharedMaterial == melter.meltedBatteryCore;
            }
            return false;
        }

        // ── 색 불일치 시각 경고 ──────────────────────────────

        void HandleColorWarning()
        {
            if (warningRenderers == null || warningRenderers.Length == 0) return;
            if (melter.lightBallHole == null) return;

            bool mismatch = DetectColorMismatch();

            if (mismatch)
            {
                if (!warningCached) { CacheWarningOriginals(); warningCached = true; }
                warningActive = true;

                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * blinkSpeed * Mathf.PI * 2f);
                for (int i = 0; i < warningRenderers.Length; i++)
                {
                    var r = warningRenderers[i];
                    if (r == null || originalColors == null || i >= originalColors.Length) continue;
                    r.material.color = Color.Lerp(originalColors[i], warningColor, pulse);
                }
            }
            else if (warningActive)
            {
                // 색 복원
                for (int i = 0; i < warningRenderers.Length; i++)
                {
                    var r = warningRenderers[i];
                    if (r == null || originalColors == null || i >= originalColors.Length) continue;
                    r.material.color = originalColors[i];
                }
                warningActive = false;
            }
        }

        /// <summary>
        /// lightBallHole 근처에 색 안 맞는 LightBall이 있는지 검사.
        /// </summary>
        bool DetectColorMismatch()
        {
            float range = Mathf.Max(melter.snapDistance, 0.2f);
            GameObject[] balls = GameObject.FindGameObjectsWithTag("LightBall");
            foreach (var b in balls)
            {
                if (b == null) continue;
                float d = Vector3.Distance(b.transform.position, melter.lightBallHole.position);
                if (d > range) continue;
                var tag = b.GetComponent<LightBallColorTag>();
                if (tag == null) continue;
                if (tag.color != color) return true; // 안 맞는 색
            }
            return false;
        }

        void CacheWarningOriginals()
        {
            originalColors = new Color[warningRenderers.Length];
            for (int i = 0; i < warningRenderers.Length; i++)
            {
                if (warningRenderers[i] != null && warningRenderers[i].sharedMaterial != null)
                    originalColors[i] = warningRenderers[i].material.color;
            }
        }

        void OnDisable()
        {
            // 비활성될 때 색 복원 (혹시 깜빡임 중에 비활성됐을 경우)
            if (warningActive && warningRenderers != null && originalColors != null)
            {
                for (int i = 0; i < warningRenderers.Length; i++)
                {
                    var r = warningRenderers[i];
                    if (r == null || i >= originalColors.Length) continue;
                    r.material.color = originalColors[i];
                }
                warningActive = false;
            }
        }
    }
}
