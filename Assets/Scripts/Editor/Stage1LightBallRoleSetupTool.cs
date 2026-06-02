#if UNITY_EDITOR
using System.Text;
using Stage1;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stage1 의 LightBall(Red/Blue/Yellow)에 사이드별 권한/시각 규칙을 일괄 부착하는 셋업 툴.
///
/// 적용 규칙:
///   · LocalSideColorMask (maskForSide = P1)  → P1(Host)에게만 공 색을 회색으로 가림 (형태는 보임)
///   · LocalSideGrabLock  (lockForSide  = P2)  → P2(Guest)는 공을 잡아 옮길 수 없음
///   · Spectator(P3)는 두 규칙 모두 해당 없음 → 색도 보이고 동작도 그대로
///
/// 대상은 씬에서 LightBallColorTag 를 가진 모든 GameObject (= Red/Blue/Yellow).
/// 메뉴: Tools/Stage1/LightBall Roles/
/// </summary>
public static class Stage1LightBallRoleSetupTool
{
    const string Root = "Tools/Stage1/LightBall Roles/";

    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRun()
    {
        var balls = Object.FindObjectsByType<LightBallColorTag>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sb = new StringBuilder();
        sb.AppendLine("[LightBall-Roles] Dry-Run — 변경 없음. 'Apply' 시 처리될 대상:\n");
        foreach (var b in balls)
        {
            var go = b.gameObject;
            bool hasMask = go.GetComponent<LocalSideColorMask>() != null;
            bool hasLock = go.GetComponent<LocalSideGrabLock>() != null;
            bool hasGrab = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>() != null;
            sb.AppendLine($"  [{b.color}] {Path(go)}" +
                          $"  (ColorMask:{(hasMask ? "있음" : "추가")}, GrabLock:{(hasLock ? "있음" : "추가")}, " +
                          $"XRGrab:{(hasGrab ? "OK" : "없음!")})");
        }
        sb.AppendLine($"\nLightBall {balls.Length}개");
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "2) Apply to Scene")]
    public static void Apply()
    {
        if (!EditorUtility.DisplayDialog("LightBall Roles Apply",
            "LightBall(Red/Blue/Yellow)에 사이드 규칙을 적용합니다.\n" +
            "· P1: 색 숨김(LocalSideColorMask)\n" +
            "· P2: 잡기 차단(LocalSideGrabLock)\n\n" +
            "Undo 가능. 적용 후 씬 저장 필요. 계속할까요?",
            "Apply", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("LightBall Role Setup Apply");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder();
        sb.AppendLine("[LightBall-Roles] Apply 결과:");

        int count = 0;
        foreach (var b in Object.FindObjectsByType<LightBallColorTag>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var go = b.gameObject;

            // 1) 색 숨김 (P1)
            var mask = go.GetComponent<LocalSideColorMask>();
            if (mask == null) mask = Undo.AddComponent<LocalSideColorMask>(go);
            // (대상 Renderer/Light 는 비워두면 런타임 Awake 에서 자식 포함 자동 수집)
            EditorUtility.SetDirty(mask);

            // 2) 잡기 차단 (P2) — XRGrabInteractable 이 있어야 필터가 동작.
            if (go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>() == null)
            {
                sb.AppendLine($"  ! [{b.color}] XRGrabInteractable 없음 — GrabLock 건너뜀: {Path(go)}");
            }
            else
            {
                var lk = go.GetComponent<LocalSideGrabLock>();
                if (lk == null) lk = Undo.AddComponent<LocalSideGrabLock>(go);
                EditorUtility.SetDirty(lk);
            }

            sb.AppendLine($"  · [{b.color}] 적용: {Path(go)}");
            count++;
        }

        sb.AppendLine($"\n총 {count}개 LightBall 처리 완료. 반드시 씬을 저장하세요(Ctrl+S).");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "3) Remove from Scene")]
    public static void Remove()
    {
        if (!EditorUtility.DisplayDialog("LightBall Roles Remove",
            "LightBall 에서 LocalSideColorMask / LocalSideGrabLock 을 제거합니다.\nUndo 가능. 계속할까요?",
            "Remove", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("LightBall Role Setup Remove");
        int group = Undo.GetCurrentGroup();

        int removed = 0;
        foreach (var b in Object.FindObjectsByType<LightBallColorTag>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var go = b.gameObject;
            var mask = go.GetComponent<LocalSideColorMask>();
            if (mask != null) { Undo.DestroyObjectImmediate(mask); removed++; }
            var lk = go.GetComponent<LocalSideGrabLock>();
            if (lk != null) { Undo.DestroyObjectImmediate(lk); removed++; }
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[LightBall-Roles] 제거 완료 — 컴포넌트 {removed}개. 씬 저장 필요.");
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
