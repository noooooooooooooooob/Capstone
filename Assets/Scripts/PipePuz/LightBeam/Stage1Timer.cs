using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 간단한 클리어 타이머.
    ///   StartTimer() — 버튼 클릭 시 시작
    ///   StopTimer()  — 문 열림 시 멈춤
    /// Display(TMP_Text) 에 "mm:ss.cc" 형식으로 매 프레임 텍스트 갱신.
    /// </summary>
    public class Stage1Timer : MonoBehaviour
    {
        [Header("Display")]
        [Tooltip("타이머 텍스트가 표시될 TMP_Text. 비워두면 같은 GameObject 에서 찾음.")]
        public TextMeshPro Display;

        [Header("Auto-subscribe (선택)")]
        [Tooltip("이 Button 의 onClick 에서 자동으로 StartTimer() 호출. 비워두면 외부에서 직접 호출.")]
        public Button StartButton;

        [Tooltip("StartButton 이 비어있을 때 씬에서 이 이름의 GameObject 아래 첫 Button 을 자동 검색.")]
        public string AutoFindButtonRootName = "Stage1_MainDisplayControlPanel";

        [Header("Initial text")]
        public string IdleText = "00:00.00";

        [Header("Debug")]
        public bool LogEvents = true;

        bool _running;
        bool _stopped;
        float _elapsed;

        void Awake()
        {
            if (Display == null) Display = GetComponent<TextMeshPro>();
            if (Display != null) Display.text = IdleText;
        }

        void OnEnable()
        {
            TryAutoFindButton();
            if (StartButton != null)
            {
                StartButton.onClick.AddListener(StartTimer);
                if (LogEvents)
                    Debug.Log($"[Stage1Timer:{name}] subscribed to Button '{StartButton.name}' onClick.");
            }
        }

        void OnDisable()
        {
            if (StartButton != null)
                StartButton.onClick.RemoveListener(StartTimer);
        }

        void TryAutoFindButton()
        {
            if (StartButton != null) return;
            if (string.IsNullOrEmpty(AutoFindButtonRootName)) return;
            var root = GameObject.Find(AutoFindButtonRootName);
            if (root == null) return;
            var btn = root.GetComponentInChildren<Button>(true);
            if (btn != null)
            {
                StartButton = btn;
                if (LogEvents)
                    Debug.Log($"[Stage1Timer:{name}] AutoFind: Button '{btn.name}' under '{root.name}'.");
            }
        }

        public void StartTimer()
        {
            if (_running || _stopped) return;
            _running = true;
            _elapsed = 0f;
            UpdateDisplay();
            if (LogEvents) Debug.Log($"[Stage1Timer:{name}] StartTimer.");
        }

        public void StopTimer()
        {
            if (!_running) return;
            _running = false;
            _stopped = true;
            UpdateDisplay();
            if (LogEvents) Debug.Log($"[Stage1Timer:{name}] StopTimer — final {_elapsed:F2}s.");
        }

        /// <summary>외부에서 강제로 리셋(다시 측정 가능).</summary>
        public void ResetTimer()
        {
            _running = false;
            _stopped = false;
            _elapsed = 0f;
            UpdateDisplay();
        }

        void Update()
        {
            if (!_running) return;
            _elapsed += Time.deltaTime;
            UpdateDisplay();
        }

        void UpdateDisplay()
        {
            if (Display == null) return;
            int totalSeconds = (int)_elapsed;
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            int hundredths = (int)((_elapsed - totalSeconds) * 100f);
            Display.text = $"{minutes:00}:{seconds:00}.{hundredths:00}";
        }
    }
}
