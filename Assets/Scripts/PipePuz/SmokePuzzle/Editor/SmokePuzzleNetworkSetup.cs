#if UNITY_EDITOR
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PipePuz.SmokePuzzle.EditorTools
{
    /// <summary>
    /// 연기 퍼즐 네트워크 동기화 일괄 셋업.
    ///   1) PipeAllPuzzleController(PipeSmokePuz) → NetworkObject + SmokeSolveNetworkSync
    ///      (누가 풀든 양쪽 다 연기가 사라지도록 풀림 상태를 RPC 로 전파)
    ///   2) SuppressionWheel("Valve") → NetworkObject(AllowStateAuthorityOverride) + SuppressionWheelNetworkSync
    ///      (한쪽에서 휠을 돌리면 양쪽 다 돌아가도록)
    ///
    /// 두 동기화 컴포넌트 모두 NetworkBehaviour 라서, 실행 후 반드시 씬을 저장해야
    /// Fusion 이 NetworkObject / NetworkedBehaviours 를 재베이킹한다.
    ///
    /// 메뉴: Tools/Stage 1/Smoke Puzzle Sync/...
    /// </summary>
    public static class SmokePuzzleNetworkSetup
    {
        const string MenuRoot = "Tools/Stage 1/Smoke Puzzle Sync/";

        [MenuItem(MenuRoot + "Diagnose")]
        public static void Diagnose()
        {
            int ctrl = 0, ctrlSynced = 0, wheel = 0, wheelSynced = 0;
            foreach (var c in Object.FindObjectsByType<PipeAllPuzzleController>(FindObjectsSortMode.None))
            {
                ctrl++;
                if (c.GetComponent<SmokeSolveNetworkSync>() != null) ctrlSynced++;
            }
            foreach (var w in Object.FindObjectsByType<SuppressionWheel>(FindObjectsSortMode.None))
            {
                wheel++;
                if (w.GetComponent<SuppressionWheelNetworkSync>() != null) wheelSynced++;
            }
            Debug.Log($"[SmokeSync:Diagnose] PipeAllPuzzleController {ctrl} (synced {ctrlSynced}) | " +
                      $"SuppressionWheel {wheel} (synced {wheelSynced})");
        }

        [MenuItem(MenuRoot + "Setup All")]
        public static void SetupAll()
        {
            int ctrlDone = 0, wheelDone = 0;

            foreach (var c in Object.FindObjectsByType<PipeAllPuzzleController>(FindObjectsSortMode.None))
            {
                EnsureNetworkObject(c.gameObject, allowOverride: false);
                if (c.GetComponent<SmokeSolveNetworkSync>() == null)
                    Undo.AddComponent<SmokeSolveNetworkSync>(c.gameObject);
                EditorUtility.SetDirty(c.gameObject);
                ctrlDone++;
            }

            foreach (var w in Object.FindObjectsByType<SuppressionWheel>(FindObjectsSortMode.None))
            {
                // 휠은 잡는 피어가 권위를 강탈할 수 있어야 하므로 AllowStateAuthorityOverride 필요.
                EnsureNetworkObject(w.gameObject, allowOverride: true);
                if (w.GetComponent<SuppressionWheelNetworkSync>() == null)
                    Undo.AddComponent<SuppressionWheelNetworkSync>(w.gameObject);
                EditorUtility.SetDirty(w.gameObject);
                wheelDone++;
            }

            Finish($"[SmokeSync:Setup All] PipeAllPuzzleController {ctrlDone} | SuppressionWheel {wheelDone}");
        }

        static void EnsureNetworkObject(GameObject go, bool allowOverride)
        {
            var no = go.GetComponent<NetworkObject>();
            if (no == null) no = Undo.AddComponent<NetworkObject>(go);
            if (allowOverride) TrySetAllowOverride(no);
        }

        static void Finish(string summary)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(summary + "  — 씬을 저장하면 Fusion 이 NetworkObject/NetworkedBehaviours 를 재베이킹합니다(필수).");
        }

        // Fusion 빌드별 Flags 프로퍼티 이름 차이를 흡수.
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
            Debug.LogWarning($"[SmokeSync] {no.name} Flags property 못 찾음 — 인스펙터에서 'Allow State Authority Override' 직접 체크 필요.", no);
        }
    }
}
#endif
