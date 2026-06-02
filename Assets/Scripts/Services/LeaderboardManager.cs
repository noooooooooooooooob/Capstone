using System.Threading.Tasks;
using UnityEngine;
#if UGS_LEADERBOARDS
using System.Collections.Generic;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;
#endif

/// <summary>
/// UGS(Unity Gaming Services) Leaderboards 래퍼. 익명 로그인 → 점수 제출/조회를 담당.
///
/// 사용 전제:
///  1. Project Settings > Services 에서 프로젝트를 Unity Cloud에 연결
///  2. Package Manager로 com.unity.services.authentication / com.unity.services.leaderboards 설치
///  3. Unity Cloud Dashboard에서 leaderboardId 와 동일한 ID로 리더보드 생성
///     (클리어 타임이므로 Sort order = Ascending, Update type = Keep Best 권장)
///  4. Player Settings > Scripting Define Symbols 에 'UGS_LEADERBOARDS' 추가
///
/// 심볼이 없으면 모든 메서드는 안전하게 no-op(경고 로그)으로 동작 — 패키지 미설치 상태에서도 컴파일됨.
/// 씬에 빈 GameObject 하나에 붙여두면 자동으로 초기화됨(DontDestroyOnLoad).
/// </summary>
public class LeaderboardManager : MonoBehaviour
{
    public static LeaderboardManager Instance { get; private set; }

    [Header("Leaderboard")]
    [Tooltip("Unity Cloud Dashboard에서 만든 Leaderboard ID와 정확히 일치해야 함")]
    public string leaderboardId = "Capstone";

    /// <summary>UGS 초기화 + 로그인이 끝나 점수 제출이 가능한 상태인지.</summary>
    public bool IsReady { get; private set; }

    async void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        await InitializeAsync();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    Task _initTask;

    /// <summary>
    /// UGS 초기화 + 익명 로그인. 동시/중복 호출에 안전 —
    /// 진행 중인 초기화가 있으면 그 작업을 그대로 반환(single-flight).
    /// </summary>
    public Task InitializeAsync()
    {
        if (IsReady) return Task.CompletedTask;
        // 진행 중인 작업이 없거나 이전 시도가 실패했을 때만 새로 시작
        if (_initTask == null || _initTask.IsFaulted || _initTask.IsCanceled)
            _initTask = InitializeInternalAsync();
        return _initTask;
    }

    async Task InitializeInternalAsync()
    {
#if UGS_LEADERBOARDS
        try
        {
            // 항상 await — 이미 초기화됐으면 즉시 반환됨. (조건부 스킵 시,
            // Leaderboards 패키지가 등록되기 전 완료된 초기화를 재사용해 버리는 문제 방지)
            await UnityServices.InitializeAsync();

            // 이미 로그인됐거나 로그인 진행 중이면 SignIn을 다시 호출하지 않음
            if (!AuthenticationService.Instance.IsSignedIn &&
                !AuthenticationService.Instance.IsAuthorized)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            IsReady = AuthenticationService.Instance.IsSignedIn;
            Debug.Log($"[LeaderboardManager] Ready. PlayerId={AuthenticationService.Instance.PlayerId}");
        }
        catch (System.Exception e)
        {
            IsReady = false;
            _initTask = null; // 실패 시 다음 호출에서 재시도 가능하도록
            Debug.LogError($"[LeaderboardManager] 초기화 실패: {e}");
        }
#else
        Debug.LogWarning("[LeaderboardManager] UGS 비활성 상태 — 패키지 설치 후 Player Settings에 " +
                         "'UGS_LEADERBOARDS' 스크립팅 디파인 심볼을 추가하세요.");
        await Task.CompletedTask;
#endif
    }

    /// <summary>클리어 타임(초) 제출 — fire-and-forget. UI 흐름을 막지 않음.</summary>
    public void SubmitClearTime(double seconds)
    {
        _ = SubmitClearTimeAsync(seconds);
    }

    /// <summary>클리어 타임(초) 제출. 낮을수록 상위(리더보드 Ascending 가정).</summary>
    public async Task SubmitClearTimeAsync(double seconds)
    {
#if UGS_LEADERBOARDS
        if (!IsReady) await InitializeAsync();
        if (!IsReady) return;

        try
        {
            var entry = await LeaderboardsService.Instance.AddPlayerScoreAsync(leaderboardId, seconds);
            Debug.Log($"[LeaderboardManager] 제출 완료 — score={entry.Score} rank={entry.Rank}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LeaderboardManager] 점수 제출 실패: {e}");
        }
#else
        Debug.LogWarning($"[LeaderboardManager] (stub) SubmitClearTime({seconds:F2}s) — UGS 미설치");
        await Task.CompletedTask;
#endif
    }

#if UGS_LEADERBOARDS
    /// <summary>상위 N개 랭킹 조회. 결과 화면/메뉴에서 사용.</summary>
    public async Task<List<LeaderboardEntry>> GetTopScoresAsync(int limit = 10)
    {
        if (!IsReady) await InitializeAsync();
        if (!IsReady) return new List<LeaderboardEntry>();

        try
        {
            var res = await LeaderboardsService.Instance.GetScoresAsync(
                leaderboardId, new GetScoresOptions { Limit = limit });
            return res.Results;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LeaderboardManager] 랭킹 조회 실패: {e}");
            return new List<LeaderboardEntry>();
        }
    }

    /// <summary>내 순위 한 줄 조회 (없으면 null).</summary>
    public async Task<LeaderboardEntry> GetMyScoreAsync()
    {
        if (!IsReady) await InitializeAsync();
        if (!IsReady) return null;

        try
        {
            return await LeaderboardsService.Instance.GetPlayerScoreAsync(leaderboardId);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LeaderboardManager] 내 점수 조회 실패(미제출일 수 있음): {e.Message}");
            return null;
        }
    }
#endif
}
