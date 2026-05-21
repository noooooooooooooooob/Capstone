using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 각 플레이어의 로컬 메인 카메라에 부착되는 world-space 자막 캔버스.
/// GameManager.OnDialoguePlayed (RPC 수신) 를 구독해 화면 하단에 텍스트 표시.
///
/// 씬에 1개만 두면 됨 — 카메라를 찾아 매 프레임 위치/회전 정렬.
/// 양쪽 플레이어 모두 동일한 자막을 자기 헤드셋에서 동시에 봄.
/// </summary>
public class SubtitleHUD : MonoBehaviour
{
    [Header("카메라 (비우면 Camera.main 자동 사용)")]
    public Camera targetCamera;

    [Header("카메라 로컬 좌표 — 자막 위치 (z=거리, y=수직 오프셋)")]
    public Vector3 localOffset = new Vector3(0f, -0.35f, 1.2f);

    [Header("캔버스 크기 (월드 단위)")]
    public Vector2 worldSize = new Vector2(1.6f, 0.4f);

    [Tooltip("월드 단위→픽셀 환산 비율. 1m가 1000px (0.001 스케일).")]
    public float pixelsPerUnit = 1000f;

    [Header("스타일")]
    [Tooltip("비우면 TMP 기본 폰트 사용. 한글 자막이면 한글 글리프 포함된 SDF 폰트 지정 필수.")]
    public TMP_FontAsset fontAsset;
    public int fontSize = 56;
    public FontStyles fontStyle = FontStyles.Normal;
    public Color textColor = Color.white;
    public Color speakerColor = new Color(1f, 0.82f, 0.2f);
    [Range(0f, 1f)] public float backgroundAlpha = 0.55f;

    [Header("페이드")]
    public float fadeInDuration = 0.18f;
    public float fadeOutDuration = 0.28f;

    [Header("디버그")]
    [Tooltip("Editor에서 GameManager 없이도 표시 테스트 — 페이드 없이 계속 표시 (위치/크기 튜닝용)")]
    public bool showOnStartForTest = false;
    public string testSpeaker = "연구원 A";
    [TextArea(2,4)] public string testText = "테스트 자막입니다.";

    Canvas _canvas;
    RectTransform _canvasRT;
    CanvasGroup _group;
    TextMeshProUGUI _textMesh;

    readonly Queue<QueuedLine> _queue = new Queue<QueuedLine>();
    Coroutine _playRoutine;
    bool _subscribed;

    struct QueuedLine { public string speaker; public string text; public float duration; }

    void Awake()
    {
        BuildCanvas();
    }

    void Start()
    {
        if (showOnStartForTest)
            ShowPersistent(testSpeaker, testText);
    }

    /// <summary>
    /// 큐/페이드 우회. 텍스트를 즉시 그리고 alpha=1로 유지.
    /// 위치·크기·폰트 튜닝 시 사용. 실제 대사 RPC가 들어오면 큐 루틴이 덮어씀.
    /// </summary>
    public void ShowPersistent(string speaker, string text)
    {
        string speakerHex = ColorUtility.ToHtmlStringRGB(speakerColor);
        _textMesh.text = string.IsNullOrEmpty(speaker)
            ? text
            : $"<color=#{speakerHex}><b>{speaker}</b></color>  {text}";
        _group.alpha = 1f;
    }

    void OnDisable()
    {
        UnsubscribeFromGameManager();
    }

    void Update()
    {
        TrySubscribeToGameManager();

        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null || _canvas == null) return;

        var ct = targetCamera.transform;
        _canvas.transform.position = ct.TransformPoint(localOffset);
        _canvas.transform.rotation = ct.rotation;
    }

    void TrySubscribeToGameManager()
    {
        if (_subscribed) return;
        if (GameManager.Instance == null) return;
        GameManager.Instance.OnDialoguePlayed += Enqueue;
        _subscribed = true;
    }

    void UnsubscribeFromGameManager()
    {
        if (!_subscribed) return;
        if (GameManager.Instance != null)
            GameManager.Instance.OnDialoguePlayed -= Enqueue;
        _subscribed = false;
    }

    void Enqueue(string speaker, string text, float duration)
    {
        _queue.Enqueue(new QueuedLine { speaker = speaker, text = text, duration = duration });
        if (_playRoutine == null) _playRoutine = StartCoroutine(PlayQueue());
    }

    IEnumerator PlayQueue()
    {
        while (_queue.Count > 0)
        {
            var line = _queue.Dequeue();
            yield return ShowLine(line.speaker, line.text, line.duration);
        }
        _playRoutine = null;
    }

    IEnumerator ShowLine(string speaker, string text, float duration)
    {
        string speakerHex = ColorUtility.ToHtmlStringRGB(speakerColor);
        _textMesh.text = string.IsNullOrEmpty(speaker)
            ? text
            : $"<color=#{speakerHex}><b>{speaker}</b></color>  {text}";

        yield return Fade(0f, 1f, fadeInDuration);
        yield return new WaitForSeconds(Mathf.Max(0.1f, duration - fadeInDuration - fadeOutDuration));
        yield return Fade(1f, 0f, fadeOutDuration);
        _textMesh.text = string.Empty;
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        if (duration <= 0f) { _group.alpha = to; yield break; }
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        _group.alpha = to;
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("[SubtitleCanvas]");
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 100;

        _canvasRT = canvasGo.GetComponent<RectTransform>();
        // pixelsPerUnit 만큼의 픽셀을 1 월드유닛으로 환산
        _canvasRT.sizeDelta = worldSize * pixelsPerUnit;
        _canvasRT.localScale = Vector3.one / pixelsPerUnit;

        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // 배경
        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, backgroundAlpha);
        var bgRT = bgGo.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // 텍스트
        var txtGo = new GameObject("Text");
        txtGo.transform.SetParent(canvasGo.transform, false);
        _textMesh = txtGo.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) _textMesh.font = fontAsset;
        _textMesh.fontStyle = fontStyle;
        _textMesh.alignment = TextAlignmentOptions.Center;
        _textMesh.color = textColor;
        _textMesh.fontSize = fontSize;
        _textMesh.enableAutoSizing = false;
        _textMesh.text = string.Empty;
        var txtRT = _textMesh.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(40f, 20f);
        txtRT.offsetMax = new Vector2(-40f, -20f);
    }
}
