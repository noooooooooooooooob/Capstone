#if UNITY_EDITOR
using System.Text;
using PipePuz.MiniGame2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// "Stage 1 Pipe parts" 컨테이너 하위의 잡을 수 있는 파이프 파츠를
/// "중력에 영향을 받고 던질 수도 있게" 만드는 셋업 툴.
///
/// 각 파츠에 적용:
///   · Rigidbody          : useGravity = true, isKinematic = false  (중력 낙하)
///   · XRGrabInteractable : throwOnDetach = true, forceGravityOnDetach = true  (던지기)
///   · NetworkGrabbableSync.forceNoThrowOnDetach = false  (런타임에 throwOnDetach 를
///                          강제 OFF 하지 않도록 — 안 끄면 던짐 속도가 0 으로 죽음)
///
/// 슬롯에 꽂히면 PipeMiniGame2Slot.AcceptPipe 가 isKinematic=true 로 고정하므로,
/// 슬롯 안에서는 떠 있고 슬롯 밖(던졌을 때)에서는 중력으로 떨어진다.
/// PipeMiniGame2Pipe.IsFixed(Source/Sink 고정 파이프)는 건너뛴다.
///
/// 메뉴: Tools/Stage1/PipeParts Physics/
/// 안전: Undo 가능. 적용 후 씬 저장 필요(Ctrl+S).
/// </summary>
public static class Stage1PipePartsPhysicsSetupTool
{
    const string Root = "Tools/Stage1/PipeParts Physics/";

    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRun()
    {
        var container = FindContainer();
        if (container == null)
        {
            Debug.LogWarning("[PipeParts-Physics] 'Stage 1 Pipe parts' 컨테이너를 못 찾음. 씬/이름 확인.");
            return;
        }

        var sb = new StringBuilder($"[PipeParts-Physics] Dry-Run — 변경 없음. 컨테이너: {Path(container)}\n");
        int n = 0;
        foreach (var pipe in container.GetComponentsInChildren<PipeMiniGame2Pipe>(true))
        {
            if (pipe.IsFixed) { sb.AppendLine($"  (건너뜀-고정) {Path(pipe.gameObject)}"); continue; }
            var rb = pipe.GetComponent<Rigidbody>();
            var grab = pipe.GetComponent<XRGrabInteractable>();
            var net = pipe.GetComponent<NetworkGrabbableSync>();
            sb.AppendLine($"  · {Path(pipe.gameObject)}");
            sb.AppendLine($"       Rigidbody: gravity={(rb ? rb.useGravity.ToString() : "없음")}, kinematic={(rb ? rb.isKinematic.ToString() : "없음")}");
            sb.AppendLine($"       Grab: throwOnDetach={(grab ? grab.throwOnDetach.ToString() : "없음")}, forceGravityOnDetach={(grab ? grab.forceGravityOnDetach.ToString() : "없음")}");
            sb.AppendLine($"       NetSync.forceNoThrowOnDetach={(net ? net.forceNoThrowOnDetach.ToString() : "없음")}");
            n++;
        }
        sb.AppendLine($"\n대상 파츠 {n}개. 'Apply' 시 중력+던지기 적용.");
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "2) Apply to Scene")]
    public static void Apply()
    {
        var container = FindContainer();
        if (container == null)
        {
            EditorUtility.DisplayDialog("PipeParts Physics",
                "'Stage 1 Pipe parts' 컨테이너를 찾지 못했습니다. 씬/이름 확인.", "확인");
            return;
        }

        if (!EditorUtility.DisplayDialog("PipeParts Physics Apply",
            $"'{container.name}' 하위 파이프 파츠에 중력 + 던지기를 적용합니다.\n" +
            "Undo 가능. 적용 후 씬 저장 필요. 계속할까요?",
            "Apply", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("PipeParts Physics Apply");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder($"[PipeParts-Physics] Apply 결과 (컨테이너 {Path(container)}):\n");
        int count = 0;
        foreach (var pipe in container.GetComponentsInChildren<PipeMiniGame2Pipe>(true))
        {
            if (pipe.IsFixed) continue;

            var rb = pipe.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Undo.RecordObject(rb, "Pipe Rigidbody");
                rb.useGravity = true;
                rb.isKinematic = false;
                EditorUtility.SetDirty(rb);
            }

            var grab = pipe.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                Undo.RecordObject(grab, "Pipe Grab");
                grab.throwOnDetach = true;
                grab.forceGravityOnDetach = true;
                EditorUtility.SetDirty(grab);
            }

            var net = pipe.GetComponent<NetworkGrabbableSync>();
            if (net != null)
            {
                Undo.RecordObject(net, "Pipe NetSync");
                net.forceNoThrowOnDetach = false;
                EditorUtility.SetDirty(net);
            }

            sb.AppendLine($"  · {Path(pipe.gameObject)}");
            count++;
        }
        sb.AppendLine($"\n파츠 {count}개에 중력+던지기 적용 완료. 반드시 씬을 저장하세요(Ctrl+S).");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "3) Revert (중력/던지기 해제)")]
    public static void Revert()
    {
        var container = FindContainer();
        if (container == null) return;

        if (!EditorUtility.DisplayDialog("PipeParts Physics Revert",
            "파이프 파츠를 다시 kinematic(중력/던지기 OFF) 로 되돌립니다.\nUndo 가능. 계속할까요?",
            "Revert", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("PipeParts Physics Revert");
        int group = Undo.GetCurrentGroup();

        int count = 0;
        foreach (var pipe in container.GetComponentsInChildren<PipeMiniGame2Pipe>(true))
        {
            if (pipe.IsFixed) continue;

            var rb = pipe.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Undo.RecordObject(rb, "Pipe Rigidbody");
                rb.useGravity = false;
                rb.isKinematic = true;
                EditorUtility.SetDirty(rb);
            }
            var grab = pipe.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                Undo.RecordObject(grab, "Pipe Grab");
                grab.throwOnDetach = false;
                grab.forceGravityOnDetach = false;
                EditorUtility.SetDirty(grab);
            }
            var net = pipe.GetComponent<NetworkGrabbableSync>();
            if (net != null)
            {
                Undo.RecordObject(net, "Pipe NetSync");
                net.forceNoThrowOnDetach = true;
                EditorUtility.SetDirty(net);
            }
            count++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[PipeParts-Physics] Revert 완료 — 파츠 {count}개. 씬 저장 필요.");
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
