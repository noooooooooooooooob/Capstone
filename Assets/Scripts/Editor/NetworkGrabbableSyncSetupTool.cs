#if UNITY_EDITOR
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Stage1;

/// <summary>
/// 기존 그랩 동기화(GrabAuthorityHandover + GrabNetworkSyncPause) → 단일 NetworkGrabbableSync 로 일괄 전환.
///
/// NetworkGrabbableSync 는 NetworkBehaviour 라서 변환 후 NetworkObject 의 NetworkedBehaviours 재베이킹이
/// 필요한데, 이는 씬 저장 / 플레이 진입 시 Fusion 이 자동 수행한다. 따라서 변환 후 반드시 씬을 저장할 것.
///
/// 권장 순서: Diagnose → (Hose 1개 선택 후) Convert Selected → 2인 플레이 검증 → Convert All.
/// </summary>
public static class NetworkGrabbableSyncSetupTool
{
    const string MenuRoot = "Tools/Stage 1/Grab Sync/";

    [MenuItem(MenuRoot + "Diagnose")]
    public static void Diagnose()
    {
        int total = 0, candidates = 0, hasOldGah = 0, hasOldGnsp = 0, hasNew = 0, missingNo = 0, missingFlag = 0;
        foreach (var grab in Object.FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None))
        {
            total++;
            var no = grab.GetComponent<NetworkObject>();
            if (no == null) { missingNo++; continue; }
            candidates++;

            if (grab.GetComponent<GrabAuthorityHandover>() != null) hasOldGah++;
            if (grab.GetComponent<GrabNetworkSyncPause>() != null) hasOldGnsp++;
            if (grab.GetComponent<NetworkGrabbableSync>() != null) hasNew++;

            if ((no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) != NetworkObjectFlags.AllowStateAuthorityOverride)
            {
                missingFlag++;
                Debug.LogWarning($"[GrabSync:Diagnose] '{grab.name}' AllowStateAuthorityOverride 꺼짐.", no);
            }
        }
        Debug.Log($"[GrabSync:Diagnose] XRGrabInteractable {total} | 변환대상(NO 있음) {candidates} | " +
                  $"구 GAH {hasOldGah} | 구 GNSP {hasOldGnsp} | 신 NGS {hasNew} | NO 없음 {missingNo} | AllowOverride 꺼짐 {missingFlag}");
    }

    [MenuItem(MenuRoot + "Convert Selected")]
    public static void ConvertSelected()
    {
        int converted = 0, skipped = 0;
        foreach (var go in Selection.gameObjects)
        {
            var grab = go.GetComponent<XRGrabInteractable>();
            if (grab == null) { skipped++; continue; }
            if (ConvertOne(grab)) converted++; else skipped++;
        }
        Finish($"[GrabSync:Convert Selected] 변환 {converted} | 스킵 {skipped}");
    }

    [MenuItem(MenuRoot + "Convert All")]
    public static void ConvertAll()
    {
        if (!EditorUtility.DisplayDialog("Convert All Grabbables",
                "씬의 모든 XRGrabInteractable(NetworkObject 보유)을 NetworkGrabbableSync 로 전환합니다.\n" +
                "구 GrabAuthorityHandover / GrabNetworkSyncPause 는 제거됩니다.\n\n계속할까요?",
                "Convert All", "Cancel"))
            return;

        int converted = 0, skipped = 0;
        foreach (var grab in Object.FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None))
        {
            if (ConvertOne(grab)) converted++; else skipped++;
        }
        Finish($"[GrabSync:Convert All] 변환 {converted} | 스킵(NO 없음) {skipped}");
    }

    [MenuItem(MenuRoot + "Revert Selected (NGS → GAH + GNSP)")]
    public static void RevertSelected()
    {
        int reverted = 0, skipped = 0;
        foreach (var go in Selection.gameObjects)
        {
            var grab = go.GetComponent<XRGrabInteractable>();
            if (grab == null) { skipped++; continue; }
            if (RevertOne(grab)) reverted++; else skipped++;
        }
        Finish($"[GrabSync:Revert Selected] 복원 {reverted} | 스킵 {skipped}");
    }

    static bool ConvertOne(XRGrabInteractable grab)
    {
        var no = grab.GetComponent<NetworkObject>();
        if (no == null) return false;

        // NetworkGrabbableSync 는 NetworkTransform 을 요구. 없으면 추가.
        if (grab.GetComponent<NetworkTransform>() == null)
            Undo.AddComponent<NetworkTransform>(grab.gameObject);

        var oldGah = grab.GetComponent<GrabAuthorityHandover>();
        if (oldGah != null) Undo.DestroyObjectImmediate(oldGah);

        var oldGnsp = grab.GetComponent<GrabNetworkSyncPause>();
        if (oldGnsp != null) Undo.DestroyObjectImmediate(oldGnsp);

        if (grab.GetComponent<NetworkGrabbableSync>() == null)
            Undo.AddComponent<NetworkGrabbableSync>(grab.gameObject);

        TrySetAllowOverride(no);

        EditorUtility.SetDirty(grab.gameObject);
        return true;
    }

    static bool RevertOne(XRGrabInteractable grab)
    {
        var no = grab.GetComponent<NetworkObject>();
        if (no == null) return false;

        var ngs = grab.GetComponent<NetworkGrabbableSync>();
        if (ngs != null) Undo.DestroyObjectImmediate(ngs);

        if (grab.GetComponent<GrabAuthorityHandover>() == null)
            Undo.AddComponent<GrabAuthorityHandover>(grab.gameObject);
        if (grab.GetComponent<GrabNetworkSyncPause>() == null)
            Undo.AddComponent<GrabNetworkSyncPause>(grab.gameObject);

        EditorUtility.SetDirty(grab.gameObject);
        return true;
    }

    static void Finish(string summary)
    {
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(summary + "  — 씬을 저장하면 Fusion 이 NetworkedBehaviours 를 재베이킹합니다(필수).");
    }

    // Fusion 빌드별로 NetworkObject.Flags 의 SerializedProperty 이름이 다를 수 있어 후보를 순회.
    static void TrySetAllowOverride(NetworkObject no)
    {
        if ((no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) == NetworkObjectFlags.AllowStateAuthorityOverride)
            return;

        int needBit = (int)NetworkObjectFlags.AllowStateAuthorityOverride;
        var so = new SerializedObject(no);
        foreach (var name in new[] { "Flags", "_flags", "m_Flags", "_objectFlags" })
        {
            var prop = so.FindProperty(name);
            if (prop != null && prop.propertyType == SerializedPropertyType.Integer)
            {
                prop.intValue |= needBit;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(no);
                return;
            }
        }
        Debug.LogWarning($"[GrabSync] {no.name} Flags property 못 찾음 — 인스펙터에서 'Allow State Authority Override' 직접 체크 필요.", no);
    }
}
#endif
