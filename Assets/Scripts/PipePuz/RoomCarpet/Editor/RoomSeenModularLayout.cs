using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Stage3/Rebuild Modular Room as RoomCliff Shape.
    ///
    /// RoomSeen 안의 "Room (Stage1 Modular)" 를 지우고, RoomCliff 의 평면도
    /// (Entrance + Corridor + Left/Right Chamber + ChamberDivider + SecondFloor + Stairs) 를
    /// Stage1 Barking_Dog 모듈러 프리팹(Floor_01, Wall_Simple_01, Roof_01, Door_Arch_01) +
    /// ParticlePack 의 Stairs.prefab 으로 다시 깐다.
    ///
    /// 모든 모듈러 프리팹은 BoxCollider/MeshCollider 를 갖고 있어 물리적으로 막힌다
    /// (벽 관통 X, 바닥/계단 위로 걸을 수 있음). XR Grab 같은 "잡을 수 있는" 상호작용은
    /// 부여하지 않는다 — 그건 원본 RoomCliff 의 퍼즐 스크립트(CliffController, AutoSlidingDoor,
    /// LightOrb 등) 가 담당하며, 본 모듈러 룸은 시각/콜리전 셸 역할만 한다.
    ///
    /// 양면 렌더링 (Stage3 한정):
    ///   - 원본 Diffuse_01.mat (_Cull: 2, 단면) 은 Stage1 도 같이 쓰므로 직접 수정 X.
    ///   - Diffuse_01_DoubleSided.mat (_Cull: 0, 양면) 를 자동 생성하고 빌더가 RoomSeen 내 모든
    ///     Floor/Wall/Roof/Door 인스턴스의 sharedMaterials 에 swap. Stage1 영향 0.
    ///
    /// 그리드 결정:
    ///   - 모든 모듈러 피스는 3m × 3m × 3m 단위.
    ///   - RoomCliff 의 원본 치수를 3m 의 배수로 라운딩(보수적으로 round-out — 살짝 큰 쪽).
    ///   - 챔버 높이 5m 는 Wall_Simple_01 의 Y 스케일 1.667 로 늘려 표현.
    ///
    /// 라운딩 결과:
    ///   Entrance       X[-3, +3]    Z[-3, 0]   H=3
    ///   Corridor       X[-24, +9]   Z[0, 3]    H=3   (원본 [-22, +7] 에서 ±2 라운딩)
    ///   LeftChamber    X[-24, +3]   Z[3, 18]   H=5
    ///   RightChamber   X[+3, +9]    Z[3, 15]   H=5
    ///   SecondFloor    X[+3, +9]    Z[9, 15]   Y=3.5
    ///   ChamberDivider X=+3 (LeftChamber.east = RightChamber.west, Z[3, 15])
    ///   LeftDoor 개구  X=-4.5 (LeftChamber 남쪽 벽)
    ///   RightDoor 개구 X=+4.5 (RightChamber 남쪽 벽)
    ///   Entrance→Corridor 개구  X[-3, +3] (Corridor 남쪽 벽)
    ///
    /// 벽 소유권 (중복 벽 방지):
    ///   - Z=-3  : Entrance 남쪽
    ///   - Z=0   : Corridor 남쪽 (Entrance 개구)
    ///   - Z=3   : 챔버 남쪽 (LeftDoor / RightDoor 개구)
    ///   - Z=15  : RightChamber 북쪽
    ///   - Z=18  : LeftChamber 북쪽
    ///   - X=-24 : Corridor/LeftChamber 서쪽
    ///   - X=-3, X=+3 (단 Entrance 옆): Entrance 좌우
    ///   - X=+3  Z[3,15] : RightChamber 서쪽 (=ChamberDivider)
    ///   - X=+3  Z[15,18]: LeftChamber 동쪽 (divider 위로 튀어나온 부분)
    ///   - X=+9  : RightChamber 동쪽 / Corridor 동쪽
    ///
    /// 사전조건: Stage3 active, RoomSeen 존재. RoomCliff 본체는 별도 (씬 루트 또는 어디든).
    /// </summary>
    public static class RoomSeenModularLayout
    {
        // ===== Prefab paths =====
        const string Floor01Path  = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs/Floor_01.prefab";
        const string Wall01Path   = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs/Wall_Simple_01.prefab";
        const string Roof01Path   = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs/Roof_02.prefab";
        const string DoorArchPath = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs/Door_Arch_01.prefab";
        const string StairsPath   = "Assets/UnityTechnologies/ParticlePack/Shared/Environment/Prefabs/Stairs.prefab";

        // ===== Material paths (양면 렌더링 — Stage3 RoomSeen 전용) =====
        // 원본 Diffuse_01.mat 은 _Cull: 2 (back-face culling 단면) — Stage1 도 같이 영향받으므로 직접 수정 X.
        // 대신 복사본 Diffuse_01_DoubleSided.mat (_Cull: 0) 을 자동 생성하고 빌더가 인스턴스에만 적용.
        const string SrcMaterialPath        = "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01.mat";
        const string DoubleSidedMaterialPath = "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01_DoubleSided.mat";

        // ===== Stairs (RightChamber 의 SecondFloor 로 올라가는 계단) =====
        // 원본 RoomCliff: StartZ=4.5, 10 steps, Riser=0.35, Depth=0.4, total height 3.5m, length 4m.
        // RoomSeen 모듈러: 라운딩된 RightChamber X[3,9] 안에 배치. StairCenterX = 6.
        const float StairCenterX = 6f;
        const float StairBaseZ   = 4.5f;   // 계단 남쪽 끝
        const float StairTargetY = 3.5f;   // 도달 높이 (SecondFloor Y)
        const float StairLengthZ = 4f;     // Z 방향 점유 길이
        const float StairWidthX  = 2.5f;

        // ===== Grid =====
        const float Tile = 3f;
        // 벽 높이 — 사용자 요청: 이전 대비 2배.
        //   Wall_Simple_01 본래 3m → 1F 벽 = 6m, 챔버 벽 = 10m.
        const float Wall1FScale    = 2f;        // 6m (이전 3m × 2)
        const float WallTallScale  = 10f / 3f;  // ≈3.333 → 10m (이전 5m × 2)

        // ===== Room rects (Xmin, Xmax, Zmin, Zmax) =====
        static readonly Vector4 Entrance     = new Vector4(-3, +3, -3, 0);
        static readonly Vector4 Corridor     = new Vector4(-24, +9, 0, 3);
        static readonly Vector4 LeftChamber  = new Vector4(-24, +3, +3, +18);
        static readonly Vector4 RightChamber = new Vector4(+3, +9, +3, +15);
        static readonly Vector4 SecondFloor  = new Vector4(+3, +9, +9, +15);

        const float CorridorEntranceOpeningXmin = -3f;
        const float CorridorEntranceOpeningXmax = +3f;
        const float LeftDoorCenterX  = -4.5f;
        const float RightDoorCenterX = +4.5f;
        const float SecondFloorY = 3.5f;
        const float ChamberHeight = 5f;

        // ===== Roof Y offset =====
        // Stage1 검증 결과: 'Roof' 부모 Y=0, Roof_02 인스턴스 Y=3.75 (3m 벽 룸).
        //   → Roof_02 의 anchor 가 곧 visual Y 위치. (Stage1 의 3.75 는 벽 위 0.75m 펜트하우스 offset 추정.)
        // 따라서 Roof Y = 벽 top. 벽 높이가 2배(6m, 10m) 로 바뀌었으므로:
        //   1F 천장 Y=6 (이전 0 또는 3 → 6)
        //   챔버 천장 Y=10 (이전 2 또는 7 → 10)
        const float RoofDefault3mTopOffset = 6f;   // 1F 벽 top (6m) 에 천장 배치
        const float RoofChamberTopOffset   = 10f;  // 챔버 벽 top (10m) 에 천장 배치

        [MenuItem("Tools/PipePuz/Stage3/Rebuild Modular Room as RoomCliff Shape")]
        public static void Rebuild()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            GameObject roomSeen = FindRoot(scene, "RoomSeen");
            if (roomSeen == null)
            {
                Debug.LogError("[ModularLayout] RoomSeen 이 없다. 먼저 'Build or Update RoomSeen' 메뉴 실행.");
                return;
            }

            GameObject floor01  = LoadPrefab(Floor01Path);
            GameObject wall01   = LoadPrefab(Wall01Path);
            GameObject roof01   = LoadPrefab(Roof01Path);
            GameObject doorArch = LoadPrefab(DoorArchPath);
            GameObject stairs   = LoadPrefab(StairsPath);   // 선택적 — 없으면 계단만 스킵.
            if (floor01 == null || wall01 == null || roof01 == null || doorArch == null) return;

            // 양면 머티리얼 보장 — Stage3 한정으로 Diffuse_01 의 양면 변형 생성/로드.
            Material srcMat = AssetDatabase.LoadAssetAtPath<Material>(SrcMaterialPath);
            Material doubleSidedMat = EnsureDoubleSidedMaterial(srcMat);
            if (srcMat == null || doubleSidedMat == null)
            {
                Debug.LogWarning("[ModularLayout] 양면 머티리얼 준비 실패 — 단면 렌더링으로 계속. " +
                                 $"원본: {SrcMaterialPath}");
            }
            _currentSrcMat = srcMat;
            _currentDsMat  = doubleSidedMat;

            Undo.SetCurrentGroupName("Rebuild Modular Room as RoomCliff Shape");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                // 1. 기존 "Room (Stage1 Modular)" 삭제.
                GameObject existing = FindChildByName(roomSeen.transform, "Room (Stage1 Modular)");
                if (existing != null)
                {
                    Undo.DestroyObjectImmediate(existing);
                }

                // 2. 새 Room + 4그룹.
                var room = CreateChild(roomSeen.transform, "Room (Stage1 Modular)");
                var floors    = CreateChild(room.transform, "Floors");
                var walls     = CreateChild(room.transform, "Walls");
                var roofs     = CreateChild(room.transform, "Roof");
                var doors     = CreateChild(room.transform, "Door");
                var stairsGrp = CreateChild(room.transform, "Stairs");

                // 3. Floors — 모든 방의 1층 바닥 + 2층 슬래브.
                TileFloor(floor01, floors.transform, Entrance,     0f, "Entrance");
                TileFloor(floor01, floors.transform, Corridor,     0f, "Corridor");
                TileFloor(floor01, floors.transform, LeftChamber,  0f, "LeftChamber");
                TileFloor(floor01, floors.transform, RightChamber, 0f, "RightChamber");
                TileFloor(floor01, floors.transform, SecondFloor,  SecondFloorY, "SecondFloor");

                // 4. Walls — 벽 소유권 규칙대로 한 번씩만.
                BuildWalls(wall01, walls.transform);

                // 5. Roofs — 1F 천장 + 챔버 천장.
                TileRoof(roof01, roofs.transform, Entrance,     RoofDefault3mTopOffset, "Entrance");
                TileRoof(roof01, roofs.transform, Corridor,     RoofDefault3mTopOffset, "Corridor");
                TileRoof(roof01, roofs.transform, LeftChamber,  RoofChamberTopOffset,   "LeftChamber");
                TileRoof(roof01, roofs.transform, RightChamber, RoofChamberTopOffset,   "RightChamber");

                // 6. Door arches (시각용 — 문틀만, 실제 sliding 문은 원본 RoomCliff 의 것 사용).
                // 문틀은 바닥 Y=0 에 둠 — 사람 키 기준 출입 위치는 동일. 천장이 높아진 건 문 위 공백.
                PlaceDoorArch(doorArch, doors.transform, LeftDoorCenterX,  3f, 0f,  "LeftDoor");
                PlaceDoorArch(doorArch, doors.transform, RightDoorCenterX, 3f, 0f,  "RightDoor");
                PlaceDoorArch(doorArch, doors.transform, 0f,               0f, 0f,  "EntranceOpening");

                // 7. Stairs — Stairs.prefab (ParticlePack) 한 개. MeshCollider 있어 걸어서 올라갈 수 있음.
                if (stairs != null)
                {
                    PlaceStairs(stairs, stairsGrp.transform);
                }
                else
                {
                    Debug.LogWarning($"[ModularLayout] Stairs.prefab 못 찾음 ({StairsPath}). 계단 스킵. " +
                                     "ParticlePack 에셋이 설치되어 있는지 확인하라.");
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Selection.activeGameObject = room;
                EditorGUIUtility.PingObject(room);
                Debug.Log("[ModularLayout] 완료. RoomSeen/Room (Stage1 Modular) 를 RoomCliff 평면도로 재구성했다.\n" +
                          "확인 후 Ctrl+S 저장. 문제 시 Ctrl+Z.");
            }
            finally
            {
                _currentSrcMat = null;
                _currentDsMat  = null;
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        // =========================================================================================
        // Floor tiling
        // =========================================================================================

        static void TileFloor(GameObject prefab, Transform parent, Vector4 rect, float y, string roomName)
        {
            // Floor_01 prefab 의 BoxCollider center = (-1.5, -1.5, 0), size = (3, 3, 0.02).
            // 자식 transform 의 -90° X-rotation 적용 후 anchor 는 타일의 "NE 코너", 타일은 anchor 의
            // SW 방향(X-3, Z-3) 으로 확장. 따라서 anchor.x = xmin + Tile*(i+1), anchor.z = zmin + Tile*(j+1).
            float xmin = rect.x, xmax = rect.y, zmin = rect.z, zmax = rect.w;
            int cols = Mathf.RoundToInt((xmax - xmin) / Tile);
            int rows = Mathf.RoundToInt((zmax - zmin) / Tile);
            var group = CreateChild(parent, $"Floor_{roomName}");
            for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                float ax = xmin + Tile * (i + 1);
                float az = zmin + Tile * (j + 1);
                var t = InstantiatePrefab(prefab, group.transform);
                t.transform.localPosition = new Vector3(ax, y, az);
                t.transform.localRotation = Quaternion.identity;
                t.transform.localScale = Vector3.one;
            }
        }

        // =========================================================================================
        // Roof tiling — same as floor but at top of room, with Y offset for the prefab's built-in
        // ceiling-height anchor.
        // =========================================================================================

        static void TileRoof(GameObject prefab, Transform parent, Vector4 rect, float yOffset, string roomName)
        {
            // Roof_01 도 Stage1 안에서 Floor_01 와 동일한 그리드(3m × 3m) 로 깔리므로 NE 코너 anchor 가정.
            // 만약 천장이 어긋나 보이면 ax/az 공식의 (i+1)/(j+1) 을 (i+0.5)/(j+0.5) 로 바꾸면 1.5m 시프트됨.
            float xmin = rect.x, xmax = rect.y, zmin = rect.z, zmax = rect.w;
            int cols = Mathf.RoundToInt((xmax - xmin) / Tile);
            int rows = Mathf.RoundToInt((zmax - zmin) / Tile);
            var group = CreateChild(parent, $"Roof_{roomName}");
            for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                float ax = xmin + Tile * (i + 1);
                float az = zmin + Tile * (j + 1);
                var t = InstantiatePrefab(prefab, group.transform);
                t.transform.localPosition = new Vector3(ax, yOffset, az);
                t.transform.localRotation = Quaternion.identity;
                t.transform.localScale = Vector3.one;
            }
        }

        // =========================================================================================
        // Walls — 벽 소유권 규칙대로 한 번씩만 빌드.
        // =========================================================================================

        static void BuildWalls(GameObject prefab, Transform parent)
        {
            // ---- Entrance ----
            var e = CreateChild(parent, "Wall_Entrance");
            BuildXWall(prefab, e.transform, Entrance.x, Entrance.y, Entrance.z,                  Wall1FScale, "S");
            BuildZWall(prefab, e.transform, Entrance.z, Entrance.w, Entrance.x,                  Wall1FScale, "W");
            BuildZWall(prefab, e.transform, Entrance.z, Entrance.w, Entrance.y,                  Wall1FScale, "E");
            // (N 은 Corridor 쪽이 처리 — 개구)

            // ---- Corridor ----
            var c = CreateChild(parent, "Wall_Corridor");
            // S wall (Z=0) — Entrance 와 만나는 X[-3, +3] 은 개구.
            BuildXWallWithOpenings(prefab, c.transform, Corridor.x, Corridor.y, Corridor.z, Wall1FScale, "S",
                CorridorEntranceOpeningXmin, CorridorEntranceOpeningXmax);
            // N wall — 챔버 쪽이 처리.
            BuildZWall(prefab, c.transform, Corridor.z, Corridor.w, Corridor.x, Wall1FScale, "W");
            BuildZWall(prefab, c.transform, Corridor.z, Corridor.w, Corridor.y, Wall1FScale, "E");

            // ---- LeftChamber ----
            var l = CreateChild(parent, "Wall_LeftChamber");
            // S wall (Z=3) — LeftDoor 개구 (X=-4.5 중심, 한 타일).
            BuildXWallWithOpenings(prefab, l.transform, LeftChamber.x, LeftChamber.y, LeftChamber.z, WallTallScale, "S",
                LeftDoorCenterX - Tile * 0.5f, LeftDoorCenterX + Tile * 0.5f);
            // N wall (Z=18).
            BuildXWall(prefab, l.transform, LeftChamber.x, LeftChamber.y, LeftChamber.w, WallTallScale, "N");
            // W wall (X=-24).
            BuildZWall(prefab, l.transform, LeftChamber.z, LeftChamber.w, LeftChamber.x, WallTallScale, "W");
            // E wall (X=+3) — 단, Z[3,15] 는 RightChamber 가 처리 (divider). Z[15,18] 만 LeftChamber 처리.
            BuildZWall(prefab, l.transform, /*zmin*/ RightChamber.w, /*zmax*/ LeftChamber.w, LeftChamber.y, WallTallScale, "E_NorthSection");

            // ---- RightChamber ----
            var r = CreateChild(parent, "Wall_RightChamber");
            // S wall (Z=3) — RightDoor 개구.
            BuildXWallWithOpenings(prefab, r.transform, RightChamber.x, RightChamber.y, RightChamber.z, WallTallScale, "S",
                RightDoorCenterX - Tile * 0.5f, RightDoorCenterX + Tile * 0.5f);
            // N wall (Z=15).
            BuildXWall(prefab, r.transform, RightChamber.x, RightChamber.y, RightChamber.w, WallTallScale, "N");
            // E wall (X=+9).
            BuildZWall(prefab, r.transform, RightChamber.z, RightChamber.w, RightChamber.y, WallTallScale, "E");
            // W wall (X=+3) — chamber divider, Z[3, 15].
            BuildZWall(prefab, r.transform, RightChamber.z, RightChamber.w, RightChamber.x, WallTallScale, "W_Divider");
        }

        /// <summary>X 축을 따라 도는 east-west 벽. y 스케일로 높이 조절.</summary>
        static void BuildXWall(GameObject prefab, Transform parent, float xmin, float xmax, float z, float yScale, string label)
        {
            BuildXWallWithOpenings(prefab, parent, xmin, xmax, z, yScale, label, 0f, 0f);
        }

        /// <summary>X 축 벽 + 선택적 개구. anchor 는 세그먼트의 west 끝 (X 길이는 +X 방향).</summary>
        static void BuildXWallWithOpenings(GameObject prefab, Transform parent, float xmin, float xmax, float z, float yScale,
                                           string label, float openingXmin, float openingXmax)
        {
            int n = Mathf.RoundToInt((xmax - xmin) / Tile);
            for (int i = 0; i < n; i++)
            {
                float ax = xmin + i * Tile;        // segment 의 west 끝 (anchor)
                float segMin = ax;
                float segMax = ax + Tile;
                bool inOpening = openingXmax > openingXmin &&
                                 segMin >= openingXmin - 0.01f && segMax <= openingXmax + 0.01f;
                if (inOpening) continue;
                var w = InstantiatePrefab(prefab, parent);
                w.name = $"Wall_X_{label}_{i}";
                w.transform.localPosition = new Vector3(ax, 0f, z);
                w.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                w.transform.localScale = new Vector3(1f, yScale, 1f);
            }
        }

        /// <summary>Z 축 벽 (north-south). anchor 는 세그먼트의 south 끝 (Z 길이는 +Z 방향).</summary>
        static void BuildZWall(GameObject prefab, Transform parent, float zmin, float zmax, float x, float yScale, string label)
        {
            int n = Mathf.RoundToInt((zmax - zmin) / Tile);
            for (int i = 0; i < n; i++)
            {
                float az = zmin + i * Tile;
                var w = InstantiatePrefab(prefab, parent);
                w.name = $"Wall_Z_{label}_{i}";
                w.transform.localPosition = new Vector3(x, 0f, az);
                w.transform.localRotation = Quaternion.identity;
                w.transform.localScale = new Vector3(1f, yScale, 1f);
            }
        }

        // =========================================================================================
        // Door arches
        // =========================================================================================

        static void PlaceDoorArch(GameObject prefab, Transform parent, float x, float z, float y, string label)
        {
            var a = InstantiatePrefab(prefab, parent);
            a.name = $"DoorArch_{label}";
            a.transform.localPosition = new Vector3(x, y, z);
            a.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // 가로 방향 문틀
            a.transform.localScale = Vector3.one;
        }

        // =========================================================================================
        // Stairs — ParticlePack Stairs.prefab. MeshCollider(Convex) 가 붙어있어 걸어서 올라갈 수 있다.
        // 원본 prefab 의 anchor 가 mesh 중앙 부근(local 5, 2.5, 0) 이라 정확한 정렬은 시각 확인 필요.
        // 필요한 height 3.5m / length 4m / width 2.5m 에 맞춰 스케일 조정.
        // =========================================================================================

        static void PlaceStairs(GameObject prefab, Transform parent)
        {
            var s = InstantiatePrefab(prefab, parent);
            s.name = "Stairs_To_SecondFloor";
            // 계단을 RightChamber 안에 배치. Z 는 StairBaseZ 에서 시작해 +Z 로 올라감.
            // Stairs.prefab 의 본래 footprint 를 모른 채 단일 인스턴스로 떨군 뒤 X/Z 스케일로 길이/폭 강제.
            // (원본 prefab footprint 는 약 10×5 단위로 보임 — anchor offset 5,2.5,0 기준)
            // 일단 sane 한 기본값으로 떨군다. 시각적으로 안 맞으면 Inspector 에서 조정.
            s.transform.localPosition = new Vector3(StairCenterX, 0f, StairBaseZ);
            s.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            // 기본 prefab footprint 가 약 10m × 5m × ?? 라 추정 (정확치 X). 사용자가 보고 정정.
            // 일단 1x 스케일로 두고 사용자가 직접 맞추도록.
            s.transform.localScale = Vector3.one;
        }

        // =========================================================================================
        // Helpers
        // =========================================================================================

        static GameObject LoadPrefab(string path)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null) Debug.LogError($"[ModularLayout] Prefab not found at: {path}");
            return p;
        }

        // Rebuild() 가 진행 중인 동안 현재 사용 중인 머티리얼 페어 보관. 멀티스레딩 X (Editor only).
        static Material _currentSrcMat;
        static Material _currentDsMat;

        static GameObject InstantiatePrefab(GameObject prefab, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(go, "Place modular piece");
            // 양면 머티리얼 적용 (있을 때만, ParticlePack Stairs 등 외부 에셋은 src 와 무관해 영향 없음).
            if (_currentSrcMat != null && _currentDsMat != null)
            {
                ApplyDoubleSidedMaterial(go, _currentSrcMat, _currentDsMat);
            }
            return go;
        }

        /// <summary>
        /// 지정 GameObject(+자식) 의 모든 MeshRenderer 에서 srcMat 을 dsMat 으로 swap.
        /// sharedMaterials 사용 — 인스턴스별 material 사본 안 만듦 (성능/메모리 보존).
        /// </summary>
        static void ApplyDoubleSidedMaterial(GameObject go, Material srcMat, Material dsMat)
        {
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == srcMat)
                    {
                        mats[i] = dsMat;
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Diffuse_01_DoubleSided.mat 이 없으면 원본 Diffuse_01.mat 복사 후 _Cull=0 으로 저장.
        /// 이미 있으면 그대로 반환. Stage1 의 원본은 절대 안 건드림.
        /// </summary>
        static Material EnsureDoubleSidedMaterial(Material src)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(DoubleSidedMaterialPath);
            if (existing != null) return existing;
            if (src == null) return null;

            var copy = new Material(src);
            // _Cull: 0 = Off (양면), 1 = Front cull, 2 = Back cull (기본 단면).
            if (copy.HasProperty("_Cull")) copy.SetFloat("_Cull", 0f);
            // URP Lit 의 RenderFace 키워드도 같이 설정 (Both = both faces).
            copy.SetOverrideTag("RenderType", copy.GetTag("RenderType", false));
            // URP shader keyword 일관성 위해 — _BUILTIN_RenderFace 가 있으면 토글.
            // (셰이더에 따라 키워드명 다름 — 안전하게 시도만 함.)
            AssetDatabase.CreateAsset(copy, DoubleSidedMaterialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ModularLayout] 양면 머티리얼 생성: {DoubleSidedMaterialPath}");
            return copy;
        }

        static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        static void EditorSceneManager_MarkActiveSceneDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        static bool ValidateStage3(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[ModularLayout] Active scene 이 유효하지 않다.");
                return false;
            }
            if (!scene.name.Contains("Stage3"))
            {
                if (!EditorUtility.DisplayDialog(
                        "Modular Layout",
                        $"현재 active scene '{scene.name}' 이 Stage3 가 아닐 수 있다.\n계속할까?",
                        "계속", "취소"))
                    return false;
            }
            return true;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name) return root;
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
