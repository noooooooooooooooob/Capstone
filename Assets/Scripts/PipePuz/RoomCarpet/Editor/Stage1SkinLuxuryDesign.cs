using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Stage3/Apply Luxury Design to Stage1 Skin Elements.
    ///
    /// RoomCliff (Stage1 Skin) 의 네 가지 퍼즐 요소의 시각을 호화롭게 교체:
    ///   - LightOrbSocket
    ///   - ColorOrderPanel
    ///   - BeamAimController
    ///   - Platforms/Platform_*  (entry + mirror platforms)
    ///
    /// 원칙:
    ///   - **인터랙티브 요소** (Knob, Mirror panel, DisplaySlot 등 — XR Grab / 색상 변화) 의 MeshRenderer 는 절대 disable X
    ///   - **Decorative** (Stand, Board, Tick, Pedestal, Platform Visual) 만 MeshRenderer disable + 위에 luxury 오버레이 추가
    ///   - 스크립트 / 콜라이더 / Transform 위치 / 참조 일체 손대지 않음
    ///
    /// 오버레이 GameObject 이름 모두 "*_Lux" 로 끝남 → Revert 메뉴에서 일괄 제거 가능.
    /// </summary>
    public static class Stage1SkinLuxuryDesign
    {
        const string DarkChromeMatPath = "Assets/PipePuz/RoomLightPuz/Materials/Holo_DarkChrome.mat";
        const string ChromeMatPath     = "Assets/PipePuz/RoomLightPuz/Materials/Holo_Chrome.mat";
        const string DarkBaseMatPath   = "Assets/PipePuz/RoomLightPuz/Materials/Holo_DarkBase.mat";
        const string AmberGlowMatPath  = "Assets/PipePuz/RoomLightPuz/Materials/Holo_AmberGlow.mat";
        const string GoldGlowMatPath   = "Assets/PipePuz/RoomLightPuz/Materials/Holo_GoldGlow.mat";

        static Material _darkChrome, _chrome, _darkBase, _amber, _gold;

        [MenuItem("Tools/PipePuz/Stage3/Apply Luxury Design to Stage1 Skin Elements")]
        public static void ApplyLuxury()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[Luxury] Active scene 무효.");
                return;
            }

            var skin = FindByNameAnywhere(scene, "RoomCliff (Stage1 Skin)");
            if (skin == null)
            {
                Debug.LogError("[Luxury] 'RoomCliff (Stage1 Skin)' 를 씬에서 못 찾았다.");
                return;
            }

            LoadMaterials();

            Undo.SetCurrentGroupName("Apply Luxury Design to Stage1 Skin Elements");
            int undoGroup = Undo.GetCurrentGroup();
            int processed = 0;

            try
            {
                // 1. LightOrbSocket
                var socket = FindChildByName(skin.transform, "LightOrbSocket");
                if (socket != null)
                {
                    DisableSpecificChildRenderers(socket, "Stand", "Bowl");
                    BuildLuxLightOrbSocket(socket);
                    WireOrbDockLEDController(socket);
                    processed++;
                }

                // 2. ColorOrderPanel — DisplaySlot_* 는 보존 (색상 변화 시각)
                var colorPanel = FindChildByName(skin.transform, "ColorOrderPanel");
                if (colorPanel != null)
                {
                    DisableSpecificChildRenderers(colorPanel,
                        "Stand", "Board",
                        "Tick_1", "Tick_2", "Tick_3", "Tick_4");
                    BuildLuxColorOrderPanel(colorPanel);
                    processed++;
                }

                // 3. BeamAimController — Knob 은 보존 (XR Grab)
                var beamAim = FindChildByName(skin.transform, "BeamAimController");
                if (beamAim != null)
                {
                    DisableSpecificChildRenderers(beamAim,
                        "Stand", "Track", "MarkerMin", "MarkerMax");
                    BuildLuxBeamAimController(beamAim);
                    processed++;
                }

                // 4. Platforms — 각 Platform_* 의 Visual 만 disable, Mirror 패널은 보존
                var platforms = FindChildByName(skin.transform, "Platforms");
                if (platforms != null)
                {
                    for (int i = 0; i < platforms.childCount; i++)
                    {
                        var p = platforms.GetChild(i);
                        if (!p.name.StartsWith("Platform_")) continue;

                        // Platform 의 "Visual" 자식만 disable
                        DisableSpecificChildRenderers(p, "Visual");
                        BuildLuxPlatform(p);
                        processed++;

                        // Mirror 자식이 있으면 그 Pedestal 만 disable (Front/Back/FrontIndicator 는 보존)
                        for (int j = 0; j < p.childCount; j++)
                        {
                            var ch = p.GetChild(j);
                            if (ch.name.StartsWith("Mirror") && !ch.name.EndsWith("_Lux"))
                            {
                                DisableSpecificChildRenderers(ch, "Pedestal");
                                BuildLuxMirrorPedestal(ch);
                                processed++;
                            }
                        }
                    }
                }

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                Selection.activeGameObject = skin;
                Debug.Log($"[Luxury] 완료. {processed}개 요소에 luxury 오버레이 적용.\n" +
                          "원본 GameObject/스크립트/인터랙티브 renderer 모두 보존.\n" +
                          "Revert: 'Tools/PipePuz/Stage3/Revert Luxury Design from Stage1 Skin' 또는 Ctrl+Z.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        // =========================================================================================
        // Revert 메뉴
        // =========================================================================================

        [MenuItem("Tools/PipePuz/Stage3/Revert Luxury Design from Stage1 Skin")]
        public static void RevertLuxury()
        {
            var scene = SceneManager.GetActiveScene();
            var skin = FindByNameAnywhere(scene, "RoomCliff (Stage1 Skin)");
            if (skin == null)
            {
                Debug.LogError("[Luxury Revert] 'RoomCliff (Stage1 Skin)' 못 찾음.");
                return;
            }

            Undo.SetCurrentGroupName("Revert Luxury Design");
            int undoGroup = Undo.GetCurrentGroup();
            int reEnabled = 0, destroyed = 0;

            try
            {
                // *_Lux 로 끝나는 모든 자식 제거
                var toDestroy = new List<GameObject>();
                CollectByNameSuffix(skin.transform, "_Lux", toDestroy);
                foreach (var go in toDestroy)
                {
                    Undo.DestroyObjectImmediate(go);
                    destroyed++;
                }

                // 비활성화된 MeshRenderer 모두 다시 enable
                var rs = skin.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in rs)
                {
                    if (r == null || r.enabled) continue;
                    Undo.RecordObject(r, "Re-enable renderer");
                    r.enabled = true;
                    reEnabled++;
                }

                // OrbDockLEDController 제거 (재실행 시 재추가)
                var socket = FindChildByName(skin.transform, "LightOrbSocket");
                if (socket != null)
                {
                    var ctrl = socket.GetComponent<PipePuz.LightBeam.OrbDockLEDController>();
                    if (ctrl != null) Undo.DestroyObjectImmediate(ctrl);

                    // DockPoint + SphereCollider 원위치 복원 (원본 값 Y=0.28)
                    var sock = socket.GetComponent<PipePuz.LightBeam.LightOrbSocket>();
                    if (sock != null && sock.DockPoint != null)
                    {
                        Undo.RecordObject(sock.DockPoint, "Restore DockPoint");
                        var lp = sock.DockPoint.localPosition;
                        lp.y = 0.28f;
                        sock.DockPoint.localPosition = lp;
                    }
                    var sc = socket.GetComponent<SphereCollider>();
                    if (sc != null)
                    {
                        Undo.RecordObject(sc, "Restore SphereCollider");
                        var center = sc.center;
                        center.y = 0.28f;
                        sc.center = center;
                    }
                }

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log($"[Luxury Revert] 완료. {reEnabled}개 renderer 재활성 + {destroyed}개 *_Lux 자식 제거 + DockPoint/SphereCollider 원위치 복원.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        // =========================================================================================
        // Luxury 디자인 빌더 — 각 요소
        // =========================================================================================

        /// <summary>
        /// LightOrbSocket — open framework 디자인 (4 corner bar, 안이 보임).
        /// 솔리드 cylinder 대신 4개 chrome bar 가 모서리에 — 안쪽 공간이 훤히 보임.
        /// 1m 완전 빈 gap, LED 평소 dark + 도킹 시 amber.
        ///
        /// LightOrbSocket parent world Y=4.07 가정 → local 좌표 변환:
        ///   Floor_SecondFloor world 3.5  → local Y = -0.57
        ///   DockPoint        world 4.35  → local Y =  0.28
        ///   챔버 천장         world 10   → local Y =  5.93
        ///
        /// 구조 (local Y):
        ///   -0.57 ~ -0.51   바닥 wide platter + gold rim
        ///   -0.51 ~ -0.07   ground column (sleek, 두꺼움 diam 0.5, 세로 flute 4)
        ///   -0.07 ~ -0.01   ground column wide cap (chrome flare + gold + amber LED)
        ///   -0.07 ~  0.63   ORB GAP 0.7m (DockPoint Y=0.28 중심)
        ///    0.63 ~  0.69   ceiling column wide cap (mirror)
        ///    0.69 ~  5.87   ceiling column (sleek, 세로 flute 4)
        ///    5.87 ~  5.93   천장 wide cap + gold rim
        /// </summary>
        static void BuildLuxLightOrbSocket(Transform parent)
        {
            var root = MakeOverlayRoot(parent, "LightOrbSocket_Lux");

            // ============ 좌표 설정 ============
            // 사용자 요청: 밑 bar 4배 길게, 위 platter 가 챔버 천장 닿음, 두 bar 그룹 사이 적당한 공간.
            // floorY = -0.45 (BasePlatter 바닥 위 안전).
            // ceilingY = 5.93 (챔버 천장 복원).
            // Bottom bars: 1.44m (0.36 × 4) — gap 까지 4배 길게.
            // gap: 1.5m (두 bar 그룹 사이 "적당한 공간").
            // Top bars: 자동 계산 — ceiling 부터 gap top 까지.
            // DockPoint 도 새 gap 중심으로 이동 (orb 가 진짜 중앙에 levitate).
            const float floorY    = -0.45f;
            const float ceilingY  =  5.93f;
            const float bottomBarLength = 1.44f;  // 4 × 0.36
            const float midGapHeight    = 1.50f;  // bar 그룹 사이 공간

            // Open framework — 4 corner bars + 다층 platter + 정밀 액센트
            const float barSpacing  = 0.22f;
            const float barWidthBot = 0.06f;  // base 쪽 살짝 굵음 (taper 효과)
            const float barWidthTop = 0.045f; // tip 쪽 살짝 가늘음

            // 다층 platter
            const float platterT1Diam = 0.70f;   // 최하단 (가장 큰 base ring)
            const float platterT2Diam = 0.56f;   // 중간 tier
            const float platterT3Diam = 0.44f;   // 상단 tier (bar 가 솟아나는 자리)
            const float platterT1H = 0.05f;
            const float platterT2H = 0.03f;
            const float platterT3H = 0.02f;
            float totalPlatterH = platterT1H + platterT2H + platterT3H; // 0.10m

            // bar 끝 액센트
            const float capH = 0.022f;
            const float capW = barWidthTop + 0.04f;

            Vector2[] corners = new Vector2[]
            {
                new Vector2( barSpacing,  barSpacing),
                new Vector2( barSpacing, -barSpacing),
                new Vector2(-barSpacing,  barSpacing),
                new Vector2(-barSpacing, -barSpacing),
            };

            // ============ 두 container 분리 — BottomFramework / TopFramework ============
            var bottomGo = new GameObject("BottomFramework");
            Undo.RegisterCreatedObjectUndo(bottomGo, "Create BottomFramework");
            bottomGo.transform.SetParent(root, false);
            var bottom = bottomGo.transform;

            var topGo = new GameObject("TopFramework");
            Undo.RegisterCreatedObjectUndo(topGo, "Create TopFramework");
            topGo.transform.SetParent(root, false);
            var top = topGo.transform;

            // ============ BOTTOM FRAMEWORK ============
            // 바닥 다층 platter (BottomFramework 안)
            // Tier1 (가장 큰 base ring, DarkChrome)
            AddCube_Cylinder(bottom, "BasePlatter_T1",
                new Vector3(0f, floorY + platterT1H * 0.5f, 0f),
                new Vector3(platterT1Diam, platterT1H, platterT1Diam), _darkChrome);
            // T1 위 gold rim
            if (_gold != null)
                AddCube_Cylinder(bottom, "BasePlatter_T1_GoldRim",
                    new Vector3(0f, floorY + platterT1H + 0.003f, 0f),
                    new Vector3(platterT1Diam + 0.02f, 0.005f, platterT1Diam + 0.02f), _gold);
            // Tier2 (Chrome 중간)
            AddCube_Cylinder(bottom, "BasePlatter_T2",
                new Vector3(0f, floorY + platterT1H + platterT2H * 0.5f, 0f),
                new Vector3(platterT2Diam, platterT2H, platterT2Diam), _chrome);
            // Tier3 (DarkBase 안쪽 — bar 가 솟아나는 자리)
            AddCube_Cylinder(bottom, "BasePlatter_T3",
                new Vector3(0f, floorY + platterT1H + platterT2H + platterT3H * 0.5f, 0f),
                new Vector3(platterT3Diam, platterT3H, platterT3Diam), _darkBase);

            // ============ 바닥에서 올라오는 open framework (4 bar, 4배 길어짐) ============
            float groundColBottom = floorY + totalPlatterH;
            float groundColTop    = groundColBottom + bottomBarLength;
            float groundColHeight = bottomBarLength;
            float groundColCenter = (groundColTop + groundColBottom) * 0.5f;
            // 새 gap 경계 (bottom bar top 부터)
            float gapBottom = groundColTop;
            float gapTop    = gapBottom + midGapHeight;

            for (int s = 0; s < 4; s++)
            {
                var c = corners[s];
                float avgBar = (barWidthBot + barWidthTop) * 0.5f;
                AddCube(bottom, $"GroundBar_{s}",
                    new Vector3(c.x, groundColCenter, c.y), Quaternion.identity,
                    new Vector3(avgBar, groundColHeight, avgBar), _chrome);
                if (_gold != null)
                {
                    float inset = avgBar * 0.5f + 0.002f;
                    Vector2 cn = c.normalized;
                    AddCube(bottom, $"GroundBar_{s}_GoldInlay",
                        new Vector3(c.x - cn.x * inset, groundColCenter, c.y - cn.y * inset),
                        Quaternion.Euler(0f, Mathf.Atan2(c.y, c.x) * Mathf.Rad2Deg, 0f),
                        new Vector3(0.005f, groundColHeight * 0.85f, 0.015f), _gold);
                }
                AddCube(bottom, $"GroundBar_{s}_Cap",
                    new Vector3(c.x, groundColTop - capH * 0.5f, c.y), Quaternion.identity,
                    new Vector3(capW, capH, capW), _chrome);
                var finial = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                finial.name = $"GroundBar_{s}_Finial";
                Undo.RegisterCreatedObjectUndo(finial, "Create finial");
                var fcol = finial.GetComponent<Collider>();
                if (fcol != null) Object.DestroyImmediate(fcol);
                finial.transform.SetParent(bottom, false);
                finial.transform.localPosition = new Vector3(c.x, groundColTop - capH - 0.015f, c.y);
                finial.transform.localScale = Vector3.one * 0.035f;
                if (_chrome != null) finial.GetComponent<MeshRenderer>().sharedMaterial = _chrome;
                AddCube(bottom, $"GroundBar_{s}_LED",
                    new Vector3(c.x, groundColTop - capH - 0.04f, c.y), Quaternion.identity,
                    new Vector3(barWidthTop + 0.01f, 0.004f, barWidthTop + 0.01f), _darkBase);
            }

            // ============ ORB GAP 1.5m (완전 빈 공간 — 새 DockPoint 중심) ============

            // ============ TOP FRAMEWORK — 천장 닿음 ============
            // 천장에서 내려오는 open framework (4 bar). ceiling 부터 gap top 까지 차지.
            float ceilColBottom = gapTop;
            float ceilColTop    = ceilingY - totalPlatterH;
            float ceilColHeight = ceilColTop - ceilColBottom;
            float ceilColCenter = (ceilColTop + ceilColBottom) * 0.5f;

            for (int s = 0; s < 4; s++)
            {
                var c = corners[s];
                float avgBar = (barWidthBot + barWidthTop) * 0.5f;
                AddCube(top, $"CeilingBar_{s}",
                    new Vector3(c.x, ceilColCenter, c.y), Quaternion.identity,
                    new Vector3(avgBar, ceilColHeight, avgBar), _chrome);
                if (_gold != null)
                {
                    float inset = avgBar * 0.5f + 0.002f;
                    Vector2 cn = c.normalized;
                    AddCube(top, $"CeilingBar_{s}_GoldInlay",
                        new Vector3(c.x - cn.x * inset, ceilColCenter, c.y - cn.y * inset),
                        Quaternion.Euler(0f, Mathf.Atan2(c.y, c.x) * Mathf.Rad2Deg, 0f),
                        new Vector3(0.005f, ceilColHeight * 0.95f, 0.015f), _gold);
                }
                AddCube(top, $"CeilingBar_{s}_Cap",
                    new Vector3(c.x, ceilColBottom + capH * 0.5f, c.y), Quaternion.identity,
                    new Vector3(capW, capH, capW), _chrome);
                var finial = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                finial.name = $"CeilingBar_{s}_Finial";
                Undo.RegisterCreatedObjectUndo(finial, "Create finial");
                var fcol = finial.GetComponent<Collider>();
                if (fcol != null) Object.DestroyImmediate(fcol);
                finial.transform.SetParent(top, false);
                finial.transform.localPosition = new Vector3(c.x, ceilColBottom + capH + 0.015f, c.y);
                finial.transform.localScale = Vector3.one * 0.035f;
                if (_chrome != null) finial.GetComponent<MeshRenderer>().sharedMaterial = _chrome;
                AddCube(top, $"CeilingBar_{s}_LED",
                    new Vector3(c.x, ceilColBottom + capH + 0.04f, c.y), Quaternion.identity,
                    new Vector3(barWidthTop + 0.01f, 0.004f, barWidthTop + 0.01f), _darkBase);
            }

            // ============ 천장 다층 platter (mirror of base, TopFramework 안) ============
            // Tier3 (Chrome 안쪽 — bar 가 매달리는 자리)
            AddCube_Cylinder(top, "CeilingPlatter_T3",
                new Vector3(0f, ceilingY - totalPlatterH + platterT3H * 0.5f, 0f),
                new Vector3(platterT3Diam, platterT3H, platterT3Diam), _darkBase);
            // Tier2 (Chrome 중간)
            AddCube_Cylinder(top, "CeilingPlatter_T2",
                new Vector3(0f, ceilingY - platterT1H - platterT2H * 0.5f, 0f),
                new Vector3(platterT2Diam, platterT2H, platterT2Diam), _chrome);
            // T1 아래 gold rim
            if (_gold != null)
                AddCube_Cylinder(top, "CeilingPlatter_T1_GoldRim",
                    new Vector3(0f, ceilingY - platterT1H - 0.003f, 0f),
                    new Vector3(platterT1Diam + 0.02f, 0.005f, platterT1Diam + 0.02f), _gold);
            // Tier1 (가장 큰 ring, DarkChrome)
            AddCube_Cylinder(top, "CeilingPlatter_T1",
                new Vector3(0f, ceilingY - platterT1H * 0.5f, 0f),
                new Vector3(platterT1Diam, platterT1H, platterT1Diam), _darkChrome);

            // ============ DockPoint + SphereCollider 새 gap 중앙으로 이동 ============
            // 두 bar 그룹 사이의 진짜 중간에 orb 가 levitate 하도록.
            float newDockY = (gapBottom + gapTop) * 0.5f;
            MoveDockAndTrigger(parent, newDockY);
        }

        /// <summary>
        /// LightOrbSocket 의 DockPoint Transform + SphereCollider center 를 새 Y 로 이동.
        /// orb 가 실제로 새 gap 중앙에 levitate 하게.
        /// </summary>
        static void MoveDockAndTrigger(Transform socketParent, float newY)
        {
            // 1. DockPoint Transform 찾기 (LightOrbSocket script 의 reference)
            var sock = socketParent.GetComponent<PipePuz.LightBeam.LightOrbSocket>();
            if (sock != null && sock.DockPoint != null)
            {
                Undo.RecordObject(sock.DockPoint, "Move DockPoint to gap center");
                var lp = sock.DockPoint.localPosition;
                lp.y = newY;
                sock.DockPoint.localPosition = lp;
            }

            // 2. SphereCollider center.y 이동 (radius 유지)
            var sc = socketParent.GetComponent<SphereCollider>();
            if (sc != null)
            {
                Undo.RecordObject(sc, "Move SphereCollider center");
                var center = sc.center;
                center.y = newY;
                sc.center = center;
            }

            Debug.Log($"[Luxury] DockPoint + Trigger 가 새 gap 중앙(Y={newY:F2}) 으로 이동.");
        }

        /// <summary>
        /// LightOrbSocket 의 OnOrbInserted/Removed 에 OrbDockLEDController 자동 wire.
        /// LightOrbSocket_Lux 안의 *_TopLED / *_BotLED MeshRenderer 를 LED 리스트로 등록.
        /// Off=DarkBase / On=AmberGlow 로 swap.
        /// </summary>
        static void WireOrbDockLEDController(Transform socketParent)
        {
            var socket = socketParent.GetComponent<PipePuz.LightBeam.LightOrbSocket>();
            if (socket == null)
            {
                Debug.LogWarning("[Luxury] LightOrbSocket script 없음 — LED 컨트롤러 wire 스킵.");
                return;
            }
            var lux = FindChildByName(socketParent, "LightOrbSocket_Lux");
            if (lux == null) return;

            // 이미 컨트롤러 있으면 제거 후 재추가 (재실행 안전)
            var existingCtrl = socketParent.GetComponent<PipePuz.LightBeam.OrbDockLEDController>();
            if (existingCtrl != null) Undo.DestroyObjectImmediate(existingCtrl);

            var ctrl = Undo.AddComponent<PipePuz.LightBeam.OrbDockLEDController>(socketParent.gameObject);
            ctrl.Socket = socket;
            ctrl.OffMaterial = _darkBase;
            ctrl.OnMaterial = _amber;

            // *_LED 라는 이름 끝나는 MeshRenderer 들 수집
            var leds = new System.Collections.Generic.List<MeshRenderer>();
            CollectLEDRenderers(lux, leds);
            ctrl.LEDs = leds;
            EditorUtility.SetDirty(ctrl);

            Debug.Log($"[Luxury] OrbDockLEDController wire 완료 — LED {leds.Count}개 (평소 dark, orb 도킹 시 amber).");
        }

        static void CollectLEDRenderers(Transform root, System.Collections.Generic.List<MeshRenderer> result)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var ch = root.GetChild(i);
                if (ch.name.EndsWith("_LED"))
                {
                    var r = ch.GetComponent<MeshRenderer>();
                    if (r != null) result.Add(r);
                }
                CollectLEDRenderers(ch, result);
            }
        }

        /// <summary>ColorOrderPanel — 다단 받침대 + 호화 컬럼 + 액자 frame (DisplaySlot 은 안 건드림).</summary>
        static void BuildLuxColorOrderPanel(Transform parent)
        {
            var root = MakeOverlayRoot(parent, "ColorOrderPanel_Lux");

            // 받침대 3단
            AddCube_Cylinder(root, "Base_T1",
                new Vector3(0f, 0.04f, 0f), new Vector3(0.55f, 0.08f, 0.55f), _darkChrome);
            AddCube_Cylinder(root, "Base_T2",
                new Vector3(0f, 0.10f, 0f), new Vector3(0.4f, 0.04f, 0.4f), _chrome);
            if (_gold != null)
            {
                AddCube_Cylinder(root, "Base_GoldRing",
                    new Vector3(0f, 0.085f, 0f), new Vector3(0.50f, 0.005f, 0.50f), _gold);
            }

            // 수직 컬럼 — 보드 받침
            AddCube(root, "Column",
                new Vector3(0f, 0.36f, 0f), Quaternion.identity,
                new Vector3(0.18f, 0.45f, 0.18f), _darkChrome);
            // 컬럼 정면 chrome stripe
            AddCube(root, "Column_FrontStripe",
                new Vector3(0f, 0.36f, 0.10f), Quaternion.identity,
                new Vector3(0.05f, 0.4f, 0.005f), _chrome);

            // 보드 base + frame (DisplaySlot 들은 보존 — 그 사이에 frame 두름)
            // 원본 보드 위치 추정: Y=0.45 부근, 가로 0.55
            float boardY = 0.62f;
            AddCube(root, "Board_Base",
                new Vector3(0f, boardY, 0f), Quaternion.identity,
                new Vector3(0.7f, 0.06f, 0.28f), _darkBase);

            // Chrome frame around board (top/bot/L/R)
            float bft = 0.025f;
            AddCube(root, "Board_FrameTop",
                new Vector3(0f, boardY + 0.04f, 0.142f), Quaternion.identity,
                new Vector3(0.74f, bft, bft), _chrome);
            AddCube(root, "Board_FrameBot",
                new Vector3(0f, boardY + 0.04f, -0.142f), Quaternion.identity,
                new Vector3(0.74f, bft, bft), _chrome);
            AddCube(root, "Board_FrameL",
                new Vector3(-0.357f, boardY + 0.04f, 0f), Quaternion.identity,
                new Vector3(bft, bft, 0.28f), _chrome);
            AddCube(root, "Board_FrameR",
                new Vector3(0.357f, boardY + 0.04f, 0f), Quaternion.identity,
                new Vector3(bft, bft, 0.28f), _chrome);

            // 상단 amber strip
            if (_amber != null)
            {
                AddCube(root, "TopAmberStrip",
                    new Vector3(0f, boardY + 0.075f, 0f), Quaternion.identity,
                    new Vector3(0.5f, 0.01f, 0.02f), _amber);
            }
        }

        /// <summary>BeamAimController — 호화 콘솔 (Knob 은 보존).</summary>
        static void BuildLuxBeamAimController(Transform parent)
        {
            var root = MakeOverlayRoot(parent, "BeamAimController_Lux");

            // 받침대 3단
            AddCube_Cylinder(root, "Base_T1",
                new Vector3(0f, 0.04f, 0f), new Vector3(0.5f, 0.08f, 0.5f), _darkChrome);
            AddCube_Cylinder(root, "Base_T2",
                new Vector3(0f, 0.10f, 0f), new Vector3(0.36f, 0.04f, 0.36f), _chrome);
            if (_gold != null)
            {
                AddCube_Cylinder(root, "Base_GoldRing",
                    new Vector3(0f, 0.085f, 0f), new Vector3(0.46f, 0.005f, 0.46f), _gold);
            }

            // 수직 컬럼
            AddCube(root, "Column",
                new Vector3(0f, 0.35f, 0f), Quaternion.identity,
                new Vector3(0.16f, 0.45f, 0.16f), _darkChrome);
            AddCube(root, "Column_FrontStripe",
                new Vector3(0f, 0.35f, 0.085f), Quaternion.identity,
                new Vector3(0.04f, 0.4f, 0.005f), _chrome);

            // 콘솔 본체 (Knob 위치 부근) — 트랙은 원본 보존
            float consY = 0.60f;
            AddCube(root, "Console_Base",
                new Vector3(0f, consY, 0f), Quaternion.identity,
                new Vector3(0.8f, 0.06f, 0.22f), _darkBase);

            // Chrome frame 4면
            float cft = 0.02f;
            AddCube(root, "Console_FrameTop",
                new Vector3(0f, consY + 0.04f, 0.11f), Quaternion.identity,
                new Vector3(0.84f, cft, cft), _chrome);
            AddCube(root, "Console_FrameBot",
                new Vector3(0f, consY + 0.04f, -0.11f), Quaternion.identity,
                new Vector3(0.84f, cft, cft), _chrome);
            AddCube(root, "Console_FrameL",
                new Vector3(-0.41f, consY + 0.04f, 0f), Quaternion.identity,
                new Vector3(cft, cft, 0.22f), _chrome);
            AddCube(root, "Console_FrameR",
                new Vector3(0.41f, consY + 0.04f, 0f), Quaternion.identity,
                new Vector3(cft, cft, 0.22f), _chrome);

            // 양 끝 amber arrow 마커
            if (_amber != null)
            {
                AddCube(root, "ArrowL",
                    new Vector3(-0.36f, consY + 0.045f, 0f), Quaternion.identity,
                    new Vector3(0.03f, 0.006f, 0.07f), _amber);
                AddCube(root, "ArrowR",
                    new Vector3(0.36f, consY + 0.045f, 0f), Quaternion.identity,
                    new Vector3(0.03f, 0.006f, 0.07f), _amber);
            }
        }

        /// <summary>
        /// Platform — 이전의 더 풍성한 디자인 (Tier1+Tier2+ChromeRing+GoldRing+CenterMarker).
        /// root 의 localPosition.y = -0.43 으로 내려서 collider top (Y=-0.13 in platform local) 과 정렬.
        /// (원본 Visual cube 는 platform local Y=-0.43 중심, scale 0.6 → top Y=-0.13)
        /// </summary>
        static void BuildLuxPlatform(Transform parent)
        {
            var root = MakeOverlayRoot(parent, "Platform_Lux");
            // ★ 핵심: root 자체를 Y=-0.43 으로 내려 collider 상단과 visual 상단 일치
            root.localPosition = new Vector3(0f, -0.43f, 0f);

            // 2단 hex disc (이전 디자인 복원)
            AddCube_Cylinder(root, "Tier1",
                new Vector3(0f, 0.15f, 0f), new Vector3(1.55f, 0.3f, 1.55f), _darkChrome);
            AddCube_Cylinder(root, "Tier2",
                new Vector3(0f, 0.31f, 0f), new Vector3(1.4f, 0.02f, 1.4f), _darkBase);

            // 가장자리 chrome ring
            AddCube_Cylinder(root, "ChromeRingTop",
                new Vector3(0f, 0.31f, 0f), new Vector3(1.5f, 0.012f, 1.5f), _chrome);

            // 외곽 gold ring (premium)
            if (_gold != null)
            {
                AddCube_Cylinder(root, "GoldRing",
                    new Vector3(0f, 0.30f, 0f), new Vector3(1.58f, 0.006f, 1.58f), _gold);
            }

            // 중심 작은 amber 마커
            if (_amber != null)
            {
                AddCube_Cylinder(root, "CenterMarker",
                    new Vector3(0f, 0.317f, 0f), new Vector3(0.18f, 0.005f, 0.18f), _amber);
            }
        }

        /// <summary>
        /// Mirror Pedestal — 거울 받침대. root Y=-0.43 으로 내려 Platform 변경에 맞춤.
        /// (Platform_Lux 가 -0.43 내려갔으므로 그 위에 서 있는 pedestal 도 동일 offset)
        /// </summary>
        static void BuildLuxMirrorPedestal(Transform parent)
        {
            var root = MakeOverlayRoot(parent, "MirrorPedestal_Lux");
            // ★ Platform_Lux 와 동일한 offset
            root.localPosition = new Vector3(0f, -0.43f, 0f);

            // 3단 받침대 (wrapper Y=0 기준 -0.8 ~ 0)
            AddCube_Cylinder(root, "Pedestal_Base",
                new Vector3(0f, -0.7f, 0f), new Vector3(0.32f, 0.2f, 0.32f), _darkChrome);
            AddCube_Cylinder(root, "Pedestal_Mid",
                new Vector3(0f, -0.42f, 0f), new Vector3(0.22f, 0.35f, 0.22f), _darkBase);
            AddCube_Cylinder(root, "Pedestal_Top",
                new Vector3(0f, -0.08f, 0f), new Vector3(0.26f, 0.04f, 0.26f), _chrome);

            if (_gold != null)
            {
                AddCube_Cylinder(root, "GoldRing",
                    new Vector3(0f, -0.59f, 0f), new Vector3(0.34f, 0.005f, 0.34f), _gold);
            }
        }

        // =========================================================================================
        // 헬퍼
        // =========================================================================================

        static Transform MakeOverlayRoot(Transform parent, string name)
        {
            // 이미 있으면 자식 삭제 후 재사용
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == name)
                {
                    Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
                    break;
                }
            }
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        static void DisableSpecificChildRenderers(Transform parent, params string[] childNames)
        {
            var names = new HashSet<string>(childNames);
            for (int i = 0; i < parent.childCount; i++)
            {
                var ch = parent.GetChild(i);
                if (!names.Contains(ch.name)) continue;
                var rs = ch.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in rs)
                {
                    if (r == null || !r.enabled) continue;
                    Undo.RecordObject(r, "Disable decorative renderer");
                    r.enabled = false;
                }
            }
        }

        static void AddCube_Cylinder(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void AddCube(Transform parent, string name, Vector3 localPos, Quaternion localRot, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void LoadMaterials()
        {
            _darkChrome = AssetDatabase.LoadAssetAtPath<Material>(DarkChromeMatPath);
            _chrome     = AssetDatabase.LoadAssetAtPath<Material>(ChromeMatPath);
            _darkBase   = AssetDatabase.LoadAssetAtPath<Material>(DarkBaseMatPath);
            _amber      = AssetDatabase.LoadAssetAtPath<Material>(AmberGlowMatPath);
            _gold       = AssetDatabase.LoadAssetAtPath<Material>(GoldGlowMatPath);
            if (_darkChrome == null || _chrome == null)
                Debug.LogWarning("[Luxury] 일부 머티리얼 없음 — RoomLightPuz 빌드 한 번 돌리면 팔레트 자동 생성.");
        }

        static GameObject FindByNameAnywhere(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = SearchByName(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        static Transform SearchByName(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = SearchByName(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static Transform FindChildByName(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            return null;
        }

        static void CollectByNameSuffix(Transform root, string suffix, List<GameObject> result)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var ch = root.GetChild(i);
                if (ch.name.EndsWith(suffix))
                    result.Add(ch.gameObject);
                else
                    CollectByNameSuffix(ch, suffix, result);
            }
        }
    }
}
