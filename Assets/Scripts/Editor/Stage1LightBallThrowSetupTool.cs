#if UNITY_EDITOR
using System.Text;
using Stage1;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Stage1 의 LightBall(Red/Blue/Yellow)을 "던질 수 있게" 만드는 셋업 툴.
///
/// 현재 LightBall 은 Rigidbody(중력 ON)·XRGrabInteractable(throwOnDetach ON)을 이미 갖고 있지만,
/// NetworkGrabbableSync.forceNoThrowOnDetach = true 가 런타임에 throwOnDetach 를 강제 OFF 해서
/// 놓으면 던짐 속도 없이 그 자리에서 떨어진다.
///
/// 각 LightBall 에 적용:
///   · NetworkGrabbableSync.forceNoThrowOnDetach = false   (throw 속도 유지)
///   · XRGrabInteractable.throwOnDetach = true, forceGravityOnDetach = true
///
/// 대상: Stage1.LightBallColorTag 가 붙은 모든 오브젝트(= Red/Blue/Yellow 라이트볼).
/// 메뉴: Tools/Stage1/LightBall Throw/
/// 안전: Undo 가능. 적용 후 씬 저장 필요(Ctrl+S).
/// </summary>
public static class Stage1LightBallThrowSetupTool
{
    const string Root = "Tools/Stage1/LightBall Throw/";

    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRun()
    {
        var balls = Object.FindObjectsByType<LightBallColorTag>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sb = new StringBuilder($"[LightBall-Throw] Dry-Run — 변경 없음. LightBall {balls.Length}개:\n");
        foreach (var b in balls)
        {
            var grab = b.GetComponent<XRGrabInteractable>();
            var net = b.GetComponent<NetworkGrabbableSync>();
            sb.AppendLine($"  · {Path(b.gameObject)}");
            sb.AppendLine($"       Grab: throwOnDetach={(grab ? grab.throwOnDetach.ToString() : "없음")}, forceGravityOnDetach={(grab ? grab.forceGravityOnDetach.ToString() : "없음")}");
            sb.AppendLine($"       NetSync.forceNoThrowOnDetach={(net ? net.forceNoThrowOnDetach.ToString() : "없음")}");
        }
        if (balls.Length == 0)
            sb.AppendLine("  (LightBallColorTag 를 못 찾음 — Main Scene 이 열려 있는지 확인하세요.)");
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "2) Apply to Scene")]
    public static void Apply()
    {
        var balls = Object.FindObjectsByType<LightBallColorTag>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (balls.Length == 0)
        {
            EditorUtility.DisplayDialog("LightBall Throw",
                "LightBall(LightBallColorTag)을 찾지 못했습니다. Main Scene 이 열려 있는지 확인하세요.", "확인");
            return;
        }

        if (!EditorUtility.DisplayDialog("LightBall Throw Apply",
            $"LightBall {balls.Length}개를 던질 수 있게 설정합니다.\n" +
            "(forceNoThrowOnDetach OFF + throwOnDetach/forceGravityOnDetach ON)\n" +
            "Undo 가능. 적용 후 씬 저장 필요. 계속할까요?",
            "Apply", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("LightBall Throw Apply");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder("[LightBall-Throw] Apply 결과:\n");
        int count = 0;
        foreach (var b in balls)
        {
            var grab = b.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                Undo.RecordObject(grab, "LightBall Grab");
                grab.throwOnDetach = true;
                grab.forceGravityOnDetach = true;
                EditorUtility.SetDirty(grab);
            }
            var net = b.GetComponent<NetworkGrabbableSync>();
            if (net != null)
            {
                Undo.RecordObject(net, "LightBall NetSync");
                net.forceNoThrowOnDetach = false;
                EditorUtility.SetDirty(net);
            }
            sb.AppendLine($"  · {Path(b.gameObject)}");
            count++;
        }
        sb.AppendLine($"\nLightBall {count}개에 던지기 설정 완료. 반드시 씬을 저장하세요(Ctrl+S).");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "3) Revert (던지기 해제)")]
    public static void Revert()
    {
        var balls = Object.FindObjectsByType<LightBallColorTag>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (balls.Length == 0) return;

        if (!EditorUtility.DisplayDialog("LightBall Throw Revert",
            "LightBall 을 다시 던지기 OFF(놓으면 그 자리 낙하)로 되돌립니다.\nUndo 가능. 계속할까요?",
            "Revert", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("LightBall Throw Revert");
        int group = Undo.GetCurrentGroup();

        int count = 0;
        foreach (var b in balls)
        {
            var grab = b.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                Undo.RecordObject(grab, "LightBall Grab");
                grab.forceGravityOnDetach = false;
                EditorUtility.SetDirty(grab);
            }
            var net = b.GetComponent<NetworkGrabbableSync>();
            if (net != null)
            {
                Undo.RecordObject(net, "LightBall NetSync");
                net.forceNoThrowOnDetach = true;
                EditorUtility.SetDirty(net);
            }
            count++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[LightBall-Throw] Revert 완료 — {count}개. 씬 저장 필요.");
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
