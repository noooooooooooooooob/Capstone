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

        // ===== Industrial Pipeline 에셋 — per-shape 단일 프리팹 매핑 =====
        // 각 PipeShape 에 해당하는 완성된 모양의 프리팹을 그대로 instantiate.
        // 머티리얼/색은 프리팹 자체 것을 사용 (White 통일, override 없음).
        // 두께축 = X (직경 0.19). 길이축 / 다리 축 = Y, Z (panel 평면).
        const string StraightPipePath = "Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Small/S_Pipe_L100_White_01.prefab";   // Z축 0.5m
        const string ElbowPipePath    = "Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Small/S_Pipe_Corner90_White_13.prefab"; // +Y, +Z 양 다리
        const string TeePipePath      = "Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Small/S_Pipe_White_07.prefab";          // Z 주빔 + Y 스텁
        const string CrossPipePath    = "Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Small/S_Pipe_White_05.prefab";          // 4-way 십자 (YZ 평면)
        const string EndcapPipePath   = "Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Small/S_Pipe_L100_White_01.prefab";      // Source/Sink — cell 폭과 동일 (Slot_0_2 / Slot_5_2 에서 인접 pipe 까지 닿게)

        // Radiator Valve 휠 — primitive(Hub/Disc/Spokes) 대신 이 prefab 사용.
        // M_Valve_Handle_White_01: 자연 두께축 Y(0.03), disc 면 XZ(0.3×0.3).
        // WheelGo (LocalAxis=Z) 와 맞추려면 X 기준 90° 회전 (Y → Z).
        const string ValveHandlePrefabPath = "Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Medium/M_Valve_Handle_White_01.prefab";
        // 시각 핸들만 2배 — disc 직경 ~0.6. RimGrab 콜라이더는 wheelGo 의 별도 자식이라 영향 없음 (반경 0.25 유지).
        const float ValveHandlePrefabScale = 2.0f;

        // SuppressionWheel 의 grab 최소 반경 — 손이 휠 중심에서 이 거리 안이면 select 거부.
        // 작은 값일수록 허브 가까이도 잡힘. (사용자 요청 0.07)
        const float WheelMinGrabRadius = 0.07f;

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
        // cellSize = 0.5 (S_Pipe_L100 길이와 일치 — Straight 가 cell 폭에 딱 맞음).
        // 보드 6×4 (= 3.0×2.0m) — 기존 5×3 대비 더 크고 Tee/Cross 까지 포함해서 더 복잡.
        const int   MG_GridWidth     = 6;     // 5 → 6
        const int   MG_GridHeight    = 4;     // 3 → 4
        const float MG_CellSize      = 0.50f; // 0.18 → 0.50 (Straight L100 = 0.5m 정확히 fit)
        const float MG_Margin        = 0.08f; // 0.04 → 0.08
        const float MG_PanelThickness = 0.02f;
        const float MG_MarkerSize    = 0.15f; // 0.07 → 0.15 (source/sink 시각 구분용)
        const float MG_WallY         = 1.40f;
        const float MG_PanelZ        = -0.025f;
        const float MG_FloorY        = 0.06f;
        const float MG_SnapDistance  = 0.23f; // slot 가로 0.46 의 절반 — 너그러운 부착 판정

        // fallback Cube arm용 (프리팹 못 찾을 때만 동작). 평소엔 안 씀.
        const float MG_ArmThickness  = 0.05f;
        const float MG_HubSize       = 0.10f;

        // ===== SmokeGauge =====
        static readonly Vector3 GaugeLocalOffset = new Vector3(0.8f, 1.4f, 0f); // Radiator 기준 +X/+Y
        const float GaugeRadius = 0.18f;
        const int GaugeSegments = 48;
        const float PointerThickness = 0.008f;
        const float PointerHeadSize = 0.025f;

        // --------------------------------------------------------------------
        // 보조 메뉴 — Radiator 의 Valve Handle 만 새 prefab 으로 교체.
        // Radiator/Wall/Pipe_*/Valve transform 다 보존, RimGrab 콜라이더도 그대로 유지.
        // --------------------------------------------------------------------
        [MenuItem("Tools/PipePuz/Replace Radiator Valve Handle (Only)")]
        public static void ReplaceValveHandleOnly()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("PipeSmokePuz", "활성 씬이 없습니다.", "OK");
                return;
            }

            var container = FindExistingContainer(scene);
            if (container == null)
            {
                EditorUtility.DisplayDialog("PipeSmokePuz",
                    "씬에 PipeSmokePuz 가 없습니다.\n먼저 Build 메뉴로 만들어주세요.", "OK");
                return;
            }

            // PipeSmokePuz/Radiator/Valve/Wheel 트리 추적.
            var radiator = container.transform.Find("Radiator");
            var valve    = radiator != null ? radiator.Find("Valve") : null;
            var wheel    = valve != null ? valve.Find("Wheel") : null;
            if (wheel == null)
            {
                EditorUtility.DisplayDialog("PipeSmokePuz",
                    "Radiator/Valve/Wheel 구조를 찾지 못했습니다.", "OK");
                return;
            }

            var handlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ValveHandlePrefabPath);
            if (handlePrefab == null)
            {
                EditorUtility.DisplayDialog("PipeSmokePuz",
                    $"Prefab not found:\n{ValveHandlePrefabPath}", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace Valve Handle");

            // Wheel 자식 중 옛 primitive 시각 부품 + 옛 Handle 인스턴스 제거.
            // RimGrab_* 콜라이더는 grab 동작 핵심이라 보존.
            var toDestroy = new List<GameObject>();
            foreach (Transform ch in wheel)
            {
                if (ch == null) continue;
                string n = ch.name;
                if (n == "Handle" || n == "Hub" || n == "Disc"
                    || n.StartsWith("Spoke_") || n.StartsWith("RimNub_"))
                {
                    toDestroy.Add(ch.gameObject);
                }
            }
            foreach (var go in toDestroy) Undo.DestroyObjectImmediate(go);

            // 새 Handle prefab 인스턴스.
            var handleInst = (GameObject)PrefabUtility.InstantiatePrefab(handlePrefab, wheel);
            Undo.RegisterCreatedObjectUndo(handleInst, "Create Handle");
            handleInst.name = "Handle";
            handleInst.transform.localPosition = Vector3.zero;
            handleInst.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            handleInst.transform.localScale    = Vector3.one * ValveHandlePrefabScale;

            // 콜라이더 strip — RimGrab 만 grab 트리거.
            foreach (var c in handleInst.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            // SuppressionWheel 컴포넌트의 MinGrabRadius 갱신 (Valve GO 에 부착돼 있음).
            var sw = valve.GetComponent<SuppressionWheel>();
            if (sw != null)
            {
                Undo.RecordObject(sw, "Update MinGrabRadius");
                sw.MinGrabRadius = WheelMinGrabRadius;
                EditorUtility.SetDirty(sw);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            EditorUtility.DisplayDialog("PipeSmokePuz",
                "Valve Handle 교체 완료.\n\n" +
                $"prefab: {System.IO.Path.GetFileNameWithoutExtension(ValveHandlePrefabPath)}\n" +
                $"scale: {ValveHandlePrefabScale:0.00}\n" +
                $"MinGrabRadius: {WheelMinGrabRadius:0.00}\n\n" +
                "기존 Hub/Disc/Spoke 제거 + RimGrab 콜라이더 보존.\n" +
                "회전축 / 크기 어긋나면 PipeSmokePuzSetup.cs 의 ValveHandlePrefabScale 또는 " +
                "handleInst.localRotation 값 조정.",
                "OK");

            Selection.activeGameObject = handleInst;
            EditorGUIUtility.PingObject(handleInst);
        }

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

            // ===== 분기 =====
            // 기존 PipeSmokePuz 가 씬에 있으면 → 'MiniGame2 자식만 재생성' 모드 (Radiator/Smoke/SmokeGauge 보존).
            // 없으면                       → 'Full Build' 모드 (예전처럼 전부 새로 만듦).
            var existingContainer = FindExistingContainer(scene);
            if (existingContainer != null)
            {
                RebuildMiniGame2Only(scene, existingContainer);
                Undo.CollapseUndoOperations(undoGroup);
                EditorGUIUtility.PingObject(existingContainer);
                Selection.activeGameObject = existingContainer;
                EditorUtility.DisplayDialog("PipeSmokePuz",
                    "MiniGame2 자식만 재생성 완료.\n\n" +
                    $"씬 '{scene.name}' 의 PipeSmokePuz worldPos: {existingContainer.transform.position}\n" +
                    "Radiator / SmokeGauge: 손대지 않음.\n" +
                    "MiniGame2 transform: 기존 그대로 유지.\n" +
                    "Smoke: 재생성 + controller 와이어업.\n\n" +
                    "에셋(White) 머티리얼 그대로 사용 — 색 swap 없음.\n" +
                    "Snap 거리: cellSize 의 40% (인접 slot 간섭 방지).",
                    "OK");
                return;
            }

            // ===== Full Build (PipeSmokePuz 가 씬에 처음 생길 때만) =====
            // 0) 기존 컨테이너의 transform 캡처는 의미 없음 (위에서 existingContainer == null 확인됨).
            Vector3    savedWorldPos   = Vector3.zero;
            Quaternion savedWorldRot   = Quaternion.identity;
            Vector3    savedLocalScale = Vector3.one;
            bool       hadExisting     = false;

            // 2) 컨테이너 GO. — 부모 없음 (씬 root). 다른 오브젝트와의 관계 0.
            var container = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(container, "Create PipeSmokePuz");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(container, scene);
            container.transform.position   = savedWorldPos;
            container.transform.rotation   = savedWorldRot;
            container.transform.localScale = savedLocalScale;

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

            string posMsg = hadExisting
                ? $"기존 위치 보존: {savedWorldPos}"
                : "새 위치 (0,0,0)";
            EditorUtility.DisplayDialog("PipeSmokePuz",
                "Build 완료.\n\n" +
                $"씬 '{scene.name}' 의 root 에 'PipeSmokePuz' 가 생성됐습니다.\n" +
                $"{posMsg}\n" +
                "외부 오브젝트 의존성 없음 — 다른 씬에서도 동일 메뉴로 빌드 가능.\n\n" +
                "튜닝: PipeSmokePuz > PipeAllPuzzleController 인스펙터의 " +
                "RecoveryRate / SuppressionPerDegPerSec / MaxSmoke",
                "OK");
        }

        // --------------------------------------------------------------------
        // 기존 컨테이너 정리
        // --------------------------------------------------------------------

        // --------------------------------------------------------------------
        // MiniGame2-only 재생성: 기존 PipeSmokePuz 의 MiniGame2 자식만 교체.
        // Radiator / Smoke / SmokeGauge / PipeAllPuzzleController 는 손대지 않음.
        // MiniGame2 자체의 transform(localPos/Rotation/Scale) 도 기존 값 보존.
        // --------------------------------------------------------------------
        static void RebuildMiniGame2Only(UnityEngine.SceneManagement.Scene scene, GameObject container)
        {
            // 1) 기존 MiniGame2 자식 찾기.
            Transform existingMG = null;
            foreach (Transform child in container.transform)
            {
                if (child != null && child.name == "MiniGame2")
                {
                    existingMG = child;
                    break;
                }
            }

            // 2) 기존 transform 캡처 (있으면).
            Vector3    savedLocalPos   = MiniGameLocalPos;
            Quaternion savedLocalRot   = Quaternion.identity;
            Vector3    savedLocalScale = Vector3.one;
            bool       hadMiniGame     = false;
            if (existingMG != null)
            {
                savedLocalPos   = existingMG.localPosition;
                savedLocalRot   = existingMG.localRotation;
                savedLocalScale = existingMG.localScale;
                hadMiniGame     = true;
                Undo.DestroyObjectImmediate(existingMG.gameObject);
            }

            // 3) MiniGame2 전용 머티리얼 (Panel / FixedFrame / SlotOutline / Source / Sink 마커용).
            //    파이프 자체는 프리팹 머티리얼을 그대로 쓰므로 disconnected/connected 머티리얼은 만들지 않음.
            var panelMat       = MakeUrpMaterial("PSP_Panel",       new Color(0.32f, 0.62f, 0.78f), false);
            var sourceMat      = MakeUrpMaterial("PSP_Source",      new Color(0.20f, 0.90f, 0.40f), false);
            var sinkMat        = MakeUrpMaterial("PSP_Sink",        new Color(0.90f, 0.60f, 0.20f), false);
            var fixedFrameMat  = MakeUrpMaterial("PSP_FixedFrame",  new Color(0.85f, 0.85f, 0.85f), false);
            var slotOutlineMat = MakeUrpMaterial("PSP_SlotOutline", new Color(0.80f, 0.95f, 1f, 0.30f), true);
            // disconnected/connected 는 board 의 머티리얼 swap 용 — 이제 swap 안 하지만 컴포넌트가 require 함.
            // 동일한 White 톤으로 채워 시각 변화 없음.
            var pipeNeutralMat = MakeUrpMaterial("PSP_PipeNeutral", new Color(0.85f, 0.85f, 0.85f), false);

            // 4) 새 MiniGame2 GO + 기존 transform 적용.
            var miniGameGo = new GameObject("MiniGame2");
            Undo.RegisterCreatedObjectUndo(miniGameGo, "Create MiniGame2");
            miniGameGo.transform.SetParent(container.transform, false);
            miniGameGo.transform.localPosition = savedLocalPos;
            miniGameGo.transform.localRotation = savedLocalRot;
            miniGameGo.transform.localScale    = savedLocalScale;

            var board = BuildMiniGame2(miniGameGo,
                panelMat, pipeNeutralMat, pipeNeutralMat, sourceMat, sinkMat,
                fixedFrameMat, slotOutlineMat);

            // 5) Smoke 재생성 — 이전 MiniGame2 통째 삭제로 같이 날아갔으므로 다시 만들어 controller.Smoke 와이어업.
            //    Smoke 는 새 MiniGame2 의 Panel 위치에서 분출.
            var panelT = miniGameGo.transform.Find("Panel");
            var smokeGo = new GameObject("Smoke");
            Undo.RegisterCreatedObjectUndo(smokeGo, "Create Smoke");
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
            {
                var so = new SerializedObject(smokeCtrl);
                var prop = so.FindProperty("Intensity");
                if (prop != null) { prop.floatValue = InitialSmokeForController; so.ApplyModifiedPropertiesWithoutUndo(); }
            }

            // 6) PipeAllPuzzleController 참조 갱신 — MiniGameBoard 와 Smoke 둘 다.
            var ctrl = container.GetComponent<PipeAllPuzzleController>();
            if (ctrl != null)
            {
                Undo.RecordObject(ctrl, "Update controller refs");
                ctrl.MiniGameBoard = board;
                ctrl.Smoke = smokeCtrl;
                EditorUtility.SetDirty(ctrl);
            }

            EditorUtility.SetDirty(miniGameGo);
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[PipeSmokePuz] MiniGame2 재생성 완료. " +
                      $"transform 보존: {(hadMiniGame ? savedLocalPos.ToString() : "(기본값)") }, Smoke 도 갱신.");
        }

        static void CleanupExistingContainer(UnityEngine.SceneManagement.Scene scene)
        {
            // 동일 이름의 GO 를 씬 전체에서 재귀 탐색해 모두 제거.
            // (사용자가 PipeSmokePuz 를 Stage 1 자식으로 옮긴 케이스도 처리.)
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root == null) continue;
                var matches = new List<GameObject>();
                CollectByNameRecursive(root.transform, ContainerName, matches);
                foreach (var go in matches)
                    Undo.DestroyObjectImmediate(go);
            }
        }

        /// <summary>
        /// 씬에서 ContainerName 과 같은 이름의 GO 첫 번째를 재귀 탐색해 반환. 없으면 null.
        /// (root level 뿐 아니라 모든 자식까지 검색 — Stage 1 자식 같은 케이스 지원.)
        /// </summary>
        static GameObject FindExistingContainer(UnityEngine.SceneManagement.Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root == null) continue;
                var t = FindByNameRecursive(root.transform, ContainerName);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        static Transform FindByNameRecursive(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindByNameRecursive(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        static void CollectByNameRecursive(Transform t, string name, List<GameObject> outList)
        {
            if (t == null) return;
            if (t.name == name) outList.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++)
                CollectByNameRecursive(t.GetChild(i), name, outList);
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

            // === Valve Handle 시각 부품 ===
            // S_Valve_Handle_White_01 prefab 으로 교체. prefab 자연 두께축 Y, disc 면 XZ.
            // wheelGo.LocalAxis = Z 와 정렬하려면 X 기준 90° 회전 (Y → Z).
            // 못 찾으면 옛 primitive(Hub + Disc + Spokes) 로 fallback.
            var handlePrefab = LoadPrefab(ValveHandlePrefabPath);
            bool usedPrefabHandle = false;
            if (handlePrefab != null)
            {
                var handleInst = (GameObject)PrefabUtility.InstantiatePrefab(handlePrefab, wheelGo.transform);
                handleInst.name = "Handle";
                handleInst.transform.localPosition = Vector3.zero;
                handleInst.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                handleInst.transform.localScale    = Vector3.one * ValveHandlePrefabScale;

                // 콜라이더 strip — RimGrab 만 grab 트리거로 사용.
                foreach (var c in handleInst.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);

                usedPrefabHandle = true;
            }
            else
            {
                // === Fallback: 옛 primitive 휠 ===
                var hub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hub.name = "Hub";
                Object.DestroyImmediate(hub.GetComponent<Collider>());
                hub.transform.SetParent(wheelGo.transform, false);
                hub.transform.localPosition = Vector3.zero;
                hub.transform.localScale = Vector3.one * HubSize;
                AssignMat(hub, valveMat);

                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Disc";
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                disc.transform.SetParent(wheelGo.transform, false);
                disc.transform.localPosition = Vector3.zero;
                disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                disc.transform.localScale = new Vector3(2f * WheelRadius * 0.95f, DiscThickness, 2f * WheelRadius * 0.95f);
                AssignMat(disc, valveMat);

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
            }

            // === Rim grab colliders — 항상 생성 (grab interaction 핵심) ===
            // 시각 nub 은 prefab handle 시 생략, fallback 일 때만 추가.
            var rimColliders = new List<Collider>();
            for (int i = 0; i < RimGrabCount; i++)
            {
                float a = (i / (float)RimGrabCount) * Mathf.PI * 2f;
                Vector3 pos = new Vector3(Mathf.Cos(a) * WheelRadius, Mathf.Sin(a) * WheelRadius, 0f);

                if (!usedPrefabHandle)
                {
                    var nub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    nub.name = $"RimNub_{i}";
                    Object.DestroyImmediate(nub.GetComponent<Collider>());
                    nub.transform.SetParent(wheelGo.transform, false);
                    nub.transform.localPosition = pos;
                    nub.transform.localScale = Vector3.one * (RimGrabRadius * 0.9f);
                    AssignMat(nub, valveMat);
                }

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
            sw.MinGrabRadius = WheelMinGrabRadius;
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
            int W = MG_GridWidth, H = MG_GridHeight;

            var board = root.AddComponent<PipeMiniGame2Board>();
            board.Width = W;
            board.Height = H;
            board.DisconnectedMaterial = disconnectedMat;
            board.ConnectedMaterial = connectedMat;
            board.SnapDistance = MG_SnapDistance;
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

                    // LightOrbSocket 패턴 — slot 영역의 trigger SphereCollider.
                    // 잡혀있지 않은 Pipe 가 이 sphere 안에 들어오면 자동 흡수.
                    var trigger = slotGo.AddComponent<SphereCollider>();
                    trigger.isTrigger = true;
                    trigger.center = Vector3.zero;
                    trigger.radius = MG_SnapDistance; // 0.23 — slot 가로 0.46 의 절반

                    board.Slots[x + y * W] = slot;
                }
            }

            // Source/Sink (고정) — 가운데 행(y = H/2) 양 끝.
            int midY = H / 2;
            var sourceSlot = board.Slots[0 + midY * W];
            var sourcePipe = CreatePipe("Source", PipeShape.Source, 0, true,
                disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
            sourcePipe.transform.SetParent(root.transform, false);
            sourceSlot.AcceptPipe(sourcePipe);
            sourcePipe.Board = board;
            board.AllPipes.Add(sourcePipe);

            var sinkSlot = board.Slots[(W - 1) + midY * W];
            var sinkPipe = CreatePipe("Sink", PipeShape.Sink, 0, true,
                disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
            sinkPipe.transform.SetParent(root.transform, false);
            sinkSlot.AcceptPipe(sinkPipe);
            sinkPipe.Board = board;
            board.AllPipes.Add(sinkPipe);

            // 바닥 movable 파이프 — Straight 4 + Elbow 6 + Tee 2 + Cross 1 = 13 개.
            // 6×4 보드(가운데 행 양끝 source/sink) — 다양한 경로 선택지.
            var pipeShapes = new[]
            {
                PipeShape.Straight, PipeShape.Straight, PipeShape.Straight, PipeShape.Straight,
                PipeShape.Elbow,    PipeShape.Elbow,    PipeShape.Elbow,    PipeShape.Elbow,    PipeShape.Elbow,    PipeShape.Elbow,
                PipeShape.Tee,      PipeShape.Tee,
                PipeShape.Cross,
            };

            // 보드 앞쪽 바닥에 가로 5개 × 세로 3줄 그리드 배치. cellSize 0.5 에 맞춰 간격 0.55 / 0.55.
            const float floorRowZ0 = 0.70f;
            const float floorRowSpacing = 0.55f;
            const float floorColSpacing = 0.55f;
            int cols = 5;

            for (int i = 0; i < pipeShapes.Length; i++)
            {
                int row = i / cols;
                int col = i % cols;
                Vector3 pos = new Vector3(
                    (col - (cols - 1) * 0.5f) * floorColSpacing,
                    MG_FloorY,
                    floorRowZ0 + row * floorRowSpacing);

                var pipe = CreatePipe($"Pipe_{pipeShapes[i]}_{i}", pipeShapes[i], 0, false,
                    disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
                pipe.transform.SetParent(root.transform, false);
                pipe.transform.localPosition = pos;
                pipe.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                pipe.Board = board;
                board.AllPipes.Add(pipe);
            }

            // ====== 랜덤 경로 생성 + PathLine 시각화 ======
            // Editor-time 한 번 박아 Scene 뷰 미리보기. Runtime(Play 모드) Start() 마다 새 path 로 덮어씀.
            var rand = new System.Random();
            var pathCells = PipeMiniGame2Board.GenerateRandomPath(W, H, midY, rand);
            board.RequiredCells = new List<Vector2Int>(pathCells);

            Debug.Log($"[PipeSmokePuz] (Editor) 초기 path 길이 {pathCells.Count}: " +
                      string.Join(" ", pathCells.ConvertAll(c => $"({c.x},{c.y})")));

            var pathLineMat = MakeUrpUnlitMaterial("PSP_PathLine", new Color(1f, 0.85f, 0.20f, 1f));
            var lr = BuildPathLineRenderer(panel, pathLineMat);
            board.PathLine = lr;
            board.SourceSlot = sourceSlot;
            board.SinkSlot = sinkSlot;
            board.RegeneratePathOnStart = true; // Play 모드 진입 마다 새 path
            board.RandomSeed = -1;              // 시간 기반 시드 — 재현 X
            board.ApplyPathLine();              // Editor 박힌 path 좌표 적용

            return board;
        }

        /// <summary>
        /// PathLine LineRenderer GO + 컴포넌트만 생성. 좌표는 board.ApplyPathLine() 이 채움.
        /// </summary>
        static LineRenderer BuildPathLineRenderer(GameObject panel, Material lineMat)
        {
            var lineGo = new GameObject("PathLine");
            lineGo.transform.SetParent(panel.transform, false);
            lineGo.transform.localPosition = Vector3.zero;
            lineGo.transform.localRotation = Quaternion.identity;

            var lr = lineGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.startWidth = MG_CellSize * 0.10f;
            lr.endWidth   = MG_CellSize * 0.10f;
            lr.sharedMaterial = lineMat;
            lr.numCornerVertices = 3;
            lr.numCapVertices    = 2;
            lr.alignment = LineAlignment.View;
            return lr;
        }

        static PipeMiniGame2Pipe CreatePipe(string name, PipeShape shape, int rotation, bool isFixed,
            Material pipeMat, Material sourceMat, Material sinkMat, Material fixedFrameMat)
        {
            var pipeGo = new GameObject(name);

            var pipeRoot = new GameObject("PipeRoot");
            pipeRoot.transform.SetParent(pipeGo.transform, false);
            pipeRoot.transform.localPosition = Vector3.zero;
            pipeRoot.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * rotation);

            // PipeRenderers 는 비워둠 — board 의 connected/disconnected 머티리얼 swap 을 의도적으로 비활성.
            // 프리팹의 White 머티리얼 그대로 시각화.
            var pipeRenderers = new List<Renderer>();
            var baseMask = PipeShapeDef.BaseMask(shape);

            // === per-shape 단일 프리팹 instantiate ===
            // 프리팹 좌표계: X = 두께축(0.19), Y/Z = 다리 축.
            // 보드 좌표계(pipeRoot 내부): X=E/W, Y=N/S, Z=두께(패널 normal).
            // 회전: 프리팹의 Z → 보드 Y(N), 프리팹의 Y → 보드 X(E). LookRotation(forward=worldY, up=worldX).
            string prefabPath = ShapePrefabPath(shape);
            Quaternion shapeRot = ShapeRotation(shape);
            Vector3    shapeScale = ShapeScale(shape);

            GameObject pipePrefab = LoadPrefab(prefabPath);
            if (pipePrefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pipePrefab, pipeRoot.transform);
                inst.name = "Pipe";
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = shapeRot;
                inst.transform.localScale    = shapeScale;

                // 콜라이더 strip — XRGrabInteractable / raycast 간섭 회피.
                foreach (var c in inst.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);

                // 자동 visual center 보정 안 함 — 사용자가 PipeMiniGame2Pipe.VisualOffset 으로
                // 인스펙터에서 명시적으로 조정. prefab 메시는 원점 그대로 둠.

                // 머티리얼은 절대 override 하지 않음 — 에셋의 White 머티리얼 그대로.
                // pipeRenderers 에도 등록 X (board 가 swap 하지 못하게).
            }
            else
            {
                // Fallback: 프리팹 못 찾으면 옛 방식대로 Hub + Cube Arm 으로 fallback.
                Debug.LogWarning($"[PipeSmokePuz] '{prefabPath}' 못 찾음 — Cube fallback. shape={shape}");
                BuildFallbackHubArms(pipeRoot.transform, baseMask, pipeMat, pipeRenderers);
            }

            // Source/Sink 마커 — 패널보다 살짝 앞으로(0.04 Z+).
            if (shape == PipeShape.Source || shape == PipeShape.Sink)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Marker";
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.transform.SetParent(pipeRoot.transform, false);
                marker.transform.localPosition = new Vector3(0f, 0f, 0.04f);
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

            // shape별 디폴트 VisualOffset — 사용자가 인스펙터에서 다듬은 값을 빌더에 hard-code.
            pipe.VisualOffset = ShapeDefaultVisualOffset(shape);

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

        /// <summary>
        /// pipeRoot 로컬에서 S_Pipe_L25 프리팹(자연 길이축 = Z)을 dir 방향으로 정렬하는 회전.
        /// (fallback Cube arm 로직 호환용 — 현재는 ShapeRotation 이 메인.)
        /// </summary>
        static Quaternion ArmRotationForDir(Direction dir)
        {
            switch (dir)
            {
                case Direction.N: return Quaternion.Euler(-90f, 0f, 0f); // Z → +Y
                case Direction.S: return Quaternion.Euler( 90f, 0f, 0f); // Z → -Y
                case Direction.E: return Quaternion.Euler(  0f, 90f, 0f); // Z → +X
                case Direction.W: return Quaternion.Euler(  0f, -90f, 0f); // Z → -X
                default: return Quaternion.identity;
            }
        }

        // --------------------------------------------------------------------
        // per-shape 프리팹 매핑 + 회전.
        // --------------------------------------------------------------------

        static string ShapePrefabPath(PipeShape shape)
        {
            switch (shape)
            {
                case PipeShape.Straight: return StraightPipePath;
                case PipeShape.Elbow:    return ElbowPipePath;
                case PipeShape.Tee:      return TeePipePath;
                case PipeShape.Cross:    return CrossPipePath;
                case PipeShape.Source:
                case PipeShape.Sink:     return EndcapPipePath;
                default:                 return null;
            }
        }

        /// <summary>
        /// pipeRoot 로컬에서 프리팹을 보드 평면(XY)으로 정렬하는 회전.
        /// 프리팹 좌표계: X=두께, Y/Z=다리 축. 목표: 두께를 보드 Z(panel normal) 로, 다리를 보드 X/Y 로.
        ///
        /// - Straight   (Z 길이축)         : Z → 보드 +Y (N|S 정렬). 회전은 LookRotation(up, right) 동등.
        /// - Elbow      (+Y, +Z 두 다리)   : Y → 보드 +Y(N), Z → 보드 +X(E). N|E 형태.
        /// - Tee        (Z 주빔 + +Y 스텁) : Z → 보드 +Y(N|S 주빔), +Y(스텁) → 보드 +X(E). N|E|S.
        /// - Cross      (4-way YZ 평면)    : Tee 와 동일 회전 (4-way 대칭).
        /// - Source/Sink (Z 짧은 직선)    : Z → 보드 +X (E 방향 short stub).
        /// </summary>
        static Quaternion ShapeRotation(PipeShape shape)
        {
            switch (shape)
            {
                case PipeShape.Straight:
                case PipeShape.Tee:
                case PipeShape.Cross:
                    // Z(prefab) → +Y(board), Y(prefab) → +X(board).
                    return Quaternion.LookRotation(Vector3.up, Vector3.right);

                case PipeShape.Elbow:
                    // Z(prefab) → -X(board), Y(prefab) → -Y(board).
                    // 메시 자연 다리가 (-Y, -Z) 사분면이라 visual N|E 만들려면 두 축 모두 뒤집어야 함.
                    // (이전엔 LookRotation(right, up) 이었는데 사용자가 "완전히 반대일 때 작동" 보고 → 180° 뒤집음.)
                    return Quaternion.LookRotation(Vector3.left, Vector3.down);

                case PipeShape.Source:
                case PipeShape.Sink:
                    // 짧은 stub — Z → +X(board). 마커가 source/sink 구분.
                    return Quaternion.LookRotation(Vector3.right, Vector3.up);

                default:
                    return Quaternion.identity;
            }
        }

        /// <summary>
        /// 프리팹 자연 사이즈 보정 스케일. 모두 1.0 — 프리팹 메시 원본 크기 그대로 사용.
        /// (사용자가 Elbow scale 1.43x 적용했더니 너무 커진다 → 롤백)
        /// 정렬 어긋남은 PipeMiniGame2Pipe.VisualOffset 으로 사용자가 직접 조정.
        /// </summary>
        static Vector3 ShapeScale(PipeShape shape)
        {
            return Vector3.one;
        }

        /// <summary>
        /// PipeMiniGame2Pipe.VisualOffset 의 shape별 디폴트값.
        /// 사용자가 인스펙터에서 시각적으로 맞춘 값을 빌더에 박아 영구화.
        /// 빌드 시 자동으로 적용 — 사용자가 매번 인스펙터에서 조정할 필요 없음.
        /// </summary>
        static Vector3 ShapeDefaultVisualOffset(PipeShape shape)
        {
            switch (shape)
            {
                case PipeShape.Elbow:
                    // 사용자가 시각 조정한 값: 메시 visual center 보정. Corner90_13 메시가 (-X,-Y) 사분면이라
                    // (+0.08, +0.08, 0) 으로 끌어 corner inside 가 slot center 부근에 오게.
                    return new Vector3(0.08f, 0.08f, 0f);
                default:
                    return Vector3.zero;
            }
        }

        /// <summary>
        /// 프리팹 못 찾을 때 폴백: 옛 방식대로 Hub Cube + 방향별 Arm Cube 생성.
        /// </summary>
        static void BuildFallbackHubArms(Transform pipeRoot, Direction baseMask, Material pipeMat, List<Renderer> pipeRenderers)
        {
            // Hub
            var hub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hub.name = "Hub";
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.transform.SetParent(pipeRoot, false);
            hub.transform.localPosition = Vector3.zero;
            hub.transform.localScale = new Vector3(MG_HubSize, MG_HubSize, MG_ArmThickness);
            AssignMat(hub, pipeMat);
            pipeRenderers.Add(hub.GetComponent<Renderer>());

            // Arms
            foreach (var dir in new[] { Direction.N, Direction.E, Direction.S, Direction.W })
            {
                if ((baseMask & dir) == 0) continue;
                var (offset, scale) = ArmTransform(dir);
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = $"Arm_{dir}";
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                arm.transform.SetParent(pipeRoot, false);
                arm.transform.localPosition = offset;
                arm.transform.localScale = scale;
                AssignMat(arm, pipeMat);
                pipeRenderers.Add(arm.GetComponent<Renderer>());
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
        // Prefab helpers — Industrial Pipeline 에셋 instantiate.
        // 못 찾으면 null 반환 → 호출부에서 primitive fallback.
        // --------------------------------------------------------------------

        static GameObject LoadPrefab(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[PipeSmokePuz] Prefab not found: {assetPath} — falling back to primitive.");
            }
            return prefab;
        }

        /// <summary>
        /// 프리팹을 인스턴스화해서 parent 하위에 붙임. 콜라이더는 모두 제거 (radiator 의 시각 부품은 콜라이더 불필요).
        /// </summary>
        static GameObject InstantiatePipelinePrefab(GameObject prefab, Transform parent,
            Vector3 localPos, Quaternion localRot, float scale, string name)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (go == null) return null;
            go.name = name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one * scale;

            // 콜라이더는 grab/raycast 간섭 회피용으로 모두 제거.
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(c);
            }
            return go;
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
