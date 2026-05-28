#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Stage1;

namespace Stage1.Editor
{
    /// <summary>
    /// 단순한 색상 + 슬롯 셋업 도구.
    /// 사용자가 이미 만들어둔 GameObject들을 가정:
    ///   - Stage1 안: Table1, Table2, Table3
    ///   - LightBall, LightBall (1), LightBall (2)
    ///
    /// 매핑:
    ///   Table1 + LightBall      = Red
    ///   Table2 + LightBall (1)  = Yellow
    ///   Table3 + LightBall (2)  = Blue
    ///
    /// BatteryMelter / MainControlSystem 원본 코드는 절대 안 건드림 —
    /// 모든 색/매칭/슬롯 로직은 외부 컴포넌트 (BatteryMelter, MultiBatterySlotPanel)로 처리.
    /// </summary>
    public static class MultiBatterySetupTool
    {
        const string MatFolder = "Assets/Stage1/Materials";

        static readonly (LightBallColor color, string name, Color rgb, string tableName, string lightBallName)[] Mapping =
        {
            (LightBallColor.Red,    "Red",    new Color(1.0f, 0.25f, 0.25f, 1f),  "Table1", "LightBall"),
            (LightBallColor.Yellow, "Yellow", new Color(1.0f, 0.85f, 0.20f, 1f),  "Table2", "LightBall (1)"),
            (LightBallColor.Blue,   "Blue",   new Color(0.25f, 0.40f, 1.0f, 1f),  "Table3", "LightBall (2)"),
        };

        // ─────────────────────────────────────────────────────
        // Run All
        // ─────────────────────────────────────────────────────

        [MenuItem("Tools/Stage 1/Battery Color Setup/Run All (Holes + LightBalls + Slots + Sync)")]
        public static void RunAll()
        {
            TintLightBallHoles();
            SetupLightBalls();
            SetupBatterySlotPanel();
            // Melter chips are no longer needed as BatteryMelter handles color validation internally.
            SetupLightBallLightSync();
            Debug.Log("[Setup] Run All 완료.");
        }

        // ─────────────────────────────────────────────────────
        // 1. Tables 안의 LightBallHole에만 색 입히기 (clone-and-tint, 디자인 보존)
        // ─────────────────────────────────────────────────────

        [MenuItem("Tools/Stage 1/Battery Color Setup/1. Tint LightBallHoles (Red/Yellow/Blue)")]
        public static void TintLightBallHoles()
        {
            EnsureFolder(MatFolder);

            foreach (var m in Mapping)
            {
                GameObject table = GameObject.Find(m.tableName);
                if (table == null)
                {
                    Debug.LogWarning($"[Setup] '{m.tableName}' GameObject 못 찾음.");
                    continue;
                }

                // Table 자식(또는 손자) 중 이름에 'LightBallHole'이 포함된 GameObject 찾기
                Transform hole = FindDescendantContaining(table.transform, "LightBallHole");
                if (hole == null)
                {
                    Debug.LogWarning($"[Setup] {m.tableName} 자식에 LightBallHole 못 찾음. " +
                                     "Table 아래에 LightBallHole 게임오브젝트를 먼저 배치해야 함.");
                    continue;
                }

                int tinted = 0;
                foreach (var rend in hole.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] currentMats = rend.sharedMaterials;
                    if (currentMats == null || currentMats.Length == 0) continue;

                    Renderer srcRend = PrefabUtility.GetCorrespondingObjectFromSource(rend) as Renderer;
                    Material[] srcMats = srcRend != null ? srcRend.sharedMaterials : null;

                    Material[] newMats = new Material[currentMats.Length];
                    bool anyChanged = false;
                    for (int i = 0; i < currentMats.Length; i++)
                    {
                        Material original = (srcMats != null && i < srcMats.Length && srcMats[i] != null)
                            ? srcMats[i]
                            : currentMats[i];
                        if (original == null) { newMats[i] = currentMats[i]; continue; }

                        string baseName = SanitizeName(original.name);
                        string assetPath = $"{MatFolder}/{baseName}_{m.name}_Tinted.mat";
                        Material tintedMat = CloneAndTint(original, m.rgb, 0.3f, assetPath);
                        newMats[i] = tintedMat != null ? tintedMat : currentMats[i];
                        if (tintedMat != null && tintedMat != currentMats[i]) anyChanged = true;
                    }

                    if (anyChanged)
                    {
                        Undo.RecordObject(rend, "Tint LightBallHole");
                        rend.sharedMaterials = newMats;
                        tinted++;
                    }
                }

                EditorUtility.SetDirty(hole.gameObject);
                Debug.Log($"[Setup] {m.tableName}/{hole.name} → {m.name} 색 적용 ({tinted}개 Renderer).");
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 자식 트리에서 이름에 needle이 포함된 첫 Transform 반환 (대소문자 무시).
        /// </summary>
        static Transform FindDescendantContaining(Transform root, string needle)
        {
            if (root == null) return null;
            string lowered = needle.ToLowerInvariant();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (t.name.ToLowerInvariant().Contains(lowered)) return t;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────
        // 2A. LightBall Shader 통째 교체 (URP/Lit + 강한 emission, 색만)
        // ─────────────────────────────────────────────────────

        [MenuItem("Tools/Stage 1/Battery Color Setup/2A. Replace LightBall Shaders (URP/Lit emission)")]
        public static void ReplaceLightBallShaders()
        {
            EnsureFolder(MatFolder);

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[Setup] URP/Lit shader 못 찾음.");
                return;
            }

            foreach (var m in Mapping)
            {
                GameObject lb = GameObject.Find(m.lightBallName);
                if (lb == null) continue;

                // 색별 새 머티리얼 생성 (한 색당 1개만, 모든 renderer 슬롯에 적용)
                string matPath = $"{MatFolder}/LightBall_Solid_{m.name}.mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(litShader);
                    mat.name = $"LightBall_Solid_{m.name}";
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.shader = litShader;
                mat.SetColor("_BaseColor", m.rgb);
                mat.SetColor("_Color", m.rgb);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", m.rgb * 3f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(mat);

                int replaced = 0;
                foreach (var rend in lb.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = rend.sharedMaterials;
                    if (mats == null || mats.Length == 0) continue;
                    Material[] newMats = new Material[mats.Length];
                    for (int i = 0; i < mats.Length; i++) newMats[i] = mat;
                    Undo.RecordObject(rend, "Replace LightBall shader");
                    rend.sharedMaterials = newMats;
                    replaced++;
                }
                EditorUtility.SetDirty(lb);
                Debug.Log($"[Setup] {m.lightBallName} → 셰이더 통째 교체 (URP/Lit + emission, {replaced}개 Renderer).");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ─────────────────────────────────────────────────────
        // 2B. LightBalls 복구 — Tag/Rigidbody/Light/Position 정상화
        // ─────────────────────────────────────────────────────

        [MenuItem("Tools/Stage 1/Battery Color Setup/2B. Repair LightBalls (Tag/Rigidbody/Light/Position)")]
        public static void RepairLightBalls()
        {
            GameObject originalLb = GameObject.Find(Mapping[0].lightBallName); // LightBall (Red 원본)
            if (originalLb == null)
            {
                Debug.LogError("[Setup] 'LightBall' 원본 못 찾음 — 위치/물리 기준 복원 불가.");
                return;
            }

            // 원본 기준값 캐싱
            Vector3 refPos = originalLb.transform.position;
            Quaternion refRot = originalLb.transform.rotation;
            Vector3 refScale = originalLb.transform.localScale;
            Rigidbody refRb = originalLb.GetComponent<Rigidbody>();
            Light refLight = originalLb.GetComponentInChildren<Light>(true);

            int idx = 0;
            foreach (var m in Mapping)
            {
                GameObject lb = GameObject.Find(m.lightBallName);
                if (lb == null) { Debug.LogWarning($"[Repair] {m.lightBallName} 없음."); idx++; continue; }

                Undo.RecordObject(lb, "Repair LightBall");

                // (1) Tag 강제 'LightBall' 보장 — 단, Tag 매니저에 등록돼 있어야 함
                try
                {
                    lb.tag = "LightBall";
                }
                catch
                {
                    Debug.LogWarning($"[Repair] {m.lightBallName} Tag='LightBall'로 설정 실패 — Tag 매니저에서 'LightBall' 태그 추가 필요.");
                }

                // (2) 위치 복원 — 원본 기준 z축으로 0.5m씩 간격 (안 날아가게 안전 위치)
                Undo.RecordObject(lb.transform, "Reset LightBall transform");
                lb.transform.position = refPos + new Vector3(0f, 0f, 0.5f * idx);
                lb.transform.rotation = refRot;
                lb.transform.localScale = refScale;

                // (3) Rigidbody 정상화
                var rb = lb.GetComponent<Rigidbody>();
                if (rb == null) rb = Undo.AddComponent<Rigidbody>(lb);
                Undo.RecordObject(rb, "Repair Rigidbody");
                if (refRb != null)
                {
                    rb.mass = refRb.mass;
                    rb.linearDamping = refRb.linearDamping;
                    rb.angularDamping = refRb.angularDamping;
                    rb.useGravity = refRb.useGravity;
                    rb.isKinematic = refRb.isKinematic;
                    rb.interpolation = refRb.interpolation;
                    rb.collisionDetectionMode = refRb.collisionDetectionMode;
                }
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                // (4) Point Light 자식 확인 — 없으면 생성, 비활성이면 활성
                Light light = lb.GetComponentInChildren<Light>(true);
                if (light == null)
                {
                    var lightGo = new GameObject("Point Light");
                    Undo.RegisterCreatedObjectUndo(lightGo, "Add Point Light");
                    lightGo.transform.SetParent(lb.transform, false);
                    lightGo.transform.localPosition = Vector3.zero;
                    light = lightGo.AddComponent<Light>();
                    if (refLight != null)
                    {
                        light.type = refLight.type;
                        light.intensity = refLight.intensity;
                        light.range = refLight.range;
                        light.shadows = refLight.shadows;
                    }
                    else
                    {
                        light.type = LightType.Point;
                        light.intensity = 3f;
                        light.range = 5f;
                        light.shadows = LightShadows.None;
                    }
                }
                Undo.RecordObject(light, "Enable point light");
                light.enabled = true;
                light.color = m.rgb;
                light.gameObject.SetActive(true);

                // (5) LightBallColorTag 갱신
                var tag = lb.GetComponent<LightBallColorTag>();
                if (tag == null) tag = Undo.AddComponent<LightBallColorTag>(lb);
                tag.color = m.color;
                EditorUtility.SetDirty(tag);

                EditorUtility.SetDirty(lb);
                Debug.Log($"[Repair] {m.lightBallName} → 위치/Rigidbody/Light/Tag 정상화.");
                idx++;
            }
        }

        // ─────────────────────────────────────────────────────
        // 2. LightBalls 셋업 — ColorTag + 메시 색 + Point Light 색 + 태그 검증
        // ─────────────────────────────────────────────────────

        [MenuItem("Tools/Stage 1/Battery Color Setup/2. Setup LightBalls (Tag + Tint Mesh + Tint Light)")]
        public static void SetupLightBalls()
        {
            EnsureFolder(MatFolder);

            foreach (var m in Mapping)
            {
                GameObject lb = GameObject.Find(m.lightBallName);
                if (lb == null)
                {
                    Debug.LogWarning($"[Setup] '{m.lightBallName}' GameObject 못 찾음.");
                    continue;
                }

                // 1) ColorTag 부착/갱신
                var tag = lb.GetComponent<LightBallColorTag>();
                if (tag == null) tag = Undo.AddComponent<LightBallColorTag>(lb);
                Undo.RecordObject(tag, "Set LightBall color tag");
                tag.color = m.color;
                EditorUtility.SetDirty(tag);

                // 2) "LightBall" 태그가 부착돼있는지 확인 (BatteryMelter가 FindGameObjectsWithTag로 찾음)
                if (lb.tag != "LightBall")
                {
                    // 태그가 다르면 경고 — 사용자가 인스펙터에서 직접 'LightBall' 태그로 변경 필요
                    // (UnityEditorInternal.InternalEditorUtility.AddTag 사용 가능하지만, 이미 LightBall 태그는 씬에 있을 것으로 추정)
                    Debug.LogWarning($"[Setup] {m.lightBallName} 의 GameObject Tag가 'LightBall' 아님 (현재: '{lb.tag}'). " +
                                     "BatteryMelter가 LightBall로 인식 못 함. 인스펙터에서 Tag를 'LightBall'로 변경 필요.");
                }

                // 3) 메시 색상 — clone-and-tint (원본 디자인 보존)
                //    sharedMaterials 배열 전체를 처리해서 multi-material renderer도 모든 슬롯 색칠.
                int tinted = 0;
                foreach (var rend in lb.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] currentMats = rend.sharedMaterials;
                    if (currentMats == null || currentMats.Length == 0) continue;

                    // 같은 인덱스의 prefab source 머티리얼도 가져와봄 (디자인 정확 복원용)
                    Renderer srcRend = PrefabUtility.GetCorrespondingObjectFromSource(rend) as Renderer;
                    Material[] srcMats = srcRend != null ? srcRend.sharedMaterials : null;

                    Material[] newMats = new Material[currentMats.Length];
                    bool anyChanged = false;
                    for (int i = 0; i < currentMats.Length; i++)
                    {
                        Material original = (srcMats != null && i < srcMats.Length && srcMats[i] != null)
                            ? srcMats[i]
                            : currentMats[i];
                        if (original == null) { newMats[i] = currentMats[i]; continue; }

                        string baseName = SanitizeName(original.name);
                        string assetPath = $"{MatFolder}/{baseName}_{m.name}_Tinted.mat";
                        Material tintedMat = CloneAndTint(original, m.rgb, 0.8f, assetPath);
                        newMats[i] = tintedMat != null ? tintedMat : currentMats[i];
                        if (tintedMat != null && tintedMat != currentMats[i]) anyChanged = true;
                    }

                    if (anyChanged)
                    {
                        Undo.RecordObject(rend, "Tint LightBall mesh");
                        rend.sharedMaterials = newMats;
                        tinted++;
                    }
                }

                // 4) Point Light 색
                foreach (var light in lb.GetComponentsInChildren<Light>(true))
                {
                    Undo.RecordObject(light, "Tint LightBall point light");
                    light.color = m.rgb;
                }

                EditorUtility.SetDirty(lb);
                Debug.Log($"[Setup] {m.lightBallName} → {m.color} 태그 + 메시 {tinted}개 색 입힘 + Point Light 색 변경.");
            }

            AssetDatabase.SaveAssets();
        }

        // 옛 메뉴 이름 호환용 alias (다른 곳에서 호출되는 경우 대비)
        public static void TagLightBalls() => SetupLightBalls();

        // ─────────────────────────────────────────────────────
        // 3. MainControlSystem에 MultiBatterySlotPanel + 슬롯 3개 셋업
        // ─────────────────────────────────────────────────────

        [MenuItem("Tools/Stage 1/Battery Color Setup/3. Setup BatterySlot Panel (3 slots)")]
        public static void SetupBatterySlotPanel()
        {
            MainControlSystem mcs = Object.FindFirstObjectByType<MainControlSystem>();
            if (mcs == null)
            {
                Debug.LogError("[Setup] MainControlSystem 인스턴스 못 찾음.");
                return;
            }

            // MultiBatterySlotPanel 부착 (없으면)
            var panel = mcs.GetComponent<MultiBatterySlotPanel>();
            if (panel == null) panel = Undo.AddComponent<MultiBatterySlotPanel>(mcs.gameObject);

            Transform anchor = mcs.batterySlot != null ? mcs.batterySlot : mcs.transform;
            Transform parent = anchor.parent != null ? anchor.parent : mcs.transform;
            Vector3 anchorLocal = anchor.localPosition;

            Transform[] slots = new Transform[3];
            LightBallColor[] colors = new LightBallColor[3];
            Vector3[] offsets =
            {
                new Vector3(-0.15f, 0f, 0f),  // Red
                new Vector3( 0.00f, 0f, 0f),  // Yellow
                new Vector3( 0.15f, 0f, 0f),  // Blue
            };

            for (int i = 0; i < 3; i++)
            {
                string slotName = $"BatterySlot_{Mapping[i].name}";
                Transform existing = FindDeep(parent, slotName);
                Transform slot;
                if (existing != null)
                {
                    slot = existing;
                }
                else
                {
                    var go = new GameObject(slotName);
                    Undo.RegisterCreatedObjectUndo(go, $"Create {slotName}");
                    slot = go.transform;
                    slot.SetParent(parent, false);
                    slot.localPosition = anchorLocal + offsets[i];
                    slot.localRotation = anchor.localRotation;
                }
                slots[i] = slot;
                colors[i] = Mapping[i].color;
            }

            Undo.RecordObject(panel, "Wire panel slots");
            panel.mainControl = mcs;
            panel.slots = slots;
            panel.slotColors = colors;
            EditorUtility.SetDirty(panel);

            // Legacy 단일 슬롯과 충돌 방지 — mainControl.batterySlot 강제 null
            // (이게 있으면 원본 MainControlSystem이 단일 배터리 1개로 즉시 Reboot → multi-slot 우회됨)
            if (mcs.batterySlot != null)
            {
                Undo.RecordObject(mcs, "Clear legacy batterySlot");
                mcs.batterySlot = null;
                EditorUtility.SetDirty(mcs);
                Debug.Log("[Setup] MainControlSystem.batterySlot → None 처리 (다중 슬롯 모드 우선).");
            }

            Debug.Log("[Setup] MultiBatterySlotPanel + BatterySlot x3 + 색 매칭 와이어링 완료.");
        }

        // ─────────────────────────────────────────────────────
        // 5. LightBall Light Sync — 모든 LightBall의 Light을 원본과 sync
        // ─────────────────────────────────────────────────────

        [MenuItem("Tools/Stage 1/Battery Color Setup/5. Setup LightBall Light Sync (밝기 동기화)")]
        public static void SetupLightBallLightSync()
        {
            MainControlSystem mcs = Object.FindFirstObjectByType<MainControlSystem>();
            if (mcs == null)
            {
                Debug.LogError("[Setup] MainControlSystem 못 찾음.");
                return;
            }

            var sync = mcs.GetComponent<LightBallLightSync>();
            if (sync == null) sync = Undo.AddComponent<LightBallLightSync>(mcs.gameObject);

            Undo.RecordObject(sync, "Setup LightBallLightSync");
            sync.mainControl = mcs;
            sync.AutoCollectLights();
            EditorUtility.SetDirty(sync);

            Debug.Log($"[Setup] LightBallLightSync 부착 + 보조 Light {(sync.syncedLights != null ? sync.syncedLights.Length : 0)}개 와이어링.");
        }

        // ─────────────────────────────────────────────────────
        // Validate — 셋업이 다 됐는지 점검
        // ─────────────────────────────────────────────────────

        [MenuItem("Tools/Stage 1/Battery Color Setup/Validate Setup")]
        public static void ValidateSetup()
        {
            int errors = 0, warnings = 0;

            // 1) LightBall 3개 검증
            foreach (var m in Mapping)
            {
                GameObject lb = GameObject.Find(m.lightBallName);
                if (lb == null)
                {
                    Debug.LogError($"[Validate] '{m.lightBallName}' 못 찾음."); errors++;
                    continue;
                }
                if (lb.tag != "LightBall")
                {
                    Debug.LogError($"[Validate] '{m.lightBallName}' Tag != 'LightBall' (현재: '{lb.tag}'). BatteryMelter가 못 찾음."); errors++;
                }
                var ct = lb.GetComponent<LightBallColorTag>();
                if (ct == null)
                {
                    Debug.LogError($"[Validate] '{m.lightBallName}' LightBallColorTag 없음."); errors++;
                }
                else if (ct.color != m.color)
                {
                    Debug.LogWarning($"[Validate] '{m.lightBallName}' ColorTag={ct.color}, 기대={m.color}."); warnings++;
                }
            }

            // 2) Table 3개 + LightBallHole 자식 검증
            foreach (var m in Mapping)
            {
                GameObject table = GameObject.Find(m.tableName);
                if (table == null)
                {
                    Debug.LogError($"[Validate] '{m.tableName}' 못 찾음."); errors++;
                    continue;
                }
                Transform hole = FindDescendantContaining(table.transform, "LightBallHole");
                if (hole == null)
                {
                    Debug.LogWarning($"[Validate] {m.tableName} 자식에 LightBallHole 없음."); warnings++;
                }
            }

            // 3) BatteryMelter들 검증
            var melters = Object.FindObjectsByType<BatteryMelter>(FindObjectsSortMode.None);
            if (melters.Length == 0)
            {
                Debug.LogError("[Validate] 씬에 BatteryMelter 없음."); errors++;
            }
            foreach (var melter in melters)
            {
                if (melter.meltedBatteryCore == null)
                {
                    Debug.LogError($"[Validate] BatteryMelter '{melter.name}'.meltedBatteryCore 미할당 — 해동 감지 불가."); errors++;
                }
                if (melter.batterySlot == null)
                {
                    Debug.LogError($"[Validate] BatteryMelter '{melter.name}'.batterySlot 미할당."); errors++;
                }
                if (melter.lightBallHole == null)
                {
                    Debug.LogError($"[Validate] BatteryMelter '{melter.name}'.lightBallHole 미할당."); errors++;
                }
            }

            // 4) MainControlSystem + MultiBatterySlotPanel 검증
            var mcs = Object.FindFirstObjectByType<MainControlSystem>();
            if (mcs == null)
            {
                Debug.LogError("[Validate] MainControlSystem 못 찾음."); errors++;
            }
            else
            {
                var panel = mcs.GetComponent<MultiBatterySlotPanel>();
                if (panel == null)
                {
                    Debug.LogError("[Validate] MainControlSystem에 MultiBatterySlotPanel 없음."); errors++;
                }
                else
                {
                    if (panel.slots == null || panel.slots.Length != 3)
                    {
                        Debug.LogError($"[Validate] MultiBatterySlotPanel.slots 길이 != 3."); errors++;
                    }
                    if (panel.slotColors == null || panel.slotColors.Length != 3)
                    {
                        Debug.LogError($"[Validate] MultiBatterySlotPanel.slotColors 길이 != 3."); errors++;
                    }
                    else
                    {
                        for (int i = 0; i < 3 && i < panel.slotColors.Length; i++)
                        {
                            if (panel.slotColors[i] != Mapping[i].color)
                            {
                                Debug.LogWarning($"[Validate] slotColors[{i}]={panel.slotColors[i]}, 기대={Mapping[i].color}.");
                                warnings++;
                            }
                        }
                    }
                }

                if (mcs.batterySlot != null)
                {
                    Debug.LogError("[Validate] MainControlSystem.batterySlot 비어있지 않음 — " +
                                   "배터리 1개만 들어가도 즉시 Reboot됨 (multi-slot 우회). " +
                                   "'3. Setup BatterySlot Panel' 다시 실행 또는 인스펙터에서 None으로 비울 것.");
                    errors++;
                }
            }

            if (errors == 0 && warnings == 0)
                Debug.Log("[Validate] 모든 셋업 OK ✓");
            else
                Debug.Log($"[Validate] 검증 완료 — Error {errors}개, Warning {warnings}개");
        }

        // ─────────────────────────────────────────────────────
        // Material clone-and-tint
        // ─────────────────────────────────────────────────────

        static Material GetOriginalPrefabMaterial(Renderer rend)
        {
            if (rend == null) return null;
            var srcRend = PrefabUtility.GetCorrespondingObjectFromSource(rend) as Renderer;
            return srcRend != null ? srcRend.sharedMaterial : null;
        }

        static Material CloneAndTint(Material src, Color tint, float emissionMul, string assetPath)
        {
            if (src == null) return null;
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));

            Material target = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (target == null)
            {
                target = new Material(src);
                target.name = Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(target, assetPath);
            }
            else
            {
                target.shader = src.shader;
                target.CopyPropertiesFromMaterial(src);
            }

            ApplyColorTint(target, tint, emissionMul);
            EditorUtility.SetDirty(target);
            return target;
        }

        static void ApplyColorTint(Material m, Color tint, float emissionMul)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);

            if (m.HasProperty("_EmissionColor") && emissionMul > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", tint * emissionMul);
            }
        }

        static string SanitizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Mat";
            return s.Replace(" (Instance)", "").Replace("/", "_").Replace("\\", "_").Trim();
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent))
                AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
#endif
