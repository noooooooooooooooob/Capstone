using UnityEngine;

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

    bool _subscribed;
    bool _started;
    bool _submitted;
    float _startTime;

    /// <summary>
    /// 클리어 타임 측정 시작. Button.onClick(UnityEvent)에 직접 연결 가능.
    /// 이미 시작됐으면 무시(중복 호출 안전).
    /// </summary>
    public void StartTimer()
    {
        StartClock();
    }

    void Update()
    {
        // GameManager는 Fusion이 네트워크 스폰하므로 Start보다 늦을 수 있음 → 구독 보장
        if (!_subscribed) TrySubscribe();
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
        if (_submitted || !_started) return;

        // 이벤트는 권한자에서만 오지만 이중 안전장치로 한 번 더 확인
        if (GameManager.Instance != null && !GameManager.Instance.HasStateAuthority) return;

        double seconds = Time.time - _startTime;
        if (seconds <= 0.0)
        {
            Debug.LogWarning("[LeaderboardSubmitter] 측정된 클리어 타임이 0 이하 — 제출 생략.");
            return;
        }

        _submitted = true;

        if (LeaderboardManager.Instance != null)
            LeaderboardManager.Instance.SubmitClearTime(seconds);
        else
            Debug.LogWarning("[LeaderboardSubmitter] LeaderboardManager.Instance 없음 — 씬에 매니저를 배치했는지 확인하세요.");
    }
}
