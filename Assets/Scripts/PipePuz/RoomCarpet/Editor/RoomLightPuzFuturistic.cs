using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Stage3/Build RoomLightPuz Procedural Futuristic Visuals.
    ///
    /// RoomSeen/RoomLightPuz 안에 LightBeam + Carpet 퍼즐 요소들의 **procedural 미래지향 시각** 을
    /// 만든다. Unity primitive (Sphere/Cylinder/Cube) + HDR 발광 머티리얼 + Point Light + 파티클로
    /// 외부 에셋 없이 셀프 디자인.
    ///
    /// 실험용: 기능 wiring 없음. 시각만. 좌표는 RoomCliffSetup.cs 의 상수 기준으로 배치.
    ///
    /// 구성 (RoomLightPuz/FuturisticVisuals 하위):
    ///   LightOrb_Holo            — Sphere + 회전 토러스 ring 3개 + Point Light + 파티클
    ///   LightOrbSocket_Holo      — 환형 받침 + 동심 발광 ring + Point Light
    ///   LightOrbRest_Holo        — 얇은 hex 패드 + emissive 윤곽
    ///   LightBeamEmitter_Holo    — Hex 베이스 + 콘 배럴 + 코일 ring + 렌즈 sphere
    ///   LightBeamReceiver_Holo   — 동심 ring 패널 + 중앙 cube 크리스털
    ///   MirrorStand_Red/Green/Blue/Yellow_Holo — Hex 받침 + 프레임 4 thin cube + 색 panel
    ///   ColorOrderPanel_Holo     — 기울어진 패널 + 4 LED 슬롯 + 측면 발광 strip
    ///   BeamAimController_Holo   — 콘솔 + 발광 트랙 + 헥사 노브
    ///   CarpetDispenser_Holo     — 길쭉한 컬럼 + 상단 베이 + LED 인디케이터
    ///   CarpetLauncher_Holo      — 슬릭 직선 그립 + 머즐 ring
    ///   CliffPlatform_Holo × 5  — Hex 디스크 + 윗면 ring + 아래 Point Light
    ///
    /// 머티리얼 (Assets/PipePuz/RoomLightPuz/Materials/Holo_*.mat — 자동 생성):
    ///   Holo_DarkBase / DarkChrome / Chrome / AmberGlow / GoldGlow / RedGlow / GreenGlow / BlueGlow /
    ///   YellowGlow / OrangeGlow / WhiteGlow
    /// </summary>
    public static class RoomLightPuzFuturistic
    {
        const string TargetParent  = "RoomSeen";
        const string TargetName    = "RoomLightPuz";
        const string VisualsName   = "FuturisticVisuals";
        const string MatFolderPath = "Assets/PipePuz/RoomLightPuz/Materials";

        // 머티리얼 팔레트
        static Dictionary<string, Material> _palette;

        // 좌표 상수 (RoomCliffSetup.cs 와 동기화)
        const float Floor2Y = 3.5f;
        const float BeamY = 1.3f;
        const float MirrorPedestalTopY = 0.8f;

        // Mirror positions (X, Z), Y=0 받침대 바닥
        static readonly (Vector2 pos, string color, string matKey)[] Mirrors =
        {
            (new Vector2(-2f,  9f), "Red",    "Holo_RedGlow"),
            (new Vector2(-7f,  16f), "Green", "Holo_GreenGlow"),
            (new Vector2(-15f, 12f), "Blue",  "Holo_BlueGlow"),
            (new Vector2(-19f, 6f), "Yellow", "Holo_YellowGlow"),
        };

        // CliffPlatform (mirror 받침 + 진입 발판)
        static readonly Vector2[] Platforms =
        {
            new Vector2(-4f, 4f),   // EntryPlatform
            new Vector2(-2f, 9f),   // 거울 1 발판
            new Vector2(-7f, 16f),
            new Vector2(-15f, 12f),
            new Vector2(-19f, 6f),
        };

        // =========================================================================================
        // 메뉴: RoomCliff (Stage1 Skin) 의 시각 디자인을 RoomLightPuz 스타일로 교체.
        // 원본 GameObject / 스크립트 / 콜라이더 / 참조 그대로 유지. MeshRenderer 만 비활성화 후
        // 새 procedural 디자인을 자식으로 추가. 비파괴적 — Undo 가능, MeshRenderer 다시 켜면 원본 시각 복귀.
        //
        // 적용 대상:
        //   - LightOrbSocket  → 두 컬럼 containment 디자인
        //   - ColorOrderPanel → 매트 디스플레이 보드 + LED 슬롯
        //   - BeamAimController → 매트 콘솔 + T 슬라이더 핸들
        //   - Platforms/Platform_* → CliffPlatform 매트 hex disc 디자인
        //   - 각 Platform_Mirror* 의 MirrorN_<Color> 자식 → MirrorStand 디자인 (색 자동 추출)
        //
        // 주의: ColorOrderPanel 의 슬롯 색상 런타임 변경은 원본 MeshRenderer 가 비활성이라
        //       새 visual 에는 안 반영됨. 런타임 색 변경 보고 싶으면 원본 DisplaySlot 들의 MeshRenderer
        //       다시 켜고 새 visual LED 들을 비활성화하면 됨.
        // =========================================================================================

        // =========================================================================================
        // REVERT — 디자인 적용 전 상태로 복구. 원본 MeshRenderer 다시 켜고 추가된 *_Holo 자식 제거.
        // 사용 시: 디자인 적용했는데 시각/기능이 이상하면 이 메뉴로 깨끗하게 되돌림.
        // =========================================================================================

        [MenuItem("Tools/PipePuz/Stage3/Revert Futuristic Design from Stage1 Skin")]
        public static void RevertDesignFromStage1Skin()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            GameObject skin = FindByNameAnywhere(scene, "RoomCliff (Stage1 Skin)");
            if (skin == null)
            {
                Debug.LogError("[Revert] 'RoomCliff (Stage1 Skin)' 가 씬에 없다.");
                return;
            }

            Undo.SetCurrentGroupName("Revert Futuristic Design from Stage1 Skin");
            int undoGroup = Undo.GetCurrentGroup();
            int enabled = 0;
            int destroyed = 0;

            try
            {
                // 1. Stage1 Skin 트리 전체에서 비활성된 MeshRenderer 다시 켜기.
                var renderers = skin.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
                foreach (var r in renderers)
                {
                    if (r == null || r.enabled) continue;
                    Undo.RecordObject(r, "Re-enable original renderer");
                    r.enabled = true;
                    enabled++;
                }

                // 2. 추가된 *_Holo 자식 GameObject 모두 제거 (재귀).
                //    이름에 "_Holo" 가 포함된 것이 대상 — 사용자가 직접 만든 게 아니라면.
                var toDestroy = new System.Collections.Generic.List<GameObject>();
                CollectHoloObjects(skin.transform, toDestroy);
                foreach (var go in toDestroy)
                {
                    Undo.DestroyObjectImmediate(go);
                    destroyed++;
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Selection.activeGameObject = skin;
                Debug.Log($"[Revert] 완료. {enabled}개 MeshRenderer 다시 켜기 + {destroyed}개 *_Holo 자식 제거.\n" +
                          "Stage1 Skin 원본 상태로 복구. Ctrl+S 저장.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        static void CollectHoloObjects(Transform root, System.Collections.Generic.List<GameObject> result)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var ch = root.GetChild(i);
                if (ch.name.EndsWith("_Holo"))
                {
                    result.Add(ch.gameObject);
                }
                else
                {
                    CollectHoloObjects(ch, result);
                }
            }
        }

        [MenuItem("Tools/PipePuz/Stage3/Apply Futuristic Design to RoomCliff (Stage1 Skin)")]
        public static void ApplyDesignToStage1Skin()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            GameObject skin = FindByNameAnywhere(scene, "RoomCliff (Stage1 Skin)");
            if (skin == null)
            {
                Debug.LogError("[FuturisticSkin] 'RoomCliff (Stage1 Skin)' 가 씬에 없다. 먼저 만들어야 함.");
                return;
            }

            EnsurePalette();
            if (_palette == null) { Debug.LogError("[FuturisticSkin] 팔레트 생성 실패."); return; }

            Undo.SetCurrentGroupName("Apply Futuristic Design to Stage1 Skin");
            int undoGroup = Undo.GetCurrentGroup();
            int processed = 0;

            try
            {
                // ----- LightOrbSocket -----
                processed += ApplyElement(skin.transform, "LightOrbSocket",
                    (t) => BuildLightOrbSocket(t, Vector3.zero));

                // ----- ColorOrderPanel -----
                processed += ApplyElement(skin.transform, "ColorOrderPanel",
                    (t) => BuildColorOrderPanel(t, Vector3.zero));

                // ----- BeamAimController -----
                processed += ApplyElement(skin.transform, "BeamAimController",
                    (t) => BuildBeamAimController(t, Vector3.zero));

                // ----- Platforms (그룹) → 각 Platform_* 에 CliffPlatform 디자인 + Mirror 디자인 -----
                var platforms = FindChildByName(skin.transform, "Platforms");
                if (platforms != null)
                {
                    for (int i = 0; i < platforms.childCount; i++)
                    {
                        var platform = platforms.GetChild(i);
                        if (!platform.name.StartsWith("Platform_")) continue;

                        // 원본 visual 비활성화 (재귀, mirror 자식 포함)
                        DisableMeshRenderersIn(platform);
                        // 새 CliffPlatform 디자인
                        BuildCliffPlatform(platform, Vector3.zero);
                        processed++;

                        // Mirror 자식 처리 — 이름 패턴: Mirror1_Red, Mirror2_Green, ...
                        for (int j = 0; j < platform.childCount; j++)
                        {
                            var ch = platform.GetChild(j);
                            // CliffPlatform_Holo (방금 추가됨) 는 건너뜀
                            if (ch.name == "CliffPlatform_Holo") continue;
                            if (ch.name.StartsWith("Mirror"))
                            {
                                string color = ExtractMirrorColor(ch.name);
                                if (color != null)
                                {
                                    string matKey = $"Holo_{color}Glow";
                                    BuildMirrorStand(ch, Vector3.zero, color, matKey);
                                    processed++;
                                }
                            }
                        }
                    }
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Selection.activeGameObject = skin;
                EditorGUIUtility.PingObject(skin);

                Debug.Log($"[FuturisticSkin] 완료. {processed}개 요소에 procedural 디자인 적용.\n" +
                          "원본 MeshRenderer 는 모두 비활성화 — 다시 켜면 원본 시각 복귀.\n" +
                          "Ctrl+S 저장. 문제 시 Ctrl+Z.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        /// <summary>주어진 부모 안에서 자식 이름 매칭 → 원본 renderer 끄고 새 디자인 빌드.</summary>
        static int ApplyElement(Transform skinRoot, string childName, System.Action<Transform> builder)
        {
            var element = FindChildByName(skinRoot, childName);
            if (element == null)
            {
                Debug.LogWarning($"[FuturisticSkin] '{childName}' 못 찾음 — 스킵.");
                return 0;
            }
            DisableMeshRenderersIn(element.transform);
            builder(element.transform);
            return 1;
        }

        /// <summary>주어진 transform 트리의 모든 MeshRenderer 를 비활성화 (Undo 등록).</summary>
        static void DisableMeshRenderersIn(Transform root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                Undo.RecordObject(r, "Disable original renderer");
                r.enabled = false;
            }
        }

        /// <summary>"Mirror1_Red" / "Mirror2_Green" 등에서 색 이름 추출.</summary>
        static string ExtractMirrorColor(string name)
        {
            // 마지막 "_" 뒤가 색이라고 가정
            int idx = name.LastIndexOf('_');
            if (idx < 0 || idx + 1 >= name.Length) return null;
            string color = name.Substring(idx + 1);
            // 유효 색만 통과
            if (color == "Red" || color == "Green" || color == "Blue" || color == "Yellow")
                return color;
            return null;
        }

        static Transform FindChildByName(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            }
            return null;
        }

        [MenuItem("Tools/PipePuz/Stage3/Build RoomLightPuz Procedural Futuristic Visuals")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            // 1. RoomLightPuz 찾기 — 없으면 RoomSeen 자식으로 생성.
            GameObject roomLightPuz = FindByNameAnywhere(scene, TargetName);
            if (roomLightPuz == null)
            {
                GameObject roomSeen = FindByNameAnywhere(scene, TargetParent);
                if (roomSeen == null)
                {
                    Debug.LogError($"[Futuristic] '{TargetParent}' / '{TargetName}' 둘 다 없다. 먼저 RoomSeen 빌더 메뉴를 실행하라.");
                    return;
                }
                roomLightPuz = new GameObject(TargetName);
                Undo.RegisterCreatedObjectUndo(roomLightPuz, $"Create {TargetName}");
                SceneManager.MoveGameObjectToScene(roomLightPuz, scene);
                Undo.SetTransformParent(roomLightPuz.transform, roomSeen.transform,
                    worldPositionStays: false, $"Parent {TargetName}");
                roomLightPuz.transform.localPosition = Vector3.zero;
                Debug.Log($"[Futuristic] '{TargetName}' 가 없어서 RoomSeen 자식으로 새로 생성.");
            }

            // 2. RoomLightPuz 안의 모든 자식 삭제 (이전 빌드 잔재 / 중복 visual 제거).
            //    실험용이라 안에 있는 거 다 비우고 새로 깐다.
            int wipedChildren = 0;
            // 뒤에서부터 순회 — 자식 인덱스 변동 방지.
            for (int i = roomLightPuz.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(roomLightPuz.transform.GetChild(i).gameObject);
                wipedChildren++;
            }
            if (wipedChildren > 0)
                Debug.Log($"[Futuristic] '{TargetName}' 안의 기존 자식 {wipedChildren}개 삭제.");

            // 3. 머티리얼 팔레트 준비.
            EnsurePalette();
            if (_palette == null) { Debug.LogError("[Futuristic] 팔레트 생성 실패."); return; }

            Undo.SetCurrentGroupName("Build RoomLightPuz Procedural Futuristic Visuals");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                var visuals = CreateChild(roomLightPuz.transform, VisualsName);
                int totalParts = 0;

                // LightOrb (5층 좌표 3, 3.7, 12)
                totalParts += BuildLightOrb(visuals, new Vector3(3f, 3.7f, 12f));

                // LightOrbSocket (5, 3.5, 12)
                totalParts += BuildLightOrbSocket(visuals, new Vector3(5f, 3.5f, 12f));

                // LightOrbRest (3, 3.5, 12)
                totalParts += BuildLightOrbRest(visuals, new Vector3(3f, 3.5f, 12f));

                // LightBeamEmitter (1.4, 1.3, 10.5 추정)
                totalParts += BuildEmitter(visuals, new Vector3(1.4f, BeamY, 10.5f));

                // LightBeamReceiver (-10, 1.3, 3.3)
                totalParts += BuildReceiver(visuals, new Vector3(-10f, BeamY, 3.3f));

                // Mirror Stands × 4
                foreach (var m in Mirrors)
                {
                    totalParts += BuildMirrorStand(visuals, new Vector3(m.pos.x, 0f, m.pos.y), m.color, m.matKey);
                }

                // ColorOrderPanel (5.5, 3.5, 13)
                totalParts += BuildColorOrderPanel(visuals, new Vector3(5.5f, Floor2Y, 13f));

                // BeamAimController (3.5, 3.5, 13)
                totalParts += BuildBeamAimController(visuals, new Vector3(3.5f, Floor2Y, 13f));

                // CarpetDispenser (5.5, 3.5, 10.5)
                totalParts += BuildCarpetDispenser(visuals, new Vector3(5.5f, Floor2Y, 10.5f));

                // CarpetLauncher (2.2, 4.6, 10)
                totalParts += BuildCarpetLauncher(visuals, new Vector3(2.2f, 4.6f, 10f));

                // CliffPlatforms
                foreach (var p in Platforms)
                {
                    totalParts += BuildCliffPlatform(visuals, new Vector3(p.x, 0f, p.y));
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Selection.activeGameObject = visuals.gameObject;
                EditorGUIUtility.PingObject(visuals.gameObject);
                Debug.Log($"[Futuristic] 완료. RoomLightPuz/FuturisticVisuals 안에 {totalParts}개 part 생성.\n" +
                          "확인 후 Ctrl+S 저장. 문제 시 Ctrl+Z.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        // =========================================================================================
        // 퍼즐 요소별 builder
        // =========================================================================================

        static int BuildLightOrb(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "LightOrb_Holo");
            root.localPosition = localPos;

            // 코어 sphere
            n += AddPrimitive(root, PrimitiveType.Sphere, "Core",
                Vector3.zero, Quaternion.identity, Vector3.one * 0.16f, "Holo_WhiteGlow");

            // 외부 shell sphere (살짝 큼, semi-transparent 느낌 — 그냥 발광)
            n += AddPrimitive(root, PrimitiveType.Sphere, "OuterShell",
                Vector3.zero, Quaternion.identity, Vector3.one * 0.24f, "Holo_AmberGlow");

            // 3개의 토러스 ring (Cylinder 를 flat 스케일 + 회전으로 표현)
            float ringScale = 0.35f;
            n += AddPrimitive(root, PrimitiveType.Cylinder, "Ring_XY",
                Vector3.zero, Quaternion.Euler(0f, 0f, 0f),
                new Vector3(ringScale, 0.005f, ringScale), "Holo_AmberGlow");
            n += AddPrimitive(root, PrimitiveType.Cylinder, "Ring_XZ",
                Vector3.zero, Quaternion.Euler(90f, 0f, 0f),
                new Vector3(ringScale, 0.005f, ringScale), "Holo_AmberGlow");
            n += AddPrimitive(root, PrimitiveType.Cylinder, "Ring_YZ",
                Vector3.zero, Quaternion.Euler(0f, 0f, 90f),
                new Vector3(ringScale, 0.005f, ringScale), "Holo_AmberGlow");

            // (Point Light 제거 — 매트 SF 톤. 빛 효과 없음.)

            return n;
        }

        static int BuildLightOrbSocket(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "LightOrbSocket_Holo");
            root.localPosition = localPos;

            // ============ Containment 디자인 — 바닥에서 올라오는 기둥 + 천장에서 내려오는 기둥 ============
            // Orb dock 위치: 두 기둥 사이 중앙. 사용자가 LightOrb 를 가져와 여기에 놓음.
            //
            // 구조 (Y 기준, root=0 = socket 바닥):
            //   Y=0.00 ─ 바닥 platform (hex disc)
            //   Y=0.05~1.00 ─ 바닥 기둥 (rising column)
            //   Y=1.05 ─ 바닥 기둥 tip ring + 위로 향하는 에너지 빔
            //   Y=1.25 ─ DockPoint (LightOrb 가 들어갈 자리)
            //   Y=1.45 ─ 천장 기둥 tip ring + 아래로 향하는 에너지 빔
            //   Y=1.50~2.40 ─ 천장 기둥 (descending column)
            //   Y=2.40 ─ 천장 mount (hex disc)

            // 1. 바닥 platform (매트, ring 제거)
            n += AddPrimitive(root, PrimitiveType.Cylinder, "BasePlatform",
                new Vector3(0f, 0.025f, 0f), Quaternion.identity,
                new Vector3(0.5f, 0.05f, 0.5f), "Holo_DarkChrome");

            // 2. 바닥에서 올라오는 기둥 (높이 0.95m) — 측면 strip 제거, 깔끔
            n += AddPrimitive(root, PrimitiveType.Cylinder, "GroundColumn",
                new Vector3(0f, 0.525f, 0f), Quaternion.identity,
                new Vector3(0.22f, 0.475f, 0.22f), "Holo_DarkChrome");

            // 3. 바닥 기둥 tip ring + 위로 향하는 에너지 빔 (얇은 발광 실린더)
            n += AddPrimitive(root, PrimitiveType.Cylinder, "GroundTipRing",
                new Vector3(0f, 1.005f, 0f), Quaternion.identity,
                new Vector3(0.28f, 0.005f, 0.28f), "Holo_AmberGlow");
            n += AddPrimitive(root, PrimitiveType.Cylinder, "GroundBeam",
                new Vector3(0f, 1.125f, 0f), Quaternion.identity,
                new Vector3(0.04f, 0.12f, 0.04f), "Holo_YellowGlow");

            // 4. DockPoint — 빈 GameObject (LightOrb 위치 마커). 추후 LightOrbSocket 스크립트가 여기에 orb 부착.
            var dock = new GameObject("DockPoint");
            Undo.RegisterCreatedObjectUndo(dock, "Create DockPoint");
            dock.transform.SetParent(root, false);
            dock.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            n += 1;
            // Orb 들어갈 자리 시각 표시 (얇은 ring + dim sphere preview)
            n += AddPrimitive(root, PrimitiveType.Sphere, "OrbPreview",
                new Vector3(0f, 1.25f, 0f), Quaternion.identity,
                Vector3.one * 0.18f, "Holo_DarkBase");

            // 5. 천장 기둥 tip ring + 아래로 향하는 에너지 빔
            n += AddPrimitive(root, PrimitiveType.Cylinder, "CeilingBeam",
                new Vector3(0f, 1.375f, 0f), Quaternion.identity,
                new Vector3(0.04f, 0.12f, 0.04f), "Holo_YellowGlow");
            n += AddPrimitive(root, PrimitiveType.Cylinder, "CeilingTipRing",
                new Vector3(0f, 1.495f, 0f), Quaternion.identity,
                new Vector3(0.28f, 0.005f, 0.28f), "Holo_AmberGlow");

            // 6. 천장에서 내려오는 기둥 (측면 strip 제거)
            n += AddPrimitive(root, PrimitiveType.Cylinder, "CeilingColumn",
                new Vector3(0f, 1.975f, 0f), Quaternion.identity,
                new Vector3(0.22f, 0.475f, 0.22f), "Holo_DarkChrome");

            // 7. 천장 mount platform (매트, ring 제거)
            n += AddPrimitive(root, PrimitiveType.Cylinder, "CeilingMount",
                new Vector3(0f, 2.475f, 0f), Quaternion.identity,
                new Vector3(0.5f, 0.05f, 0.5f), "Holo_DarkChrome");

            // (Point Light 제거 — 매트 SF 톤)

            return n;
        }

        static int BuildLightOrbRest(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "LightOrbRest_Holo");
            root.localPosition = localPos;
            // 얇은 헥사 패드 (cylinder flat)
            n += AddPrimitive(root, PrimitiveType.Cylinder, "Pad",
                Vector3.zero, Quaternion.identity,
                new Vector3(0.22f, 0.01f, 0.22f), "Holo_DarkChrome");
            n += AddPrimitive(root, PrimitiveType.Cylinder, "RimGlow",
                new Vector3(0f, 0.011f, 0f), Quaternion.identity,
                new Vector3(0.2f, 0.003f, 0.2f), "Holo_AmberGlow");
            return n;
        }

        static int BuildEmitter(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "LightBeamEmitter_Holo");
            root.localPosition = localPos;
            // Hex 베이스
            n += AddPrimitive(root, PrimitiveType.Cylinder, "Base",
                new Vector3(0f, -0.2f, 0f), Quaternion.identity,
                new Vector3(0.3f, 0.04f, 0.3f), "Holo_DarkChrome");
            // 콘 바디 (cube 가 점차 좁아지는 느낌 — cube + scale)
            n += AddPrimitive(root, PrimitiveType.Cube, "Body",
                Vector3.zero, Quaternion.identity,
                new Vector3(0.35f, 0.3f, 0.25f), "Holo_DarkChrome");
            // 코일 ring 3개
            for (int i = 0; i < 3; i++)
            {
                n += AddPrimitive(root, PrimitiveType.Cylinder, $"Coil_{i}",
                    new Vector3(0f, 0.08f - i * 0.06f, 0f), Quaternion.Euler(0f, 0f, 90f),
                    new Vector3(0.13f, 0.01f, 0.13f), "Holo_OrangeGlow");
            }
            // 렌즈 (sphere 강 발광)
            n += AddPrimitive(root, PrimitiveType.Sphere, "Lens",
                new Vector3(0f, 0f, 0.18f), Quaternion.identity,
                Vector3.one * 0.1f, "Holo_YellowGlow");
            // (Point Light 제거)
            return n;
        }

        static int BuildReceiver(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "LightBeamReceiver_Holo");
            root.localPosition = localPos;
            // 외부 plate cylinder flat
            n += AddPrimitive(root, PrimitiveType.Cylinder, "Plate",
                Vector3.zero, Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.5f, 0.04f, 0.5f), "Holo_DarkChrome");
            // 중간 ring
            n += AddPrimitive(root, PrimitiveType.Cylinder, "MidRing",
                new Vector3(0f, 0f, -0.045f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.4f, 0.01f, 0.4f), "Holo_AmberGlow");
            // 중앙 크리스털
            n += AddPrimitive(root, PrimitiveType.Cube, "Crystal",
                new Vector3(0f, 0f, -0.05f), Quaternion.Euler(45f, 45f, 0f),
                Vector3.one * 0.25f, "Holo_WhiteGlow");
            return n;
        }

        static int BuildMirrorStand(Transform parent, Vector3 localPos, string colorName, string panelMatKey)
        {
            int n = 0;
            var root = CreateChild(parent, $"MirrorStand_{colorName}_Holo");
            root.localPosition = localPos;
            // Pedestal — hex 받침 (Cylinder)
            n += AddPrimitive(root, PrimitiveType.Cylinder, "Pedestal",
                new Vector3(0f, MirrorPedestalTopY * 0.5f, 0f), Quaternion.identity,
                new Vector3(0.24f, MirrorPedestalTopY * 0.5f, 0.24f), "Holo_DarkChrome");
            // Pedestal 윗 ring
            n += AddPrimitive(root, PrimitiveType.Cylinder, "PedestalTopRing",
                new Vector3(0f, MirrorPedestalTopY + 0.005f, 0f), Quaternion.identity,
                new Vector3(0.26f, 0.005f, 0.26f), panelMatKey);
            // Mirror frame — 4 thin cubes around (0.7 × 1.0 박스 윤곽)
            float frameW = 0.7f, frameH = 1.0f, frameT = 0.04f;
            float midY = MirrorPedestalTopY + BeamY - MirrorPedestalTopY; // = BeamY = 1.3
            // Top
            n += AddPrimitive(root, PrimitiveType.Cube, "Frame_Top",
                new Vector3(0f, midY + frameH * 0.5f, 0f), Quaternion.identity,
                new Vector3(frameW, frameT, frameT), "Holo_Chrome");
            // Bottom
            n += AddPrimitive(root, PrimitiveType.Cube, "Frame_Bottom",
                new Vector3(0f, midY - frameH * 0.5f, 0f), Quaternion.identity,
                new Vector3(frameW, frameT, frameT), "Holo_Chrome");
            // Left
            n += AddPrimitive(root, PrimitiveType.Cube, "Frame_Left",
                new Vector3(-frameW * 0.5f, midY, 0f), Quaternion.identity,
                new Vector3(frameT, frameH, frameT), "Holo_Chrome");
            // Right
            n += AddPrimitive(root, PrimitiveType.Cube, "Frame_Right",
                new Vector3(frameW * 0.5f, midY, 0f), Quaternion.identity,
                new Vector3(frameT, frameH, frameT), "Holo_Chrome");
            // Mirror panel (front emissive colored)
            n += AddPrimitive(root, PrimitiveType.Cube, "Panel_Front",
                new Vector3(0f, midY, 0.02f), Quaternion.identity,
                new Vector3(frameW - frameT * 2, frameH - frameT * 2, 0.02f), panelMatKey);
            // Mirror panel back (chrome)
            n += AddPrimitive(root, PrimitiveType.Cube, "Panel_Back",
                new Vector3(0f, midY, -0.02f), Quaternion.identity,
                new Vector3(frameW - frameT * 2, frameH - frameT * 2, 0.02f), "Holo_Chrome");
            return n;
        }

        static int BuildColorOrderPanel(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "ColorOrderPanel_Holo");
            root.localPosition = localPos;

            // ============ 클린한 시퀀스 디스플레이 ============
            // 안테나 / 떠있는 패널 제거. 받침대 → 컬럼 → 보드 + 4 LED 슬롯 — 명확한 1열.
            // 키 약 2m. 정면(+Z)에서 LED 4개가 좌→우 순서로 보이도록 배치.

            // 1. 받침대 platform (매트, ring 제거)
            n += AddPrimitive(root, PrimitiveType.Cylinder, "BasePlatform",
                new Vector3(0f, 0.08f, 0f), Quaternion.identity,
                new Vector3(1.4f, 0.08f, 1.4f), "Holo_DarkChrome");

            // 2. 메인 컬럼 (단일, 깔끔) — FrontStrip 제거
            n += AddPrimitive(root, PrimitiveType.Cube, "MainColumn",
                new Vector3(0f, 0.7f, 0f), Quaternion.identity,
                new Vector3(0.45f, 1.0f, 0.45f), "Holo_DarkChrome");

            // 3. 디스플레이 보드 (10° 뒤로) — 외곽 frame strip 제거. 깔끔한 매트 보드만.
            Quaternion boardRot = Quaternion.Euler(-10f, 0f, 0f);
            Vector3 boardCenter = new Vector3(0f, 1.45f, 0f);
            n += AddPrimitive(root, PrimitiveType.Cube, "MainBoard",
                boardCenter, boardRot, new Vector3(1.6f, 0.1f, 0.5f), "Holo_DarkBase");

            // 4. LED 슬롯 4개 — 발광. 얇게(0.02), 슬롯 크기는 유지(0.20).
            string[] colorKeys = { "Holo_RedGlow", "Holo_GreenGlow", "Holo_BlueGlow", "Holo_YellowGlow" };
            for (int i = 0; i < 4; i++)
            {
                float xOffset = -0.6f + i * 0.4f;
                Vector3 slotLocal = new Vector3(xOffset, 0.06f, 0.05f);

                // 슬롯 bezel (어두운 음각, 매트)
                n += AddPrimitive(root, PrimitiveType.Cube, $"LEDSlot_{i}_Bezel",
                    boardCenter + boardRot * slotLocal, boardRot,
                    new Vector3(0.3f, 0.03f, 0.3f), "Holo_DarkChrome");
                // 실제 LED (얇게 — Y=0.02, 작은 발광 면)
                n += AddPrimitive(root, PrimitiveType.Cube, $"LEDSlot_{i}",
                    boardCenter + boardRot * (slotLocal + new Vector3(0f, 0.02f, 0f)), boardRot,
                    new Vector3(0.20f, 0.02f, 0.20f), colorKeys[i]);
                // (인덱스 tick 제거 — 발광 strip 류라서 없앰)
            }

            // (Point Light 제거)

            return n;
        }

        static int BuildBeamAimController(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "BeamAimController_Holo");
            root.localPosition = localPos;

            // ============ 직관적 산업용 슬라이더 ============
            // 단일 큰 슬라이더 트랙 + 큰 T 핸들 + 방향 화살표 + 디스플레이.
            // 누가 봐도 "이 핸들을 좌우로 슬라이드해서 빔 각도를 조절한다" 가 즉시 보임.
            // 키 약 1.4m. 콘솔 정면(+Z) 을 향함.

            // 1. 받침대 platform (매트, ring 제거)
            n += AddPrimitive(root, PrimitiveType.Cylinder, "BasePlatform",
                new Vector3(0f, 0.08f, 0f), Quaternion.identity,
                new Vector3(1.2f, 0.08f, 1.2f), "Holo_DarkChrome");

            // 2. 단일 컬럼 (스템) — 정면 strip 제거
            n += AddPrimitive(root, PrimitiveType.Cube, "Stem",
                new Vector3(0f, 0.55f, 0f), Quaternion.identity,
                new Vector3(0.25f, 0.85f, 0.4f), "Holo_DarkChrome");

            // 3. 콘솔 본체 — 4면 frame strip 제거. 깔끔한 매트 콘솔.
            Vector3 conCenter = new Vector3(0f, 1.05f, 0.05f);
            n += AddPrimitive(root, PrimitiveType.Cube, "Console",
                conCenter, Quaternion.identity, new Vector3(1.4f, 0.15f, 0.5f), "Holo_DarkBase");

            // 4. 트랙 채널 — 음각 슬롯만 (양 측면 발광 strip 제거)
            n += AddPrimitive(root, PrimitiveType.Cube, "TrackChannel",
                conCenter + new Vector3(0f, 0.08f, 0f), Quaternion.identity,
                new Vector3(1.1f, 0.04f, 0.18f), "Holo_DarkBase");

            // 5. T 자형 핸들 — 그립 가능함 강조. 그립 위 strip 제거, 핸들 자체는 색으로 강조.
            Vector3 knobBase = conCenter + new Vector3(0f, 0.1f, 0f);
            // 핸들 하부
            n += AddPrimitive(root, PrimitiveType.Cube, "Knob_Slider",
                knobBase + new Vector3(0f, 0.04f, 0f), Quaternion.identity,
                new Vector3(0.16f, 0.08f, 0.16f), "Holo_OrangeGlow");
            // 핸들 stem (chrome)
            n += AddPrimitive(root, PrimitiveType.Cube, "Knob_Stem",
                knobBase + new Vector3(0f, 0.15f, 0f), Quaternion.identity,
                new Vector3(0.06f, 0.12f, 0.06f), "Holo_Chrome");
            // T 가로 그립 bar
            n += AddPrimitive(root, PrimitiveType.Cube, "Knob_Grip",
                knobBase + new Vector3(0f, 0.22f, 0f), Quaternion.identity,
                new Vector3(0.22f, 0.05f, 0.08f), "Holo_OrangeGlow");

            // 6. 트랙 양 끝에 화살표 마커 (≪ 좌, 우 ≫) — 방향성 강조
            // 왼쪽 화살표: 3개 작은 cube 로 < 모양
            float arrL = -0.62f;
            for (int i = 0; i < 3; i++)
            {
                float dx = i * 0.03f;
                n += AddPrimitive(root, PrimitiveType.Cube, $"ArrowL_{i}",
                    conCenter + new Vector3(arrL + dx, 0.09f, 0f + (i - 1) * 0.04f), Quaternion.identity,
                    new Vector3(0.015f, 0.005f, 0.04f), "Holo_GoldGlow");
            }
            // 오른쪽 화살표: > 모양
            float arrR = 0.62f;
            for (int i = 0; i < 3; i++)
            {
                float dx = -i * 0.03f;
                n += AddPrimitive(root, PrimitiveType.Cube, $"ArrowR_{i}",
                    conCenter + new Vector3(arrR + dx, 0.09f, 0f + (i - 1) * 0.04f), Quaternion.identity,
                    new Vector3(0.015f, 0.005f, 0.04f), "Holo_GoldGlow");
            }

            // 7. 상단 작은 디스플레이 패널 — "AIM" 같은 헤드업 디스플레이 (시각 표시)
            Vector3 dispCenter = new Vector3(0f, 1.4f, -0.05f);
            Quaternion dispRot = Quaternion.Euler(-20f, 0f, 0f);
            n += AddPrimitive(root, PrimitiveType.Cube, "Display_Bezel",
                dispCenter, dispRot, new Vector3(0.6f, 0.04f, 0.18f), "Holo_DarkChrome");
            n += AddPrimitive(root, PrimitiveType.Cube, "Display_Screen",
                dispCenter + dispRot * new Vector3(0f, 0.025f, 0f), dispRot,
                new Vector3(0.5f, 0.005f, 0.12f), "Holo_AmberGlow");

            // (Point Light 제거)

            return n;
        }

        static int BuildCarpetDispenser(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "CarpetDispenser_Holo");
            root.localPosition = localPos;
            // Column
            n += AddPrimitive(root, PrimitiveType.Cube, "Column",
                new Vector3(0f, 0.45f, 0f), Quaternion.identity,
                new Vector3(0.18f, 0.9f, 0.18f), "Holo_DarkChrome");
            // Top bay
            n += AddPrimitive(root, PrimitiveType.Cube, "Bay",
                new Vector3(0f, 0.95f, 0.05f), Quaternion.identity,
                new Vector3(0.3f, 0.08f, 0.15f), "Holo_DarkBase");
            // (측면 LED strip 제거)
            // 상단 spawn 지점에 작은 발광 점 1개만 (얇은 액센트)
            n += AddPrimitive(root, PrimitiveType.Sphere, "SpawnDot",
                new Vector3(0f, 0.95f, 0.12f), Quaternion.identity,
                Vector3.one * 0.04f, "Holo_AmberGlow");
            return n;
        }

        static int BuildCarpetLauncher(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "CarpetLauncher_Holo");
            root.localPosition = localPos;
            // Sleek body (slim cube)
            n += AddPrimitive(root, PrimitiveType.Cube, "Body",
                new Vector3(0f, 0f, 0.04f), Quaternion.identity,
                new Vector3(0.07f, 0.09f, 0.26f), "Holo_DarkChrome");
            // Grip
            n += AddPrimitive(root, PrimitiveType.Cube, "Grip",
                new Vector3(0f, -0.075f, -0.06f), Quaternion.Euler(15f, 0f, 0f),
                new Vector3(0.04f, 0.15f, 0.06f), "Holo_DarkBase");
            // (Grip emissive line strip 제거)
            // Muzzle ring (얇은 발광 — 발사 방향 표시)
            n += AddPrimitive(root, PrimitiveType.Cylinder, "MuzzleRing",
                new Vector3(0f, 0f, 0.16f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.06f, 0.01f, 0.06f), "Holo_AmberGlow");
            return n;
        }

        static int BuildCliffPlatform(Transform parent, Vector3 localPos)
        {
            int n = 0;
            var root = CreateChild(parent, "CliffPlatform_Holo");
            root.localPosition = localPos;
            // Hex disc (cylinder) — 매트
            n += AddPrimitive(root, PrimitiveType.Cylinder, "Disc",
                new Vector3(0f, -0.15f, 0f), Quaternion.identity,
                new Vector3(1.5f, 0.15f, 1.5f), "Holo_DarkChrome");
            // (윗면 ring strip 제거)
            // 중앙 작은 발광 점 1개 — platform 인식용
            n += AddPrimitive(root, PrimitiveType.Cylinder, "CenterDot",
                new Vector3(0f, 0.005f, 0f), Quaternion.identity,
                new Vector3(0.15f, 0.008f, 0.15f), "Holo_AmberGlow");
            return n;
        }

        // =========================================================================================
        // 헬퍼 — primitive 생성 + 머티리얼 swap
        // =========================================================================================

        static int AddPrimitive(Transform parent, PrimitiveType type, string name,
                                Vector3 localPos, Quaternion localRot, Vector3 localScale, string matKey)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            // Primitive 의 기본 콜라이더는 시각용이라 제거 (퍼즐 콜라이더는 RoomCliff Skin 의 것 사용).
            var coll = go.GetComponent<Collider>();
            if (coll != null) Object.DestroyImmediate(coll);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            // 머티리얼 적용
            var r = go.GetComponent<MeshRenderer>();
            if (r != null && _palette.TryGetValue(matKey, out var m) && m != null)
            {
                r.sharedMaterial = m;
            }
            return 1;
        }

        static int AddPointLight(Transform parent, string name, Vector3 localPos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create light {name}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None; // VR 성능 — 그림자 끔
            return 1;
        }

        static Transform CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        // =========================================================================================
        // 머티리얼 팔레트
        // =========================================================================================

        static void EnsurePalette()
        {
            _palette = new Dictionary<string, Material>();
            EnsureFolder(MatFolderPath);

            var defs = new (string Key, Color Base, Color Emis, float Metallic, float Smoothness)[]
            {
                // ============ 매트 베이스 + 차분한 LED 발광 (얇은 액센트에만) ============
                // 매트 SF 베이스 + 발광은 LED 인디케이터 정도 (subtle emission). HDR 폭발 없음.
                ("Holo_DarkBase",    new Color(0.13f, 0.13f, 0.15f), Color.black,                  0.20f, 0.40f),
                ("Holo_DarkChrome",  new Color(0.30f, 0.30f, 0.32f), Color.black,                  0.70f, 0.55f),
                ("Holo_Chrome",      new Color(0.82f, 0.82f, 0.85f), Color.black,                  1.00f, 0.90f),
                // 발광 인디케이터 — 차분한 LED 수준 (emission 1.5~2.0). 얇은 액센트용.
                ("Holo_AmberGlow",   new Color(0.80f, 0.50f, 0.15f), new Color(1.8f, 0.8f, 0.20f), 0.15f, 0.55f),
                ("Holo_GoldGlow",    new Color(0.85f, 0.70f, 0.25f), new Color(1.5f, 1.1f, 0.30f), 0.30f, 0.65f),
                ("Holo_RedGlow",     new Color(0.80f, 0.20f, 0.20f), new Color(2.0f, 0.2f, 0.20f), 0.15f, 0.55f),
                ("Holo_GreenGlow",   new Color(0.25f, 0.80f, 0.35f), new Color(0.4f, 1.9f, 0.55f), 0.15f, 0.55f),
                ("Holo_BlueGlow",    new Color(0.25f, 0.50f, 0.85f), new Color(0.4f, 0.8f, 1.9f),  0.15f, 0.55f),
                ("Holo_YellowGlow",  new Color(0.92f, 0.82f, 0.22f), new Color(1.8f, 1.5f, 0.25f), 0.15f, 0.55f),
                ("Holo_OrangeGlow",  new Color(0.92f, 0.50f, 0.15f), new Color(2.2f, 1.0f, 0.18f), 0.15f, 0.55f),
                ("Holo_WhiteGlow",   new Color(0.96f, 0.95f, 0.92f), new Color(1.8f, 1.7f, 1.4f),  0.10f, 0.75f),
            };

            foreach (var def in defs)
            {
                string path = $"{MatFolderPath}/{def.Key}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                Material mat;
                if (existing != null)
                {
                    // 기존 머티리얼이라도 색상 강제 갱신 — 팔레트 정의 변경 시 자동 반영.
                    mat = existing;
                    ApplyUrpLitColors(mat, def.Base, def.Emis, def.Metallic, def.Smoothness);
                    EditorUtility.SetDirty(mat);
                }
                else
                {
                    mat = CreateUrpLitMaterial(def.Key, def.Base, def.Emis, def.Metallic, def.Smoothness);
                    AssetDatabase.CreateAsset(mat, path);
                }
                _palette[def.Key] = mat;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static Material CreateUrpLitMaterial(string name, Color baseColor, Color emission, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            ApplyUrpLitColors(mat, baseColor, emission, metallic, smoothness);
            return mat;
        }

        /// <summary>이미 존재하는 머티리얼의 색/메탈/발광 갱신. 팔레트 정의 변경 시 자동 반영용.</summary>
        static void ApplyUrpLitColors(Material mat, Color baseColor, Color emission, float metallic, float smoothness)
        {
            if (mat == null) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_Metallic"))  mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (emission.maxColorComponent > 0.001f)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
            }
        }

        // =========================================================================================
        // Helpers
        // =========================================================================================

        static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string[] parts = folderPath.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cur}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(cur, parts[i]);
                }
                cur = next;
            }
        }

        static bool ValidateStage3(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[Futuristic] Active scene 이 유효하지 않다.");
                return false;
            }
            if (!scene.name.Contains("Stage3"))
            {
                if (!EditorUtility.DisplayDialog(
                        "Futuristic Design",
                        $"현재 active scene '{scene.name}' 이 Stage3 가 아닐 수 있다.\n계속할까?",
                        "계속", "취소"))
                    return false;
            }
            return true;
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

        static void EditorSceneManager_MarkActiveSceneDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
    }
}
