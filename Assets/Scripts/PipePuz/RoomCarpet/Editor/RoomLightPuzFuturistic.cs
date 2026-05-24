using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Stage3/Build RoomLightPuz (Futuristic Redesign).
    ///
    /// RoomCliff (Stage1 Skin) 의 13개 퍼즐 자식 + CliffController 컴포넌트를
    /// RoomLightPuz 안에 복제하되 — **각 시각 요소의 머티리얼을 미래지향 팔레트로 swap.**
    /// LightOrb 의 Cube 메쉬도 Sphere 메쉬로 교체.
    ///
    /// 결과: 모든 퍼즐 기능은 그대로 작동(스크립트/콜라이더/참조 보존), 시각은 다크 + 네온 발광
    /// 사이파이 톤으로 통합.
    ///
    /// 머티리얼 팔레트 (Assets/PipePuz/RoomLightPuz/Materials/Holo_*.mat 로 자동 생성):
    ///   Holo_DarkBase     — 어두운 무광 (베이스 패널)
    ///   Holo_DarkChrome   — 어두운 메탈 (받침대/프레임)
    ///   Holo_Chrome       — 밝은 크롬 (거울 뒷면, 총기 금속)
    ///   Holo_CyanGlow     — 시안 네온 발광
    ///   Holo_RedGlow      — 적 네온
    ///   Holo_GreenGlow    — 녹 네온 (Mirror Green, Entry platform)
    ///   Holo_BlueGlow     — 청 네온
    ///   Holo_YellowGlow   — 황 네온 (Emitter lens, Mirror Yellow)
    ///   Holo_OrangeGlow   — 오렌지 네온 (Knob, Indicator, GunAccent)
    ///   Holo_WhiteGlow    — 백색 강발광 (LightOrb)
    ///
    /// 매핑 (원본 머티리얼 이름 → 미래풍):
    ///   Cliff_StandMat / PedestalMat / EmitterFrameMat / GunGripMat → Holo_DarkChrome
    ///   Cliff_PlatformMat / SlotMat_*  / ReceiverPlateMat           → Holo_DarkBase
    ///   Cliff_EntryPlatformMat                                       → Holo_GreenGlow
    ///   Cliff_GunMetalMat / MirrorBackMat                            → Holo_Chrome
    ///   Cliff_LightOrbMat                                            → Holo_WhiteGlow
    ///   Cliff_EmitterLensMat / MirrorFaceMat_Yellow                  → Holo_YellowGlow
    ///   Cliff_GunAccentMat / KnobMat / IndicatorMat                  → Holo_OrangeGlow
    ///   Cliff_OrbSocketBowlMat                                       → Holo_CyanGlow
    ///   Cliff_MirrorFaceMat_Red    → Holo_RedGlow
    ///   Cliff_MirrorFaceMat_Green  → Holo_GreenGlow
    ///   Cliff_MirrorFaceMat_Blue   → Holo_BlueGlow
    ///
    /// 사전조건:
    ///   - Stage3 active scene
    ///   - 'RoomCliff (Stage1 Skin)' 존재 (어디든)
    ///   - 'RoomLightPuz' 존재 (RoomSeen 안 권장)
    ///   - RoomLightPuz 가 비어있어야 함 (자식 0)
    /// </summary>
    public static class RoomLightPuzFuturistic
    {
        const string TargetName     = "RoomLightPuz";
        const string SourceSkinName = "RoomCliff (Stage1 Skin)";
        const string MatFolderPath  = "Assets/PipePuz/RoomLightPuz/Materials";

        // 머티리얼 팔레트 (런타임 빌드 후 캐시)
        static Dictionary<string, Material> _palette;
        // 원본 머티리얼 이름 → 미래풍 머티리얼 키 매핑
        static readonly Dictionary<string, string> NameMap = new()
        {
            { "Cliff_StandMat",            "Holo_DarkChrome" },
            { "Cliff_PedestalMat",         "Holo_DarkChrome" },
            { "Cliff_EmitterFrameMat",     "Holo_DarkChrome" },
            { "Cliff_GunGripMat",          "Holo_DarkChrome" },
            { "Cliff_PlatformMat",         "Holo_DarkBase" },
            { "Cliff_ReceiverPlateMat",    "Holo_DarkBase" },
            { "Cliff_SlotMat_0",           "Holo_DarkBase" },
            { "Cliff_SlotMat_1",           "Holo_DarkBase" },
            { "Cliff_SlotMat_2",           "Holo_DarkBase" },
            { "Cliff_SlotMat_3",           "Holo_DarkBase" },
            { "Cliff_EntryPlatformMat",    "Holo_GreenGlow" },
            { "Cliff_GunMetalMat",         "Holo_Chrome" },
            { "Cliff_MirrorBackMat",       "Holo_Chrome" },
            { "Cliff_LightOrbMat",         "Holo_WhiteGlow" },
            { "Cliff_EmitterLensMat",      "Holo_YellowGlow" },
            { "Cliff_GunAccentMat",        "Holo_OrangeGlow" },
            { "Cliff_KnobMat",             "Holo_OrangeGlow" },
            { "Cliff_IndicatorMat",        "Holo_OrangeGlow" },
            { "Cliff_OrbSocketBowlMat",    "Holo_CyanGlow" },
            { "Cliff_MirrorFaceMat_Red",   "Holo_RedGlow" },
            { "Cliff_MirrorFaceMat_Green", "Holo_GreenGlow" },
            { "Cliff_MirrorFaceMat_Blue",  "Holo_BlueGlow" },
            { "Cliff_MirrorFaceMat_Yellow","Holo_YellowGlow" },
        };

        [MenuItem("Tools/PipePuz/Stage3/Build RoomLightPuz (Futuristic Redesign)")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            GameObject roomLightPuz = FindAnywhere(scene, TargetName);
            if (roomLightPuz == null)
            {
                Debug.LogError($"[FutRedesign] '{TargetName}' 가 씬에 없다.");
                return;
            }
            if (roomLightPuz.transform.childCount > 0)
            {
                Debug.LogError($"[FutRedesign] '{TargetName}' 가 이미 자식 {roomLightPuz.transform.childCount}개를 가지고 있다. 비우고 재실행하라.");
                Selection.activeGameObject = roomLightPuz;
                return;
            }

            GameObject sourceSkin = FindAnywhere(scene, SourceSkinName);
            if (sourceSkin == null)
            {
                Debug.LogError($"[FutRedesign] '{SourceSkinName}' 를 찾을 수 없다. 먼저 'Build or Update RoomSeen' 메뉴로 만들어야 함.");
                return;
            }

            // 1. 미래풍 머티리얼 팔레트 준비 (없으면 자동 생성).
            EnsurePalette();
            if (_palette == null || _palette.Count == 0)
            {
                Debug.LogError("[FutRedesign] 머티리얼 팔레트 생성 실패.");
                return;
            }

            Undo.SetCurrentGroupName("Build RoomLightPuz Futuristic Redesign");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                // 2. Stage1 Skin 자식 13개 모두 복제 → RoomLightPuz 직접 자식으로.
                int copied = 0;
                int swappedMats = 0;
                int orbMeshChanged = 0;

                int childCount = sourceSkin.transform.childCount;
                Transform[] toCopy = new Transform[childCount];
                for (int i = 0; i < childCount; i++) toCopy[i] = sourceSkin.transform.GetChild(i);

                Mesh sphereMesh = GetBuiltinSphereMesh();

                foreach (var child in toCopy)
                {
                    if (child == null) continue;
                    var copy = Object.Instantiate(child.gameObject);
                    copy.name = child.name;
                    Undo.RegisterCreatedObjectUndo(copy, $"Duplicate {child.name}");
                    if (copy.scene != scene) SceneManager.MoveGameObjectToScene(copy, scene);
                    Undo.SetTransformParent(copy.transform, roomLightPuz.transform,
                        worldPositionStays: false, "Parent puzzle child");
                    copy.transform.localPosition = child.localPosition;
                    copy.transform.localRotation = child.localRotation;
                    copy.transform.localScale    = child.localScale;

                    // 3. 모든 자식 MeshRenderer 의 머티리얼 → 미래풍 swap.
                    swappedMats += SwapMaterialsRecursive(copy);

                    // 4. LightOrb 자체 또는 그 자식 중 cube mesh 인 것 → sphere 로 (이름 매칭).
                    if (copy.name == "LightOrb" && sphereMesh != null)
                    {
                        var mf = copy.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.ToLower().Contains("cube"))
                        {
                            Undo.RecordObject(mf, "LightOrb mesh → Sphere");
                            mf.sharedMesh = sphereMesh;
                            orbMeshChanged++;
                        }
                    }
                    copied++;
                }

                // 5. 부모 컴포넌트 (CliffController 등) 도 복사 — Transform 제외.
                int copiedComps = 0;
                var srcComponents = sourceSkin.GetComponents<Component>();
                foreach (var c in srcComponents)
                {
                    if (c is Transform) continue;
                    if (UnityEditorInternal.ComponentUtility.CopyComponent(c) &&
                        UnityEditorInternal.ComponentUtility.PasteComponentAsNew(roomLightPuz))
                    {
                        copiedComps++;
                    }
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Selection.activeGameObject = roomLightPuz;
                EditorGUIUtility.PingObject(roomLightPuz);

                Debug.Log($"[FutRedesign] 완료 — RoomLightPuz 안:\n" +
                          $"  복제된 퍼즐 자식: {copied} 개\n" +
                          $"  swap 된 머티리얼 슬롯: {swappedMats} 개\n" +
                          $"  cube→sphere 메쉬 교체: {orbMeshChanged} 개\n" +
                          $"  부모 컴포넌트 복사: {copiedComps} 개\n" +
                          "확인 후 Ctrl+S 저장. 문제 시 Ctrl+Z.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        // =========================================================================================
        // 머티리얼 swap
        // =========================================================================================

        /// <summary>go 와 그 자식들의 모든 MeshRenderer 를 순회하며 sharedMaterials 를 미래풍으로 swap.</summary>
        static int SwapMaterialsRecursive(GameObject go)
        {
            int swapped = 0;
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var srcMat = mats[i];
                    if (srcMat == null) continue;
                    if (NameMap.TryGetValue(srcMat.name, out string futKey))
                    {
                        if (_palette.TryGetValue(futKey, out Material futMat) && futMat != null)
                        {
                            Undo.RecordObject(r, "Swap material to futuristic");
                            mats[i] = futMat;
                            changed = true;
                            swapped++;
                        }
                    }
                    else
                    {
                        // 매핑 없는 머티리얼 → DarkChrome 기본값 (Unknown 패딩).
                        if (_palette.TryGetValue("Holo_DarkChrome", out Material defMat) && defMat != null)
                        {
                            Undo.RecordObject(r, "Swap material to futuristic (default)");
                            mats[i] = defMat;
                            changed = true;
                            swapped++;
                        }
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
            return swapped;
        }

        // =========================================================================================
        // 미래풍 머티리얼 팔레트 생성/로드
        // =========================================================================================

        static void EnsurePalette()
        {
            _palette = new Dictionary<string, Material>();
            EnsureFolder(MatFolderPath);

            // 각 머티리얼 정의: (key, baseColor, emissionColor, metallic, smoothness)
            var defs = new (string Key, Color Base, Color Emis, float Metallic, float Smoothness)[]
            {
                ("Holo_DarkBase",    new Color(0.04f, 0.05f, 0.08f), Color.black,                     0.10f, 0.30f),
                ("Holo_DarkChrome",  new Color(0.10f, 0.12f, 0.15f), Color.black,                     0.90f, 0.70f),
                ("Holo_Chrome",      new Color(0.70f, 0.75f, 0.85f), Color.black,                     1.00f, 0.95f),
                ("Holo_CyanGlow",    new Color(0.10f, 0.40f, 0.60f), new Color(0.0f, 4.0f, 6.0f),    0.20f, 0.80f),
                ("Holo_RedGlow",     new Color(0.60f, 0.10f, 0.10f), new Color(6.0f, 0.3f, 0.3f),    0.30f, 0.70f),
                ("Holo_GreenGlow",   new Color(0.10f, 0.60f, 0.20f), new Color(0.3f, 6.0f, 1.0f),    0.30f, 0.70f),
                ("Holo_BlueGlow",    new Color(0.10f, 0.30f, 0.70f), new Color(0.3f, 1.0f, 6.0f),    0.30f, 0.70f),
                ("Holo_YellowGlow",  new Color(0.70f, 0.60f, 0.10f), new Color(6.0f, 5.0f, 0.5f),    0.30f, 0.70f),
                ("Holo_OrangeGlow",  new Color(0.70f, 0.30f, 0.10f), new Color(6.0f, 2.0f, 0.3f),    0.30f, 0.70f),
                ("Holo_WhiteGlow",   new Color(0.90f, 0.95f, 1.00f), new Color(4.0f, 5.0f, 6.0f),    0.10f, 0.90f),
            };

            foreach (var def in defs)
            {
                string path = $"{MatFolderPath}/{def.Key}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                Material mat;
                if (existing != null)
                {
                    mat = existing;
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
            if (shader == null)
            {
                Debug.LogWarning("[FutRedesign] URP Lit 셰이더를 찾을 수 없다. Standard 폴백.");
                shader = Shader.Find("Standard");
            }
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", baseColor); // builtin fallback
            if (mat.HasProperty("_Metallic"))  mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness); // builtin fallback
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
            return mat;
        }

        // =========================================================================================
        // Helpers
        // =========================================================================================

        /// <summary>Unity 빌트인 Sphere mesh 가져오기 (Primitive 임시 생성 후 mesh 만 꺼냄).</summary>
        static Mesh GetBuiltinSphereMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string[] parts = folderPath.Split('/');
            string cur = parts[0]; // "Assets"
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
                Debug.LogError("[FutRedesign] Active scene 이 유효하지 않다.");
                return false;
            }
            if (!scene.name.Contains("Stage3"))
            {
                if (!EditorUtility.DisplayDialog(
                        "Futuristic Redesign",
                        $"현재 active scene '{scene.name}' 이 Stage3 가 아닐 수 있다.\n계속할까?",
                        "계속", "취소"))
                    return false;
            }
            return true;
        }

        static GameObject FindAnywhere(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = SearchRecursive(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        static Transform SearchRecursive(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = SearchRecursive(t.GetChild(i), name);
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
