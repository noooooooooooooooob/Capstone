#if UNITY_EDITOR
using System.Text;
using Fusion;
using PipePuz.LightBeam;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// LightOrb / LightOrbSocket 에 네트워크 동기화를 일괄 적용하는 셋업 툴.
///
/// 메뉴 Tools/Network/LightOrb/:
///   - 각 LightOrb   → NetworkObject(AllowStateAuthorityOverride + 버전비트) + NetworkTransform
///                     + NetworkGrabbableSync(던지기 OFF: 그냥 떨어뜨림) + LightOrbNetworkSync
///   - 각 LightOrbSocket → NetworkObject(동일 플래그)  ← 도킹 상태 복제 시 id 로 참조됨
///
/// 이로써 한 플레이어가 orb 를 잡아 옮기거나 socket 에 끼우거나 빼는 것을 상대도 실시간으로 보고,
/// 양쪽 모두 orb 를 잡을 수 있다(권위 이전). 적용 후 반드시 씬 저장.
/// </summary>
public static class LightOrbSyncSetupTool
{
    const string Root = "Tools/Network/LightOrb/";

    // Fusion NetworkObjectFlags 의 "current version"(V1) 비트. 이게 없으면 AllowStateAuthorityOverride 가
    // 무시되어 게스트의 권위 요청이 거부된다(카펫에서 확인된 문제와 동일). 786433 = bit0 + Allow + V1.
    const int VersionCurrentBit = 1 << 19; // 524288

    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRun()
    {
        var orbs = Object.FindObjectsByType<LightOrb>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sockets = Object.FindObjectsByType<LightOrbSocket>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sb = new StringBuilder();
        sb.AppendLine("[LightOrb-Sync] Dry-Run — 변경 없음. 'Apply' 시 처리될 대상:\n");
        foreach (var o in orbs) sb.AppendLine($"  [LightOrb]{(IsNested(o.gameObject) ? " (중첩-건너뜀)" : "")}  {Path(o.gameObject)}");
        foreach (var s in sockets) sb.AppendLine($"  [LightOrbSocket]{(IsNested(s.gameObject) ? " (중첩-건너뜀)" : "")}  {Path(s.gameObject)}");
        sb.AppendLine($"\nLightOrb {orbs.Length} | LightOrbSocket {sockets.Length}");
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "2) Apply to Scene")]
    public static void Apply()
    {
        if (!EditorUtility.DisplayDialog("LightOrb Apply",
            "LightOrb / LightOrbSocket 에 네트워크 동기화를 적용합니다.\nUndo 가능. 적용 후 씬 저장 필요.\n\n계속할까요?",
            "Apply", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("LightOrb Network Sync Apply");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder();
        sb.AppendLine("[LightOrb-Sync] Apply 결과:");

        int orbs = 0, orbSkip = 0;
        foreach (var o in Object.FindObjectsByType<LightOrb>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsNested(o.gameObject)) { orbSkip++; sb.AppendLine($"  ! 중첩 건너뜀 [LightOrb] {Path(o.gameObject)}"); continue; }
            var no = o.GetComponent<NetworkObject>() ?? Undo.AddComponent<NetworkObject>(o.gameObject);
            if (o.GetComponent<NetworkTransform>() == null) Undo.AddComponent<NetworkTransform>(o.gameObject);
            var ngs = o.GetComponent<NetworkGrabbableSync>() ?? Undo.AddComponent<NetworkGrabbableSync>(o.gameObject);
            Undo.RecordObject(ngs, "ngs");
            ngs.forceNoThrowOnDetach = true; // orb 는 던지지 않고 떨어뜨려 socket 에 안착.
            EditorUtility.SetDirty(ngs);
            if (o.GetComponent<LightOrbNetworkSync>() == null) Undo.AddComponent<LightOrbNetworkSync>(o.gameObject);
            SetFlags(no);
            EditorUtility.SetDirty(o);
            orbs++;
        }
        sb.AppendLine($"  · LightOrb 적용 {orbs} (중첩 {orbSkip})");

        int socks = 0, sockSkip = 0;
        foreach (var s in Object.FindObjectsByType<LightOrbSocket>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsNested(s.gameObject)) { sockSkip++; sb.AppendLine($"  ! 중첩 건너뜀 [LightOrbSocket] {Path(s.gameObject)}"); continue; }
            var no = s.GetComponent<NetworkObject>() ?? Undo.AddComponent<NetworkObject>(s.gameObject);
            SetFlags(no);
            EditorUtility.SetDirty(s);
            socks++;
        }
        sb.AppendLine($"  · LightOrbSocket 적용 {socks} (중첩 {sockSkip})");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        sb.AppendLine("\n완료. 반드시 씬을 저장하세요(Ctrl+S).");
        Debug.Log(sb.ToString());
    }

    // ── 유틸 ──────────────────────────────────────────────────────────────
    static void SetFlags(NetworkObject no)
    {
        if (no == null) return;
        int needBits = (int)NetworkObjectFlags.AllowStateAuthorityOverride | VersionCurrentBit;
        var so = new SerializedObject(no);
        foreach (var name in new[] { "Flags", "_flags", "m_Flags", "_objectFlags" })
        {
            var prop = so.FindProperty(name);
            if (prop != null && prop.propertyType == SerializedPropertyType.Integer)
            {
                if ((prop.intValue & needBits) == needBits) return;
                prop.intValue |= needBits;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(no);
                return;
            }
        }
        Debug.LogWarning($"[LightOrb-Sync] {no.name} Flags property 를 못 찾음 — 인스펙터에서 'Allow State Authority Override' 직접 체크 필요.", no);
    }

    static bool IsNested(GameObject go)
    {
        if (go.GetComponent<NetworkObject>() != null) return false;
        var p = go.transform.parent;
        while (p != null) { if (p.GetComponent<NetworkObject>() != null) return true; p = p.parent; }
        return false;
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
