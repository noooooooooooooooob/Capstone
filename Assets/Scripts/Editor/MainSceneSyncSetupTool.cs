#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Capstone.Network.Sync;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Main Scene 의 모든 인터랙터블에 네트워크 동기화 컴포넌트를 일괄 부착하는 셋업 툴.
///
/// 카테고리:
///   - GRABBABLE  : XRGrabInteractable 보유 → NetworkObject + NetworkTransform + NetworkGrabbableSync
///   - MOVER      : 이동/회전 로직 스크립트 보유 → NetworkObject + NetworkTransform
///                  + NetworkAuthorityClaim(근접/조작 시 권위) + ProxyDriverGate(비권위 로직 차단)
///   - DELETABLE  : 사라짐/숨김 스크립트 보유 → NetworkObject + NetworkActiveSync(autoMirror)
///   - BUTTON     : (그랩 아닌) XR Interactable → NetworkObject + NetworkAuthorityClaim + NetworkEventRelay
///
/// 안전장치:
///   - 모든 변경은 Unity Undo 로 되돌릴 수 있고, 씬을 저장하기 전엔 디스크에 반영되지 않는다.
///   - "Apply to Whole Scene" 은 부모에 NetworkObject 가 있는 중첩 후보를 건너뛰고 리포트한다(베이킹 사고 방지).
///   - 적용 후 반드시 씬 저장 → Fusion 이 NetworkedBehaviours 를 재베이킹한다(필수).
///
/// 권장 순서: 1) Dry-Run Report → 2) Finish Grab Conversion → 3) Apply to Whole Scene → 씬 저장 → 2인 플레이 검증.
/// </summary>
public static class MainSceneSyncSetupTool
{
    const string Root = "Tools/Network/Auto-Sync/";

    // 자기(루트) transform 이 직접 회전/이동하는 로직 스크립트 — 타입 이름으로 매칭(어셈블리 결합 회피).
    // 주의: 근접 자동문(AutoSlidingDoor 등)은 '자식 패널'이 움직이고 근접 트리거 기반이라 여기에 넣지 않는다.
    //       → PlayerHeadRegistry + AutoSlidingDoor 의 결정론적 양쪽-머리 감지로 동기화한다(소스 참조).
    static readonly HashSet<string> MoverTypeNames = new HashSet<string>
    {
        "SuppressionWheel", "EMHandle", "EMSlider", "RadiatorController",
        "BeamAimController", "LightBeamMirror", "Valve",
    };

    // 사라지거나 숨겨지는 오브젝트 — 타입 이름으로 매칭.
    static readonly HashSet<string> DeletableTypeNames = new HashSet<string>
    {
        "FirefightFire", "MeltedBattery", "HintBall",
        "SlimeCreature", "ScorpionCreature", "FlyingCreature", "BoxerCreature",
        "CrabCreature", "DragonflyCreature", "LizardCreature", "SnakeCreature", "ZooCreature",
    };

    enum Category { None, Grabbable, Mover, Deletable, Button }

    // ──────────────────────────────────────────────────────────────────────
    //  1) Dry-Run Report
    // ──────────────────────────────────────────────────────────────────────
    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRunReport()
    {
        int grab = 0, mover = 0, del = 0, btn = 0, nested = 0;
        int needNo = 0, needFlag = 0;
        var sb = new StringBuilder();
        sb.AppendLine("[Auto-Sync] Dry-Run Report — 변경 없음. 아래는 'Apply' 시 적용될 분류입니다.\n");

        foreach (var go in AllSceneGameObjects())
        {
            var cat = Classify(go);
            if (cat == Category.None) continue;

            bool isNested = HasParentNetworkObject(go) && go.GetComponent<NetworkObject>() == null;
            if (isNested) nested++;

            var no = go.GetComponent<NetworkObject>();
            if (no == null) needNo++;
            else if (!HasAllowOverride(no)) needFlag++;

            switch (cat)
            {
                case Category.Grabbable: grab++; break;
                case Category.Mover: mover++; break;
                case Category.Deletable: del++; break;
                case Category.Button: btn++; break;
            }

            sb.AppendLine($"  [{cat}]{(isNested ? " (중첩-건너뜀)" : "")}  {GetPath(go)}");
        }

        sb.AppendLine();
        sb.AppendLine($"합계 — Grabbable {grab} | Mover {mover} | Deletable {del} | Button {btn}");
        sb.AppendLine($"NetworkObject 추가 필요 {needNo} | AllowOverride 설정 필요 {needFlag} | 중첩(건너뜀) {nested}");
        Debug.Log(sb.ToString());
    }

    // ──────────────────────────────────────────────────────────────────────
    //  2) Finish Grab Conversion (구버전 → NetworkGrabbableSync)
    // ──────────────────────────────────────────────────────────────────────
    [MenuItem(Root + "2) Finish Grab Conversion (전체)")]
    public static void FinishGrabConversion()
    {
        int converted = 0, already = 0, skipped = 0;
        foreach (var grab in Object.FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None))
        {
            var no = grab.GetComponent<NetworkObject>();
            if (no == null) { skipped++; continue; }
            if (grab.GetComponent<NetworkGrabbableSync>() != null) { already++; continue; }
            EnsureGrabbable(grab.gameObject);
            converted++;
        }
        Finish($"[Auto-Sync] Grab 전환 — 신규 {converted} | 이미 적용 {already} | NO 없음 스킵 {skipped}");
    }

    // ──────────────────────────────────────────────────────────────────────
    //  3) Apply to Selection / Whole Scene
    // ──────────────────────────────────────────────────────────────────────
    [MenuItem(Root + "3) Apply to Selection")]
    public static void ApplySelection()
    {
        int n = 0;
        foreach (var go in Selection.gameObjects)
        {
            // 선택 적용은 사용자가 명시적으로 고른 것이므로 중첩도 허용.
            if (ApplyOne(go, allowNested: true)) n++;
        }
        Finish($"[Auto-Sync] Apply to Selection — 처리 {n}");
    }

    [MenuItem(Root + "4) Apply to Whole Scene (movers + deletables + buttons + grab)")]
    public static void ApplyWholeScene()
    {
        if (!EditorUtility.DisplayDialog("Apply to Whole Scene",
            "Main Scene 의 모든 인터랙터블에 네트워크 동기화 컴포넌트를 부착합니다.\n" +
            "· 부모에 NetworkObject 가 있는 중첩 후보는 건너뜁니다(리포트).\n" +
            "· Undo 가능. 씬을 저장하기 전엔 디스크에 반영되지 않습니다.\n\n계속할까요?",
            "Apply", "Cancel"))
            return;

        int applied = 0, nestedSkipped = 0;
        var nestedList = new List<string>();
        foreach (var go in AllSceneGameObjects())
        {
            var cat = Classify(go);
            if (cat == Category.None) continue;

            if (HasParentNetworkObject(go) && go.GetComponent<NetworkObject>() == null)
            {
                nestedSkipped++;
                nestedList.Add($"  [{cat}] {GetPath(go)}");
                continue;
            }
            if (ApplyOne(go, allowNested: false)) applied++;
        }

        var msg = $"[Auto-Sync] Apply to Whole Scene — 적용 {applied} | 중첩 건너뜀 {nestedSkipped}";
        if (nestedList.Count > 0)
            msg += "\n중첩으로 건너뛴 오브젝트(필요 시 선택 후 'Apply to Selection' 으로 수동 처리):\n" + string.Join("\n", nestedList);
        Finish(msg);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  분류 / 적용
    // ──────────────────────────────────────────────────────────────────────
    static Category Classify(GameObject go)
    {
        if (go.GetComponent<XRGrabInteractable>() != null) return Category.Grabbable;

        var monos = go.GetComponents<MonoBehaviour>();
        foreach (var m in monos)
        {
            if (m == null) continue;
            var n = m.GetType().Name;
            if (MoverTypeNames.Contains(n)) return Category.Mover;
        }
        foreach (var m in monos)
        {
            if (m == null) continue;
            var n = m.GetType().Name;
            if (DeletableTypeNames.Contains(n)) return Category.Deletable;
        }
        if (go.GetComponent<XRBaseInteractable>() != null) return Category.Button;
        return Category.None;
    }

    static bool ApplyOne(GameObject go, bool allowNested)
    {
        switch (Classify(go))
        {
            case Category.Grabbable: EnsureGrabbable(go); return true;
            case Category.Mover: EnsureMover(go); return true;
            case Category.Deletable: EnsureDeletable(go); return true;
            case Category.Button: EnsureButton(go); return true;
            default: return false;
        }
    }

    static void EnsureGrabbable(GameObject go)
    {
        var no = EnsureNetworkObject(go);
        if (go.GetComponent<NetworkTransform>() == null) Undo.AddComponent<NetworkTransform>(go);
        if (go.GetComponent<NetworkGrabbableSync>() == null) Undo.AddComponent<NetworkGrabbableSync>(go);
        SetAllowOverride(no);
        EditorUtility.SetDirty(go);
    }

    static void EnsureMover(GameObject go)
    {
        var no = EnsureNetworkObject(go);
        if (go.GetComponent<NetworkTransform>() == null) Undo.AddComponent<NetworkTransform>(go);

        var claim = go.GetComponent<NetworkAuthorityClaim>() ?? Undo.AddComponent<NetworkAuthorityClaim>(go);
        claim.claimOnSelect = true;
        claim.claimOnActivate = true;
        claim.claimOnLocalProximity = true;

        // 비권위 측에서 끌 구동 스크립트 = 이 오브젝트의 mover 컴포넌트들.
        var drivers = go.GetComponents<MonoBehaviour>()
                        .Where(m => m != null && MoverTypeNames.Contains(m.GetType().Name))
                        .Cast<UnityEngine.Behaviour>().ToArray();
        var gate = go.GetComponent<ProxyDriverGate>() ?? Undo.AddComponent<ProxyDriverGate>(go);
        gate.driversDisabledOnProxy = drivers;

        SetAllowOverride(no);
        EditorUtility.SetDirty(go);
    }

    static void EnsureDeletable(GameObject go)
    {
        var no = EnsureNetworkObject(go);
        var sync = go.GetComponent<NetworkActiveSync>() ?? Undo.AddComponent<NetworkActiveSync>(go);
        sync.autoMirror = true;
        SetAllowOverride(no);
        EditorUtility.SetDirty(go);
    }

    static void EnsureButton(GameObject go)
    {
        var no = EnsureNetworkObject(go);

        var claim = go.GetComponent<NetworkAuthorityClaim>() ?? Undo.AddComponent<NetworkAuthorityClaim>(go);
        claim.claimOnSelect = true;
        claim.claimOnActivate = true;
        claim.claimOnLocalProximity = false;

        // 이미 네트워크 처리되는 버튼(예: XRPhysicalButton: NetworkBehaviour) 위에는 Relay 가 불필요.
        bool alreadyNetworked = go.GetComponents<MonoBehaviour>().Any(m => m is NetworkBehaviour && !(m is NetworkAuthorityClaim) && !(m is NetworkGrabbableSync));
        if (!alreadyNetworked && go.GetComponent<NetworkEventRelay>() == null)
            Undo.AddComponent<NetworkEventRelay>(go); // onRelayed 는 인스펙터에서 수동 연결 필요

        SetAllowOverride(no);
        EditorUtility.SetDirty(go);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  유틸
    // ──────────────────────────────────────────────────────────────────────
    static NetworkObject EnsureNetworkObject(GameObject go)
    {
        var no = go.GetComponent<NetworkObject>();
        if (no == null) no = Undo.AddComponent<NetworkObject>(go);
        return no;
    }

    static bool HasAllowOverride(NetworkObject no) =>
        (no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) == NetworkObjectFlags.AllowStateAuthorityOverride;

    // Fusion 빌드별로 Flags 의 SerializedProperty 이름이 다를 수 있어 후보를 순회(기존 셋업 툴과 동일 패턴).
    static void SetAllowOverride(NetworkObject no)
    {
        if (HasAllowOverride(no)) return;
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
        Debug.LogWarning($"[Auto-Sync] {no.name} Flags property 를 못 찾음 — 인스펙터에서 'Allow State Authority Override' 직접 체크 필요.", no);
    }

    static bool HasParentNetworkObject(GameObject go)
    {
        var p = go.transform.parent;
        while (p != null)
        {
            if (p.GetComponent<NetworkObject>() != null) return true;
            p = p.parent;
        }
        return false;
    }

    static IEnumerable<GameObject> AllSceneGameObjects()
    {
        // 인터랙티브 후보가 될 수 있는 컴포넌트를 가진 오브젝트만 순회(성능).
        var set = new HashSet<GameObject>();
        foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b != null) set.Add(b.gameObject);
        return set;
    }

    static string GetPath(GameObject go)
    {
        var sb = new StringBuilder(go.name);
        var t = go.transform.parent;
        while (t != null) { sb.Insert(0, t.name + "/"); t = t.parent; }
        return sb.ToString();
    }

    static void Finish(string summary)
    {
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(summary + "\n— 씬을 저장하면 Fusion 이 NetworkedBehaviours 를 재베이킹합니다(필수). 저장 후 2인 플레이로 검증하세요.");
    }
}
#endif
