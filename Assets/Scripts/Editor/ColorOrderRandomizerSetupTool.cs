#if UNITY_EDITOR
using System.Linq;
using System.Text;
using Fusion;
using PipePuz.LightBeam;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// RoomCliff (Stage1 Skin) 의 ColorOrderPanel 색 순서를 매 세션 랜덤화하는
/// <see cref="ColorOrderRandomizer"/> 를 씬에 설치/배선하는 셋업 툴.
///
/// 메뉴 (Tools/Network/Stage3/):
///   5) Setup Color Order Randomizer : 독립 루트 "ColorOrderRandomizer"
///        (NetworkObject + ColorOrderRandomizer) 를 만들고 Stage1 Skin 의 ColorOrderPanel 을 할당.
///        해당 서브트리의 LightBeamController.RequiredOrderPanel 이 같은 패널을 가리키는지 검증.
///
/// 안전장치: Undo 가능. 적용 후 반드시 씬 저장(Ctrl+S) → Fusion 베이크 갱신.
/// 패턴은 Stage3SyncSetupTool / CliffPlatformRandomizerSetupTool 과 동일(SetAllowOverride + 버전 비트).
/// </summary>
public static class ColorOrderRandomizerSetupTool
{
    const string Root = "Tools/Network/Stage3/";
    const int VersionCurrentBit = 1 << 19; // 524288

    [MenuItem(Root + "5) Setup Color Order Randomizer")]
    public static void Setup()
    {
        var panel = FindStage1SkinPanel(out var skinRoot);
        if (panel == null)
        {
            const string msg = "RoomCliff (Stage1 Skin) 아래에서 ColorOrderPanel 을 찾지 못했습니다.";
            EditorUtility.DisplayDialog("Color Order Randomizer", msg, "OK");
            Debug.LogWarning("[ColorOrderRandomizer-Setup] " + msg);
            return;
        }

        // 같은 서브트리의 LightBeamController 가 이 패널을 검증에 쓰는지 확인.
        var beamCtrl = skinRoot.GetComponentsInChildren<LightBeamController>(true).FirstOrDefault();
        string beamNote;
        if (beamCtrl == null)
            beamNote = "  ! 경고: 이 서브트리에 LightBeamController 가 없습니다 — 순서 검증이 동작하지 않을 수 있음.";
        else if (beamCtrl.RequiredOrderPanel != panel)
            beamNote = $"  ! 주의: LightBeamController.RequiredOrderPanel 이 이 패널과 다릅니다 ({(beamCtrl.RequiredOrderPanel != null ? beamCtrl.RequiredOrderPanel.name : "null")}). 자동 연결합니다.";
        else
            beamNote = "  ✓ LightBeamController.RequiredOrderPanel 이 이 패널을 가리킴(검증 연결됨).";

        if (!EditorUtility.DisplayDialog("Color Order Randomizer 설치",
            $"대상 패널: {Path(panel.transform)}\n" +
            $"슬롯 {panel.DisplaySlots.Count} / 팔레트 {panel.ColorPalette.Count} / MaxSequenceLength {panel.MaxSequenceLength}\n" +
            $"{beamNote}\n\n" +
            "· 독립 루트 'ColorOrderRandomizer' (NetworkObject) 생성\n" +
            "· Undo 가능. 적용 후 씬을 저장해야 Fusion 베이크에 반영됩니다.\n\n계속할까요?",
            "Setup", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Setup Color Order Randomizer");
        int group = Undo.GetCurrentGroup();

        var sb = new StringBuilder("[ColorOrderRandomizer-Setup] 결과:\n");

        var randomizer = Object.FindFirstObjectByType<ColorOrderRandomizer>();
        if (randomizer == null)
        {
            var go = new GameObject("ColorOrderRandomizer");
            Undo.RegisterCreatedObjectUndo(go, "Create ColorOrderRandomizer");
            var no = Undo.AddComponent<NetworkObject>(go);
            SetAllowOverride(no);
            randomizer = Undo.AddComponent<ColorOrderRandomizer>(go);
            sb.AppendLine("  + 루트 'ColorOrderRandomizer' 생성 (NetworkObject + ColorOrderRandomizer)");
        }
        else
        {
            if (randomizer.GetComponent<NetworkObject>() == null)
                SetAllowOverride(Undo.AddComponent<NetworkObject>(randomizer.gameObject));
            sb.AppendLine("  · 기존 ColorOrderRandomizer 재사용");
        }

        Undo.RecordObject(randomizer, "Configure ColorOrderRandomizer");
        randomizer.panel = panel;
        randomizer.colorCount = Mathf.Max(1, panel.MaxSequenceLength);
        EditorUtility.SetDirty(randomizer);
        sb.AppendLine($"  · panel 할당: {panel.name} / colorCount {randomizer.colorCount}");

        // LightBeamController 가 다른 패널을 가리키면 이 패널로 연결.
        if (beamCtrl != null && beamCtrl.RequiredOrderPanel != panel)
        {
            Undo.RecordObject(beamCtrl, "Wire RequiredOrderPanel");
            beamCtrl.RequiredOrderPanel = panel;
            EditorUtility.SetDirty(beamCtrl);
            sb.AppendLine("  · LightBeamController.RequiredOrderPanel → 이 패널로 연결");
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        sb.AppendLine("\n완료. 반드시 씬을 저장하세요(Ctrl+S) — Fusion 이 NetworkedBehaviours/베이크를 갱신합니다.");
        Debug.Log(sb.ToString(), randomizer);
        Selection.activeObject = randomizer.gameObject;
    }

    [MenuItem(Root + "5) Setup Color Order Randomizer (Dry-Run)")]
    public static void DryRun()
    {
        var panel = FindStage1SkinPanel(out _);
        Debug.Log(panel == null
            ? "[ColorOrderRandomizer-Setup] Dry-Run: ColorOrderPanel(Stage1 Skin) 을 못 찾음."
            : $"[ColorOrderRandomizer-Setup] Dry-Run: 대상 패널 = {Path(panel.transform)} (변경 없음).");
    }

    // ──────────────────────────────────────────────────────────────────────
    static ColorOrderPanel FindStage1SkinPanel(out Transform skinRoot)
    {
        skinRoot = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name == "RoomCliff (Stage1 Skin)");
        if (skinRoot == null) return null;
        return skinRoot.GetComponentsInChildren<ColorOrderPanel>(true).FirstOrDefault();
    }

    static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        var p = t.parent;
        while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
        return sb.ToString();
    }

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
        Debug.LogWarning($"[ColorOrderRandomizer-Setup] {no.name} Flags property 를 못 찾음 — " +
                         "인스펙터에서 'Allow State Authority Override' 직접 체크 필요.", no);
    }
}
#endif
