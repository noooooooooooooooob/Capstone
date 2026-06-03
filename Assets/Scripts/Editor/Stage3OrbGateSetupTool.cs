#if UNITY_EDITOR
using System.Text;
using PipePuz.LightBeam;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 씬의 모든 <see cref="LightOrb"/> 에 <see cref="Stage3OrbGate"/> 를 부착해서
/// "Stage2 클리어(= Stage3 시작) 전까지 orb 숨김 → 클리어 시 공중에 떠 있는 상태로 공개"
/// 동작을 일괄 적용하는 셋업 툴.
///
/// 메뉴: Tools/Stage3/LightOrb Gate/
/// 안전: Undo 가능. 적용 후 씬 저장 필요(Ctrl+S).
/// </summary>
public static class Stage3OrbGateSetupTool
{
    const string Root = "Tools/Stage3/LightOrb Gate/";
    const int RevealAtPuzzleIndex = 2; // LightBeam = Stage3 = Stage2 클리어 직후

    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRun()
    {
        var orbs = Object.FindObjectsByType<LightOrb>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sb = new StringBuilder();
        sb.AppendLine($"[Stage3-OrbGate] Dry-Run — 변경 없음. LightOrb {orbs.Length}개:\n");
        foreach (var o in orbs)
        {
            bool has = o.GetComponent<Stage3OrbGate>() != null;
            sb.AppendLine($"  · {Path(o.gameObject)}  (Stage3OrbGate:{(has ? "있음" : "추가")})");
        }
        if (orbs.Length == 0)
            sb.AppendLine("  (LightOrb 를 못 찾음 — Main Scene 이 열려 있는지 확인하세요.)");
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "2) Apply to Scene")]
    public static void Apply()
    {
        var orbs = Object.FindObjectsByType<LightOrb>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (orbs.Length == 0)
        {
            EditorUtility.DisplayDialog("Stage3 OrbGate",
                "LightOrb 를 찾지 못했습니다. Main Scene 이 열려 있는지 확인하세요.", "확인");
            return;
        }

        if (!EditorUtility.DisplayDialog("Stage3 OrbGate Apply",
            $"LightOrb {orbs.Length}개에 Stage3OrbGate(RevealAtPuzzleIndex={RevealAtPuzzleIndex})를 적용합니다.\n" +
            "Stage2 클리어 전까지 숨김 → 클리어 시 공중에 떠 있는 상태로 공개.\n" +
            "Undo 가능. 적용 후 씬 저장 필요. 계속할까요?",
            "Apply", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Stage3 OrbGate Apply");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder("[Stage3-OrbGate] Apply 결과:\n");
        int count = 0;
        foreach (var o in orbs)
        {
            var gate = o.GetComponent<Stage3OrbGate>();
            if (gate == null) gate = Undo.AddComponent<Stage3OrbGate>(o.gameObject);
            Undo.RecordObject(gate, "Stage3 OrbGate Config");
            gate.RevealAtPuzzleIndex = RevealAtPuzzleIndex;
            gate.FloatAfterReveal = true;
            EditorUtility.SetDirty(gate);
            sb.AppendLine($"  · {Path(o.gameObject)}");
            count++;
        }
        sb.AppendLine($"\nLightOrb {count}개에 Stage3OrbGate 적용 완료. 반드시 씬을 저장하세요(Ctrl+S).");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "3) Remove from Scene")]
    public static void Remove()
    {
        var orbs = Object.FindObjectsByType<LightOrb>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (orbs.Length == 0) return;

        if (!EditorUtility.DisplayDialog("Stage3 OrbGate Remove",
            "LightOrb 에서 Stage3OrbGate 를 제거합니다.\nUndo 가능. 계속할까요?", "Remove", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Stage3 OrbGate Remove");
        int group = Undo.GetCurrentGroup();

        int removed = 0;
        foreach (var o in orbs)
        {
            var gate = o.GetComponent<Stage3OrbGate>();
            if (gate != null) { Undo.DestroyObjectImmediate(gate); removed++; }
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Stage3-OrbGate] 제거 완료 — 컴포넌트 {removed}개. 씬 저장 필요.");
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
