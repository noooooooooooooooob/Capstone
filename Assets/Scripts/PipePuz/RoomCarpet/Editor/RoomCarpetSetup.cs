using UnityEditor;
using UnityEngine;

namespace PipePuz.RoomCarpet.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Build Room Carpet.
    /// 'RoomCarpet' 이름의 GameObject 안에 Floor / StartZone / GoalZone /
    /// Dispenser / ActiveCarpets / Controller 를 자동 생성한다.
    ///
    /// 게임 진행:
    ///   1. 사용자가 StartZone 위에서 시작 (XR Origin 의 위치 셋업은 수동).
    ///   2. Dispenser 위에 떠 있는 카펫을 잡아 던짐.
    ///   3. 카펫이 Floor (CarpetFloor 마커) 에 닿으면 거기 안착 (5초 수명).
    ///   4. 사용자는 그 카펫 위를 **직접 걸어서** 이동 (continuous locomotion).
    ///   5. 카펫 / StartZone / GoalZone 어디에도 걸쳐있지 않으면 StartZone 으로 즉시 리스폰.
    ///   6. GoalZone 도달 시 OnSolved.
    /// </summary>
    public static class RoomCarpetSetup
    {
        // 룸 사이즈
        const float FloorWidth = 5f;
        const float FloorDepth = 5f;
        const float FloorThickness = 0.05f;

        // 안전 영역
        const float ZoneWidth = 1.2f;
        const float ZoneDepth = 1.2f;
        const float ZoneThickness = 0.01f;
        const float ZoneOffsetX = 1.8f; // 시작/도착 영역이 floor 중심에서 떨어진 거리
        const float ZoneTopY = 0.03f;   // floor 윗면 살짝 위

        // Goal 트리거 (수직 박스 — 카메라가 어느 높이에 있어도 감지)
        const float GoalTriggerHeight = 3.0f;

        // Dispenser
        const float DispenserStandHeight = 1.0f;
        const float DispenserStandRadius = 0.10f;
        static readonly Vector3 DispenserPos = new Vector3(-2.5f, 0f, 0f);
        const float SpawnPointY = 1.10f; // 카펫이 떠 있을 높이

        // 카펫
        static readonly Vector2 CarpetSize = new Vector2(0.9f, 1.2f);
        const float CarpetThickness = 0.02f;
        const float CarpetLifetime = 5f;
        const float CarpetWarningSeconds = 1.5f;

        [MenuItem("Tools/PipePuz/Build Room Carpet")]
        public static void Build()
        {
            var room = GameObject.Find("RoomCarpet");
            if (room == null)
            {
                EditorUtility.DisplayDialog("RoomCarpet",
                    "씬에서 'RoomCarpet' 오브젝트를 찾을 수 없습니다.\nPipe Scene 을 열고 다시 시도하세요.", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Room Carpet");

            // 기존 자식 정리.
            DestroyChildIfExists(room.transform, "Floor");
            DestroyChildIfExists(room.transform, "StartZone");
            DestroyChildIfExists(room.transform, "GoalZone");
            DestroyChildIfExists(room.transform, "Dispenser");
            DestroyChildIfExists(room.transform, "ActiveCarpets");
            var oldCtrl = room.GetComponent<DisappearingCarpetController>();
            if (oldCtrl != null) Undo.DestroyObjectImmediate(oldCtrl);

            // 머티리얼.
            var floorMat = MakeEmissiveMaterial(
                "Carpet_FloorMat",
                new Color(0.55f, 0.10f, 0.10f),
                new Color(1.0f, 0.18f, 0.18f) * 0.8f);
            var startMat = MakeEmissiveMaterial(
                "Carpet_StartMat",
                new Color(0.15f, 0.7f, 0.30f),
                new Color(0.25f, 1.4f, 0.5f) * 0.7f);
            var goalMat = MakeEmissiveMaterial(
                "Carpet_GoalMat",
                new Color(0.20f, 0.55f, 1.0f),
                new Color(0.35f, 0.85f, 1.6f) * 0.9f);
            var standMat = MakeUrpMaterial(
                "Carpet_StandMat",
                new Color(0.35f, 0.32f, 0.30f), false);
            var carpetMat = MakeUrpMaterial(
                "Carpet_CarpetMat",
                new Color(0.70f, 0.45f, 0.25f), false);

            // 컨트롤러.
            var ctrl = room.AddComponent<DisappearingCarpetController>();

            // Floor — 위험 바닥. CarpetFloor 마커 + 외형.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(room.transform, false);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localScale = new Vector3(FloorWidth, FloorThickness, FloorDepth);
            AssignMat(floor, floorMat);
            floor.AddComponent<CarpetFloor>();

            // StartZone — 시작 안전 영역. 직접 걸어다니므로 TeleportationArea 는 부착하지 않는다.
            var start = GameObject.CreatePrimitive(PrimitiveType.Cube);
            start.name = "StartZone";
            start.transform.SetParent(room.transform, false);
            start.transform.localPosition = new Vector3(-ZoneOffsetX, ZoneTopY, 0f);
            start.transform.localScale = new Vector3(ZoneWidth, ZoneThickness, ZoneDepth);
            AssignMat(start, startMat);

            // GoalZone — 빈 root + trigger BoxCollider + CarpetGoalZone.
            // Visual 자식은 실제 텔레포트할 평면.
            var goal = new GameObject("GoalZone");
            goal.transform.SetParent(room.transform, false);
            goal.transform.localPosition = new Vector3(+ZoneOffsetX, GoalTriggerHeight * 0.5f, 0f);

            var goalTrigger = goal.AddComponent<BoxCollider>();
            goalTrigger.size = new Vector3(ZoneWidth, GoalTriggerHeight, ZoneDepth);
            goalTrigger.isTrigger = true;

            var goalComp = goal.AddComponent<CarpetGoalZone>();

            var goalVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            goalVis.name = "Visual";
            goalVis.transform.SetParent(goal.transform, false);
            // Goal 의 center.y 가 1.5 인 반면 시각은 ZoneTopY(=0.03) 에 위치 → 로컬 Y 보정.
            goalVis.transform.localPosition = new Vector3(0f, ZoneTopY - (GoalTriggerHeight * 0.5f), 0f);
            goalVis.transform.localScale = new Vector3(ZoneWidth, ZoneThickness, ZoneDepth);
            AssignMat(goalVis, goalMat);

            // Dispenser — 받침 + spawn point + 컴포넌트.
            var disp = new GameObject("Dispenser");
            disp.transform.SetParent(room.transform, false);
            disp.transform.localPosition = DispenserPos;

            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "Stand";
            var standCol = stand.GetComponent<Collider>();
            if (standCol != null) Object.DestroyImmediate(standCol);
            stand.transform.SetParent(disp.transform, false);
            stand.transform.localPosition = new Vector3(0f, DispenserStandHeight * 0.5f, 0f);
            stand.transform.localScale = new Vector3(DispenserStandRadius * 2f, DispenserStandHeight * 0.5f, DispenserStandRadius * 2f);
            AssignMat(stand, standMat);

            var spawnPoint = new GameObject("SpawnPoint");
            spawnPoint.transform.SetParent(disp.transform, false);
            spawnPoint.transform.localPosition = new Vector3(0f, SpawnPointY, 0f);

            var dispComp = disp.AddComponent<CarpetDispenser>();
            dispComp.SpawnPoint = spawnPoint.transform;
            dispComp.CarpetMaterial = carpetMat;
            dispComp.CarpetSize = CarpetSize;
            dispComp.CarpetThickness = CarpetThickness;
            dispComp.CarpetLifetime = CarpetLifetime;
            dispComp.CarpetWarningSeconds = CarpetWarningSeconds;

            // ActiveCarpets — 동적 생성되는 카펫들의 부모.
            var active = new GameObject("ActiveCarpets");
            active.transform.SetParent(room.transform, false);
            active.transform.localPosition = Vector3.zero;
            dispComp.ActiveCarpetsRoot = active.transform;

            // 컨트롤러 wire-up.
            ctrl.Dispenser = dispComp;
            ctrl.Goal = goalComp;
            ctrl.ActiveCarpetsRoot = active.transform;
            ctrl.FloorCollider = floor.GetComponent<BoxCollider>();
            ctrl.StartZoneCollider = start.GetComponent<BoxCollider>();
            ctrl.GoalZoneCollider = goalTrigger;
            ctrl.StartPoint = start.transform;
            ctrl.OverlapRadius = 0.15f;
            ctrl.RespawnCooldown = 1.0f;
            // ctrl.XROriginRef 는 비워둠 — 런타임 Start 에서 자동 검색.

            EditorUtility.SetDirty(room);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(room.scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[RoomCarpet] Build 완료. " +
                      "Play 모드 진입 시 Dispenser 위에 첫 카펫이 spawn 됩니다. " +
                      "XR Origin 에 Continuous Move Provider 가 있어야 직접 걷기로 이동 가능. " +
                      "초기 위치는 StartZone 위로 수동 셋업하세요.");
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

        static void AssignMat(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }
    }
}
