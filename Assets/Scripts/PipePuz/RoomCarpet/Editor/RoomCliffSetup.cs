using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Stage3/Build Cliff Layout.
    ///
    /// Stage3 씬 안의 기존 RoomCliff GameObject 안에 RoomCarpet 와 동일한 레이아웃을 빌드하되,
    /// 핵심 메커닉을 절벽(cliff)으로 교체:
    ///   - 좌측 챔버에 위험 바닥(CarpetFloor) 대신 빈 공간(추락 가능).
    ///   - 6개 <see cref="CliffPlatform"/> 을 고정 시드 랜덤으로 배치 — 안전한 영구 발판.
    ///   - 카메라(머리) Y < FallThreshold 가 되면 마지막에 밟았던 발판으로 리스폰
    ///     (<see cref="CliffController"/>).
    ///   - 카펫은 CarpetFloor 충돌 대신 floating mode — y=0.8m 에서 멈춰 임시 발판이 됨
    ///     (DisappearingCarpet.UseFloatingMode).
    ///
    /// 모든 자식은 RoomCliff GameObject 아래 <see cref="Transform.localPosition"/> 사용 → RoomCliff
    /// 의 world position 이 통째로 offset 역할. RoomCliff 를 드래그해 위치 조정 가능.
    /// </summary>
    public static class RoomCliffSetup
    {
        // ===== Layout constants (Stage3LayoutSetup 와 동일 — RoomCliff local 좌표) =====

        const float WallThickness = 0.2f;

        const float EntranceXmin = -3f;
        const float EntranceXmax = +3f;
        const float EntranceZmin = -3f;
        const float EntranceZmax = 0f;
        const float EntranceHeight = 3f;

        const float CorridorXmin = -12f;
        const float CorridorXmax = +7f;
        const float CorridorZmin = 0f;
        const float CorridorZmax = 3f;
        const float CorridorHeight = 3f;

        const float LeftDoorCenterX = -4f;
        const float RightDoorCenterX = +4f;
        const float DoorOpeningWidth = 2f;
        const float DoorHeight = 2.2f;
        const float DoorPanelThickness = 0.08f;

        const float LeftChamberXmin = -12f;
        const float LeftChamberXmax = +1.5f;
        const float LeftChamberZmin = +3f;
        const float LeftChamberZmax = +14f;
        const float LeftChamberWallY = 5f;

        const float RightChamberXmin = +1.5f;
        const float RightChamberXmax = +7f;
        const float RightChamberZmin = +3f;
        const float RightChamberZmax = +14f;
        const float RightChamberWallY = 5f;

        const float Floor2Y = 3.5f;
        const float Floor2Thickness = 0.1f;
        const float Floor2Zmin = 8.5f;

        const int   StairStepCount = 10;
        const float StairStepDepth = 0.4f;
        const float StairStepRiser = 0.35f;
        const float StairStepWidth = 2.5f;
        const float StairStartZ = 4.5f;
        const float StairCenterX = (RightChamberXmin + RightChamberXmax) * 0.5f;

        // ===== Cliff-specific =====

        [System.Serializable]
        struct PlatformSpec { public Vector2 PosXZ; public float TopY; public Vector2 Size; }

        // 플랫폼 윗면 = 복도/우측 챔버 1F 바닥(y=0) 과 동일 → 문을 통과해 진입할 때 단차 없음.
        // 카펫 floating Y 도 플랫폼과 거의 같은 레벨로 낮춰 카펫 ↔ 플랫폼 보행이 자연스럽게.
        const float PlatformTopY = 0f;
        const float CliffFloatingY = 0.05f;
        const float PlatformThickness = 0.6f; // 아래로 두꺼운 박스 (시각적 단단함, 절벽으로 잠긴 부분)
        const float FallThresholdY = -3f;

        const int PlatformSeed = 1234;
        const int PlatformCount = 6;
        // 플랫폼 분산을 위한 최소 거리(m).
        const float PlatformMinSpacing = 2.5f;
        // 플랫폼 크기 (1~1.5m 정사각)
        const float PlatformMinSize = 1.0f;
        const float PlatformMaxSize = 1.4f;
        // 첫 플랫폼은 좌측 문 바로 안쪽에 고정 — P2 가 문을 통과하자마자 발 디딜 수 있게.
        // Z 방향 깊이 2m, 남쪽 끝이 챔버 south wall(z=3) 과 정확히 맞닿음.
        // X 방향 폭은 도어 폭 + 약간 여유 → 문 어느 쪽으로 들어와도 발판 위.
        static readonly Vector2 EntryPlatformPos = new Vector2(LeftDoorCenterX, LeftChamberZmin + 1.0f);
        static readonly Vector2 EntryPlatformSize = new Vector2(DoorOpeningWidth + 0.4f, 2.0f);

        // ===== Game logic positions =====
        const float FloorThickness = 0.05f;

        const float ZoneWidth = 1.4f;
        const float ZoneDepth = 1.4f;
        const float ZoneThickness = 0.01f;

        // GoalZone — 좌측 챔버 서쪽 끝 위치, 보너스 클리어 조건용
        static readonly Vector3 GoalZoneLocal = new Vector3(LeftChamberXmin + 1f, PlatformTopY + 1.5f, (LeftChamberZmin + LeftChamberZmax) * 0.5f);
        const float GoalTriggerHeight = 3f;

        // HintBoard / Catcher — 좌측 챔버 동쪽 (RoomCarpet 와 동일)
        static readonly Vector3 BoardLocal   = new Vector3(LeftChamberXmax - 0.6f, 0f, LeftChamberZmin + 1.5f);
        static readonly Vector3 CatcherLocal = new Vector3(LeftChamberXmax - 0.6f, 1.6f, LeftChamberZmin + 3.5f);
        const float CatcherTriggerRadius = 0.55f;
        const float BoardSlotSpacing = 0.28f;
        const int   BoardSlotCount = 5;
        const float BoardSlotY = 1.10f;
        const float BoardSlotRadius = 0.07f;

        // Dispenser / Launcher (2층)
        static readonly Vector3 DispenserLocal = new Vector3(RightChamberXmax - 1.5f, Floor2Y, Floor2Zmin + 2f);
        const float DispenserStandHeight = 1.0f;
        const float DispenserStandRadius = 0.10f;
        const float DispenserSpawnY = 1.10f;

        const float HolsterTopY = Floor2Y + 0.95f;
        static readonly Vector3 HolsterLocal  = new Vector3(RightChamberXmin + 0.7f, HolsterTopY - 0.025f, Floor2Zmin + 1.5f);
        static readonly Vector3 LauncherLocal = new Vector3(RightChamberXmin + 0.7f, HolsterTopY + 0.15f, Floor2Zmin + 1.5f);
        static readonly Quaternion LauncherRot = Quaternion.Euler(0f, -90f, 0f);
        const float LauncherMuzzleSpeed = 7.5f;
        const float LauncherMuzzleSpin = 2.5f;
        const float LauncherCooldown = 0.5f;

        static readonly Vector2 CarpetSize = new Vector2(0.9f, 1.2f);
        const float CarpetThickness = 0.02f;
        const float CarpetLifetime = 6f;        // 절벽에선 약간 더 길게 — 건너기 어려움 보상
        const float CarpetWarningSeconds = 1.5f;

        // HintBalls — 플랫폼 위(EntryPlatform 제외)에 하나씩 배치
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

        // ===== Menu =====

        [MenuItem("Tools/PipePuz/Stage3/Build Cliff Layout")]
        public static void Build()
        {
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid()) return;
            if (!activeScene.name.Contains("Stage3"))
            {
                if (!EditorUtility.DisplayDialog("RoomCliff",
                    $"활성 씬({activeScene.name})이 Stage3 가 아닙니다. 그래도 빌드?", "빌드", "취소")) return;
            }

            var root = GameObject.Find("RoomCliff");
            if (root == null)
            {
                root = new GameObject("RoomCliff");
                Undo.RegisterCreatedObjectUndo(root, "Create RoomCliff root");
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build RoomCliff");

            // 기존 자식 정리.
            string[] knownChildren =
            {
                "Architecture", "Entrance", "Corridor", "LeftChamber", "RightChamber",
                "ChamberDivider", "Stairs", "SecondFloor",
                "LeftDoor", "RightDoor",
                "Platforms", "Floor", "StartZone", "GoalZone",
                "Dispenser", "ActiveCarpets", "HintCatcher", "HintBoard", "HintBalls",
                "LauncherHolster", "CarpetLauncher",
            };
            foreach (var n in knownChildren) DestroyChildIfExists(root.transform, n);
            // 컨트롤러도 — 어느 쪽이든 다 제거
            var oldDis = root.GetComponent<DisappearingCarpetController>();
            if (oldDis != null) Undo.DestroyObjectImmediate(oldDis);
            var oldCliff = root.GetComponent<CliffController>();
            if (oldCliff != null) Undo.DestroyObjectImmediate(oldCliff);

            // Materials
            var wallMat       = MakeUrpMaterial("Cliff_WallMat",       new Color(0.32f, 0.34f, 0.40f), false);
            var corridorMat   = MakeUrpMaterial("Cliff_CorridorMat",   new Color(0.28f, 0.30f, 0.36f), false);
            var doorFrameMat  = MakeUrpMaterial("Cliff_DoorFrameMat",  new Color(0.18f, 0.20f, 0.24f), false);
            var doorPanelMat  = MakeEmissiveMaterial("Cliff_DoorPanelMat",
                new Color(0.18f, 0.55f, 0.85f), new Color(0.35f, 0.85f, 1.4f) * 0.6f);
            var stairMat      = MakeUrpMaterial("Cliff_StairMat",      new Color(0.42f, 0.42f, 0.45f), false);
            var floor2Mat     = MakeUrpMaterial("Cliff_Floor2Mat",     new Color(0.30f, 0.32f, 0.38f), false);
            var rightFloorMat = MakeUrpMaterial("Cliff_RightFloorMat", new Color(0.38f, 0.40f, 0.45f), false);
            var platformMat   = MakeEmissiveMaterial("Cliff_PlatformMat",
                new Color(0.55f, 0.45f, 0.30f), new Color(1.0f, 0.65f, 0.30f) * 0.4f);
            var entryPlatformMat = MakeEmissiveMaterial("Cliff_EntryPlatformMat",
                new Color(0.15f, 0.7f, 0.30f), new Color(0.25f, 1.4f, 0.5f) * 0.7f);

            var goalMat    = MakeEmissiveMaterial("Cliff_GoalMat",
                new Color(0.20f, 0.55f, 1.0f), new Color(0.35f, 0.85f, 1.6f) * 0.9f);
            var carpetMat  = MakeUrpMaterial("Cliff_CarpetMat", new Color(0.70f, 0.45f, 0.25f), false);
            var catcherMat = MakeEmissiveMaterial("Cliff_CatcherMat",
                new Color(0.25f, 0.45f, 0.95f), new Color(0.45f, 0.75f, 1.6f) * 0.6f);
            var boardMat   = MakeUrpMaterial("Cliff_BoardMat", new Color(0.18f, 0.18f, 0.22f), false);
            var slotMat    = MakeUrpMaterial("Cliff_SlotMat",  new Color(0.50f, 0.50f, 0.55f), false);
            var standMat   = MakeUrpMaterial("Cliff_StandMat", new Color(0.35f, 0.32f, 0.30f), false);

            var ctrl = root.AddComponent<CliffController>();

            // ===== Architecture =====
            var arch = new GameObject("Architecture");
            arch.transform.SetParent(root.transform, false);

            BuildEntrance(arch.transform, corridorMat, wallMat);
            BuildCorridor(arch.transform, corridorMat, wallMat, doorFrameMat);
            BuildLeftChamberWalls(arch.transform, wallMat);
            BuildRightChamberWalls(arch.transform, wallMat, rightFloorMat);
            BuildChamberDivider(arch.transform, wallMat);

            float doorZ = CorridorZmax + WallThickness * 0.5f;
            BuildSideDoor(arch.transform, "LeftDoor",
                localPos: new Vector3(LeftDoorCenterX, 0f, doorZ),
                doorPanelMat);
            BuildSideDoor(arch.transform, "RightDoor",
                localPos: new Vector3(RightDoorCenterX, 0f, doorZ),
                doorPanelMat);

            BuildStairs(arch.transform, stairMat);
            BuildSecondFloor(arch.transform, floor2Mat);

            // ===== Platforms (Cliff 핵심) =====
            var platformSpecs = GeneratePlatformPositions();
            var platformsGroup = new GameObject("Platforms");
            platformsGroup.transform.SetParent(root.transform, false);
            platformsGroup.transform.localPosition = Vector3.zero;

            var cliffPlatforms = new List<CliffPlatform>();
            for (int i = 0; i < platformSpecs.Count; i++)
            {
                var spec = platformSpecs[i];
                bool isEntry = (i == 0); // 첫 번째는 입구 진입대
                var mat = isEntry ? entryPlatformMat : platformMat;
                var cp = BuildPlatform(platformsGroup.transform, $"Platform_{i + 1}{(isEntry ? "_Entry" : "")}", spec, mat);
                cliffPlatforms.Add(cp);
            }

            // StartZone = 첫(entry) 플랫폼의 dock 으로 사용. 시각도 작은 emissive 표시 추가.
            Transform defaultSpawn = cliffPlatforms.Count > 0 ? cliffPlatforms[0].GetDock() : null;

            // ===== GoalZone (옵션 보조 클리어) =====
            var goal = new GameObject("GoalZone");
            goal.transform.SetParent(root.transform, false);
            goal.transform.localPosition = GoalZoneLocal;
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

            // ===== Dispenser (Floating mode) =====
            var disp = new GameObject("Dispenser");
            disp.transform.SetParent(root.transform, false);
            disp.transform.localPosition = DispenserLocal;
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "Stand"; DisableColliderIfAny(stand);
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
            // *** Cliff 핵심: floating mode 활성 ***
            dispComp.UseFloatingMode = true;
            dispComp.FloatingY = CliffFloatingY;

            var active = new GameObject("ActiveCarpets");
            active.transform.SetParent(root.transform, false);
            active.transform.localPosition = Vector3.zero;
            dispComp.ActiveCarpetsRoot = active.transform;

            // Holster + Launcher
            var holster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            holster.name = "LauncherHolster";
            holster.transform.SetParent(root.transform, false);
            holster.transform.localPosition = HolsterLocal;
            holster.transform.localScale = new Vector3(0.45f, 0.05f, 0.30f);
            AssignMat(holster, standMat);

            var launcher = BuildLauncher(root.transform, carpetMat, active.transform);

            // HintBoard / Catcher / HintBalls
            var board = BuildHintBoard(root.transform, boardMat, slotMat);
            var catcher = BuildHintCatcher(root.transform, catcherMat, board.GetComponent<HintPuzzleBoard>());

            // HintBalls — 첫 entry platform 제외, 나머지 5개 플랫폼 위에 하나씩.
            BuildHintBallsOnPlatforms(root.transform, cliffPlatforms);

            // ===== Controller wire-up =====
            ctrl.HintBoard = board.GetComponent<HintPuzzleBoard>();
            ctrl.Goal = goalComp;
            ctrl.DefaultSpawnPoint = defaultSpawn;
            ctrl.FallThresholdY = root.transform.position.y + FallThresholdY; // 월드 Y 기준으로 변환
            ctrl.PlatformDetectMaxDist = 3f;
            ctrl.PlatformDetectMask = ~0;
            ctrl.RespawnCooldown = 1f;
            // XROriginRef 는 비워둠 — runtime Start 에서 자동 검색.

            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[Cliff] Build 완료. 6 platforms + floating carpets. " +
                      "낙하 임계 y=" + ctrl.FallThresholdY + " (world). " +
                      "RoomCliff 의 world position 을 옮기면 전체가 따라감.");
        }

        // ===== Platforms =====

        /// <summary>고정 시드 RNG 로 6개 플랫폼 위치 생성. 첫 번째는 입구(고정), 나머지 5개는 챔버 내부 랜덤.</summary>
        static List<PlatformSpec> GeneratePlatformPositions()
        {
            var list = new List<PlatformSpec>();
            // Entry platform — fixed
            list.Add(new PlatformSpec {
                PosXZ = EntryPlatformPos,
                TopY = PlatformTopY,
                Size = EntryPlatformSize,
            });

            var rng = new System.Random(PlatformSeed);

            // 후보 영역: 챔버 안쪽으로 margin
            float xMin = LeftChamberXmin + 1f;
            float xMax = LeftChamberXmax - 1f;
            float zMin = LeftChamberZmin + 1f;
            float zMax = LeftChamberZmax - 1f;

            int safety = 200;
            while (list.Count < PlatformCount && safety-- > 0)
            {
                float x = (float)(rng.NextDouble() * (xMax - xMin) + xMin);
                float z = (float)(rng.NextDouble() * (zMax - zMin) + zMin);
                var pos = new Vector2(x, z);

                bool tooClose = false;
                foreach (var prev in list)
                {
                    if ((prev.PosXZ - pos).sqrMagnitude < PlatformMinSpacing * PlatformMinSpacing)
                    {
                        tooClose = true; break;
                    }
                }
                if (tooClose) continue;

                float sx = Mathf.Lerp(PlatformMinSize, PlatformMaxSize, (float)rng.NextDouble());
                float sz = Mathf.Lerp(PlatformMinSize, PlatformMaxSize, (float)rng.NextDouble());

                list.Add(new PlatformSpec {
                    PosXZ = pos,
                    TopY = PlatformTopY,
                    Size = new Vector2(sx, sz),
                });
            }
            return list;
        }

        static CliffPlatform BuildPlatform(Transform parent, string name, PlatformSpec spec, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            // 플랫폼 root 는 top 위치 — 자식 dock 등 로컬 기준
            go.transform.localPosition = new Vector3(spec.PosXZ.x, spec.TopY, spec.PosXZ.y);

            // 시각/콜라이더 (cube) — 윗면이 top 에 오도록 자식으로 살짝 아래로
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vis.name = "Visual";
            vis.transform.SetParent(go.transform, false);
            vis.transform.localPosition = new Vector3(0f, -PlatformThickness * 0.5f, 0f);
            vis.transform.localScale = new Vector3(spec.Size.x, PlatformThickness, spec.Size.y);
            AssignMat(vis, mat);

            // Dock — 리스폰 시 player root(XR Origin) 의 floor 가 정렬될 위치. 플랫폼 윗면(top) 과 동일.
            var dock = new GameObject("Dock");
            dock.transform.SetParent(go.transform, false);
            dock.transform.localPosition = Vector3.zero; // platform root 가 이미 윗면(y=PlatformTopY) 위치

            var cp = go.AddComponent<CliffPlatform>();
            cp.Dock = dock.transform;
            return cp;
        }

        // ===== HintBalls on platforms =====

        static void BuildHintBallsOnPlatforms(Transform parent, List<CliffPlatform> platforms)
        {
            var ballsRoot = new GameObject("HintBalls");
            ballsRoot.transform.SetParent(parent, false);

            int count = Mathf.Min(HintBallCount, Mathf.Max(0, platforms.Count - 1));
            for (int i = 0; i < count; i++)
            {
                var platform = platforms[i + 1]; // entry(idx 0) 제외
                var color = HintBallColors[i % HintBallColors.Length];
                var ballMat = MakeUrpMaterial($"Cliff_HintBallMat_{i}", color, false);
                if (ballMat.HasProperty("_EmissionColor"))
                {
                    ballMat.SetColor("_EmissionColor", color * 0.4f);
                    ballMat.EnableKeyword("_EMISSION");
                }

                var ball = new GameObject($"HintBall_{i + 1}");
                ball.transform.SetParent(ballsRoot.transform, false);
                // 플랫폼 윗면(top) 위에 ball 반경만큼 떠서 안착
                var p = platform.transform.localPosition;
                ball.transform.localPosition = new Vector3(p.x, p.y + HintBallRadius + 0.01f, p.z);

                var ballVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ballVis.name = "Visual"; DisableColliderIfAny(ballVis);
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

        // ===== Architecture builders (Stage3LayoutSetup 와 동일, localPosition 사용) =====

        static void BuildEntrance(Transform parent, Material floorMat, Material wallMat)
        {
            var ent = new GameObject("Entrance");
            ent.transform.SetParent(parent, false);
            float xCenter = (EntranceXmin + EntranceXmax) * 0.5f;
            float xLen = EntranceXmax - EntranceXmin;
            float zCenter = (EntranceZmin + EntranceZmax) * 0.5f;
            float zLen = EntranceZmax - EntranceZmin;

            MakeBoxLocal(ent.transform, "Floor",
                new Vector3(xCenter, -0.025f, zCenter),
                new Vector3(xLen, 0.05f, zLen), floorMat);
            MakeBoxLocal(ent.transform, "Wall_W",
                new Vector3(EntranceXmin - WallThickness * 0.5f, EntranceHeight * 0.5f, zCenter),
                new Vector3(WallThickness, EntranceHeight, zLen), wallMat);
            MakeBoxLocal(ent.transform, "Wall_E",
                new Vector3(EntranceXmax + WallThickness * 0.5f, EntranceHeight * 0.5f, zCenter),
                new Vector3(WallThickness, EntranceHeight, zLen), wallMat);
        }

        static void BuildCorridor(Transform parent, Material floorMat, Material wallMat, Material doorFrameMat)
        {
            var corr = new GameObject("Corridor");
            corr.transform.SetParent(parent, false);
            float xCenter = (CorridorXmin + CorridorXmax) * 0.5f;
            float xLen = CorridorXmax - CorridorXmin;
            float zCenter = (CorridorZmin + CorridorZmax) * 0.5f;
            float zLen = CorridorZmax - CorridorZmin;

            MakeBoxLocal(corr.transform, "Floor",
                new Vector3(xCenter, -0.025f, zCenter),
                new Vector3(xLen, 0.05f, zLen), floorMat);

            BuildWallWithOpening(corr.transform, "Wall_S",
                axisIsX: false,
                fixedCoord: CorridorZmin - WallThickness * 0.5f,
                openingCenter: (EntranceXmin + EntranceXmax) * 0.5f,
                openingHalfWidth: (EntranceXmax - EntranceXmin) * 0.5f,
                wallStart: CorridorXmin - 0.001f,
                wallEnd: CorridorXmax + 0.001f,
                wallY: CorridorHeight,
                openingHeight: CorridorHeight,
                wallThickness: WallThickness,
                wallMat: wallMat, frameMat: doorFrameMat);

            MakeBoxLocal(corr.transform, "Wall_W",
                new Vector3(CorridorXmin - WallThickness * 0.5f, CorridorHeight * 0.5f, zCenter),
                new Vector3(WallThickness, CorridorHeight, zLen + WallThickness * 2f), wallMat);
            MakeBoxLocal(corr.transform, "Wall_E",
                new Vector3(CorridorXmax + WallThickness * 0.5f, CorridorHeight * 0.5f, zCenter),
                new Vector3(WallThickness, CorridorHeight, zLen + WallThickness * 2f), wallMat);

            float chamberWallY = LeftChamberWallY;
            float halfDoor = DoorOpeningWidth * 0.5f;

            // 북쪽 벽 fragment 4개 + 헤더 2개
            float f1Min = CorridorXmin;
            float f1Max = LeftDoorCenterX - halfDoor;
            if (f1Max > f1Min + 0.01f)
                MakeBoxLocal(corr.transform, "Wall_N_F1",
                    new Vector3((f1Min + f1Max) * 0.5f, chamberWallY * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    new Vector3(f1Max - f1Min, chamberWallY, WallThickness), wallMat);
            if (chamberWallY > DoorHeight + 0.01f)
                MakeBoxLocal(corr.transform, "Wall_N_D1Hdr",
                    new Vector3(LeftDoorCenterX, (DoorHeight + chamberWallY) * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    new Vector3(DoorOpeningWidth, chamberWallY - DoorHeight, WallThickness), doorFrameMat);
            float f2Min = LeftDoorCenterX + halfDoor;
            float f2Max = RightDoorCenterX - halfDoor;
            if (f2Max > f2Min + 0.01f)
                MakeBoxLocal(corr.transform, "Wall_N_F2",
                    new Vector3((f2Min + f2Max) * 0.5f, chamberWallY * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    new Vector3(f2Max - f2Min, chamberWallY, WallThickness), wallMat);
            if (chamberWallY > DoorHeight + 0.01f)
                MakeBoxLocal(corr.transform, "Wall_N_D2Hdr",
                    new Vector3(RightDoorCenterX, (DoorHeight + chamberWallY) * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    new Vector3(DoorOpeningWidth, chamberWallY - DoorHeight, WallThickness), doorFrameMat);
            float f3Min = RightDoorCenterX + halfDoor;
            float f3Max = CorridorXmax;
            if (f3Max > f3Min + 0.01f)
                MakeBoxLocal(corr.transform, "Wall_N_F3",
                    new Vector3((f3Min + f3Max) * 0.5f, chamberWallY * 0.5f, CorridorZmax + WallThickness * 0.5f),
                    new Vector3(f3Max - f3Min, chamberWallY, WallThickness), wallMat);
        }

        static void BuildLeftChamberWalls(Transform parent, Material wallMat)
        {
            var chamber = new GameObject("LeftChamber");
            chamber.transform.SetParent(parent, false);
            float xCenter = (LeftChamberXmin + LeftChamberXmax) * 0.5f;
            float xLen = LeftChamberXmax - LeftChamberXmin;
            float zCenter = (LeftChamberZmin + LeftChamberZmax) * 0.5f;
            float zLen = LeftChamberZmax - LeftChamberZmin;

            MakeBoxLocal(chamber.transform, "Wall_W",
                new Vector3(LeftChamberXmin - WallThickness * 0.5f, LeftChamberWallY * 0.5f, zCenter),
                new Vector3(WallThickness, LeftChamberWallY, zLen + WallThickness * 2f), wallMat);
            MakeBoxLocal(chamber.transform, "Wall_N",
                new Vector3(xCenter, LeftChamberWallY * 0.5f, LeftChamberZmax + WallThickness * 0.5f),
                new Vector3(xLen, LeftChamberWallY, WallThickness), wallMat);
        }

        static void BuildRightChamberWalls(Transform parent, Material wallMat, Material floorMat)
        {
            var chamber = new GameObject("RightChamber");
            chamber.transform.SetParent(parent, false);
            float xCenter = (RightChamberXmin + RightChamberXmax) * 0.5f;
            float xLen = RightChamberXmax - RightChamberXmin;
            float zCenter = (RightChamberZmin + RightChamberZmax) * 0.5f;
            float zLen = RightChamberZmax - RightChamberZmin;

            MakeBoxLocal(chamber.transform, "Floor1F",
                new Vector3(xCenter, -0.025f, zCenter),
                new Vector3(xLen, 0.05f, zLen), floorMat);
            MakeBoxLocal(chamber.transform, "Wall_E",
                new Vector3(RightChamberXmax + WallThickness * 0.5f, RightChamberWallY * 0.5f, zCenter),
                new Vector3(WallThickness, RightChamberWallY, zLen + WallThickness * 2f), wallMat);
            MakeBoxLocal(chamber.transform, "Wall_N",
                new Vector3(xCenter, RightChamberWallY * 0.5f, RightChamberZmax + WallThickness * 0.5f),
                new Vector3(xLen, RightChamberWallY, WallThickness), wallMat);
        }

        static void BuildChamberDivider(Transform parent, Material wallMat)
        {
            var div = new GameObject("ChamberDivider");
            div.transform.SetParent(parent, false);
            float sMin = LeftChamberZmin;
            float sMax = Floor2Zmin;
            MakeBoxLocal(div.transform, "Div_S",
                new Vector3(LeftChamberXmax + WallThickness * 0.5f, RightChamberWallY * 0.5f, (sMin + sMax) * 0.5f),
                new Vector3(WallThickness, RightChamberWallY, sMax - sMin), wallMat);
            float nMin = Floor2Zmin;
            float nMax = LeftChamberZmax;
            MakeBoxLocal(div.transform, "Div_N",
                new Vector3(LeftChamberXmax + WallThickness * 0.5f, Floor2Y * 0.5f, (nMin + nMax) * 0.5f),
                new Vector3(WallThickness, Floor2Y, nMax - nMin), wallMat);
        }

        static GameObject BuildSideDoor(Transform parent, string name, Vector3 localPos, Material doorPanelMat)
        {
            var door = new GameObject(name);
            door.transform.SetParent(parent, false);
            door.transform.localPosition = localPos;

            float halfPanelWidth = DoorOpeningWidth * 0.5f;
            float panelWidthX = halfPanelWidth;

            // 벽이 X 축 따라 뻗으므로 패널은 X 방향으로 wide, Z 방향으로 thin — 벽과 같은 평면.
            var leftPanel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftPanel.name = "LeftPanel";
            leftPanel.transform.SetParent(door.transform, false);
            leftPanel.transform.localPosition = new Vector3(-halfPanelWidth * 0.5f, DoorHeight * 0.5f, 0f);
            leftPanel.transform.localScale = new Vector3(panelWidthX, DoorHeight, DoorPanelThickness);
            AssignMat(leftPanel, doorPanelMat);

            var rightPanel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightPanel.name = "RightPanel";
            rightPanel.transform.SetParent(door.transform, false);
            rightPanel.transform.localPosition = new Vector3(+halfPanelWidth * 0.5f, DoorHeight * 0.5f, 0f);
            rightPanel.transform.localScale = new Vector3(panelWidthX, DoorHeight, DoorPanelThickness);
            AssignMat(rightPanel, doorPanelMat);

            // 감지 트리거 — 문 X 폭 + 양쪽 1m, Z 방향 ±2m (복도/챔버 양쪽 커버).
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

        static void BuildStairs(Transform parent, Material stairMat)
        {
            var stairs = new GameObject("Stairs");
            stairs.transform.SetParent(parent, false);
            for (int i = 0; i < StairStepCount; i++)
            {
                float yTop = (i + 1) * StairStepRiser;
                float zMin = StairStartZ + i * StairStepDepth;
                float zCenter = zMin + StairStepDepth * 0.5f;
                MakeBoxLocal(stairs.transform, $"Step_{i + 1}",
                    new Vector3(StairCenterX, yTop * 0.5f, zCenter),
                    new Vector3(StairStepWidth, yTop, StairStepDepth), stairMat);
            }
        }

        static void BuildSecondFloor(Transform parent, Material floor2Mat)
        {
            var sf = new GameObject("SecondFloor");
            sf.transform.SetParent(parent, false);
            float xCenter = (RightChamberXmin + RightChamberXmax) * 0.5f;
            float xLen = RightChamberXmax - RightChamberXmin;
            float zCenter = (Floor2Zmin + RightChamberZmax) * 0.5f;
            float zLen = RightChamberZmax - Floor2Zmin;

            MakeBoxLocal(sf.transform, "Slab",
                new Vector3(xCenter, Floor2Y - Floor2Thickness * 0.5f, zCenter),
                new Vector3(xLen, Floor2Thickness, zLen), floor2Mat);
        }

        static GameObject BuildLauncher(Transform parent, Material carpetMat, Transform active)
        {
            var gunGripMat   = MakeUrpMaterial("Cliff_GunGripMat",   new Color(0.15f, 0.15f, 0.18f), false);
            var gunMetalMat  = MakeUrpMaterial("Cliff_GunMetalMat",  new Color(0.45f, 0.47f, 0.52f), false);
            var gunAccentMat = MakeUrpMaterial("Cliff_GunAccentMat", new Color(0.80f, 0.30f, 0.15f), false);

            var launcher = new GameObject("CarpetLauncher");
            launcher.transform.SetParent(parent, false);
            launcher.transform.localPosition = LauncherLocal;
            launcher.transform.localRotation = LauncherRot;

            // 부속 비주얼 (Stage3 와 동일)
            var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grip.name = "Grip"; DisableColliderIfAny(grip);
            grip.transform.SetParent(launcher.transform, false);
            grip.transform.localPosition = new Vector3(0f, -0.075f, -0.015f);
            grip.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            grip.transform.localScale = new Vector3(0.045f, 0.15f, 0.065f);
            AssignMat(grip, gunGripMat);

            var receiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
            receiver.name = "Receiver"; DisableColliderIfAny(receiver);
            receiver.transform.SetParent(launcher.transform, false);
            receiver.transform.localPosition = new Vector3(0f, 0.015f, 0.09f);
            receiver.transform.localScale = new Vector3(0.06f, 0.075f, 0.22f);
            AssignMat(receiver, gunGripMat);

            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel"; DisableColliderIfAny(barrel);
            barrel.transform.SetParent(launcher.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 0.015f, 0.28f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.035f, 0.10f, 0.035f);
            AssignMat(barrel, gunMetalMat);

            var muzzleRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            muzzleRing.name = "MuzzleRing"; DisableColliderIfAny(muzzleRing);
            muzzleRing.transform.SetParent(launcher.transform, false);
            muzzleRing.transform.localPosition = new Vector3(0f, 0.015f, 0.40f);
            muzzleRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            muzzleRing.transform.localScale = new Vector3(0.05f, 0.012f, 0.05f);
            AssignMat(muzzleRing, gunMetalMat);

            var guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            guard.name = "TriggerGuard"; DisableColliderIfAny(guard);
            guard.transform.SetParent(launcher.transform, false);
            guard.transform.localPosition = new Vector3(0f, -0.030f, 0.04f);
            guard.transform.localScale = new Vector3(0.025f, 0.035f, 0.025f);
            AssignMat(guard, gunGripMat);

            var trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trigger.name = "Trigger"; DisableColliderIfAny(trigger);
            trigger.transform.SetParent(launcher.transform, false);
            trigger.transform.localPosition = new Vector3(0f, -0.025f, 0.035f);
            trigger.transform.localScale = new Vector3(0.012f, 0.022f, 0.008f);
            AssignMat(trigger, gunAccentMat);

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
            // *** Cliff 핵심 ***
            launcherComp.UseFloatingMode = true;
            launcherComp.FloatingY = CliffFloatingY;
            return launcher;
        }

        static GameObject BuildHintBoard(Transform parent, Material boardMat, Material slotMat)
        {
            var board = new GameObject("HintBoard");
            board.transform.SetParent(parent, false);
            board.transform.localPosition = BoardLocal;
            board.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);

            var boardVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boardVis.name = "Visual"; DisableColliderIfAny(boardVis);
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
                cup.name = "Cup"; DisableColliderIfAny(cup);
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
            catcher.transform.localPosition = CatcherLocal;

            var catcherTrigger = catcher.AddComponent<SphereCollider>();
            catcherTrigger.radius = CatcherTriggerRadius;
            catcherTrigger.isTrigger = true;

            var catcherVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            catcherVis.name = "Visual"; DisableColliderIfAny(catcherVis);
            catcherVis.transform.SetParent(catcher.transform, false);
            catcherVis.transform.localPosition = Vector3.zero;
            catcherVis.transform.localScale = Vector3.one * (CatcherTriggerRadius * 2f);
            AssignMat(catcherVis, catcherMat);

            var catcherComp = catcher.AddComponent<HintCatcher>();
            catcherComp.Board = board;
            return catcher;
        }

        // ===== Helpers =====

        static void BuildWallWithOpening(
            Transform parent, string baseName,
            bool axisIsX, float fixedCoord,
            float openingCenter, float openingHalfWidth,
            float wallStart, float wallEnd,
            float wallY, float openingHeight,
            float wallThickness,
            Material wallMat, Material frameMat)
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
                MakeBoxLocal(parent, $"{baseName}_Side1", c, s, wallMat);
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
                MakeBoxLocal(parent, $"{baseName}_Side2", c, s, wallMat);
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
                MakeBoxLocal(parent, $"{baseName}_Header", c, s, frameMat);
            }
        }

        static GameObject MakeBoxLocal(Transform parent, string name, Vector3 localCenter, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
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
