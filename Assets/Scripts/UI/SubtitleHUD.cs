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
    public Vector3 localOffset = new Vector3(0f, -0.22f, 1.2f);

    [Header("캔버스 크기 (월드 단위)")]
    public Vector2 worldSize = new Vector2(1.6f, 0.4f);

    [Tooltip("월드 단위→픽셀 환산 비율. 1m가 1000px (0.001 스케일).")]
    public float pixelsPerUnit = 1000f;

    [Header("스타일")]
    [Tooltip("비우면 TMP 기본 폰트 사용. 한글 자막이면 한글 글리프 포함된 SDF 폰트 지정 필수.")]
    public TMP_FontAsset fontAsset;
    [Tooltip("자동 크기 OFF면 고정 크기. ON이면 최대 크기로 사용됨.")]
    public int fontSize = 56;
    [Tooltip("긴 자막이 박스를 넘으면 폰트를 줄여 맞춤 (세로 오버플로 방지)")]
    public bool autoSizeText = true;
    [Tooltip("자동 크기 ON일 때 줄어들 수 있는 최소 폰트 크기")]
    public int fontSizeMin = 28;
    public FontStyles fontStyle = FontStyles.Normal;
    public Color textColor = Color.white;
    public Color speakerColor = new Color(1f, 0.82f, 0.2f);
    [Range(0f, 1f)] public float backgroundAlpha = 0.55f;

    [Header("페이드")]
    public float fadeInDuration = 0.18f;
    public float fadeOutDuration = 0.28f;

    [Header("문장 분할")]
    [Tooltip("켜면 한 클립의 긴 대사를 문장(. ! ? … / 개행) 단위로 나눠 클립 길이 안에서 순서대로 표시. " +
             "보이스는 1회만 재생되고 자막만 전환됨. 끄면 전체 텍스트를 한 번에 표시(기존 동작).")]
    public bool splitLongTextIntoSentences = true;

    [Header("읽기 시간")]
    [Tooltip("보이스 클립이 없을 때만 적용 — 타이핑 완료 후 최소 정지(읽기) 시간(초).")]
    public float minReadTime = 1.2f;
    [Tooltip("보이스 클립 종료 후 자막이 남아있는 여유(초). GameManager.dialogueTail과 일치시킬 것.")]
    public float voiceTail = 0.3f;

    [Header("Animalese 음성 (보이스 클립 없을 때 폴백)")]
    [Tooltip("보이스 클립이 없는 라인에서만 글자마다 랜덤 재생할 짧은 클립들")]
    public AudioClip[] animaleseSyllables;
    [Tooltip("글자당 타이핑 간격(초). 보이스가 있으면 클립 길이에 맞춰 자동 압축됨.")]
    public float typeInterval = 0.06f;
    [Tooltip("피치 랜덤 범위")]
    public float pitchMin = 0.85f;
    public float pitchMax = 1.2f;

    AudioSource _animaleseSource;
    AudioSource _voiceSource;

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

    struct QueuedLine { public string speaker; public string text; public float duration; public AudioClip voice; }

    void Awake()
    {
        BuildCanvas();
        _animaleseSource = gameObject.AddComponent<AudioSource>();
        _animaleseSource.playOnAwake = false;
        _animaleseSource.spatialBlend = 0f;

        _voiceSource = gameObject.AddComponent<AudioSource>();
        _voiceSource.playOnAwake = false;
        _voiceSource.spatialBlend = 0f;
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

    void Enqueue(string speaker, string text, float duration, AudioClip voice)
    {
        _queue.Enqueue(new QueuedLine { speaker = speaker, text = text, duration = duration, voice = voice });
        if (_playRoutine == null) _playRoutine = StartCoroutine(PlayQueue());
    }

    IEnumerator PlayQueue()
    {
        while (_queue.Count > 0)
        {
            var line = _queue.Dequeue();
            yield return ShowLine(line.speaker, line.text, line.duration, line.voice);
        }
        _playRoutine = null;
    }

    IEnumerator ShowLine(string speaker, string text, float duration, AudioClip voice)
    {
        string speakerHex = ColorUtility.ToHtmlStringRGB(speakerColor);
        string prefix = string.IsNullOrEmpty(speaker)
            ? ""
            : $"<color=#{speakerHex}><b>{speaker}</b></color>  ";

        bool hasVoice = voice != null;

        // 한 클립(한 줄)을 문장 단위로 나눠 순서대로 표시. 보이스 클립은 1회만 재생되고,
        // 자막만 문장별로 전환되어 긴 대사도 화면에 다 안 들어가는 일이 없도록 한다.
        var segments = splitLongTextIntoSentences ? SplitIntoSentences(text) : null;
        if (segments == null || segments.Count == 0)
            segments = new List<string> { text };

        int totalChars = 0;
        for (int i = 0; i < segments.Count; i++) totalChars += Mathf.Max(1, segments[i].Length);

        // 표시 총 시간: 보이스가 있으면 클립 길이에 맞춰 끝나자마자 사라짐(+voiceTail 여유).
        // 없으면 duration을 따르되, 긴 자막이 잘리지 않게 타이핑+최소 읽기 시간을 보장.
        float total = hasVoice
            ? voice.length + voiceTail
            : Mathf.Max(duration, fadeInDuration + text.Length * typeInterval + minReadTime + fadeOutDuration);

        // 보이스는 줄 시작 시 한 번만 재생.
        if (hasVoice)
        {
            _voiceSource.Stop();
            _voiceSource.clip = voice;
            _voiceSource.pitch = 1f;
            _voiceSource.Play();
        }

        _textMesh.text = prefix;
        yield return Fade(0f, 1f, fadeInDuration);

        // 문장 전환에 쓸 수 있는 총 시간(페이드 제외)을 문장 길이에 비례 배분.
        float typingRegion = Mathf.Max(0.01f, total - fadeInDuration - fadeOutDuration);

        for (int s = 0; s < segments.Count; s++)
        {
            string seg = segments[s];
            float segDuration = typingRegion * (Mathf.Max(1, seg.Length) / (float)totalChars);

            // 글자별 리빌(동숲식). 보이스가 있으면 클립 길이에 맞춰 타이핑 간격 압축.
            float interval = typeInterval;
            if (seg.Length > 0)
                interval = Mathf.Min(typeInterval, segDuration * 0.6f / seg.Length);

            float typeTime = 0f;
            for (int i = 0; i < seg.Length; i++)
            {
                _textMesh.text = prefix + seg.Substring(0, i + 1);

                // 실제 보이스가 재생 중이면 글자별 animalese는 생략 (보이스 없을 때 폴백 전용).
                if (!hasVoice && !char.IsWhiteSpace(seg[i]) && animaleseSyllables != null && animaleseSyllables.Length > 0)
                {
                    _animaleseSource.clip = animaleseSyllables[Random.Range(0, animaleseSyllables.Length)];
                    _animaleseSource.pitch = Random.Range(pitchMin, pitchMax);
                    _animaleseSource.Play();
                }

                typeTime += interval;
                yield return new WaitForSeconds(interval);
            }

            // 남은 시간 동안 해당 문장을 유지(읽기 시간).
            float hold = segDuration - typeTime;
            if (hold > 0f) yield return new WaitForSeconds(hold);
        }

        yield return Fade(1f, 0f, fadeOutDuration);
        _textMesh.text = string.Empty;
    }

    /// <summary>
    /// 한 줄 텍스트를 문장 단위로 분할. 개행과 종결부호(. ! ? …)를 경계로 사용하되,
    /// 연속된 부호("...", "?!")는 한 문장으로 묶고, 줄표(—)는 분할하지 않는다.
    /// </summary>
    static List<string> SplitIntoSentences(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool isNewline = (c == '\n');
            bool isEnder = (c == '.' || c == '!' || c == '?' || c == '…'); // …

            if (isEnder)
            {
                // 다음 비공백 문자가 또 종결부호면 아직 끊지 않음 ("...", "?!" 등).
                int j = i + 1;
                if (j < text.Length)
                {
                    char n = text[j];
                    if (n == '.' || n == '!' || n == '?' || n == '…') continue;
                }
            }

            if (isEnder || isNewline)
            {
                int len = i - start + (isNewline ? 0 : 1);
                if (len > 0)
                {
                    string seg = text.Substring(start, len).Trim();
                    if (seg.Length > 0) result.Add(seg);
                }
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            string tail = text.Substring(start).Trim();
            if (tail.Length > 0) result.Add(tail);
        }
        return result;
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
        _textMesh.enableAutoSizing = autoSizeText;
        if (autoSizeText)
        {
            _textMesh.fontSizeMin = fontSizeMin;
            _textMesh.fontSizeMax = fontSize;
        }
        else
        {
            _textMesh.fontSize = fontSize;
        }
        _textMesh.text = string.Empty;
        var txtRT = _textMesh.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(40f, 20f);
        txtRT.offsetMax = new Vector2(-40f, -20f);
    }
}
