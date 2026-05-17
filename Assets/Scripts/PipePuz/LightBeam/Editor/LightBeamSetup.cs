using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz.LightBeam.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/LightBeam/Build in RoomCliff.
    ///
    /// 절벽 챔버 내에 광선 굴절 퍼즐 구성:
    ///   - 동쪽 벽(챔버 분리벽) 안쪽에 Emitter (forward = -X, 서쪽 향함)
    ///   - cliff chamber 안에 mirror stand 3개 (zigzag 경로 형성)
    ///   - 남쪽 벽 안쪽에 Receiver (forward = +Z, 북쪽에서 오는 광선 받음)
    ///   - 별도 LineRenderer 게임오브젝트 + LightBeamController
    ///
    /// 모든 좌표는 RoomCliff GameObject 의 LOCAL — RoomCliff 옮기면 통째로 따라감.
    ///
    /// 높이 설계:
    ///   - BeamY = 1.3 (가슴~허리 사이, 플레이어 floor=0 에 서서 닿는 높이)
    ///   - MirrorStandTopY = 0.8 (베이스 윗면, 거울 회전 pivot)
    ///   - 거울 visual 은 pivot 위 0.5m 에 center → world y=1.3 정확히 빔과 일치
    /// </summary>
    public static class LightBeamSetup
    {
        // ===== Layout constants (LOCAL relative to RoomCliff) =====
        const float BeamY = 1.3f;
        const float MirrorStandSize = 1.0f;
        const float MirrorStandTopY = 0.8f;
        const float MirrorVisualWidth = 0.7f;
        const float MirrorVisualHeight = 1.0f;
        const float MirrorVisualThickness = 0.05f;
        // visualLocalY = BeamY - MirrorStandTopY = 0.5 (mirror root 가 pivot=base top)
        static readonly float MirrorVisualCenterLocalY = BeamY - MirrorStandTopY;

        // 챔버 boundary (RoomCliffSetup 와 동일)
        const float LeftChamberXmin = -12f;
        const float LeftChamberXmax = +1.5f;
        const float LeftChamberZmin = +3f;
        const float LeftChamberZmax = +14f;
        const float WallThickness = 0.2f;

        // Beam 경로 — Emitter → M1 → M2 → M3 → Receiver
        // 정답 거울 yaw: M1=45°, M2=225°(=-135°), M3=135°.
        // 초기 각도 모두 0° — player 가 회전해서 찾아야 함.
        static readonly Vector3 EmitterLocal = new Vector3(LeftChamberXmax - 0.1f, BeamY, 8.5f);
        static readonly Quaternion EmitterRot = Quaternion.Euler(0f, -90f, 0f); // forward = -X

        static readonly Vector3 Mirror1Local = new Vector3(-3f, 0f, 8.5f);  // base 위치
        static readonly Vector3 Mirror2Local = new Vector3(-3f, 0f, 12f);
        static readonly Vector3 Mirror3Local = new Vector3(-9f, 0f, 12f);

        static readonly Vector3 ReceiverLocal = new Vector3(-9f, BeamY, LeftChamberZmin + 0.3f);
        static readonly Quaternion ReceiverRot = Quaternion.Euler(0f, 0f, 0f); // forward = +Z

        const float InitialYawM1 = 0f;
        const float InitialYawM2 = 0f;
        const float InitialYawM3 = 0f;

        [MenuItem("Tools/PipePuz/LightBeam/Build in RoomCliff")]
        public static void BuildInRoomCliff()
        {
            var roomCliff = GameObject.Find("RoomCliff");
            if (roomCliff == null)
            {
                EditorUtility.DisplayDialog("LightBeam",
                    "씬에서 'RoomCliff' GameObject 를 찾을 수 없습니다. " +
                    "먼저 Tools/PipePuz/Stage3/Build Cliff Layout 으로 빌드하세요.",
                    "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build LightBeam in RoomCliff");

            var oldPuzzle = roomCliff.transform.Find("LightBeamPuzzle");
            if (oldPuzzle != null) Undo.DestroyObjectImmediate(oldPuzzle.gameObject);

            var puzzle = new GameObject("LightBeamPuzzle");
            puzzle.transform.SetParent(roomCliff.transform, false);
            puzzle.transform.localPosition = Vector3.zero;

            // Materials
            var beamMat        = MakeBeamMaterial();
            var emitterFrameMat = MakeUrpMaterial("LightBeam_EmitterFrameMat",
                new Color(0.15f, 0.15f, 0.18f), false);
            var emitterLensMat  = MakeEmissiveMaterial("LightBeam_EmitterLensMat",
                new Color(1f, 0.85f, 0.3f), new Color(2.5f, 2f, 0.7f));
            var mirrorBaseMat   = MakeUrpMaterial("LightBeam_MirrorBaseMat",
                new Color(0.30f, 0.32f, 0.36f), false);
            var mirrorFaceMat   = MakeEmissiveMaterial("LightBeam_MirrorFaceMat",
                new Color(0.85f, 0.90f, 0.95f), new Color(1.4f, 1.5f, 1.7f) * 0.3f);
            var mirrorBackMat   = MakeUrpMaterial("LightBeam_MirrorBackMat",
                new Color(0.18f, 0.18f, 0.22f), false);
            var indicatorMat    = MakeEmissiveMaterial("LightBeam_IndicatorMat",
                new Color(1f, 0.6f, 0.2f), new Color(2.5f, 1.5f, 0.5f));
            var receiverPlateMat = MakeUrpMaterial("LightBeam_ReceiverPlateMat",
                new Color(0.2f, 0.2f, 0.24f), false);

            // ===== Emitter =====
            var emitter = BuildEmitter(puzzle.transform, EmitterLocal, EmitterRot,
                emitterFrameMat, emitterLensMat);

            // ===== Mirrors =====
            var m1 = BuildMirrorStand(puzzle.transform, "Mirror1", Mirror1Local, InitialYawM1,
                mirrorBaseMat, mirrorFaceMat, mirrorBackMat, indicatorMat);
            var m2 = BuildMirrorStand(puzzle.transform, "Mirror2", Mirror2Local, InitialYawM2,
                mirrorBaseMat, mirrorFaceMat, mirrorBackMat, indicatorMat);
            var m3 = BuildMirrorStand(puzzle.transform, "Mirror3", Mirror3Local, InitialYawM3,
                mirrorBaseMat, mirrorFaceMat, mirrorBackMat, indicatorMat);

            // ===== Receiver =====
            var receiver = BuildReceiver(puzzle.transform, ReceiverLocal, ReceiverRot,
                receiverPlateMat);

            // ===== Controller + LineRenderer =====
            var ctrlGo = new GameObject("LightBeamController");
            ctrlGo.transform.SetParent(puzzle.transform, false);
            ctrlGo.transform.localPosition = Vector3.zero;

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

            var ctrl = ctrlGo.AddComponent<LightBeamController>();
            ctrl.Emitter = emitter;
            ctrl.BeamRenderer = lr;
            ctrl.Receivers = new System.Collections.Generic.List<LightBeamReceiver> { receiver };
            ctrl.MaxSegmentDistance = 50f;
            ctrl.MaxBounces = 12;
            ctrl.ReflectOffset = 0.001f;
            ctrl.BeamMask = ~0;

            EditorUtility.SetDirty(roomCliff);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(roomCliff.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[LightBeam] Build 완료. BeamY=1.3, MirrorBase top=0.8. " +
                      "정답 거울 yaw: M1=45°, M2=225°, M3=135°. 초기 모두 0°.");
        }

        // ===== Emitter builder =====
        static LightBeamEmitter BuildEmitter(Transform parent, Vector3 localPos, Quaternion localRot,
            Material frameMat, Material lensMat)
        {
            var root = new GameObject("Emitter");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            DisableColliderIfAny(body);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0f, -0.15f);
            body.transform.localScale = new Vector3(0.4f, 0.4f, 0.3f);
            AssignMat(body, frameMat);

            var lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lens.name = "Lens";
            DisableColliderIfAny(lens);
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

        // ===== Mirror builder =====
        static LightBeamMirror BuildMirrorStand(Transform parent, string name, Vector3 baseLocalPos, float initialYaw,
            Material baseMat, Material faceMat, Material backMat, Material indicatorMat)
        {
            // Stand root — base 의 BOTTOM 중심에 위치 (y=0 ground level)
            var stand = new GameObject(name + "Stand");
            stand.transform.SetParent(parent, false);
            stand.transform.localPosition = baseLocalPos;

            // 베이스 cube — y=0 ~ y=MirrorStandTopY (낮은 받침대)
            var baseCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseCube.name = "Base";
            baseCube.transform.SetParent(stand.transform, false);
            baseCube.transform.localPosition = new Vector3(0f, MirrorStandTopY * 0.5f, 0f);
            baseCube.transform.localScale = new Vector3(MirrorStandSize, MirrorStandTopY, MirrorStandSize);
            AssignMat(baseCube, baseMat);

            // Mirror pivot — base TOP 중심. y = MirrorStandTopY (= 0.8).
            var mirror = new GameObject(name);
            mirror.transform.SetParent(stand.transform, false);
            mirror.transform.localPosition = new Vector3(0f, MirrorStandTopY, 0f);
            mirror.transform.localRotation = Quaternion.Euler(0f, initialYaw, 0f);

            // visual center Y = BeamY - MirrorStandTopY = 0.5 (mirror pivot 위로 0.5m)
            float halfThick = MirrorVisualThickness * 0.5f;
            float visualCenterY = MirrorVisualCenterLocalY;

            // Front face (reflective +Z side)
            var front = GameObject.CreatePrimitive(PrimitiveType.Cube);
            front.name = "Front";
            DisableColliderIfAny(front);
            front.transform.SetParent(mirror.transform, false);
            front.transform.localPosition = new Vector3(0f, visualCenterY, halfThick * 0.5f);
            front.transform.localScale = new Vector3(MirrorVisualWidth, MirrorVisualHeight, halfThick);
            AssignMat(front, faceMat);

            // Back face (dark)
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Back";
            DisableColliderIfAny(back);
            back.transform.SetParent(mirror.transform, false);
            back.transform.localPosition = new Vector3(0f, visualCenterY, -halfThick * 0.5f);
            back.transform.localScale = new Vector3(MirrorVisualWidth, MirrorVisualHeight, halfThick);
            AssignMat(back, backMat);

            // FrontIndicator — 거울 위쪽에 작은 cube 로 front 방향 표시
            var indicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
            indicator.name = "FrontIndicator";
            DisableColliderIfAny(indicator);
            indicator.transform.SetParent(mirror.transform, false);
            indicator.transform.localPosition = new Vector3(0f, visualCenterY + MirrorVisualHeight * 0.5f + 0.05f, halfThick);
            indicator.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);
            indicator.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            AssignMat(indicator, indicatorMat);

            // 거울 자체 collider — raycast 가 hit 할 box.
            var mirrorCol = mirror.AddComponent<BoxCollider>();
            mirrorCol.center = new Vector3(0f, visualCenterY, 0f);
            mirrorCol.size = new Vector3(MirrorVisualWidth, MirrorVisualHeight, MirrorVisualThickness);

            // XRSimpleInteractable — 객체 이동 없이 select 이벤트만 발행.
            // LightBeamMirror 스크립트가 컨트롤러 yaw delta 를 거울 yaw 에 직접 적용.
            mirror.AddComponent<XRSimpleInteractable>();

            // LightBeamMirror 컴포넌트
            var mirrorComp = mirror.AddComponent<LightBeamMirror>();
            mirrorComp.ReflectAxisLocal = Vector3.forward;
            mirrorComp.ReflectDotThreshold = 0.7f;
            mirrorComp.LockPosition = true;
            mirrorComp.LockToYawOnly = true;
            // 회전 모드 — PointTowardHand: 손 위치 방향으로 거울이 향함 (VR 친화적).
            mirrorComp.Mode = LightBeamMirror.RotationMode.PointTowardHand;
            mirrorComp.MinHandDistance = 0.08f;
            mirrorComp.RotationSensitivity = 1.0f; // (WristYawDelta 모드에서만 쓰임)

            return mirrorComp;
        }

        // ===== Receiver builder =====
        static LightBeamReceiver BuildReceiver(Transform parent, Vector3 localPos, Quaternion localRot,
            Material plateMat)
        {
            var root = new GameObject("Receiver");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = localRot;

            // 벽 mount 플레이트
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            DisableColliderIfAny(plate);
            plate.transform.SetParent(root.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            plate.transform.localScale = new Vector3(0.5f, 0.5f, 0.08f);
            AssignMat(plate, plateMat);

            // 크리스털 sphere — receiver visual
            var crystal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crystal.name = "Crystal";
            DisableColliderIfAny(crystal);
            crystal.transform.SetParent(root.transform, false);
            crystal.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            crystal.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);

            // Receiver 본체 collider — sphere area 커버
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

        // ===== Beam material =====
        static Material MakeBeamMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = "LightBeam_BeamMat" };
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
        static void DisableColliderIfAny(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
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
