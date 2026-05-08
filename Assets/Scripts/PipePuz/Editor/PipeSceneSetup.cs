using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.EditorTools
{
    /// <summary>
    /// Pipe Scene 의 RadiatorA / RadiatorB 안에 벽·파이프·밸브·소켓·연기를
    /// 좌우 대칭으로 자동 생성해 주는 에디터 메뉴.
    ///
    /// 사용법: Unity 메뉴 > Tools > PipePuz > Build Pipe Scene
    /// 다시 누르면 RadiatorA / RadiatorB 안의 자식을 모두 지우고 새로 만든다.
    /// 동시에 Radiator 의 Plane 을 투명·콜라이더 비활성으로 정리한다.
    /// </summary>
    public static class PipeSceneSetup
    {
        // RadiatorA 기준 X 좌표. RadiatorB 는 부호 반전.
        static readonly float[] PipeXs = new float[] { -0.6f, -0.2f, 0.2f, 0.6f };
        const int ValveIdx = 1;
        const int BrokeIdx = 2;

        const float WallY = 1f;
        const float WallZ = -0.5f;
        const float WallW = 1.6f;
        const float WallH = 2.0f;
        const float WallT = 0.1f;

        const float PipeY = 1f;
        const float PipeZ = -0.42f;
        const float PipeRadius = 0.06f;
        const float PipeHalfHeight = 1.0f; // Cylinder 프리미티브 height = 2

        const float ValveZ = -0.25f;       // 밸브 본체가 파이프 앞쪽으로 나와있는 위치
        const float ValveStemLen = 0.18f;
        const float WheelRadius = 0.25f;   // 가장자리를 잡고 크게 돌릴 수 있도록 큼직하게
        const float DiscThickness = 0.04f;
        const float SpokeThickness = 0.025f;
        const float HubSize = 0.07f;
        const float RimGrabRadius = 0.06f; // 림 sphere 콜라이더 반경
        const int RimGrabCount = 8;        // 림에 배치되는 grab 콜라이더 개수

        // LightBall
        const float LightBallVisualScale = 0.18f; // 시각용 sphere 지름 0.18m
        const float LightBallColliderRadius = 0.12f;
        const float LightBallLocalX = 3f;         // LightBall 로컬에서 ±X 로 떨어진 위치
        const float LightBallLocalY = 1.5f;
        const float LightBallLocalZ = 0f;
        const float LightBallRange = 5f;
        const float LightBallIntensity = 4.5f;

        [MenuItem("Tools/PipePuz/Build Pipe Scene")]
        public static void Build()
        {
            var radiator = GameObject.Find("Radiator");
            if (radiator == null)
            {
                EditorUtility.DisplayDialog("PipePuz",
                    "현재 씬에서 'Radiator' 오브젝트를 찾을 수 없습니다.\nPipe Scene 을 열고 다시 시도하세요.", "OK");
                return;
            }

            var radA = radiator.transform.Find("RadiatorA");
            var radB = radiator.transform.Find("RadiatorB");
            if (radA == null || radB == null)
            {
                EditorUtility.DisplayDialog("PipePuz",
                    "Radiator 안에 RadiatorA/RadiatorB 가 모두 있어야 합니다.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Pipe Scene");

            ClearChildren(radA);
            ClearChildren(radB);

            // 머티리얼
            var pipeMat = MakeUrpMaterial("PipeMat", new Color(0.72f, 0.72f, 0.78f, 1f), false);
            var brokeMat = MakeUrpMaterial("PipeBrokeMat", new Color(1f, 0.45f, 0.05f, 1f), false);
            var wallMat = MakeUrpMaterial("WallMat", new Color(0.55f, 0.55f, 0.55f, 1f), false);
            var valveMat = MakeUrpMaterial("ValveMat", new Color(0.35f, 0.35f, 0.4f, 1f), false);
            var socketMat = MakeUrpMaterial("PipeSocketMat", new Color(1f, 1f, 1f, 0.22f), true);
            var planeInvisibleMat = MakeUrpMaterial("PlaneInvisibleMat", new Color(1f, 1f, 1f, 0f), true);

            var resA = BuildRadiator(radA, mirrorX: false, includeBrokeAndSocket: false,
                pipeMat, wallMat, valveMat, brokeMat, socketMat);
            var resB = BuildRadiator(radB, mirrorX: true, includeBrokeAndSocket: true,
                pipeMat, wallMat, valveMat, brokeMat, socketMat);

            // 밸브 페어링 — 두 밸브 모두 LocalAxis=forward, InvertDirection=false 로 통일.
            // (양쪽 플레이어 모두 자기 시점에서 +Z 방향으로 휠 노멀이 나오므로 같은 부호 규약을 쓰면 된다.)
            if (resA.valve != null && resB.valve != null)
            {
                resA.valve.PairedValve = resB.valve;
                resB.valve.PairedValve = resA.valve;
            }

            // PipeSocket 의 EligibleSockets 와 InitialPipe 연결
            if (resB.socket != null)
            {
                if (resB.broke != null)
                {
                    resB.broke.EligibleSockets = new[] { resB.socket };
                    resB.socket.InitialPipe = resB.broke;
                }
                if (resB.newPipe != null)
                {
                    resB.newPipe.EligibleSockets = new[] { resB.socket };
                }
            }

            // 중앙 Plane 정리
            MakePlaneNonInteractive(radiator.transform, planeInvisibleMat);

            // LightBallA / LightBallB
            var plane = radiator.transform.Find("Plane");
            BuildLightBalls(plane);

            EditorUtility.SetDirty(radA.gameObject);
            EditorUtility.SetDirty(radB.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(radA.gameObject.scene);

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[PipePuz] Pipe Scene 자동 생성 완료. RadiatorA / RadiatorB · Plane · LightBallA/B 를 확인하세요.");
        }

        [MenuItem("Tools/PipePuz/Build Light Balls Only")]
        public static void BuildLightBallsOnly()
        {
            var radiator = GameObject.Find("Radiator");
            if (radiator == null)
            {
                EditorUtility.DisplayDialog("PipePuz", "Radiator 오브젝트가 필요합니다.", "OK");
                return;
            }
            var plane = radiator.transform.Find("Plane");
            if (plane == null)
            {
                EditorUtility.DisplayDialog("PipePuz", "Radiator 안에 Plane 이 필요합니다.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Light Balls");

            BuildLightBalls(plane);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(plane.gameObject.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[PipePuz] LightBallA / LightBallB 빌드 완료.");
        }

        // --------------------------------------------------------------------
        // Radiator build
        // --------------------------------------------------------------------

        struct BuildResult
        {
            public Valve valve;
            public PipeSocket socket;
            public PipeGrabbable broke;
            public PipeGrabbable newPipe;
        }

        static BuildResult BuildRadiator(Transform parent, bool mirrorX, bool includeBrokeAndSocket,
            Material pipeMat, Material wallMat, Material valveMat, Material brokeMat, Material socketMat)
        {
            var res = new BuildResult();
            float xs = mirrorX ? -1f : 1f;

            // 벽
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = new Vector3(0f, WallY, WallZ);
            wall.transform.localScale = new Vector3(WallW, WallH, WallT);
            AssignMat(wall, wallMat);

            var radCtrl = parent.gameObject.GetComponent<RadiatorController>();
            if (radCtrl == null) radCtrl = parent.gameObject.AddComponent<RadiatorController>();

            // 4 개의 파이프
            for (int i = 0; i < PipeXs.Length; i++)
            {
                float x = PipeXs[i] * xs;
                bool isBrokeSlot = includeBrokeAndSocket && i == BrokeIdx;
                if (isBrokeSlot)
                {
                    var socketGo = new GameObject("PipeSocket");
                    socketGo.transform.SetParent(parent, false);
                    socketGo.transform.localPosition = new Vector3(x, PipeY, PipeZ);
                    var trigger = socketGo.AddComponent<BoxCollider>();
                    trigger.isTrigger = true;
                    trigger.size = new Vector3(2f * PipeRadius * 1.6f, 2f * PipeHalfHeight, 2f * PipeRadius * 1.6f);
                    var socket = socketGo.AddComponent<PipeSocket>();
                    socket.SnapRadius = 0.25f;
                    socket.Radiator = radCtrl;
                    res.socket = socket;

                    var ghost = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    ghost.name = "SocketGhost";
                    Object.DestroyImmediate(ghost.GetComponent<Collider>());
                    ghost.transform.SetParent(socketGo.transform, false);
                    ghost.transform.localPosition = Vector3.zero;
                    ghost.transform.localScale = new Vector3(2f * PipeRadius * 1.05f, PipeHalfHeight, 2f * PipeRadius * 1.05f);
                    AssignMat(ghost, socketMat);

                    res.broke = CreateGrabbablePipe(parent, "Pipe_Broke", brokeMat, PipeKind.Broke);
                    res.broke.transform.SetParent(socketGo.transform, false);
                    res.broke.transform.localPosition = Vector3.zero;
                    res.broke.transform.localRotation = Quaternion.identity;

                    res.newPipe = CreateGrabbablePipe(parent, "Pipe_New", pipeMat, PipeKind.New);
                    res.newPipe.transform.SetParent(parent, false);
                    res.newPipe.transform.localPosition = new Vector3(x + 0.55f * xs, 0.4f, PipeZ + 0.45f);
                    res.newPipe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
                else
                {
                    var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pipe.name = $"Pipe_{i + 1}";
                    pipe.transform.SetParent(parent, false);
                    pipe.transform.localPosition = new Vector3(x, PipeY, PipeZ);
                    pipe.transform.localScale = new Vector3(2f * PipeRadius, PipeHalfHeight, 2f * PipeRadius);
                    AssignMat(pipe, pipeMat);
                    Object.DestroyImmediate(pipe.GetComponent<Collider>());
                }

                if (i == ValveIdx)
                {
                    res.valve = BuildValve(parent, x, valveMat);
                }
            }

            if (includeBrokeAndSocket)
            {
                var smokeGo = new GameObject("Smoke");
                smokeGo.transform.SetParent(parent, false);
                float bx = PipeXs[BrokeIdx] * xs;
                smokeGo.transform.localPosition = new Vector3(bx, PipeY, PipeZ);
                var ps = smokeGo.AddComponent<ParticleSystem>();
                ConfigureSmokeParticleSystem(ps);
                var ctrl = smokeGo.AddComponent<SmokeController>();
                radCtrl.Smoke = ctrl;
            }

            radCtrl.Valve = res.valve;
            radCtrl.Socket = res.socket;

            return res;
        }

        // --------------------------------------------------------------------
        // Valve build — 가장자리 잡기 가능한 큰 휠 형태
        // --------------------------------------------------------------------

        static Valve BuildValve(Transform parent, float x, Material valveMat)
        {
            var valveGo = new GameObject("Valve");
            valveGo.transform.SetParent(parent, false);
            valveGo.transform.localPosition = new Vector3(x, PipeY, ValveZ);

            // 1) Stem — 파이프와 휠을 잇는 짧은 막대(시각용).
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stem.name = "Stem";
            Object.DestroyImmediate(stem.GetComponent<Collider>());
            stem.transform.SetParent(valveGo.transform, false);
            stem.transform.localPosition = new Vector3(0f, 0f, -ValveStemLen * 0.5f);
            stem.transform.localScale = new Vector3(0.04f, 0.04f, ValveStemLen);
            AssignMat(stem, valveMat);

            // 2) Wheel — 회전하는 부분. 자체엔 회전 없음(=identity), 자식들이 함께 돌아간다.
            var wheelGo = new GameObject("Wheel");
            wheelGo.transform.SetParent(valveGo.transform, false);

            // 2-a) Hub — 휠 중앙의 작은 구.
            var hub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hub.name = "Hub";
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.transform.SetParent(wheelGo.transform, false);
            hub.transform.localPosition = Vector3.zero;
            hub.transform.localScale = Vector3.one * HubSize;
            AssignMat(hub, valveMat);

            // 2-b) Disc — 림이 잘 보이도록 얇은 디스크 시각.
            //      Cylinder 프리미티브의 long-axis 를 Z 로 눕히기 위해 X 축으로 90° 회전.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(wheelGo.transform, false);
            disc.transform.localPosition = Vector3.zero;
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(2f * WheelRadius * 0.95f, DiscThickness, 2f * WheelRadius * 0.95f);
            AssignMat(disc, valveMat);

            // 2-c) Spokes — 4 개. 림과 허브를 잇는 막대.
            for (int i = 0; i < 4; i++)
            {
                float a = (i / 4f) * Mathf.PI * 2f;
                var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spoke.name = $"Spoke_{i}";
                Object.DestroyImmediate(spoke.GetComponent<Collider>());
                spoke.transform.SetParent(wheelGo.transform, false);
                // 디스크 평면(=XY)에서 중앙 → 림 방향으로 절반 위치에 배치.
                spoke.transform.localPosition = new Vector3(Mathf.Cos(a) * WheelRadius * 0.5f, Mathf.Sin(a) * WheelRadius * 0.5f, 0f);
                // 큐브의 +X 가 길쭉한 방향이 되도록 Z 축 회전.
                spoke.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
                spoke.transform.localScale = new Vector3(WheelRadius, SpokeThickness, SpokeThickness);
                AssignMat(spoke, valveMat);
            }

            // 2-d) Rim — 림 둘레 8 개의 grab 콜라이더(휠과 함께 회전).
            var rimColliders = new List<Collider>();
            for (int i = 0; i < RimGrabCount; i++)
            {
                float a = (i / (float)RimGrabCount) * Mathf.PI * 2f;
                Vector3 pos = new Vector3(Mathf.Cos(a) * WheelRadius, Mathf.Sin(a) * WheelRadius, 0f);

                // 시각용 작은 림 표시.
                var nub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nub.name = $"RimNub_{i}";
                Object.DestroyImmediate(nub.GetComponent<Collider>());
                nub.transform.SetParent(wheelGo.transform, false);
                nub.transform.localPosition = pos;
                nub.transform.localScale = Vector3.one * (RimGrabRadius * 0.9f);
                AssignMat(nub, valveMat);

                // 잡기용 콜라이더(별도 빈 GO).
                var rimGrab = new GameObject($"RimGrab_{i}");
                rimGrab.transform.SetParent(wheelGo.transform, false);
                rimGrab.transform.localPosition = pos;
                var sc = rimGrab.AddComponent<SphereCollider>();
                sc.radius = RimGrabRadius;
                rimColliders.Add(sc);
            }

            // 3) Valve 컴포넌트 — Wheel 을 회전시키고 림 콜라이더로 grab 을 받는다.
            var v = valveGo.AddComponent<Valve>();
            v.LocalAxis = Vector3.forward; // +Z (휠 노멀 방향)
            v.MaxAngle = 720f;
            v.Wheel = wheelGo.transform;
            v.Openness = 1f;
            v.InvertDirection = false;
            // 가장자리에서만 잡히게 — 중앙(허브) 영역의 select 를 차단.
            v.MinGrabRadius = WheelRadius * 0.65f;
            v.MaxGrabRadius = WheelRadius * 1.6f;

            // XRBaseInteractable 의 colliders 리스트에 림 grab 콜라이더 등록.
            // 비어 있으면 자동 검색이지만, 다른 자식 콜라이더가 섞이지 않도록 명시.
            v.colliders.Clear();
            foreach (var c in rimColliders) v.colliders.Add(c);

            return v;
        }

        // --------------------------------------------------------------------
        // Pipe / Smoke
        // --------------------------------------------------------------------

        static PipeGrabbable CreateGrabbablePipe(Transform parent, string name, Material mat, PipeKind kind)
        {
            var go = new GameObject(name);
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            vis.name = "Visual";
            Object.DestroyImmediate(vis.GetComponent<Collider>());
            vis.transform.SetParent(go.transform, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;
            vis.transform.localScale = new Vector3(2f * PipeRadius, PipeHalfHeight, 2f * PipeRadius);
            AssignMat(vis, mat);

            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 1; // Y axis
            col.radius = PipeRadius * 1.05f;
            col.height = PipeHalfHeight * 2f;

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            go.AddComponent<XRGrabInteractable>();
            var pg = go.AddComponent<PipeGrabbable>();
            pg.Kind = kind;
            return pg;
        }

        static void ConfigureSmokeParticleSystem(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = 4.0f;       // 더 오래 머물러서 진해 보이게
            main.startSpeed = 0.5f;
            main.startSize = 1.2f;
            main.startColor = new Color(0.65f, 0.65f, 0.65f, 0.95f); // 거의 불투명에 가깝게
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;        // 입자 수 한도 상향
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;     // SmokeController 가 제어

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
                    new GradientAlphaKey(0.95f, 0.1f),  // 빠르게 거의 불투명까지 올림
                    new GradientAlphaKey(0.9f, 0.6f),   // 중반까지 짙게 유지
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.6f);
            sizeCurve.AddKey(0.5f, 1.5f);
            sizeCurve.AddKey(1f, 2.2f);   // 끝까지 점점 부풀어 올라가 시야를 가린다
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
        // Plane 처리
        // --------------------------------------------------------------------

        static void MakePlaneNonInteractive(Transform radiatorRoot, Material invisibleMat)
        {
            var plane = radiatorRoot.Find("Plane");
            if (plane == null)
            {
                Debug.LogWarning("[PipePuz] Radiator 안에 Plane 이 없어 Plane 처리를 건너뜁니다.");
                return;
            }

            // 자식 콜라이더 포함 모두 비활성화 — 손이 통과하게.
            var colliders = plane.GetComponentsInChildren<Collider>(true);
            foreach (var c in colliders)
            {
                Undo.RecordObject(c, "Disable Plane Collider");
                c.enabled = false;
            }

            // 시각도 투명화. Renderer 자체를 끄지 않고, 알파 0 으로 보이지 않게 하되 기즈모로는 위치 인지 가능.
            var renderers = plane.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                Undo.RecordObject(r, "Make Plane Transparent");
                r.sharedMaterial = invisibleMat;
            }
        }

        // --------------------------------------------------------------------
        // LightBall A / B
        // --------------------------------------------------------------------

        static void BuildLightBalls(Transform planeRef)
        {
            var lightBallRoot = GameObject.Find("LightBall");
            if (lightBallRoot == null)
            {
                Debug.LogWarning("[PipePuz] 씬에서 'LightBall' 오브젝트를 찾을 수 없어 LightBall 생성을 건너뜁니다.");
                return;
            }

            // 기존 자식 LightBallA/LightBallB 가 있으면 제거(이름이 겹칠 때만 — LightBall 의 다른 자식은 보존).
            var oldA = lightBallRoot.transform.Find("LightBallA");
            if (oldA != null) Undo.DestroyObjectImmediate(oldA.gameObject);
            var oldB = lightBallRoot.transform.Find("LightBallB");
            if (oldB != null) Undo.DestroyObjectImmediate(oldB.gameObject);

            // 발광 머티리얼
            var ballMat = MakeEmissiveMaterial(
                "LightBallMat",
                baseColor: new Color(1f, 0.92f, 0.65f, 1f),
                emissionColor: new Color(2.5f, 2.2f, 1.4f) * 1.4f);

            // LightBallA — 비상호작용
            var lA = CreateLightBall(
                name: "LightBallA",
                parent: lightBallRoot.transform,
                localPos: new Vector3(-LightBallLocalX, LightBallLocalY, LightBallLocalZ),
                ballMat: ballMat,
                interactable: false);

            // LightBallB — 잡기 가능
            var lB = CreateLightBall(
                name: "LightBallB",
                parent: lightBallRoot.transform,
                localPos: new Vector3(LightBallLocalX, LightBallLocalY, LightBallLocalZ),
                ballMat: ballMat,
                interactable: true);

            // 미러 동기화
            var mirror = lB.AddComponent<LightBallMirror>();
            mirror.Plane = planeRef;
            mirror.Mirror = lA.transform;
            // Plane 은 Z 90° 회전된 Plane 프리미티브이므로 world 기준 법선이 곧 Plane.up.
            mirror.NormalAxis = LightBallMirror.PlaneAxis.Up;
            mirror.MirrorRotation = false;

            EditorUtility.SetDirty(lightBallRoot);
        }

        static GameObject CreateLightBall(string name, Transform parent, Vector3 localPos, Material ballMat, bool interactable)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // 시각: sphere mesh.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * LightBallVisualScale;
            AssignMat(visual, ballMat);

            // 빛: Point Light.
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = Vector3.zero;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = LightBallRange;
            light.intensity = LightBallIntensity;
            light.color = new Color(1f, 0.93f, 0.75f);
            light.shadows = LightShadows.Soft;

            // 실체용 콜라이더 (양쪽 공통).
            var col = go.AddComponent<SphereCollider>();
            col.radius = LightBallColliderRadius;
            col.isTrigger = false;

            if (interactable)
            {
                // B 만 잡기 가능. Rigidbody 는 kinematic — 손이 놓아도 그 자리에 머문다.
                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                go.AddComponent<XRGrabInteractable>();
            }

            return go;
        }

        // --------------------------------------------------------------------
        // Material / utility
        // --------------------------------------------------------------------

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
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return m;
        }

        static void AssignMat(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }

        static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                Undo.DestroyObjectImmediate(child);
            }
            var rc = t.GetComponent<RadiatorController>();
            if (rc != null) Undo.DestroyObjectImmediate(rc);
        }
    }
}
