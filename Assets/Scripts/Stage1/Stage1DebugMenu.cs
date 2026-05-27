using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Stage1
{
    /// <summary>
    /// Debug UI for Stage 1 to quickly skip puzzles.
    /// Should be attached to a Canvas in the scene.
    /// </summary>
    public class Stage1DebugMenu : MonoBehaviour
    {
        [Header("UI References")]
        public GameObject menuPanel;
        public Button toggleButton;
        public Button skipCurrentButton;
        public Button skipPipeButton;
        public Button skipZooButton;
        public Button skipLightButton;
        public Button skipAllButton;

        [Header("Auto Setup")]
        public bool buildMissingUi = true;
        public Vector2 panelSize = new Vector2(420f, 300f);

        private void Awake()
        {
            if (buildMissingUi && (menuPanel == null || toggleButton == null))
                BuildMissingUi();
        }

        private void Start()
        {
            if (toggleButton) toggleButton.onClick.AddListener(ToggleMenu);
            if (skipCurrentButton) skipCurrentButton.onClick.AddListener(SkipCurrentPuzzle);
            if (skipPipeButton) skipPipeButton.onClick.AddListener(() => ForceCompletePuzzle(0));
            if (skipZooButton) skipZooButton.onClick.AddListener(() => ForceCompletePuzzle(1));
            if (skipLightButton) skipLightButton.onClick.AddListener(() => ForceCompletePuzzle(2));
            if (skipAllButton) skipAllButton.onClick.AddListener(SkipAllPuzzles);

            // Hide menu by default
            if (menuPanel) menuPanel.SetActive(false);
        }

        public void ToggleMenu()
        {
            if (menuPanel) menuPanel.SetActive(!menuPanel.activeSelf);
        }

        public void SkipCurrentPuzzle()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.DebugCompleteCurrentPuzzle();
        }

        public void ForceCompletePuzzle(int index)
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.DebugCompletePuzzle(index);
        }

        public void SkipAllPuzzles()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.DebugCompleteAllRemainingPuzzles();
        }

        void BuildMissingUi()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            if (GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = 10f;
            }
            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
            if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            var root = transform as RectTransform;
            if (root != null)
            {
                root.sizeDelta = panelSize;
                root.localScale = Vector3.one * 0.0015f;
            }

            if (toggleButton == null)
                toggleButton = CreateButton("Toggle Debug", new Vector2(0f, 185f), transform);

            if (menuPanel == null)
            {
                menuPanel = new GameObject("Debug Skip Panel", typeof(RectTransform), typeof(Image));
                menuPanel.transform.SetParent(transform, false);

                var panelRect = (RectTransform)menuPanel.transform;
                panelRect.sizeDelta = panelSize;
                panelRect.anchoredPosition = Vector2.zero;

                var panelImage = menuPanel.GetComponent<Image>();
                panelImage.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
            }

            if (skipCurrentButton == null)
                skipCurrentButton = CreateButton("Skip Current", new Vector2(0f, 90f), menuPanel.transform);
            if (skipPipeButton == null)
                skipPipeButton = CreateButton("Skip Pipe", new Vector2(0f, 35f), menuPanel.transform);
            if (skipZooButton == null)
                skipZooButton = CreateButton("Skip Zoo", new Vector2(0f, -20f), menuPanel.transform);
            if (skipLightButton == null)
                skipLightButton = CreateButton("Skip Light", new Vector2(0f, -75f), menuPanel.transform);
            if (skipAllButton == null)
                skipAllButton = CreateButton("Skip All", new Vector2(0f, -130f), menuPanel.transform);
        }

        Button CreateButton(string label, Vector2 anchoredPosition, Transform parent)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(300f, 42f);
            rect.anchoredPosition = anchoredPosition;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.15f, 0.18f, 0.22f, 0.95f);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);

            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 22f;
            text.color = Color.white;

            return go.GetComponent<Button>();
        }
    }
}
