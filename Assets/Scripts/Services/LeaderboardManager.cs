using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;

/// <summary>
/// UGS(Unity Gaming Services) Leaderboards 래퍼. 익명 로그인 → 점수 제출/조회를 담당.
///
/// 사용 전제:
///  1. Project Settings > Services 에서 프로젝트를 Unity Cloud에 연결
///  2. Package Manager로 com.unity.services.authentication / com.unity.services.leaderboards 설치
///  3. Unity Cloud Dashboard에서 leaderboardId 와 동일한 ID로 리더보드 생성
///     (클리어 타임이므로 Sort order = Ascending, Update type = Keep Best 권장)
///
/// 씬의 빈 GameObject 하나에 붙여두면 자동으로 초기화됨(DontDestroyOnLoad).
/// </summary>
public class LeaderboardManager : MonoBehaviour
{
    public static LeaderboardManager Instance { get; private set; }

    [Header("Leaderboard")]
    [Tooltip("Unity Cloud Dashboard에서 만든 Leaderboard ID와 정확히 일치해야 함")]
    public string leaderboardId = "Capstone";

    /// <summary>UGS 초기화 + 로그인이 끝나 점수 제출이 가능한 상태인지.</summary>
    public bool IsReady { get; private set; }

    Task _initTask;

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

    /// <summary>
    /// UGS 초기화 + 익명 로그인. 동시/중복 호출에 안전 —
    /// 진행 중인 초기화가 있으면 그 작업을 그대로 반환(single-flight).
    /// </summary>
    public Task InitializeAsync()
    {
        if (IsReady) return Task.CompletedTask;
        if (_initTask == null || _initTask.IsFaulted || _initTask.IsCanceled)
            _initTask = InitializeInternalAsync();
        return _initTask;
    }

    async Task InitializeInternalAsync()
    {
        try
        {
            await UnityServices.InitializeAsync();

            // 이미 로그인됐거나 로그인 진행 중이면 SignIn을 다시 호출하지 않음
            if (!AuthenticationService.Instance.IsSignedIn &&
                !AuthenticationService.Instance.IsAuthorized)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            IsReady = AuthenticationService.Instance.IsSignedIn;

            // 에디터 타이밍 이슈 보정: leaderboards가 등록은 됐지만 init 패스에서
            // 누락된 경우, 누락된 패키지의 Initialize를 직접 호출해 살려준다.
            EnsureLeaderboardsInitialized();

            Debug.Log($"[LeaderboardManager] Ready. PlayerId={AuthenticationService.Instance.PlayerId}");
        }
        catch (System.Exception e)
        {
            IsReady = false;
            _initTask = null; // 실패 시 다음 호출에서 재시도 가능하도록
            Debug.LogError($"[LeaderboardManager] 초기화 실패: {e}");
        }
    }

    const System.Reflection.BindingFlags k_Flags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;

    static System.Type FindType(string fullName)
    {
        foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }

    /// <summary>
    /// leaderboards가 core 레지스트리에 등록은 됐지만(의존성도 충족) 초기화 패스에서
    /// 누락돼 LeaderboardsService.Instance가 null인 경우, 등록된 LeaderboardsInitializer의
    /// Initialize(CoreRegistry)를 직접 호출해 서비스 인스턴스를 살린다. (에디터 초기화 타이밍 보정)
    /// </summary>
    static void EnsureLeaderboardsInitialized()
    {
        // 이미 정상이면 아무것도 안 함
        try { var ok = LeaderboardsService.Instance; if (ok != null) return; }
        catch { /* null 상태 — 아래에서 보정 */ }

        try
        {
            // CorePackageRegistry.Instance.Registry.Tree.PackageTypeHashToInstance 에서
            // LeaderboardsInitializer 인스턴스를 찾는다.
            var regType = FindType("Unity.Services.Core.Internal.CorePackageRegistry");
            var inst = regType?.GetProperty("Instance", k_Flags)?.GetValue(null);
            var registry = regType?.GetProperty("Registry", k_Flags)?.GetValue(inst);
            var tree = registry?.GetType().GetProperty("Tree", k_Flags)?.GetValue(registry);
            var dict = tree?.GetType().GetField("PackageTypeHashToInstance", k_Flags)?.GetValue(tree)
                       as System.Collections.IDictionary;
            if (dict == null) { Debug.LogError("[LeaderboardManager] 레지스트리 접근 실패 — 보정 불가"); return; }

            object lbInitializer = null;
            foreach (var v in dict.Values)
            {
                if (v != null && v.GetType().FullName == "Unity.Services.Leaderboards.LeaderboardsInitializer")
                {
                    lbInitializer = v;
                    break;
                }
            }
            if (lbInitializer == null) { Debug.LogError("[LeaderboardManager] LeaderboardsInitializer 미등록 — 패키지 설치 확인"); return; }

            var coreRegType = FindType("Unity.Services.Core.Internal.CoreRegistry");
            var coreReg = coreRegType?.GetProperty("Instance", k_Flags)?.GetValue(null);
            if (coreReg == null) { Debug.LogError("[LeaderboardManager] CoreRegistry.Instance 없음"); return; }

            var initMethod = lbInitializer.GetType().GetMethod("Initialize", k_Flags, null, new[] { coreRegType }, null);
            if (initMethod == null) { Debug.LogError("[LeaderboardManager] Initialize(CoreRegistry) 메서드 못 찾음"); return; }

            initMethod.Invoke(lbInitializer, new[] { coreReg });

            // 확인
            try
            {
                var ok = LeaderboardsService.Instance;
                Debug.Log($"[LeaderboardManager] leaderboards 보정 초기화 {(ok != null ? "성공" : "실패")}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LeaderboardManager] 보정 후에도 Instance 접근 실패: {ex.Message}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LeaderboardManager] leaderboards 보정 중 예외: {e.Message}");
        }
    }

    /// <summary>클리어 타임(초) 제출 — fire-and-forget. UI 흐름을 막지 않음.</summary>
    public void SubmitClearTime(double seconds)
    {
        _ = SubmitClearTimeAsync(seconds);
    }

    /// <summary>클리어 타임(초) 제출. 낮을수록 상위(리더보드 Ascending 가정).</summary>
    public async Task SubmitClearTimeAsync(double seconds)
    {
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
    }

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
}
