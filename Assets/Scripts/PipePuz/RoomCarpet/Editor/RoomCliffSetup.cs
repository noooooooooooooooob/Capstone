using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using PipePuz.LightBeam;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Stage3/Build Cliff Layout.
    ///
    /// RoomCarpet 와 **같은 모양(architecture)** 의 레이아웃을 RoomCliff GameObject 안에 빌드하되,
    /// 두 가지 핵심 메커닉을 교체:
    ///
    ///   1. 좌측 챔버의 **빨간 위험 카펫 바닥 → 절벽(cliff)**
    ///      - CarpetFloor 미배치, 챔버 바닥이 비어있음 (추락 가능)
    ///      - 6개의 <see cref="CliffPlatform"/> 발판 (3개는 거울 받침대 겸용, 3개는 순수 발판)
    ///      - <see cref="CliffController"/> 가 카메라 Y &lt; FallThresholdY 시 마지막 발판으로 리스폰
    ///      - 카펫은 <see cref="DisappearingCarpet.UseFloatingMode"/> = true → y=FloatingY 에 anchor
    ///        (위험 바닥 충돌 대신 일정 높이에 떠있는 임시 발판)
    ///
    ///   2. **HintBall + HintBoard + HintCatcher (색깔 공 퍼즐) → 빛 굴절 퍼즐**
    ///      - 챔버 분리벽 동쪽에 <see cref="LightBeamEmitter"/> (서쪽으로 발사)
    ///      - 3 거울 (cliff platform 위 받침대에 부착) — P2 가 잡고 회전 (PointTowardHand)
    ///      - 좌측 챔버 남쪽 벽에 <see cref="LightBeamReceiver"/>
    ///      - <see cref="LightBeamController"/> 가 매 프레임 raycast + reflect 로 광선 계산
    ///      - 모든 거울 정렬 시 receiver hit → OnAllReceiversHit
    ///
    /// 모든 좌표는 RoomCliff GameObject 의 LOCAL — RoomCliff 옮기면 통째로 따라감.
    /// </summary>
    public static class RoomCliffSetup
    {
        // ===== Architecture constants (Stage3LayoutSetup 와 동일) =====
        const float WallThickness = 0.2f;

        const float EntranceXmin = -3f;
        const float EntranceXmax = +3f;
        const float EntranceZmin = -3f;
        const float EntranceZmax = 0f;
        const float EntranceHeight = 3f;

        // NOTE: 챔버 X 확장과 함께 복도도 서쪽으로 연장.
        const float CorridorXmin = -22f;
        const float CorridorXmax = +7f;
        const float CorridorZmin = 0f;
        const float CorridorZmax = 3f;
        const float CorridorHeight = 3f;

        const float LeftDoorCenterX = -4f;
        const float RightDoorCenterX = +4f;
        const float DoorOpeningWidth = 2f;
        const float DoorHeight = 2.2f;
        const float DoorPanelThickness = 0.08f;

        // 챔버 확장: 폭 13.5m → 23.5m, 깊이 11m → 15m.
        // 점프(VR 일반 사거리 ~3m) 으로는 발판 사이를 절대 못 건너가도록 면적 확보.
        const float LeftChamberXmin = -22f;
        const float LeftChamberXmax = +1.5f;
        const float LeftChamberZmin = +3f;
        const float LeftChamberZmax = +18f;
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
        const float CliffFloatingY = 0.05f;    // 카펫이 멈출 높이 — 거의 floor level
        const float PlatformTopY = 0f;          // cliff platform 윗면
        const float PlatformThickness = 0.6f;
        const float FallThresholdY_RelativeToRoot = -3f;

        const int PlatformSeed = 1234;
        // 점프 차단: 모든 발판이 다른 발판과 최소 5m 떨어져야 P2 가 카펫 없이는 못 건너감.
        const float PlatformMinSpacing = 5.0f;
        const float PlatformMinSize = 1.0f;
        const float PlatformMaxSize = 1.4f;

        // Entry platform — 좌측 문 안쪽 진입대
        static readonly Vector2 EntryPlatformPos = new Vector2(LeftDoorCenterX, LeftChamberZmin + 1.0f);
        static readonly Vector2 EntryPlatformSize = new Vector2(DoorOpeningWidth + 0.4f, 2.0f);

        // ===== Light beam =====
        const float BeamY = 1.3f;               // 광선 수평면
        const float MirrorPedestalTopY = 0.8f;  // 거울 회전 pivot — platform top 위로 0.8m
        const float MirrorPedestalRadius = 0.12f;
        const float MirrorVisualWidth = 0.7f;
        const float MirrorVisualHeight = 1.0f;
        const float MirrorVisualThickness = 0.05f;
        // Mirror visual center Y (mirror pivot 기준) = BeamY - MirrorPedestalTopY = 0.5
        static readonly float MirrorVisualCenterLocalY = BeamY - MirrorPedestalTopY;

        // ===== 거울 (4개, 모두 x/z 좌표 unique, 인접 거리 ≥ 5m) =====
        //   M1 Red    (-2,  9)  ColorId=0
        //   M2 Green  (-7,  16) ColorId=1
        //   M3 Blue   (-15, 12) ColorId=2
        //   M4 Yellow (-19, 6)  ColorId=3
        //   Entry     (-4,  4)
        // x 집합 {-4, -2, -7, -15, -19}, z 집합 {4, 9, 16, 12, 6} — 모두 고유.
        const int MirrorCount = 4;
        static readonly Vector2 Mirror1Pos = new Vector2(-2f, 9f);
        static readonly Vector2 Mirror2Pos = new Vector2(-7f, 16f);
        static readonly Vector2 Mirror3Pos = new Vector2(-15f, 12f);
        static readonly Vector2 Mirror4Pos = new Vector2(-19f, 6f);
        static readonly Vector2 MirrorPlatformSize = new Vector2(1.5f, 1.5f);

        // 거울 색상 팔레트 (ColorOrderPanel 의 ColorPalette 와 일치)
        static readonly Color[] MirrorColors =
        {
            new Color(0.95f, 0.20f, 0.20f), // 0 Red
            new Color(0.25f, 0.85f, 0.35f), // 1 Green
            new Color(0.25f, 0.55f, 0.95f), // 2 Blue
            new Color(0.95f, 0.85f, 0.25f), // 3 Yellow
        };
        static readonly string[] MirrorColorNames = { "Red", "Green", "Blue", "Yellow" };

        // ===== 사전 정의된 거울 통과 순서 =====
        // 디자이너가 이 배열만 바꾸면 순서 변경 가능. ColorOrderPanel 은 이를 디스플레이에만 표시(읽기 전용).
        // 기본: Red → Green → Blue → Yellow
        static readonly int[] PreSetMirrorOrder = { 0, 1, 2, 3 };

        // Emitter / Receiver — emitter Z 초기값. BeamAimController 가 런타임에 갱신.
        static readonly Vector3 EmitterLocal = new Vector3(LeftChamberXmax - 0.1f, BeamY, EmitterInitialZ);
        static readonly Quaternion EmitterRot = Quaternion.Euler(0f, -90f, 0f); // forward = -X
        static readonly Vector3 ReceiverLocal = new Vector3(-10f, BeamY, LeftChamberZmin + 0.3f);
        static readonly Quaternion ReceiverRot = Quaternion.Euler(0f, 0f, 0f); // forward = +Z

        // 초기 거울 yaw — P2 가 PointTowardHand 로 손 움직여 맞춤. 정답 yaw 는 동적(순서·위치 따라 변함).
        const float InitialMirrorYaw = 0f;

        // ===== Color Order Panel (2층 표시 전용) =====
        static readonly Vector3 ColorPanelBaseLocal = new Vector3(5.5f, Floor2Y, 13f);
        const float ColorPanelStandHeight = 0.9f;
        const float ColorPanelSlotSpacing = 0.12f;
        const float ColorPanelSlotSize = 0.06f;

        // ===== Beam Aim Slider (2층) — knob X → emitter Z =====
        // Knob 중심(X=0) 일 때 emitter z=10 이 되도록 범위 대칭.
        // 거울 z 값: M1=9, M2=16, M3=12, M4=6 — [3, 17] 범위면 모두 닿음, 중심 = (3+17)/2 = 10 ✓
        static readonly Vector3 AimControlBaseLocal = new Vector3(3.5f, Floor2Y, 13f);
        const float AimControlStandHeight = 0.9f;
        const float AimKnobTrackMin = -0.30f;
        const float AimKnobTrackMax = +0.30f;
        const float EmitterInitialZ = 10f;      // 시작 z = 슬라이더 중심
        const float EmitterSlideMinZ = 3f;      // 양 끝 ±7 → 중심 10 대칭
        const float EmitterSlideMaxZ = 17f;
        // 슬라이더 방향 반전 — P1 이 knob 을 미는 방향과 빔이 챔버에서 움직이는 방향이 일치하도록.
        const bool InvertEmitterMapping = true;

        // ===== Game logic positions =====
        const float ZoneWidth = 1.4f;
        const float ZoneDepth = 1.4f;
        const float ZoneThickness = 0.01f;

        // Dispenser / Launcher (2층)
        static readonly Vector3 DispenserLocal = new Vector3(RightChamberXmax - 1.5f, Floor2Y, Floor2Zmin + 2f);
        const float DispenserStandHeight = 1.0f;
        const float DispenserStandRadius = 0.10f;
        const float DispenserSpawnY = 1.10f;

        const float HolsterTopY = Floor2Y + 0.95f;
        static readonly Vector3 HolsterLocal  = new Vector3(RightChamberXmin + 0.7f, HolsterTopY - 0.025f, Floor2Zmin + 1.5f);
        static readonly Vector3 LauncherLocal = new Vector3(RightChamberXmin + 0.7f, HolsterTopY + 0.15f, Floor2Zmin + 1.5f);
        static readonly Quaternion LauncherRot = Quaternion.Euler(0f, -90f, 0f); // forward = -X
        // 챔버 폭 23.5m 으로 확장 — 30° 아크 발사 시 약 26m 사거리 필요. 7.5 → 10 m/s.
        const float LauncherMuzzleSpeed = 10f;
        const float LauncherMuzzleSpin = 2.5f;
        const float LauncherCooldown = 0.5f;

        static readonly Vector2 CarpetSize = new Vector2(0.9f, 1.2f);
        const float CarpetThickness = 0.02f;
        const float CarpetLifetime = 6f;
        const float CarpetWarningSeconds = 1.5f;

        struct PlatformSpec { public Vector2 PosXZ; public Vector2 Size; public bool IsEntry; public bool HasMirror; public int MirrorIndex; }

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
            Undo.SetCurrentGroupName("Build RoomCliff (Cliff + LightBeam)");

            // 기존 자식 정리.
            string[] knownChildren =
            {
                "Architecture", "Entrance", "Corridor", "LeftChamber", "RightChamber",
                "ChamberDivider", "Stairs", "SecondFloor", "LeftDoor", "RightDoor",
                "Platforms", "StartZone",
                "Dispenser", "ActiveCarpets", "LauncherHolster", "CarpetLauncher",
                "LightBeamPuzzle",
                "ColorOrderPanel", "BeamAimController",
            };
            foreach (var n in knownChildren) DestroyChildIfExists(root.transform, n);
            var oldCliff = root.GetComponent<CliffController>();
            if (oldCliff != null) Undo.DestroyObjectImmediate(oldCliff);

            // Materials
            var wallMat       = MakeUrpMaterial("Cliff_WallMat",      new Color(0.32f, 0.34f, 0.40f), false);
            var corridorMat   = MakeUrpMaterial("Cliff_CorridorMat",  new Color(0.28f, 0.30f, 0.36f), false);
            var doorFrameMat  = MakeUrpMaterial("Cliff_DoorFrameMat", new Color(0.18f, 0.20f, 0.24f), false);
            var doorPanelMat  = MakeEmissiveMaterial("Cliff_DoorPanelMat",
                new Color(0.18f, 0.55f, 0.85f), new Color(0.35f, 0.85f, 1.4f) * 0.6f);
            var stairMat      = MakeUrpMaterial("Cliff_StairMat",     new Color(0.42f, 0.42f, 0.45f), false);
            var floor2Mat     = MakeUrpMaterial("Cliff_Floor2Mat",    new Color(0.30f, 0.32f, 0.38f), false);
            var rightFloorMat = MakeUrpMaterial("Cliff_RightFloorMat", new Color(0.38f, 0.40f, 0.45f), false);
            var platformMat   = MakeEmissiveMaterial("Cliff_PlatformMat",
                new Color(0.55f, 0.45f, 0.30f), new Color(1.0f, 0.65f, 0.30f) * 0.4f);
            var entryPlatformMat = MakeEmissiveMaterial("Cliff_EntryPlatformMat",
                new Color(0.15f, 0.7f, 0.30f), new Color(0.25f, 1.4f, 0.5f) * 0.7f);
            var pedestalMat   = MakeUrpMaterial("Cliff_PedestalMat",  new Color(0.35f, 0.32f, 0.30f), false);
            var carpetMat     = MakeUrpMaterial("Cliff_CarpetMat",    new Color(0.70f, 0.45f, 0.25f), false);
            var standMat      = MakeUrpMaterial("Cliff_StandMat",     new Color(0.35f, 0.32f, 0.30f), false);

            // LightBeam materials
            var beamMat       = MakeBeamMaterial();
            var emitterFrameMat = MakeUrpMaterial("Cliff_EmitterFrameMat",
                new Color(0.15f, 0.15f, 0.18f), false);
            var emitterLensMat  = MakeEmissiveMaterial("Cliff_EmitterLensMat",
                new Color(1f, 0.85f, 0.3f), new Color(2.5f, 2f, 0.7f));
            // 거울 face 머티리얼은 ColorPalette 와 1:1 매핑 — 빌드 루프 안에서 색깔별로 instance 화.
            var mirrorBackMat   = MakeUrpMaterial("Cliff_MirrorBackMat",
                new Color(0.18f, 0.18f, 0.22f), false);
            var indicatorMat    = MakeEmissiveMaterial("Cliff_IndicatorMat",
                new Color(1f, 0.6f, 0.2f), new Color(2.5f, 1.5f, 0.5f));
            var receiverPlateMat = MakeUrpMaterial("Cliff_ReceiverPlateMat",
                new Color(0.2f, 0.2f, 0.24f), false);

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
            BuildSideDoor(arch.transform, "LeftDoor",  new Vector3(LeftDoorCenterX,  0f, doorZ), doorPanelMat);
            BuildSideDoor(arch.transform, "RightDoor", new Vector3(RightDoorCenterX, 0f, doorZ), doorPanelMat);
            BuildStairs(arch.transform, stairMat);
            BuildSecondFloor(arch.transform, floor2Mat);

            // ===== Cliff Platforms =====
            var platformSpecs = GeneratePlatformSpecs();
            var platformsGroup = new GameObject("Platforms");
            platformsGroup.transform.SetParent(root.transform, false);

            var cliffPlatforms = new List<CliffPlatform>();
            var mirrorPlatforms = new Dictionary<int, CliffPlatform>(); // mirror idx → platform
            for (int i = 0; i < platformSpecs.Count; i++)
            {
                var spec = platformSpecs[i];
                Material mat = spec.IsEntry ? entryPlatformMat : platformMat;
                var cp = BuildPlatform(platformsGroup.transform,
                    spec.IsEntry ? "Platform_Entry" : (spec.HasMirror ? $"Platform_Mirror{spec.MirrorIndex + 1}" : $"Platform_{i}"),
                    spec, mat);
                cliffPlatforms.Add(cp);
                if (spec.HasMirror) mirrorPlatforms[spec.MirrorIndex] = cp;
            }

            // StartZone = entry platform 의 dock 위에 시각 표시 (작은 emissive 슬랩)
            var entryDock = cliffPlatforms[0].GetDock();
            var start = GameObject.CreatePrimitive(PrimitiveType.Cube);
            start.name = "StartZone";
            DisableColliderIfAny(start);
            start.transform.SetParent(root.transform, false);
            start.transform.position = entryDock.position + Vector3.up * 0.005f;
            start.transform.localScale = new Vector3(ZoneWidth, ZoneThickness, ZoneDepth);
            AssignMat(start, entryPlatformMat);

            // ===== Dispenser (2층, FloatingMode 활성) =====
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
            dispComp.UseFloatingMode = true; // *** Cliff: 떠있는 카펫 ***
            dispComp.FloatingY = CliffFloatingY;

            var active = new GameObject("ActiveCarpets");
            active.transform.SetParent(root.transform, false);
            active.transform.localPosition = Vector3.zero;
            dispComp.ActiveCarpetsRoot = active.transform;

            // ===== LauncherHolster + Launcher =====
            var holster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            holster.name = "LauncherHolster";
            holster.transform.SetParent(root.transform, false);
            holster.transform.localPosition = HolsterLocal;
            holster.transform.localScale = new Vector3(0.45f, 0.05f, 0.30f);
            AssignMat(holster, standMat);

            var launcher = BuildLauncher(root.transform, carpetMat, active.transform);

            // ===== Light Beam Puzzle =====
            var puzzleGroup = new GameObject("LightBeamPuzzle");
            puzzleGroup.transform.SetParent(root.transform, false);

            var emitter = BuildEmitter(puzzleGroup.transform, EmitterLocal, EmitterRot,
                emitterFrameMat, emitterLensMat);

            // 거울별 색상 face 머티리얼 (4종) — 각 거울이 자기 색깔로 발광.
            var mirrorColorMats = new Material[MirrorCount];
            for (int i = 0; i < MirrorCount; i++)
            {
                Color c = MirrorColors[i];
                mirrorColorMats[i] = MakeEmissiveMaterial(
                    $"Cliff_MirrorFaceMat_{MirrorColorNames[i]}",
                    c, c * 0.6f);
            }

            // 거울 4개 — 각 mirror platform 위에 pedestal + mirror, ColorId 부여.
            var mirrors = new List<LightBeamMirror>();
            for (int i = 0; i < MirrorCount; i++)
            {
                if (!mirrorPlatforms.TryGetValue(i, out var platform)) continue;
                var mirror = BuildMirrorOnPlatform(puzzleGroup.transform,
                    $"Mirror{i + 1}_{MirrorColorNames[i]}",
                    platform, pedestalMat, mirrorColorMats[i], mirrorBackMat, indicatorMat,
                    colorId: i, baseColor: MirrorColors[i]);
                mirrors.Add(mirror);
            }

            var receiver = BuildReceiver(puzzleGroup.transform, ReceiverLocal, ReceiverRot, receiverPlateMat);

            // 2층 ColorOrderPanel — 사전 정의 순서를 색상으로 표시 (입력 받지 않음).
            var colorPanel = BuildColorOrderPanel(root.transform, standMat, pedestalMat);

            // 2층 BeamAimController — Knob 슬라이더로 emitter Z 위치를 P1 이 조정.
            var aimCtrl = BuildBeamAimController(root.transform, emitter, standMat, pedestalMat);

            // Controller + LineRenderer
            var ctrlGo = new GameObject("LightBeamController");
            ctrlGo.transform.SetParent(puzzleGroup.transform, false);
            var lr = ctrlGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 0;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.material = beamMat;
            var beamColor = new Color(1.5f, 1.3f, 0.5f, 1f);
            lr.startColor = beamColor;
            lr.endColor = beamColor;

            var beamCtrl = ctrlGo.AddComponent<LightBeamController>();
            beamCtrl.Emitter = emitter;
            beamCtrl.BeamRenderer = lr;
            beamCtrl.Receivers = new List<LightBeamReceiver> { receiver };
            beamCtrl.MaxSegmentDistance = 50f;
            beamCtrl.MaxBounces = 12;
            beamCtrl.ReflectOffset = 0.001f;
            beamCtrl.BeamMask = ~0;
            beamCtrl.RequiredOrderPanel = colorPanel; // 사전 정의 순서로 빔 검증.

            // ===== CliffController wire-up =====
            ctrl.DefaultSpawnPoint = cliffPlatforms.Count > 0 ? cliffPlatforms[0].GetDock() : null;
            ctrl.FallThresholdY = root.transform.position.y + FallThresholdY_RelativeToRoot;
            ctrl.PlatformDetectMaxDist = 3f;
            ctrl.PlatformDetectMask = ~0;
            ctrl.RespawnCooldown = 1f;

            var orderStr = string.Join("→", System.Array.ConvertAll(PreSetMirrorOrder, id => MirrorColorNames[id]));
            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[Cliff] Build 완료. 4 거울(R/G/B/Y) + 사전 정의 순서 패널 + 2층 빔 조준 슬라이더. " +
                      $"낙하 임계 y={ctrl.FallThresholdY} (world). 사전 정의 순서: {orderStr}. " +
                      $"emitter Z 슬라이더 범위 [{EmitterSlideMinZ}, {EmitterSlideMaxZ}] (중심={EmitterInitialZ}, knob 중앙일 때). " +
                      "P1: 카펫 발사 + 빔 조준 슬라이더. P2 가 거울 yaw 를 맞춰 빔이 위 순서대로(모든 거울 거쳐서) receiver 에 도달해야 솔브 — " +
                      "순서 틀리거나 거울 빠뜨리면 receiver 시각도 안 켜짐.");
        }

        // ===== Platform spec generation =====

        /// <summary>
        /// 발판 spec — 1 entry + 4 mirror = 5개 (랜덤 nav 폐기, 모든 발판 좌표 결정적).
        /// 모든 발판의 x, z 좌표 unique. 인접 거리 ≥ 5m (점프 차단).
        /// </summary>
        static List<PlatformSpec> GeneratePlatformSpecs()
        {
            return new List<PlatformSpec>
            {
                new PlatformSpec { PosXZ = EntryPlatformPos, Size = EntryPlatformSize, IsEntry = true,  HasMirror = false },
                new PlatformSpec { PosXZ = Mirror1Pos,       Size = MirrorPlatformSize, HasMirror = true, MirrorIndex = 0 },
                new PlatformSpec { PosXZ = Mirror2Pos,       Size = MirrorPlatformSize, HasMirror = true, MirrorIndex = 1 },
                new PlatformSpec { PosXZ = Mirror3Pos,       Size = MirrorPlatformSize, HasMirror = true, MirrorIndex = 2 },
                new PlatformSpec { PosXZ = Mirror4Pos,       Size = MirrorPlatformSize, HasMirror = true, MirrorIndex = 3 },
            };
        }

        static CliffPlatform BuildPlatform(Transform parent, string name, PlatformSpec spec, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(spec.PosXZ.x, PlatformTopY, spec.PosXZ.y);

            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vis.name = "Visual";
            vis.transform.SetParent(go.transform, false);
            vis.transform.localPosition = new Vector3(0f, -PlatformThickness * 0.5f, 0f);
            vis.transform.localScale = new Vector3(spec.Size.x, PlatformThickness, spec.Size.y);
            AssignMat(vis, mat);

            var dock = new GameObject("Dock");
            dock.transform.SetParent(go.transform, false);
            dock.transform.localPosition = Vector3.zero;

            var cp = go.AddComponent<CliffPlatform>();
            cp.Dock = dock.transform;
            return cp;
        }

        // ===== Light Beam builders =====

        static LightBeamEmitter BuildEmitter(Transform parent, Vector3 localPos, Quaternion localRot,
            Material frameMat, Material lensMat)
        {
            var root = new GameObject("Emitter");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body"; DisableColliderIfAny(body);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0f, -0.15f);
            body.transform.localScale = new Vector3(0.4f, 0.4f, 0.3f);
            AssignMat(body, frameMat);

            var lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lens.name = "Lens"; DisableColliderIfAny(lens);
            lens.transform.SetParent(root.transform, false);
            lens.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lens.transform.localScale = new Vector3(0.12f, 0.02f, 0.12f);
            AssignMat(lens, lensMat);

            var emissionPoint = new GameObject("EmissionPoint");
            emissionPoint.transform.SetParent(root.transform, false);
            emissionPoint.transform.localPosition = new Vector3(0f, 0f, 0.1f);

            var emitterComp = root.AddComponent<LightBeamEmitter>();
            emitterComp.IsOn = true;
            emitterComp.EmissionPoint = emissionPoint.transform;
            return emitterComp;
        }

        /// <summary>Mirror 받침대 (pedestal) + 거울을 cliff platform 위에 부착.
        /// colorId / baseColor 는 빔 순서 검증과 시각 구분에 사용.</summary>
        static LightBeamMirror BuildMirrorOnPlatform(Transform parent, string name, CliffPlatform platform,
            Material pedestalMat, Material faceMat, Material backMat, Material indicatorMat,
            int colorId, Color baseColor)
        {
            // Mirror stand root — platform 의 child 로 부착해서 platform 위치 따라감.
            var stand = new GameObject(name + "Stand");
            stand.transform.SetParent(platform.transform, false);
            stand.transform.localPosition = Vector3.zero; // platform 윗면(y=0) 위에 직접 부착

            // Pedestal — 작은 cylinder, y=0 부터 y=MirrorPedestalTopY 까지
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Pedestal";
            DisableColliderIfAny(pedestal);
            pedestal.transform.SetParent(stand.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, MirrorPedestalTopY * 0.5f, 0f);
            pedestal.transform.localScale = new Vector3(MirrorPedestalRadius * 2f, MirrorPedestalTopY * 0.5f, MirrorPedestalRadius * 2f);
            AssignMat(pedestal, pedestalMat);

            // Mirror pivot — pedestal top
            var mirror = new GameObject(name);
            mirror.transform.SetParent(stand.transform, false);
            mirror.transform.localPosition = new Vector3(0f, MirrorPedestalTopY, 0f);
            mirror.transform.localRotation = Quaternion.Euler(0f, InitialMirrorYaw, 0f);

            float halfThick = MirrorVisualThickness * 0.5f;
            float visualCenterY = MirrorVisualCenterLocalY;

            var front = GameObject.CreatePrimitive(PrimitiveType.Cube);
            front.name = "Front"; DisableColliderIfAny(front);
            front.transform.SetParent(mirror.transform, false);
            front.transform.localPosition = new Vector3(0f, visualCenterY, halfThick * 0.5f);
            front.transform.localScale = new Vector3(MirrorVisualWidth, MirrorVisualHeight, halfThick);
            AssignMat(front, faceMat);

            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Back"; DisableColliderIfAny(back);
            back.transform.SetParent(mirror.transform, false);
            back.transform.localPosition = new Vector3(0f, visualCenterY, -halfThick * 0.5f);
            back.transform.localScale = new Vector3(MirrorVisualWidth, MirrorVisualHeight, halfThick);
            AssignMat(back, backMat);

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
            indicator.name = "FrontIndicator"; DisableColliderIfAny(indicator);
            indicator.transform.SetParent(mirror.transform, false);
            indicator.transform.localPosition = new Vector3(0f, visualCenterY + MirrorVisualHeight * 0.5f + 0.05f, halfThick);
            indicator.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);
            indicator.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            AssignMat(indicator, indicatorMat);

            var mirrorCol = mirror.AddComponent<BoxCollider>();
            mirrorCol.center = new Vector3(0f, visualCenterY, 0f);
            mirrorCol.size = new Vector3(MirrorVisualWidth, MirrorVisualHeight, MirrorVisualThickness);

            mirror.AddComponent<XRSimpleInteractable>();

            var mirrorComp = mirror.AddComponent<LightBeamMirror>();
            mirrorComp.ColorId = colorId;
            mirrorComp.BaseColor = baseColor;
            mirrorComp.ReflectAxisLocal = Vector3.forward;
            mirrorComp.ReflectDotThreshold = 0.7f;
            mirrorComp.LockPosition = true;
            mirrorComp.LockToYawOnly = true;
            mirrorComp.Mode = LightBeamMirror.RotationMode.PointTowardHand;
            mirrorComp.MinHandDistance = 0.08f;
            mirrorComp.RotationSensitivity = 1.0f;

            return mirrorComp;
        }

        /// <summary>
        /// 2층의 색상 순서 패널 — 사전 정의 순서를 4개 슬롯에 색상으로 표시. 입력 없음(read-only).
        /// </summary>
        static ColorOrderPanel BuildColorOrderPanel(Transform parent, Material standMat, Material boardMat)
        {
            var root = new GameObject("ColorOrderPanel");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = ColorPanelBaseLocal;

            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "Stand"; DisableColliderIfAny(stand);
            stand.transform.SetParent(root.transform, false);
            stand.transform.localPosition = new Vector3(0f, ColorPanelStandHeight * 0.5f, 0f);
            stand.transform.localScale = new Vector3(0.15f, ColorPanelStandHeight * 0.5f, 0.15f);
            AssignMat(stand, standMat);

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board"; DisableColliderIfAny(board);
            board.transform.SetParent(root.transform, false);
            board.transform.localPosition = new Vector3(0f, ColorPanelStandHeight + 0.02f, 0f);
            board.transform.localScale = new Vector3(0.55f, 0.04f, 0.20f);
            AssignMat(board, boardMat);

            var panel = root.AddComponent<ColorOrderPanel>();
            panel.MaxSequenceLength = MirrorCount;
            panel.ColorPalette = new List<Color>(MirrorColors);
            panel.RequiredSequence = new List<int>(PreSetMirrorOrder);
            panel.EmptySlotColor = new Color(0.08f, 0.08f, 0.10f);
            panel.EmissionIntensity = 1.6f;

            // 4 디스플레이 슬롯 — 좌→우 = 첫→마지막. Start() 의 UpdateDisplay 가 자동으로 색칠.
            var displays = new List<Renderer>();
            for (int i = 0; i < MirrorCount; i++)
            {
                float xOffset = (i - (MirrorCount - 1) * 0.5f) * ColorPanelSlotSpacing;
                var slot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slot.name = $"DisplaySlot_{i + 1}_{MirrorColorNames[PreSetMirrorOrder[i]]}";
                DisableColliderIfAny(slot);
                slot.transform.SetParent(root.transform, false);
                slot.transform.localPosition = new Vector3(xOffset, ColorPanelStandHeight + 0.05f, 0f);
                slot.transform.localScale = new Vector3(ColorPanelSlotSize, 0.03f, ColorPanelSlotSize);
                var slotMat = MakeEmissiveMaterial($"Cliff_SlotMat_{i}",
                    panel.EmptySlotColor, Color.black);
                slot.GetComponent<Renderer>().sharedMaterial = slotMat;
                displays.Add(slot.GetComponent<Renderer>());
            }
            panel.DisplaySlots = displays;

            // 위치 가이드 표지 — 슬롯 옆 작은 막대, 1/2/3/4 순으로 길이 늘어남.
            for (int i = 0; i < MirrorCount; i++)
            {
                float xOffset = (i - (MirrorCount - 1) * 0.5f) * ColorPanelSlotSpacing;
                var tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tick.name = $"Tick_{i + 1}";
                DisableColliderIfAny(tick);
                tick.transform.SetParent(root.transform, false);
                tick.transform.localPosition = new Vector3(xOffset, ColorPanelStandHeight + 0.045f, -0.07f);
                tick.transform.localScale = new Vector3(0.008f * (i + 1), 0.015f, 0.012f);
                AssignMat(tick, boardMat);
            }

            return panel;
        }

        /// <summary>
        /// P1 의 2층 빔 조준 슬라이더 — Knob 의 X 위치가 emitter 의 world Z 에 매핑됨.
        /// 로컬 X 가 world Z 가 되도록 90° 회전 → P1 이 트랙을 따라 앞뒤로 knob 을 밀면
        /// emitter 도 챔버 z 축으로 평행 이동, 빔이 다른 거울에 닿게 됨.
        ///
        /// Knob 은 XRSimpleInteractable — XRGrabInteractable 의 attach-pose snap 부작용을 회피하기 위함.
        /// 위치는 BeamAimController 가 100% 제어 (손-knob 투영 + clamp).
        /// </summary>
        static BeamAimController BuildBeamAimController(Transform parent, LightBeamEmitter emitter,
            Material standMat, Material plateMat)
        {
            var root = new GameObject("BeamAimController");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = AimControlBaseLocal;
            root.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // local +X == world +Z

            // 스탠드 (cylinder)
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "Stand"; DisableColliderIfAny(stand);
            stand.transform.SetParent(root.transform, false);
            stand.transform.localPosition = new Vector3(0f, AimControlStandHeight * 0.5f, 0f);
            stand.transform.localScale = new Vector3(0.12f, AimControlStandHeight * 0.5f, 0.12f);
            AssignMat(stand, standMat);

            // 트랙 plate (knob 가 슬라이드할 레일)
            var track = GameObject.CreatePrimitive(PrimitiveType.Cube);
            track.name = "Track"; DisableColliderIfAny(track);
            track.transform.SetParent(root.transform, false);
            track.transform.localPosition = new Vector3(0f, AimControlStandHeight + 0.015f, 0f);
            track.transform.localScale = new Vector3(0.70f, 0.03f, 0.10f);
            AssignMat(track, plateMat);

            // 트랙 양 끝 표지 (어느 방향이 어느 끝인지 시각 단서)
            var markerMin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            markerMin.name = "MarkerMin"; DisableColliderIfAny(markerMin);
            markerMin.transform.SetParent(root.transform, false);
            markerMin.transform.localPosition = new Vector3(AimKnobTrackMin - 0.02f, AimControlStandHeight + 0.025f, 0f);
            markerMin.transform.localScale = new Vector3(0.02f, 0.04f, 0.08f);
            AssignMat(markerMin, plateMat);

            var markerMax = GameObject.CreatePrimitive(PrimitiveType.Cube);
            markerMax.name = "MarkerMax"; DisableColliderIfAny(markerMax);
            markerMax.transform.SetParent(root.transform, false);
            markerMax.transform.localPosition = new Vector3(AimKnobTrackMax + 0.02f, AimControlStandHeight + 0.025f, 0f);
            markerMax.transform.localScale = new Vector3(0.02f, 0.04f, 0.08f);
            AssignMat(markerMax, plateMat);

            // Knob — XRSimpleInteractable. CreatePrimitive(Cube) 가 BoxCollider 자동 부착해 select 검출 가능.
            var knob = GameObject.CreatePrimitive(PrimitiveType.Cube);
            knob.name = "Knob";
            knob.transform.SetParent(root.transform, false);
            float initT = Mathf.InverseLerp(EmitterSlideMinZ, EmitterSlideMaxZ, EmitterInitialZ);
            if (InvertEmitterMapping) initT = 1f - initT;
            float initX = Mathf.Lerp(AimKnobTrackMin, AimKnobTrackMax, initT);
            knob.transform.localPosition = new Vector3(initX, AimControlStandHeight + 0.055f, 0f);
            knob.transform.localScale = new Vector3(0.07f, 0.06f, 0.10f);
            var knobMat = MakeEmissiveMaterial("Cliff_KnobMat",
                new Color(0.95f, 0.65f, 0.20f), new Color(1.6f, 1.1f, 0.4f));
            knob.GetComponent<Renderer>().sharedMaterial = knobMat;

            // XRSimpleInteractable — select 이벤트만 발생시키고 transform 은 절대 안 건드림.
            // (XRGrabInteractable 은 잡는 순간 attach-pose snap 으로 knob 가 손 위치로 텔레포트되는 부작용 있음.)
            knob.AddComponent<XRSimpleInteractable>();

            var aimCtrl = root.AddComponent<BeamAimController>();
            aimCtrl.TargetEmitter = emitter;
            aimCtrl.Knob = knob.transform;
            aimCtrl.MinKnobLocalX = AimKnobTrackMin;
            aimCtrl.MaxKnobLocalX = AimKnobTrackMax;
            aimCtrl.MinEmitterZ = EmitterSlideMinZ;
            aimCtrl.MaxEmitterZ = EmitterSlideMaxZ;
            aimCtrl.InvertMapping = InvertEmitterMapping;

            return aimCtrl;
        }

        static LightBeamReceiver BuildReceiver(Transform parent, Vector3 localPos, Quaternion localRot,
            Material plateMat)
        {
            var root = new GameObject("Receiver");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate"; DisableColliderIfAny(plate);
            plate.transform.SetParent(root.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            plate.transform.localScale = new Vector3(0.5f, 0.5f, 0.08f);
            AssignMat(plate, plateMat);

            var crystal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crystal.name = "Crystal"; DisableColliderIfAny(crystal);
            crystal.transform.SetParent(root.transform, false);
            crystal.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            crystal.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);

            var col = root.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 0f, 0.1f);
            col.radius = 0.2f;

            var receiverComp = root.AddComponent<LightBeamReceiver>();
            receiverComp.GlowRenderer = crystal.GetComponent<Renderer>();
            receiverComp.HitColor = new Color(0.4f, 1f, 0.5f);
            receiverComp.IdleColor = new Color(0.4f, 0.4f, 0.4f);
            receiverComp.HitEmissionIntensity = 2.5f;
            receiverComp.IdleEmissionIntensity = 0.2f;
            return receiverComp;
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
            // 남쪽 벽 = BuildCorridor 의 Wall_N_F1/D1Hdr/F2/D2Hdr/F3 가 챔버 높이까지 덮음 — 중복 안 만듦.
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
            auto.SlideAxisLocal = new Vector3(1f, 0f, 0f);
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
            launcherComp.UseFloatingMode = true; // *** Cliff: floating carpets ***
            launcherComp.FloatingY = CliffFloatingY;

            return launcher;
        }

        // ===== Beam material =====
        static Material MakeBeamMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = "Cliff_BeamMat" };
            Color beamColor = new Color(1f, 0.85f, 0.3f);
            m.color = beamColor;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", beamColor);
            if (m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", beamColor * 2.5f);
                m.EnableKeyword("_EMISSION");
            }
            return m;
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
