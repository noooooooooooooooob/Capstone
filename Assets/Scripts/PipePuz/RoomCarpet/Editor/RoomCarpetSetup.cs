using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Build Room Carpet.
    ///
    /// 비대칭 협력 레이아웃을 한 번에 자동 빌드한다:
    ///   P1Platform (단단한 바닥, CarpetFloor 아님)
    ///     ├ Dispenser : 카펫을 P2 쪽으로 던지는 디스펜서
    ///     ├ LauncherHolster + CarpetLauncher : 트리거로 카펫 쏘는 총 (선택적, 던지기와 공존)
    ///     ├ HintCatcher : P2 가 던진 단서공을 흡인하는 자석 트리거
    ///     └ HintBoard : 5개의 슬롯 — 다 채워지면 OnSolved
    ///   Floor (위험 바닥, 8x6, CarpetFloor 마커)
    ///     └ HintBalls 5개 흩뿌림 — 위험 바닥 위에 정지된 단서공
    ///   StartZone (P2 시작 안전 영역, 위험 바닥의 동쪽 끝)
    ///   GoalZone (옵션 보존 — 모든 슬롯 채워지면 무의미하지만 기존 호환)
    ///   ActiveCarpets (런타임 카펫 부모)
    ///
    /// 게임 진행 (의도):
    ///   1. P1 은 P1Platform 위에 서서 디스펜서에서 카펫을 잡아 P2 영역으로 던진다.
    ///   2. P2 는 카펫 위를 직접 걸어 위험 바닥에 흩뿌려진 단서공을 줍는다.
    ///   3. P2 는 단서공을 P1 쪽 HintCatcher 트리거 영역으로 던진다.
    ///   4. 캐처는 빈 슬롯에 공을 흡인 → 슬롯에 lock.
    ///   5. 5슬롯 전부 채워지면 HintPuzzleBoard.OnSolved → Controller.OnSolved.
    /// </summary>
    public static class RoomCarpetSetup
    {
        // 위험 바닥 (넓힘 — P2 활동 무대)
        const float FloorWidth = 8f;
        const float FloorDepth = 6f;
        const float FloorThickness = 0.05f;
        static readonly Vector3 FloorCenter = new Vector3(0f, 0f, 0f);

        // P1Platform — 위험 바닥 서쪽 외부 (단단한 바닥)
        const float P1PlatformWidth = 2.5f;
        const float P1PlatformDepth = 5f;
        const float P1PlatformThickness = 0.05f;
        static readonly Vector3 P1PlatformCenter = new Vector3(-5.5f, 0f, 0f);

        // P2StartZone — 위험 바닥 동쪽 끝 (안전 섬)
        const float ZoneWidth = 1.4f;
        const float ZoneDepth = 1.4f;
        const float ZoneThickness = 0.01f;
        const float ZoneTopY = 0.03f;
        static readonly Vector3 StartZoneLocal = new Vector3(3.0f, ZoneTopY, 0f);

        // GoalZone (기존 호환 보존, 보드 클리어가 진짜 골) — Start 옆에 작게.
        const float GoalTriggerHeight = 3.0f;
        static readonly Vector3 GoalZoneLocal = new Vector3(3.0f, GoalTriggerHeight * 0.5f, 1.8f);

        // Dispenser — P1Platform 위, 동쪽 끝 (위험 바닥 가까이)
        static readonly Vector3 DispenserLocal = new Vector3(-4.4f, 0f, 0f);
        const float DispenserStandHeight = 1.0f;
        const float DispenserStandRadius = 0.10f;
        const float SpawnPointY = 1.10f;

        // LauncherHolster — P1Platform 위, 디스펜서 옆 (사용자가 양손 사용)
        // 거치대 윗면 y ≈ HolsterTopY, 그 위에 런처가 놓임.
        static readonly Vector3 HolsterLocal = new Vector3(-5.0f, 0f, -0.9f);
        const float HolsterTopY = 0.95f;
        // 런처 pivot 의 world Y = HolsterTopY + 0.15 → 그립 바닥(local y=-0.15) 이 holster 윗면에 닿음.
        static readonly Vector3 LauncherLocal = new Vector3(-5.0f, HolsterTopY + 0.15f, -0.9f);
        static readonly Quaternion LauncherRot = Quaternion.Euler(0f, 90f, 0f); // 동쪽(+X) 향함
        const float LauncherMuzzleSpeed = 8.5f;
        const float LauncherMuzzleSpin = 2.5f;
        const float LauncherCooldown = 0.5f;

        // HintCatcher — P1Platform 위 공중 (P2 가 던진 공이 들어옴)
        static readonly Vector3 CatcherLocal = new Vector3(-5.0f, 1.55f, -1.6f);
        const float CatcherTriggerRadius = 0.55f;

        // HintBoard — P1Platform 위, 디스펜서 반대편 (북쪽)
        static readonly Vector3 BoardLocal = new Vector3(-5.5f, 0.0f, 1.6f);
        const float BoardSlotSpacing = 0.28f;
        const int   BoardSlotCount = 5;
        const float BoardSlotY = 1.10f;
        const float BoardSlotRadius = 0.07f;

        // 카펫
        static readonly Vector2 CarpetSize = new Vector2(0.9f, 1.2f);
        const float CarpetThickness = 0.02f;
        const float CarpetLifetime = 5f;
        const float CarpetWarningSeconds = 1.5f;

        // 단서공
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

        // 단서공 흩뿌리기 위치 (위험 바닥 로컬 X/Z, Floor center 기준).
        // Start zone (x≈3) 과 P1Platform 경계(x≈-4) 를 피하도록 분포.
        static readonly Vector2[] HintBallSpread =
        {
            new Vector2(-2.5f, -1.8f),
            new Vector2(-1.0f,  1.6f),
            new Vector2( 0.5f, -2.0f),
            new Vector2( 1.6f,  1.2f),
            new Vector2( 2.4f, -0.6f),
        };

        [MenuItem("Tools/PipePuz/Build Room Carpet")]
        public static void Build()
        {
            var room = GameObject.Find("RoomCarpet");
            if (room == null)
            {
                EditorUtility.DisplayDialog("RoomCarpet",
                    "씬에서 'RoomCarpet' 오브젝트를 찾을 수 없습니다.\nPipe Scene 을 열고 다시 시도하세요.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Room Carpet");

            // 기존 자식 정리.
            DestroyChildIfExists(room.transform, "Floor");
            DestroyChildIfExists(room.transform, "P1Platform");
            DestroyChildIfExists(room.transform, "StartZone");
            DestroyChildIfExists(room.transform, "GoalZone");
            DestroyChildIfExists(room.transform, "Dispenser");
            DestroyChildIfExists(room.transform, "ActiveCarpets");
            DestroyChildIfExists(room.transform, "HintCatcher");
            DestroyChildIfExists(room.transform, "HintBoard");
            DestroyChildIfExists(room.transform, "HintBalls");
            DestroyChildIfExists(room.transform, "LauncherHolster");
            DestroyChildIfExists(room.transform, "CarpetLauncher");
            var oldCtrl = room.GetComponent<DisappearingCarpetController>();
            if (oldCtrl != null) Undo.DestroyObjectImmediate(oldCtrl);

            // 머티리얼.
            var floorMat = MakeEmissiveMaterial(
                "Carpet_FloorMat",
                new Color(0.55f, 0.10f, 0.10f),
                new Color(1.0f, 0.18f, 0.18f) * 0.8f);
            var p1Mat = MakeUrpMaterial(
                "Carpet_P1PlatformMat",
                new Color(0.30f, 0.32f, 0.38f), false);
            var startMat = MakeEmissiveMaterial(
                "Carpet_StartMat",
                new Color(0.15f, 0.7f, 0.30f),
                new Color(0.25f, 1.4f, 0.5f) * 0.7f);
            var goalMat = MakeEmissiveMaterial(
                "Carpet_GoalMat",
                new Color(0.20f, 0.55f, 1.0f),
                new Color(0.35f, 0.85f, 1.6f) * 0.9f);
            var standMat = MakeUrpMaterial(
                "Carpet_StandMat",
                new Color(0.35f, 0.32f, 0.30f), false);
            var carpetMat = MakeUrpMaterial(
                "Carpet_CarpetMat",
                new Color(0.70f, 0.45f, 0.25f), false);
            var catcherMat = MakeEmissiveMaterial(
                "Carpet_CatcherMat",
                new Color(0.25f, 0.45f, 0.95f),
                new Color(0.45f, 0.75f, 1.6f) * 0.6f);
            var boardMat = MakeUrpMaterial(
                "Carpet_BoardMat",
                new Color(0.18f, 0.18f, 0.22f), false);
            var slotMat = MakeUrpMaterial(
                "Carpet_SlotMat",
                new Color(0.50f, 0.50f, 0.55f), false);

            // 컨트롤러.
            var ctrl = room.AddComponent<DisappearingCarpetController>();

            // === Floor — 넓힌 위험 바닥 ===
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(room.transform, false);
            floor.transform.localPosition = FloorCenter;
            floor.transform.localScale = new Vector3(FloorWidth, FloorThickness, FloorDepth);
            AssignMat(floor, floorMat);
            floor.AddComponent<CarpetFloor>();

            // === P1Platform — 단단한 바닥 (CarpetFloor 부착 안 함) ===
            var p1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p1.name = "P1Platform";
            p1.transform.SetParent(room.transform, false);
            p1.transform.localPosition = P1PlatformCenter;
            p1.transform.localScale = new Vector3(P1PlatformWidth, P1PlatformThickness, P1PlatformDepth);
            AssignMat(p1, p1Mat);

            // === StartZone (P2) ===
            var start = GameObject.CreatePrimitive(PrimitiveType.Cube);
            start.name = "StartZone";
            start.transform.SetParent(room.transform, false);
            start.transform.localPosition = StartZoneLocal;
            start.transform.localScale = new Vector3(ZoneWidth, ZoneThickness, ZoneDepth);
            AssignMat(start, startMat);

            // === GoalZone (기존 호환 — 보드 솔브가 진짜 골이지만 트리거 유지) ===
            var goal = new GameObject("GoalZone");
            goal.transform.SetParent(room.transform, false);
            goal.transform.localPosition = GoalZoneLocal;

            var goalTrigger = goal.AddComponent<BoxCollider>();
            goalTrigger.size = new Vector3(ZoneWidth, GoalTriggerHeight, ZoneDepth);
            goalTrigger.isTrigger = true;

            var goalComp = goal.AddComponent<CarpetGoalZone>();

            var goalVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            goalVis.name = "Visual";
            goalVis.transform.SetParent(goal.transform, false);
            goalVis.transform.localPosition = new Vector3(0f, ZoneTopY - (GoalTriggerHeight * 0.5f), 0f);
            goalVis.transform.localScale = new Vector3(ZoneWidth, ZoneThickness, ZoneDepth);
            AssignMat(goalVis, goalMat);

            // === Dispenser (P1Platform 위 동쪽 끝) ===
            var disp = new GameObject("Dispenser");
            disp.transform.SetParent(room.transform, false);
            disp.transform.localPosition = DispenserLocal;

            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "Stand";
            var standCol = stand.GetComponent<Collider>();
            if (standCol != null) Object.DestroyImmediate(standCol);
            stand.transform.SetParent(disp.transform, false);
            stand.transform.localPosition = new Vector3(0f, DispenserStandHeight * 0.5f, 0f);
            stand.transform.localScale = new Vector3(DispenserStandRadius * 2f, DispenserStandHeight * 0.5f, DispenserStandRadius * 2f);
            AssignMat(stand, standMat);

            var spawnPoint = new GameObject("SpawnPoint");
            spawnPoint.transform.SetParent(disp.transform, false);
            spawnPoint.transform.localPosition = new Vector3(0f, SpawnPointY, 0f);

            var dispComp = disp.AddComponent<CarpetDispenser>();
            dispComp.SpawnPoint = spawnPoint.transform;
            dispComp.CarpetMaterial = carpetMat;
            dispComp.CarpetSize = CarpetSize;
            dispComp.CarpetThickness = CarpetThickness;
            dispComp.CarpetLifetime = CarpetLifetime;
            dispComp.CarpetWarningSeconds = CarpetWarningSeconds;

            // === ActiveCarpets (런타임 카펫 부모) ===
            var active = new GameObject("ActiveCarpets");
            active.transform.SetParent(room.transform, false);
            active.transform.localPosition = Vector3.zero;
            dispComp.ActiveCarpetsRoot = active.transform;

            // === LauncherHolster (총 거치대) ===
            var holster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            holster.name = "LauncherHolster";
            holster.transform.SetParent(room.transform, false);
            holster.transform.localPosition = new Vector3(HolsterLocal.x, HolsterTopY - 0.025f, HolsterLocal.z);
            holster.transform.localScale = new Vector3(0.45f, 0.05f, 0.30f);
            AssignMat(holster, standMat);

            // === CarpetLauncher (권총) ===
            //
            // 로컬 좌표계:
            //   원점(0,0,0) = 그립과 리시버가 만나는 지점.
            //   +Z = forward (런처 회전 적용 후 world +X = 위험 바닥 방향).
            //   +Y = up, +X = right.
            //
            // 구성:
            //   Grip (살짝 뒤로 기운 손잡이) — 그립 바닥이 holster 윗면에 닿도록 local y=-0.075 중심
            //   Receiver (리시버 본체, 그립 위 horizontal block)
            //   Barrel (총신, 리시버 앞 cylinder)
            //   MuzzleRing (총신 끝 살짝 더 큰 ring)
            //   TriggerGuard (트리거 가드, 트리거 보호 블록)
            //   Trigger (트리거 작은 돌출)
            //   FrontSight / RearSight (조준기)
            //   Muzzle (Transform — 카펫 발사 기준점, 모든 비주얼보다 앞)
            //   AttachPoint (Transform — XRGrabInteractable 의 손 부착 위치)
            //
            // 콜라이더는 그립 + 리시버만 감싸 — 총신과 머즐 영역은 콜라이더 밖이라 카펫이 충돌 없음.
            var launcher = new GameObject("CarpetLauncher");
            launcher.transform.SetParent(room.transform, false);
            launcher.transform.localPosition = LauncherLocal;
            launcher.transform.localRotation = LauncherRot;

            // 머티리얼 — 그립/마감재(dark) / 금속부(barrel/sight, light) / 트리거 accent
            var gunGripMat   = MakeUrpMaterial("Carpet_GunGripMat",   new Color(0.15f, 0.15f, 0.18f), false);
            var gunMetalMat  = MakeUrpMaterial("Carpet_GunMetalMat",  new Color(0.45f, 0.47f, 0.52f), false);
            var gunAccentMat = MakeUrpMaterial("Carpet_GunAccentMat", new Color(0.80f, 0.30f, 0.15f), false);

            // --- Grip: 살짝 뒤로 기운 손잡이 (사용자가 잡는 부분) ---
            var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grip.name = "Grip";
            var gripCol = grip.GetComponent<Collider>();
            if (gripCol != null) Object.DestroyImmediate(gripCol);
            grip.transform.SetParent(launcher.transform, false);
            grip.transform.localPosition = new Vector3(0f, -0.075f, -0.015f);
            grip.transform.localRotation = Quaternion.Euler(12f, 0f, 0f); // 살짝 뒤로 기울임
            grip.transform.localScale = new Vector3(0.045f, 0.15f, 0.065f);
            AssignMat(grip, gunGripMat);

            // --- Receiver: 그립 위 본체 ---
            var receiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
            receiver.name = "Receiver";
            var receiverCol = receiver.GetComponent<Collider>();
            if (receiverCol != null) Object.DestroyImmediate(receiverCol);
            receiver.transform.SetParent(launcher.transform, false);
            receiver.transform.localPosition = new Vector3(0f, 0.015f, 0.09f);
            receiver.transform.localScale = new Vector3(0.06f, 0.075f, 0.22f);
            AssignMat(receiver, gunGripMat);

            // --- Barrel: 총신 (cylinder, X축 90도 회전해 long axis 가 +Z 방향) ---
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel";
            var barrelCol = barrel.GetComponent<Collider>();
            if (barrelCol != null) Object.DestroyImmediate(barrelCol);
            barrel.transform.SetParent(launcher.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 0.015f, 0.28f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.035f, 0.10f, 0.035f); // 길이 0.20
            AssignMat(barrel, gunMetalMat);

            // --- MuzzleRing: 총신 끝 살짝 더 큰 ring ---
            var muzzleRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            muzzleRing.name = "MuzzleRing";
            var muzzleRingCol = muzzleRing.GetComponent<Collider>();
            if (muzzleRingCol != null) Object.DestroyImmediate(muzzleRingCol);
            muzzleRing.transform.SetParent(launcher.transform, false);
            muzzleRing.transform.localPosition = new Vector3(0f, 0.015f, 0.40f);
            muzzleRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            muzzleRing.transform.localScale = new Vector3(0.05f, 0.012f, 0.05f); // 길이 0.024
            AssignMat(muzzleRing, gunMetalMat);

            // --- TriggerGuard: 트리거 가드 ---
            var guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            guard.name = "TriggerGuard";
            var guardCol = guard.GetComponent<Collider>();
            if (guardCol != null) Object.DestroyImmediate(guardCol);
            guard.transform.SetParent(launcher.transform, false);
            guard.transform.localPosition = new Vector3(0f, -0.030f, 0.04f);
            guard.transform.localScale = new Vector3(0.025f, 0.035f, 0.025f);
            AssignMat(guard, gunGripMat);

            // --- Trigger: accent 색 작은 돌출 ---
            var trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trigger.name = "Trigger";
            var triggerCol = trigger.GetComponent<Collider>();
            if (triggerCol != null) Object.DestroyImmediate(triggerCol);
            trigger.transform.SetParent(launcher.transform, false);
            trigger.transform.localPosition = new Vector3(0f, -0.025f, 0.035f);
            trigger.transform.localScale = new Vector3(0.012f, 0.022f, 0.008f);
            AssignMat(trigger, gunAccentMat);

            // --- FrontSight ---
            var frontSight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frontSight.name = "FrontSight";
            var frontSightCol = frontSight.GetComponent<Collider>();
            if (frontSightCol != null) Object.DestroyImmediate(frontSightCol);
            frontSight.transform.SetParent(launcher.transform, false);
            frontSight.transform.localPosition = new Vector3(0f, 0.060f, 0.395f);
            frontSight.transform.localScale = new Vector3(0.008f, 0.012f, 0.012f);
            AssignMat(frontSight, gunMetalMat);

            // --- RearSight ---
            var rearSight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rearSight.name = "RearSight";
            var rearSightCol = rearSight.GetComponent<Collider>();
            if (rearSightCol != null) Object.DestroyImmediate(rearSightCol);
            rearSight.transform.SetParent(launcher.transform, false);
            rearSight.transform.localPosition = new Vector3(0f, 0.060f, 0.165f);
            rearSight.transform.localScale = new Vector3(0.022f, 0.010f, 0.012f);
            AssignMat(rearSight, gunMetalMat);

            // --- Muzzle Transform: 모든 비주얼보다 충분히 앞 ---
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(launcher.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.015f, 0.50f);

            // --- AttachPoint: XRGrabInteractable 손 부착 위치 (트리거 부근) ---
            var attach = new GameObject("AttachPoint");
            attach.transform.SetParent(launcher.transform, false);
            attach.transform.localPosition = new Vector3(0f, -0.025f, 0.025f);

            // --- 잡기 / 물리 ---
            // 콜라이더는 그립+리시버만 감쌈. 총신/머즐 비주얼은 콜라이더 바깥이라
            // 카펫이 spawn 되어도 콜라이더와 겹치지 않음. + IgnoreSelfCollision 으로 이중 안전.
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
            launcherGrab.throwOnDetach = false; // 총은 던지지 말고 휴대용.
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
            launcherComp.ActiveCarpetsRoot = active.transform;
            launcherComp.Cooldown = LauncherCooldown;
            launcherComp.SpawnAhead = 0.05f;
            launcherComp.IgnoreSelfCollision = true;

            // === HintBoard — 슬롯 N개 ===
            var board = new GameObject("HintBoard");
            board.transform.SetParent(room.transform, false);
            board.transform.localPosition = BoardLocal;

            // 보드 시각 — 슬롯들을 받치는 검은 패널.
            var boardVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boardVis.name = "Visual";
            var boardVisCol = boardVis.GetComponent<Collider>();
            if (boardVisCol != null) Object.DestroyImmediate(boardVisCol);
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

                // 시각 — 슬롯 컵.
                var cup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cup.name = "Cup";
                var cupCol = cup.GetComponent<Collider>();
                if (cupCol != null) Object.DestroyImmediate(cupCol);
                cup.transform.SetParent(slotGo.transform, false);
                cup.transform.localPosition = new Vector3(0f, -BoardSlotRadius * 0.4f, 0f);
                cup.transform.localScale = Vector3.one * (BoardSlotRadius * 2f);
                AssignMat(cup, slotMat);

                // Dock point — 공이 안착할 정확한 위치.
                var dock = new GameObject("Dock");
                dock.transform.SetParent(slotGo.transform, false);
                dock.transform.localPosition = new Vector3(0f, 0f, 0f);

                var slotComp = slotGo.AddComponent<HintSlot>();
                slotComp.DockPoint = dock.transform;
                boardComp.Slots.Add(slotComp);
            }

            // 보드 OnSolved → 컨트롤러 OnSolved 전파는 컨트롤러의 Start() 에서 런타임 AddListener 로 처리됨.

            // === HintCatcher — 보드 앞 공중에 자석 트리거 ===
            var catcher = new GameObject("HintCatcher");
            catcher.transform.SetParent(room.transform, false);
            catcher.transform.localPosition = CatcherLocal;

            var catcherTrigger = catcher.AddComponent<SphereCollider>();
            catcherTrigger.radius = CatcherTriggerRadius;
            catcherTrigger.isTrigger = true;

            // 시각 — 반투명 구 (트리거 시각화).
            var catcherVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            catcherVis.name = "Visual";
            var catcherVisCol = catcherVis.GetComponent<Collider>();
            if (catcherVisCol != null) Object.DestroyImmediate(catcherVisCol);
            catcherVis.transform.SetParent(catcher.transform, false);
            catcherVis.transform.localPosition = Vector3.zero;
            catcherVis.transform.localScale = Vector3.one * (CatcherTriggerRadius * 2f);
            AssignMat(catcherVis, catcherMat);

            var catcherComp = catcher.AddComponent<HintCatcher>();
            catcherComp.Board = boardComp;

            // === HintBalls — 위험 바닥 위에 흩뿌림 ===
            var ballsRoot = new GameObject("HintBalls");
            ballsRoot.transform.SetParent(room.transform, false);
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
                ball.transform.localPosition = new Vector3(
                    HintBallSpread[i].x,
                    FloorThickness * 0.5f + HintBallRadius + 0.001f, // 위험 바닥 윗면 + 공 반경
                    HintBallSpread[i].y);

                // 시각.
                var ballVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ballVis.name = "Visual";
                var ballVisCol = ballVis.GetComponent<Collider>();
                if (ballVisCol != null) Object.DestroyImmediate(ballVisCol);
                ballVis.transform.SetParent(ball.transform, false);
                ballVis.transform.localPosition = Vector3.zero;
                ballVis.transform.localScale = Vector3.one * (HintBallRadius * 2f);
                AssignMat(ballVis, ballMat);

                // 충돌/잡기.
                var col = ball.AddComponent<SphereCollider>();
                col.radius = HintBallRadius;

                // Rigidbody — 떨어진 자리에 그대로 정지하도록 높은 drag.
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

            // === 컨트롤러 wire-up ===
            ctrl.Dispenser = dispComp;
            ctrl.Goal = goalComp;
            ctrl.ActiveCarpetsRoot = active.transform;
            ctrl.FloorCollider = floor.GetComponent<BoxCollider>();
            ctrl.StartZoneCollider = start.GetComponent<BoxCollider>();
            ctrl.GoalZoneCollider = goalTrigger;
            ctrl.StartPoint = start.transform;
            ctrl.OverlapRadius = 0.15f;
            ctrl.RespawnCooldown = 1.0f;
            ctrl.HintBoard = boardComp;
            ctrl.P1SafeColliders = new Collider[] { p1.GetComponent<BoxCollider>() };
            // ctrl.XROriginRef 는 비워둠 — 런타임 Start 에서 자동 검색.

            EditorUtility.SetDirty(room);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(room.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[RoomCarpet] Build 완료. " +
                      "P1Platform 위에서 카펫 던지기, P2 가 카펫으로 건너 단서공 회수 → HintCatcher 로 던지기. " +
                      $"슬롯 {BoardSlotCount} 채우면 OnSolved. " +
                      "XR Origin 의 초기 위치(P2)는 StartZone 위로 수동 셋업하세요.");
        }

        // ----- Util -----

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
