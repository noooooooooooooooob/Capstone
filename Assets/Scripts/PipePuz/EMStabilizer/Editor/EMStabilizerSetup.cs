using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.EMStabilizer.EditorTools
{
    /// <summary>
    /// 메뉴 Tools > PipePuz > Build EM Stabilizer.
    /// 씬의 Radio 오브젝트 안에 안테나(Antenna) 와 태블릿(Tablet) 을 자동 생성하고
    /// 모든 컴포넌트 참조를 wire-up 한다. 다시 누르면 이전에 만든 Antenna / Tablet 자식을
    /// 지우고 새로 만든다 (Radio 안의 다른 자식은 보존).
    ///
    /// Radio 의 +Z 가 플레이어 방향이라고 가정. 부모 Radio 의 위치/회전으로 배치를 잡으면 된다.
    /// </summary>
    public static class EMStabilizerSetup
    {
        // ---- 안테나 사이즈 ----
        const float BaseHeight = 0.10f;
        const float BaseRadius = 0.22f;
        const float TowerHeight = 1.20f;
        const float TowerRadius = 0.035f;
        const float DishSize = 0.35f;
        const float ArmHeight = 0.30f;
        const float ArmForward = 0.25f;
        const float GripSize = 0.10f;

        // ---- 태블릿 ----
        const float BodyWidth = 0.42f;
        const float BodyHeight = 0.30f;
        const float BodyDepth = 0.025f;
        const float StandHeight = 1.05f;
        const float TabletYWorld = 1.30f;   // Tablet 본체 중심 Y (floor 기준)
        const float ScreenInset = 0.012f;   // body 앞면 보다 살짝 앞에 그려질 z 오프셋

        // ---- 슬라이더 ----
        const float SliderTrackLen = 0.18f;
        const float SliderTrackThickness = 0.018f;
        const float SliderKnobSize = 0.045f;
        const float SliderYOffset = -0.215f; // tablet 본체 아래로 살짝 내려간 위치
        const float SliderZOffset = 0.055f;  // body 앞으로 튀어나오게

        // ---- 타깃 기본값 ----
        const float DefaultTargetAngle = 30f;
        const float DefaultTargetS1 = 0.7f;
        const float DefaultTargetS2 = 0.3f;
        const float DefaultAngleTol = 5f;
        const float DefaultSliderTol = 0.08f;

        [MenuItem("Tools/PipePuz/Build EM Stabilizer")]
        public static void Build()
        {
            var radio = GameObject.Find("Radio");
            if (radio == null)
            {
                EditorUtility.DisplayDialog("EM Stabilizer",
                    "씬에서 'Radio' 오브젝트를 찾을 수 없습니다.\nPipe Scene 을 열고 다시 시도하세요.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build EM Stabilizer");

            // 기존 Antenna / Tablet 만 정리. 다른 자식은 건드리지 않음.
            DestroyChildIfExists(radio.transform, "Antenna");
            DestroyChildIfExists(radio.transform, "Tablet");
            var oldCtrl = radio.GetComponent<EMStabilizerController>();
            if (oldCtrl != null) Undo.DestroyObjectImmediate(oldCtrl);

            // 머티리얼
            var mats = new Materials
            {
                AntennaBody = MakeUrpMaterial("EM_AntennaBody", new Color(0.35f, 0.38f, 0.42f), false),
                AntennaAccent = MakeUrpMaterial("EM_AntennaAccent", new Color(0.55f, 0.6f, 0.7f), false),
                DishEmissive = MakeEmissiveMaterial("EM_Dish", new Color(0.3f, 0.32f, 0.35f), new Color(0.05f, 0.05f, 0.05f)),
                Handle = MakeUrpMaterial("EM_Handle", new Color(0.25f, 0.25f, 0.28f), false),
                Grip = MakeEmissiveMaterial("EM_Grip", new Color(0.9f, 0.65f, 0.25f), new Color(0.6f, 0.35f, 0.1f) * 1.2f),
                TargetMarker = MakeEmissiveMaterial("EM_TargetMarker", new Color(1f, 0.85f, 0.1f), new Color(1f, 0.85f, 0.1f) * 1.6f),

                TabletFrame = MakeUrpMaterial("EM_TabletFrame", new Color(0.18f, 0.18f, 0.2f), false),
                Screen = MakeEmissiveMaterial("EM_Screen", new Color(0.04f, 0.06f, 0.1f), new Color(0.05f, 0.1f, 0.2f)),
                ScreenAccent = MakeUrpMaterial("EM_ScreenAccent", new Color(0.1f, 0.18f, 0.28f), false),

                Track = MakeUrpMaterial("EM_Track", new Color(0.2f, 0.2f, 0.22f), false),
                TrackTarget = MakeEmissiveMaterial("EM_TrackTarget", new Color(0.25f, 0.9f, 0.5f), new Color(0.2f, 0.9f, 0.45f) * 0.9f),
                Knob = MakeEmissiveMaterial("EM_Knob", new Color(0.85f, 0.85f, 0.9f), new Color(0.4f, 0.55f, 0.7f) * 0.8f),

                LampOk = MakeEmissiveMaterial("EM_LampOk", new Color(0.2f, 0.95f, 0.4f), new Color(0.2f, 1.2f, 0.4f) * 1.5f),
                LampBad = MakeEmissiveMaterial("EM_LampBad", new Color(0.9f, 0.25f, 0.25f), new Color(1.1f, 0.25f, 0.25f) * 1.0f),

                LockMeterBg = MakeUrpMaterial("EM_LockBg", new Color(0.12f, 0.18f, 0.25f), false),
                LockMeterFill = MakeEmissiveMaterial("EM_LockFill", new Color(0.3f, 0.85f, 1f), new Color(0.3f, 1.1f, 1.4f) * 1.4f),

                WaveformLine = MakeUnlitMaterial("EM_WaveformLine", new Color(0.35f, 1f, 0.7f)),
            };

            // 컨트롤러 컴포넌트 추가 (참조는 끝에 채움)
            var ctrl = radio.AddComponent<EMStabilizerController>();
            ctrl.TargetAngle = DefaultTargetAngle;
            ctrl.TargetSlider1 = DefaultTargetS1;
            ctrl.TargetSlider2 = DefaultTargetS2;
            ctrl.AngleTolerance = DefaultAngleTol;
            ctrl.SliderTolerance = DefaultSliderTol;
            ctrl.LockDuration = 3.0f;
            ctrl.DecayDuration = 1.5f;

            // --- 안테나 ---
            var antennaRefs = BuildAntenna(radio.transform, mats);

            // --- 태블릿 ---
            var tabletRefs = BuildTablet(radio.transform, mats);

            // --- 슬라이더 (태블릿 자식) ---
            var slider1 = BuildSlider(tabletRefs.Root, "Slider1", -0.10f, mats, DefaultTargetS1, 0.15f);
            var slider2 = BuildSlider(tabletRefs.Root, "Slider2", +0.10f, mats, DefaultTargetS2, 0.85f);

            // --- 참조 wire-up ---
            ctrl.Handle = antennaRefs.Handle;
            ctrl.Slider1 = slider1;
            ctrl.Slider2 = slider2;
            ctrl.Tablet = tabletRefs.TabletComp;

            tabletRefs.TabletComp.AntennaGlowRenderer = antennaRefs.DishRenderer;
            tabletRefs.TabletComp.LampOkMaterial = mats.LampOk;
            tabletRefs.TabletComp.LampBadMaterial = mats.LampBad;

            // 안테나 베이스의 타깃 마커 — TargetAngle 만큼 회전된 위치에 노란 인디케이터.
            PlaceTargetMarker(antennaRefs.BaseTransform, DefaultTargetAngle, mats.TargetMarker);

            EditorUtility.SetDirty(radio);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(radio.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[EMStabilizer] Build 완료. Radio 안의 Antenna / Tablet 을 확인하세요.");
        }

        // ----- Antenna -----

        struct AntennaRefs
        {
            public EMHandle Handle;
            public Renderer DishRenderer;
            public Transform BaseTransform;
        }

        static AntennaRefs BuildAntenna(Transform parent, Materials mats)
        {
            var antenna = new GameObject("Antenna");
            antenna.transform.SetParent(parent, false);
            antenna.transform.localPosition = new Vector3(-0.45f, 0f, 0f);
            antenna.transform.localRotation = Quaternion.identity;

            // Base
            var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseGo.name = "Base";
            Object.DestroyImmediate(baseGo.GetComponent<Collider>());
            baseGo.transform.SetParent(antenna.transform, false);
            baseGo.transform.localPosition = new Vector3(0f, BaseHeight * 0.5f, 0f);
            baseGo.transform.localScale = new Vector3(BaseRadius * 2f, BaseHeight * 0.5f, BaseRadius * 2f);
            AssignMat(baseGo, mats.AntennaBody);

            // Tower
            var tower = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tower.name = "Tower";
            Object.DestroyImmediate(tower.GetComponent<Collider>());
            tower.transform.SetParent(antenna.transform, false);
            tower.transform.localPosition = new Vector3(0f, BaseHeight + TowerHeight * 0.5f, 0f);
            tower.transform.localScale = new Vector3(TowerRadius * 2f, TowerHeight * 0.5f, TowerRadius * 2f);
            AssignMat(tower, mats.AntennaAccent);

            // PivotYaw — 회전축
            var pivot = new GameObject("PivotYaw");
            pivot.transform.SetParent(antenna.transform, false);
            float pivotY = BaseHeight + TowerHeight;
            pivot.transform.localPosition = new Vector3(0f, pivotY, 0f);
            pivot.transform.localRotation = Quaternion.identity;

            // Dish — 살짝 평평한 구체로 표현 + Emission 가능
            var dish = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dish.name = "Dish";
            Object.DestroyImmediate(dish.GetComponent<Collider>());
            dish.transform.SetParent(pivot.transform, false);
            dish.transform.localPosition = new Vector3(0f, 0.08f, 0.10f);
            dish.transform.localScale = new Vector3(DishSize, DishSize * 0.45f, DishSize * 0.8f);
            // 각 빌드마다 새 dish 머티리얼을 만들어 emission 갱신이 다른 안테나와 분리되게 한다.
            var dishMat = new Material(mats.DishEmissive) { name = "EM_Dish_Inst" };
            dish.GetComponent<Renderer>().sharedMaterial = dishMat;
            var dishRenderer = dish.GetComponent<Renderer>();

            // PointerArrow — 핀이 어디 가리키는지 보여주는 작은 표식
            var pointer = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pointer.name = "Pointer";
            Object.DestroyImmediate(pointer.GetComponent<Collider>());
            pointer.transform.SetParent(pivot.transform, false);
            pointer.transform.localPosition = new Vector3(0f, -0.02f, 0.22f);
            pointer.transform.localScale = new Vector3(0.04f, 0.04f, 0.14f);
            AssignMat(pointer, mats.AntennaAccent);

            // HandleArm — 시각용 막대 (Pivot 의 자식, 함께 회전)
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "HandleArm";
            Object.DestroyImmediate(arm.GetComponent<Collider>());
            arm.transform.SetParent(pivot.transform, false);
            arm.transform.localPosition = new Vector3(0f, -ArmHeight * 0.5f, ArmForward);
            arm.transform.localScale = new Vector3(0.035f, ArmHeight, 0.035f);
            AssignMat(arm, mats.Handle);

            // HandleGrip — 잡기 가능한 끝 부분 (Pivot 의 직접 자식. arm 의 자식으로 두면 arm.scale 이 영향)
            var gripGo = new GameObject("HandleGrip");
            gripGo.transform.SetParent(pivot.transform, false);
            gripGo.transform.localPosition = new Vector3(0f, -ArmHeight, ArmForward);
            gripGo.transform.localScale = Vector3.one;

            var gripVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gripVisual.name = "Visual";
            Object.DestroyImmediate(gripVisual.GetComponent<Collider>());
            gripVisual.transform.SetParent(gripGo.transform, false);
            gripVisual.transform.localPosition = Vector3.zero;
            gripVisual.transform.localScale = Vector3.one * GripSize;
            AssignMat(gripVisual, mats.Grip);

            var gripCollider = gripGo.AddComponent<SphereCollider>();
            gripCollider.radius = GripSize * 0.6f;
            var gripInteractable = gripGo.AddComponent<XRSimpleInteractable>();

            // EMHandle 컴포넌트 (안테나 루트에 배치)
            var handle = antenna.AddComponent<EMHandle>();
            handle.PivotYaw = pivot.transform;
            handle.GripInteractable = gripInteractable;
            handle.MinAngle = -75f;
            handle.MaxAngle = 75f;
            handle.HeldDriftDegPerSec = 8f;
            handle.DriftSpeedDegPerSec = 35f;

            return new AntennaRefs
            {
                Handle = handle,
                DishRenderer = dishRenderer,
                BaseTransform = baseGo.transform,
            };
        }

        static void PlaceTargetMarker(Transform baseTransform, float targetAngleDeg, Material mat)
        {
            // baseTransform 의 부모(Antenna) 좌표계 기준으로 PivotYaw 가 회전하므로,
            // 마커는 Antenna 의 자식이 되어 베이스 위에 작은 표식으로 배치한다.
            var antenna = baseTransform.parent;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "TargetMarker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(antenna, false);
            float rad = targetAngleDeg * Mathf.Deg2Rad;
            float r = BaseRadius * 0.85f;
            // 안테나 로컬에서 +Z 방향이 angle=0. atan2(rgt, fwd) 와 일치하도록 (sin, *, cos).
            marker.transform.localPosition = new Vector3(Mathf.Sin(rad) * r, BaseHeight + 0.005f, Mathf.Cos(rad) * r);
            marker.transform.localRotation = Quaternion.Euler(0f, targetAngleDeg, 0f);
            marker.transform.localScale = new Vector3(0.05f, 0.02f, 0.08f);
            AssignMat(marker, mat);
        }

        // ----- Tablet -----

        struct TabletRefs
        {
            public Transform Root;
            public EMTablet TabletComp;
        }

        static TabletRefs BuildTablet(Transform parent, Materials mats)
        {
            var tablet = new GameObject("Tablet");
            tablet.transform.SetParent(parent, false);
            tablet.transform.localPosition = new Vector3(0.45f, 0f, 0f);
            tablet.transform.localRotation = Quaternion.identity;

            // Stand
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "Stand";
            Object.DestroyImmediate(stand.GetComponent<Collider>());
            stand.transform.SetParent(tablet.transform, false);
            stand.transform.localPosition = new Vector3(0f, StandHeight * 0.5f, 0f);
            stand.transform.localScale = new Vector3(0.05f, StandHeight * 0.5f, 0.05f);
            AssignMat(stand, mats.AntennaAccent);

            // Body — 화면 받침. 약간 위쪽으로 기울어져 사용자 시선 쪽으로 향함.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(tablet.transform, false);
            body.transform.localPosition = new Vector3(0f, TabletYWorld, 0f);
            body.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f); // 위쪽 12° 기울임
            body.transform.localScale = new Vector3(BodyWidth, BodyHeight, BodyDepth);
            AssignMat(body, mats.TabletFrame);

            // Screen — body 와 같은 회전을 받도록 body 의 자식.
            // 다만 body 의 scale 이 적용되므로 scale 보정 필요. 별도 child 로 두고 절대 크기 지정.
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "Screen";
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.transform.SetParent(body.transform, false);
            screen.transform.localPosition = new Vector3(0f, 0f, -0.5f - ScreenInset / BodyDepth);
            // body scale 영향을 상쇄하기 위해 lossy 역수로 local scale 설정.
            screen.transform.localScale = new Vector3(0.93f, 0.92f, ScreenInset / BodyDepth);
            AssignMat(screen, mats.Screen);

            // 화면 위에 그려질 요소들 — body 의 자식으로 두되 위치는 body 의 local space.
            // 모두 body 의 -Z (앞쪽) 방향에 살짝 띄워 z-fighting 방지.

            // LockMeter (가로 막대) — body local space.
            // body 가 -12° 기울었으므로 자식들은 자연스럽게 같이 기운다.
            float meterY = -0.30f; // body 로컬 (-0.5 ~ 0.5). 화면 아래쪽.
            float meterFrameZ = -0.55f;
            float meterFrameW = 0.75f;
            float meterFrameH = 0.10f;

            var meterBg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            meterBg.name = "LockMeterBg";
            Object.DestroyImmediate(meterBg.GetComponent<Collider>());
            meterBg.transform.SetParent(body.transform, false);
            meterBg.transform.localPosition = new Vector3(0f, meterY, meterFrameZ);
            meterBg.transform.localScale = new Vector3(meterFrameW, meterFrameH, 0.05f);
            AssignMat(meterBg, mats.LockMeterBg);

            var meterFill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            meterFill.name = "LockMeterFill";
            Object.DestroyImmediate(meterFill.GetComponent<Collider>());
            meterFill.transform.SetParent(body.transform, false);
            // 좌측 정렬 채움: pivot 을 좌측 끝에 두기 위해 wrapper 사용.
            var meterFillWrapper = new GameObject("LockMeterFillWrapper");
            meterFillWrapper.transform.SetParent(body.transform, false);
            meterFillWrapper.transform.localPosition = new Vector3(-meterFrameW * 0.5f, meterY, meterFrameZ - 0.005f);
            meterFillWrapper.transform.localScale = Vector3.one;
            meterFill.transform.SetParent(meterFillWrapper.transform, false);
            meterFill.transform.localPosition = new Vector3(0.5f * 0.0f /* 시작은 0 채움 */, 0f, 0f);
            // localScale.x 가 0..0.75 로 바뀌면 좌측에서 우측으로 채워짐.
            // 큐브 기본 polynomial: scale.x*1 면 폭 1, 위치 0.5 이면 좌측 끝이 0.
            // 좀더 단순하게: fill 의 transform.position = wrapper 시작, scale.x = 진행률 * meterFrameW. 큐브는 -0.5~0.5 폭.
            meterFill.transform.localScale = new Vector3(0f, meterFrameH * 0.85f, 0.04f);
            // fill 의 피벗을 왼쪽 정렬 (큐브 기본은 중앙). 자식 transform.localPosition.x = scale.x*0.5 로 갱신해야 한다 → 이 작업을 EMTablet 의 LockMeterMaxScaleX 매핑과 연동하기 어렵다.
            // 대안: fill 큐브를 정렬용 wrapper 의 자식으로 두고, fill 의 localPosition 을 (scale.x*0.5, 0, 0) 로 둠. EMTablet 의 LockMeterMaxScaleX 가 meterFrameW 와 같게 두면 정상 작동.
            AssignMat(meterFill, mats.LockMeterFill);

            // LockMeterFill 의 piivot 정렬 헬퍼 — 동작에 의존하지 않고, EMTablet 이 fill.localScale.x 만 바꿔도 좌측 정렬되도록.
            // 추가 child 'Inner' 를 만들어, Inner.localPosition = (0.5, 0, 0), Inner 의 scale 은 1 — fill 부모만 X 스케일 조절.
            var fillInner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fillInner.name = "Inner";
            Object.DestroyImmediate(fillInner.GetComponent<Collider>());
            fillInner.transform.SetParent(meterFill.transform, false);
            fillInner.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            fillInner.transform.localScale = Vector3.one;
            AssignMat(fillInner, mats.LockMeterFill);
            // 원래 fill 큐브의 mesh 는 보이지 않게 — fill 은 단순한 '컨테이너'.
            var fillR = meterFill.GetComponent<Renderer>();
            if (fillR != null) fillR.enabled = false;

            // Lamps (Angle, Slider1, Slider2)
            float lampY = 0.42f; // 화면 위쪽
            float lampZ = -0.55f;
            float lampSpacing = 0.18f;

            var lampAngle = MakeLamp(body.transform, "LampAngle", new Vector3(-lampSpacing, lampY, lampZ), mats.LampBad);
            var lampS1 = MakeLamp(body.transform, "LampSlider1", new Vector3(0f, lampY, lampZ), mats.LampBad);
            var lampS2 = MakeLamp(body.transform, "LampSlider2", new Vector3(+lampSpacing, lampY, lampZ), mats.LampBad);

            // Waveform LineRenderer — body 의 자식, 화면 위쪽 영역.
            var waveformGo = new GameObject("WaveformLine");
            waveformGo.transform.SetParent(body.transform, false);
            waveformGo.transform.localPosition = new Vector3(0f, 0.10f, -0.56f);
            waveformGo.transform.localRotation = Quaternion.identity;
            waveformGo.transform.localScale = Vector3.one;

            var line = waveformGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.startWidth = 0.012f;
            line.endWidth = 0.012f;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.TransformZ;
            line.material = mats.WaveformLine;

            // EMTablet 컴포넌트
            var tabletComp = tablet.AddComponent<EMTablet>();
            tabletComp.WaveformLine = line;
            tabletComp.WaveformAreaSize = new Vector2(0.70f, 0.30f);
            tabletComp.WaveformAreaCenter = new Vector2(0f, 0f);
            tabletComp.WaveformPointCount = 60;
            tabletComp.SignalHz = 1.5f;
            tabletComp.LockMeterFill = meterFill.transform;
            tabletComp.LockMeterMaxScaleX = meterFrameW; // fill.localScale.x 가 0..meterFrameW 로 변하면 좌측에서 우측으로 채움
            tabletComp.AngleOkLamp = lampAngle;
            tabletComp.Slider1OkLamp = lampS1;
            tabletComp.Slider2OkLamp = lampS2;

            return new TabletRefs { Root = tablet.transform, TabletComp = tabletComp };
        }

        static Renderer MakeLamp(Transform parent, string name, Vector3 localPos, Material mat)
        {
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = name;
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = localPos;
            lamp.transform.localScale = new Vector3(0.07f, 0.07f, 0.07f);
            AssignMat(lamp, mat);
            return lamp.GetComponent<Renderer>();
        }

        // ----- Slider -----

        static EMSlider BuildSlider(Transform tabletRoot, string name, float localX, Materials mats, float targetValue, float initialValue)
        {
            var slider = new GameObject(name);
            slider.transform.SetParent(tabletRoot, false);
            slider.transform.localPosition = new Vector3(localX, TabletYWorld + SliderYOffset, SliderZOffset);
            slider.transform.localRotation = Quaternion.identity;

            // 트랙 시각
            var trackVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trackVisual.name = "TrackVisual";
            Object.DestroyImmediate(trackVisual.GetComponent<Collider>());
            trackVisual.transform.SetParent(slider.transform, false);
            trackVisual.transform.localPosition = Vector3.zero;
            trackVisual.transform.localScale = new Vector3(SliderTrackLen, SliderTrackThickness, SliderTrackThickness);
            AssignMat(trackVisual, mats.Track);

            // Track 양끝점 (empty)
            var trackStart = new GameObject("TrackStart");
            trackStart.transform.SetParent(slider.transform, false);
            trackStart.transform.localPosition = new Vector3(-SliderTrackLen * 0.5f, 0f, 0f);

            var trackEnd = new GameObject("TrackEnd");
            trackEnd.transform.SetParent(slider.transform, false);
            trackEnd.transform.localPosition = new Vector3(+SliderTrackLen * 0.5f, 0f, 0f);

            // Target zone — 트랙 위 targetValue 위치를 강조.
            var targetMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            targetMarker.name = "TargetZone";
            Object.DestroyImmediate(targetMarker.GetComponent<Collider>());
            targetMarker.transform.SetParent(slider.transform, false);
            targetMarker.transform.localPosition = new Vector3(-SliderTrackLen * 0.5f + targetValue * SliderTrackLen, SliderTrackThickness * 1.2f, 0f);
            targetMarker.transform.localScale = new Vector3(SliderTrackLen * 0.16f, SliderTrackThickness * 0.6f, SliderTrackThickness * 1.6f);
            AssignMat(targetMarker, mats.TrackTarget);

            // Knob — 잡기 가능
            var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knob.name = "Knob";
            Object.DestroyImmediate(knob.GetComponent<Collider>()); // 기본 콜라이더 제거 후 정확한 크기 재추가
            knob.transform.SetParent(slider.transform, false);
            knob.transform.localPosition = new Vector3(-SliderTrackLen * 0.5f + initialValue * SliderTrackLen, 0f, 0f);
            knob.transform.localScale = new Vector3(SliderKnobSize, SliderKnobSize, SliderKnobSize);
            AssignMat(knob, mats.Knob);

            var col = knob.AddComponent<SphereCollider>();
            col.radius = 0.5f; // 노브 local scale 이 적용되므로 반경 = 0.5 * SliderKnobSize

            var rb = knob.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var grab = knob.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = false;
            grab.smoothPosition = true;

            // EMSlider 컴포넌트
            var s = slider.AddComponent<EMSlider>();
            s.Knob = knob.transform;
            s.TrackStart = trackStart.transform;
            s.TrackEnd = trackEnd.transform;
            s.InitialValue = initialValue;
            s.Value = initialValue;
            return s;
        }

        // ----- Utility -----

        struct Materials
        {
            public Material AntennaBody;
            public Material AntennaAccent;
            public Material DishEmissive;
            public Material Handle;
            public Material Grip;
            public Material TargetMarker;

            public Material TabletFrame;
            public Material Screen;
            public Material ScreenAccent;

            public Material Track;
            public Material TrackTarget;
            public Material Knob;

            public Material LampOk;
            public Material LampBad;

            public Material LockMeterBg;
            public Material LockMeterFill;

            public Material WaveformLine;
        }

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
