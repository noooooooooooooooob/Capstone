#if UNITY_EDITOR
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PipePuz.MiniGame2.EditorTools
{
    /// <summary>
    /// 씬의 모든 PipeMiniGame2Pipe 에 PipeRotationSync(회전 네트워크 동기화)를 일괄 부착한다.
    ///
    /// PipeRotationSync 는 NetworkBehaviour 라서 부착 후 NetworkObject 의 NetworkedBehaviours
    /// 재베이킹이 필요한데, 이는 씬 저장 / 플레이 진입 시 Fusion 이 자동 수행한다.
    /// 따라서 실행 후 반드시 씬을 저장할 것.
    ///
    /// 메뉴: Tools/Stage 1/Pipe Rotation Sync/...
    /// </summary>
    public static class PipeRotationSyncSetup
    {
        const string MenuRoot = "Tools/Stage 1/Pipe Rotation Sync/";

        [MenuItem(MenuRoot + "Diagnose")]
        public static void Diagnose()
        {
            int total = 0, hasNo = 0, hasSync = 0, missingNo = 0;
            foreach (var pipe in Object.FindObjectsByType<PipeMiniGame2Pipe>(FindObjectsSortMode.None))
            {
                total++;
                if (pipe.GetComponent<NetworkObject>() == null) { missingNo++; continue; }
                hasNo++;
                if (pipe.GetComponent<PipeRotationSync>() != null) hasSync++;
            }
            Debug.Log($"[PipeRotSync:Diagnose] PipeMiniGame2Pipe {total} | NetworkObject 보유 {hasNo} | " +
                      $"이미 Sync 보유 {hasSync} | NetworkObject 없음(스킵) {missingNo}");
        }

        [MenuItem(MenuRoot + "Add To All Pipes")]
        public static void AddToAll()
        {
            int added = 0, already = 0, skipped = 0;
            foreach (var pipe in Object.FindObjectsByType<PipeMiniGame2Pipe>(FindObjectsSortMode.None))
            {
                if (AddOne(pipe, ref already)) added++;
                else if (pipe.GetComponent<NetworkObject>() == null) skipped++;
            }
            Finish($"[PipeRotSync:Add All] 추가 {added} | 이미 있음 {already} | 스킵(NO 없음) {skipped}");
        }

        [MenuItem(MenuRoot + "Add To Selected")]
        public static void AddToSelected()
        {
            int added = 0, already = 0, skipped = 0;
            foreach (var go in Selection.gameObjects)
            {
                var pipe = go.GetComponent<PipeMiniGame2Pipe>();
                if (pipe == null) { skipped++; continue; }
                if (AddOne(pipe, ref already)) added++;
                else if (pipe.GetComponent<NetworkObject>() == null) skipped++;
            }
            Finish($"[PipeRotSync:Add Selected] 추가 {added} | 이미 있음 {already} | 스킵 {skipped}");
        }

        [MenuItem(MenuRoot + "Remove From All Pipes")]
        public static void RemoveFromAll()
        {
            int removed = 0;
            foreach (var pipe in Object.FindObjectsByType<PipeMiniGame2Pipe>(FindObjectsSortMode.None))
            {
                var sync = pipe.GetComponent<PipeRotationSync>();
                if (sync != null) { Undo.DestroyObjectImmediate(sync); removed++; }
            }
            Finish($"[PipeRotSync:Remove All] 제거 {removed}");
        }

        static bool AddOne(PipeMiniGame2Pipe pipe, ref int already)
        {
            // NetworkObject 가 있어야 동기화 가능. (NetworkGrabbableSync 셋업 시 이미 부여됨)
            if (pipe.GetComponent<NetworkObject>() == null) return false;

            if (pipe.GetComponent<PipeRotationSync>() != null) { already++; return false; }

            Undo.AddComponent<PipeRotationSync>(pipe.gameObject);
            EditorUtility.SetDirty(pipe.gameObject);
            return true;
        }

        static void Finish(string summary)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(summary + "  — 씬을 저장하면 Fusion 이 NetworkedBehaviours 를 재베이킹합니다(필수).");
        }
    }
}
#endif
