using Fusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.Firefight.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Build Firefight.
    /// Pipe Scene 의 'Firefight' GameObject 안에 다음을 자동 생성:
    ///   - Pump (받침대 + 위아래 슬라이드 Handle)
    ///   - PressureGauge (압력 시각화)
    ///   - HoseStand + Hose (잡을 수 있는 호스, 노즐 + WaterStream)
    ///   - Fires 3 개 (불꽃 ParticleSystem + SphereCollider)
    ///   - FirefightController (wire-up)
    /// </summary>
    public static class FirefightSetup
    {
        // ---- 위치 (Firefight 로컬, 사용자 origin 에 +Z forward 기준) ----
        static readonly Vector3 PumpPos = new Vector3(-1.2f, 0f, -0.3f);
        static readonly Vector3 GaugePos = new Vector3(-1.2f, 1.55f, 0.3f);
        static readonly Vector3 HoseStandPos = new Vector3(+1.2f, 0f, -0.3f);
        static readonly Vector3 HoseInitPos = new Vector3(+1.2f, 1.05f, -0.3f);
        static readonly Quaternion HoseInitRot = Quaternion.Euler(15f, 60f, 0f); // 초기에 살짝 fires 방향 향해

        static readonly Vector3[] FirePositions = new[]
        {
            new Vector3(-0.7f, 0.25f, 1.5f),
            new Vector3( 0.0f, 0.25f, 1.8f),
            new Vector3(+0.7f, 0.25f, 1.5f),
        };

        // Pump — 실제 펌프 형태
        const float PumpBodyHeight = 0.60f;     // 굵은 본체
        const float PumpBodyDiameter = 0.30f;
        const float PumpShaftDiameter = 0.05f;  // 본체 안으로 들어가는 가는 샤프트
        const float PumpShaftLength = 0.60f;    // 항상 본체 안으로 들어갈 만큼 길게
        const float TrackTopY = 1.20f;          // HandleAssembly 가 위 끝일 때 Y
        const float TrackBottomY = 0.65f;       // 아래 끝일 때 Y (본체 위면 살짝 위)
        const float GripBarLength = 0.30f;      // T 자 가로 그립 길이
        const float GripBarDiameter = 0.05f;
        const float GripColliderHalfX = 0.18f;  // 그립 잡기 collider X 반폭
        const float GripColliderHalfY = 0.06f;
        const float GripColliderHalfZ = 0.06f;

        // Gauge
        const float GaugeFrameW = 0.12f;
        const float GaugeFrameH = 0.5f;
        const float GaugeFrameThickness = 0.03f;
        const float GaugeFillMaxScale = 0.45f;

        // Hose
        const float HoseLength = 0.30f;
        const float HoseRadius = 0.035f;
        const float HoseStandHeight = 1.0f;

        // Fire
        const float FireColliderRadius = 0.30f;

        [MenuItem("Tools/PipePuz/Build Firefight")]
        public static void Build()
        {
            var room = GameObject.Find("Firefight");
            if (room == null)
            {
                EditorUtility.DisplayDialog("Firefight",
                    "씬에서 'Firefight' 오브젝트를 찾을 수 없습니다.\nPipe Scene 을 열고 다시 시도하세요.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Firefight");

            DestroyChildIfExists(room.transform, "Pump");
            DestroyChildIfExists(room.transform, "PressureGauge");
            DestroyChildIfExists(room.transform, "HoseStand");
            DestroyChildIfExists(room.transform, "Hose");
            DestroyChildIfExists(room.transform, "Fires");
            var oldCtrl = room.GetComponent<FirefightController>();
            if (oldCtrl != null) Undo.DestroyObjectImmediate(oldCtrl);

            // 머티리얼
            var pumpStandMat = MakeUrpMaterial("FF_PumpStand", new Color(0.32f, 0.32f, 0.36f), false);
            var pumpHandleMat = MakeEmissiveMaterial("FF_PumpHandle",
                new Color(1f, 0.55f, 0.18f),
                new Color(1.2f, 0.5f, 0.1f) * 0.8f);

            var gaugeFrameMat = MakeUrpMaterial("FF_GaugeFrame", new Color(0.15f, 0.15f, 0.17f), false);
            var gaugeFillMat = MakeEmissiveMaterial("FF_GaugeFill",
                new Color(0.5f, 0.5f, 0.5f),
                new Color(0.8f, 0.5f, 0.5f) * 0.5f);

            var hoseStandMat = MakeUrpMaterial("FF_HoseStand", new Color(0.35f, 0.35f, 0.38f), false);
            var hoseBodyMat = MakeUrpMaterial("FF_HoseBody", new Color(0.12f, 0.12f, 0.14f), false);
            var nozzleMat = MakeEmissiveMaterial("FF_Nozzle",
                new Color(0.75f, 0.75f, 0.78f),
                new Color(0.3f, 0.4f, 0.6f) * 0.6f);

            var fireBaseMat = MakeUrpMaterial("FF_FireBase", new Color(0.15f, 0.10f, 0.08f), false);

            // Controller 먼저 생성 (참조 wire-up 끝에).
            var ctrl = room.AddComponent<FirefightController>();

            // ===== Pump (진짜 펌프 형태) =====
            // 구조:
            //   Pump (root, FirefightPump 컴포넌트)
            //   ├── Body                  (굵은 실린더 — 고정)
            //   ├── TrackTop / TrackBottom (empty refs)
            //   └── HandleAssembly         (수직 이동, XRSimpleInteractable)
            //       ├── Shaft               (가는 실린더, 본체 안으로 들어감)
            //       ├── GripBar             (T 자 가로 그립)
            //       └── (BoxCollider 그립 영역 + XRSimpleInteractable)
            var pumpGo = new GameObject("Pump");
            pumpGo.transform.SetParent(room.transform, false);
            pumpGo.transform.localPosition = PumpPos;

            // Body — 굵은 펌프 본체 (고정).
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(pumpGo.transform, false);
            body.transform.localPosition = new Vector3(0f, PumpBodyHeight * 0.5f, 0f);
            body.transform.localScale = new Vector3(PumpBodyDiameter, PumpBodyHeight * 0.5f, PumpBodyDiameter);
            AssignMat(body, pumpStandMat);

            // Body 위쪽 림 — 얇은 디스크로 펌프 캡 느낌.
            var bodyCap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bodyCap.name = "BodyCap";
            Object.DestroyImmediate(bodyCap.GetComponent<Collider>());
            bodyCap.transform.SetParent(pumpGo.transform, false);
            bodyCap.transform.localPosition = new Vector3(0f, PumpBodyHeight + 0.005f, 0f);
            bodyCap.transform.localScale = new Vector3(PumpBodyDiameter + 0.04f, 0.02f, PumpBodyDiameter + 0.04f);
            AssignMat(bodyCap, pumpHandleMat); // 캡은 살짝 발광색

            // 트랙 양 끝.
            var trackTop = new GameObject("TrackTop");
            trackTop.transform.SetParent(pumpGo.transform, false);
            trackTop.transform.localPosition = new Vector3(0f, TrackTopY, 0f);

            var trackBottom = new GameObject("TrackBottom");
            trackBottom.transform.SetParent(pumpGo.transform, false);
            trackBottom.transform.localPosition = new Vector3(0f, TrackBottomY, 0f);

            // HandleAssembly — 위아래로 이동하는 묶음.
            var handleAsm = new GameObject("HandleAssembly");
            handleAsm.transform.SetParent(pumpGo.transform, false);
            handleAsm.transform.localPosition = new Vector3(0f, TrackTopY, 0f); // 시작 = 위 끝

            // Shaft — 가는 수직 실린더. HandleAssembly 의 로컬 (0, -shaftLen/2, 0) 위치 →
            // 상단이 assembly 원점, 하단이 본체 안으로 깊이 들어감.
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            Object.DestroyImmediate(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(handleAsm.transform, false);
            shaft.transform.localPosition = new Vector3(0f, -PumpShaftLength * 0.5f, 0f);
            shaft.transform.localScale = new Vector3(PumpShaftDiameter, PumpShaftLength * 0.5f, PumpShaftDiameter);
            AssignMat(shaft, pumpStandMat);

            // GripBar — T 자 가로 그립. 실린더 Y 축이 길이축이므로 Z 축 90° 회전해서 X 축이 길이.
            var gripBar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            gripBar.name = "GripBar";
            Object.DestroyImmediate(gripBar.GetComponent<Collider>());
            gripBar.transform.SetParent(handleAsm.transform, false);
            gripBar.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            gripBar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            gripBar.transform.localScale = new Vector3(GripBarDiameter, GripBarLength * 0.5f, GripBarDiameter);
            AssignMat(gripBar, pumpHandleMat);

            // Grip collider — HandleAssembly 의 BoxCollider, 그립 영역만 덮음.
            var gripCol = handleAsm.AddComponent<BoxCollider>();
            gripCol.center = new Vector3(0f, 0.025f, 0f);
            gripCol.size = new Vector3(GripColliderHalfX * 2f, GripColliderHalfY * 2f, GripColliderHalfZ * 2f);
            gripCol.isTrigger = false;

            // XRSimpleInteractable — select 이벤트만 사용. position 은 FirefightPump 가 직접 제어.
            var gripInter = handleAsm.AddComponent<XRSimpleInteractable>();

            // 펌프 컴포넌트.
            var pumpComp = pumpGo.AddComponent<FirefightPump>();
            pumpComp.Handle = handleAsm.transform;
            pumpComp.GripInteractable = gripInter;
            pumpComp.TrackTop = trackTop.transform;
            pumpComp.TrackBottom = trackBottom.transform;
            pumpComp.Controller = ctrl;
            pumpComp.StrokeThreshold = 0.15f;
            pumpComp.PumpBoost = 0.3f;
            pumpComp.MinStrokeInterval = 0.2f;

            // ===== PressureGauge =====
            var gaugeGo = new GameObject("PressureGauge");
            gaugeGo.transform.SetParent(room.transform, false);
            gaugeGo.transform.localPosition = GaugePos;

            // Frame
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.transform.SetParent(gaugeGo.transform, false);
            frame.transform.localPosition = Vector3.zero;
            frame.transform.localScale = new Vector3(GaugeFrameW, GaugeFrameH, GaugeFrameThickness);
            AssignMat(frame, gaugeFrameMat);

            // Fill wrapper 2 개 — 앞면 / 뒷면. frame 의 양쪽 면에 각각 배치해서
            // 사용자가 어느 방향에서 봐도 게이지가 보이게 한다.
            float fillBaseY = -GaugeFrameH * 0.5f + 0.015f;
            float fillFrontZ = GaugeFrameThickness * 0.5f + 0.008f;
            float fillBackZ = -(GaugeFrameThickness * 0.5f + 0.008f);

            // ----- Front fill -----
            var fillWrapperFront = new GameObject("FillBarWrapperFront");
            fillWrapperFront.transform.SetParent(gaugeGo.transform, false);
            fillWrapperFront.transform.localPosition = new Vector3(0f, fillBaseY, fillFrontZ);
            fillWrapperFront.transform.localScale = new Vector3(GaugeFrameW * 0.78f, 0f, 0.012f);

            var fillInnerFront = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fillInnerFront.name = "InnerFront";
            Object.DestroyImmediate(fillInnerFront.GetComponent<Collider>());
            fillInnerFront.transform.SetParent(fillWrapperFront.transform, false);
            fillInnerFront.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            fillInnerFront.transform.localScale = Vector3.one;
            AssignMat(fillInnerFront, gaugeFillMat);

            // ----- Back fill (frame 의 반대편) -----
            var fillWrapperBack = new GameObject("FillBarWrapperBack");
            fillWrapperBack.transform.SetParent(gaugeGo.transform, false);
            fillWrapperBack.transform.localPosition = new Vector3(0f, fillBaseY, fillBackZ);
            fillWrapperBack.transform.localScale = new Vector3(GaugeFrameW * 0.78f, 0f, 0.012f);

            var fillInnerBack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fillInnerBack.name = "InnerBack";
            Object.DestroyImmediate(fillInnerBack.GetComponent<Collider>());
            fillInnerBack.transform.SetParent(fillWrapperBack.transform, false);
            fillInnerBack.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            fillInnerBack.transform.localScale = Vector3.one;
            AssignMat(fillInnerBack, gaugeFillMat);

            var gaugeComp = gaugeGo.AddComponent<FirefightPressureGauge>();
            gaugeComp.Controller = ctrl;
            gaugeComp.FillBars = new[] { fillWrapperFront.transform, fillWrapperBack.transform };
            gaugeComp.FillRenderers = new[]
            {
                fillInnerFront.GetComponent<Renderer>(),
                fillInnerBack.GetComponent<Renderer>(),
            };
            gaugeComp.MaxScale = GaugeFillMaxScale;
            gaugeComp.LowColor = new Color(0.9f, 0.25f, 0.18f);
            gaugeComp.HighColor = new Color(0.25f, 0.95f, 0.45f);

            // ===== HoseStand =====
            var hoseStandGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hoseStandGo.name = "HoseStand";
            Object.DestroyImmediate(hoseStandGo.GetComponent<Collider>());
            hoseStandGo.transform.SetParent(room.transform, false);
            hoseStandGo.transform.localPosition = new Vector3(HoseStandPos.x, HoseStandHeight * 0.5f, HoseStandPos.z);
            hoseStandGo.transform.localScale = new Vector3(0.18f, HoseStandHeight * 0.5f, 0.18f);
            AssignMat(hoseStandGo, hoseStandMat);

            // ===== Hose =====
            var hoseGo = new GameObject("Hose");
            hoseGo.transform.SetParent(room.transform, false);
            hoseGo.transform.localPosition = HoseInitPos;
            hoseGo.transform.localRotation = HoseInitRot;

            // Body — Cylinder, length 축이 Z 가 되도록 회전.
            var hoseBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hoseBody.name = "BodyVisual";
            Object.DestroyImmediate(hoseBody.GetComponent<Collider>());
            hoseBody.transform.SetParent(hoseGo.transform, false);
            hoseBody.transform.localPosition = Vector3.zero;
            hoseBody.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Y 축이 길이 → Z 축으로
            hoseBody.transform.localScale = new Vector3(HoseRadius * 2f, HoseLength * 0.5f, HoseRadius * 2f);
            AssignMat(hoseBody, hoseBodyMat);

            // Grab collider — CapsuleCollider on hose root, direction Z.
            var hoseCol = hoseGo.AddComponent<CapsuleCollider>();
            hoseCol.direction = 2; // Z
            hoseCol.radius = HoseRadius * 1.4f;
            hoseCol.height = HoseLength + 0.04f;

            var hoseRb = hoseGo.AddComponent<Rigidbody>();
            hoseRb.useGravity = false;
            hoseRb.isKinematic = true;
            hoseRb.interpolation = RigidbodyInterpolation.Interpolate;

            var hoseGrab = hoseGo.AddComponent<XRGrabInteractable>();
            hoseGrab.throwOnDetach = false;
            hoseGrab.smoothPosition = false;

            // 네트워크 잡기 동기화 — 다른 grabbable과 동일한 표준 레시피.
            // 이게 없으면 호스는 순수 로컬 오브젝트라 한쪽이 잡고 움직여도 상대에겐 동기화되지 않는다.
            SetupNetworkGrabbable(hoseGo);

            // Nozzle — empty Transform at front + small visual.
            var nozzle = new GameObject("Nozzle");
            nozzle.transform.SetParent(hoseGo.transform, false);
            nozzle.transform.localPosition = new Vector3(0f, 0f, HoseLength * 0.5f + 0.02f);
            nozzle.transform.localRotation = Quaternion.identity;

            var nozzleVis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            nozzleVis.name = "Visual";
            Object.DestroyImmediate(nozzleVis.GetComponent<Collider>());
            nozzleVis.transform.SetParent(nozzle.transform, false);
            nozzleVis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            nozzleVis.transform.localPosition = Vector3.zero;
            nozzleVis.transform.localScale = new Vector3(HoseRadius * 2.4f, 0.025f, HoseRadius * 2.4f);
            AssignMat(nozzleVis, nozzleMat);

            // WaterStream — child of nozzle, identity rotation → 분사 방향 = nozzle.forward.
            var waterGo = new GameObject("WaterStream");
            waterGo.transform.SetParent(nozzle.transform, false);
            waterGo.transform.localPosition = Vector3.zero;
            waterGo.transform.localRotation = Quaternion.identity;
            var waterPs = waterGo.AddComponent<ParticleSystem>();
            ConfigureWaterStream(waterPs);

            var hoseComp = hoseGo.AddComponent<FirefightHose>();
            hoseComp.Nozzle = nozzle.transform;
            hoseComp.WaterStream = waterPs;
            hoseComp.Controller = ctrl;
            hoseComp.FireMask = ~0;
            hoseComp.MaxRange = 5f;
            hoseComp.DamagePerSecond = 0.5f;
            hoseComp.HitRadius = 0.08f;
            hoseComp.MaxEmissionRate = 150f;
            hoseComp.MaxStartSpeed = 10f;
            hoseComp.MaxStartSize = 0.06f;

            // ===== Fires =====
            var firesParent = new GameObject("Fires");
            firesParent.transform.SetParent(room.transform, false);
            firesParent.transform.localPosition = Vector3.zero;

            var fireComps = new FirefightFire[FirePositions.Length];
            for (int i = 0; i < FirePositions.Length; i++)
            {
                var fGo = new GameObject($"Fire_{i}");
                fGo.transform.SetParent(firesParent.transform, false);
                fGo.transform.localPosition = FirePositions[i];

                // 콜라이더 — raycast 표적.
                var fCol = fGo.AddComponent<SphereCollider>();
                fCol.radius = FireColliderRadius;
                fCol.isTrigger = true;

                // Base 시각 — 작은 검게 탄 큐브.
                var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseGo.name = "Base";
                Object.DestroyImmediate(baseGo.GetComponent<Collider>());
                baseGo.transform.SetParent(fGo.transform, false);
                baseGo.transform.localPosition = new Vector3(0f, -0.10f, 0f);
                baseGo.transform.localScale = new Vector3(0.22f, 0.06f, 0.22f);
                AssignMat(baseGo, fireBaseMat);

                // 불꽃 ParticleSystem.
                var flamesGo = new GameObject("Flames");
                flamesGo.transform.SetParent(fGo.transform, false);
                flamesGo.transform.localPosition = Vector3.zero;
                var flamesPs = flamesGo.AddComponent<ParticleSystem>();
                ConfigureFire(flamesPs);

                var ff = fGo.AddComponent<FirefightFire>();
                ff.FireParticles = flamesPs;
                ff.StartStrength = 0.15f;
                ff.GrowthRate = 0.04f;   // 더 천천히 자라 — 테스트 중 폭주 방지
                ff.MaxStrength = 1.0f;
                // 위협적 톤 — 메인 불꽃을 더 크고 강하게.
                ff.MaxEmissionRate = 60f;
                ff.MinStartSize = 0.20f;
                ff.MaxStartSize = 1.10f;
                ff.AutoEnhanceFlames = true;
                ff.TurbulenceStrength = 0.8f;
                ff.MaxLightIntensity = 4.5f;
                ff.LightRange = 5f;
                ff.LightFlickerSpeed = 18f;
                ff.MaxEmberRate = 70f;

                fireComps[i] = ff;
            }

            // ===== Controller wire-up =====
            ctrl.Pump = pumpComp;
            ctrl.Hose = hoseComp;
            ctrl.Fires = fireComps;
            ctrl.MaxPressure = 1f;
            ctrl.PressureDecayRate = 0.15f;

            EditorUtility.SetDirty(room);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(room.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[Firefight] Build 완료. 펌프(좌) / 호스(우) / 불 3개(앞) / 압력 게이지 확인.");
        }

        // ---- ParticleSystem 셋업 ----

        static void ConfigureWaterStream(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 1.5f;
            main.loop = true;
            main.startLifetime = 0.45f;
            main.startSpeed = 10f;         // 런타임에 hose 가 pressure 비례 덮어씀
            main.startSize = 0.035f;       // 작은 입자 — Trails 가 모션 스트릭 담당
            main.startColor = new Color(0.95f, 0.98f, 1f, 1f);
            main.maxParticles = 1500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.gravityModifier = 0f;     // 직선 — 시각과 SphereCast 라인 일치

            var emission = ps.emission;
            emission.rateOverTime = 0f;    // Hose 가 매 프레임 갱신

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 1.5f;
            shape.radius = 0.003f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 1f, 1f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.8f, 1f), 0.6f),
                    new GradientColorKey(new Color(0.3f, 0.55f, 0.9f), 1f),
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = grad;

            // Trails 모듈 — 각 입자 뒤로 짧은 streak 만 그려서 단방향 강한 수압 효과.
            // Stretched Billboard 와 달리 입자 위치 기준 양방향이 아닌 "이전 경로" 만 그림.
            var trails = ps.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.08f);
            trails.minVertexDistance = 0.02f;
            trails.worldSpace = true;
            trails.dieWithParticles = true;
            trails.sizeAffectsWidth = true;
            trails.sizeAffectsLifetime = false;
            trails.inheritParticleColor = true;
            var widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0f));
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, widthCurve);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // Billboard (Stretch 아님) — 입자 자체는 작은 droplet, 모션 streak 는 Trails 가 담당.
                renderer.renderMode = ParticleSystemRenderMode.Billboard;

                var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
                if (mat != null)
                {
                    renderer.sharedMaterial = mat;
                    renderer.trailMaterial = mat;
                }
            }
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static void ConfigureFire(ParticleSystem ps)
        {
            // 메인 불꽃 — 흰열 코어가 도는 위협적 화염.
            // 색 그라데이션 / 난기류 / main 모듈은 FirefightFire.EnhanceFlameVisuals() 가 런타임에 다시 덮어쓰지만,
            // 여기서도 동일한 톤으로 셋업해서 에디터 미리보기에서 즉시 위협적으로 보이도록 한다.
            var main = ps.main;
            main.duration = 2.0f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = 0.5f; // 런타임에 strength 비례로 덮어씀
            main.startColor = new Color(1f, 0.55f, 0.15f, 1f);
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;
            main.gravityModifier = -0.35f; // 위로 솟음 — 너무 빠르지 않게 (발원지에 머무는 시간 ↑)

            var emission = ps.emission;
            emission.rateOverTime = 35f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.10f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // 위쪽 방향

            // 색: 흰열 코어 → 진한 적색 → 그을음 (위협적 톤)
            // 시작 알파가 0 이면 발원지 근처가 비어 보임 → 0.4 로 시작.
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1.4f, 1.1f, 0.55f), 0f),
                    new GradientColorKey(new Color(1.0f, 0.45f, 0.08f), 0.35f),
                    new GradientColorKey(new Color(0.7f, 0.12f, 0.03f), 0.75f),
                    new GradientColorKey(new Color(0.12f, 0.04f, 0.02f), 1f),
                },
                new[] {
                    new GradientAlphaKey(0.4f, 0f),
                    new GradientAlphaKey(1.0f, 0.2f),
                    new GradientAlphaKey(0.9f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.5f);
            sizeCurve.AddKey(0.4f, 1.0f);
            sizeCurve.AddKey(1f, 0.2f);
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // 난기류 — 불꽃이 흔들리며 위협감 ↑
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 1.2f;
            noise.scrollSpeed = 1.5f;
            noise.damping = true;
            noise.octaveCount = 2;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
                if (mat != null) renderer.sharedMaterial = mat;
            }
        }

        // ---- Network grabbable ----

        /// <summary>
        /// Shared 모드에서 잡고 움직일 때 위치/회전이 상대에게 동기화되도록 표준 4종 부착:
        ///   NetworkObject(AllowStateAuthorityOverride + AreaOfInterest) + NetworkTransform
        ///   + GrabAuthorityHandover + GrabNetworkSyncPause.
        /// (Tools/Network Interactable Setup 및 Grab Authority Setup 툴과 동일한 구성.)
        /// </summary>
        static void SetupNetworkGrabbable(GameObject go)
        {
            var no = go.GetComponent<NetworkObject>();
            if (no == null) no = go.AddComponent<NetworkObject>();

            var noSO = new SerializedObject(no);
            var interest = noSO.FindProperty("ObjectInterest");
            if (interest != null) interest.intValue = 1; // Area Of Interest
            var flags = noSO.FindProperty("Flags");
            if (flags != null) flags.intValue |= (int)NetworkObjectFlags.AllowStateAuthorityOverride;
            noSO.ApplyModifiedProperties();

            if (go.GetComponent<NetworkTransform>() == null) go.AddComponent<NetworkTransform>();
            if (go.GetComponent<GrabAuthorityHandover>() == null) go.AddComponent<GrabAuthorityHandover>();
            if (go.GetComponent<Stage1.GrabNetworkSyncPause>() == null) go.AddComponent<Stage1.GrabNetworkSyncPause>();
        }

        // ---- Material / utility ----

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
