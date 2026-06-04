#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fusion;
using PipePuz.RoomCarpet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// RoomCliff (Stage1 Skin)/Platforms 의 Platform_Mirror1~4 위치를 매 세션 랜덤화하는
/// <see cref="CliffPlatformRandomizer"/> 를 씬에 설치/배선하는 셋업 툴.
///
/// 메뉴 (Tools/Network/Stage3/):
///   4) Setup Cliff Platform Randomizer : 독립 루트 "CliffPlatformRandomizer"
///        (NetworkObject + CliffPlatformRandomizer) 를 만들고 Platform_Mirror1~4 를 할당.
///        영역/회피 지점은 RoomCliff 좌측 챔버 기준 기본값으로 채운다(인스펙터에서 조정 가능).
///
/// 안전장치: Undo 가능. 적용 후 반드시 씬 저장(Ctrl+S) → Fusion 이 NetworkedBehaviours/베이크 갱신.
/// 패턴은 Stage3SyncSetupTool 과 동일(SetAllowOverride + 버전 비트).
/// </summary>
public static class CliffPlatformRandomizerSetupTool
{
    const string Root = "Tools/Network/Stage3/";

    // RoomCliff (Stage1 Skin) 좌측 챔버 안쪽(이미터/벽 여유 포함). RoomCliffSetup 의 상수 기준.
    static readonly Vector2 DefaultAreaMin = new Vector2(-20f, 5.5f);
    static readonly Vector2 DefaultAreaMax = new Vector2(-2.5f, 16.5f);
    const float DefaultMinSpacing = 5f;
    const float DefaultAvoidSpacing = 3.5f;

    static readonly string[] MirrorNames =
        { "Platform_Mirror1", "Platform_Mirror2", "Platform_Mirror3", "Platform_Mirror4" };

    const int VersionCurrentBit = 1 << 19; // 524288 — Stage3SyncSetupTool 과 동일.

    [MenuItem(Root + "4) Setup Cliff Platform Randomizer")]
    public static void Setup()
    {
        var platforms = FindMirrorPlatforms(out string report);
        if (platforms == null)
        {
            EditorUtility.DisplayDialog("Cliff Randomizer", report, "OK");
            Debug.LogWarning("[CliffRandomizer-Setup] " + report);
            return;
        }

        if (!EditorUtility.DisplayDialog("Cliff Randomizer 설치",
            "RoomCliff (Stage1 Skin) 의 거울 발판 위치를 매 세션 랜덤화합니다.\n\n" +
            report + "\n\n" +
            "· 독립 루트 'CliffPlatformRandomizer' (NetworkObject) 생성\n" +
            "· Undo 가능. 적용 후 씬을 저장해야 Fusion 베이크에 반영됩니다.\n\n계속할까요?",
            "Setup", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Setup Cliff Platform Randomizer");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder("[CliffRandomizer-Setup] 결과:\n");

        var randomizer = Object.FindFirstObjectByType<CliffPlatformRandomizer>();
        if (randomizer == null)
        {
            var go = new GameObject("CliffPlatformRandomizer");
            Undo.RegisterCreatedObjectUndo(go, "Create CliffPlatformRandomizer");
            var no = Undo.AddComponent<NetworkObject>(go);
            SetAllowOverride(no);
            randomizer = Undo.AddComponent<CliffPlatformRandomizer>(go);
            sb.AppendLine("  + 루트 'CliffPlatformRandomizer' 생성 (NetworkObject + CliffPlatformRandomizer)");
        }
        else
        {
            if (randomizer.GetComponent<NetworkObject>() == null)
                SetAllowOverride(Undo.AddComponent<NetworkObject>(randomizer.gameObject));
            sb.AppendLine("  · 기존 CliffPlatformRandomizer 재사용");
        }

        Undo.RecordObject(randomizer, "Configure CliffPlatformRandomizer");
        randomizer.mirrorPlatforms = platforms;
        randomizer.areaMin = DefaultAreaMin;
        randomizer.areaMax = DefaultAreaMax;
        randomizer.minSpacing = DefaultMinSpacing;
        randomizer.avoidSpacing = DefaultAvoidSpacing;

        // 입구 발판 / 리시버를 회피 지점으로 자동 수집(있으면).
        var avoid = new List<Vector2>();
        var entry = FindInStage1SkinPlatforms("Platform_Entry");
        if (entry != null) avoid.Add(new Vector2(entry.localPosition.x, entry.localPosition.z));
        var receiver = FindDeep("Receiver");
        if (receiver != null)
        {
            // Receiver 의 좌표를 Platforms 부모 LOCAL 공간으로 변환(보통 동일 프레임이라 큰 차이 없음).
            var parent = platforms[0] != null ? platforms[0].parent : null;
            Vector3 lp = parent != null ? parent.InverseTransformPoint(receiver.position) : receiver.localPosition;
            avoid.Add(new Vector2(lp.x, lp.z));
        }
        if (avoid.Count > 0) randomizer.avoidPoints = avoid.ToArray();

        EditorUtility.SetDirty(randomizer);

        sb.AppendLine($"  · mirrorPlatforms 할당 {platforms.Length}개: " +
                      string.Join(", ", platforms.Select(p => p != null ? p.name : "null")));
        sb.AppendLine($"  · area [{DefaultAreaMin} ~ {DefaultAreaMax}] / minSpacing {DefaultMinSpacing} / avoidSpacing {DefaultAvoidSpacing}");
        sb.AppendLine($"  · avoidPoints {randomizer.avoidPoints.Length}개");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        sb.AppendLine("\n완료. 반드시 씬을 저장하세요(Ctrl+S) — Fusion 이 NetworkedBehaviours/베이크를 갱신합니다.");
        Debug.Log(sb.ToString(), randomizer);
        Selection.activeObject = randomizer.gameObject;
    }

    [MenuItem(Root + "4) Setup Cliff Platform Randomizer (Dry-Run)")]
    public static void DryRun()
    {
        var platforms = FindMirrorPlatforms(out string report);
        Debug.Log("[CliffRandomizer-Setup] Dry-Run — 변경 없음.\n" + report);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  검색 헬퍼
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>RoomCliff (Stage1 Skin)/Platforms 아래에서 Platform_Mirror1~4 를 순서대로 찾는다.</summary>
    static Transform[] FindMirrorPlatforms(out string report)
    {
        var platformsGroup = FindStage1SkinPlatformsGroup();
        if (platformsGroup == null)
        {
            report = "RoomCliff (Stage1 Skin)/Platforms 를 씬에서 찾지 못했습니다.\n" +
                     "(RoomCliff (Stage1 Skin) 가 비활성/이름 변경되지 않았는지 확인하세요.)";
            return null;
        }

        var result = new Transform[MirrorNames.Length];
        var found = new List<string>();
        var missing = new List<string>();
        for (int i = 0; i < MirrorNames.Length; i++)
        {
            var t = FindChildByName(platformsGroup, MirrorNames[i]);
            result[i] = t;
            if (t != null) found.Add($"{MirrorNames[i]} (현재 local x={t.localPosition.x:F1}, z={t.localPosition.z:F1})");
            else missing.Add(MirrorNames[i]);
        }

        if (missing.Count == MirrorNames.Length)
        {
            report = $"'{Path(platformsGroup)}' 아래에서 Platform_Mirror1~4 를 하나도 못 찾았습니다.";
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"대상 그룹: {Path(platformsGroup)}");
        foreach (var f in found) sb.AppendLine($"  ✓ {f}");
        foreach (var m in missing) sb.AppendLine($"  ✗ {m} (없음 — null 로 둠)");
        report = sb.ToString();
        return result;
    }

    /// <summary>이름이 정확히 "RoomCliff (Stage1 Skin)" 인 오브젝트 아래의 "Platforms" 자식을 반환.</summary>
    static Transform FindStage1SkinPlatformsGroup()
    {
        var roots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in roots)
        {
            if (t.name != "RoomCliff (Stage1 Skin)") continue;
            var platforms = FindChildByName(t, "Platforms");
            if (platforms != null) return platforms;
        }
        return null;
    }

    static Transform FindInStage1SkinPlatforms(string name)
    {
        var group = FindStage1SkinPlatformsGroup();
        return group != null ? FindChildByName(group, name) : null;
    }

    /// <summary>RoomCliff (Stage1 Skin) 서브트리에서 이름으로 깊이 탐색.</summary>
    static Transform FindDeep(string name)
    {
        var roots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform skinRoot = roots.FirstOrDefault(t => t.name == "RoomCliff (Stage1 Skin)");
        if (skinRoot == null) return null;
        foreach (var t in skinRoot.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static Transform FindChildByName(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name == name) return parent.GetChild(i);
        return null;
    }

    static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        var p = t.parent;
        while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Fusion 플래그 (Stage3SyncSetupTool 과 동일 패턴)
    // ──────────────────────────────────────────────────────────────────────
    static void SetAllowOverride(NetworkObject no)
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
        Debug.LogWarning($"[CliffRandomizer-Setup] {no.name} Flags property 를 못 찾음 — " +
                         "인스펙터에서 'Allow State Authority Override' 직접 체크 필요.", no);
    }
}
#endif
