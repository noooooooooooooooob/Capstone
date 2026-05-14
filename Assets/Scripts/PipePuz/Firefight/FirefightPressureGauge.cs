using UnityEngine;

namespace PipePuz.Firefight
{
    /// <summary>
    /// 펌프 옆 압력 게이지. Controller.OnPressureChanged 를 구독해서
    /// 모든 FillBars 의 localScale.y 를 pressure 비례로 변경 (bottom-anchored fill).
    /// 양면 게이지 (앞면 + 뒷면) 지원 — FillBars 와 FillRenderers 를 배열로.
    /// 색은 LowColor (낮은 압력) → HighColor (최대 압력) 로 lerp.
    /// </summary>
    public class FirefightPressureGauge : MonoBehaviour
    {
        [Header("Refs")]
        public FirefightController Controller;

        [Tooltip("Wrapper Transform 배열. 각각 localScale.y 가 pressure 비례로 변한다. " +
                 "양면 게이지면 [앞면, 뒷면] 두 개.")]
        public Transform[] FillBars;

        [Tooltip("FillBars 안쪽 cube 들의 Renderer 배열. 색 lerp 대상. FillBars 와 같은 인덱스 매핑.")]
        public Renderer[] FillRenderers;

        [Header("Tuning")]
        [Tooltip("Pressure 1.0 일 때 FillBar.localScale.y 값.")]
        public float MaxScale = 0.45f;

        public Color LowColor = new Color(0.9f, 0.25f, 0.18f);   // 빨강
        public Color HighColor = new Color(0.25f, 0.95f, 0.45f); // 초록

        Material[] _matInstances;

        void Start()
        {
            if (Controller == null)
            {
                Debug.LogWarning("[FirefightPressureGauge] Controller 가 null! Editor 빌드를 다시 누르세요.");
                return;
            }
            Controller.OnPressureChanged.AddListener(SetPressure);
            Debug.Log("[FirefightPressureGauge] Subscribed to Controller.OnPressureChanged");

            if (FillRenderers != null)
            {
                _matInstances = new Material[FillRenderers.Length];
                for (int i = 0; i < FillRenderers.Length; i++)
                {
                    if (FillRenderers[i] != null)
                        _matInstances[i] = FillRenderers[i].material; // 각 renderer 별 인스턴스
                }
            }
            SetPressure(Controller.CurrentPressure);
        }

        void OnDestroy()
        {
            if (Controller != null) Controller.OnPressureChanged.RemoveListener(SetPressure);
        }

        public void SetPressure(float pressure)
        {
            pressure = Mathf.Clamp01(pressure);

            // 모든 FillBars 의 scale.y 동시 갱신.
            if (FillBars != null)
            {
                float targetY = MaxScale * pressure;
                for (int i = 0; i < FillBars.Length; i++)
                {
                    if (FillBars[i] == null) continue;
                    var s = FillBars[i].localScale;
                    s.y = targetY;
                    FillBars[i].localScale = s;
                }
            }

            // 모든 renderer 의 색 동시 lerp.
            if (_matInstances != null)
            {
                Color c = Color.Lerp(LowColor, HighColor, pressure);
                for (int i = 0; i < _matInstances.Length; i++)
                {
                    var m = _matInstances[i];
                    if (m == null) continue;
                    m.color = c;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_EmissionColor"))
                    {
                        m.SetColor("_EmissionColor", c * 0.6f);
                        m.EnableKeyword("_EMISSION");
                    }
                }
            }

            Debug.Log($"[FirefightPressureGauge] SetPressure({pressure:F2}) → fill.y={MaxScale * pressure:F3}");
        }
    }
}
