#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Capstone.Network.Sync;
using Fusion;
using PipePuz.RoomCarpet;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Stage3(RoomCarpet) 의 상호작용 오브젝트에 네트워크 동기화를 일괄 적용하는 셋업 툴.
///
/// 무엇을 하나 (메뉴 Tools/Network/Stage3/):
///   2) Build Carpet Prefab     : 런타임 스폰용 Carpet_Net 프리팹 + 머티리얼 생성
///                                (NetworkObject + NetworkTransform + NetworkGrabbableSync
///                                 + DisappearingCarpet + CarpetNetworkSync)
///   3) Apply to Scene          : 위 프리팹을 보장/할당하고,
///        - 독립 루트 "Stage3CarpetNetwork"(NetworkObject + Stage3CarpetNetwork)에 프리팹 할당
///        - 각 HintBall  → NetworkObject + NetworkTransform + NetworkGrabbableSync + HintBallNetworkSync
///        - 각 CarpetLauncher → NetworkObject + NetworkTransform + NetworkGrabbableSync (잡기 동기화)
///        - CarpetGoalZone → NetworkObject + NetworkEventRelay (도착 → 컨트롤러 솔브를 양쪽에 전파)
///        - 모든 추가 NetworkObject 에 AllowStateAuthorityOverride ON
///
/// 안전장치: Undo 가능. 부모에 NetworkObject 가 있는 중첩 후보는 건너뛰고 리포트(중첩 NetworkObject 금지).
/// 적용 후 반드시 씬 저장 → Fusion 이 NetworkedBehaviours/프리팹 테이블을 재베이킹.
/// </summary>
public static class Stage3SyncSetupTool
{
    const string Root = "Tools/Network/Stage3/";
    const string PrefabPath = "Assets/Prefab/Stage3/Carpet_Net.prefab";
    const string MatPath = "Assets/Prefab/Stage3/Carpet_NetMat.mat";

    static readonly Vector3 CarpetVisualScale = new Vector3(0.9f, 0.02f, 1.2f);

    // ──────────────────────────────────────────────────────────────────────
    //  1) Dry-Run Report
    // ──────────────────────────────────────────────────────────────────────
    [MenuItem(Root + "1) Dry-Run Report (변경 없음)")]
    public static void DryRun()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Stage3-Sync] Dry-Run — 변경 없음. 'Apply to Scene' 시 처리될 항목:\n");

        var balls = Object.FindObjectsByType<HintBall>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var launchers = Object.FindObjectsByType<CarpetLauncher>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dispensers = Object.FindObjectsByType<CarpetDispenser>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var goals = Object.FindObjectsByType<CarpetGoalZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var boards = Object.FindObjectsByType<HintPuzzleBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var ctrls = Object.FindObjectsByType<DisappearingCarpetController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        int nested = 0;
        foreach (var b in balls)
            sb.AppendLine($"  [HintBall]{(IsNested(b.gameObject) ? " (중첩-건너뜀)" : "")}  {Path(b.gameObject)}");
        foreach (var l in launchers)
            sb.AppendLine($"  [CarpetLauncher]{(IsNested(l.gameObject) ? " (중첩-건너뜀)" : "")}  {Path(l.gameObject)}");
        foreach (var g in goals)
            sb.AppendLine($"  [CarpetGoalZone]{(IsNested(g.gameObject) ? " (중첩-건너뜀)" : "")}  {Path(g.gameObject)}");
        nested = balls.Count(x => IsNested(x.gameObject)) + launchers.Count(x => IsNested(x.gameObject)) + goals.Count(x => IsNested(x.gameObject));

        sb.AppendLine();
        sb.AppendLine($"HintBall {balls.Length} | CarpetLauncher {launchers.Length} | CarpetDispenser {dispensers.Length} (네트워크 매니저가 관리) | GoalZone {goals.Length} | Board {boards.Length} | Controller {ctrls.Length}");
        sb.AppendLine($"중첩(건너뜀) {nested} | Carpet 프리팹 존재: {(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)}");
        sb.AppendLine("\n권장 순서: 2) Build Carpet Prefab → 3) Apply to Scene → 씬 저장(Ctrl+S) → 2인 플레이 검증.");
        Debug.Log(sb.ToString());
    }

    // ──────────────────────────────────────────────────────────────────────
    //  2) Build Carpet Prefab
    // ──────────────────────────────────────────────────────────────────────
    [MenuItem(Root + "2) Build Carpet Prefab")]
    public static GameObject BuildCarpetPrefab()
    {
        EnsureFolder("Assets/Prefab");
        EnsureFolder("Assets/Prefab/Stage3");

        // 머티리얼.
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = "Carpet_NetMat" };
            var col = new Color(0.70f, 0.45f, 0.25f);
            mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            // 살짝 빛나도록 emissive 켜기.
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", new Color(0.6f, 0.36f, 0.16f));
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(mat, MatPath);
        }

        // 임시 루트 빌드.
        var root = new GameObject("Carpet_Net");

        var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vis.name = "Visual";
        var visCol = vis.GetComponent<Collider>();
        if (visCol != null) Object.DestroyImmediate(visCol);
        vis.transform.SetParent(root.transform, false);
        vis.transform.localPosition = Vector3.zero;
        vis.transform.localScale = CarpetVisualScale;
        vis.GetComponent<Renderer>().sharedMaterial = mat;

        var bcol = root.AddComponent<BoxCollider>();
        bcol.size = CarpetVisualScale;

        // 배터리(검증된 양방향 그랩)와 동일하게 dynamic Rigidbody + NetworkGrabbableSync 를 쓴다.
        // dynamic 이어야 NGS 의 권위 이전·양방향 이동과 XRGrab 던지기(throwOnDetach)가 정상 동작한다.
        // "디스펜서 위 고정"은 kinematic 이 아니라 RefreshPhysics 의 FreezeAll constraints 로 처리(드리프트 방지).
        // damping 0 으로 둬 던진 거리가 죽지 않게 한다.
        var rb = root.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = root.AddComponent<XRGrabInteractable>();
        grab.throwOnDetach = true;
        grab.movementType = XRGrabInteractable.MovementType.VelocityTracking;
        grab.smoothPosition = false;
        grab.smoothRotation = false;

        var no = root.AddComponent<NetworkObject>();
        root.AddComponent<NetworkTransform>();

        // 잡기/이동/던지기/권위이전 — 배터리와 동일한 검증된 경로. 던지기 허용.
        var ngs = root.AddComponent<NetworkGrabbableSync>();
        ngs.forceNoThrowOnDetach = false;

        var carpet = root.AddComponent<DisappearingCarpet>();
        carpet.VisualRenderer = vis.GetComponent<Renderer>();
        carpet.Lifetime = 5f;
        carpet.WarningSeconds = 1.5f;

        // 상태 전파 / 삭제(Despawn) / 발사 / 프록시 물리 게이팅.
        root.AddComponent<CarpetNetworkSync>();

        SetAllowOverride(no);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Stage3-Sync] Carpet 프리팹 생성/갱신: {PrefabPath}\n" +
                  "씬 저장 시 Fusion 이 NetworkPrefab 테이블에 등록합니다.", prefab);
        return prefab;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  3) Apply to Scene
    // ──────────────────────────────────────────────────────────────────────
    [MenuItem(Root + "3) Apply to Scene (full)")]
    public static void ApplyToScene()
    {
        if (!EditorUtility.DisplayDialog("Stage3 Apply",
            "Stage3 의 상호작용 오브젝트에 네트워크 동기화를 적용합니다.\n" +
            "· Carpet 프리팹이 없으면 자동 생성합니다.\n" +
            "· Undo 가능. 씬을 저장하기 전엔 디스크에 반영되지 않습니다.\n\n계속할까요?",
            "Apply", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Stage3 Network Sync Apply");
        int group = Undo.GetCurrentGroup();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? BuildCarpetPrefab();
        var prefabNo = prefab != null ? prefab.GetComponent<NetworkObject>() : null;

        var sb = new StringBuilder();
        sb.AppendLine("[Stage3-Sync] Apply 결과:");

        // ── 네트워크 매니저 루트 ────────────────────────────────────────────
        var mgr = Object.FindFirstObjectByType<Stage3CarpetNetwork>();
        if (mgr == null)
        {
            var mgrGo = new GameObject("Stage3CarpetNetwork");
            Undo.RegisterCreatedObjectUndo(mgrGo, "Create Stage3CarpetNetwork");
            var mno = Undo.AddComponent<NetworkObject>(mgrGo);
            SetAllowOverride(mno);
            mgr = Undo.AddComponent<Stage3CarpetNetwork>(mgrGo);
            sb.AppendLine("  + 루트 'Stage3CarpetNetwork' 생성 (NetworkObject + Stage3CarpetNetwork)");
        }
        else if (mgr.GetComponent<NetworkObject>() == null)
        {
            SetAllowOverride(Undo.AddComponent<NetworkObject>(mgr.gameObject));
        }
        if (mgr.carpetPrefab != prefabNo)
        {
            Undo.RecordObject(mgr, "Assign carpetPrefab");
            mgr.carpetPrefab = prefabNo;
            EditorUtility.SetDirty(mgr);
        }
        sb.AppendLine($"  · carpetPrefab 할당: {(prefabNo != null ? prefab.name : "null!")}");

        // ── HintBall ────────────────────────────────────────────────────────
        int balls = 0, ballSkip = 0;
        foreach (var b in Object.FindObjectsByType<HintBall>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsNested(b.gameObject)) { ballSkip++; sb.AppendLine($"  ! 중첩 건너뜀 [HintBall] {Path(b.gameObject)}"); continue; }
            EnsureGrabbable(b.gameObject, allowThrow: true);
            if (b.GetComponent<HintBallNetworkSync>() == null) Undo.AddComponent<HintBallNetworkSync>(b.gameObject);
            balls++;
        }
        sb.AppendLine($"  · HintBall 적용 {balls} (중첩 {ballSkip})");

        // ── CarpetLauncher (잡기 동기화) ─────────────────────────────────────
        int launchers = 0, lSkip = 0;
        foreach (var l in Object.FindObjectsByType<CarpetLauncher>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsNested(l.gameObject)) { lSkip++; sb.AppendLine($"  ! 중첩 건너뜀 [CarpetLauncher] {Path(l.gameObject)}"); continue; }
            EnsureGrabbable(l.gameObject, allowThrow: false);
            launchers++;
        }
        sb.AppendLine($"  · CarpetLauncher 적용 {launchers} (중첩 {lSkip})");

        // ── CarpetGoalZone → NetworkEventRelay → 컨트롤러 솔브 전파 ───────────
        var ctrl = Object.FindFirstObjectByType<DisappearingCarpetController>();
        int goals = 0;
        foreach (var g in Object.FindObjectsByType<CarpetGoalZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (IsNested(g.gameObject)) { sb.AppendLine($"  ! 중첩 건너뜀 [CarpetGoalZone] {Path(g.gameObject)}"); continue; }
            var no = g.GetComponent<NetworkObject>() ?? Undo.AddComponent<NetworkObject>(g.gameObject);
            SetAllowOverride(no);
            var relay = g.GetComponent<NetworkEventRelay>() ?? Undo.AddComponent<NetworkEventRelay>(g.gameObject);

            // goal.OnReached → relay.Relay
            WireOnce(g.OnReached, relay.Relay, "Relay");
            // relay.onRelayed → controller.HandleHintPuzzleSolvedExternal
            if (ctrl != null) WireOnce(relay.onRelayed, ctrl.HandleHintPuzzleSolvedExternal, "HandleHintPuzzleSolvedExternal");
            EditorUtility.SetDirty(g);
            goals++;
        }
        sb.AppendLine($"  · CarpetGoalZone 적용 {goals} (relay→{(ctrl != null ? "controller" : "컨트롤러 없음!")})");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        sb.AppendLine("\n완료. 반드시 씬을 저장하세요(Ctrl+S) — Fusion 이 NetworkedBehaviours/프리팹 테이블을 재베이킹합니다.");
        Debug.Log(sb.ToString());
    }

    // ──────────────────────────────────────────────────────────────────────
    //  적용 헬퍼
    // ──────────────────────────────────────────────────────────────────────
    static void EnsureGrabbable(GameObject go, bool allowThrow)
    {
        var no = go.GetComponent<NetworkObject>() ?? Undo.AddComponent<NetworkObject>(go);
        if (go.GetComponent<NetworkTransform>() == null) Undo.AddComponent<NetworkTransform>(go);
        var ngs = go.GetComponent<NetworkGrabbableSync>() ?? Undo.AddComponent<NetworkGrabbableSync>(go);
        if (allowThrow)
        {
            Undo.RecordObject(ngs, "ngs throw");
            ngs.forceNoThrowOnDetach = false; // 던져야 하는 오브젝트(공/카펫).
            EditorUtility.SetDirty(ngs);
        }
        SetAllowOverride(no);
        EditorUtility.SetDirty(go);
    }

    static void WireOnce(UnityEngine.Events.UnityEvent evt, UnityEngine.Events.UnityAction call, string methodName)
    {
        if (evt == null) return;
        for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            if (evt.GetPersistentMethodName(i) == methodName) return; // 이미 연결됨.
        UnityEventTools.AddPersistentListener(evt, call);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  유틸 (MainSceneSyncSetupTool 과 동일 패턴)
    // ──────────────────────────────────────────────────────────────────────
    // Fusion NetworkObjectFlags 의 "current version"(V1) 비트. 베이킹된 오브젝트(예: 정상 동작하는
    // Battery 프리팹 = 786433)는 이 비트를 가진다. 이 비트가 없으면 Fusion 이 플래그를 '구버전'으로 보고
    // AllowStateAuthorityOverride 를 무시해 → 게스트의 RequestStateAuthority 가 조용히 거부된다.
    // (PrefabUtility.SaveAsPrefabAsset 로 만든 프리팹은 Fusion 베이커가 안 돌아 이 비트가 빠질 수 있음.)
    const int VersionCurrentBit = 1 << 19; // 524288

    static bool HasAllowOverride(NetworkObject no) =>
        (no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) == NetworkObjectFlags.AllowStateAuthorityOverride;

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
                if ((prop.intValue & needBits) == needBits) return; // 이미 둘 다 설정됨.
                prop.intValue |= needBits;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(no);
                return;
            }
        }
        Debug.LogWarning($"[Stage3-Sync] {no.name} Flags property 를 못 찾음 — 인스펙터에서 'Allow State Authority Override' 직접 체크 필요.", no);
    }

    static bool IsNested(GameObject go)
    {
        if (go.GetComponent<NetworkObject>() != null) return false; // 이미 자기 NO 가 있으면 중첩 아님.
        var p = go.transform.parent;
        while (p != null)
        {
            if (p.GetComponent<NetworkObject>() != null) return true;
            p = p.parent;
        }
        return false;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        var name = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
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
