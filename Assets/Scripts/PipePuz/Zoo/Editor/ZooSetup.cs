using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.Zoo.EditorTools
{
    /// <summary>
    /// Pipe Scene 의 Zoo 컨테이너 안에 4 생명체·도구·케이지·hole·컨트롤러를 자동 구성한다.
    /// Zoo/Dragonfly, Zoo/Lizard, Zoo/Crab, Zoo/Snake 4 개의 빈 GameObject 는 이미
    /// 씬에 있다고 가정한다. 이 함수는 각 빈 컨테이너의 자식을 정리 후 새로 생성한다.
    ///
    /// 사용법: 메뉴 Tools/Zoo/Build Zoo
    /// 다시 누르면 모든 산출물을 깔끔하게 다시 만든다.
    /// </summary>
    public static class ZooSetup
    {
        // ===== 배치 (Zoo 로컬 기준) =====
        // Pipe Scene 의 Zoo 컨테이너 안에서 +X, +Z 방향으로 자유롭게 놓을 수 있다.

        // 생명체 시작 위치
        static readonly Vector3 DragonflyStart = new Vector3(-1.0f, 1.5f, 1.5f);
        static readonly Vector3 LizardStart    = new Vector3( 0.0f, 0.05f, 1.0f);
        static readonly Vector3 CrabStart      = new Vector3( 1.0f, 0.10f, 0.5f);
        static readonly Vector3 SnakeStart     = new Vector3(-0.5f, 0.05f, 0.5f);

        // 케이지 배치 — 한 줄로 4 개, 뒤쪽 벽에서 z 방향으로 약간 떨어진 위치
        static readonly Vector3[] CagePositions = new[]
        {
            new Vector3(-1.5f, 0.3f, -2.0f),
            new Vector3(-0.5f, 0.3f, -2.0f),
            new Vector3( 0.5f, 0.3f, -2.0f),
            new Vector3( 1.5f, 0.3f, -2.0f),
        };
        static readonly Color[] CageColors = new[]
        {
            new Color(1.0f, 0.25f, 0.25f, 0.45f), // Red
            new Color(0.25f, 0.45f, 1.0f, 0.45f), // Blue
            new Color(0.30f, 0.85f, 0.30f, 0.45f),// Green
            new Color(1.0f, 0.95f, 0.25f, 0.45f), // Yellow
        };
        static readonly CageId[] CageIds = new[]
        {
            CageId.Red, CageId.Blue, CageId.Green, CageId.Yellow
        };
        // 1차 테스트 정답 매핑 (인스펙터에서 변경 가능)
        static readonly CreatureKind[] CageAccepts = new[]
        {
            CreatureKind.Dragonfly,
            CreatureKind.Lizard,
            CreatureKind.Crab,
            CreatureKind.Snake,
        };

        // LizardEscapeHole (게로 막아 도마뱀의 도주를 둔화시킬 위치)
        static readonly Vector3 HolePos  = new Vector3(0.5f, 0.05f, 0.0f);
        static readonly Vector3 HoleSize = new Vector3(0.5f, 0.2f, 0.5f);

        // 도구: 잠자리채, 장갑
        static readonly Vector3 CatchNetPos = new Vector3(-2.0f, 1.0f, -0.5f);
        static readonly Vector3 GlovesPos   = new Vector3(-2.5f, 1.0f, -0.5f);

        // ===== 메뉴 =====
        [MenuItem("Tools/Zoo/Build Zoo")]
        public static void Build()
        {
            var zoo = GameObject.Find("Zoo");
            if (zoo == null)
            {
                EditorUtility.DisplayDialog("Zoo",
                    "현재 씬에서 'Zoo' 오브젝트를 찾을 수 없습니다.\nPipe Scene 을 열고 다시 시도하세요.", "OK");
                return;
            }

            // 4 빈 컨테이너 확인
            var dragonflyT = zoo.transform.Find("Dragonfly");
            var lizardT    = zoo.transform.Find("Lizard");
            var crabT      = zoo.transform.Find("Crab");
            var snakeT     = zoo.transform.Find("Snake");
            if (dragonflyT == null || lizardT == null || crabT == null || snakeT == null)
            {
                EditorUtility.DisplayDialog("Zoo",
                    "Zoo 안에 Dragonfly/Lizard/Crab/Snake 4 개의 자식 GameObject 가 모두 있어야 합니다.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Zoo");

            // 4 컨테이너 청소
            ClearChildren(dragonflyT);
            ClearChildren(lizardT);
            ClearChildren(crabT);
            ClearChildren(snakeT);
            // 이전 setup 잔존물 정리
            DestroyChildIfExists(zoo.transform, "Cages");
            DestroyChildIfExists(zoo.transform, "LizardEscapeHole");
            DestroyChildIfExists(zoo.transform, "CatchNet");
            DestroyChildIfExists(zoo.transform, "Gloves");
            DestroyChildIfExists(zoo.transform, "ShockEmitter");
            var oldCtrl = zoo.GetComponent<ZooPuzzleController>();
            if (oldCtrl != null) Undo.DestroyObjectImmediate(oldCtrl);
            // 기존 컨테이너 자체의 컴포넌트도 비움(이전에 붙였을 수 있는 ZooCreature 등)
            StripCreatureScripts(dragonflyT.gameObject);
            StripCreatureScripts(lizardT.gameObject);
            StripCreatureScripts(crabT.gameObject);
            StripCreatureScripts(snakeT.gameObject);

            // 머티리얼
            var dragonflyMat = MakeUrpMaterial("Zoo_DragonflyMat", new Color(0.30f, 0.85f, 1.00f), false);
            var lizardMat    = MakeUrpMaterial("Zoo_LizardMat",    new Color(0.30f, 0.65f, 0.30f), false);
            var crabBodyMat  = MakeUrpMaterial("Zoo_CrabBodyMat",  new Color(0.90f, 0.45f, 0.20f), false);
            var crabShellMat = MakeUrpMaterial("Zoo_CrabShellMat", new Color(0.50f, 0.40f, 0.30f), false);
            var snakeMat     = MakeUrpMaterial("Zoo_SnakeMat",     new Color(0.95f, 0.85f, 0.20f), false);

            var catchNetPoleMat = MakeUrpMaterial("Zoo_NetPoleMat", new Color(0.35f, 0.25f, 0.15f), false);
            var catchNetRingMat = MakeUrpMaterial("Zoo_NetRingMat", new Color(0.80f, 0.80f, 0.80f), false);
            var gloveMat        = MakeUrpMaterial("Zoo_GloveMat",   new Color(0.15f, 0.15f, 0.20f), false);

            // 컨트롤러
            var ctrl = Undo.AddComponent<ZooPuzzleController>(zoo);

            // ===== VFX =====
            var shock = BuildShockEmitter(zoo.transform);

            // ===== 생명체 4 종 =====
            BuildDragonfly(dragonflyT, ctrl, dragonflyMat);
            var hole = BuildLizardEscapeHole(zoo.transform);
            BuildLizard(lizardT, ctrl, lizardMat, hole);
            BuildCrab(crabT, ctrl, crabBodyMat, crabShellMat);
            BuildSnake(snakeT, ctrl, snakeMat, shock);

            // ===== 케이지 =====
            BuildCages(zoo.transform, ctrl);

            // ===== 도구 =====
            BuildCatchNet(zoo.transform, catchNetPoleMat, catchNetRingMat);
            BuildGloves(zoo.transform, gloveMat);

            EditorUtility.SetDirty(zoo);
            Undo.CollapseUndoOperations(undoGroup);

            EditorUtility.DisplayDialog("Zoo",
                "Zoo 자동 구성 완료.\n\n" +
                "1) 잠자리채(CatchNet) 를 손으로 잡아 잠자리 헤드 트리거로 잡는다.\n" +
                "2) 손 GameObject 에 HandInsulation + 트리거 자식에 HandCreatureProbe 를 직접 부착해야 도마뱀/뱀 캡처가 작동한다.\n" +
                "3) 장갑(Gloves) 을 손에 자식으로 두면(또는 forceInsulated 토글) 뱀 캡처 허용.\n" +
                "4) 게 는 손으로 강하게 부딪치면 셸 모드 토글. 셸 상태로 LizardEscapeHole 위에 두면 도마뱀이 느려진다.",
                "OK");
        }

        // ===== 생명체 빌더 =====

        static void BuildDragonfly(Transform parent, ZooPuzzleController ctrl, Material mat)
        {
            parent.localPosition = DragonflyStart;
            parent.localRotation = Quaternion.identity;

            // 본체 — 작은 캡슐
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(parent, false);
            body.transform.localScale = new Vector3(0.06f, 0.10f, 0.06f);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GetRenderer(body).sharedMaterial = mat;
            DestroyImmediateSafe(body.GetComponent<Collider>());

            // 좌우 날개 — 얇은 큐브
            for (int s = -1; s <= 1; s += 2)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = s < 0 ? "Wing_L" : "Wing_R";
                w.transform.SetParent(parent, false);
                w.transform.localScale = new Vector3(0.18f, 0.005f, 0.06f);
                w.transform.localPosition = new Vector3(0.10f * s, 0f, 0f);
                GetRenderer(w).sharedMaterial = mat;
                DestroyImmediateSafe(w.GetComponent<Collider>());
            }

            // 캡처 콜라이더(트리거) — 잠자리채가 닿을 영역
            var trigger = parent.gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.15f;

            // Rigidbody (kinematic 으로 둠 — AI 가 transform 으로 이동)
            var rb = parent.gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            var c = parent.gameObject.AddComponent<DragonflyCreature>();
            c.SetKind(CreatureKind.Dragonfly);
            SetController(c, ctrl);
            SetThreatRadius(c, 0.6f);
            SetWanderRadius(c, 1.2f);
            SetMoveSpeed(c, 0.7f);
        }

        static void BuildLizard(Transform parent, ZooPuzzleController ctrl, Material mat, LizardEscapeHole hole)
        {
            parent.localPosition = LizardStart;
            parent.localRotation = Quaternion.identity;

            // 몸통 — 길쭉한 캡슐
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(parent, false);
            body.transform.localScale = new Vector3(0.10f, 0.18f, 0.10f);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GetRenderer(body).sharedMaterial = mat;
            // 콜라이더는 본체에서 제거하고 부모 트리거 사용
            DestroyImmediateSafe(body.GetComponent<Collider>());

            // 꼬리 — 작은 캡슐
            var tail = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            tail.name = "Tail";
            tail.transform.SetParent(parent, false);
            tail.transform.localScale = new Vector3(0.04f, 0.10f, 0.04f);
            tail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            tail.transform.localPosition = new Vector3(0f, 0f, -0.18f);
            GetRenderer(tail).sharedMaterial = mat;
            DestroyImmediateSafe(tail.GetComponent<Collider>());

            // 캡처용 트리거
            var trigger = parent.gameObject.AddComponent<CapsuleCollider>();
            trigger.isTrigger = true;
            trigger.direction = 2; // Z axis
            trigger.height = 0.5f;
            trigger.radius = 0.08f;

            var rb = parent.gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            var c = parent.gameObject.AddComponent<LizardCreature>();
            c.SetKind(CreatureKind.Lizard);
            SetController(c, ctrl);
            SetThreatRadius(c, 0.5f);
            SetWanderRadius(c, 2.0f);
            SetMoveSpeed(c, 0.8f);
            SetPrivate(c, "hole", hole);
        }

        static void BuildCrab(Transform parent, ZooPuzzleController ctrl, Material bodyMat, Material shellMat)
        {
            parent.localPosition = CrabStart;
            parent.localRotation = Quaternion.identity;

            // 평상시 모델 — 넓고 낮은 큐브 + 양쪽 다리
            var normal = new GameObject("NormalModel");
            normal.transform.SetParent(parent, false);
            {
                var bd = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bd.name = "CrabBody";
                bd.transform.SetParent(normal.transform, false);
                bd.transform.localScale = new Vector3(0.28f, 0.10f, 0.20f);
                GetRenderer(bd).sharedMaterial = bodyMat;
                DestroyImmediateSafe(bd.GetComponent<Collider>());

                for (int s = -1; s <= 1; s += 2)
                {
                    var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    leg.name = s < 0 ? "Leg_L" : "Leg_R";
                    leg.transform.SetParent(normal.transform, false);
                    leg.transform.localScale = new Vector3(0.04f, 0.04f, 0.16f);
                    leg.transform.localPosition = new Vector3(0.18f * s, -0.02f, 0f);
                    GetRenderer(leg).sharedMaterial = bodyMat;
                    DestroyImmediateSafe(leg.GetComponent<Collider>());
                }
            }

            // 셸 모델 — 큰 반구 (sphere 의 위쪽 절반처럼 보이도록 sphere 사용 + scale)
            var shell = new GameObject("ShellModel");
            shell.transform.SetParent(parent, false);
            shell.SetActive(false);
            {
                var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dome.name = "ShellDome";
                dome.transform.SetParent(shell.transform, false);
                dome.transform.localScale = new Vector3(0.34f, 0.22f, 0.30f);
                dome.transform.localPosition = new Vector3(0f, 0.0f, 0f);
                GetRenderer(dome).sharedMaterial = shellMat;
                DestroyImmediateSafe(dome.GetComponent<Collider>());
            }

            // 콜라이더(물리 충돌용 — 트리거 아님)
            var box = parent.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.32f, 0.22f, 0.30f);
            box.center = new Vector3(0f, 0.05f, 0f);

            // 무거운 Rigidbody
            var rb = parent.gameObject.AddComponent<Rigidbody>();
            rb.mass = 8f;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 1.0f;
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var c = parent.gameObject.AddComponent<CrabCreature>();
            c.SetKind(CreatureKind.Crab);
            SetController(c, ctrl);
            SetThreatRadius(c, 0.5f);
            SetWanderRadius(c, 0.8f);
            SetMoveSpeed(c, 0.3f);
            SetPrivate(c, "normalModel", normal);
            SetPrivate(c, "shellModel", shell);
        }

        static void BuildSnake(Transform parent, ZooPuzzleController ctrl, Material mat, ParticleSystem shock)
        {
            parent.localPosition = SnakeStart;
            parent.localRotation = Quaternion.identity;

            // 몸통 세그먼트 5 개 — 캡슐들을 일렬로
            float spacing = 0.10f;
            for (int i = 0; i < 5; i++)
            {
                var seg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                seg.name = $"Seg_{i}";
                seg.transform.SetParent(parent, false);
                seg.transform.localScale = new Vector3(0.08f, 0.06f, 0.08f);
                seg.transform.localPosition = new Vector3(0f, 0f, -i * spacing);
                GetRenderer(seg).sharedMaterial = mat;
                DestroyImmediateSafe(seg.GetComponent<Collider>());
            }

            // 캡처 트리거
            var trigger = parent.gameObject.AddComponent<CapsuleCollider>();
            trigger.isTrigger = true;
            trigger.direction = 2;
            trigger.height = 0.55f;
            trigger.radius = 0.08f;
            trigger.center = new Vector3(0f, 0f, -0.20f);

            var rb = parent.gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            var c = parent.gameObject.AddComponent<SnakeCreature>();
            c.SetKind(CreatureKind.Snake);
            SetController(c, ctrl);
            SetThreatRadius(c, 0.4f);
            SetWanderRadius(c, 1.5f);
            SetMoveSpeed(c, 0.5f);
            if (shock != null) SetPrivate(c, "shockEmitter", shock);
        }

        // ===== 보조 오브젝트 =====

        // ===== Shock VFX (DA 의 ConfigureShockParticleSystem 패턴 재사용) =====

        static ParticleSystem BuildShockEmitter(Transform parent)
        {
            var go = new GameObject("ShockEmitter");
            Undo.RegisterCreatedObjectUndo(go, "Create ShockEmitter");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            var ps = go.AddComponent<ParticleSystem>();
            ConfigureShockParticleSystem(ps);
            return ps;
        }

        static void ConfigureShockParticleSystem(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = 0.45f;
            main.startSpeed = 1.8f;
            main.startSize = 0.028f;
            main.startColor = new Color(0.65f, 0.95f, 1.4f, 1f);
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.gravityModifier = 0.4f;

            var emission = ps.emission;
            emission.rateOverTime = 0f; // Emit() 만 사용.

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.04f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(0.75f, 1f, 1.5f), 0f),
                    new GradientColorKey(new Color(0.4f, 0.7f, 1.2f), 0.6f),
                    new GradientColorKey(new Color(0.2f, 0.4f, 0.8f), 1f),
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.6f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 1f);
            sizeCurve.AddKey(1f, 0f);
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
                if (mat != null) renderer.sharedMaterial = mat;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static LizardEscapeHole BuildLizardEscapeHole(Transform parent)
        {
            var go = new GameObject("LizardEscapeHole");
            Undo.RegisterCreatedObjectUndo(go, "Create LizardEscapeHole");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = HolePos;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = HoleSize;

            // 시각 마커 — 얇은 큐브 (반투명)
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Visual";
            marker.transform.SetParent(go.transform, false);
            marker.transform.localScale = new Vector3(HoleSize.x, 0.02f, HoleSize.z);
            marker.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            DestroyImmediateSafe(marker.GetComponent<Collider>());
            var mat = MakeUrpMaterial("Zoo_HoleMat", new Color(0.2f, 0.2f, 0.25f, 0.4f), true);
            GetRenderer(marker).sharedMaterial = mat;

            return go.AddComponent<LizardEscapeHole>();
        }

        static void BuildCages(Transform parent, ZooPuzzleController ctrl)
        {
            var root = new GameObject("Cages");
            Undo.RegisterCreatedObjectUndo(root, "Create Cages");
            root.transform.SetParent(parent, false);

            for (int i = 0; i < 4; i++)
            {
                var cage = new GameObject($"Cage_{CageIds[i]}");
                cage.transform.SetParent(root.transform, false);
                cage.transform.localPosition = CagePositions[i];

                // 트리거 박스
                var trig = cage.AddComponent<BoxCollider>();
                trig.isTrigger = true;
                trig.size = new Vector3(0.5f, 0.5f, 0.5f);

                // 시각 — 색 반투명 큐브
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Visual";
                visual.transform.SetParent(cage.transform, false);
                visual.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                DestroyImmediateSafe(visual.GetComponent<Collider>());
                var m = MakeUrpMaterial($"Zoo_Cage_{CageIds[i]}", CageColors[i], true);
                GetRenderer(visual).sharedMaterial = m;

                // 바닥 — 보다 진한 색 얇은 큐브
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Floor";
                floor.transform.SetParent(cage.transform, false);
                floor.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
                floor.transform.localPosition = new Vector3(0f, -0.25f, 0f);
                DestroyImmediateSafe(floor.GetComponent<Collider>());
                var fm = MakeUrpMaterial($"Zoo_CageFloor_{CageIds[i]}",
                    new Color(CageColors[i].r * 0.5f, CageColors[i].g * 0.5f, CageColors[i].b * 0.5f, 1f), false);
                GetRenderer(floor).sharedMaterial = fm;

                var comp = cage.AddComponent<CreatureCage>();
                SetPrivate(comp, "id", CageIds[i]);
                SetPrivate(comp, "acceptedKind", CageAccepts[i]);
                SetPrivate(comp, "controller", ctrl);
            }
        }

        static void BuildCatchNet(Transform parent, Material poleMat, Material ringMat)
        {
            var net = new GameObject("CatchNet");
            Undo.RegisterCreatedObjectUndo(net, "Create CatchNet");
            net.transform.SetParent(parent, false);
            net.transform.localPosition = CatchNetPos;
            net.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);

            // Pole — 길쭉한 실린더
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(net.transform, false);
            pole.transform.localScale = new Vector3(0.025f, 0.40f, 0.025f);
            pole.transform.localPosition = new Vector3(0f, 0f, 0f);
            pole.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GetRenderer(pole).sharedMaterial = poleMat;
            // Pole 자체 콜라이더는 grab 잡기용으로 유지 (작게)
            var poleCol = pole.GetComponent<CapsuleCollider>();
            if (poleCol != null) poleCol.isTrigger = false;

            // Head — 잠자리채 헤드(원판 + 트리거 sphere)
            var head = new GameObject("Head");
            head.transform.SetParent(net.transform, false);
            head.transform.localPosition = new Vector3(0f, 0f, 0.45f);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ring.name = "HeadVisual";
            ring.transform.SetParent(head.transform, false);
            ring.transform.localScale = new Vector3(0.20f, 0.05f, 0.20f);
            GetRenderer(ring).sharedMaterial = ringMat;
            DestroyImmediateSafe(ring.GetComponent<Collider>());

            // 트리거 sphere — 잠자리 캡처 영역
            var trig = head.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = 0.12f;

            // Rigidbody (XRGrabInteractable 필수)
            var rb = net.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.mass = 0.3f;
            rb.linearDamping = 1.5f;
            rb.angularDamping = 1.5f;

            // XR Grab (다른 PipePuz setup 들과 동일 — 기본 attach 동작 사용)
            net.AddComponent<XRGrabInteractable>();

            // CatchNet 스크립트
            var catchNet = net.AddComponent<CatchNet>();
            SetPrivate(catchNet, "netHead", head.transform);
            SetPrivate(catchNet, "headTrigger", trig);
        }

        static void BuildGloves(Transform parent, Material gloveMat)
        {
            var glove = new GameObject("Gloves");
            Undo.RegisterCreatedObjectUndo(glove, "Create Gloves");
            glove.transform.SetParent(parent, false);
            glove.transform.localPosition = GlovesPos;

            // 시각 — 손 모양 흉내 (큐브 + 손가락 작은 큐브)
            var palm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            palm.name = "Palm";
            palm.transform.SetParent(glove.transform, false);
            palm.transform.localScale = new Vector3(0.10f, 0.04f, 0.13f);
            GetRenderer(palm).sharedMaterial = gloveMat;

            // 콜라이더는 palm 의 BoxCollider 그대로 사용 (grab 영역)

            // Rigidbody
            var rb = glove.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.mass = 0.15f;
            rb.linearDamping = 1.5f;
            rb.angularDamping = 1.5f;

            // XR Grab
            glove.AddComponent<XRGrabInteractable>();

            // GloveAttachment — 손(HandInsulation 보유) 자식으로 들어가면 자동 등록
            glove.AddComponent<GloveAttachment>();
        }

        // ===== 헬퍼 =====

        static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(t.GetChild(i).gameObject);
        }

        static void DestroyChildIfExists(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }

        static void StripCreatureScripts(GameObject go)
        {
            foreach (var c in go.GetComponents<ZooCreature>()) Undo.DestroyObjectImmediate(c);
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) Undo.DestroyObjectImmediate(rb);
            foreach (var col in go.GetComponents<Collider>()) Undo.DestroyObjectImmediate(col);
        }

        static Renderer GetRenderer(GameObject g)
        {
            return g.GetComponent<Renderer>();
        }

        static void DestroyImmediateSafe(Object obj)
        {
            if (obj != null) Undo.DestroyObjectImmediate(obj);
        }

        /// <summary>SerializedObject 로 직렬화 필드에 값을 주입. private/SerializeField 모두 가능.</summary>
        static void SetPrivate(Object target, string fieldName, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"[ZooSetup] {target.GetType().Name} 에 '{fieldName}' 필드를 찾을 수 없습니다."); return; }
            switch (prop.propertyType)
            {
                case SerializedPropertyType.ObjectReference: prop.objectReferenceValue = value as Object; break;
                case SerializedPropertyType.Enum:            prop.enumValueIndex = System.Convert.ToInt32(value); break;
                case SerializedPropertyType.Integer:         prop.intValue = System.Convert.ToInt32(value); break;
                case SerializedPropertyType.Float:           prop.floatValue = System.Convert.ToSingle(value); break;
                case SerializedPropertyType.Boolean:         prop.boolValue = (bool)value; break;
                case SerializedPropertyType.String:          prop.stringValue = (string)value; break;
                default:
                    Debug.LogWarning($"[ZooSetup] '{fieldName}' 의 SerializedPropertyType={prop.propertyType} 미지원");
                    break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ZooCreature 보호 필드를 일관되게 세팅하기 위한 편의
        static void SetController(ZooCreature c, ZooPuzzleController ctrl) => SetPrivate(c, "controller", ctrl);
        static void SetThreatRadius(ZooCreature c, float v) => SetPrivate(c, "threatRadius", v);
        static void SetWanderRadius(ZooCreature c, float v) => SetPrivate(c, "wanderRadius", v);
        static void SetMoveSpeed(ZooCreature c, float v)    => SetPrivate(c, "moveSpeed", v);

        // ===== 머티리얼 =====
        // URP Lit/Unlit 의 _BaseColor 와 Surface(Opaque/Transparent) 를 직접 설정.
        static Material MakeUrpMaterial(string name, Color color, bool transparent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            else if (m.HasProperty("_Color"))   m.SetColor("_Color", color);

            if (transparent)
            {
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
                if (m.HasProperty("_Blend"))   m.SetFloat("_Blend", 0f);   // Alpha
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_ZWrite", 0);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (m.HasProperty("_SrcBlend"))
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend"))
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            return m;
        }
    }
}
