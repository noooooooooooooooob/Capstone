using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace PipePuz.ShadowPuppetry.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Build Shadow Room.
    /// puz2 안에 Wall + Podium + Caster + Platform + HighSwitch + FlashlightStand + Flashlight + ShadowRoom 컨트롤러를 구성.
    /// </summary>
    public static class ShadowRoomSetup
    {
        // ----- 디멘션 -----
        // Wall
        const float WallWidth = 4f;
        const float WallHeight = 3f;
        const float WallThickness = 0.05f;
        static readonly Vector3 WallPos = new Vector3(0f, WallHeight * 0.5f, -1.0f);

        // Podium / Caster
        const float PodiumHeight = 1.0f;
        const float PodiumWidth = 0.30f;
        const float PodiumDepth = 0.30f;
        static readonly Vector3 PodiumCenter = new Vector3(0f, PodiumHeight * 0.5f, -0.5f);

        const float CasterSize = 0.40f;
        static readonly Vector3 CasterCenter = new Vector3(0f, PodiumHeight + CasterSize * 0.5f, -0.5f);

        // HighSwitch (벽 위의 위치 — 플랫폼 없이는 못 닿게 충분히 높게)
        const float SwitchOffsetX = 0f;
        const float SwitchOffsetY = 1.0f;   // wall 로컬, world Y = WallPos.y + 1.0 = 2.5
        const float SwitchProtrude = 0.04f; // 벽 면에서 방쪽으로 튀어나오는 거리
        const float SwitchVisualWidth = 0.08f;
        const float SwitchColliderRadius = 0.09f;

        // Flashlight
        static readonly Vector3 StandPos = new Vector3(0.65f, 0.35f, 0.40f);
        const float StandHeight = 0.70f;
        const float StandRadius = 0.10f;

        static readonly Vector3 FlashlightInitialPos = new Vector3(0.65f, StandHeight + 0.04f, 0.40f);
        // 초기에 손전등이 캐스터 쪽을 향하도록 회전.
        static readonly Quaternion FlashlightInitialRot = Quaternion.Euler(35f, -150f, 0f);
        const float FlashlightLen = 0.20f;
        const float FlashlightRadius = 0.025f;

        const float SpotLightRange = 7f;
        const float SpotLightAngle = 50f;
        const float SpotLightIntensity = 2.5f;

        // ----- Menu -----

        [MenuItem("Tools/PipePuz/Build Shadow Room")]
        public static void Build()
        {
            var puz = GameObject.Find("puz2");
            if (puz == null)
            {
                EditorUtility.DisplayDialog("Shadow Room",
                    "씬에서 'puz2' 오브젝트를 찾을 수 없습니다.\nPipe Scene 을 열고 다시 시도하세요.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Shadow Room");

            DestroyChildIfExists(puz.transform, "Wall");
            DestroyChildIfExists(puz.transform, "Podium");
            DestroyChildIfExists(puz.transform, "Caster_0");
            DestroyChildIfExists(puz.transform, "Platform_0");
            DestroyChildIfExists(puz.transform, "FlashlightStand");
            DestroyChildIfExists(puz.transform, "Flashlight");
            var oldCtrl = puz.GetComponent<ShadowRoomController>();
            if (oldCtrl != null) Undo.DestroyObjectImmediate(oldCtrl);

            // ----- 머티리얼 -----
            var wallMat = MakeUrpMaterial("Shadow_Wall", new Color(0.78f, 0.78f, 0.76f), false);
            var podiumMat = MakeUrpMaterial("Shadow_Podium", new Color(0.42f, 0.42f, 0.45f), false);
            var casterMat = MakeUrpMaterial("Shadow_Caster", new Color(0.65f, 0.45f, 0.25f), false);
            var standMat = MakeUrpMaterial("Shadow_Stand", new Color(0.3f, 0.3f, 0.32f), false);

            var flashBodyMat = MakeUrpMaterial("Shadow_FlashBody", new Color(0.22f, 0.22f, 0.25f), false);
            var flashTipMat = MakeEmissiveMaterial("Shadow_FlashTip", new Color(1f, 0.95f, 0.7f), new Color(1.5f, 1.3f, 0.8f));

            var platformMat = MakeTransparentEmissiveMaterial(
                "Shadow_Platform",
                new Color(0.18f, 0.22f, 0.35f, 0.65f),
                new Color(0.15f, 0.3f, 0.6f) * 0.7f);
            var shadowQuadMat = MakeTransparentUnlitMaterial("Shadow_Quad", new Color(0f, 0f, 0f, 0.7f));

            var switchInactiveMat = MakeEmissiveMaterial("Shadow_SwitchOff", new Color(0.8f, 0.18f, 0.18f), new Color(1.2f, 0.2f, 0.2f));
            var switchActiveMat = MakeEmissiveMaterial("Shadow_SwitchOn", new Color(0.2f, 0.9f, 0.4f), new Color(0.3f, 1.5f, 0.5f));

            // ----- Wall (root = surface point) -----
            var wall = new GameObject("Wall");
            wall.transform.SetParent(puz.transform, false);
            wall.transform.localPosition = WallPos;
            wall.transform.localRotation = Quaternion.identity;

            var wallMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallMesh.name = "Mesh";
            Object.DestroyImmediate(wallMesh.GetComponent<Collider>());
            wallMesh.transform.SetParent(wall.transform, false);
            // wall.position 이 surface 이므로 mesh 는 -forward 방향(=-Z)으로 thickness/2 만큼 뒤로.
            wallMesh.transform.localPosition = new Vector3(0f, 0f, -WallThickness * 0.5f);
            wallMesh.transform.localScale = new Vector3(WallWidth, WallHeight, WallThickness);
            AssignMat(wallMesh, wallMat);

            // ----- HighSwitch (Wall 의 자식) -----
            var switchGo = new GameObject("HighSwitch");
            switchGo.transform.SetParent(wall.transform, false);
            switchGo.transform.localPosition = new Vector3(SwitchOffsetX, SwitchOffsetY, 0f);
            switchGo.transform.localRotation = Quaternion.identity;

            var switchButton = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            switchButton.name = "ButtonVisual";
            Object.DestroyImmediate(switchButton.GetComponent<Collider>());
            switchButton.transform.SetParent(switchGo.transform, false);
            // 실린더의 Y 축이 길이축이므로 X 축으로 90° 회전해서 길이축을 Z 방향으로.
            switchButton.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            switchButton.transform.localPosition = new Vector3(0f, 0f, SwitchProtrude * 0.5f);
            switchButton.transform.localScale = new Vector3(SwitchVisualWidth, SwitchProtrude * 0.5f, SwitchVisualWidth);
            AssignMat(switchButton, switchInactiveMat);

            var switchCol = switchGo.AddComponent<SphereCollider>();
            switchCol.radius = SwitchColliderRadius;
            switchCol.center = new Vector3(0f, 0f, SwitchProtrude * 0.4f);
            switchCol.isTrigger = false;

            var switchInter = switchGo.AddComponent<XRSimpleInteractable>();
            var switchComp = switchGo.AddComponent<ShadowSwitch>();
            switchComp.Interactable = switchInter;
            switchComp.ButtonRenderer = switchButton.GetComponent<Renderer>();
            switchComp.InactiveMaterial = switchInactiveMat;
            switchComp.ActiveMaterial = switchActiveMat;

            // ----- Podium (캐스터를 받치는 받침대) -----
            var podium = GameObject.CreatePrimitive(PrimitiveType.Cube);
            podium.name = "Podium";
            Object.DestroyImmediate(podium.GetComponent<Collider>());
            podium.transform.SetParent(puz.transform, false);
            podium.transform.localPosition = PodiumCenter;
            podium.transform.localScale = new Vector3(PodiumWidth, PodiumHeight, PodiumDepth);
            AssignMat(podium, podiumMat);

            // ----- Caster_0 -----
            var casterGo = new GameObject("Caster_0");
            casterGo.transform.SetParent(puz.transform, false);
            casterGo.transform.localPosition = CasterCenter;
            casterGo.transform.localRotation = Quaternion.identity;

            var casterMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            casterMesh.name = "Mesh";
            Object.DestroyImmediate(casterMesh.GetComponent<Collider>()); // 플레이어가 통과할 수 있게
            casterMesh.transform.SetParent(casterGo.transform, false);
            casterMesh.transform.localScale = Vector3.one * CasterSize;
            AssignMat(casterMesh, casterMat);

            var caster = casterGo.AddComponent<ShadowCaster>();
            caster.CasterRenderer = casterMesh.GetComponent<Renderer>();

            // ----- Platform_0 -----
            var platformRoot = new GameObject("Platform_0");
            platformRoot.transform.SetParent(puz.transform, false);
            platformRoot.transform.localPosition = Vector3.zero;
            platformRoot.transform.localRotation = Quaternion.identity;
            platformRoot.transform.localScale = Vector3.one;

            // PlatformBody — Cube + BoxCollider + TeleportationArea. 컨트롤러가 매 프레임 갱신.
            var bodyGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bodyGo.name = "Body";
            bodyGo.transform.SetParent(platformRoot.transform, false);
            // 기본 콜라이더 유지(Cube primitive 의 BoxCollider). 그 위에 TeleportationArea 추가.
            AssignMat(bodyGo, platformMat);
            bodyGo.AddComponent<TeleportationArea>();

            // ShadowVisualQuad — Quad + 검은 머티리얼.
            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "ShadowVisualQuad";
            Object.DestroyImmediate(quadGo.GetComponent<Collider>());
            quadGo.transform.SetParent(platformRoot.transform, false);
            AssignMat(quadGo, shadowQuadMat);

            var platformComp = platformRoot.AddComponent<ShadowPlatform>();
            platformComp.PlatformBody = bodyGo.transform;
            platformComp.ShadowVisualQuad = quadGo.transform;
            platformComp.SetActive(false); // 시작 시엔 숨김 — 유효한 그림자 생기면 컨트롤러가 켠다.

            // ----- FlashlightStand -----
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "FlashlightStand";
            Object.DestroyImmediate(stand.GetComponent<Collider>());
            stand.transform.SetParent(puz.transform, false);
            stand.transform.localPosition = new Vector3(StandPos.x, StandHeight * 0.5f, StandPos.z);
            stand.transform.localScale = new Vector3(StandRadius * 2f, StandHeight * 0.5f, StandRadius * 2f);
            AssignMat(stand, standMat);

            // ----- Flashlight -----
            var flashGo = new GameObject("Flashlight");
            flashGo.transform.SetParent(puz.transform, false);
            flashGo.transform.localPosition = FlashlightInitialPos;
            flashGo.transform.localRotation = FlashlightInitialRot;

            var flashBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            flashBody.name = "BodyVisual";
            Object.DestroyImmediate(flashBody.GetComponent<Collider>());
            flashBody.transform.SetParent(flashGo.transform, false);
            // 실린더 길이축(Y) 을 Z 로.
            flashBody.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            flashBody.transform.localPosition = Vector3.zero;
            flashBody.transform.localScale = new Vector3(FlashlightRadius * 2f, FlashlightLen * 0.5f, FlashlightRadius * 2f);
            AssignMat(flashBody, flashBodyMat);

            // Grab 용 콜라이더 (실린더 형태).
            var flashCol = flashGo.AddComponent<CapsuleCollider>();
            flashCol.direction = 2; // Z
            flashCol.radius = FlashlightRadius;
            flashCol.height = FlashlightLen;
            flashCol.center = Vector3.zero;

            var flashRb = flashGo.AddComponent<Rigidbody>();
            flashRb.useGravity = false;
            flashRb.isKinematic = true;
            flashRb.interpolation = RigidbodyInterpolation.Interpolate;

            var flashGrab = flashGo.AddComponent<XRGrabInteractable>();
            flashGrab.throwOnDetach = false;
            flashGrab.smoothPosition = false;

            // Tip — 손전등 앞쪽 끝 + 작은 발광 sphere.
            var tip = new GameObject("Tip");
            tip.transform.SetParent(flashGo.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, FlashlightLen * 0.5f);
            tip.transform.localRotation = Quaternion.identity;

            var tipVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tipVis.name = "Visual";
            Object.DestroyImmediate(tipVis.GetComponent<Collider>());
            tipVis.transform.SetParent(tip.transform, false);
            tipVis.transform.localScale = Vector3.one * (FlashlightRadius * 1.6f);
            AssignMat(tipVis, flashTipMat);

            // Spot Light on Tip — 그림자 계산엔 영향 없지만 시각적 빛 효과.
            var lightGo = new GameObject("SpotLight");
            lightGo.transform.SetParent(tip.transform, false);
            lightGo.transform.localPosition = Vector3.zero;
            lightGo.transform.localRotation = Quaternion.identity;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = SpotLightRange;
            light.spotAngle = SpotLightAngle;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.intensity = SpotLightIntensity;
            light.shadows = LightShadows.None; // 그림자는 우리가 직접 콜라이더+다크 quad 로 처리

            var flashlightComp = flashGo.AddComponent<ShadowFlashlight>();
            flashlightComp.Tip = tip.transform;
            flashlightComp.SpotLight = light;

            // ----- Controller wire-up -----
            var ctrl = puz.AddComponent<ShadowRoomController>();
            ctrl.Flashlight = flashlightComp;
            ctrl.WallSurface = wall.transform;
            ctrl.Casters = new[] { caster };
            ctrl.Platforms = new[] { platformComp };
            ctrl.Switch = switchComp;
            ctrl.PlatformDepth = 0.45f;
            ctrl.PlatformThickness = 0.06f;
            ctrl.WallSurfaceOffset = 0.005f;
            ctrl.Smoothing = 0.35f;

            EditorUtility.SetDirty(puz);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(puz.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[ShadowRoom] Build 완료. puz2 안의 Wall / Podium / Caster_0 / Platform_0 / Flashlight / HighSwitch 를 확인하세요.");
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
            if (transparent) MakeTransparent(m);
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

        static Material MakeTransparentEmissiveMaterial(string name, Color baseColor, Color emissionColor)
        {
            var m = MakeEmissiveMaterial(name, baseColor, emissionColor);
            MakeTransparent(m);
            return m;
        }

        static Material MakeTransparentUnlitMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = name };
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            MakeTransparent(m);
            return m;
        }

        static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static void AssignMat(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }
    }
}
