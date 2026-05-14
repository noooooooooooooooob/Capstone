using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PipePuz;
using PipePuz.MiniGame2;
using PipePuz.SmokePuzzle;

namespace PipePuz.SmokePuzzle.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Build PipeAll Smoke Puzzle.
    ///
    /// 씬의 원본 'Radiator' 와 'PipeMiniGame2' 를 그대로 복제(Object.Instantiate)해서
    /// 'PipeAll/SmokePuzzle' 컨테이너 안으로 가져온 뒤, 다음 후처리를 자동 수행:
    ///   1) 복제판 RadiatorA 의 Valve → SuppressionWheel 로 교체(무한 회전)
    ///   2) 복제판 RadiatorA 의 RadiatorController 제거(매니저가 smoke 단독 제어)
    ///   3) 복제판 RadiatorB/Smoke GameObject 를 복제판 PipeMiniGame2/Panel 자식으로 reparent
    ///      → 패널 뒤에서 패널을 향해 분출되도록 위치/회전 보정
    ///   4) 복제판 RadiatorB 통째로 제거 (이 퍼즐에선 RadiatorA + 분리된 Smoke 만 필요)
    ///   5) PipeAllPuzzleController 부착 + 모든 참조 wire-up
    ///
    /// 다시 누르면 PipeAll/SmokePuzzle 컨테이너를 통째로 지우고 새로 만든다.
    /// 원본 Radiator / PipeMiniGame2 에는 영향 없음.
    /// </summary>
    public static class PipeAllPuzzleSetup
    {
        // 복제판 배치 — PipeAll 로컬 기준
        static readonly Vector3 RadiatorLocalPos = new Vector3(-1.5f, 0f, 0f);
        static readonly Vector3 MiniGameLocalPos = new Vector3(+1.5f, 0f, 0f);

        // Smoke 의 초기 강도. PipeAllPuzzleController.MaxSmoke 와 일치시킨다.
        const float InitialSmokeForController = 0.85f;

        [MenuItem("Tools/PipePuz/Build PipeAll Smoke Puzzle")]
        public static void Build()
        {
            var pipeAll = GameObject.Find("PipeAll");
            if (pipeAll == null)
            {
                EditorUtility.DisplayDialog("PipeAll Smoke Puzzle",
                    "현재 씬에서 'PipeAll' GameObject 를 찾을 수 없습니다.\nPipe Scene 을 열고 다시 시도하세요.", "OK");
                return;
            }

            var origRadiator = GameObject.Find("Radiator");
            var origMini     = GameObject.Find("PipeMiniGame2");
            if (origRadiator == null || origMini == null)
            {
                EditorUtility.DisplayDialog("PipeAll Smoke Puzzle",
                    "원본 'Radiator' 또는 'PipeMiniGame2' GameObject 를 씬에서 찾을 수 없습니다.\n" +
                    "Pipe Scene 에 두 오브젝트가 모두 존재해야 합니다.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build PipeAll Smoke Puzzle");

            // 기존 SmokePuzzle 컨테이너 정리
            var existing = pipeAll.transform.Find("SmokePuzzle");
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            // 컨테이너
            var container = new GameObject("SmokePuzzle");
            Undo.RegisterCreatedObjectUndo(container, "Create SmokePuzzle");
            container.transform.SetParent(pipeAll.transform, false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.identity;
            container.transform.localScale    = Vector3.one;

            // ===== 1) 원본 복제 (deep copy, 원본은 무영향) =====
            var dupRadiator = Object.Instantiate(origRadiator);
            Undo.RegisterCreatedObjectUndo(dupRadiator, "Duplicate Radiator");
            dupRadiator.name = "Radiator_Smoke";
            dupRadiator.transform.SetParent(container.transform, worldPositionStays: false);
            dupRadiator.transform.localPosition = RadiatorLocalPos;
            dupRadiator.transform.localRotation = Quaternion.identity;
            dupRadiator.transform.localScale    = origRadiator.transform.localScale;

            var dupMini = Object.Instantiate(origMini);
            Undo.RegisterCreatedObjectUndo(dupMini, "Duplicate PipeMiniGame2");
            dupMini.name = "PipeMiniGame2_Smoke";
            dupMini.transform.SetParent(container.transform, worldPositionStays: false);
            dupMini.transform.localPosition = MiniGameLocalPos;
            dupMini.transform.localRotation = Quaternion.identity;
            dupMini.transform.localScale    = origMini.transform.localScale;

            // ===== 2) Valve → SuppressionWheel 교체 (RadiatorA) =====
            SuppressionWheel suppressionWheel = null;
            var radiatorAValveT = FindDeep(dupRadiator.transform, "RadiatorA", "Valve");
            if (radiatorAValveT != null)
            {
                suppressionWheel = ReplaceValveWithSuppressionWheel(radiatorAValveT.gameObject);
            }
            else
            {
                Debug.LogWarning("[PipeAllPuzzle] 복제판에서 'RadiatorA/Valve' 를 찾지 못함. SuppressionWheel 부착 실패.");
            }

            // ===== 3) RadiatorA 의 RadiatorController 제거 (smoke 제어 매니저 일원화) =====
            DestroyRadiatorController(dupRadiator.transform.Find("RadiatorA"));

            // ===== 4) RadiatorB 통째로 제거 (Smoke 도 이 안에 있었지만 어차피 새로 만들 것) =====
            var radiatorBT = dupRadiator.transform.Find("RadiatorB");
            if (radiatorBT != null) Undo.DestroyObjectImmediate(radiatorBT.gameObject);

            // ===== 5) Smoke 를 PipeMiniGame2_Smoke 자식으로 새로 생성 =====
            // 원본 reparent 가 환경에 따라 실패하는 케이스가 있어, 항상 새로 만든다.
            // 설정은 PipeSceneSetup.ConfigureSmokeParticleSystem 과 동일하게 적용.
            var panelT = dupMini.transform.Find("Panel");
            var smokeGo = new GameObject("Smoke");
            Undo.RegisterCreatedObjectUndo(smokeGo, "Create Smoke");
            smokeGo.transform.SetParent(dupMini.transform, worldPositionStays: false);
            if (panelT != null)
            {
                // panel 의 월드 위치/회전을 그대로 따라가게 두면 panel 평면에서 연기가 새어 나옴.
                smokeGo.transform.position = panelT.position;
                smokeGo.transform.rotation = panelT.rotation;
            }
            else
            {
                Debug.LogWarning("[PipeAllPuzzle] 복제판에서 'PipeMiniGame2/Panel' 을 찾지 못해 Smoke 위치 보정 생략.");
            }
            smokeGo.transform.localScale = Vector3.one;

            var ps = smokeGo.AddComponent<ParticleSystem>();
            ConfigureSmokeParticleSystem(ps);
            var smokeCtrl = smokeGo.AddComponent<SmokeController>();

            // 시작부터 연기가 최대 캡으로 보이도록 SmokeController.Intensity 를 사전 주입.
            // (인스펙터 default 0 → SmokeController.Awake 가 ps.Stop 시키는 1 프레임 공백 회피)
            {
                var so = new SerializedObject(smokeCtrl);
                var prop = so.FindProperty("Intensity");
                if (prop != null) { prop.floatValue = InitialSmokeForController; so.ApplyModifiedPropertiesWithoutUndo(); }
            }

            // ===== 6) Board 참조 =====
            var board = dupMini.GetComponent<PipeMiniGame2Board>();
            if (board == null)
                Debug.LogWarning("[PipeAllPuzzle] 복제판 PipeMiniGame2 에 PipeMiniGame2Board 컴포넌트가 없습니다.");

            // ===== 7) 매니저 부착 + wire-up =====
            var ctrl = Undo.AddComponent<PipeAllPuzzleController>(container);
            ctrl.Wheel = suppressionWheel;
            ctrl.Smoke = smokeCtrl;
            ctrl.MiniGameBoard = board;
            ctrl.InitialSmoke = InitialSmokeForController;
            ctrl.MaxSmoke = InitialSmokeForController;
            EditorUtility.SetDirty(ctrl);

            EditorUtility.SetDirty(pipeAll);
            EditorSceneManager.MarkSceneDirty(pipeAll.scene);
            Undo.CollapseUndoOperations(undoGroup);

            EditorUtility.DisplayDialog("PipeAll Smoke Puzzle",
                "Build 완료.\n\n" +
                "PipeAll/SmokePuzzle 안에 다음이 생성됨:\n" +
                "  - Radiator_Smoke (RadiatorA 만 — RadiatorB 는 제거됨)\n" +
                "  - PipeMiniGame2_Smoke/Smoke (Panel 위치에서 새로 생성된 ParticleSystem)\n" +
                "  - PipeAllPuzzleController\n\n" +
                "동작:\n" +
                "  - 시작 시 MaxSmoke(0.85) 강도로 패널에서 연기 분출\n" +
                "  - RadiatorA 의 휠을 시계방향으로 돌리면 연기 감소\n" +
                "  - 손을 놓거나 멈추면 연기 회복(MaxSmoke 까지만)\n" +
                "  - PipeMiniGame2 해결 시 연기 영구 정지(0)\n\n" +
                "튜닝: PipeAllPuzzleController 의 RecoveryRate / SuppressionPerDegPerSec / MaxSmoke",
                "OK");
        }

        // ===== 후처리 헬퍼 =====

        /// <summary>
        /// Valve 가 부착된 GameObject 의 Valve 컴포넌트를 제거하고 SuppressionWheel 을 새로 부착.
        /// Wheel/LocalAxis/InvertDirection/GrabRadius 등 기존 인스펙터 값을 가능한 한 그대로 옮긴다.
        /// </summary>
        static SuppressionWheel ReplaceValveWithSuppressionWheel(GameObject valveGo)
        {
            if (valveGo == null) return null;

            var oldValve = valveGo.GetComponent<Valve>();
            Vector3 axis = Vector3.forward;
            bool invert = false;
            Transform wheel = null;
            float minR = 0.15f;
            float maxR = 0.4f;

            if (oldValve != null)
            {
                axis = oldValve.LocalAxis;
                invert = oldValve.InvertDirection;
                wheel = oldValve.Wheel;
                minR = oldValve.MinGrabRadius;
                maxR = oldValve.MaxGrabRadius;
                Undo.DestroyObjectImmediate(oldValve);
            }

            var sw = Undo.AddComponent<SuppressionWheel>(valveGo);
            sw.LocalAxis = axis;
            sw.InvertDirection = invert;
            sw.Wheel = wheel != null ? wheel : valveGo.transform;
            sw.MinGrabRadius = minR;
            sw.MaxGrabRadius = maxR;
            return sw;
        }

        /// <summary>RadiatorController 컴포넌트 제거 (매니저 일원화).</summary>
        static void DestroyRadiatorController(Transform radT)
        {
            if (radT == null) return;
            var ctrl = radT.GetComponent<RadiatorController>();
            if (ctrl != null) Undo.DestroyObjectImmediate(ctrl);
        }

        /// <summary>Transform 트리에서 두 단계 깊이로 자식 검색 (예: "RadiatorA/Valve").</summary>
        static Transform FindDeep(Transform root, string l1, string l2)
        {
            var a = root.Find(l1);
            if (a == null) return null;
            return a.Find(l2);
        }

        // ===== ParticleSystem 설정 =====
        // PipeSceneSetup.ConfigureSmokeParticleSystem 과 동일하게 유지.

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
            emission.rateOverTime = 0f; // SmokeController 가 제어

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
    }
}
