using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using Unity.Services.Leaderboards.Models;

/// <summary>
/// 리더보드 UI 표시 컴포넌트.
/// - 상위 N명(기본 8명)의 클리어 타임을 rankRows 텍스트에 표시
/// - 그 아래 myScoreText에 내 클리어 타임 + 내 순위 표시
/// - 내 순위가 상위 N 안에 들면 myScoreText를 강조색(노란색)으로 표시
///
/// rankRows: 1~N위를 표시할 TMP_Text를 순서대로 할당 (월드 TextMeshPro / 캔버스 TextMeshProUGUI 둘 다 가능).
/// Refresh()를 버튼이나 화면 진입 시 호출하면 갱신됨.
/// </summary>
public class LeaderboardDisplay : MonoBehaviour
{
    [Header("표시 대상")]
    [Tooltip("1위부터 순서대로 채울 행 텍스트 (개수 = 표시할 등수, 기본 8개)")]
    public TMP_Text[] rankRows = new TMP_Text[8];

    [Tooltip("내 클리어 타임을 표시할 텍스트")]
    public TMP_Text myScoreText;

    [Header("색상")]
    [Tooltip("내가 상위권(rankRows 개수 이내)에 들었을 때 내 기록 텍스트 색")]
    public Color highlightColor = Color.yellow;
    [Tooltip("상위권 밖일 때 내 기록 텍스트 색")]
    public Color normalColor = Color.white;

    [Header("동작")]
    [Tooltip("활성화될 때 자동으로 새로고침")]
    public bool refreshOnEnable = true;

    void OnEnable()
    {
        if (refreshOnEnable) Refresh();
    }

    /// <summary>리더보드를 다시 불러와 UI 갱신. 버튼 onClick 등에 연결 가능.</summary>
    public void Refresh()
    {
        _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        var mgr = LeaderboardManager.Instance;
        if (mgr == null)
        {
            Debug.LogWarning("[LeaderboardDisplay] LeaderboardManager.Instance 없음.");
            return;
        }

        int topCount = rankRows != null ? rankRows.Length : 0;

        // 상위 N명
        List<LeaderboardEntry> top = await mgr.GetTopScoresAsync(topCount);
        for (int i = 0; i < topCount; i++)
        {
            if (rankRows[i] == null) continue;

            if (top != null && i < top.Count)
            {
                var e = top[i];
                rankRows[i].text = $"{e.Rank + 1,2}.  {DisplayName(e.PlayerName)}   {FormatTime(e.Score)}";
            }
            else
            {
                rankRows[i].text = $"{i + 1,2}.  -";
            }
        }

        // 내 기록
        LeaderboardEntry me = await mgr.GetMyScoreAsync();
        if (myScoreText != null)
        {
            if (me != null)
            {
                myScoreText.text = $"내 기록   {FormatTime(me.Score)}   ({me.Rank + 1}위)";
                // Rank는 0-기반 → 상위 topCount 안에 들면 (Rank < topCount) 강조
                myScoreText.color = (me.Rank < topCount) ? highlightColor : normalColor;
            }
            else
            {
                myScoreText.text = "내 기록   없음";
                myScoreText.color = normalColor;
            }
        }
    }

    static string DisplayName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "익명";
        // UGS 익명 계정은 "이름#1234" 형태 → 태그(#뒤) 제거해 보기 좋게
        int hash = raw.IndexOf('#');
        return hash > 0 ? raw.Substring(0, hash) : raw;
    }

    /// <summary>초(double) → "mm:ss.cc" 포맷. Stage1Timer 표기와 동일.</summary>
    static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        int total = (int)seconds;
        int minutes = total / 60;
        int secs = total % 60;
        int hundredths = (int)((seconds - total) * 100.0);
        return $"{minutes:00}:{secs:00}.{hundredths:00}";
    }
}
