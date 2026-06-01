#if UNITY_EDITOR
using System.Text;
using Capstone.Network.Sync;
using Fusion;
using PipePuz.LightBeam;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// LightBeam 퍼즐의 "조작 가능한 무버"들을 네트워크 동기화하는 셋업 툴.
///
/// 빔 경로(LightBeamController)는 매 프레임 emitter 위치 + 거울 회전으로 결정론적으로 계산되므로,
/// "거울 회전"과 "조준 노브 위치"만 동기화하면 빔·리시버 적중·클리어가 양쪽에서 동일하게 재생된다.
/// 따라서 컨트롤러/리시버/LightBeamPuzzle 컨테이너 자체는 네트워크 컴포넌트가 필요 없다.
///
/// 메뉴 Tools/Network/LightBeam/:
///   - 각 LightBeamMirror(Platform_Mirror 1~4 등) → NetworkObject + NetworkTransform
///       + NetworkAuthorityClaim(잡으면 권위 획득) + ProxyDriverGate(비권위에선 회전 로직 OFF → 떨림 방지)
///   - 각 BeamAimController 의 Knob → NetworkObject + NetworkTransform + NetworkAuthorityClaim
///       (BeamAimController 는 양쪽에서 돌며 '동기화된 노브 위치'로 emitter Z 를 계산 → 빔 일치)
///   - 모든 NetworkObject 에 AllowStateAuthorityOverride + 버전비트(786433) 설정.
///
/// 적용 후 반드시 씬 저장. (코드로 추가한 NetworkObject 는 베이킹 시 버전비트가 빠질 수 있어,
///  필요하면 씬의 'Flags: 262145' 를 786433 으로 일괄 보정해야 한다 — 카펫/오브와 동일 이슈.)
/// </summary>
public static class LightBeamSyncSetupTool
{
    const string Root = "Tools/Network/LightBeam/";
    const int VersionCurrentBit = 1 << 19; // 524288 — 정상 오브젝트(786433)가 가진 current-version 비트.

    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRun()
    {
        var mirrors = Object.FindObjectsByType<LightBeamMirror>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var aims = Object.FindObjectsByType<BeamAimController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sb = new StringBuilder();
        sb.AppendLine("[LightBeam-Sync] Dry-Run — 변경 없음.\n");
        foreach (var m in mirrors) sb.AppendLine($"  [Mirror]{(IsNested(m.gameObject) ? " (중첩-건너뜀)" : "")}  {Path(m.gameObject)}");
        foreach (var a in aims)
        {
            string knob = a.Knob != null ? Path(a.Knob.gameObject) : "(Knob 미지정!)";
            sb.AppendLine($"  [AimKnob] {knob}   ← {Path(a.gameObject)}");
        }
        sb.AppendLine($"\nLightBeamMirror {mirrors.Length} | BeamAimController {aims.Length}");
        Debug.Log(sb.ToString());
    }

    [MenuItem(Root + "2) Apply to Scene")]
    public static void Apply()
    {
        if (!EditorUtility.DisplayDialog("LightBeam Apply",
            "LightBeamMirror(거울들) 과 BeamAimController 의 Knob 에 네트워크 동기화를 적용합니다.\n" +
            "Undo 가능. 적용 후 씬 저장 필요.\n\n계속할까요?",
            "Apply", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("LightBeam Network Sync Apply");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder();
        sb.AppendLine("[LightBeam-Sync] Apply 결과:");

        // ── 거울 (Mover) ──────────────────────────────────────────────────
        int mirrors = 0, mSkip = 0;
        foreach (var m in Object.FindObjectsByType<LightBeamMirror>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsNested(m.gameObject)) { mSkip++; sb.AppendLine($"  ! 중첩 건너뜀 [Mirror] {Path(m.gameObject)}"); continue; }
            var no = m.GetComponent<NetworkObject>() ?? Undo.AddComponent<NetworkObject>(m.gameObject);
            if (m.GetComponent<NetworkTransform>() == null) Undo.AddComponent<NetworkTransform>(m.gameObject);

            var claim = m.GetComponent<NetworkAuthorityClaim>() ?? Undo.AddComponent<NetworkAuthorityClaim>(m.gameObject);
            claim.claimOnSelect = true;
            claim.claimOnActivate = true;
            claim.claimOnLocalProximity = false;

            var gate = m.GetComponent<ProxyDriverGate>() ?? Undo.AddComponent<ProxyDriverGate>(m.gameObject);
            gate.driversDisabledOnProxy = new UnityEngine.Behaviour[] { m };

            SetFlags(no);
            EditorUtility.SetDirty(m);
            mirrors++;
        }
        sb.AppendLine($"  · LightBeamMirror 적용 {mirrors} (중첩 {mSkip})");

        // ── 조준 노브 ─────────────────────────────────────────────────────
        int knobs = 0, kSkip = 0, kNull = 0;
        foreach (var a in Object.FindObjectsByType<BeamAimController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (a.Knob == null) { kNull++; sb.AppendLine($"  ! Knob 미지정 [AimController] {Path(a.gameObject)}"); continue; }
            var go = a.Knob.gameObject;
            if (IsNested(go)) { kSkip++; sb.AppendLine($"  ! 중첩 건너뜀 [AimKnob] {Path(go)}"); continue; }
            var no = go.GetComponent<NetworkObject>() ?? Undo.AddComponent<NetworkObject>(go);
            if (go.GetComponent<NetworkTransform>() == null) Undo.AddComponent<NetworkTransform>(go);
            var claim = go.GetComponent<NetworkAuthorityClaim>() ?? Undo.AddComponent<NetworkAuthorityClaim>(go);
            claim.claimOnSelect = true;
            claim.claimOnActivate = true;
            claim.claimOnLocalProximity = false;
            SetFlags(no);
            EditorUtility.SetDirty(go);
            knobs++;
        }
        sb.AppendLine($"  · BeamAimController Knob 적용 {knobs} (중첩 {kSkip}, Knob 미지정 {kNull})");

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
        Debug.LogWarning($"[LightBeam-Sync] {no.name} Flags property 를 못 찾음 — 인스펙터에서 'Allow State Authority Override' 직접 체크 필요.", no);
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
