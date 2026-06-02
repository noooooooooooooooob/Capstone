using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 게임 클리어 시점에 전체 클리어 타임을 LeaderboardManager로 제출하는 연결 컴포넌트.
///
/// - 자체적으로 전체 시간을 측정 (별도 타이머 오브젝트 불필요)
/// - 측정 시작: startMode 설정에 따름
///     · Manual      — 버튼 등에서 StartTimer()를 직접 호출 (Button.onClick에 연결)
///     · FirstPuzzle — 첫 퍼즐 활성화 시점(인트로 대사 제외)
///     · GameReady   — GameManager 준비(게임 시작) 시점
/// - 측정 종료: GameManager.OnAllPuzzlesCompleted (StateAuthority에서 발생)
/// - 호스트(StateAuthority)만 제출 → 협동 클리어 1회당 한 기록 (중복 방지)
///
/// 씬의 아무 GameObject에 붙이면 됨 (LeaderboardManager와 같은 오브젝트도 OK).
/// </summary>
public class LeaderboardSubmitter : MonoBehaviour
{
    public enum StartMode { Manual, FirstPuzzle, GameReady }

    [Tooltip("타이머 시작 방식. Manual이면 StartTimer()를 버튼 onClick 등에서 직접 호출")]
    public StartMode startMode = StartMode.Manual;

    [Header("제출 후")]
    [Tooltip("제출 완료 후 자동 새로고침할 랭킹 표시. 비워두면 씬에서 자동 탐색")]
    public LeaderboardDisplay display;

    [Tooltip("제출이 완료된 뒤 호출 (서버 반영 후). 결과 화면 전환 등에 연결")]
    public UnityEvent OnSubmitted;

    bool _subscribed;
    bool _started;
    bool _submitted;
    bool _displayed;
    float _startTime;

    /// <summary>
    /// 클리어 타임 측정 시작. Button.onClick(UnityEvent)에 직접 연결 가능.
    /// 이미 시작됐으면 무시(중복 호출 안전).
    /// </summary>
    public void StartTimer()
    {
        StartClock();
    }

    /// <summary>
    /// 측정을 끝내고 클리어 타임을 제출. Button.onClick(UnityEvent)에 직접 연결 가능.
    /// 호스트(StateAuthority)만 제출하며, 시작 전이거나 이미 제출했으면 무시(중복 호출 안전).
    /// </summary>
    public void StopTimer()
    {
        Finish();
    }

    void Update()
    {
        // GameManager는 Fusion이 네트워크 스폰하므로 Start보다 늦을 수 있음 → 구독 보장
        if (!_subscribed) TrySubscribe();

        // 호스트가 기록한 클리어 타임이 네트워크로 도착하면 표시 (호스트/게스트 공통).
        // 게스트는 OnAllPuzzlesCompleted 이벤트가 안 오므로 이 경로로 시간이 뜬다.
        if (!_displayed && GameManager.Instance != null && GameManager.Instance.ClearTimeSeconds > 0f)
            ShowTime(GameManager.Instance.ClearTimeSeconds);
    }

    void ShowTime(float seconds)
    {
        if (_displayed) return;
        _displayed = true;
        if (display == null) display = FindFirstObjectByType<LeaderboardDisplay>(FindObjectsInactive.Include);
        if (display != null) display.SetMyTime(seconds);
    }

    void TrySubscribe()
    {
        if (_subscribed || GameManager.Instance == null) return;

        GameManager.Instance.OnPuzzleActivated += HandlePuzzleActivated;
        GameManager.Instance.OnAllPuzzlesCompleted += HandleAllCompleted;
        _subscribed = true;

        // GameReady 모드: 구독되는 순간(=GameManager 준비됨)을 시작으로
        if (startMode == StartMode.GameReady) StartClock();
    }

    void OnDestroy()
    {
        if (!_subscribed || GameManager.Instance == null) return;
        GameManager.Instance.OnPuzzleActivated -= HandlePuzzleActivated;
        GameManager.Instance.OnAllPuzzlesCompleted -= HandleAllCompleted;
    }

    void HandlePuzzleActivated(int index)
    {
        if (startMode == StartMode.FirstPuzzle) StartClock();
    }

    void StartClock()
    {
        if (_started) return;
        _started = true;
        _startTime = Time.time;
    }

    void HandleAllCompleted()
    {
        Finish();
    }

    void Finish()
    {
        if (_submitted || !_started) return;

        double seconds = Time.time - _startTime;
        if (seconds <= 0.0)
        {
            Debug.LogWarning("[LeaderboardSubmitter] 측정된 클리어 타임이 0 이하 — 생략.");
            return;
        }

        _submitted = true;

        // 이번에 플레이한 클리어 타임을 화면에 표시
        ShowTime((float)seconds);

        // 제출은 호스트(StateAuthority)만 — 협동 클리어 1회당 한 기록
        if (GameManager.Instance != null && !GameManager.Instance.HasStateAuthority) return;

        // 호스트가 측정한 시간을 네트워크로 브로드캐스트 → 게스트도 같은 시간 표시
        if (GameManager.Instance != null) GameManager.Instance.SetClearTime((float)seconds);

        if (LeaderboardManager.Instance != null)
            _ = SubmitAndRefresh(seconds);
        else
            Debug.LogWarning("[LeaderboardSubmitter] LeaderboardManager.Instance 없음 — 씬에 매니저를 배치했는지 확인하세요.");
    }

    async Task SubmitAndRefresh(double seconds)
    {
        // 제출이 서버에 반영될 때까지 기다린 뒤 표시를 갱신해야 새 기록이 보임
        await LeaderboardManager.Instance.SubmitClearTimeAsync(seconds);

        if (display == null) display = FindFirstObjectByType<LeaderboardDisplay>(FindObjectsInactive.Include);
        if (display != null) display.Refresh();

        OnSubmitted?.Invoke();
    }
}
