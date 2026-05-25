using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Stage3/Setup Beam Gated Door at Wall_Z_W_0.
    ///
    /// Wall_Corridor 의 Wall_Z_W_0 위치에 LightBeamReceiver 가 빔 hit 받으면 열리는
    /// **양쪽 hinge 문** (각 패널이 양 끝 pivot 에서 90° 회전해 바깥으로 swing) 설치.
    ///
    /// 동작:
    ///   1. Wall_Corridor 그룹 안에서 Wall_Z_W_0 찾기.
    ///   2. 위치/부모/스케일 기록 후 Wall_Z_W_0 SetActive(false).
    ///   3. 같은 부모 아래 BeamGatedDoor_WestEntry GameObject 생성:
    ///        - LeftPivot (Z=0 끝) + LeftPanel (자식, +Z 방향 offset)
    ///        - RightPivot (Z=3 끝) + RightPanel (자식, -Z 방향 offset)
    ///        - Lintel (상단 가로지름)
    ///        - BeamGatedDoor 컴포넌트 부착 (LeftPivot/RightPivot + Receiver 자동 설정)
    ///   4. Stage1 Skin 안에서 활성 LightBeamReceiver 검색.
    ///   5. BeamGatedDoor.Receiver 필드에 직접 할당 (Awake 자동 구독) + persistent listener 도 추가.
    ///
    /// 결과: 빔이 Receiver hit → 두 패널이 양 끝 hinge 에서 -X 방향(채임버 바깥)으로 90° 회전.
    ///       빔 해제 → 0.3s 후 닫힘.
    /// </summary>
    public static class BeamGatedDoorSetup
    {
        const string TargetWallGroup = "Wall_Corridor"; // 어느 wall 그룹 안에서 찾을지
        const string TargetWallName  = "Wall_Z_W_0";   // 그 안의 어떤 segment
        const string DoorRootName    = "BeamGatedDoor_WestEntry";

        // 매트 다크 머티리얼 (RoomLightPuz 와 같은 팔레트 사용)
        const string DarkChromeMatPath = "Assets/PipePuz/RoomLightPuz/Materials/Holo_DarkChrome.mat";
        const string ChromeMatPath     = "Assets/PipePuz/RoomLightPuz/Materials/Holo_Chrome.mat";
        const string DarkBaseMatPath   = "Assets/PipePuz/RoomLightPuz/Materials/Holo_DarkBase.mat";
        const string AmberGlowMatPath  = "Assets/PipePuz/RoomLightPuz/Materials/Holo_AmberGlow.mat";
        const string GoldGlowMatPath   = "Assets/PipePuz/RoomLightPuz/Materials/Holo_GoldGlow.mat";

        // 도어 디자인 상수
        const float SegmentLength = 3f;     // Wall_Simple_01 길이
        const float DoorHeight    = 3.5f;   // 벽 높이 (yScale 1.25 × 3m = 3.75 근사)
        const float PanelWidth    = 1.5f;   // 각 패널 폭 (Z 방향, 절반)
        const float PanelThickness = 0.15f; // 패널 두께 (X 방향)
        // (이전 slide 방식 SlideDistance 는 hinge 방식으로 전환되어 미사용)

        [MenuItem("Tools/PipePuz/Stage3/Setup Beam Gated Door at Wall_Z_W_0")]
        public static void Setup()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[BeamDoor] Active scene 무효.");
                return;
            }

            // 1. Wall_Corridor 그룹 먼저 찾고 그 안의 Wall_Z_W_0 찾기 — 다른 wall 그룹(Entrance, LeftChamber)
            //    에도 동일 이름이 있으므로 정확한 그룹 지정 필요.
            GameObject group = FindByNameAnywhere(scene, TargetWallGroup);
            if (group == null)
            {
                Debug.LogError($"[BeamDoor] '{TargetWallGroup}' 그룹을 씬에서 못 찾았다. Modular room 이 빌드되어 있는지 확인.");
                return;
            }
            GameObject wall = SearchByName(group.transform, TargetWallName)?.gameObject;
            if (wall == null)
            {
                Debug.LogError($"[BeamDoor] '{TargetWallGroup}' 안에서 '{TargetWallName}' 못 찾음.");
                return;
            }
            Debug.Log($"[BeamDoor] 대상 확정: {TargetWallGroup}/{TargetWallName} (world X={wall.transform.position.x:F2}, Y={wall.transform.position.y:F2}, Z={wall.transform.position.z:F2})");

            Transform wallParent = wall.transform.parent;
            Vector3 wallLocalPos = wall.transform.localPosition;
            Quaternion wallLocalRot = wall.transform.localRotation;

            // 2. LightBeamReceiver 찾기 — 활성 RoomCliff (Stage1 Skin) 안에서 우선 검색.
            //    (씬 루트에 비활성 RoomCliff 백업이 있어도 거기 거 안 잡도록)
            PipePuz.LightBeam.LightBeamReceiver receiver = FindReceiverInStage1Skin(scene);
            if (receiver == null)
            {
                receiver = FindReceiverAnywhere(scene);
                if (receiver != null)
                    Debug.LogWarning($"[BeamDoor] 'RoomCliff (Stage1 Skin)' 안에서 Receiver 못 찾음 — fallback 으로 씬 전체에서 '{receiver.name}' 사용. 잘못된 Receiver 일 수 있음.");
            }
            if (receiver == null)
            {
                if (!EditorUtility.DisplayDialog(
                        "Receiver 없음",
                        "LightBeamReceiver 를 씬에서 못 찾았다. 도어는 만들지만 자동 wire 는 안 됨.\n계속할까?",
                        "계속", "취소"))
                    return;
            }
            else
            {
                Debug.Log($"[BeamDoor] Wire 대상 Receiver: '{receiver.name}' (path: {GetFullPath(receiver.transform)})");
            }

            // 3. 머티리얼 로드 (없으면 fallback)
            Material darkChromeMat = AssetDatabase.LoadAssetAtPath<Material>(DarkChromeMatPath);
            Material chromeMat     = AssetDatabase.LoadAssetAtPath<Material>(ChromeMatPath);
            Material darkBaseMat   = AssetDatabase.LoadAssetAtPath<Material>(DarkBaseMatPath);
            Material amberMat      = AssetDatabase.LoadAssetAtPath<Material>(AmberGlowMatPath);
            Material goldMat       = AssetDatabase.LoadAssetAtPath<Material>(GoldGlowMatPath);
            if (darkChromeMat == null || chromeMat == null)
                Debug.LogWarning("[BeamDoor] 일부 머티리얼 없음 — 기본 머티리얼 사용. RoomLightPuz 팔레트 먼저 생성하면 톤 통일됨.");

            Undo.SetCurrentGroupName("Setup Beam Gated Door at Wall_Z_W_0");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                // 4. 기존 BeamGatedDoor_WestEntry 가 있으면 제거.
                for (int i = 0; wallParent != null && i < wallParent.childCount; i++)
                {
                    var ch = wallParent.GetChild(i);
                    if (ch.name == DoorRootName)
                    {
                        Undo.DestroyObjectImmediate(ch.gameObject);
                        break;
                    }
                }

                // 5. Wall_Z_W_0 비활성화 (SetActive(false))
                Undo.RecordObject(wall, "Disable Wall_Z_W_0");
                wall.SetActive(false);

                // 6. 새 도어 루트 생성
                var doorRoot = new GameObject(DoorRootName);
                Undo.RegisterCreatedObjectUndo(doorRoot, "Create BeamGatedDoor root");
                SceneManager.MoveGameObjectToScene(doorRoot, scene);
                if (wallParent != null)
                {
                    Undo.SetTransformParent(doorRoot.transform, wallParent, false, "Parent door root");
                }
                doorRoot.transform.localPosition = wallLocalPos;
                doorRoot.transform.localRotation = wallLocalRot;
                doorRoot.transform.localScale = Vector3.one;

                // 7. Hinge 구조 — 양 끝에 Pivot GameObject + 그 자식으로 panel cube.
                //    Wall_Z_W_0 의 local 기준: X 방향이 thickness, Z 방향이 length(0~3), Y 가 높이.
                //    LeftPivot 위치  Z=0 (south 끝, 채임버 내부 쪽)
                //    RightPivot 위치 Z=3 (north 끝, 챔버 내부 쪽)
                //    각 pivot 의 panel cube 는 pivot 에서 안쪽(중앙) 으로 PanelWidth/2 만큼 offset.
                //    LeftPivot 이 Y=-90° 회전 → panel 이 +Z → -X 방향으로 swing (밖으로 = -X = 채임버 바깥).
                //    RightPivot 이 Y=+90° 회전 → panel 이 -Z → -X 방향으로 swing.

                // 좌측 pivot (Z=0)
                var leftPivot = new GameObject("LeftPivot");
                Undo.RegisterCreatedObjectUndo(leftPivot, "Create LeftPivot");
                leftPivot.transform.SetParent(doorRoot.transform, false);
                leftPivot.transform.localPosition = new Vector3(0f, 0f, 0f);
                leftPivot.transform.localRotation = Quaternion.identity;
                // 좌측 호화 패널 (pivot 자식, +Z 방향으로 offset)
                BuildLuxuryPanel(leftPivot.transform, "LeftPanel",
                    new Vector3(0f, DoorHeight * 0.5f, PanelWidth * 0.5f),
                    new Vector3(PanelThickness, DoorHeight, PanelWidth),
                    isLeft: true,
                    darkChromeMat, chromeMat, darkBaseMat, amberMat, goldMat);

                // 우측 pivot (Z=3)
                var rightPivot = new GameObject("RightPivot");
                Undo.RegisterCreatedObjectUndo(rightPivot, "Create RightPivot");
                rightPivot.transform.SetParent(doorRoot.transform, false);
                rightPivot.transform.localPosition = new Vector3(0f, 0f, SegmentLength);
                rightPivot.transform.localRotation = Quaternion.identity;
                // 우측 호화 패널 (pivot 자식, -Z 방향으로 offset)
                BuildLuxuryPanel(rightPivot.transform, "RightPanel",
                    new Vector3(0f, DoorHeight * 0.5f, -PanelWidth * 0.5f),
                    new Vector3(PanelThickness, DoorHeight, PanelWidth),
                    isLeft: false,
                    darkChromeMat, chromeMat, darkBaseMat, amberMat, goldMat);

                // 정적 도어 프레임 (lintel + 양 jambs + threshold) — pivot 회전과 무관
                BuildDoorFrame(doorRoot.transform, darkChromeMat, chromeMat, amberMat);

                // 8. BeamGatedDoor 컴포넌트 부착 + 설정 (회전식 hinge)
                var beamDoor = Undo.AddComponent<PipePuz.LightBeam.BeamGatedDoor>(doorRoot);
                Undo.RecordObject(beamDoor, "Configure BeamGatedDoor");
                beamDoor.LeftPivot = leftPivot.transform;
                beamDoor.RightPivot = rightPivot.transform;
                beamDoor.LeftOpenAngle = -90f;  // -Y 회전 → +Z 였던 panel 이 -X 방향으로 swing (벽 바깥)
                beamDoor.RightOpenAngle = 90f;  // +Y 회전 → -Z 였던 panel 이 -X 방향으로 swing (벽 바깥)
                beamDoor.OpenSpeedDegPerSec = 180f;
                beamDoor.CloseSpeedDegPerSec = 120f;
                beamDoor.CloseDelay = 0.3f;
                beamDoor.Receiver = receiver; // ★ 직접 참조 — Awake 에서 자동 구독

                // 9. Receiver.OnHitChanged 에 persistent listener 도 추가 (이중 안전망).
                //    BeamGatedDoor.Awake 가 런타임 구독 하지만, persistent listener 도 있으면 Inspector 에서 확인 가능.
                if (receiver != null)
                {
                    UnityAction<bool> action = beamDoor.SetBeamConnected;
                    bool already = false;
                    int existingCount = receiver.OnHitChanged.GetPersistentEventCount();
                    for (int i = 0; i < existingCount; i++)
                    {
                        var t = receiver.OnHitChanged.GetPersistentTarget(i);
                        var m = receiver.OnHitChanged.GetPersistentMethodName(i);
                        if (t == beamDoor && m == nameof(beamDoor.SetBeamConnected))
                        {
                            already = true;
                            break;
                        }
                    }
                    if (!already)
                    {
                        Undo.RecordObject(receiver, "Wire OnHitChanged → SetBeamConnected");
                        UnityEventTools.AddPersistentListener(receiver.OnHitChanged, action);
                        EditorUtility.SetDirty(receiver);
                        Debug.Log($"[BeamDoor] Persistent listener 추가: Receiver.OnHitChanged → BeamGatedDoor.SetBeamConnected.");
                    }
                    else
                    {
                        Debug.Log($"[BeamDoor] Persistent listener 이미 있음 (스킵). 런타임 구독은 따로 작동.");
                    }
                }

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                Selection.activeGameObject = doorRoot;
                EditorGUIUtility.PingObject(doorRoot);
                Debug.Log($"[BeamDoor] 완료. {TargetWallName} 비활성화, '{DoorRootName}' 생성, BeamGatedDoor 설정.\n" +
                          "Ctrl+S 저장. 빔이 Receiver 에 hit 되면 문 열림.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        /// <summary>
        /// 호화 다층 패널 — base + 외곽 chrome frame + 내부 recess + 중앙 diamond + 수직 핸들 + 상단 LED.
        /// 모든 자식은 비스케일 (panel root 가 스케일 X — 자식은 절대 사이즈로 직접 배치).
        /// </summary>
        static GameObject BuildLuxuryPanel(Transform parent, string name, Vector3 localPos, Vector3 panelSize,
                                           bool isLeft,
                                           Material darkChromeMat, Material chromeMat, Material darkBaseMat,
                                           Material amberMat, Material goldMat)
        {
            // 루트 — 스케일 1 유지, 자식들이 절대 사이즈로 표현. Collider 추가 (문 닫힘 차단).
            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, $"Create {name}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // 루트에 BoxCollider — 문 닫혔을 때 관통 차단.
            var rootCol = Undo.AddComponent<BoxCollider>(root);
            rootCol.size = panelSize;
            rootCol.center = Vector3.zero;

            float W = panelSize.x; // 두께
            float H = panelSize.y; // 높이
            float D = panelSize.z; // 길이 (Z 방향)

            // 1. BasePlate — 메인 어두운 패널
            AddCube(root.transform, "BasePlate",
                Vector3.zero, Vector3.one,
                new Vector3(W, H, D), darkChromeMat, false);

            // 2. 외곽 frame strips (chrome, 4면 두름) — 패널 정면(+X)에서 약간 튀어나옴
            float frameT = 0.025f; // frame thickness
            float frameO = W * 0.5f + 0.005f; // 정면 offset
            // Top
            AddCube(root.transform, "Frame_Top",
                new Vector3(frameO, H * 0.5f - frameT * 0.5f, 0f),
                Vector3.one, new Vector3(frameT, frameT, D), chromeMat, false);
            // Bottom
            AddCube(root.transform, "Frame_Bottom",
                new Vector3(frameO, -H * 0.5f + frameT * 0.5f, 0f),
                Vector3.one, new Vector3(frameT, frameT, D), chromeMat, false);
            // Inner edge (door center seam 쪽)
            float innerZ = isLeft ? D * 0.5f - frameT * 0.5f : -D * 0.5f + frameT * 0.5f;
            AddCube(root.transform, "Frame_Inner",
                new Vector3(frameO, 0f, innerZ),
                Vector3.one, new Vector3(frameT, H, frameT), chromeMat, false);
            // Outer edge (hinge 쪽)
            float outerZ = isLeft ? -D * 0.5f + frameT * 0.5f : D * 0.5f - frameT * 0.5f;
            AddCube(root.transform, "Frame_Outer",
                new Vector3(frameO, 0f, outerZ),
                Vector3.one, new Vector3(frameT, H, frameT), chromeMat, false);

            // 3. 내부 recess (DarkBase) — 정면에 살짝 들어간 어두운 패널
            float recessW = D - frameT * 2 - 0.06f;
            float recessH = H - frameT * 2 - 0.06f;
            AddCube(root.transform, "InnerRecess",
                new Vector3(W * 0.5f - 0.005f, 0f, 0f),
                Vector3.one, new Vector3(0.015f, recessH, recessW), darkBaseMat, false);

            // 4. 중앙 diamond (chrome) — 큐브를 45° 회전한 마름모
            AddCube(root.transform, "CenterDiamond",
                new Vector3(W * 0.5f + 0.015f, 0f, 0f),
                Quaternion.Euler(45f, 0f, 0f),
                new Vector3(0.04f, 0.18f, 0.18f), chromeMat, false);

            // 5. 수직 핸들 (chrome) — 안쪽(door center seam) 가까이 세로 grip
            float handleZ = isLeft ? D * 0.5f - 0.15f : -D * 0.5f + 0.15f;
            AddCube(root.transform, "Handle",
                new Vector3(W * 0.5f + 0.04f, 0f, handleZ),
                Vector3.one,
                new Vector3(0.06f, 0.8f, 0.04f), chromeMat, false);
            // 핸들 위/아래 brace (chrome, 작은 가로 cube — 핸들 지지대 느낌)
            AddCube(root.transform, "HandleBrace_Top",
                new Vector3(W * 0.5f + 0.025f, 0.42f, handleZ),
                Vector3.one,
                new Vector3(0.03f, 0.04f, 0.06f), chromeMat, false);
            AddCube(root.transform, "HandleBrace_Bot",
                new Vector3(W * 0.5f + 0.025f, -0.42f, handleZ),
                Vector3.one,
                new Vector3(0.03f, 0.04f, 0.06f), chromeMat, false);

            // 6. 상단 상태 LED (amber) — 작은 indicator
            float ledZ = isLeft ? D * 0.5f - 0.2f : -D * 0.5f + 0.2f;
            AddCube(root.transform, "StatusLED",
                new Vector3(W * 0.5f + 0.02f, H * 0.5f - 0.15f, ledZ),
                Vector3.one,
                new Vector3(0.02f, 0.04f, 0.04f), amberMat, false);

            // 7. 하단 gold accent 가로선 (장식)
            if (goldMat != null)
            {
                AddCube(root.transform, "BottomGoldLine",
                    new Vector3(W * 0.5f + 0.012f, -H * 0.5f + 0.15f, 0f),
                    Vector3.one,
                    new Vector3(0.01f, 0.012f, D - 0.2f), goldMat, false);
            }

            return root;
        }

        /// <summary>도어 정적 프레임 — lintel + 양 jamb + threshold (회전 안 함).</summary>
        static void BuildDoorFrame(Transform doorRoot, Material darkChromeMat, Material chromeMat, Material amberMat)
        {
            const float jambW = 0.2f;   // jamb 가로 두께 (X)
            const float jambD = 0.18f;  // jamb 세로 두께 (Z 방향)
            const float lintelH = 0.18f;
            const float thresholdH = 0.06f;

            // Lintel (윗문틀) — 어두운 + chrome top strip + amber LED
            AddCube(doorRoot, "Lintel_Base",
                new Vector3(0f, DoorHeight + lintelH * 0.5f, SegmentLength * 0.5f),
                Vector3.one,
                new Vector3(jambW, lintelH, SegmentLength), darkChromeMat, false);
            AddCube(doorRoot, "Lintel_TopStrip",
                new Vector3(0f, DoorHeight + lintelH - 0.015f, SegmentLength * 0.5f),
                Vector3.one,
                new Vector3(jambW + 0.005f, 0.025f, SegmentLength + 0.05f), chromeMat, false);
            // 아래쪽 amber strip (활성/비활성 시각 표시 용 — 항상 표시)
            if (amberMat != null)
            {
                AddCube(doorRoot, "Lintel_AmberStrip",
                    new Vector3(0f, DoorHeight + 0.01f, SegmentLength * 0.5f),
                    Vector3.one,
                    new Vector3(jambW * 0.5f, 0.015f, SegmentLength - 0.2f), amberMat, false);
            }

            // Left jamb (Z=0 쪽)
            AddCube(doorRoot, "LeftJamb",
                new Vector3(0f, DoorHeight * 0.5f, -jambD * 0.3f),
                Vector3.one,
                new Vector3(jambW, DoorHeight + lintelH, jambD), darkChromeMat, false);
            // Chrome accent stripe
            AddCube(doorRoot, "LeftJamb_Stripe",
                new Vector3(jambW * 0.5f + 0.005f, DoorHeight * 0.5f, -jambD * 0.3f),
                Vector3.one,
                new Vector3(0.015f, DoorHeight - 0.3f, 0.04f), chromeMat, false);

            // Right jamb (Z=SegmentLength 쪽)
            AddCube(doorRoot, "RightJamb",
                new Vector3(0f, DoorHeight * 0.5f, SegmentLength + jambD * 0.3f),
                Vector3.one,
                new Vector3(jambW, DoorHeight + lintelH, jambD), darkChromeMat, false);
            AddCube(doorRoot, "RightJamb_Stripe",
                new Vector3(jambW * 0.5f + 0.005f, DoorHeight * 0.5f, SegmentLength + jambD * 0.3f),
                Vector3.one,
                new Vector3(0.015f, DoorHeight - 0.3f, 0.04f), chromeMat, false);

            // Threshold (바닥 문턱)
            AddCube(doorRoot, "Threshold",
                new Vector3(0f, thresholdH * 0.5f, SegmentLength * 0.5f),
                Vector3.one,
                new Vector3(jambW * 1.2f, thresholdH, SegmentLength + 0.1f), darkChromeMat, false);
        }

        /// <summary>cube primitive 추가 헬퍼 — 콜라이더 제거, 위치/스케일/머티리얼 설정.</summary>
        static GameObject AddCube(Transform parent, string name, Vector3 localPos, Vector3 _unused,
                                  Vector3 localScale, Material mat, bool keepCollider)
        {
            return AddCubeImpl(parent, name, localPos, Quaternion.identity, localScale, mat, keepCollider);
        }
        static GameObject AddCube(Transform parent, string name, Vector3 localPos, Quaternion localRot,
                                  Vector3 localScale, Material mat, bool keepCollider)
        {
            return AddCubeImpl(parent, name, localPos, localRot, localScale, mat, keepCollider);
        }
        static GameObject AddCubeImpl(Transform parent, string name, Vector3 localPos, Quaternion localRot,
                                      Vector3 localScale, Material mat, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            if (!keepCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>활성 RoomCliff (Stage1 Skin) 안에서만 Receiver 검색.</summary>
        static PipePuz.LightBeam.LightBeamReceiver FindReceiverInStage1Skin(Scene scene)
        {
            var skin = FindByNameAnywhere(scene, "RoomCliff (Stage1 Skin)");
            if (skin == null) return null;
            // 활성 receiver 만 (비활성은 제외)
            var receivers = skin.GetComponentsInChildren<PipePuz.LightBeam.LightBeamReceiver>(includeInactive: false);
            return receivers.Length > 0 ? receivers[0] : null;
        }

        /// <summary>씬 전체에서 첫 Receiver 검색 (활성만).</summary>
        static PipePuz.LightBeam.LightBeamReceiver FindReceiverAnywhere(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.activeInHierarchy) continue;
                var r = root.GetComponentInChildren<PipePuz.LightBeam.LightBeamReceiver>(includeInactive: false);
                if (r != null) return r;
            }
            return null;
        }

        static string GetFullPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
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
    }
}
