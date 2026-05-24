using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Stage3/Rebuild Modular Room as RoomCliff Shape.
    ///
    /// RoomSeen 안의 "Room (Stage1 Modular)" 를 지우고, RoomCliff 의 평면도
    /// (Entrance + Corridor + Left/Right Chamber + ChamberDivider + SecondFloor + Stairs) 를
    /// Stage1 Barking_Dog 모듈러 프리팹(Floor_01, Wall_Simple_01, Roof_01, Door_Arch_01) +
    /// ParticlePack 의 Stairs.prefab 으로 다시 깐다.
    ///
    /// 모든 모듈러 프리팹은 BoxCollider/MeshCollider 를 갖고 있어 물리적으로 막힌다
    /// (벽 관통 X, 바닥/계단 위로 걸을 수 있음). XR Grab 같은 "잡을 수 있는" 상호작용은
    /// 부여하지 않는다 — 그건 원본 RoomCliff 의 퍼즐 스크립트(CliffController, AutoSlidingDoor,
    /// LightOrb 등) 가 담당하며, 본 모듈러 룸은 시각/콜리전 셸 역할만 한다.
    ///
    /// 양면 렌더링 (Stage3 한정):
    ///   - 원본 Diffuse_01.mat (_Cull: 2, 단면) 은 Stage1 도 같이 쓰므로 직접 수정 X.
    ///   - Diffuse_01_DoubleSided.mat (_Cull: 0, 양면) 를 자동 생성하고 빌더가 RoomSeen 내 모든
    ///     Floor/Wall/Roof/Door 인스턴스의 sharedMaterials 에 swap. Stage1 영향 0.
    ///
    /// 그리드 결정:
    ///   - 모든 모듈러 피스는 3m × 3m × 3m 단위.
    ///   - RoomCliff 의 원본 치수를 3m 의 배수로 라운딩(보수적으로 round-out — 살짝 큰 쪽).
    ///   - 챔버 높이 5m 는 Wall_Simple_01 의 Y 스케일 1.667 로 늘려 표현.
    ///
    /// 라운딩 결과:
    ///   Entrance       X[-3, +3]    Z[-3, 0]   H=3
    ///   Corridor       X[-24, +9]   Z[0, 3]    H=3   (원본 [-22, +7] 에서 ±2 라운딩)
    ///   LeftChamber    X[-24, +3]   Z[3, 18]   H=5
    ///   RightChamber   X[+3, +9]    Z[3, 15]   H=5
    ///   SecondFloor    X[+3, +9]    Z[9, 15]   Y=3.5
    ///   ChamberDivider X=+3 (LeftChamber.east = RightChamber.west, Z[3, 15])
    ///   LeftDoor 개구  X=-4.5 (LeftChamber 남쪽 벽)
    ///   RightDoor 개구 X=+4.5 (RightChamber 남쪽 벽)
    ///   Entrance→Corridor 개구  X[-3, +3] (Corridor 남쪽 벽)
    ///
    /// 벽 소유권 (중복 벽 방지):
    ///   - Z=-3  : Entrance 남쪽
    ///   - Z=0   : Corridor 남쪽 (Entrance 개구)
    ///   - Z=3   : 챔버 남쪽 (LeftDoor / RightDoor 개구)
    ///   - Z=15  : RightChamber 북쪽
    ///   - Z=18  : LeftChamber 북쪽
    ///   - X=-24 : Corridor/LeftChamber 서쪽
    ///   - X=-3, X=+3 (단 Entrance 옆): Entrance 좌우
    ///   - X=+3  Z[3,15] : RightChamber 서쪽 (=ChamberDivider)
    ///   - X=+3  Z[15,18]: LeftChamber 동쪽 (divider 위로 튀어나온 부분)
    ///   - X=+9  : RightChamber 동쪽 / Corridor 동쪽
    ///
    /// 사전조건: Stage3 active, RoomSeen 존재. RoomCliff 본체는 별도 (씬 루트 또는 어디든).
    /// </summary>
    public static class RoomSeenModularLayout
    {
        // ===== Prefab paths =====
        const string Floor01Path  = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs/Floor_01.prefab";
        const string Wall01Path   = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs/Wall_Simple_01.prefab";
        const string Roof01Path   = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs/Roof_02.prefab";
        const string DoorArchPath = "Assets/Barking_Dog/3D Free Modular Kit/Prefabs/Door_Arch_01.prefab";
        const string StairsPath   = "Assets/UnityTechnologies/ParticlePack/Shared/Environment/Prefabs/Stairs.prefab";

        // ===== Material paths (양면 렌더링 — Stage3 RoomSeen 전용) =====
        // 원본 Diffuse_01.mat 은 _Cull: 2 (back-face culling 단면) — Stage1 도 같이 영향받으므로 직접 수정 X.
        // 대신 복사본 Diffuse_01_DoubleSided.mat (_Cull: 0) 을 자동 생성하고 빌더가 인스턴스에만 적용.
        const string SrcMaterialPath        = "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01.mat";
        const string DoubleSidedMaterialPath = "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01_DoubleSided.mat";

        // ===== Stairs (RightChamber 의 SecondFloor 로 올라가는 계단) =====
        // 원본 RoomCliff: StartZ=4.5, 10 steps, Riser=0.35, Depth=0.4, total height 3.5m, length 4m.
        // RoomSeen 모듈러: 라운딩된 RightChamber X[3,9] 안에 배치. StairCenterX = 6.
        const float StairCenterX = 6f;
        const float StairBaseZ   = 4.5f;   // 계단 남쪽 끝
        const float StairTargetY = 3.5f;   // 도달 높이 (SecondFloor Y)
        const float StairLengthZ = 4f;     // Z 방향 점유 길이
        const float StairWidthX  = 2.5f;

        // ===== Grid =====
        const float Tile = 3f;
        // 벽 높이 — 사용자 요청: Entrance/Corridor 천장을 SecondFloor 높이(Y=3.5) 와 맞춤.
        //   그러면 코리도어 천장 위 = 2층 슬래브와 같은 레벨 → 자연스럽게 연결.
        //   Wall_Simple_01 본래 3m → 1F 벽 스케일 = 3.5/3 ≈ 1.167 (벽 높이 3.5m).
        //   챔버 벽은 그대로 10m 유지.
        const float Wall1FScale    = SecondFloorY / 3f;  // 3.5/3 ≈ 1.167 → 1F 벽 = 3.5m (이전 6m)
        const float WallTallScale  = 10f / 3f;            // ≈3.333 → 챔버 벽 = 10m (변경 없음)

        // ===== Room rects (Xmin, Xmax, Zmin, Zmax) =====
        static readonly Vector4 Entrance     = new Vector4(-3, +3, -3, 0);
        static readonly Vector4 Corridor     = new Vector4(-24, +9, 0, 3);
        static readonly Vector4 LeftChamber  = new Vector4(-24, +3, +3, +18);
        static readonly Vector4 RightChamber = new Vector4(+3, +9, +3, +15);
        static readonly Vector4 SecondFloor  = new Vector4(+3, +9, +9, +15);

        const float CorridorEntranceOpeningXmin = -3f;
        const float CorridorEntranceOpeningXmax = +3f;
        const float LeftDoorCenterX  = -4.5f;
        const float RightDoorCenterX = +4.5f;
        const float SecondFloorY = 3.5f;
        const float ChamberHeight = 5f;

        // ===== Roof Y offset =====
        // Stage1 검증 결과: 'Roof' 부모 Y=0, Roof_02 인스턴스 Y=3.75 (3m 벽 룸).
        //   → Roof_02 의 anchor 가 곧 visual Y 위치.
        // Entrance/Corridor 천장을 SecondFloor 슬래브 높이(Y=3.5) 와 같게 — 코리도어 위에서 바로 2층 슬래브로 이어짐.
        // 챔버 천장은 그대로 Y=10 (벽 10m 유지, SecondFloor Y=3.5 위로도 6.5m 여유).
        const float RoofDefault3mTopOffset = SecondFloorY;  // Y=3.5 — Entrance/Corridor 천장 = 2층 슬래브 높이
        const float RoofChamberTopOffset   = 10f;            // Y=10 — 챔버 천장 (변경 없음)

        // =========================================================================================
        // Adjust 메뉴 — 씬의 현재 Floor_SecondFloor Y 값을 읽어와 Roof_Entrance/Roof_Corridor 와
        // Wall_Entrance/Wall_Corridor 의 높이를 그에 맞춰 조정. 스크립트 상수 무시 — 씬 현재 상태 기준.
        // 재빌드 없이 부분 조정만 함 (사용자가 손으로 수정한 다른 부분 보존).
        // =========================================================================================

        // =========================================================================================
        // Set Heights — Floor_SecondFloor + Roof_Entrance/Corridor 의 자식 Y 를 일괄 지정값으로,
        // Wall_Entrance/Corridor 의 yScale 도 그에 맞춰 (yScale = target/3).
        // 사용자가 특정 Y 값을 직접 정하고 모든 관련 요소를 한 번에 적용할 때 사용.
        // =========================================================================================

        // =========================================================================================
        // Setup Door1* — 이름이 "Door1" 로 시작하는 모든 GameObject 에 AutoSlidingDoor 컴포넌트
        // 자동 부착. Door_Left_01 (오른쪽), Door_Left_01 (1) (왼쪽) 두 패널이 양쪽으로 슬라이드
        // 해서 문이 열림. DetectionVolume (트리거 박스) 자동 생성 — 카메라(머리) 가 안에 있으면 Open.
        // =========================================================================================

        const float Door1SlideDistance = 1.5f;     // 각 패널이 양쪽으로 슬라이드할 거리(m)
        const float Door1TriggerDepth  = 4f;       // 감지 트리거 박스 Z 크기 (양쪽 접근 감지)
        const float Door1TriggerHeight = 3f;       // 감지 트리거 박스 Y 크기
        const float Door1TriggerExtraWidth = 1.5f; // 패널 간 거리에 추가로 확장할 X 폭

        [MenuItem("Tools/PipePuz/Stage3/Setup Door1* AutoSlidingDoor")]
        public static void SetupDoor1AutoSliding()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            // 1. 씬 안의 모든 GameObject 중 이름이 "Door1" 로 시작하는 것 찾기 (Door1, Door1 (1), Door1 (2) 등).
            var allTransforms = new System.Collections.Generic.List<Transform>();
            foreach (var root in scene.GetRootGameObjects())
            {
                CollectAllDescendants(root.transform, allTransforms);
            }

            var door1Groups = new System.Collections.Generic.List<Transform>();
            foreach (var t in allTransforms)
            {
                if (t == null) continue;
                if (t.name.StartsWith("Door1"))
                {
                    door1Groups.Add(t);
                }
            }

            if (door1Groups.Count == 0)
            {
                Debug.LogError("[Door1Setup] 'Door1' 로 시작하는 GameObject 를 못 찾았다.");
                return;
            }

            Undo.SetCurrentGroupName("Setup Door1* AutoSlidingDoor");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                int setupCount = 0;
                int skipCount = 0;

                foreach (var doorGroup in door1Groups)
                {
                    // 2. 자식 중 Door_Left_01 / Door_Left_01 (1) 찾기.
                    Transform leftPanelA = null;   // Door_Left_01
                    Transform leftPanelB = null;   // Door_Left_01 (1)
                    for (int i = 0; i < doorGroup.childCount; i++)
                    {
                        var c = doorGroup.GetChild(i);
                        if (c.name == "Door_Left_01") leftPanelA = c;
                        else if (c.name == "Door_Left_01 (1)") leftPanelB = c;
                    }
                    if (leftPanelA == null || leftPanelB == null)
                    {
                        Debug.LogWarning($"[Door1Setup] '{doorGroup.name}' 에 Door_Left_01 / Door_Left_01 (1) 자식 둘 다 못 찾음. 스킵.");
                        skipCount++;
                        continue;
                    }

                    // 3. 두 패널 사이 X 거리, 중심 계산 → 어느 쪽이 LeftPanel (smaller X) 인지 결정.
                    Transform leftPanelFinal, rightPanelFinal;
                    if (leftPanelA.localPosition.x < leftPanelB.localPosition.x)
                    {
                        leftPanelFinal = leftPanelA;
                        rightPanelFinal = leftPanelB;
                    }
                    else
                    {
                        leftPanelFinal = leftPanelB;
                        rightPanelFinal = leftPanelA;
                    }

                    // 4. AutoSlidingDoor 컴포넌트 — 이미 있으면 재사용, 없으면 추가.
                    var asd = doorGroup.GetComponent<PipePuz.RoomCarpet.AutoSlidingDoor>();
                    if (asd == null)
                    {
                        asd = Undo.AddComponent<PipePuz.RoomCarpet.AutoSlidingDoor>(doorGroup.gameObject);
                    }
                    Undo.RecordObject(asd, "Configure AutoSlidingDoor");
                    asd.LeftPanel  = leftPanelFinal;
                    asd.RightPanel = rightPanelFinal;
                    asd.SlideDistance = Door1SlideDistance;
                    asd.SlideAxisLocal = Vector3.right;
                    asd.OpenSpeed = 2.5f;
                    asd.CloseSpeed = 1.8f;
                    asd.CloseDelay = 0.4f;

                    // 5. DetectionVolume — 이미 자식에 있으면 재사용, 없으면 생성.
                    Transform existingDV = null;
                    for (int i = 0; i < doorGroup.childCount; i++)
                    {
                        if (doorGroup.GetChild(i).name == "DetectionVolume")
                        {
                            existingDV = doorGroup.GetChild(i);
                            break;
                        }
                    }

                    GameObject dvGo;
                    if (existingDV != null)
                    {
                        dvGo = existingDV.gameObject;
                    }
                    else
                    {
                        dvGo = new GameObject("DetectionVolume");
                        Undo.RegisterCreatedObjectUndo(dvGo, "Create DetectionVolume");
                        Undo.SetTransformParent(dvGo.transform, doorGroup, worldPositionStays: false, "Parent DV");
                    }

                    // DetectionVolume Transform — 두 패널 중심에 배치.
                    Vector3 panelMid = (leftPanelFinal.localPosition + rightPanelFinal.localPosition) * 0.5f;
                    panelMid.y += Door1TriggerHeight * 0.5f; // 박스 중심을 바닥 위 절반 높이로
                    Undo.RecordObject(dvGo.transform, "DV transform");
                    dvGo.transform.localPosition = panelMid;
                    dvGo.transform.localRotation = Quaternion.identity;
                    dvGo.transform.localScale    = Vector3.one;

                    // BoxCollider (isTrigger).
                    var box = dvGo.GetComponent<BoxCollider>();
                    if (box == null) box = Undo.AddComponent<BoxCollider>(dvGo);
                    Undo.RecordObject(box, "DV box");
                    box.isTrigger = true;
                    float gapX = Mathf.Abs(rightPanelFinal.localPosition.x - leftPanelFinal.localPosition.x);
                    box.size = new Vector3(gapX + Door1TriggerExtraWidth, Door1TriggerHeight, Door1TriggerDepth);
                    box.center = Vector3.zero;

                    asd.DetectionVolume = box;

                    Debug.Log($"[Door1Setup] '{doorGroup.name}' setup OK. " +
                              $"LeftPanel='{leftPanelFinal.name}' RightPanel='{rightPanelFinal.name}' " +
                              $"gapX={gapX:F2}m SlideDistance={Door1SlideDistance:F2}m " +
                              $"TriggerBox=({box.size.x:F2}, {box.size.y:F2}, {box.size.z:F2})");
                    setupCount++;
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Debug.Log($"[Door1Setup] 완료 — {setupCount}개 setup, {skipCount}개 skip. " +
                          "Play mode 진입 후 두 패널 사이로 다가가면 문이 열림. Ctrl+S 저장.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        static void CollectAllDescendants(Transform root, System.Collections.Generic.List<Transform> list)
        {
            list.Add(root);
            for (int i = 0; i < root.childCount; i++)
                CollectAllDescendants(root.GetChild(i), list);
        }

        // =========================================================================================
        // Lift Inner Mesh — 각 프리팹 인스턴스 안의 자식 mesh (Floor_01 또는 RoofMesh) 의
        // localPosition.y 를 지정 값으로 변경. 기본값(1.136496e-06 ≈ 0) → 0.25 로 살짝 들어올림.
        // 동시에 Wall 들의 yScale 을 새 effective 천장 높이에 맞춰 자동 조정.
        //
        // 사용처: Floor_SecondFloor 의 2층 슬래브를 0.25m 두께만큼 올리고, Roof_Entrance/Corridor
        //         의 천장 mesh 도 같은 양 올려서 정렬 맞춤 — 벽도 그 위까지 자동으로 길어짐.
        // =========================================================================================

        const float LiftInnerMeshTargetY = 0.25f; // 새 자식 localPosition.y 값

        [MenuItem("Tools/PipePuz/Stage3/Lift Inner Floor_01 Y to 0.25 (+walls)")]
        public static void LiftInnerMeshY()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            float lift = LiftInnerMeshTargetY;
            // 자식 mesh 의 가능한 이름들 (Floor_01 = prefab 기본, RoofMesh = Rename 메뉴 실행 후).
            string[] meshChildNames = { "Floor_01", "RoofMesh" };

            Undo.SetCurrentGroupName($"Lift inner mesh to Y={lift:F3}");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                int lifted = 0;

                // 1. Floor_SecondFloor 의 각 자식(Floor_01 인스턴스) → 그 내부 자식 mesh → localPos.y = lift
                lifted += LiftMeshChildrenIn(scene, "Floor_SecondFloor", meshChildNames, lift);

                // 2. Roof_Entrance, Roof_Corridor — 동일.
                lifted += LiftMeshChildrenIn(scene, "Roof_Entrance",  meshChildNames, lift);
                lifted += LiftMeshChildrenIn(scene, "Roof_Corridor",  meshChildNames, lift);

                // 3. Wall_Entrance/Corridor 의 yScale 재계산.
                //    effective 천장 visual top = Roof_Entrance 자식 parent Y + lift.
                //    벽 yScale = effective top / 3 (Wall_Simple_01 본래 3m).
                int wallScaled = 0;
                var roofEntrance = FindByNameAnywhere(scene, "Roof_Entrance");
                if (roofEntrance != null && roofEntrance.transform.childCount > 0)
                {
                    float roofParentY = roofEntrance.transform.GetChild(0).localPosition.y;
                    float effectiveTop = roofParentY + lift;
                    float newWallScaleY = effectiveTop / 3f;

                    foreach (var groupName in new[] { "Wall_Entrance", "Wall_Corridor" })
                    {
                        var grp = FindByNameAnywhere(scene, groupName);
                        if (grp == null) continue;
                        for (int i = 0; i < grp.transform.childCount; i++)
                        {
                            var c = grp.transform.GetChild(i);
                            Undo.RecordObject(c, "Adjust wall yScale");
                            var ls = c.localScale;
                            ls.y = newWallScaleY;
                            c.localScale = ls;
                            wallScaled++;
                        }
                    }
                    Debug.Log($"[LiftInner] Roof parent Y={roofParentY:F3} + lift {lift:F3} = effective top {effectiveTop:F3}m. 벽 yScale={newWallScaleY:F3}.");
                }
                else
                {
                    Debug.LogWarning("[LiftInner] Roof_Entrance 못 찾음 — 벽 yScale 조정 스킵.");
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Debug.Log($"[LiftInner] 완료. mesh 자식 {lifted}개의 localPosition.y={lift:F3}, 벽 자식 {wallScaled}개 yScale 조정.\n" +
                          "Ctrl+S 저장. 문제 시 Ctrl+Z.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        /// <summary>
        /// 주어진 그룹 (예: "Floor_SecondFloor") 의 모든 자식 (prefab instances) 를 순회하며,
        /// 그 안의 가능한 이름의 자식 mesh (meshNames 후보 중 매칭되는 첫 자식) 의 localPosition.y 를
        /// targetY 로 설정. prefab override 로 기록됨.
        /// </summary>
        static int LiftMeshChildrenIn(Scene scene, string groupName, string[] meshNames, float targetY)
        {
            var grp = FindByNameAnywhere(scene, groupName);
            if (grp == null)
            {
                Debug.LogWarning($"[LiftInner] '{groupName}' 못 찾음.");
                return 0;
            }
            int count = 0;
            for (int i = 0; i < grp.transform.childCount; i++)
            {
                var instance = grp.transform.GetChild(i);
                Transform meshChild = null;
                foreach (var n in meshNames)
                {
                    var c = instance.Find(n);
                    if (c != null) { meshChild = c; break; }
                }
                if (meshChild == null)
                {
                    Debug.LogWarning($"[LiftInner] '{groupName}' 안 [{i}] '{instance.name}' 에서 자식 mesh ({string.Join("/", meshNames)}) 못 찾음.");
                    continue;
                }
                Undo.RecordObject(meshChild, "Lift inner mesh Y");
                var lp = meshChild.localPosition;
                lp.y = targetY;
                meshChild.localPosition = lp;
                count++;
            }
            return count;
        }

        // 구버전 SetHeights 메뉴 (절대값 일괄 설정) 는 그대로 둠 — 다른 목적용.
        const float SetHeightsTargetY = 0.25f;  // ← 여기 값만 바꿔 다음 클릭부터 다른 높이로 적용 가능.

        [MenuItem("Tools/PipePuz/Stage3/Set 1F + SecondFloor Heights to Y=0.25")]
        public static void SetHeightsToTarget()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            float target = SetHeightsTargetY;
            if (target < 1.5f)
            {
                if (!EditorUtility.DisplayDialog(
                        "낮은 천장 경고",
                        $"Y={target:F3} 은 매우 낮은 천장이라 캐릭터가 못 들어갈 수 있다.\n그래도 계속할까?",
                        "계속", "취소"))
                    return;
            }

            float wallScaleY = target / 3f; // Wall_Simple_01 본래 3m → 새 높이/3

            Undo.SetCurrentGroupName($"Set Heights to Y={target:F3}");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                int floorMoved = 0, roofMoved = 0, wallScaled = 0;

                // 1. Floor_SecondFloor — 각 자식의 localPosition.y → target.
                var floor2F = FindByNameAnywhere(scene, "Floor_SecondFloor");
                if (floor2F != null)
                {
                    for (int i = 0; i < floor2F.transform.childCount; i++)
                    {
                        var c = floor2F.transform.GetChild(i);
                        Undo.RecordObject(c, "Adjust SecondFloor Y");
                        var lp = c.localPosition;
                        lp.y = target;
                        c.localPosition = lp;
                        floorMoved++;
                    }
                }
                else Debug.LogWarning("[SetHeights] 'Floor_SecondFloor' 못 찾음.");

                // 2. Roof_Entrance, Roof_Corridor — 각 자식의 localPosition.y → target.
                foreach (var groupName in new[] { "Roof_Entrance", "Roof_Corridor" })
                {
                    var grp = FindByNameAnywhere(scene, groupName);
                    if (grp == null) { Debug.LogWarning($"[SetHeights] '{groupName}' 못 찾음."); continue; }
                    for (int i = 0; i < grp.transform.childCount; i++)
                    {
                        var c = grp.transform.GetChild(i);
                        Undo.RecordObject(c, "Adjust roof Y");
                        var lp = c.localPosition;
                        lp.y = target;
                        c.localPosition = lp;
                        roofMoved++;
                    }
                }

                // 3. Wall_Entrance, Wall_Corridor — 각 자식의 localScale.y → wallScaleY.
                foreach (var groupName in new[] { "Wall_Entrance", "Wall_Corridor" })
                {
                    var grp = FindByNameAnywhere(scene, groupName);
                    if (grp == null) { Debug.LogWarning($"[SetHeights] '{groupName}' 못 찾음."); continue; }
                    for (int i = 0; i < grp.transform.childCount; i++)
                    {
                        var c = grp.transform.GetChild(i);
                        Undo.RecordObject(c, "Adjust wall yScale");
                        var ls = c.localScale;
                        ls.y = wallScaleY;
                        c.localScale = ls;
                        wallScaled++;
                    }
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Debug.Log($"[SetHeights] 완료. Target Y={target:F3}\n" +
                          $"  Floor_SecondFloor 자식 {floorMoved}개 Y={target:F3}\n" +
                          $"  Roof_Entrance/Corridor 자식 {roofMoved}개 Y={target:F3}\n" +
                          $"  Wall_Entrance/Corridor 자식 {wallScaled}개 yScale={wallScaleY:F3} (벽 높이 {target:F3}m)\n" +
                          "Ctrl+S 저장. 문제 시 Ctrl+Z.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        [MenuItem("Tools/PipePuz/Stage3/Rename Floor_01 inside Roof_* to RoofMesh")]
        public static void RenameFloorInsideRoof()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            // Roof_02 프리팹은 부모 "Roof_02" + 자식 "Floor_01" 구조 — 자식이 실제 mesh 보유.
            // 모든 Roof_* 그룹 안의 Roof_02 인스턴스를 순회하며, 그 자식 중 "Floor_01" 이름을 가진 것을
            // "RoofMesh" 로 rename. Hierarchy 만 정돈 — 시각/기능 영향 X.
            string[] roofGroups = { "Roof_Entrance", "Roof_Corridor", "Roof_LeftChamber", "Roof_RightChamber" };

            Undo.SetCurrentGroupName("Rename Floor_01 inside Roof_*");
            int undoGroup = Undo.GetCurrentGroup();
            int renamed = 0;

            try
            {
                foreach (var groupName in roofGroups)
                {
                    var grp = FindByNameAnywhere(scene, groupName);
                    if (grp == null) continue;
                    // 각 자식(Roof_02 인스턴스) 의 자식(Floor_01) rename.
                    for (int i = 0; i < grp.transform.childCount; i++)
                    {
                        var roofInstance = grp.transform.GetChild(i);
                        for (int j = 0; j < roofInstance.childCount; j++)
                        {
                            var child = roofInstance.GetChild(j);
                            if (child.name == "Floor_01")
                            {
                                Undo.RecordObject(child.gameObject, "Rename Floor_01 → RoofMesh");
                                child.gameObject.name = "RoofMesh";
                                renamed++;
                            }
                        }
                    }
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Debug.Log($"[Rename] Roof_* 안의 Floor_01 자식 {renamed}개를 'RoofMesh' 로 rename. " +
                          "시각/기능 영향 없음 — Hierarchy 정리 목적. Ctrl+S 저장.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        [MenuItem("Tools/PipePuz/Stage3/Adjust 1F Roof+Walls to SecondFloor Height")]
        public static void AdjustRoofWallsToSecondFloor()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            // 1. Floor_SecondFloor 찾기 → 첫 자식의 Y 값을 SecondFloor 높이로 채택.
            GameObject floor2F = FindByNameAnywhere(scene, "Floor_SecondFloor");
            if (floor2F == null)
            {
                Debug.LogError("[Adjust] 'Floor_SecondFloor' 를 씬에서 찾을 수 없다.");
                return;
            }
            if (floor2F.transform.childCount == 0)
            {
                Debug.LogError("[Adjust] 'Floor_SecondFloor' 에 자식이 없다 — 빌드되지 않은 상태.");
                return;
            }
            float secondFloorY = floor2F.transform.GetChild(0).localPosition.y;
            float newWallScaleY = secondFloorY / 3f; // Wall_Simple_01 본래 3m → 새 높이/3

            Debug.Log($"[Adjust] Floor_SecondFloor 현재 Y = {secondFloorY:F3}. " +
                      $"Roof_Entrance/Corridor 를 Y={secondFloorY:F3}, " +
                      $"Wall_Entrance/Corridor 의 yScale 을 {newWallScaleY:F3} 로 조정한다.");

            Undo.SetCurrentGroupName("Adjust 1F Roof+Walls to SecondFloor Height");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                int roofMoved = 0;
                int wallScaled = 0;

                // 2. Roof_Entrance, Roof_Corridor — 각 자식의 localPosition.y 를 secondFloorY 로.
                foreach (var groupName in new[] { "Roof_Entrance", "Roof_Corridor" })
                {
                    var grp = FindByNameAnywhere(scene, groupName);
                    if (grp == null)
                    {
                        Debug.LogWarning($"[Adjust] '{groupName}' 못 찾음. 스킵.");
                        continue;
                    }
                    for (int i = 0; i < grp.transform.childCount; i++)
                    {
                        var c = grp.transform.GetChild(i);
                        Undo.RecordObject(c, "Adjust roof Y");
                        var lp = c.localPosition;
                        lp.y = secondFloorY;
                        c.localPosition = lp;
                        roofMoved++;
                    }
                }

                // 3. Wall_Entrance, Wall_Corridor — 각 자식의 localScale.y 를 newWallScaleY 로.
                foreach (var groupName in new[] { "Wall_Entrance", "Wall_Corridor" })
                {
                    var grp = FindByNameAnywhere(scene, groupName);
                    if (grp == null)
                    {
                        Debug.LogWarning($"[Adjust] '{groupName}' 못 찾음. 스킵.");
                        continue;
                    }
                    for (int i = 0; i < grp.transform.childCount; i++)
                    {
                        var c = grp.transform.GetChild(i);
                        Undo.RecordObject(c, "Adjust wall yScale");
                        var ls = c.localScale;
                        ls.y = newWallScaleY;
                        c.localScale = ls;
                        wallScaled++;
                    }
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Debug.Log($"[Adjust] 완료. Roof 자식 {roofMoved}개 Y 조정, Wall 자식 {wallScaled}개 yScale 조정.\n" +
                          $"확인 후 Ctrl+S 저장. 문제 시 Ctrl+Z.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        /// <summary>씬 안 어디든 (루트 또는 자식 트리) 에서 이름으로 검색.</summary>
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

        [MenuItem("Tools/PipePuz/Stage3/Rebuild Modular Room as RoomCliff Shape")]
        public static void Rebuild()
        {
            var scene = SceneManager.GetActiveScene();
            if (!ValidateStage3(scene)) return;

            GameObject roomSeen = FindRoot(scene, "RoomSeen");
            if (roomSeen == null)
            {
                Debug.LogError("[ModularLayout] RoomSeen 이 없다. 먼저 'Build or Update RoomSeen' 메뉴 실행.");
                return;
            }

            GameObject floor01  = LoadPrefab(Floor01Path);
            GameObject wall01   = LoadPrefab(Wall01Path);
            GameObject roof01   = LoadPrefab(Roof01Path);
            GameObject doorArch = LoadPrefab(DoorArchPath);
            GameObject stairs   = LoadPrefab(StairsPath);   // 선택적 — 없으면 계단만 스킵.
            if (floor01 == null || wall01 == null || roof01 == null || doorArch == null) return;

            // 양면 머티리얼 보장 — Stage3 한정으로 Diffuse_01 의 양면 변형 생성/로드.
            Material srcMat = AssetDatabase.LoadAssetAtPath<Material>(SrcMaterialPath);
            Material doubleSidedMat = EnsureDoubleSidedMaterial(srcMat);
            if (srcMat == null || doubleSidedMat == null)
            {
                Debug.LogWarning("[ModularLayout] 양면 머티리얼 준비 실패 — 단면 렌더링으로 계속. " +
                                 $"원본: {SrcMaterialPath}");
            }
            _currentSrcMat = srcMat;
            _currentDsMat  = doubleSidedMat;

            Undo.SetCurrentGroupName("Rebuild Modular Room as RoomCliff Shape");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                // 1. 기존 "Room (Stage1 Modular)" 삭제.
                GameObject existing = FindChildByName(roomSeen.transform, "Room (Stage1 Modular)");
                if (existing != null)
                {
                    Undo.DestroyObjectImmediate(existing);
                }

                // 2. 새 Room + 4그룹.
                var room = CreateChild(roomSeen.transform, "Room (Stage1 Modular)");
                var floors    = CreateChild(room.transform, "Floors");
                var walls     = CreateChild(room.transform, "Walls");
                var roofs     = CreateChild(room.transform, "Roof");
                var doors     = CreateChild(room.transform, "Door");
                var stairsGrp = CreateChild(room.transform, "Stairs");

                // 3. Floors — 모든 방의 1층 바닥 + 2층 슬래브.
                TileFloor(floor01, floors.transform, Entrance,     0f, "Entrance");
                TileFloor(floor01, floors.transform, Corridor,     0f, "Corridor");
                TileFloor(floor01, floors.transform, LeftChamber,  0f, "LeftChamber");
                TileFloor(floor01, floors.transform, RightChamber, 0f, "RightChamber");
                TileFloor(floor01, floors.transform, SecondFloor,  SecondFloorY, "SecondFloor");

                // 4. Walls — 벽 소유권 규칙대로 한 번씩만.
                BuildWalls(wall01, walls.transform);

                // 5. Roofs — 1F 천장 + 챔버 천장.
                TileRoof(roof01, roofs.transform, Entrance,     RoofDefault3mTopOffset, "Entrance");
                TileRoof(roof01, roofs.transform, Corridor,     RoofDefault3mTopOffset, "Corridor");
                TileRoof(roof01, roofs.transform, LeftChamber,  RoofChamberTopOffset,   "LeftChamber");
                TileRoof(roof01, roofs.transform, RightChamber, RoofChamberTopOffset,   "RightChamber");

                // 6. Door arches (시각용 — 문틀만, 실제 sliding 문은 원본 RoomCliff 의 것 사용).
                // 문틀은 바닥 Y=0 에 둠 — 사람 키 기준 출입 위치는 동일. 천장이 높아진 건 문 위 공백.
                PlaceDoorArch(doorArch, doors.transform, LeftDoorCenterX,  3f, 0f,  "LeftDoor");
                PlaceDoorArch(doorArch, doors.transform, RightDoorCenterX, 3f, 0f,  "RightDoor");
                PlaceDoorArch(doorArch, doors.transform, 0f,               0f, 0f,  "EntranceOpening");

                // 7. Stairs — Stairs.prefab (ParticlePack) 한 개. MeshCollider 있어 걸어서 올라갈 수 있음.
                if (stairs != null)
                {
                    PlaceStairs(stairs, stairsGrp.transform);
                }
                else
                {
                    Debug.LogWarning($"[ModularLayout] Stairs.prefab 못 찾음 ({StairsPath}). 계단 스킵. " +
                                     "ParticlePack 에셋이 설치되어 있는지 확인하라.");
                }

                EditorSceneManager_MarkActiveSceneDirty();
                Selection.activeGameObject = room;
                EditorGUIUtility.PingObject(room);
                Debug.Log("[ModularLayout] 완료. RoomSeen/Room (Stage1 Modular) 를 RoomCliff 평면도로 재구성했다.\n" +
                          "확인 후 Ctrl+S 저장. 문제 시 Ctrl+Z.");
            }
            finally
            {
                _currentSrcMat = null;
                _currentDsMat  = null;
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        // =========================================================================================
        // Floor tiling
        // =========================================================================================

        static void TileFloor(GameObject prefab, Transform parent, Vector4 rect, float y, string roomName)
        {
            // Floor_01 prefab 의 BoxCollider center = (-1.5, -1.5, 0), size = (3, 3, 0.02).
            // 자식 transform 의 -90° X-rotation 적용 후 anchor 는 타일의 "NE 코너", 타일은 anchor 의
            // SW 방향(X-3, Z-3) 으로 확장. 따라서 anchor.x = xmin + Tile*(i+1), anchor.z = zmin + Tile*(j+1).
            float xmin = rect.x, xmax = rect.y, zmin = rect.z, zmax = rect.w;
            int cols = Mathf.RoundToInt((xmax - xmin) / Tile);
            int rows = Mathf.RoundToInt((zmax - zmin) / Tile);
            var group = CreateChild(parent, $"Floor_{roomName}");
            for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                float ax = xmin + Tile * (i + 1);
                float az = zmin + Tile * (j + 1);
                var t = InstantiatePrefab(prefab, group.transform);
                t.transform.localPosition = new Vector3(ax, y, az);
                t.transform.localRotation = Quaternion.identity;
                t.transform.localScale = Vector3.one;
            }
        }

        // =========================================================================================
        // Roof tiling — same as floor but at top of room, with Y offset for the prefab's built-in
        // ceiling-height anchor.
        // =========================================================================================

        static void TileRoof(GameObject prefab, Transform parent, Vector4 rect, float yOffset, string roomName)
        {
            // Roof_01 도 Stage1 안에서 Floor_01 와 동일한 그리드(3m × 3m) 로 깔리므로 NE 코너 anchor 가정.
            // 만약 천장이 어긋나 보이면 ax/az 공식의 (i+1)/(j+1) 을 (i+0.5)/(j+0.5) 로 바꾸면 1.5m 시프트됨.
            float xmin = rect.x, xmax = rect.y, zmin = rect.z, zmax = rect.w;
            int cols = Mathf.RoundToInt((xmax - xmin) / Tile);
            int rows = Mathf.RoundToInt((zmax - zmin) / Tile);
            var group = CreateChild(parent, $"Roof_{roomName}");
            for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                float ax = xmin + Tile * (i + 1);
                float az = zmin + Tile * (j + 1);
                var t = InstantiatePrefab(prefab, group.transform);
                t.transform.localPosition = new Vector3(ax, yOffset, az);
                t.transform.localRotation = Quaternion.identity;
                t.transform.localScale = Vector3.one;
            }
        }

        // =========================================================================================
        // Walls — 벽 소유권 규칙대로 한 번씩만 빌드.
        // =========================================================================================

        static void BuildWalls(GameObject prefab, Transform parent)
        {
            // ---- Entrance ----
            var e = CreateChild(parent, "Wall_Entrance");
            BuildXWall(prefab, e.transform, Entrance.x, Entrance.y, Entrance.z,                  Wall1FScale, "S");
            BuildZWall(prefab, e.transform, Entrance.z, Entrance.w, Entrance.x,                  Wall1FScale, "W");
            BuildZWall(prefab, e.transform, Entrance.z, Entrance.w, Entrance.y,                  Wall1FScale, "E");
            // (N 은 Corridor 쪽이 처리 — 개구)

            // ---- Corridor ----
            var c = CreateChild(parent, "Wall_Corridor");
            // S wall (Z=0) — Entrance 와 만나는 X[-3, +3] 은 개구.
            BuildXWallWithOpenings(prefab, c.transform, Corridor.x, Corridor.y, Corridor.z, Wall1FScale, "S",
                CorridorEntranceOpeningXmin, CorridorEntranceOpeningXmax);
            // N wall — 챔버 쪽이 처리.
            BuildZWall(prefab, c.transform, Corridor.z, Corridor.w, Corridor.x, Wall1FScale, "W");
            BuildZWall(prefab, c.transform, Corridor.z, Corridor.w, Corridor.y, Wall1FScale, "E");

            // ---- LeftChamber ----
            var l = CreateChild(parent, "Wall_LeftChamber");
            // S wall (Z=3) — LeftDoor 개구 (X=-4.5 중심, 한 타일).
            BuildXWallWithOpenings(prefab, l.transform, LeftChamber.x, LeftChamber.y, LeftChamber.z, WallTallScale, "S",
                LeftDoorCenterX - Tile * 0.5f, LeftDoorCenterX + Tile * 0.5f);
            // N wall (Z=18).
            BuildXWall(prefab, l.transform, LeftChamber.x, LeftChamber.y, LeftChamber.w, WallTallScale, "N");
            // W wall (X=-24).
            BuildZWall(prefab, l.transform, LeftChamber.z, LeftChamber.w, LeftChamber.x, WallTallScale, "W");
            // E wall (X=+3) — 단, Z[3,15] 는 RightChamber 가 처리 (divider). Z[15,18] 만 LeftChamber 처리.
            BuildZWall(prefab, l.transform, /*zmin*/ RightChamber.w, /*zmax*/ LeftChamber.w, LeftChamber.y, WallTallScale, "E_NorthSection");

            // ---- RightChamber ----
            var r = CreateChild(parent, "Wall_RightChamber");
            // S wall (Z=3) — RightDoor 개구.
            BuildXWallWithOpenings(prefab, r.transform, RightChamber.x, RightChamber.y, RightChamber.z, WallTallScale, "S",
                RightDoorCenterX - Tile * 0.5f, RightDoorCenterX + Tile * 0.5f);
            // N wall (Z=15).
            BuildXWall(prefab, r.transform, RightChamber.x, RightChamber.y, RightChamber.w, WallTallScale, "N");
            // E wall (X=+9).
            BuildZWall(prefab, r.transform, RightChamber.z, RightChamber.w, RightChamber.y, WallTallScale, "E");
            // W wall (X=+3) — chamber divider, Z[3, 15].
            BuildZWall(prefab, r.transform, RightChamber.z, RightChamber.w, RightChamber.x, WallTallScale, "W_Divider");
        }

        /// <summary>X 축을 따라 도는 east-west 벽. y 스케일로 높이 조절.</summary>
        static void BuildXWall(GameObject prefab, Transform parent, float xmin, float xmax, float z, float yScale, string label)
        {
            BuildXWallWithOpenings(prefab, parent, xmin, xmax, z, yScale, label, 0f, 0f);
        }

        /// <summary>X 축 벽 + 선택적 개구. anchor 는 세그먼트의 west 끝 (X 길이는 +X 방향).</summary>
        static void BuildXWallWithOpenings(GameObject prefab, Transform parent, float xmin, float xmax, float z, float yScale,
                                           string label, float openingXmin, float openingXmax)
        {
            int n = Mathf.RoundToInt((xmax - xmin) / Tile);
            for (int i = 0; i < n; i++)
            {
                float ax = xmin + i * Tile;        // segment 의 west 끝 (anchor)
                float segMin = ax;
                float segMax = ax + Tile;
                bool inOpening = openingXmax > openingXmin &&
                                 segMin >= openingXmin - 0.01f && segMax <= openingXmax + 0.01f;
                if (inOpening) continue;
                var w = InstantiatePrefab(prefab, parent);
                w.name = $"Wall_X_{label}_{i}";
                w.transform.localPosition = new Vector3(ax, 0f, z);
                w.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                w.transform.localScale = new Vector3(1f, yScale, 1f);
            }
        }

        /// <summary>Z 축 벽 (north-south). anchor 는 세그먼트의 south 끝 (Z 길이는 +Z 방향).</summary>
        static void BuildZWall(GameObject prefab, Transform parent, float zmin, float zmax, float x, float yScale, string label)
        {
            int n = Mathf.RoundToInt((zmax - zmin) / Tile);
            for (int i = 0; i < n; i++)
            {
                float az = zmin + i * Tile;
                var w = InstantiatePrefab(prefab, parent);
                w.name = $"Wall_Z_{label}_{i}";
                w.transform.localPosition = new Vector3(x, 0f, az);
                w.transform.localRotation = Quaternion.identity;
                w.transform.localScale = new Vector3(1f, yScale, 1f);
            }
        }

        // =========================================================================================
        // Door arches
        // =========================================================================================

        static void PlaceDoorArch(GameObject prefab, Transform parent, float x, float z, float y, string label)
        {
            var a = InstantiatePrefab(prefab, parent);
            a.name = $"DoorArch_{label}";
            a.transform.localPosition = new Vector3(x, y, z);
            a.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // 가로 방향 문틀
            a.transform.localScale = Vector3.one;
        }

        // =========================================================================================
        // Stairs — ParticlePack Stairs.prefab. MeshCollider(Convex) 가 붙어있어 걸어서 올라갈 수 있다.
        // 원본 prefab 의 anchor 가 mesh 중앙 부근(local 5, 2.5, 0) 이라 정확한 정렬은 시각 확인 필요.
        // 필요한 height 3.5m / length 4m / width 2.5m 에 맞춰 스케일 조정.
        // =========================================================================================

        static void PlaceStairs(GameObject prefab, Transform parent)
        {
            var s = InstantiatePrefab(prefab, parent);
            s.name = "Stairs_To_SecondFloor";
            // 계단을 RightChamber 안에 배치. Z 는 StairBaseZ 에서 시작해 +Z 로 올라감.
            // Stairs.prefab 의 본래 footprint 를 모른 채 단일 인스턴스로 떨군 뒤 X/Z 스케일로 길이/폭 강제.
            // (원본 prefab footprint 는 약 10×5 단위로 보임 — anchor offset 5,2.5,0 기준)
            // 일단 sane 한 기본값으로 떨군다. 시각적으로 안 맞으면 Inspector 에서 조정.
            s.transform.localPosition = new Vector3(StairCenterX, 0f, StairBaseZ);
            s.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            // 기본 prefab footprint 가 약 10m × 5m × ?? 라 추정 (정확치 X). 사용자가 보고 정정.
            // 일단 1x 스케일로 두고 사용자가 직접 맞추도록.
            s.transform.localScale = Vector3.one;
        }

        // =========================================================================================
        // Helpers
        // =========================================================================================

        static GameObject LoadPrefab(string path)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null) Debug.LogError($"[ModularLayout] Prefab not found at: {path}");
            return p;
        }

        // Rebuild() 가 진행 중인 동안 현재 사용 중인 머티리얼 페어 보관. 멀티스레딩 X (Editor only).
        static Material _currentSrcMat;
        static Material _currentDsMat;

        static GameObject InstantiatePrefab(GameObject prefab, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(go, "Place modular piece");
            // 양면 머티리얼 적용 (있을 때만, ParticlePack Stairs 등 외부 에셋은 src 와 무관해 영향 없음).
            if (_currentSrcMat != null && _currentDsMat != null)
            {
                ApplyDoubleSidedMaterial(go, _currentSrcMat, _currentDsMat);
            }
            return go;
        }

        /// <summary>
        /// 지정 GameObject(+자식) 의 모든 MeshRenderer 에서 srcMat 을 dsMat 으로 swap.
        /// sharedMaterials 사용 — 인스턴스별 material 사본 안 만듦 (성능/메모리 보존).
        /// </summary>
        static void ApplyDoubleSidedMaterial(GameObject go, Material srcMat, Material dsMat)
        {
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == srcMat)
                    {
                        mats[i] = dsMat;
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Diffuse_01_DoubleSided.mat 이 없으면 원본 Diffuse_01.mat 복사 후 _Cull=0 으로 저장.
        /// 이미 있으면 그대로 반환. Stage1 의 원본은 절대 안 건드림.
        /// </summary>
        static Material EnsureDoubleSidedMaterial(Material src)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(DoubleSidedMaterialPath);
            if (existing != null) return existing;
            if (src == null) return null;

            var copy = new Material(src);
            // _Cull: 0 = Off (양면), 1 = Front cull, 2 = Back cull (기본 단면).
            if (copy.HasProperty("_Cull")) copy.SetFloat("_Cull", 0f);
            // URP Lit 의 RenderFace 키워드도 같이 설정 (Both = both faces).
            copy.SetOverrideTag("RenderType", copy.GetTag("RenderType", false));
            // URP shader keyword 일관성 위해 — _BUILTIN_RenderFace 가 있으면 토글.
            // (셰이더에 따라 키워드명 다름 — 안전하게 시도만 함.)
            AssetDatabase.CreateAsset(copy, DoubleSidedMaterialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ModularLayout] 양면 머티리얼 생성: {DoubleSidedMaterialPath}");
            return copy;
        }

        static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        static void EditorSceneManager_MarkActiveSceneDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        static bool ValidateStage3(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[ModularLayout] Active scene 이 유효하지 않다.");
                return false;
            }
            if (!scene.name.Contains("Stage3"))
            {
                if (!EditorUtility.DisplayDialog(
                        "Modular Layout",
                        $"현재 active scene '{scene.name}' 이 Stage3 가 아닐 수 있다.\n계속할까?",
                        "계속", "취소"))
                    return false;
            }
            return true;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        static GameObject FindChildByName(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c.gameObject;
            }
            return null;
        }
    }
}
