#if UNITY_EDITOR
using Fusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Battery 프리팹에 네트워크 잡기 동기화(NetworkGrabbableSync)를 일괄 적용하는 에디터 도구.
///
/// 왜 필요한가:
///   배터리는 디스펜서가 Runner.Spawn 으로 생성한다. NetworkBehaviour(잡기 동기화)는
///   런타임 AddComponent 로 못 붙인다(Fusion 베이크 필요 + Spawn 콜백은 권한자에서만 실행 →
///   프록시엔 안 생김). 따라서 반드시 "프리팹"에 미리 있어야 모든 피어가 동일하게 보유한다.
///
/// 이 도구는 프리팹을 열어 NetworkObject(+AllowStateAuthorityOverride) / NetworkTransform /
/// NetworkGrabbableSync 를 보장하고, 구버전 잡기 컴포넌트(GrabAuthorityHandover, GrabNetworkSyncPause)는
/// 제거한 뒤 저장한다. 저장 시 Fusion 이 NetworkedBehaviours 를 재베이크한다.
/// </summary>
public static class BatteryPrefabSyncTool
{
    // 디스펜서가 스폰하는 배터리 프리팹 경로. 다른 프리팹을 쓰면 여기에 추가.
    static readonly string[] BatteryPrefabPaths =
    {
        "Assets/Prefab/Stages/Stage1/Battery.prefab",
    };

    [MenuItem("Tools/Stage 1/Battery/Setup Battery Grab Sync")]
    public static void Setup()
    {
        int done = 0;
        foreach (var path in BatteryPrefabPaths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogWarning($"[BatteryPrefabSyncTool] 프리팹을 찾지 못함: {path}");
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var grab = root.GetComponent<XRGrabInteractable>();
                if (grab == null)
                {
                    Debug.LogError($"[BatteryPrefabSyncTool] {path} 에 XRGrabInteractable 이 없음 — 건너뜀.");
                    continue;
                }

                var no = root.GetComponent<NetworkObject>() ?? root.AddComponent<NetworkObject>();
                SetAllowOverride(no);

                if (root.GetComponent<NetworkTransform>() == null)
                    root.AddComponent<NetworkTransform>();

                // 구버전 잡기 컴포넌트 제거(충돌 방지).
                var gah = root.GetComponent<GrabAuthorityHandover>();
                if (gah != null) Object.DestroyImmediate(gah, true);
                var gnsp = root.GetComponent<Stage1.GrabNetworkSyncPause>();
                if (gnsp != null) Object.DestroyImmediate(gnsp, true);

                if (root.GetComponent<NetworkGrabbableSync>() == null)
                    root.AddComponent<NetworkGrabbableSync>();

                PrefabUtility.SaveAsPrefabAsset(root, path);
                done++;
                Debug.Log($"[BatteryPrefabSyncTool] 적용 완료: {path}\n" +
                          "  NetworkObject(+AllowStateAuthorityOverride) / NetworkTransform / NetworkGrabbableSync 보장.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BatteryPrefabSyncTool] 완료 — {done}개 프리팹 처리. 이제 양쪽 플레이어가 배터리를 끊김 없이 잡을 수 있습니다.");
    }

    static void SetAllowOverride(NetworkObject no)
    {
        var so = new SerializedObject(no);
        foreach (var name in new[] { "Flags", "_flags", "m_Flags", "_objectFlags" })
        {
            var prop = so.FindProperty(name);
            if (prop != null && prop.propertyType == SerializedPropertyType.Integer)
            {
                prop.intValue |= (int)NetworkObjectFlags.AllowStateAuthorityOverride;
                so.ApplyModifiedProperties();
                return;
            }
        }
        Debug.LogWarning($"[BatteryPrefabSyncTool] {no.name} Flags 프로퍼티를 못 찾음 — 인스펙터에서 'Allow State Authority Override' 직접 체크 필요.");
    }
}
#endif
