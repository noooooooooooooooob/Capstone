using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Stage3/Build Layout.
    ///
    /// 평면도 기반 비대칭 협력 레벨 (top-down view):
    ///
    /// ```
    ///                  (z=14, north)
    ///   ┌──────────────────────┬────────────────┐
    ///   │                      │   2층 슬래브    │
    ///   │                      │  (y=3.5)        │
    ///   │  Left Chamber (P2)   │  카펫 던지는 곳   │
    ///   │  위험 바닥 + HintBall │                │
    ///   │  + HintBoard/Catcher ├────────────────┤
    ///   │                      │   계단(북쪽으로)  │
    ///   │                      │   y=0→3.5      │
    ///   ├──────[L door]────────┴───[R door]─────┤  (z=3)
    ///   │                                       │
    ///   │              Corridor                 │
    ///   ├─────────┬──────────────┬──────────────┤  (z=0)
    ///             │  Entrance    │
    ///             │  protrusion  │
    ///             └──────────────┘                 (z=-3)
    /// ```
    ///
    /// 챔버 사이 벽 (x = LeftChamberXmax = RightChamberXmin = +1.5):
    ///   - z ∈ [3, Floor2Zmin] : 천장까지 (y=0~RightChamberWallY) — 1층에서 두 챔버 완전 분리
    ///   - z ∈ [Floor2Zmin, 14] : 2층 바닥 높이까지 (y=0~Floor2Y) — 2층에서 P1 이 왼쪽 챔버를 내려다볼 수 있게
    ///
    /// 게임 플레이:
    ///   1. P2: 왼쪽 문 → StartZone → 위험 바닥 위 HintBall 수거 → 왼쪽 챔버 안의 HintCatcher 로 throw → 보드 슬롯 채움.
    ///   2. P1: 오른쪽 문 → 1층 계단 → 2층 → Dispenser/Launcher 로 왼쪽 챔버 쪽으로 카펫 발사.
    /// </summary>
    public static class Stage3LayoutSetup
    {
        // ===== Layout constants =====

        const float WallThickness = 0.2f;

        // -- 입구 돌출 (T-shape bottom)
        const float EntranceXmin = -3f;
        const float EntranceXmax = +3f;
        const float EntranceZmin = -3f;
        const float EntranceZmax = 0f;
        const float EntranceHeight = 3f;

        // -- 복도 (가로 복도)
        const float CorridorXmin = -12f;
        const float CorridorXmax = +7f;
        const float CorridorZmin = 0f;
        const float CorridorZmax = 3f;
        const float CorridorHeight = 3f;

        // -- 문 (복도 북쪽 벽 = 챔버 남쪽 벽에 두 개의 doorway)
        const float LeftDoorCenterX = -4f;
        const float RightDoorCenterX = +4f;
        const float DoorOpeningWidth = 2f;    // along X
        const float DoorHeight = 2.2f;
        const float DoorPanelThickness = 0.08f;

        // -- 왼쪽 챔버 (P2)
        const float LeftChamberXmin = -12f;
        const float LeftChamberXmax = +1.5f;
        const float LeftChamberZmin = +3f;
        const float LeftChamberZmax = +14f;
        const float LeftChamberWallY = 5f;

        // -- 오른쪽 챔버
        const float RightChamberXmin = +1.5f;
        const float RightChamberXmax = +7f;
        const float RightChamberZmin = +3f;
        const float RightChamberZmax = +14f;
        const float RightChamberWallY = 5f;

        // -- 2층 (오른쪽 챔버 북쪽 절반에 슬래브)
        const float Floor2Y = 3.5f;
        const float Floor2Thickness = 0.1f;
        const float Floor2Zmin = 8.5f;  // 2층 슬래브 남쪽 끝 = 마지막 계단 북쪽 끝

        // -- 계단 (오른쪽 챔버 1층, 북쪽으로 올라감)
        const int   StairStepCount = 10;
        const float StairStepDepth = 0.4f;   // along Z
        const float StairStepRiser = 0.35f;  // along Y
        const float StairStepWidth = 2.5f;   // along X (오른쪽 챔버 폭 5.5m 중간)
        const float StairStartZ = 4.5f;      // 8.5 - 10*0.4 = 4.5
        const float StairCenterX = (RightChamberXmin + RightChamberXmax) * 0.5f; // 4.25

        // -- 위험 바닥 (CarpetFloor) — 왼쪽 챔버 footprint 전체
        const float FloorThickness = 0.05f;

        // -- StartZone (P2 spawn — 왼쪽 문 안쪽)
        static readonly Vector3 StartZoneWorld = new Vector3(LeftDoorCenterX, 0.03f, LeftChamberZmin + 0.7f);
        const float ZoneWidth = 1.4f;
        const float ZoneDepth = 1.4f;
        const float ZoneThickness = 0.01f;

        // -- GoalZone (옵션 보조 클리어, 왼쪽 챔버 서쪽 끝)
        static readonly Vector3 GoalZoneWorld = new Vector3(LeftChamberXmin + 1f, 1.5f, (LeftChamberZmin + LeftChamberZmax) * 0.5f);
        const float GoalTriggerHeight = 3f;

        // -- HintBoard / HintCatcher (왼쪽 챔버 내, StartZone 동쪽 근처)
        // P2 가 StartZone 또는 카펫 위에서 east 방향으로 throw → catcher → slot.
        static readonly Vector3 BoardWorld   = new Vector3(LeftChamberXmax - 0.6f, 0f, LeftChamberZmin + 1.5f);
        static readonly Vector3 CatcherWorld = new Vector3(LeftChamberXmax - 0.6f, 1.6f, LeftChamberZmin + 3.5f);
        const float CatcherTriggerRadius = 0.55f;
        const float BoardSlotSpacing = 0.28f;
        const int   BoardSlotCount = 5;
        const float BoardSlotY = 1.10f;
        const float BoardSlotRadius = 0.07f;

        // -- Dispenser (2층, 동쪽)
        static readonly Vector3 DispenserWorld = new Vector3(RightChamberXmax - 1.5f, Floor2Y, Floor2Zmin + 2f);
        const float DispenserStandHeight = 1.0f;
        const float DispenserStandRadius = 0.10f;
        const float DispenserSpawnY = 1.10f;

        // -- LauncherHolster + CarpetLauncher (2층, 서쪽 가장자리, forward = -X)
        // 2층 서쪽 가장자리(x=RightChamberXmin) 에서 살짝 안쪽. 발사 방향은 왼쪽 챔버 (위험 바닥).
        const float HolsterTopY = Floor2Y + 0.95f;
        static readonly Vector3 HolsterWorld  = new Vector3(RightChamberXmin + 0.7f, HolsterTopY - 0.025f, Floor2Zmin + 1.5f);
        static readonly Vector3 LauncherWorld = new Vector3(RightChamberXmin + 0.7f, HolsterTopY + 0.15f, Floor2Zmin + 1.5f);
        static readonly Quaternion LauncherRot = Quaternion.Euler(0f, -90f, 0f); // forward = -X (서쪽 = 왼쪽 챔버)
        const float LauncherMuzzleSpeed = 7.5f;
        const float LauncherMuzzleSpin = 2.5f;
        const float LauncherCooldown = 0.5f;

        // -- 카펫 config
        static readonly Vector2 CarpetSize = new Vector2(0.9f, 1.2f);
        const float CarpetThickness = 0.02f;
        const float CarpetLifetime = 5f;
        const float CarpetWarningSeconds = 1.5f;

        // -- HintBalls — 위험 바닥에 흩뿌림 (StartZone 피함)
        const int HintBallCount = 5;
        const float HintBallRadius = 0.08f;
        static readonly Color[] HintBallColors =
        {
            new Color(0.95f, 0.25f, 0.25f),
            new Color(0.25f, 0.70f, 0.95f),
            new Color(0.95f, 0.85f, 0.25f),
            new Color(0.30f, 0.90f, 0.45f),
            new Color(0.85f, 0.40f, 0.95f),
        };
        static readonly Vector2[] HintBallSpread =
        {
            new Vector2(-7f,   6f),
            new Vector2(-9f,   9.5f),
            new Vector2(-5f,  11.5f),
            new Vector2(-10f, 12f),
            new Vector2(-2.5f, 9f),
        };

        // ===== Menu =====

        [MenuItem("Tools/PipePuz/Stage3/Build Layout")]
        public static void Build()
        {
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                EditorUtility.DisplayDialog("Stage3", "활성 씬을 찾을 수 없습니다. Stage3 씬을 열고 다시 시도하세요.", "OK");
                return;
            }
            if (!activeScene.name.Contains("Stage3"))
            {
                if (!EditorUtility.DisplayDialog("Stage3",
                    $"현재 활성 씬({activeScene.name})이 Stage3 가 아닙니다. 그래도 빌드할까요?",
                    "빌드", "취소")) return;
            }

            var root = GameObject.Find("RoomCarpet");
            if (root == null)
            {
                root = new GameObject("RoomCarpet");
                Undo.RegisterCreatedObjectUndo(root, "Create RoomCarpet root");
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Stage3 Layout");

            // 기존 자식 정리.
            string[] knownChildren =
            {
                "Architecture", "Entrance", "Corridor", "Junction",
                "LeftChamber", "RightChamber",
                "P2Chamber", "StairChamber", "Balcony", "SecondFloor", "Stairs",
                "LeftDoor", "RightDoor",
                "Floor", "P1Platform", "StartZone", "GoalZone",
                "Dispenser", "ActiveCarpets", "HintCatcher", "HintBoard", "HintBalls",
                "LauncherHolster", "CarpetLauncher",
            };
            foreach (var n in knownChildren) DestroyChildIfExists(root.transform, n);
            var oldCtrl = root.GetComponent<DisappearingCarpetController>();
            if (oldCtrl != null) Undo.DestroyObjectImmediate(oldCtrl);

            // Materials.
            var wallMat      = MakeUrpMaterial("Stage3_WallMat",      new Color(0.32f, 0.34f, 0.40f), false);
            var corridorMat  = MakeUrpMaterial("Stage3_CorridorMat",  new Color(0.28f, 0.30f, 0.36f), false);
            var doorFrameMat = MakeUrpMaterial("Stage3_DoorFrameMat", new Color(0.18f, 0.20f, 0.24f), false);
            var doorPanelMat = MakeEmissiveMaterial("Stage3_DoorPanelMat",
                new Color(0.18f, 0.55f, 0.85f), new Color(0.35f, 0.85f, 1.4f) * 0.6f);
            var stairMat     = MakeUrpMaterial("Stage3_StairMat",   new Color(0.42f, 0.42f, 0.45f), false);
            var floor2Mat    = MakeUrpMaterial("Stage3_Floor2Mat",  new Color(0.30f, 0.32f, 0.38f), false);
            var rightFloorMat = MakeUrpMaterial("Stage3_RightFloorMat", new Color(0.38f, 0.40f, 0.45f), false);

            var floorMat   = MakeEmissiveMaterial("Carpet_FloorMat",
                new Color(0.55f, 0.10f, 0.10f), new Color(1.0f, 0.18f, 0.18f) * 0.8f);
            var startMat   = MakeEmissiveMaterial("Carpet_StartMat",
                new Color(0.15f, 0.7f, 0.30f), new Color(0.25f, 1.4f, 0.5f) * 0.7f);
            var goalMat    = MakeEmissiveMaterial("Carpet_GoalMat",
                new Color(0.20f, 0.55f, 1.0f), new Color(0.35f, 0.85f, 1.6f) * 0.9f);
            var carpetMat  = MakeUrpMaterial("Carpet_CarpetMat", new Color(0.70f, 0.45f, 0.25f), false);
            var catcherMat = MakeEmissiveMaterial("Carpet_CatcherMat",
                new Color(0.25f, 0.45f, 0.95f), new Color(0.45f, 0.75f, 1.6f) * 0.6f);
            var boardMat   = MakeUrpMaterial("Carpet_BoardMat", new Color(0.18f, 0.18f, 0.22f), false);
            var slotMat    = MakeUrpMaterial("Carpet_SlotMat",  new Color(0.50f, 0.50f, 0.55f), false);
            var standMat   = MakeUrpMaterial("Carpet_StandMat", new Color(0.35f, 0.32f, 0.30f), false);

            var ctrl = root.AddComponent<DisappearingCarpetController>();

            // ===== Architecture parent =====
            var arch = new GameObject("Architecture");
            arch.transform.SetParent(root.transform, false);

            BuildEntrance(arch.transform, corridorMat, wallMat);
            BuildCorridor(arch.transform, corridorMat, wallMat, doorFrameMat);
            BuildLeftChamberWalls(arch.transform, wallMat);
            BuildRightChamberWalls(arch.transform, wallMat, rightFloorMat);
            BuildChamberDivider(arch.transform, wallMat);

            // Doors (복도 북쪽 벽 평면 중앙에 — z = CorridorZmax + WallThickness/2 = 3.1).
            float doorZ = CorridorZmax + WallThickness * 0.5f;
            BuildSideDoor(
                arch.transform,
                name: "LeftDoor",
                worldPos: new Vector3(LeftDoorCenterX, 0f, doorZ),
                doorFrameMat: doorFrameMat,
                doorPanelMat: doorPanelMat);
            BuildSideDoor(
                arch.transform,
                name: "RightDoor",
                worldPos: new Vector3(RightDoorCenterX, 0f, doorZ),
                doorFrameMat: doorFrameMat,
                doorPanelMat: doorPanelMat);

            // 계단 + 2층 (오른쪽 챔버 안).
            BuildStairs(arch.transform, stairMat);
            BuildSecondFloor(arch.transform, floor2Mat);

            // ===== Danger Floor (왼쪽 챔버 전체) =====
            float floorWidth = LeftChamberXmax - LeftChamberXmin;
            float floorDepth = LeftChamberZmax - LeftChamberZmin;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.position = new Vector3(
                (LeftChamberXmin + LeftChamberXmax) * 0.5f,
                -FloorThickness * 0.5f,
                (LeftChamberZmin + LeftChamberZmax) * 0.5f);
            floor.transform.localScale = new Vector3(floorWidth, FloorThickness, floorDepth);
            AssignMat(floor, floorMat);
            floor.AddComponent<CarpetFloor>();

            // ===== StartZone =====
            var start = GameObject.CreatePrimitive(PrimitiveType.Cube);
            start.name = "StartZone";
            start.transform.SetParent(root.transform, false);
            start.transform.position = StartZoneWorld;
            start.transform.localScale = new Vector3(ZoneWidth, ZoneThickness, ZoneDepth);
            AssignMat(start, startMat);

            // ===== GoalZone =====
            var goal = new GameObject("GoalZone");
            goal.transform.SetParent(root.transform, false);
            goal.transform.position = GoalZoneWorld;
            var goalTrigger = goal.AddComponent<BoxCollider>();
            goalTrigger.size = new Vector3(ZoneWidth, GoalTriggerHeight, ZoneDepth);
            goalTrigger.isTrigger = true;
            var goalComp = goal.AddComponent<CarpetGoalZone>();
            var goalVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            goalVis.name = "Visual";
            goalVis.transform.SetParent(goal.transform, false);
            goalVis.transform.localPosition = new Vector3(0f, 0.03f - GoalTriggerHeight * 0.5f, 0f);
            goalVis.transform.localScale = new Vector3(ZoneWidth, ZoneThickness, ZoneDepth);
            AssignMat(goalVis, goalMat);

            // ===== Dispenser (2층 위) =====
            var disp = new GameObject("Dispenser");
            disp.transform.SetParent(root.transform, false);
            disp.transform.position = DispenserWorld;

            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "Stand";
            DisableColliderIfAny(stand);
            stand.transform.SetParent(disp.transform, false);
            stand.transform.localPosition = new Vector3(0f, DispenserStandHeight * 0.5f, 0f);
            stand.transform.localScale = new Vector3(DispenserStandRadius * 2f, DispenserStandHeight * 0.5f, DispenserStandRadius * 2f);
            AssignMat(stand, standMat);

            var spawnPoint = new GameObject("SpawnPoint");
            spawnPoint.transform.SetParent(disp.transform, false);
            spawnPoint.transform.localPosition = new Vector3(0f, DispenserSpawnY, 0f);

            var dispComp = disp.AddComponent<CarpetDispenser>();
            dispComp.SpawnPoint = spawnPoint.transform;
            dispComp.CarpetMaterial = carpetMat;
            dispComp.CarpetSize = CarpetSize;
            dispComp.CarpetThickness = CarpetThickness;
            dispComp.CarpetLifetime = CarpetLifetime;
            dispComp.CarpetWarningSeconds = CarpetWarningSeconds;

            // ===== ActiveCarpets =====
            var active = new GameObject("ActiveCarpets");
            active.transform.SetParent(root.transform, false);
            active.transform.localPosition = Vector3.zero;
            dispComp.ActiveCarpetsRoot = active.transform;

            // ===== LauncherHolster + CarpetLauncher (2층 서쪽 가장자리) =====
            var holster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            holster.name = "LauncherHolster";
            holster.transform.SetParent(root.transform, false);
            holster.transform.position = HolsterWorld;
            holster.transform.localScale = new Vector3(0.45f, 0.05f, 0.30f);
            AssignMat(holster, standMat);

            var launcher = BuildLauncher(root.transform, carpetMat, active.transform);

            // ===== HintBoard (왼쪽 챔버 내, 동쪽 벽 가까이) =====
            var board = BuildHintBoard(root.transform, boardMat, slotMat);

            // ===== HintCatcher (왼쪽 챔버 내) =====
            var catcher = BuildHintCatcher(root.transform, catcherMat, board.GetComponent<HintPuzzleBoard>());

            // ===== HintBalls =====
            BuildHintBalls(root.transform);

            // ===== Controller wire-up =====
            ctrl.Dispenser = dispComp;
            ctrl.Goal = goalComp;
            ctrl.ActiveCarpetsRoot = active.transform;
            ctrl.FloorCollider = floor.GetComponent<BoxCollider>();
            ctrl.StartZoneCollider = start.GetComponent<BoxCollider>();
            ctrl.GoalZoneCollider = goalTrigger;
            ctrl.StartPoint = start.transform;
            ctrl.OverlapRadius = 0.15f;
            ctrl.RespawnCooldown = 1.0f;
            ctrl.HintBoard = board.GetComponent<HintPuzzleBoard>();
            // P1Safe = 2층 슬래브 + 계단들 (P1 머리가 위험 바닥 X/Z 와 절대 안 겹치니 실제로 거의 안 쓰임)
            var safeCols = new System.Collections.Generic.List<Collider>();
            var sf = arch.transform.Find("SecondFloor");
            if (sf != null)
            {
                var bc = sf.GetComponentInChildren<BoxCollider>();
                if (bc != null) safeCols.Add(bc);
            }
            var stairsGo = arch.transform.Find("Stairs");
            if (stairsGo != null)
            {
                var stepCols = stairsGo.GetComponentsInChildren<BoxCollider>();
                safeCols.AddRange(stepCols);
            }
            ctrl.P1SafeColliders = safeCols.ToArray();

            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[Stage3] Build 완료. 평면도 기반 레이아웃 — 복도+T입구, 왼쪽 P2 챔버, 오른쪽 챔버 1층 계단 + 2층 카펫 발사대. " +
                      "XR Origin 의 초기 위치는 entrance 안쪽 (예: 0, 0, -1.5) 으로 수동 셋업하세요.");
        }

        // ===== Architecture builders =====

        static void BuildEntrance(Transform parent, Material floorMat, Material wallMat)
        {
            var ent = new GameObject("Entrance");
            ent.transform.SetParent(parent, false);

            float xCenter = (EntranceXmin + EntranceXmax) * 0.5f;
            float xLen    = EntranceXmax - EntranceXmin;
            float zCenter = (EntranceZmin + EntranceZmax) * 0.5f;
            float zLen    = EntranceZmax - EntranceZmin;

            MakeBox(ent.transform, "Floor",
                center: new Vector3(xCenter, -0.025f, zCenter),
                size:   new Vector3(xLen, 0.05f, zLen),
                mat: floorMat);

            // 양쪽 벽 (남쪽 입구는 열어둠).
            MakeBox(ent.transform, "Wall_W",
                center: new Vector3(EntranceXmin - WallThickness * 0.5f, EntranceHeight * 0.5f, zCenter),
                size:   new Vector3(WallThickness, EntranceHeight, zLen),
                mat: wallMat);
            MakeBox(ent.transform, "Wall_E",
                center: new Vector3(EntranceXmax + WallThickness * 0.5f, EntranceHeight * 0.5f, zCenter),
                size:   new Vector3(WallThickness, EntranceHeight, zLen),
                mat: wallMat);
        }

        static void BuildCorridor(Transform parent, Material floorMat, Material wallMat, Material doorFrameMat)
        {
            var corr = new GameObject("Corridor");
            corr.transform.SetParent(parent, false);

            float xCenter = (CorridorXmin + CorridorXmax) * 0.5f;
            float xLen    = CorridorXmax - CorridorXmin;
            float zCenter = (CorridorZmin + CorridorZmax) * 0.5f;
            float zLen    = CorridorZmax - CorridorZmin;

            MakeBox(corr.transform, "Floor",
                center: new Vector3(xCenter, -0.025f, zCenter),
                size:   new Vector3(xLen, 0.05f, zLen),
                mat: floorMat);

            // 복도 남쪽 벽 — 입구 돌출 부분(x=EntranceXmin..EntranceXmax)에는 구멍.
            BuildWallWithOpening(
                corr.transform, "Wall_S",
                axisIsX: false,
                fixedCoord: CorridorZmin - WallThickness * 0.5f,
                openingCenter: (EntranceXmin + EntranceXmax) * 0.5f,
                openingHalfWidth: (EntranceXmax - EntranceXmin) * 0.5f,
                wallStart: CorridorXmin - 0.001f,
                wallEnd:   CorridorXmax + 0.001f,
                wallY: CorridorHeight,
                openingHeight: CorridorHeight,   // 헤더 없이 입구는 천장까지 열려있음
                wallThickness: WallThickness,
                wallMat: wallMat,
                frameMat: doorFrameMat);

            // 복도 서/동 벽.
            MakeBox(corr.transform, "Wall_W",
                center: new Vector3(CorridorXmin - WallThickness * 0.5f, CorridorHeight * 0.5f, zCenter),
                size:   new Vector3(WallThickness, CorridorHeight, zLen + WallThickness * 2f),
                mat: wallMat);
            MakeBox(corr.transform, "Wall_E",
                center: new Vector3(CorridorXmax + WallThickness * 0.5f, CorridorHeight * 0.5f, zCenter),
                size:   new Vector3(WallThickness, CorridorHeight, zLen + WallThickness * 2f),
                mat: wallMat);

            // 복도 북쪽 벽 = 챔버 남쪽 벽. 두 doorway 를 가짐.
            // 한번에 처리하기 위해 두 번 호출하여 doorway 영역을 빼냄.
            // 챔버 벽 높이는 5m (LeftChamberWallY), 도어 높이는 2.2m. 헤더는 도어 위 ~2.8m.
            // 두 도어 사이의 wall fragment 도 필요.
            // 가장 간단한 방법: 4 개 fragment 직접 생성.
            float chamberWallY = LeftChamberWallY; // 5m
            float doorY = DoorHeight;
            float halfDoor = DoorOpeningWidth * 0.5f;

            // Fragment 1: x ∈ [CorridorXmin, LeftDoorCenterX - halfDoor], 전체 높이
            float f1Min = CorridorXmin;
            float f1Max = LeftDoorCenterX - halfDoor;
            if (f1Max > f1Min + 0.01f)
            {
                MakeBox(corr.transform, "Wall_N_F1",
                    center: new Vector3((f1Min + f1Max) * 0.5f, chamberWallY * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    size:   new Vector3(f1Max - f1Min, chamberWallY, WallThickness),
                    mat: wallMat);
            }
            // Header (door1 위)
            if (chamberWallY > doorY + 0.01f)
            {
                MakeBox(corr.transform, "Wall_N_D1Hdr",
                    center: new Vector3(LeftDoorCenterX, (doorY + chamberWallY) * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    size:   new Vector3(DoorOpeningWidth, chamberWallY - doorY, WallThickness),
                    mat: doorFrameMat);
            }
            // Fragment 2: door1 우측 ~ door2 좌측
            float f2Min = LeftDoorCenterX + halfDoor;
            float f2Max = RightDoorCenterX - halfDoor;
            if (f2Max > f2Min + 0.01f)
            {
                MakeBox(corr.transform, "Wall_N_F2",
                    center: new Vector3((f2Min + f2Max) * 0.5f, chamberWallY * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    size:   new Vector3(f2Max - f2Min, chamberWallY, WallThickness),
                    mat: wallMat);
            }
            // Header (door2 위)
            if (chamberWallY > doorY + 0.01f)
            {
                MakeBox(corr.transform, "Wall_N_D2Hdr",
                    center: new Vector3(RightDoorCenterX, (doorY + chamberWallY) * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    size:   new Vector3(DoorOpeningWidth, chamberWallY - doorY, WallThickness),
                    mat: doorFrameMat);
            }
            // Fragment 3: door2 우측 ~ CorridorXmax
            float f3Min = RightDoorCenterX + halfDoor;
            float f3Max = CorridorXmax;
            if (f3Max > f3Min + 0.01f)
            {
                MakeBox(corr.transform, "Wall_N_F3",
                    center: new Vector3((f3Min + f3Max) * 0.5f, chamberWallY * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    size:   new Vector3(f3Max - f3Min, chamberWallY, WallThickness),
                    mat: wallMat);
            }
        }

        static void BuildLeftChamberWalls(Transform parent, Material wallMat)
        {
            var chamber = new GameObject("LeftChamber");
            chamber.transform.SetParent(parent, false);

            float xCenter = (LeftChamberXmin + LeftChamberXmax) * 0.5f;
            float xLen    = LeftChamberXmax - LeftChamberXmin;
            float zCenter = (LeftChamberZmin + LeftChamberZmax) * 0.5f;
            float zLen    = LeftChamberZmax - LeftChamberZmin;

            MakeBox(chamber.transform, "Wall_W",
                center: new Vector3(LeftChamberXmin - WallThickness * 0.5f, LeftChamberWallY * 0.5f, zCenter),
                size:   new Vector3(WallThickness, LeftChamberWallY, zLen + WallThickness * 2f),
                mat: wallMat);
            MakeBox(chamber.transform, "Wall_N",
                center: new Vector3(xCenter, LeftChamberWallY * 0.5f, LeftChamberZmax + WallThickness * 0.5f),
                size:   new Vector3(xLen, LeftChamberWallY, WallThickness),
                mat: wallMat);
            // 남쪽 벽 = 복도 북쪽 벽 — 이미 BuildCorridor 에서 세움.
            // 동쪽 벽 = 챔버 사이 분리벽 — BuildChamberDivider 에서 세움.
        }

        static void BuildRightChamberWalls(Transform parent, Material wallMat, Material floorMat)
        {
            var chamber = new GameObject("RightChamber");
            chamber.transform.SetParent(parent, false);

            float xCenter = (RightChamberXmin + RightChamberXmax) * 0.5f;
            float xLen    = RightChamberXmax - RightChamberXmin;
            float zCenter = (RightChamberZmin + RightChamberZmax) * 0.5f;
            float zLen    = RightChamberZmax - RightChamberZmin;

            // 1층 바닥 (오른쪽 챔버 전체)
            MakeBox(chamber.transform, "Floor1F",
                center: new Vector3(xCenter, -0.025f, zCenter),
                size:   new Vector3(xLen, 0.05f, zLen),
                mat: floorMat);

            MakeBox(chamber.transform, "Wall_E",
                center: new Vector3(RightChamberXmax + WallThickness * 0.5f, RightChamberWallY * 0.5f, zCenter),
                size:   new Vector3(WallThickness, RightChamberWallY, zLen + WallThickness * 2f),
                mat: wallMat);
            MakeBox(chamber.transform, "Wall_N",
                center: new Vector3(xCenter, RightChamberWallY * 0.5f, RightChamberZmax + WallThickness * 0.5f),
                size:   new Vector3(xLen, RightChamberWallY, WallThickness),
                mat: wallMat);
            // 남쪽 벽 = 복도 북쪽 벽 (BuildCorridor 에서).
            // 서쪽 벽 = 챔버 분리벽 (BuildChamberDivider 에서).
        }

        /// <summary>
        /// 왼쪽-오른쪽 챔버 사이 분리벽 (x = LeftChamberXmax = RightChamberXmin = +1.5).
        ///   - z ∈ [3, Floor2Zmin] : 천장까지 (높이 RightChamberWallY)
        ///   - z ∈ [Floor2Zmin, 14] : 2층 바닥 높이까지만 (높이 Floor2Y)
        /// </summary>
        static void BuildChamberDivider(Transform parent, Material wallMat)
        {
            var div = new GameObject("ChamberDivider");
            div.transform.SetParent(parent, false);

            // 남쪽 부분: 천장까지.
            float sMin = LeftChamberZmin;
            float sMax = Floor2Zmin;
            MakeBox(div.transform, "Div_S",
                center: new Vector3(LeftChamberXmax + WallThickness * 0.5f, RightChamberWallY * 0.5f, (sMin + sMax) * 0.5f),
                size:   new Vector3(WallThickness, RightChamberWallY, sMax - sMin),
                mat: wallMat);

            // 북쪽 부분: 2층 바닥 높이까지만 (위는 열림 — P1 이 왼쪽 챔버 내려다봄).
            float nMin = Floor2Zmin;
            float nMax = LeftChamberZmax;
            MakeBox(div.transform, "Div_N",
                center: new Vector3(LeftChamberXmax + WallThickness * 0.5f, Floor2Y * 0.5f, (nMin + nMax) * 0.5f),
                size:   new Vector3(WallThickness, Floor2Y, nMax - nMin),
                mat: wallMat);
        }

        /// <summary>
        /// 자동 미닫이문. 복도 북쪽 벽에 설치되며, 패널은 ±X 방향으로 슬라이드.
        /// door root: forward = +Z (chamber 쪽), local X = world X (회전 없음).
        /// </summary>
        static GameObject BuildSideDoor(
            Transform parent, string name, Vector3 worldPos,
            Material doorFrameMat, Material doorPanelMat)
        {
            var door = new GameObject(name);
            door.transform.SetParent(parent, false);
            door.transform.position = worldPos;

            float halfPanelWidth = DoorOpeningWidth * 0.5f;
            float panelWidthX    = halfPanelWidth; // 각 패널 X 폭 (닫혀 있을 때)

            // 패널 콜라이더 그대로 유지 — 닫힌 상태에서 물리적으로 문 역할.
            var leftPanel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftPanel.name = "LeftPanel";
            leftPanel.transform.SetParent(door.transform, false);
            leftPanel.transform.localPosition = new Vector3(-halfPanelWidth * 0.5f, DoorHeight * 0.5f, 0f);
            leftPanel.transform.localScale    = new Vector3(panelWidthX, DoorHeight, DoorPanelThickness);
            AssignMat(leftPanel, doorPanelMat);

            var rightPanel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightPanel.name = "RightPanel";
            rightPanel.transform.SetParent(door.transform, false);
            rightPanel.transform.localPosition = new Vector3(+halfPanelWidth * 0.5f, DoorHeight * 0.5f, 0f);
            rightPanel.transform.localScale    = new Vector3(panelWidthX, DoorHeight, DoorPanelThickness);
            AssignMat(rightPanel, doorPanelMat);

            // 감지 트리거 — 문 앞뒤로 ±2m Z, 그리고 doorway 폭 + 양쪽 1m.
            var trigger = new GameObject("DetectionVolume");
            trigger.transform.SetParent(door.transform, false);
            trigger.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            var col = trigger.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(DoorOpeningWidth + 2f, 2.5f, 4f);

            var auto = door.AddComponent<AutoSlidingDoor>();
            auto.LeftPanel = leftPanel.transform;
            auto.RightPanel = rightPanel.transform;
            auto.SlideDistance = halfPanelWidth;
            auto.OpenSpeed = 2.5f;
            auto.CloseSpeed = 1.8f;
            auto.CloseDelay = 0.4f;
            auto.DetectionVolume = col;
            auto.SlideAxisLocal = new Vector3(1f, 0f, 0f); // 패널이 ±X 방향으로 슬라이드
            auto.RecacheClosedPositions();

            return door;
        }

        /// <summary>
        /// 계단 — 오른쪽 챔버 안에서 +Z 방향(북쪽)으로 올라감.
        /// 마지막 계단 북쪽 끝 = Floor2Zmin (2층 슬래브 시작).
        /// </summary>
        static void BuildStairs(Transform parent, Material stairMat)
        {
            var stairs = new GameObject("Stairs");
            stairs.transform.SetParent(parent, false);

            for (int i = 0; i < StairStepCount; i++)
            {
                float yTop = (i + 1) * StairStepRiser;
                float zMin = StairStartZ + i * StairStepDepth;
                float zCenter = zMin + StairStepDepth * 0.5f;
                // 각 계단은 floor(y=0) 부터 yTop 까지 채운 박스 — 발 빠짐 방지.
                MakeBox(stairs.transform, $"Step_{i + 1}",
                    center: new Vector3(StairCenterX, yTop * 0.5f, zCenter),
                    size:   new Vector3(StairStepWidth, yTop, StairStepDepth),
                    mat: stairMat);
            }
        }

        /// <summary>
        /// 2층 슬래브 — 오른쪽 챔버 북쪽 절반에 y=Floor2Y 로 설치.
        /// 슬래브 동·북 가장자리는 챔버 벽에 닿아 자연 마감. 남쪽은 계단 상단에 연결.
        /// 서쪽 가장자리는 ChamberDivider 의 북쪽 부분 위로 ⇒ P1 이 -X 방향으로 카펫 발사 가능.
        /// 난간은 오직 서쪽(왼쪽 챔버 쪽) 가장자리에만 — 부분 안전 + P1 의 시야 확보.
        /// </summary>
        static void BuildSecondFloor(Transform parent, Material floor2Mat)
        {
            var sf = new GameObject("SecondFloor");
            sf.transform.SetParent(parent, false);

            float xCenter = (RightChamberXmin + RightChamberXmax) * 0.5f;
            float xLen    = RightChamberXmax - RightChamberXmin;
            float zCenter = (Floor2Zmin + RightChamberZmax) * 0.5f;
            float zLen    = RightChamberZmax - Floor2Zmin;

            // Slab
            MakeBox(sf.transform, "Slab",
                center: new Vector3(xCenter, Floor2Y - Floor2Thickness * 0.5f, zCenter),
                size:   new Vector3(xLen, Floor2Thickness, zLen),
                mat: floor2Mat);

            // 서쪽 가장자리 난간 — 단, P1 이 카펫을 -X 방향으로 발사할 수 있게 부분적으로만.
            // launcher 가 있는 z 영역(Floor2Zmin .. Floor2Zmin+3) 은 비워두고 그 외 z 만 난간.
            const float RailY = 1.0f;
            const float RailThickness = 0.05f;
            float launchGapZmin = Floor2Zmin;
            float launchGapZmax = Floor2Zmin + 3f;

            if (launchGapZmax < RightChamberZmax - 0.01f)
            {
                float segZmin = launchGapZmax;
                float segZmax = RightChamberZmax;
                MakeBox(sf.transform, "Rail_W",
                    center: new Vector3(RightChamberXmin + RailThickness * 0.5f, Floor2Y + RailY * 0.5f, (segZmin + segZmax) * 0.5f),
                    size:   new Vector3(RailThickness, RailY, segZmax - segZmin),
                    mat: floor2Mat);
            }

            // 남쪽 가장자리 난간 — 계단 폭(StairStepWidth) 만큼 비워두고 양쪽만 보호.
            float stairXmin = StairCenterX - StairStepWidth * 0.5f;
            float stairXmax = StairCenterX + StairStepWidth * 0.5f;
            if (stairXmin > RightChamberXmin + 0.01f)
            {
                MakeBox(sf.transform, "Rail_S_W",
                    center: new Vector3((RightChamberXmin + stairXmin) * 0.5f, Floor2Y + RailY * 0.5f, Floor2Zmin + RailThickness * 0.5f),
                    size:   new Vector3(stairXmin - RightChamberXmin, RailY, RailThickness),
                    mat: floor2Mat);
            }
            if (stairXmax < RightChamberXmax - 0.01f)
            {
                MakeBox(sf.transform, "Rail_S_E",
                    center: new Vector3((stairXmax + RightChamberXmax) * 0.5f, Floor2Y + RailY * 0.5f, Floor2Zmin + RailThickness * 0.5f),
                    size:   new Vector3(RightChamberXmax - stairXmax, RailY, RailThickness),
                    mat: floor2Mat);
            }
        }

        // ===== Launcher / Board / Catcher / HintBalls =====

        static GameObject BuildLauncher(Transform parent, Material carpetMat, Transform active)
        {
            var gunGripMat   = MakeUrpMaterial("Carpet_GunGripMat",   new Color(0.15f, 0.15f, 0.18f), false);
            var gunMetalMat  = MakeUrpMaterial("Carpet_GunMetalMat",  new Color(0.45f, 0.47f, 0.52f), false);
            var gunAccentMat = MakeUrpMaterial("Carpet_GunAccentMat", new Color(0.80f, 0.30f, 0.15f), false);

            var launcher = new GameObject("CarpetLauncher");
            launcher.transform.SetParent(parent, false);
            launcher.transform.position = LauncherWorld;
            launcher.transform.rotation = LauncherRot;

            var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grip.name = "Grip";
            DisableColliderIfAny(grip);
            grip.transform.SetParent(launcher.transform, false);
            grip.transform.localPosition = new Vector3(0f, -0.075f, -0.015f);
            grip.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            grip.transform.localScale = new Vector3(0.045f, 0.15f, 0.065f);
            AssignMat(grip, gunGripMat);

            var receiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
            receiver.name = "Receiver";
            DisableColliderIfAny(receiver);
            receiver.transform.SetParent(launcher.transform, false);
            receiver.transform.localPosition = new Vector3(0f, 0.015f, 0.09f);
            receiver.transform.localScale = new Vector3(0.06f, 0.075f, 0.22f);
            AssignMat(receiver, gunGripMat);

            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel";
            DisableColliderIfAny(barrel);
            barrel.transform.SetParent(launcher.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 0.015f, 0.28f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.035f, 0.10f, 0.035f);
            AssignMat(barrel, gunMetalMat);

            var muzzleRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            muzzleRing.name = "MuzzleRing";
            DisableColliderIfAny(muzzleRing);
            muzzleRing.transform.SetParent(launcher.transform, false);
            muzzleRing.transform.localPosition = new Vector3(0f, 0.015f, 0.40f);
            muzzleRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            muzzleRing.transform.localScale = new Vector3(0.05f, 0.012f, 0.05f);
            AssignMat(muzzleRing, gunMetalMat);

            var guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            guard.name = "TriggerGuard";
            DisableColliderIfAny(guard);
            guard.transform.SetParent(launcher.transform, false);
            guard.transform.localPosition = new Vector3(0f, -0.030f, 0.04f);
            guard.transform.localScale = new Vector3(0.025f, 0.035f, 0.025f);
            AssignMat(guard, gunGripMat);

            var trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trigger.name = "Trigger";
            DisableColliderIfAny(trigger);
            trigger.transform.SetParent(launcher.transform, false);
            trigger.transform.localPosition = new Vector3(0f, -0.025f, 0.035f);
            trigger.transform.localScale = new Vector3(0.012f, 0.022f, 0.008f);
            AssignMat(trigger, gunAccentMat);

            var frontSight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frontSight.name = "FrontSight";
            DisableColliderIfAny(frontSight);
            frontSight.transform.SetParent(launcher.transform, false);
            frontSight.transform.localPosition = new Vector3(0f, 0.060f, 0.395f);
            frontSight.transform.localScale = new Vector3(0.008f, 0.012f, 0.012f);
            AssignMat(frontSight, gunMetalMat);

            var rearSight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rearSight.name = "RearSight";
            DisableColliderIfAny(rearSight);
            rearSight.transform.SetParent(launcher.transform, false);
            rearSight.transform.localPosition = new Vector3(0f, 0.060f, 0.165f);
            rearSight.transform.localScale = new Vector3(0.022f, 0.010f, 0.012f);
            AssignMat(rearSight, gunMetalMat);

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(launcher.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.015f, 0.50f);

            var attach = new GameObject("AttachPoint");
            attach.transform.SetParent(launcher.transform, false);
            attach.transform.localPosition = new Vector3(0f, -0.025f, 0.025f);

            var launcherCol = launcher.AddComponent<BoxCollider>();
            launcherCol.size = new Vector3(0.07f, 0.25f, 0.28f);
            launcherCol.center = new Vector3(0f, -0.025f, 0.05f);

            var launcherRb = launcher.AddComponent<Rigidbody>();
            launcherRb.mass = 1.0f;
            launcherRb.useGravity = true;
            launcherRb.isKinematic = false;
            launcherRb.linearDamping = 0.5f;
            launcherRb.angularDamping = 2f;
            launcherRb.interpolation = RigidbodyInterpolation.Interpolate;
            launcherRb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var launcherGrab = launcher.AddComponent<XRGrabInteractable>();
            launcherGrab.throwOnDetach = false;
            launcherGrab.attachTransform = attach.transform;

            var launcherComp = launcher.AddComponent<CarpetLauncher>();
            launcherComp.Muzzle = muzzle.transform;
            launcherComp.MuzzleSpeed = LauncherMuzzleSpeed;
            launcherComp.MuzzleSpin = LauncherMuzzleSpin;
            launcherComp.CarpetMaterial = carpetMat;
            launcherComp.CarpetSize = CarpetSize;
            launcherComp.CarpetThickness = CarpetThickness;
            launcherComp.CarpetLifetime = CarpetLifetime;
            launcherComp.CarpetWarningSeconds = CarpetWarningSeconds;
            launcherComp.ActiveCarpetsRoot = active;
            launcherComp.Cooldown = LauncherCooldown;
            launcherComp.SpawnAhead = 0.05f;
            launcherComp.IgnoreSelfCollision = true;

            return launcher;
        }

        static GameObject BuildHintBoard(Transform parent, Material boardMat, Material slotMat)
        {
            var board = new GameObject("HintBoard");
            board.transform.SetParent(parent, false);
            board.transform.position = BoardWorld;
            // 보드 정면이 west(-X) 를 향함 → P2 가 동쪽 벽을 바라볼 때 보드가 보이고, 슬롯이 P2 쪽으로 열림.
            board.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

            var boardVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boardVis.name = "Visual";
            DisableColliderIfAny(boardVis);
            boardVis.transform.SetParent(board.transform, false);
            boardVis.transform.localPosition = new Vector3(0f, BoardSlotY - 0.15f, 0f);
            boardVis.transform.localScale = new Vector3(BoardSlotSpacing * BoardSlotCount + 0.15f, 0.06f, 0.25f);
            AssignMat(boardVis, boardMat);

            var boardComp = board.AddComponent<HintPuzzleBoard>();
            boardComp.Slots.Clear();

            for (int i = 0; i < BoardSlotCount; i++)
            {
                var slotGo = new GameObject($"Slot_{i + 1}");
                slotGo.transform.SetParent(board.transform, false);
                float xOffset = (i - (BoardSlotCount - 1) * 0.5f) * BoardSlotSpacing;
                slotGo.transform.localPosition = new Vector3(xOffset, BoardSlotY, 0f);

                var cup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cup.name = "Cup";
                DisableColliderIfAny(cup);
                cup.transform.SetParent(slotGo.transform, false);
                cup.transform.localPosition = new Vector3(0f, -BoardSlotRadius * 0.4f, 0f);
                cup.transform.localScale = Vector3.one * (BoardSlotRadius * 2f);
                AssignMat(cup, slotMat);

                var dock = new GameObject("Dock");
                dock.transform.SetParent(slotGo.transform, false);
                dock.transform.localPosition = Vector3.zero;

                var slotComp = slotGo.AddComponent<HintSlot>();
                slotComp.DockPoint = dock.transform;
                boardComp.Slots.Add(slotComp);
            }
            return board;
        }

        static GameObject BuildHintCatcher(Transform parent, Material catcherMat, HintPuzzleBoard board)
        {
            var catcher = new GameObject("HintCatcher");
            catcher.transform.SetParent(parent, false);
            catcher.transform.position = CatcherWorld;

            var catcherTrigger = catcher.AddComponent<SphereCollider>();
            catcherTrigger.radius = CatcherTriggerRadius;
            catcherTrigger.isTrigger = true;

            var catcherVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            catcherVis.name = "Visual";
            DisableColliderIfAny(catcherVis);
            catcherVis.transform.SetParent(catcher.transform, false);
            catcherVis.transform.localPosition = Vector3.zero;
            catcherVis.transform.localScale = Vector3.one * (CatcherTriggerRadius * 2f);
            AssignMat(catcherVis, catcherMat);

            var catcherComp = catcher.AddComponent<HintCatcher>();
            catcherComp.Board = board;
            return catcher;
        }

        static void BuildHintBalls(Transform parent)
        {
            var ballsRoot = new GameObject("HintBalls");
            ballsRoot.transform.SetParent(parent, false);
            ballsRoot.transform.localPosition = Vector3.zero;

            int count = Mathf.Min(HintBallCount, HintBallSpread.Length);
            for (int i = 0; i < count; i++)
            {
                var color = HintBallColors[i % HintBallColors.Length];
                var ballMat = MakeUrpMaterial($"Carpet_HintBallMat_{i}", color, false);
                if (ballMat.HasProperty("_EmissionColor"))
                {
                    ballMat.SetColor("_EmissionColor", color * 0.4f);
                    ballMat.EnableKeyword("_EMISSION");
                }

                var ball = new GameObject($"HintBall_{i + 1}");
                ball.transform.SetParent(ballsRoot.transform, false);
                ball.transform.position = new Vector3(
                    HintBallSpread[i].x,
                    FloorThickness * 0.5f + HintBallRadius + 0.001f,
                    HintBallSpread[i].y);

                var ballVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ballVis.name = "Visual";
                DisableColliderIfAny(ballVis);
                ballVis.transform.SetParent(ball.transform, false);
                ballVis.transform.localPosition = Vector3.zero;
                ballVis.transform.localScale = Vector3.one * (HintBallRadius * 2f);
                AssignMat(ballVis, ballMat);

                var col = ball.AddComponent<SphereCollider>();
                col.radius = HintBallRadius;

                var rb = ball.AddComponent<Rigidbody>();
                rb.mass = 0.3f;
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.linearDamping = 1.5f;
                rb.angularDamping = 2.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                var grab = ball.AddComponent<XRGrabInteractable>();
                grab.throwOnDetach = true;
                grab.smoothPosition = false;
                grab.smoothRotation = false;

                var hint = ball.AddComponent<HintBall>();
                hint.VisualRenderer = ballVis.GetComponent<Renderer>();
                hint.ColorId = i;
                hint.BaseColor = color;
            }
        }

        // ===== Helpers =====

        /// <summary>
        /// 한 축이 고정인 벽에 doorway 모양 직사각형 구멍을 남긴다.
        /// </summary>
        static void BuildWallWithOpening(
            Transform parent, string baseName,
            bool axisIsX,
            float fixedCoord,
            float openingCenter,
            float openingHalfWidth,
            float wallStart,
            float wallEnd,
            float wallY,
            float openingHeight,
            float wallThickness,
            Material wallMat,
            Material frameMat)
        {
            float openingMin = openingCenter - openingHalfWidth;
            float openingMax = openingCenter + openingHalfWidth;

            if (openingMin > wallStart + 0.01f)
            {
                float cMin = wallStart;
                float cMax = openingMin;
                float center = (cMin + cMax) * 0.5f;
                float len = cMax - cMin;
                Vector3 c = axisIsX
                    ? new Vector3(fixedCoord, wallY * 0.5f, center)
                    : new Vector3(center, wallY * 0.5f, fixedCoord);
                Vector3 s = axisIsX
                    ? new Vector3(wallThickness, wallY, len)
                    : new Vector3(len, wallY, wallThickness);
                MakeBox(parent, $"{baseName}_Side1", c, s, wallMat);
            }
            if (openingMax < wallEnd - 0.01f)
            {
                float cMin = openingMax;
                float cMax = wallEnd;
                float center = (cMin + cMax) * 0.5f;
                float len = cMax - cMin;
                Vector3 c = axisIsX
                    ? new Vector3(fixedCoord, wallY * 0.5f, center)
                    : new Vector3(center, wallY * 0.5f, fixedCoord);
                Vector3 s = axisIsX
                    ? new Vector3(wallThickness, wallY, len)
                    : new Vector3(len, wallY, wallThickness);
                MakeBox(parent, $"{baseName}_Side2", c, s, wallMat);
            }
            if (wallY > openingHeight + 0.01f)
            {
                float headerY = (openingHeight + wallY) * 0.5f;
                float headerH = wallY - openingHeight;
                float center = openingCenter;
                float len = openingHalfWidth * 2f;
                Vector3 c = axisIsX
                    ? new Vector3(fixedCoord, headerY, center)
                    : new Vector3(center, headerY, fixedCoord);
                Vector3 s = axisIsX
                    ? new Vector3(wallThickness, headerH, len)
                    : new Vector3(len, headerH, wallThickness);
                MakeBox(parent, $"{baseName}_Header", c, s, frameMat);
            }
        }

        static GameObject MakeBox(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            AssignMat(go, mat);
            return go;
        }

        static void DisableColliderIfAny(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        static void DestroyChildIfExists(Transform parent, string childName)
        {
            var t = parent.Find(childName);
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }

        static Material MakeUrpMaterial(string name, Color color, bool transparent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (transparent)
            {
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return m;
        }

        static Material MakeEmissiveMaterial(string name, Color baseColor, Color emissionColor)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.color = baseColor;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
            if (m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", emissionColor);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        static void AssignMat(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }
    }
}
