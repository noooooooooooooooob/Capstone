using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PipePuz;
using PipePuz.MiniGame;
using PipePuz.MiniGame2;
using PipePuz.SmokePuzzle;

namespace PipePuz.SmokePuzzle.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Build PipeSmokePuz (Standalone).
    ///
    /// PipeAll 또는 씬에 미리 있는 'Radiator' / 'PipeMiniGame2' 같은 외부 오브젝트에 일절 의존하지 않고,
    /// 빈 씬·다른 씬 어디서 실행해도 동일하게 동작하는 단일 'PipeSmokePuz' 오브젝트를 처음부터 빌드한다.
    ///
    /// 생성되는 자식 구조 (모두 PipeSmokePuz/ 하위, 외부 참조 없음):
    ///   PipeSmokePuz/
    ///     Radiator/                       ← 새로 만든 라디에이터 (RadiatorA 기능과 동일)
    ///       Wall, Pipe_1..4, Valve (Valve 컴포넌트 대신 SuppressionWheel 직접 부착)
    ///     MiniGame2/                      ← PipeMiniGame2Board 그대로 부착, 5x3 그리드 + Source/Sink + 7 movable pipes
    ///       Panel, Slot_x_y..., Source, Sink, Pipe_*
    ///     Smoke/                          ← Panel 위치에서 분출되는 ParticleSystem + SmokeController
    ///     SmokeGauge/                     ← 반원 게이지 (백·빨강 fill·포인터)
    ///   + PipeAllPuzzleController 컴포넌트 (자식 참조만 wire-up — 외부 0)
    ///
    /// 동작:
    ///   - 시작 시 MaxSmoke(0.85) 강도로 패널에서 연기 분출
    ///   - Radiator 의 휠을 시계방향으로 돌리면 연기 감소
    ///   - 손을 놓거나 멈추면 연기 회복 (MaxSmoke 까지만)
    ///   - PipeMiniGame2 해결 시 연기 영구 정지 (0)
    ///
    /// 다시 누르면 'PipeSmokePuz' 컨테이너를 통째로 지우고 새로 만든다.
    /// 씬의 다른 오브젝트(PipeAll/Radiator/PipeMiniGame2 등)에는 영향 없음.
    /// </summary>
    public static class PipeSmokePuzSetup
    {
        const string ContainerName = "PipeSmokePuz";

        // ===== 컨테이너 내부 레이아웃 =====
        // Radiator(왼쪽) ↔ MiniGame2(오른쪽) 둘이 마주보지 않게 살짝 거리 둠.
        static readonly Vector3 RadiatorLocalPos = new Vector3(-1.5f, 0f, 0f);
        static readonly Vector3 MiniGameLocalPos = new Vector3(+1.5f, 0f, 0f);

        // ===== Smoke / Controller 초기값 (PipeAll 과 동일) =====
        const float InitialSmokeForController = 0.85f;

        // ===== Radiator(=RadiatorA 동등) =====
        static readonly float[] PipeXs = new float[] { -0.6f, -0.2f, 0.2f, 0.6f };
        const int ValveIdx = 1; // 두번째 파이프 자리에 휠

        const float WallY = 1f;
        const float WallZ = -0.5f;
        const float WallW = 1.6f;
        const float WallH = 2.0f;
        const float WallT = 0.1f;

        const float PipeY = 1f;
        const float PipeZ = -0.42f;
        const float PipeRadius = 0.06f;
        const float PipeHalfHeight = 1.0f;

        const float ValveZ = -0.25f;
        const float ValveStemLen = 0.18f;
        const float WheelRadius = 0.25f;
        const float DiscThickness = 0.04f;
        const float SpokeThickness = 0.025f;
        const float HubSize = 0.07f;
        const float RimGrabRadius = 0.06f;
        const int RimGrabCount = 8;

        // ===== MiniGame2 =====
        const float MG_CellSize = 0.18f;
        const float MG_ArmThickness = 0.025f;
        const float MG_HubSize = 0.05f;
        const float MG_Margin = 0.04f;
        const float MG_PanelThickness = 0.02f;
        const float MG_MarkerSize = 0.07f;
        const float MG_WallY = 1.40f;
        const float MG_PanelZ = -0.025f;
        const float MG_FloorY = 0.06f;

        // ===== SmokeGauge =====
        static readonly Vector3 GaugeLocalOffset = new Vector3(0.8f, 1.4f, 0f); // Radiator 기준 +X/+Y
        const float GaugeRadius = 0.18f;
        const int GaugeSegments = 48;
        const float PointerThickness = 0.008f;
        const float PointerHeadSize = 0.025f;

        // --------------------------------------------------------------------
        // Menu entry — 어디서든 실행 가능 (Active Scene 에 빌드).
        // --------------------------------------------------------------------
        [MenuItem("Tools/PipePuz/Build PipeSmokePuz (Standalone)")]
        public static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("PipeSmokePuz",
                    "활성 씬이 없습니다. 빌드할 씬을 먼저 여세요.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build PipeSmokePuz");

            // 1) 기존 PipeSmokePuz 컨테이너 (있다면) 제거. — 이름이 같은 다른 씬 오브젝트도 모두 지움.
            CleanupExistingContainer(scene);

            // 2) 컨테이너 GO. — 부모 없음 (씬 root). 다른 오브젝트와의 관계 0.
            var container = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(container, "Create PipeSmokePuz");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(container, scene);
            container.transform.position = Vector3.zero;
            container.transform.rotation = Quaternion.identity;
            container.transform.localScale = Vector3.one;

            // ===== 머티리얼 (이 빌드만의 신규 인스턴스) =====
            // PipeAll 의 머티리얼과 공유하지 않도록 모두 새로 생성. URP/Lit 셰이더 기반.
            var pipeMat       = MakeUrpMaterial("PSP_PipeMat",     new Color(0.72f, 0.72f, 0.78f, 1f), false);
            var wallMat       = MakeUrpMaterial("PSP_WallMat",     new Color(0.55f, 0.55f, 0.55f, 1f), false);
            var valveMat      = MakeUrpMaterial("PSP_ValveMat",    new Color(0.35f, 0.35f, 0.40f, 1f), false);

            var panelMat        = MakeUrpMaterial("PSP_Panel",        new Color(0.32f, 0.62f, 0.78f), false);
            var disconnectedMat = MakeUrpMaterial("PSP_Disconnected", new Color(1f, 0.85f, 0.20f), false);
            var connectedMat    = MakeUrpMaterial("PSP_Connected",    new Color(0.90f, 0.18f, 0.18f), false);
            var sourceMat       = MakeUrpMaterial("PSP_Source",       new Color(0.20f, 0.90f, 0.40f), false);
            var sinkMat         = MakeUrpMaterial("PSP_Sink",         new Color(0.90f, 0.60f, 0.20f), false);
            var fixedFrameMat   = MakeUrpMaterial("PSP_FixedFrame",   new Color(0.85f, 0.85f, 0.85f), false);
            var slotOutlineMat  = MakeUrpMaterial("PSP_SlotOutline",  new Color(0.80f, 0.95f, 1f, 0.30f), true);

            var gaugeWhite = MakeUrpUnlitMaterial("PSP_GaugeWhite", new Color(0.95f, 0.95f, 0.97f));
            var gaugeRed   = MakeUrpUnlitMaterial("PSP_GaugeRed",   new Color(0.90f, 0.18f, 0.18f));
            var gaugeDark  = MakeUrpUnlitMaterial("PSP_GaugeDark",  new Color(0.10f, 0.10f, 0.12f));
            var gaugeFrame = MakeUrpUnlitMaterial("PSP_GaugeFrame", new Color(0.20f, 0.20f, 0.24f));

            // ===== 3) Radiator (RadiatorA 동등) =====
            var radiator = new GameObject("Radiator");
            radiator.transform.SetParent(container.transform, false);
            radiator.transform.localPosition = RadiatorLocalPos;

            var suppressionWheel = BuildRadiator(radiator.transform,
                pipeMat, wallMat, valveMat);

            // ===== 4) MiniGame2 (독립형 5x3 보드) =====
            var miniGameGo = new GameObject("MiniGame2");
            miniGameGo.transform.SetParent(container.transform, false);
            miniGameGo.transform.localPosition = MiniGameLocalPos;

            var board = BuildMiniGame2(miniGameGo,
                panelMat, disconnectedMat, connectedMat, sourceMat, sinkMat,
                fixedFrameMat, slotOutlineMat);

            // ===== 5) Smoke (MiniGame Panel 위치에서 분출) =====
            var panelT = miniGameGo.transform.Find("Panel");
            var smokeGo = new GameObject("Smoke");
            smokeGo.transform.SetParent(miniGameGo.transform, false);
            if (panelT != null)
            {
                smokeGo.transform.position = panelT.position;
                smokeGo.transform.rotation = panelT.rotation;
            }
            smokeGo.transform.localScale = Vector3.one;

            var ps = smokeGo.AddComponent<ParticleSystem>();
            ConfigureSmokeParticleSystem(ps);
            var smokeCtrl = smokeGo.AddComponent<SmokeController>();
            // 인스펙터 default 0 → SmokeController.Awake 가 ps.Stop 하는 1 프레임 공백 방지
            {
                var so = new SerializedObject(smokeCtrl);
                var prop = so.FindProperty("Intensity");
                if (prop != null) { prop.floatValue = InitialSmokeForController; so.ApplyModifiedPropertiesWithoutUndo(); }
            }

            // ===== 6) 매니저 부착 + wire-up =====
            // PipeAllPuzzleController 클래스 그대로 재사용 — 클래스는 순수 코드라 외부 의존성 0.
            var ctrl = Undo.AddComponent<PipeAllPuzzleController>(container);
            ctrl.Wheel = suppressionWheel;
            ctrl.Smoke = smokeCtrl;
            ctrl.MiniGameBoard = board;
            ctrl.InitialSmoke = InitialSmokeForController;
            ctrl.MaxSmoke = InitialSmokeForController;
            EditorUtility.SetDirty(ctrl);

            // ===== 7) Smoke Gauge =====
            BuildSmokeGauge(container.transform, radiator.transform, ctrl,
                gaugeWhite, gaugeRed, gaugeDark, gaugeFrame);

            EditorUtility.SetDirty(container);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);

            EditorGUIUtility.PingObject(container);
            Selection.activeGameObject = container;

            EditorUtility.DisplayDialog("PipeSmokePuz",
                "Build 완료.\n\n" +
                $"씬 '{scene.name}' 의 root 에 'PipeSmokePuz' 가 생성됐습니다.\n" +
                "외부 오브젝트 의존성 없음 — 다른 씬에서도 동일 메뉴로 빌드 가능.\n\n" +
                "튜닝: PipeSmokePuz > PipeAllPuzzleController 인스펙터의 " +
                "RecoveryRate / SuppressionPerDegPerSec / MaxSmoke",
                "OK");
        }

        // --------------------------------------------------------------------
        // 기존 컨테이너 정리
        // --------------------------------------------------------------------

        static void CleanupExistingContainer(UnityEngine.SceneManagement.Scene scene)
        {
            // 동일 이름의 root GO 들을 모두 제거. (다른 씬 오브젝트엔 손대지 않음)
            var roots = scene.GetRootGameObjects();
            foreach (var go in roots)
            {
                if (go != null && go.name == ContainerName)
                {
                    Undo.DestroyObjectImmediate(go);
                }
            }
        }

        // --------------------------------------------------------------------
        // Radiator build (RadiatorA 동등 — 벽 + 4 파이프 + Valve 위치에 SuppressionWheel 직접 부착)
        // --------------------------------------------------------------------

        static SuppressionWheel BuildRadiator(Transform parent,
            Material pipeMat, Material wallMat, Material valveMat)
        {
            // 벽
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            Object.DestroyImmediate(wall.GetComponent<Collider>());
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = new Vector3(0f, WallY, WallZ);
            wall.transform.localScale = new Vector3(WallW, WallH, WallT);
            AssignMat(wall, wallMat);

            // 4 개의 파이프 (ValveIdx 자리는 시각 파이프 생략하고 그 위치에 휠 부착)
            for (int i = 0; i < PipeXs.Length; i++)
            {
                float x = PipeXs[i];
                if (i == ValveIdx) continue; // 휠 자리 — 파이프 생략

                var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pipe.name = $"Pipe_{i + 1}";
                Object.DestroyImmediate(pipe.GetComponent<Collider>());
                pipe.transform.SetParent(parent, false);
                pipe.transform.localPosition = new Vector3(x, PipeY, PipeZ);
                pipe.transform.localScale = new Vector3(2f * PipeRadius, PipeHalfHeight, 2f * PipeRadius);
                AssignMat(pipe, pipeMat);
            }

            // Valve 자리 — SuppressionWheel 을 처음부터 부착 (Valve 컴포넌트 없음, 교체 절차 불필요)
            var sw = BuildSuppressionWheel(parent, PipeXs[ValveIdx], valveMat);
            return sw;
        }

        // --------------------------------------------------------------------
        // SuppressionWheel 빌드 — 큰 휠 형태, 가장자리 grab.
        // --------------------------------------------------------------------

        static SuppressionWheel BuildSuppressionWheel(Transform parent, float x, Material valveMat)
        {
            var valveGo = new GameObject("Valve");
            valveGo.transform.SetParent(parent, false);
            valveGo.transform.localPosition = new Vector3(x, PipeY, ValveZ);

            // Stem
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stem.name = "Stem";
            Object.DestroyImmediate(stem.GetComponent<Collider>());
            stem.transform.SetParent(valveGo.transform, false);
            stem.transform.localPosition = new Vector3(0f, 0f, -ValveStemLen * 0.5f);
            stem.transform.localScale = new Vector3(0.04f, 0.04f, ValveStemLen);
            AssignMat(stem, valveMat);

            // Wheel (회전 대상)
            var wheelGo = new GameObject("Wheel");
            wheelGo.transform.SetParent(valveGo.transform, false);

            // Hub
            var hub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hub.name = "Hub";
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.transform.SetParent(wheelGo.transform, false);
            hub.transform.localPosition = Vector3.zero;
            hub.transform.localScale = Vector3.one * HubSize;
            AssignMat(hub, valveMat);

            // Disc
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(wheelGo.transform, false);
            disc.transform.localPosition = Vector3.zero;
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(2f * WheelRadius * 0.95f, DiscThickness, 2f * WheelRadius * 0.95f);
            AssignMat(disc, valveMat);

            // Spokes
            for (int i = 0; i < 4; i++)
            {
                float a = (i / 4f) * Mathf.PI * 2f;
                var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spoke.name = $"Spoke_{i}";
                Object.DestroyImmediate(spoke.GetComponent<Collider>());
                spoke.transform.SetParent(wheelGo.transform, false);
                spoke.transform.localPosition = new Vector3(Mathf.Cos(a) * WheelRadius * 0.5f, Mathf.Sin(a) * WheelRadius * 0.5f, 0f);
                spoke.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
                spoke.transform.localScale = new Vector3(WheelRadius, SpokeThickness, SpokeThickness);
                AssignMat(spoke, valveMat);
            }

            // Rim grab colliders + 시각 nub
            var rimColliders = new List<Collider>();
            for (int i = 0; i < RimGrabCount; i++)
            {
                float a = (i / (float)RimGrabCount) * Mathf.PI * 2f;
                Vector3 pos = new Vector3(Mathf.Cos(a) * WheelRadius, Mathf.Sin(a) * WheelRadius, 0f);

                var nub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nub.name = $"RimNub_{i}";
                Object.DestroyImmediate(nub.GetComponent<Collider>());
                nub.transform.SetParent(wheelGo.transform, false);
                nub.transform.localPosition = pos;
                nub.transform.localScale = Vector3.one * (RimGrabRadius * 0.9f);
                AssignMat(nub, valveMat);

                var rimGrab = new GameObject($"RimGrab_{i}");
                rimGrab.transform.SetParent(wheelGo.transform, false);
                rimGrab.transform.localPosition = pos;
                var sc = rimGrab.AddComponent<SphereCollider>();
                sc.radius = RimGrabRadius;
                rimColliders.Add(sc);
            }

            // SuppressionWheel 컴포넌트
            var sw = valveGo.AddComponent<SuppressionWheel>();
            sw.LocalAxis = Vector3.forward;
            sw.InvertDirection = false;
            sw.Wheel = wheelGo.transform;
            sw.MinGrabRadius = WheelRadius * 0.65f;
            sw.MaxGrabRadius = WheelRadius * 1.6f;

            sw.colliders.Clear();
            foreach (var c in rimColliders) sw.colliders.Add(c);

            return sw;
        }

        // --------------------------------------------------------------------
        // MiniGame2 build (5x3 + Source/Sink + 7 movable pipes)
        // --------------------------------------------------------------------

        static PipeMiniGame2Board BuildMiniGame2(GameObject root,
            Material panelMat, Material disconnectedMat, Material connectedMat,
            Material sourceMat, Material sinkMat,
            Material fixedFrameMat, Material slotOutlineMat)
        {
            int W = 5, H = 3;

            var board = root.AddComponent<PipeMiniGame2Board>();
            board.Width = W;
            board.Height = H;
            board.DisconnectedMaterial = disconnectedMat;
            board.ConnectedMaterial = connectedMat;
            board.SnapDistance = 0.20f;
            board.Slots = new PipeMiniGame2Slot[W * H];
            board.AllPipes = new List<PipeMiniGame2Pipe>();

            // Panel
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(root.transform, false);
            float panelW = W * MG_CellSize + 2f * MG_Margin;
            float panelH = H * MG_CellSize + 2f * MG_Margin;
            panel.transform.localPosition = new Vector3(0f, MG_WallY, MG_PanelZ);
            panel.transform.localScale = new Vector3(panelW, panelH, MG_PanelThickness);
            AssignMat(panel, panelMat);

            // Slots
            float originX = -((W - 1) * MG_CellSize) * 0.5f;
            float originY = MG_WallY - ((H - 1) * MG_CellSize) * 0.5f;

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    var slotGo = new GameObject($"Slot_{x}_{y}");
                    slotGo.transform.SetParent(root.transform, false);
                    slotGo.transform.localPosition = new Vector3(originX + x * MG_CellSize, originY + y * MG_CellSize, 0f);

                    var outline = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    outline.name = "EmptyOutline";
                    Object.DestroyImmediate(outline.GetComponent<Collider>());
                    outline.transform.SetParent(slotGo.transform, false);
                    outline.transform.localPosition = Vector3.zero;
                    outline.transform.localScale = new Vector3(MG_CellSize * 0.92f, MG_CellSize * 0.92f, 0.004f);
                    AssignMat(outline, slotOutlineMat);

                    var slot = slotGo.AddComponent<PipeMiniGame2Slot>();
                    slot.X = x;
                    slot.Y = y;
                    slot.Board = board;
                    slot.EmptyOutline = outline;

                    board.Slots[x + y * W] = slot;
                }
            }

            // Source/Sink (고정)
            var sourceSlot = board.Slots[0 + 1 * W];
            var sourcePipe = CreatePipe("Source", PipeShape.Source, 0, true,
                disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
            sourcePipe.transform.SetParent(root.transform, false);
            sourceSlot.AcceptPipe(sourcePipe);
            sourcePipe.Board = board;
            board.AllPipes.Add(sourcePipe);

            var sinkSlot = board.Slots[4 + 1 * W];
            var sinkPipe = CreatePipe("Sink", PipeShape.Sink, 0, true,
                disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
            sinkPipe.transform.SetParent(root.transform, false);
            sinkSlot.AcceptPipe(sinkPipe);
            sinkPipe.Board = board;
            board.AllPipes.Add(sinkPipe);

            // 바닥 7 개 (Straight ×3 + Elbow ×4)
            var pipeShapes = new[]
            {
                PipeShape.Straight, PipeShape.Straight, PipeShape.Straight,
                PipeShape.Elbow,    PipeShape.Elbow,    PipeShape.Elbow,    PipeShape.Elbow,
            };
            var floorPositions = new[]
            {
                new Vector3(-0.45f, MG_FloorY, 0.40f),
                new Vector3(-0.15f, MG_FloorY, 0.40f),
                new Vector3(+0.15f, MG_FloorY, 0.40f),
                new Vector3(+0.45f, MG_FloorY, 0.40f),
                new Vector3(-0.30f, MG_FloorY, 0.60f),
                new Vector3( 0.00f, MG_FloorY, 0.60f),
                new Vector3(+0.30f, MG_FloorY, 0.60f),
            };

            for (int i = 0; i < pipeShapes.Length; i++)
            {
                var pipe = CreatePipe($"Pipe_{pipeShapes[i]}_{i}", pipeShapes[i], 0, false,
                    disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
                pipe.transform.SetParent(root.transform, false);
                pipe.transform.localPosition = floorPositions[i];
                pipe.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                pipe.Board = board;
                board.AllPipes.Add(pipe);
            }

            return board;
        }

        static PipeMiniGame2Pipe CreatePipe(string name, PipeShape shape, int rotation, bool isFixed,
            Material pipeMat, Material sourceMat, Material sinkMat, Material fixedFrameMat)
        {
            var pipeGo = new GameObject(name);

            var pipeRoot = new GameObject("PipeRoot");
            pipeRoot.transform.SetParent(pipeGo.transform, false);
            pipeRoot.transform.localPosition = Vector3.zero;
            pipeRoot.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * rotation);

            var pipeRenderers = new List<Renderer>();
            var baseMask = PipeShapeDef.BaseMask(shape);

            // Hub
            var hub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hub.name = "Hub";
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.transform.SetParent(pipeRoot.transform, false);
            hub.transform.localPosition = Vector3.zero;
            hub.transform.localScale = new Vector3(MG_HubSize, MG_HubSize, MG_ArmThickness);
            AssignMat(hub, pipeMat);
            pipeRenderers.Add(hub.GetComponent<Renderer>());

            // Arms
            foreach (var dir in new[] { Direction.N, Direction.E, Direction.S, Direction.W })
            {
                if ((baseMask & dir) == 0) continue;
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = $"Arm_{dir}";
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                arm.transform.SetParent(pipeRoot.transform, false);
                var (offset, scale) = ArmTransform(dir);
                arm.transform.localPosition = offset;
                arm.transform.localScale = scale;
                AssignMat(arm, pipeMat);
                pipeRenderers.Add(arm.GetComponent<Renderer>());
            }

            // Source/Sink 마커
            if (shape == PipeShape.Source || shape == PipeShape.Sink)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Marker";
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.transform.SetParent(pipeRoot.transform, false);
                marker.transform.localPosition = new Vector3(0f, 0f, MG_ArmThickness * 0.6f);
                marker.transform.localScale = Vector3.one * MG_MarkerSize;
                AssignMat(marker, shape == PipeShape.Source ? sourceMat : sinkMat);
            }

            // 고정 frame
            if (isFixed)
            {
                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frame.name = "FixedFrame";
                Object.DestroyImmediate(frame.GetComponent<Collider>());
                frame.transform.SetParent(pipeGo.transform, false);
                frame.transform.localPosition = new Vector3(0f, 0f, -MG_ArmThickness * 0.6f);
                frame.transform.localScale = new Vector3(MG_CellSize * 0.92f, MG_CellSize * 0.92f, MG_ArmThickness * 0.4f);
                AssignMat(frame, fixedFrameMat);
            }

            var pipe = pipeGo.AddComponent<PipeMiniGame2Pipe>();
            pipe.Shape = shape;
            pipe.Rotation = rotation;
            pipe.IsFixed = isFixed;
            pipe.PipeRoot = pipeRoot.transform;
            pipe.PipeRenderers = pipeRenderers.ToArray();

            if (!isFixed)
            {
                var col = pipeGo.AddComponent<BoxCollider>();
                col.size = new Vector3(MG_CellSize * 0.9f, MG_CellSize * 0.9f, MG_ArmThickness * 3f);

                var rb = pipeGo.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                var grab = pipeGo.AddComponent<XRGrabInteractable>();
                grab.throwOnDetach = false;
                grab.smoothPosition = false;
            }

            return pipe;
        }

        static (Vector3 offset, Vector3 scale) ArmTransform(Direction dir)
        {
            float halfArm = MG_CellSize * 0.25f;
            float armLen = MG_CellSize * 0.5f + 0.005f;
            switch (dir)
            {
                case Direction.N: return (new Vector3(0f, +halfArm, 0f), new Vector3(MG_ArmThickness, armLen, MG_ArmThickness));
                case Direction.S: return (new Vector3(0f, -halfArm, 0f), new Vector3(MG_ArmThickness, armLen, MG_ArmThickness));
                case Direction.E: return (new Vector3(+halfArm, 0f, 0f), new Vector3(armLen, MG_ArmThickness, MG_ArmThickness));
                case Direction.W: return (new Vector3(-halfArm, 0f, 0f), new Vector3(armLen, MG_ArmThickness, MG_ArmThickness));
                default: return (Vector3.zero, Vector3.one * 0.001f);
            }
        }

        // --------------------------------------------------------------------
        // Smoke Gauge build (반원)
        // --------------------------------------------------------------------

        static void BuildSmokeGauge(Transform parent, Transform radiatorRef, PipeAllPuzzleController controller,
            Material whiteMat, Material redMat, Material darkMat, Material frameMat)
        {
            var root = new GameObject("SmokeGauge");
            root.transform.SetParent(parent, false);
            if (radiatorRef != null)
                root.transform.position = radiatorRef.position + GaugeLocalOffset;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // 배경 반원
            var bg = new GameObject("Background");
            bg.transform.SetParent(root.transform, false);
            var bgMf = bg.AddComponent<MeshFilter>();
            var bgMr = bg.AddComponent<MeshRenderer>();
            bgMf.sharedMesh = CreateStaticSectorMesh(GaugeRadius, GaugeSegments, 0f, 180f);
            bgMr.sharedMaterial = whiteMat;

            // 빨강 fill (양면)
            var redFront = new GameObject("RedFill_Front");
            redFront.transform.SetParent(root.transform, false);
            redFront.transform.localPosition = new Vector3(0f, 0f, -0.0015f);
            var redFrontMf = redFront.AddComponent<MeshFilter>();
            var redFrontMr = redFront.AddComponent<MeshRenderer>();
            redFrontMr.sharedMaterial = redMat;

            var redBack = new GameObject("RedFill_Back");
            redBack.transform.SetParent(root.transform, false);
            redBack.transform.localPosition = new Vector3(0f, 0f, +0.0015f);
            var redBackMf = redBack.AddComponent<MeshFilter>();
            var redBackMr = redBack.AddComponent<MeshRenderer>();
            redBackMr.sharedMaterial = redMat;

            // Frame
            BuildGaugeFrame(root.transform, frameMat);

            // Pointer
            var pointer = new GameObject("Pointer");
            pointer.transform.SetParent(root.transform, false);
            pointer.transform.localPosition = new Vector3(0f, 0f, -0.003f);

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.transform.SetParent(pointer.transform, false);
            bar.transform.localPosition = new Vector3(GaugeRadius * 0.5f, 0f, 0f);
            bar.transform.localScale = new Vector3(GaugeRadius * 0.95f, PointerThickness, PointerThickness);
            bar.GetComponent<Renderer>().sharedMaterial = darkMat;

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(pointer.transform, false);
            head.transform.localPosition = new Vector3(GaugeRadius * 0.92f, 0f, 0f);
            head.transform.localScale = new Vector3(PointerHeadSize, PointerHeadSize, PointerThickness);
            head.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            head.GetComponent<Renderer>().sharedMaterial = darkMat;

            var hub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hub.name = "Hub";
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.transform.SetParent(root.transform, false);
            hub.transform.localPosition = new Vector3(0f, 0f, -0.004f);
            hub.transform.localScale = new Vector3(GaugeRadius * 0.12f, GaugeRadius * 0.12f, PointerThickness);
            hub.GetComponent<Renderer>().sharedMaterial = darkMat;

            var gauge = root.AddComponent<SmokeGauge>();
            gauge.Controller = controller;
            gauge.Pointer = pointer.transform;
            gauge.RedFillFilter = redFrontMf;
            gauge.RedFillFilterBack = redBackMf;
            gauge.Radius = GaugeRadius;
            gauge.Segments = GaugeSegments;
        }

        static void BuildGaugeFrame(Transform root, Material frameMat)
        {
            var frame = new GameObject("Frame");
            frame.transform.SetParent(root, false);

            int segs = GaugeSegments / 2;
            float thickness = 0.012f;
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs;
                float t1 = (i + 1) / (float)segs;
                float a0 = Mathf.Lerp(0f, 180f, t0) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(0f, 180f, t1) * Mathf.Deg2Rad;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * GaugeRadius, Mathf.Sin(a0) * GaugeRadius, 0f);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * GaugeRadius, Mathf.Sin(a1) * GaugeRadius, 0f);
                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 dir = (p1 - p0);
                float len = dir.magnitude;
                float angDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = $"FrameSeg_{i}";
                Object.DestroyImmediate(seg.GetComponent<Collider>());
                seg.transform.SetParent(frame.transform, false);
                seg.transform.localPosition = new Vector3(mid.x, mid.y, 0.0005f);
                seg.transform.localRotation = Quaternion.Euler(0f, 0f, angDeg);
                seg.transform.localScale = new Vector3(len * 1.05f, thickness, thickness);
                seg.GetComponent<Renderer>().sharedMaterial = frameMat;
            }

            var baseLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseLine.name = "FrameBase";
            Object.DestroyImmediate(baseLine.GetComponent<Collider>());
            baseLine.transform.SetParent(frame.transform, false);
            baseLine.transform.localPosition = new Vector3(0f, 0f, 0.0005f);
            baseLine.transform.localScale = new Vector3(GaugeRadius * 2.05f, thickness, thickness);
            baseLine.GetComponent<Renderer>().sharedMaterial = frameMat;
        }

        static Mesh CreateStaticSectorMesh(float radius, int segments, float startDeg, float endDeg)
        {
            var mesh = new Mesh { name = "PSP_GaugeSector" };
            var verts = new Vector3[segments + 2];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float ang = Mathf.Lerp(startDeg, endDeg, u) * Mathf.Deg2Rad;
                verts[i + 1] = new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, 0f);
            }
            for (int i = 0; i < segments; i++)
            {
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 2;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // --------------------------------------------------------------------
        // ParticleSystem 설정 (PipeSceneSetup / PipeAllPuzzleSetup 와 동일)
        // --------------------------------------------------------------------

        static void ConfigureSmokeParticleSystem(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = 4.0f;
            main.startSpeed = 0.5f;
            main.startSize = 1.2f;
            main.startColor = new Color(0.65f, 0.65f, 0.65f, 0.95f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 0f),
                    new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 1f),
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.95f, 0.1f),
                    new GradientAlphaKey(0.9f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.6f);
            sizeCurve.AddKey(0.5f, 1.5f);
            sizeCurve.AddKey(1f, 2.2f);
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var smokeMat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
                if (smokeMat != null) renderer.sharedMaterial = smokeMat;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // --------------------------------------------------------------------
        // Material helpers — 모두 신규 인스턴스. PipeAll 머티리얼과 공유 안 함.
        // --------------------------------------------------------------------

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
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return m;
        }

        static Material MakeUrpUnlitMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.doubleSidedGI = true;
            return m;
        }

        static void AssignMat(GameObject go, Material m)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = m;
        }
    }
}
