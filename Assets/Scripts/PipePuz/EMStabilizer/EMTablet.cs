using UnityEngine;

namespace PipePuz.EMStabilizer
{
    /// <summary>
    /// 태블릿/스캐너 시각화. EMStabilizerController 가 매 프레임 호출해 화면 상태를 갱신한다.
    ///
    /// 구성:
    /// - <see cref="WaveformLine"/> : 50 포인트 LineRenderer. 좌→우로 스크롤. 깨끗한 사인파에 노이즈가 섞여 흐른다.
    /// - <see cref="LockMeterFill"/> : 락 진행률을 채우는 Quad. localScale.x 로 채움량 조절.
    /// - <see cref="Slider1OkLamp"/>, <see cref="Slider2OkLamp"/> : 슬라이더가 tolerance 안에 들어왔는지 시각화하는 작은 큐브 (초/적색 머티리얼 교체).
    /// </summary>
    public class EMTablet : MonoBehaviour
    {
        [Header("Waveform")]
        public LineRenderer WaveformLine;
        [Tooltip("파형 영역의 가로/세로 크기 (m). 태블릿 스크린 로컬 좌표 기준.")]
        public Vector2 WaveformAreaSize = new Vector2(0.30f, 0.10f);
        [Tooltip("파형 영역 중심 (스크린 로컬). 0,0 이면 스크린 중심.")]
        public Vector2 WaveformAreaCenter = new Vector2(0f, 0.06f);
        [Tooltip("LineRenderer 포인트 개수.")]
        public int WaveformPointCount = 50;
        [Tooltip("기본 사인파 진동수(Hz).")]
        public float SignalHz = 1.5f;

        [Header("Lock Meter")]
        [Tooltip("락 진행률에 따라 가로로 채워지는 fill Transform. 채움 = localScale.x.")]
        public Transform LockMeterFill;
        [Tooltip("LockMeterFill 가 가질 수 있는 최대 localScale.x (=fill 100%).")]
        public float LockMeterMaxScaleX = 0.28f;

        [Header("OK Lamps")]
        public Renderer Slider1OkLamp;
        public Renderer Slider2OkLamp;
        public Renderer AngleOkLamp;

        [Header("Materials for lamps")]
        public Material LampOkMaterial;     // 초록
        public Material LampBadMaterial;    // 적/노

        [Header("Antenna glow target (옵션)")]
        [Tooltip("락 진행률에 비례해 Emission 강도가 올라가는 안테나 dish Renderer.")]
        public Renderer AntennaGlowRenderer;
        public Color AntennaGlowColor = new Color(0.4f, 1f, 0.6f);
        [Tooltip("락 진행률 1 일 때의 Emission 강도 multiplier.")]
        public float AntennaGlowMaxIntensity = 3.5f;

        float[] _samples;
        Vector3[] _points;

        void Awake()
        {
            if (WaveformLine != null)
            {
                _samples = new float[WaveformPointCount];
                _points = new Vector3[WaveformPointCount];
                WaveformLine.positionCount = WaveformPointCount;
                WaveformLine.useWorldSpace = false;
            }
        }

        /// <summary>매 프레임 컨트롤러가 호출.</summary>
        public void Tick(float tuningQuality, float lockProgress, bool angleOk, bool slider1Ok, bool slider2Ok)
        {
            UpdateWaveform(tuningQuality);
            UpdateLockMeter(lockProgress);
            UpdateLamps(angleOk, slider1Ok, slider2Ok);
            UpdateAntennaGlow(lockProgress);
        }

        void UpdateWaveform(float tuningQuality)
        {
            if (WaveformLine == null || _samples == null) return;
            int n = _samples.Length;

            // 좌측으로 1 칸 시프트.
            for (int i = 0; i < n - 1; i++) _samples[i] = _samples[i + 1];

            // 새 우측 샘플 = 사인파 + 노이즈(튜닝 품질이 낮을수록 큼).
            float t = Time.time;
            float baseSignal = Mathf.Sin(t * SignalHz * 2f * Mathf.PI) * 0.45f;
            float noiseAmt = Mathf.Clamp01(1f - tuningQuality) * 0.85f;
            float noise = (UnityEngine.Random.value - 0.5f) * 2f * noiseAmt;
            _samples[n - 1] = Mathf.Clamp(baseSignal + noise, -1f, 1f);

            // LineRenderer 위치 갱신 (스크린 로컬 평면 — 태블릿 스크린의 X 가로, Y 세로, Z 는 앞으로 살짝).
            float w = WaveformAreaSize.x;
            float h = WaveformAreaSize.y;
            float cx = WaveformAreaCenter.x;
            float cy = WaveformAreaCenter.y;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1); // 0..1
                float x = cx - w * 0.5f + u * w;
                float y = cy + _samples[i] * (h * 0.5f);
                _points[i] = new Vector3(x, y, 0.002f);
            }
            WaveformLine.SetPositions(_points);
        }

        void UpdateLockMeter(float lockProgress)
        {
            if (LockMeterFill == null) return;
            var s = LockMeterFill.localScale;
            s.x = Mathf.Clamp01(lockProgress) * LockMeterMaxScaleX;
            LockMeterFill.localScale = s;
        }

        void UpdateLamps(bool angleOk, bool slider1Ok, bool slider2Ok)
        {
            SetLamp(AngleOkLamp, angleOk);
            SetLamp(Slider1OkLamp, slider1Ok);
            SetLamp(Slider2OkLamp, slider2Ok);
        }

        void SetLamp(Renderer r, bool ok)
        {
            if (r == null) return;
            var mat = ok ? LampOkMaterial : LampBadMaterial;
            if (mat != null) r.sharedMaterial = mat;
        }

        void UpdateAntennaGlow(float lockProgress)
        {
            if (AntennaGlowRenderer == null) return;
            var mat = AntennaGlowRenderer.sharedMaterial;
            if (mat == null) return;
            if (!mat.HasProperty("_EmissionColor")) return;

            Color emission = AntennaGlowColor * (AntennaGlowMaxIntensity * Mathf.Clamp01(lockProgress));
            mat.SetColor("_EmissionColor", emission);
            mat.EnableKeyword("_EMISSION");
        }
    }
}
