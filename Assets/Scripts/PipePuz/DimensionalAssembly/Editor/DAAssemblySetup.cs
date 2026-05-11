using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.DimensionalAssembly.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Build Dimensional Assembly.
    /// 씬의 puz1 GameObject 안에 책상 + 톱니바퀴 기계 + 에너지 노드 4개 + Wand + 쇼크 VFX 를 생성한다.
    /// 다시 누르면 puz1 안의 Table/Machine/Circuit/Wand 만 정리하고 새로 만든다.
    /// </summary>
    public static class DAAssemblySetup
    {
        // ----- 치수 -----
        const float TableTopY = 0.80f;       // 테이블 윗면 Y (floor 기준)
        const float TableWidth = 1.50f;
        const float TableDepth = 0.80f;
        const float TableThickness = 0.08f;

        const float PedestalTopY = 1.10f;    // 받침대 윗면
        const float PedestalWidth = 0.50f;
        const float PedestalDepth = 0.40f;

        const float GearRadius = 0.20f;
        const float DiscThickness = 0.04f;
        const int TeethCount = 12;
        const float ToothLen = 0.06f;        // 림 둘레 따라 (=접선 방향)
        const float ToothHeight = 0.025f;    // 위/아래 두께
        const float ToothWidth = 0.03f;      // 반경 방향
        const float GearY = PedestalTopY + DiscThickness * 0.5f;  // 1.12

        const float GripBallSize = 0.07f;

        // 노드 위치 — puz1 로컬, 기계 위쪽 + 앞쪽으로 떠있게.
        static readonly Vector3[] NodeLocalPositions = new[]
        {
            new Vector3(-0.50f, 1.45f, 0.10f),
            new Vector3(-0.17f, 1.60f, 0.10f),
            new Vector3(+0.17f, 1.60f, 0.10f),
            new Vector3(+0.50f, 1.45f, 0.10f),
        };
        const float NodeVisualSize = 0.10f;
        const float NodeColliderRadius = 0.09f; // 잡기 쉽게 시각보다 약간 큼

        // 와이어/연결선
        const float ConnectionWidth = 0.012f;

        // Wand
        static readonly Vector3 WandRestPos = new Vector3(0.75f, 1.05f, 0.20f);
        static readonly Quaternion WandRestRot = Quaternion.Euler(35f, -25f, 0f);
        const float WandLen = 0.26f;
        const float WandDiameter = 0.035f;

        // 타깃
        const float DefaultTargetAngle = 45f;
        const float DefaultAngleTolerance = 6f;

        [MenuItem("Tools/PipePuz/Build Dimensional Assembly")]
        public static void Build()
        {
            var puz = GameObject.Find("puz1");
            if (puz == null)
            {
                EditorUtility.DisplayDialog("Dimensional Assembly",
                    "씬에서 'puz1' 오브젝트를 찾을 수 없습니다.\nPipe Scene 을 열고 다시 시도하세요.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Dimensional Assembly");

            DestroyChildIfExists(puz.transform, "Table");
            DestroyChildIfExists(puz.transform, "Machine");
            DestroyChildIfExists(puz.transform, "Circuit");
            DestroyChildIfExists(puz.transform, "Wand");
            DestroyChildIfExists(puz.transform, "ShockEmitter");
            var oldCtrl = puz.GetComponent<DAAssemblyController>();
            if (oldCtrl != null) Undo.DestroyObjectImmediate(oldCtrl);

            // ----- 머티리얼 -----
            var tableMat = MakeUrpMaterial("DA_Table", new Color(0.32f, 0.28f, 0.22f), false);
            var pedestalMat = MakeUrpMaterial("DA_Pedestal", new Color(0.4f, 0.42f, 0.45f), false);
            var gearMat = MakeUrpMaterial("DA_Gear", new Color(0.55f, 0.55f, 0.6f), false);
            var gearAccentMat = MakeEmissiveMaterial("DA_GearAccent", new Color(0.6f, 0.5f, 0.3f), new Color(0.4f, 0.3f, 0.1f));
            var gripMat = MakeEmissiveMaterial("DA_Grip", new Color(0.9f, 0.65f, 0.25f), new Color(0.6f, 0.4f, 0.1f) * 1.2f);
            var markerMat = MakeEmissiveMaterial("DA_AlignMarker", new Color(1f, 0.85f, 0.1f), new Color(1f, 0.85f, 0.1f) * 1.5f);

            var nodeMat = MakeEmissiveMaterial("DA_Node", new Color(0.2f, 0.4f, 0.7f), new Color(0.08f, 0.12f, 0.28f));
            var connectionMat = MakeUnlitMaterial("DA_Connection", Color.white);
            var laserMat = MakeUnlitMaterial("DA_Laser", new Color(1f, 0.65f, 0.2f));
            var previewMat = MakeUnlitMaterial("DA_Preview", new Color(0.6f, 0.95f, 1f));

            var wandHandleMat = MakeUrpMaterial("DA_WandHandle", new Color(0.18f, 0.18f, 0.22f), false);
            var wandTipMat = MakeEmissiveMaterial("DA_WandTip", new Color(0.9f, 0.95f, 1f), new Color(0.4f, 0.7f, 1f) * 1.5f);

            // ----- 컨트롤러 -----
            var ctrl = puz.AddComponent<DAAssemblyController>();
            ctrl.TargetAngle = DefaultTargetAngle;
            ctrl.AngleTolerance = DefaultAngleTolerance;
            ctrl.ConnectionMaterial = connectionMat;
            ctrl.ConnectionColor = new Color(0.45f, 0.9f, 1.5f, 1f);
            ctrl.ConnectionWidth = ConnectionWidth;
            ctrl.LockDuration = 2.5f;
            ctrl.DecayDuration = 1.0f;
            ctrl.RequiredPairs = new[]
            {
                new Vector2Int(0, 1),
                new Vector2Int(1, 2),
                new Vector2Int(2, 3),
            };

            // ----- 책상 -----
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Table";
            Object.DestroyImmediate(table.GetComponent<Collider>());
            table.transform.SetParent(puz.transform, false);
            table.transform.localPosition = new Vector3(0f, TableTopY - TableThickness * 0.5f, 0f);
            table.transform.localScale = new Vector3(TableWidth, TableThickness, TableDepth);
            AssignMat(table, tableMat);

            // ----- 기계 -----
            var machine = new GameObject("Machine");
            machine.transform.SetParent(puz.transform, false);

            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = "Pedestal";
            Object.DestroyImmediate(pedestal.GetComponent<Collider>());
            pedestal.transform.SetParent(machine.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, (TableTopY + PedestalTopY) * 0.5f, -0.10f);
            pedestal.transform.localScale = new Vector3(PedestalWidth, PedestalTopY - TableTopY, PedestalDepth);
            AssignMat(pedestal, pedestalMat);

            // 기어 루트 — 받침대 위 중앙.
            var gearRoot = new GameObject("Gear");
            gearRoot.transform.SetParent(machine.transform, false);
            gearRoot.transform.localPosition = new Vector3(0f, GearY, -0.10f);

            // PivotYaw — DAGear 가 Y 축 회전을 적용.
            var pivot = new GameObject("PivotYaw");
            pivot.transform.SetParent(gearRoot.transform, false);

            // Disc (얇은 실린더).
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(pivot.transform, false);
            disc.transform.localPosition = Vector3.zero;
            disc.transform.localScale = new Vector3(GearRadius * 2f, DiscThickness * 0.5f, GearRadius * 2f);
            AssignMat(disc, gearMat);

            // 톱니 (12 개, 접선 정렬).
            for (int i = 0; i < TeethCount; i++)
            {
                float a = (i / (float)TeethCount) * Mathf.PI * 2f;
                var tooth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tooth.name = $"Tooth_{i}";
                Object.DestroyImmediate(tooth.GetComponent<Collider>());
                tooth.transform.SetParent(pivot.transform, false);
                float r = GearRadius + ToothWidth * 0.4f;
                tooth.transform.localPosition = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                tooth.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                tooth.transform.localScale = new Vector3(ToothLen, ToothHeight, ToothWidth);
                AssignMat(tooth, gearMat);
            }

            // Spoke — 회전 방향 인디케이터.
            var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spoke.name = "Spoke";
            Object.DestroyImmediate(spoke.GetComponent<Collider>());
            spoke.transform.SetParent(pivot.transform, false);
            spoke.transform.localPosition = new Vector3(0f, DiscThickness * 0.55f, GearRadius * 0.5f);
            spoke.transform.localScale = new Vector3(0.025f, 0.015f, GearRadius * 0.95f);
            AssignMat(spoke, gearAccentMat);

            // Grip — 림 위쪽에 위치한 잡기용 sphere (림 회전과 함께 돌아간다).
            var grip = new GameObject("Grip");
            grip.transform.SetParent(pivot.transform, false);
            grip.transform.localPosition = new Vector3(0f, DiscThickness * 0.55f + 0.02f, GearRadius - 0.005f);
            var gripVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gripVis.name = "Visual";
            Object.DestroyImmediate(gripVis.GetComponent<Collider>());
            gripVis.transform.SetParent(grip.transform, false);
            gripVis.transform.localScale = Vector3.one * GripBallSize;
            AssignMat(gripVis, gripMat);
            var gripCol = grip.AddComponent<SphereCollider>();
            gripCol.radius = GripBallSize * 0.55f;
            var gripInter = grip.AddComponent<XRSimpleInteractable>();

            // DAGear 컴포넌트.
            var gear = gearRoot.AddComponent<DAGear>();
            gear.PivotYaw = pivot.transform;
            gear.GripInteractable = gripInter;
            gear.MinAngle = -180f;
            gear.MaxAngle = +180f;
            gear.HeldDriftDegPerSec = 5f;
            gear.ReleasedDriftDegPerSec = 35f;

            // AlignmentMarker — gearRoot 의 자식이지만 PivotYaw 가 아니므로 회전하지 않음.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "AlignmentMarker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(gearRoot.transform, false);
            float trad = DefaultTargetAngle * Mathf.Deg2Rad;
            float mr = GearRadius + 0.07f;
            marker.transform.localPosition = new Vector3(Mathf.Sin(trad) * mr, -DiscThickness * 0.3f, Mathf.Cos(trad) * mr);
            marker.transform.localRotation = Quaternion.Euler(0f, DefaultTargetAngle, 0f);
            marker.transform.localScale = new Vector3(0.05f, 0.02f, 0.08f);
            AssignMat(marker, markerMat);

            // ----- 회로 (노드 4 개 + connections root) -----
            var circuit = new GameObject("Circuit");
            circuit.transform.SetParent(puz.transform, false);

            var nodes = new DAEnergyNode[NodeLocalPositions.Length];
            for (int i = 0; i < NodeLocalPositions.Length; i++)
            {
                var nodeGo = new GameObject($"Node_{i}");
                nodeGo.transform.SetParent(circuit.transform, false);
                nodeGo.transform.localPosition = NodeLocalPositions[i];

                var nodeVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nodeVis.name = "Visual";
                Object.DestroyImmediate(nodeVis.GetComponent<Collider>());
                nodeVis.transform.SetParent(nodeGo.transform, false);
                nodeVis.transform.localScale = Vector3.one * NodeVisualSize;
                // 각 노드마다 별도 머티리얼 인스턴스 (Awake 의 .material 호출이 자동 처리하지만 명시적으로).
                var instMat = new Material(nodeMat) { name = $"DA_Node_{i}" };
                nodeVis.GetComponent<Renderer>().sharedMaterial = instMat;

                var nodeCol = nodeGo.AddComponent<SphereCollider>();
                nodeCol.radius = NodeColliderRadius;
                nodeCol.isTrigger = true; // 레이가 트리거 콜라이더로 hit

                var node = nodeGo.AddComponent<DAEnergyNode>();
                node.Id = i;
                node.VisualRenderer = nodeVis.GetComponent<Renderer>();
                node.InactiveEmission = new Color(0.08f, 0.12f, 0.28f);
                node.ActiveEmission = new Color(0.4f, 0.85f, 1.5f) * 1.6f;
                nodes[i] = node;
            }

            var connectionsRoot = new GameObject("ConnectionsRoot");
            connectionsRoot.transform.SetParent(circuit.transform, false);
            connectionsRoot.transform.localPosition = Vector3.zero;

            // ----- Wand -----
            var wand = new GameObject("Wand");
            wand.transform.SetParent(puz.transform, false);
            wand.transform.localPosition = WandRestPos;
            wand.transform.localRotation = WandRestRot;

            var wandHandleVis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wandHandleVis.name = "HandleVisual";
            Object.DestroyImmediate(wandHandleVis.GetComponent<Collider>());
            wandHandleVis.transform.SetParent(wand.transform, false);
            wandHandleVis.transform.localPosition = Vector3.zero;
            wandHandleVis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 실린더의 Y 가 Z 로
            wandHandleVis.transform.localScale = new Vector3(WandDiameter, WandLen * 0.5f, WandDiameter);
            AssignMat(wandHandleVis, wandHandleMat);

            var wandCol = wand.AddComponent<CapsuleCollider>();
            wandCol.direction = 2; // Z axis
            wandCol.height = WandLen;
            wandCol.radius = WandDiameter * 0.5f;
            wandCol.isTrigger = false;

            var wandRb = wand.AddComponent<Rigidbody>();
            wandRb.useGravity = false;
            wandRb.isKinematic = true;
            wandRb.interpolation = RigidbodyInterpolation.Interpolate;

            var wandGrab = wand.AddComponent<XRGrabInteractable>();
            wandGrab.throwOnDetach = false;
            wandGrab.smoothPosition = false;

            // Tip — wand 의 앞쪽 끝 + 약간 위로 살짝 빛나는 sphere.
            var tip = new GameObject("Tip");
            tip.transform.SetParent(wand.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, WandLen * 0.5f);
            tip.transform.localRotation = Quaternion.identity;

            var tipVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tipVis.name = "Visual";
            Object.DestroyImmediate(tipVis.GetComponent<Collider>());
            tipVis.transform.SetParent(tip.transform, false);
            tipVis.transform.localScale = Vector3.one * 0.032f;
            AssignMat(tipVis, wandTipMat);

            // LaserLine (잡혀있는 동안 항상 표시).
            var laserGo = new GameObject("LaserLine");
            laserGo.transform.SetParent(wand.transform, false);
            laserGo.transform.localPosition = Vector3.zero;
            var laserLine = laserGo.AddComponent<LineRenderer>();
            laserLine.useWorldSpace = true;
            laserLine.positionCount = 2;
            laserLine.startWidth = 0.008f;
            laserLine.endWidth = 0.008f;
            laserLine.material = laserMat;
            laserLine.startColor = new Color(1f, 0.65f, 0.2f, 1f);
            laserLine.endColor = new Color(1f, 0.65f, 0.2f, 0.6f);

            // PreviewLine (그리기 중에만 표시).
            var previewGo = new GameObject("PreviewLine");
            previewGo.transform.SetParent(wand.transform, false);
            previewGo.transform.localPosition = Vector3.zero;
            var previewLine = previewGo.AddComponent<LineRenderer>();
            previewLine.useWorldSpace = true;
            previewLine.positionCount = 2;
            previewLine.startWidth = ConnectionWidth;
            previewLine.endWidth = ConnectionWidth;
            previewLine.material = previewMat;
            previewLine.startColor = new Color(0.6f, 0.95f, 1f, 0.8f);
            previewLine.endColor = new Color(0.6f, 0.95f, 1f, 0.4f);
            previewLine.enabled = false;

            var wandComp = wand.AddComponent<DAConnectionWand>();
            wandComp.Tip = tip.transform;
            wandComp.LaserLine = laserLine;
            wandComp.PreviewLine = previewLine;
            wandComp.Controller = ctrl;
            wandComp.NodeMask = ~0;
            wandComp.MaxRayDistance = 4f;
            wandComp.HitTriggers = true;

            // ----- 쇼크 VFX -----
            var shockGo = new GameObject("ShockEmitter");
            shockGo.transform.SetParent(puz.transform, false);
            shockGo.transform.localPosition = Vector3.zero;
            var ps = shockGo.AddComponent<ParticleSystem>();
            ConfigureShockParticleSystem(ps);

            // ----- 컨트롤러 wire-up -----
            ctrl.Gear = gear;
            ctrl.Nodes = nodes;
            ctrl.Wand = wandComp;
            ctrl.ConnectionsRoot = connectionsRoot.transform;
            ctrl.ShockEmitter = ps;
            ctrl.ShockParticlesPerEndpoint = 28;

            EditorUtility.SetDirty(puz);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(puz.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[DAAssembly] Build 완료. puz1 안의 Table / Machine / Circuit / Wand / ShockEmitter 를 확인하세요.");
        }

        // ----- Shock ParticleSystem -----

        static void ConfigureShockParticleSystem(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = 0.45f;
            main.startSpeed = 1.8f;
            main.startSize = 0.028f;
            main.startColor = new Color(0.6f, 0.95f, 1.4f, 1f);
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
                    new GradientColorKey(new Color(0.7f, 1f, 1.5f), 0f),
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

        // ----- Material / Util -----

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

        static Material MakeUnlitMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = name };
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            return m;
        }

        static void AssignMat(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }
    }
}
