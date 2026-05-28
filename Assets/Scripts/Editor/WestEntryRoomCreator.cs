#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Stage1.Editor
{
    /// <summary>
    /// Stage 1 의 BeamGatedDoor_WestEntry 옆에 작은 방을 자동으로 생성하는 Editor 툴.
    ///
    /// - Wall_Corridor 의 child 로 새 방 컨테이너(Room_WestEntry) 를 만들고
    ///   기존 corridor / chamber 와 동일한 모듈러 키트 프리팹
    ///   (Wall_Simple_01 / Floor_01 / Roof_01) 으로 6m x 9m 의 작은 방을 구성한다.
    ///
    /// - 사용 좌표계는 Wall_Corridor local space (door 가 (-24, 0, 0) 에 위치하는 공간).
    ///   따라서 Stage 3 의 -90도 회전 / world offset 을 신경쓰지 않아도 된다.
    ///
    /// - 좌표 컨벤션 (모듈러 키트 자체에 의해 결정됨):
    ///     Floor_01 at (xP, 0, zP)            → 타일 영역  X=[xP-3, xP],  Z=[zP, zP+3]
    ///     Wall_Simple_01 at (xP, 0, zP) rotY=0  → 벽 영역  X=[xP-0.25, xP], Z=[zP, zP+3]
    ///     Wall_Simple_01 at (xP, 0, zP) rotY=90 → 벽 영역  X=[xP, xP+3],    Z=[zP, zP+0.25]
    /// </summary>
    public static class WestEntryRoomCreator
    {
        // 모듈러 키트 프리팹 GUID
        const string WallGuid  = "a4c18952442944083b1fddc06ba477c2"; // Wall_Simple_01
        const string FloorGuid = "c76d2d3504ddb4847940a815cad04644"; // Floor_01
        const string RoofGuid  = "1a5e2f40365fe44ada271dd97ea7506f"; // Roof_01

        const string TargetDoorName = "BeamGatedDoor_WestEntry";
        const string NewRoomName    = "Room_WestEntry";
        const float  TileSize       = 3f;
        const float  WallScaleY     = 1.25f; // 기존 corridor 벽과 동일

        [MenuItem("Tools/Stage 1/Spawn West Entry Room")]
        public static void SpawnRoom()
        {
            // 1) door 찾기
            GameObject door = FindGameObjectByName(TargetDoorName);
            if (door == null)
            {
                EditorUtility.DisplayDialog(
                    "Spawn West Entry Room",
                    $"씬에서 '{TargetDoorName}' 를 찾지 못했습니다.\n" +
                    "Stage 1 씬을 열어둔 상태에서 다시 실행하세요.",
                    "OK");
                return;
            }

            // 2) 모듈러 프리팹 로드
            GameObject wallPrefab  = LoadPrefab(WallGuid,  "Wall_Simple_01");
            GameObject floorPrefab = LoadPrefab(FloorGuid, "Floor_01");
            GameObject roofPrefab  = LoadPrefab(RoofGuid,  "Roof_01");
            if (wallPrefab == null || floorPrefab == null || roofPrefab == null)
                return;

            // 3) 이미 방이 있다면 한번 물어보고 덮어쓰기
            Transform corridor = door.transform.parent; // Wall_Corridor
            if (corridor == null)
            {
                EditorUtility.DisplayDialog(
                    "Spawn West Entry Room",
                    $"'{TargetDoorName}' 의 부모(Wall_Corridor) 가 없습니다.",
                    "OK");
                return;
            }

            Transform existing = corridor.Find(NewRoomName);
            if (existing != null)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "Spawn West Entry Room",
                    $"이미 '{NewRoomName}' 이(가) 존재합니다. 다시 생성할까요?\n(기존 항목은 삭제됩니다)",
                    "다시 생성", "취소");
                if (!replace) return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            // 4) 좌표 계산 (Wall_Corridor local space)
            //    door.localPosition 은 (-24, 0, 0) 으로 가정하지만 실제 값을 그대로 사용해
            //    혹시 이동되었어도 같이 따라가도록 한다.
            Vector3 doorLP = door.transform.localPosition;
            float doorX = doorLP.x;
            float doorZ = doorLP.z;

            // 방 외곽: x = [doorX - 6, doorX], z = [doorZ - 6, doorZ + 3]  → 6m x 9m
            //   문(z=[doorZ, doorZ+3]) 이 east 벽의 북쪽에 오도록 z 음수 방향으로 늘림.
            //   chamber 가 z > doorZ+3 영역 (corridor-local z>3) 을 차지하고 있어
            //   북쪽으로는 문 폭만큼만 확장하고 나머지는 남쪽으로 확장한다.
            int xTiles = 2; // 6m
            int zTiles = 3; // 9m
            float xEast  = doorX;                       // -24
            float xWest  = doorX - xTiles * TileSize;   // -30
            float zSouth = doorZ - 2f * TileSize;       // -6
            float zNorth = doorZ + 1f * TileSize;       //  3

            // 5) 컨테이너 생성
            GameObject room = new GameObject(NewRoomName);
            Undo.RegisterCreatedObjectUndo(room, "Spawn West Entry Room");
            room.transform.SetParent(corridor, false);
            room.transform.localPosition = Vector3.zero;
            room.transform.localRotation = Quaternion.identity;
            room.transform.localScale    = Vector3.one;

            Transform floorRoot = MakeChild(room.transform, "Floor");
            Transform wallsRoot = MakeChild(room.transform, "Walls");
            Transform roofRoot  = MakeChild(room.transform, "Roof");

            // ===== Floor =====
            // Floor_01 at (xP, 0, zP) → covers X=[xP-3, xP], Z=[zP, zP+3]
            for (int i = 1; i <= xTiles; i++)
            {
                float xP = xWest + i * TileSize;     // -27, -24
                for (int j = 0; j < zTiles; j++)
                {
                    float zP = zSouth + j * TileSize; // -6, -3, 0
                    PlacePrefab(floorPrefab, floorRoot,
                        new Vector3(xP, 0f, zP), Quaternion.identity, Vector3.one,
                        $"Floor_{i}_{j}");
                }
            }

            // ===== Walls =====
            Quaternion rotZWall = Quaternion.identity;          // Wall_Z_* (north-south 방향 벽)
            Quaternion rotXWall = Quaternion.Euler(0f, 90f, 0f); // Wall_X_* (east-west 방향 벽)

            // West wall (x=xWest, length in +Z) : rotY=0, 위치 z 는 +Z 끝이 아닌 -Z 시작점
            for (int j = 0; j < zTiles; j++)
            {
                float zP = zSouth + j * TileSize;
                PlaceWall(wallPrefab, wallsRoot,
                    new Vector3(xWest, 0f, zP), rotZWall,
                    $"Wall_W_{j}");
            }

            // East wall (x=xEast) : 문이 차지하는 z=[doorZ, doorZ+3] 구간은 비워둠.
            //   wall covers Z=[zP, zP+3]. 문 구간을 덮는 wall 의 zP 는 doorZ.
            for (int j = 0; j < zTiles; j++)
            {
                float zP = zSouth + j * TileSize;
                if (Mathf.Approximately(zP, doorZ))
                    continue;
                PlaceWall(wallPrefab, wallsRoot,
                    new Vector3(xEast, 0f, zP), rotZWall,
                    $"Wall_E_{j}");
            }

            // South wall (z=zSouth) : rotY=90, length in +X, position 은 -X 끝
            for (int i = 0; i < xTiles; i++)
            {
                float xP = xWest + i * TileSize;
                PlaceWall(wallPrefab, wallsRoot,
                    new Vector3(xP, 0f, zSouth), rotXWall,
                    $"Wall_S_{i}");
            }

            // North wall (z=zNorth)
            for (int i = 0; i < xTiles; i++)
            {
                float xP = xWest + i * TileSize;
                PlaceWall(wallPrefab, wallsRoot,
                    new Vector3(xP, 0f, zNorth), rotXWall,
                    $"Wall_N_{i}");
            }

            // ===== Roof =====
            // Roof_01 도 Floor_01 과 동일한 conv 으로 배치
            for (int i = 1; i <= xTiles; i++)
            {
                float xP = xWest + i * TileSize;
                for (int j = 0; j < zTiles; j++)
                {
                    float zP = zSouth + j * TileSize;
                    PlacePrefab(roofPrefab, roofRoot,
                        new Vector3(xP, 0f, zP), Quaternion.identity, Vector3.one,
                        $"Roof_{i}_{j}");
                }
            }

            // ===== Light =====
            // 방 중앙 (천장 가까이) 에 포인트 라이트 하나
            GameObject lightGo = new GameObject("Room_Light");
            Undo.RegisterCreatedObjectUndo(lightGo, "Spawn West Entry Room");
            lightGo.transform.SetParent(room.transform, false);
            float cx = (xWest + xEast) * 0.5f;
            float cz = (zSouth + zNorth) * 0.5f;
            lightGo.transform.localPosition = new Vector3(cx, 3.0f, cz);
            Light l = lightGo.AddComponent<Light>();
            l.type      = LightType.Point;
            l.range     = 10f;
            l.intensity = 1.6f;
            l.color     = new Color(1f, 0.92f, 0.78f);
            l.shadows   = LightShadows.Soft;

            // 선택해서 사용자에게 보여주기
            Selection.activeGameObject = room;
            EditorGUIUtility.PingObject(room);

            EditorSceneManagerMarkDirty(door);

            Debug.Log($"[WestEntryRoomCreator] '{NewRoomName}' 생성 완료. " +
                      $"문 local pos={doorLP}, 방 범위(corridor local): " +
                      $"X=[{xWest}, {xEast}], Z=[{zSouth}, {zNorth}].");
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        static GameObject LoadPrefab(string guid, string label)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog(
                    "Spawn West Entry Room",
                    $"'{label}' (GUID={guid}) 프리팹을 찾을 수 없습니다.\n" +
                    "Barking_Dog/3D Free Modular Kit 가 임포트되어 있는지 확인하세요.",
                    "OK");
                return null;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog(
                    "Spawn West Entry Room",
                    $"'{label}' 프리팹 로드 실패: {path}",
                    "OK");
            }
            return prefab;
        }

        static GameObject FindGameObjectByName(string name)
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go.name == name && go.scene.IsValid() && go.scene.isLoaded)
                    return go;
            }
            return null;
        }

        static Transform MakeChild(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Spawn West Entry Room");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;
            return go.transform;
        }

        static GameObject PlacePrefab(GameObject prefab, Transform parent, Vector3 localPos,
            Quaternion localRot, Vector3 localScale, string name)
        {
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(inst, "Spawn West Entry Room");
            inst.name = name;
            inst.transform.localPosition = localPos;
            inst.transform.localRotation = localRot;
            inst.transform.localScale    = localScale;
            return inst;
        }

        static GameObject PlaceWall(GameObject prefab, Transform parent, Vector3 localPos,
            Quaternion localRot, string name)
        {
            return PlacePrefab(prefab, parent, localPos, localRot,
                new Vector3(1f, WallScaleY, 1f), name);
        }

        static void EditorSceneManagerMarkDirty(GameObject anyGoInScene)
        {
            if (anyGoInScene == null) return;
            var scene = anyGoInScene.scene;
            if (scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
#endif
