#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// "Stage 1 Pipe parts" 컨테이너 하위의 모든 잡을 수 있는 파츠(XRBaseInteractable)에
/// LocalSideGrabLock(lockForSide = P2) 을 일괄 부착하는 셋업 툴.
///
/// 결과: P2(Guest)는 파이프 파츠를 잡을 수 없고, P1(Host)·Spectator(P3)는 평소대로 잡을 수 있다.
/// 효과는 로컬 전용(각 디바이스가 자기 LocalPlayerSide 로 판정).
///
/// 컨테이너는 이름으로 찾는다("Stage 1 Pipe parts" / "Stage1PipeParts" 등 변형 허용).
/// 메뉴: Tools/Stage1/PipeParts Roles/
/// </summary>
public static class Stage1PipePartsRoleSetupTool
{
    const string Root = "Tools/Stage1/PipeParts Roles/";

    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRun()
    {
        var container = FindContainer();
        var sb = new StringBuilder();
        if (container == null)
        {
            Debug.LogWarning("[PipeParts-Roles] 'Stage 1 Pipe parts' 컨테이너를 못 찾음. 씬이 열려 있는지/이름을 확인하세요.");
            return;
        }

        var parts = container.GetComponentsInChildren<XRBaseInteractable>(true);
        sb.AppendLine($"[PipeParts-Roles] Dry-Run — 변경 없음. 컨테이너: {Path(container)}");
        sb.AppendLine($"잡을 수 있는 파츠(XRBaseInteractable) {parts.Length}개:\n");
        foreach (var p in parts)
        {
            bool has = p.GetComponent<LocalSideGrabLock>() != null;
            sb.AppendLine($"  [{p.GetType().Name}] {Path(p.gameObject)}  (GrabLock:{(has ? "있음" : "추가")})");
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "2) Apply to Scene")]
    public static void Apply()
    {
        var container = FindContainer();
        if (container == null)
        {
            EditorUtility.DisplayDialog("PipeParts Roles",
                "'Stage 1 Pipe parts' 컨테이너를 찾지 못했습니다.\n씬이 열려 있는지/이름을 확인하세요.", "확인");
            return;
        }

        if (!EditorUtility.DisplayDialog("PipeParts Roles Apply",
            $"'{container.name}' 하위 파츠에 P2 잡기 차단(LocalSideGrabLock)을 적용합니다.\n" +
            "Undo 가능. 적용 후 씬 저장 필요. 계속할까요?",
            "Apply", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("PipeParts Role Setup Apply");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder();
        sb.AppendLine($"[PipeParts-Roles] Apply 결과 (컨테이너 {Path(container)}):");

        int count = 0;
        foreach (var p in container.GetComponentsInChildren<XRBaseInteractable>(true))
        {
            if (p.GetComponent<LocalSideGrabLock>() == null)
                Undo.AddComponent<LocalSideGrabLock>(p.gameObject); // 기본 lockForSide = P2
            EditorUtility.SetDirty(p);
            sb.AppendLine($"  · [{p.GetType().Name}] {Path(p.gameObject)}");
            count++;
        }

        sb.AppendLine($"\n파츠 {count}개에 P2 잡기 차단 적용 완료. 반드시 씬을 저장하세요(Ctrl+S).");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "3) Remove from Scene")]
    public static void Remove()
    {
        var container = FindContainer();
        if (container == null) return;

        if (!EditorUtility.DisplayDialog("PipeParts Roles Remove",
            "파츠에서 LocalSideGrabLock 을 제거합니다.\nUndo 가능. 계속할까요?", "Remove", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("PipeParts Role Setup Remove");
        int group = Undo.GetCurrentGroup();

        int removed = 0;
        foreach (var p in container.GetComponentsInChildren<XRBaseInteractable>(true))
        {
            var c = p.GetComponent<LocalSideGrabLock>();
            if (c != null) { Undo.DestroyObjectImmediate(c); removed++; }
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[PipeParts-Roles] 제거 완료 — 컴포넌트 {removed}개. 씬 저장 필요.");
    }

    // 이름으로 컨테이너 검색 (공백/대소문자 변형 허용).
    static GameObject FindContainer()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var compact = t.name.Replace(" ", "").ToLowerInvariant();
            if (compact == "stage1pipeparts" || compact.Contains("pipeparts"))
                return t.gameObject;
        }
        return null;
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
