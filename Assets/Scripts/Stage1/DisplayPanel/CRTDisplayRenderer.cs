using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// Canvas 안의 TMP 텍스트 하나로 CRT 스타일 디스플레이를 렌더링.
/// MainControlSystem의 상태를 읽기만 하고 건드리지 않는다.
/// </summary>
public class CRTDisplayRenderer : MonoBehaviour
{
    [Header("연결")]
    public TextMeshProUGUI crtText;
    public MainControlSystem mainControl;

    // ── 색상 (TMP Rich Text hex) ──────────────────
    const string C_AMBER     = "#E8A000";
    const string C_DIM       = "#5A3C00";
    const string C_MID       = "#9A6200";
    const string C_RED       = "#FF2800";
    const string C_RED_DIM   = "#6A1000";
    const string C_GREEN     = "#00E87A";
    const string C_GREEN_DIM = "#004D29";

    // ── 깜빡임 ────────────────────────────────────
    float _blinkTimer;
    bool  _blink;
    const float BLINK_RATE = 0.5f;

    // ── 레이아웃 상수 ─────────────────────────────
    const int W         = 56;   // 전체 가로 문자 수
    const int VERT_ROWS = 7;    // 수직 게이지 행 수
    const int LEFT_W    = 16;   // 수직 게이지 패널 가로

    void Update()
    {
        _blinkTimer += Time.deltaTime;
        if (_blinkTimer >= BLINK_RATE) { _blinkTimer -= BLINK_RATE; _blink = !_blink; }

        // [Networked] 프로퍼티(CurrentState/Stability)는 Spawned() 이후에만 읽을 수 있다.
        // NetworkObject가 아직 스폰되지 않았으면 빌드하지 않는다.
        if (crtText == null || mainControl == null) return;
        var no = mainControl.Object;
        if (no == null || !no.IsValid) return;

        crtText.text = Build();
    }

    // ─────────────────────────────────────────────
    //  전체 화면 조립
    // ─────────────────────────────────────────────
    string Build()
    {
        var state  = mainControl.CurrentState;
        float stab = mainControl.Stability;
        int   pct  = Mathf.RoundToInt(stab);
        bool  bat  = mainControl.snappedBattery != null;

        bool isAlert = state == MainControlSystem.SystemState.BatteryLow
                    || state == MainControlSystem.SystemState.PowerOff;
        bool isGood  = state == MainControlSystem.SystemState.Idle && pct >= 100;

        string cA = isAlert ? C_RED     : isGood ? C_GREEN     : C_AMBER;
        string cD = isAlert ? C_RED_DIM : isGood ? C_GREEN_DIM : C_DIM;
        string cM = isAlert ? C_RED     : isGood ? C_GREEN     : C_MID;

        var sb = new StringBuilder();

        // ── 헤더 ─────────────────────────────────
        sb.AppendLine(BuildHeader(pct, state, cA, cD, cM));
        sb.AppendLine(Col(Rep('-', W), cD));

        // ── 중앙: 수직게이지(좌) + 상태리포트(우) ─
        var left  = BuildVertGauge(pct, cA, cD);
        var right = BuildStatusPanel(state, cA, cD, cM);
        int rows  = Mathf.Max(left.Length, right.Length);
        for (int i = 0; i < rows; i++)
        {
            string l = i < left.Length  ? left[i]  : Rep(' ', LEFT_W + 2);
            string r = i < right.Length ? right[i] : "";
            int rawL = StripTags(l).Length;
            sb.AppendLine(l + Rep(' ', Mathf.Max(1, 18 - rawL)) + r);
        }

        // ── 진행 바 ───────────────────────────────
        sb.AppendLine(Col(Rep('-', W), cD));
        sb.AppendLine(BuildProgressBar(pct, cA, cD, cM));

        // ── 배터리 슬롯 + 로그 ────────────────────
        sb.AppendLine(Col(Rep('-', W), cD));
        string batStr = bat
            ? Col("BATTERY[", cD) + Col("INSTALLED", cA) + Col("]", cD)
            : Col("BATTERY[", cD) + Col(" EMPTY   ", isAlert ? C_RED : cD) + Col("]", cD);
        sb.AppendLine(batStr + "  " + Col("LOG:", cD) + " " + Col(GetLog(state), cM));

        // ── 알림 박스 ─────────────────────────────
        sb.AppendLine(Col(Rep('-', W), cD));
        sb.Append(BuildAlertBox(state, cA, cD));

        return sb.ToString();
    }

    // ─────────────────────────────────────────────
    //  섹션 빌더
    // ─────────────────────────────────────────────

    string BuildHeader(int pct, MainControlSystem.SystemState state,
                       string cA, string cD, string cM)
    {
        int gaugeW  = 18;
        int filled  = Mathf.RoundToInt(pct / 100f * gaugeW);
        string gauge = Col(Rep('|', filled), cA) + Col(Rep('.', gaugeW - filled), cD);
        string label = PadR(GetSysLabel(state), 20);
        return Col(LPad(pct + "%", 4), cA) + " "
             + Col("[", cD) + gauge + Col("]", cD)
             + "  " + Col(label, cM);
    }

    string[] BuildVertGauge(int pct, string cA, string cD)
    {
        int filled = Mathf.RoundToInt(pct / 100f * VERT_ROWS);
        var lines  = new string[VERT_ROWS + 2];
        lines[0]   = Col("+" + Rep('=', LEFT_W) + "+", cD);
        for (int i = 0; i < VERT_ROWS; i++)
        {
            bool on      = (VERT_ROWS - 1 - i) < filled;
            string inner = on
                ? Col(Rep('|', LEFT_W), cA)
                : Col(Rep('.', LEFT_W), cD);
            lines[i + 1] = Col("|", cD) + inner + Col("|", cD);
        }
        lines[VERT_ROWS + 1] = Col("+" + Rep('=', LEFT_W) + "+", cD);
        return lines;
    }

    string[] BuildStatusPanel(MainControlSystem.SystemState state,
                              string cA, string cD, string cM)
    {
        GetStatusLines(state, out string s1, out string s2, out string s3);
        const int PW = 24;
        return new[]
        {
            Col("+" + Rep('-', PW) + "+", cD),
            Col("| ", cD) + Col(PadR("STATUS REPORT", PW - 2), cD) + Col(" |", cD),
            Col("|" + Rep(' ', PW) + "|", cD),
            Col("| ", cD) + Col(PadR(s1, PW - 2), cM) + Col(" |", cD),
            Col("| ", cD) + Col(PadR(s2, PW - 2), cM) + Col(" |", cD),
            Col("| ", cD) + Col(PadR(s3, PW - 2), cM) + Col(" |", cD),
            Col("|" + Rep(' ', PW) + "|", cD),
            Col("+" + Rep('-', PW) + "+", cD),
        };
    }

    string BuildProgressBar(int pct, string cA, string cD, string cM)
    {
        const string label = "STABILITY ";
        int barW   = W - label.Length - 7;
        int filled = Mathf.RoundToInt(pct / 100f * barW);
        string bar = Rep('#', filled) + Rep('-', barW - filled);
        return Col(label, cM)
             + Col("[", cD) + Col(bar, cA) + Col("]", cD)
             + " " + Col(LPad(pct + "%", 4), cA);
    }

    string BuildAlertBox(MainControlSystem.SystemState state, string cA, string cD)
    {
        string msg = GetAlertMsg(state);
        int boxW   = msg.Length + 2;
        return Col("+" + Rep('-', boxW) + "+", cD) + "\n"
             + Col("| ", cD) + Col(msg, cA) + Col(" |", cD) + "\n"
             + Col("+" + Rep('-', boxW) + "+", cD);
    }

    // ─────────────────────────────────────────────
    //  상태별 텍스트
    // ─────────────────────────────────────────────

    string GetSysLabel(MainControlSystem.SystemState s) => s switch
    {
        MainControlSystem.SystemState.Stabilizing => "STABILIZING...",
        MainControlSystem.SystemState.BatteryLow  => "BATTERY CRITICAL",
        MainControlSystem.SystemState.PowerOff    => "POWER OFFLINE",
        MainControlSystem.SystemState.Rebooting   => "REBOOTING...",
        _                                          => "MAINFRAME-84",
    };

    void GetStatusLines(MainControlSystem.SystemState s,
                        out string s1, out string s2, out string s3)
    {
        switch (s)
        {
            case MainControlSystem.SystemState.Stabilizing:
                s1 = "ENERGY   : FLUCTUATING";
                s2 = "PRESSURE : ELEVATED";
                s3 = "POWER    : CHARGING";
                break;
            case MainControlSystem.SystemState.BatteryLow:
                s1 = "ENERGY   : CRITICAL";
                s2 = "PRESSURE : DANGER";
                s3 = "POWER    : FAILING";
                break;
            case MainControlSystem.SystemState.PowerOff:
                s1 = "ENERGY   : OFFLINE";
                s2 = "PRESSURE : UNKNOWN";
                s3 = "POWER    : OFFLINE";
                break;
            case MainControlSystem.SystemState.Rebooting:
                s1 = "ENERGY   : RESTORING";
                s2 = "PRESSURE : NOMINAL";
                s3 = "POWER    : REBOOTING";
                break;
            default:
                s1 = "ENERGY   : STABLE";
                s2 = "PRESSURE : NOMINAL";
                s3 = "POWER    : ONLINE";
                break;
        }
    }

    string GetLog(MainControlSystem.SystemState s) => s switch
    {
        MainControlSystem.SystemState.Stabilizing => "SEQUENCE RUNNING",
        MainControlSystem.SystemState.BatteryLow  => _blink ? "CRITICAL ALERT  " : "                ",
        MainControlSystem.SystemState.PowerOff    => _blink ? "AWAITING INPUT  " : "                ",
        MainControlSystem.SystemState.Rebooting   => "RESTARTING SYS  ",
        _                                          => "AWAITING COMMAND",
    };

    string GetAlertMsg(MainControlSystem.SystemState s) => s switch
    {
        MainControlSystem.SystemState.Idle        => "PRESS STABILIZE TO BEGIN        ",
        MainControlSystem.SystemState.Stabilizing => _blink ? "SEQUENCE RUNNING..." : "SEQUENCE RUNNING   ",
        MainControlSystem.SystemState.BatteryLow  => _blink ? "!! BATTERY CRITICAL - POWER FAILURE IMMINENT !!" : Rep(' ', 48),
        MainControlSystem.SystemState.PowerOff    => _blink ? ">>> INSERT BATTERY TO REBOOT <<<" : Rep(' ', 32),
        MainControlSystem.SystemState.Rebooting   => _blink ? "SYSTEM RESTART IN PROGRESS..." : "SYSTEM RESTART IN PROGRESS   ",
        _                                          => "ALL SYSTEMS NOMINAL - STAGE 2 READY",
    };

    // ─────────────────────────────────────────────
    //  유틸
    // ─────────────────────────────────────────────

    static string Col(string s, string hex) => $"<color={hex}>{s}</color>";

    static string Rep(char c, int n) => n <= 0 ? "" : new string(c, n);

    static string PadR(string s, int n)
        => s.Length >= n ? s.Substring(0, n) : s + new string(' ', n - s.Length);

    static string LPad(string s, int n)
        => s.Length >= n ? s.Substring(s.Length - n) : new string(' ', n - s.Length) + s;

    static string StripTags(string s)
    {
        var sb = new StringBuilder();
        bool inTag = false;
        foreach (char c in s)
        {
            if (c == '<') { inTag = true;  continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }
}
