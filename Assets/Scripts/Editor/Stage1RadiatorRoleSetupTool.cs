#if UNITY_EDITOR
using System.Text;
using PipePuz.SmokePuzzle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stage1 의 PipeSmokePuz/Radiator 에 사이드별 권한/시각 규칙을 일괄 부착하는 셋업 툴.
///
/// 적용 규칙:
///   · SmokeGauge → LocalSideHide (hideForSide = P1)  → P1(Host)에게만 게이지가 안 보임
///                  (Renderer 만 끄므로 PointerInRedZone 계산 등 로직은 유지 → 연기 억제 정상)
///   · Valve(SuppressionWheel) → LocalSideGrabLock (lockForSide = P2)  → P2(Guest)는 밸브를 못 잡음
///   · Spectator(P3)는 두 규칙 모두 해당 없음 → 게이지도 보이고 밸브도 평소대로
///
/// 결과 비대칭: P1 은 밸브를 돌리지만 게이지를 못 보고, P2 는 게이지를 보지만 밸브를 못 돌린다 → 협동 필수.
/// 메뉴: Tools/Stage1/Radiator Roles/
/// </summary>
public static class Stage1RadiatorRoleSetupTool
{
    const string Root = "Tools/Stage1/Radiator Roles/";

    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRun()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Radiator-Roles] Dry-Run — 변경 없음. 'Apply' 시 처리될 대상:\n");

        foreach (var g in Object.FindObjectsByType<SmokeGauge>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool has = g.GetComponent<LocalSideHide>() != null;
            int rends = g.GetComponentsInChildren<Renderer>(true).Length;
            sb.AppendLine($"  [SmokeGauge] {Path(g.gameObject)}  (LocalSideHide:{(has ? "있음" : "추가")}, Renderer {rends}개 숨김)");
        }
        foreach (var v in Object.FindObjectsByType<SuppressionWheel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool has = v.GetComponent<LocalSideGrabLock>() != null;
            sb.AppendLine($"  [Valve] {Path(v.gameObject)}  (LocalSideGrabLock:{(has ? "있음" : "추가")})");
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "2) Apply to Scene")]
    public static void Apply()
    {
        if (!EditorUtility.DisplayDialog("Radiator Roles Apply",
            "PipeSmokePuz/Radiator 에 사이드 규칙을 적용합니다.\n" +
            "· P1: SmokeGauge 숨김(LocalSideHide)\n" +
            "· P2: Valve 잡기 차단(LocalSideGrabLock)\n\n" +
            "Undo 가능. 적용 후 씬 저장 필요. 계속할까요?",
            "Apply", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Radiator Role Setup Apply");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder();
        sb.AppendLine("[Radiator-Roles] Apply 결과:");

        int gauges = 0;
        foreach (var g in Object.FindObjectsByType<SmokeGauge>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g.GetComponent<LocalSideHide>() == null)
                Undo.AddComponent<LocalSideHide>(g.gameObject); // 기본 hideForSide = P1
            EditorUtility.SetDirty(g);
            sb.AppendLine($"  · [SmokeGauge=P1 숨김] {Path(g.gameObject)}");
            gauges++;
        }

        int valves = 0;
        foreach (var v in Object.FindObjectsByType<SuppressionWheel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (v.GetComponent<LocalSideGrabLock>() == null)
                Undo.AddComponent<LocalSideGrabLock>(v.gameObject); // 기본 lockForSide = P2
            EditorUtility.SetDirty(v);
            sb.AppendLine($"  · [Valve=P2 잡기차단] {Path(v.gameObject)}");
            valves++;
        }

        sb.AppendLine($"\nSmokeGauge {gauges}개, Valve {valves}개 처리 완료. 반드시 씬을 저장하세요(Ctrl+S).");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "3) Remove from Scene")]
    public static void Remove()
    {
        if (!EditorUtility.DisplayDialog("Radiator Roles Remove",
            "SmokeGauge 의 LocalSideHide / Valve 의 LocalSideGrabLock 을 제거합니다.\nUndo 가능. 계속할까요?",
            "Remove", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Radiator Role Setup Remove");
        int group = Undo.GetCurrentGroup();

        int removed = 0;
        foreach (var g in Object.FindObjectsByType<SmokeGauge>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var c = g.GetComponent<LocalSideHide>();
            if (c != null) { Undo.DestroyObjectImmediate(c); removed++; }
        }
        foreach (var v in Object.FindObjectsByType<SuppressionWheel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var c = v.GetComponent<LocalSideGrabLock>();
            if (c != null) { Undo.DestroyObjectImmediate(c); removed++; }
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Radiator-Roles] 제거 완료 — 컴포넌트 {removed}개. 씬 저장 필요.");
    }

    static string Path(GameObject go)
    {
        var sb = new StringBuilder(go.name);
        var t = go.transform.parent;
        while (t != null) { sb.Insert(0, t.name + "/"); t = t.parent; }
        return sb.ToString();
    }
}
#endif
