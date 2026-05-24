using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// Stage3 의 RoomSeen 구축/갱신용 Editor 메뉴 (idempotent).
    ///
    /// 메뉴 한 번 누를 때마다 "있어야 할 것" 을 점검해서 빠진 것만 추가한다.
    /// 여러 번 눌러도 같은 결과 — 이미 있는 건 건드리지 않음.
    ///
    /// 최종 계층 (Stage3 씬 루트 기준):
    ///   ├── RoomCliff                      ← 원본, 손대지 않음 (씬 루트)
    ///   └── RoomSeen
    ///         ├── Room (Stage1 Modular)    ← Stage1 의 Floors/Walls/Roof/Door
    ///         └── RoomCliff (Stage1 Skin)  ← RoomCliff 복제본 - Architecture 자식
    ///
    /// 동작:
    ///   1. RoomCliff 가 RoomSeen 자식으로 잘못 들어가 있으면 자동으로 루트로 복원.
    ///   2. RoomSeen 이 없으면 만든다. 있으면 그대로 사용.
    ///   3. RoomSeen 안에 "Room (Stage1 Modular)" 이 없으면 Stage1 씬에서 Room 을 가져와 추가.
    ///   4. RoomSeen 안에 "RoomCliff (Stage1 Skin)" 이 없으면 원본 RoomCliff 복제 → Architecture 자식만 제거.
    /// </summary>
    public static class RoomSeenSetup
    {
        const string Stage1ScenePath = "Assets/Scenes/Scenes/Level Scenes/Stage 1.unity";
        const string SourceRoomName = "Room";
        const string ModularRoomName = "Room (Stage1 Modular)";
        const string TargetParentName = "RoomSeen";
        const string CliffName = "RoomCliff";
        const string CliffSkinName = "RoomCliff (Stage1 Skin)";
        const string ArchitectureChildName = "Architecture";

        [MenuItem("Tools/PipePuz/Stage3/Build or Update RoomSeen")]
        public static void BuildOrUpdate()
        {
            var stage3 = SceneManager.GetActiveScene();
            if (!ValidateStage3(stage3)) return;

            Undo.SetCurrentGroupName("Build or Update RoomSeen");
            int undoGroup = Undo.GetCurrentGroup();
            Scene stage1 = default;
            bool stage1Loaded = false;

            try
            {
                // ---- 0. RoomCliff 위치 정상화 (루트에 없으면 RoomSeen 자식에서 찾아서 루트로 복원) ----
                GameObject originalCliff = FindRoot(stage3, CliffName);
                GameObject roomSeenExisting = FindRoot(stage3, TargetParentName);

                if (originalCliff == null && roomSeenExisting != null)
                {
                    for (int i = 0; i < roomSeenExisting.transform.childCount; i++)
                    {
                        var c = roomSeenExisting.transform.GetChild(i);
                        if (c.name == CliffName)
                        {
                            Undo.SetTransformParent(c, null, worldPositionStays: true,
                                "Restore RoomCliff to scene root");
                            originalCliff = c.gameObject;
                            Debug.Log($"[RoomSeenSetup] '{CliffName}' 를 씬 루트로 복원했다 (원래 자리).");
                            break;
                        }
                    }
                }
                if (originalCliff == null)
                {
                    Debug.LogError($"[RoomSeenSetup] '{CliffName}' GameObject 를 씬에서 찾을 수 없다. Stage3 씬이 맞는지 확인하라.");
                    return;
                }

                // ---- 1. RoomSeen 보장 ----
                GameObject roomSeen = roomSeenExisting;
                bool roomSeenCreated = false;
                if (roomSeen == null)
                {
                    roomSeen = new GameObject(TargetParentName);
                    Undo.RegisterCreatedObjectUndo(roomSeen, "Create RoomSeen");
                    SceneManager.MoveGameObjectToScene(roomSeen, stage3);
                    roomSeen.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    roomSeen.transform.localScale = Vector3.one;
                    roomSeenCreated = true;
                    Debug.Log($"[RoomSeenSetup] '{TargetParentName}' 생성.");
                }

                // ---- 2. Room (Stage1 Modular) 보장 ----
                GameObject modular = FindChildByName(roomSeen.transform, ModularRoomName);
                bool modularCreated = false;
                if (modular == null)
                {
                    // Stage1 씬을 additive 로 잠깐 로드.
                    try
                    {
                        stage1 = EditorSceneManager.OpenScene(Stage1ScenePath, OpenSceneMode.Additive);
                        stage1Loaded = true;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[RoomSeenSetup] Stage1 씬 로드 실패: {Stage1ScenePath}\n{e}");
                        return;
                    }

                    GameObject sourceRoom = FindRoot(stage1, SourceRoomName);
                    if (sourceRoom == null)
                    {
                        Debug.LogError($"[RoomSeenSetup] Stage1 씬에 '{SourceRoomName}' 루트가 없다.");
                        return;
                    }

                    modular = Object.Instantiate(sourceRoom);
                    modular.name = ModularRoomName;
                    Undo.RegisterCreatedObjectUndo(modular, "Instantiate Stage1 Room");
                    if (modular.scene != stage3)
                        SceneManager.MoveGameObjectToScene(modular, stage3);
                    Undo.SetTransformParent(modular.transform, roomSeen.transform, worldPositionStays: true,
                        "Parent Room under RoomSeen");
                    modularCreated = true;
                    Debug.Log($"[RoomSeenSetup] '{ModularRoomName}' 추가.");
                }

                // ---- 3. RoomCliff (Stage1 Skin) 보장 ----
                GameObject skin = FindChildByName(roomSeen.transform, CliffSkinName);
                bool skinCreated = false;
                if (skin == null)
                {
                    skin = Object.Instantiate(originalCliff);
                    skin.name = CliffSkinName;
                    Undo.RegisterCreatedObjectUndo(skin, "Duplicate RoomCliff");
                    if (skin.scene != stage3)
                        SceneManager.MoveGameObjectToScene(skin, stage3);
                    Undo.SetTransformParent(skin.transform, roomSeen.transform, worldPositionStays: true,
                        "Parent CliffSkin under RoomSeen");

                    // Architecture (cube 벽/바닥) 만 제거.
                    Transform architecture = null;
                    for (int i = 0; i < skin.transform.childCount; i++)
                    {
                        var c = skin.transform.GetChild(i);
                        if (c.name == ArchitectureChildName)
                        {
                            architecture = c;
                            break;
                        }
                    }
                    if (architecture != null)
                    {
                        Undo.DestroyObjectImmediate(architecture.gameObject);
                        Debug.Log($"[RoomSeenSetup] '{CliffSkinName}' 에서 '{ArchitectureChildName}' 제거.");
                    }
                    else
                    {
                        Debug.LogWarning($"[RoomSeenSetup] '{CliffSkinName}' 에서 '{ArchitectureChildName}' 자식을 못 찾았다. 수동 정리 필요.");
                    }
                    skinCreated = true;
                    Debug.Log($"[RoomSeenSetup] '{CliffSkinName}' 추가.");
                }

                EditorSceneManager.MarkSceneDirty(stage3);
                Selection.activeGameObject = roomSeen;
                EditorGUIUtility.PingObject(roomSeen);

                string summary =
                    $"[RoomSeenSetup] 완료.\n" +
                    $"  RoomSeen           : {(roomSeenCreated ? "신규 생성" : "기존 사용")}\n" +
                    $"  Room (Stage1 Mod)  : {(modularCreated ? "신규 추가" : "기존 유지")}\n" +
                    $"  RoomCliff Skin     : {(skinCreated ? "신규 추가" : "기존 유지")}\n" +
                    $"확인 후 Ctrl+S 로 저장. 문제 시 Ctrl+Z 한 번.";
                Debug.Log(summary);
            }
            finally
            {
                if (stage1Loaded && stage1.IsValid() && stage1.isLoaded)
                {
                    EditorSceneManager.CloseScene(stage1, removeScene: true);
                }
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        // =========================================================================================
        // Helpers
        // =========================================================================================

        static bool ValidateStage3(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[RoomSeenSetup] Active scene 이 유효하지 않다.");
                return false;
            }
            if (!scene.name.Contains("Stage3"))
            {
                if (!EditorUtility.DisplayDialog(
                        "RoomSeen Setup",
                        $"현재 active scene '{scene.name}' 이 Stage3 가 아닐 수 있다.\n계속할까?",
                        "계속", "취소"))
                    return false;
            }
            return true;
        }

        static GameObject FindRoot(Scene scene, string rootName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == rootName) return root;
            }
            return null;
        }

        static GameObject FindChildByName(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c.gameObject;
            }
            return null;
        }
    }
}
