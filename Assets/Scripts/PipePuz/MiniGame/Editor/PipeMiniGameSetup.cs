using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.MiniGame.EditorTools
{
    /// <summary>
    /// PipeMiniGame GameObject 안에 5×3 격자 파이프 퍼즐 패널을 자동 생성한다.
    /// 메뉴: Tools > PipePuz > Build Pipe MiniGame
    ///
    /// 생성 결과:
    ///   PipeMiniGame
    ///   ├── PipeMiniGameBoard (component)
    ///   ├── Panel             (배경 큐브)
    ///   ├── Cell_0_1_Source   (고정, 회전 불가)
    ///   ├── Cell_1_1_Elbow    (회전 가능)
    ///   ├── Cell_2_2_Straight ...
    ///   └── Cell_4_1_Sink     (고정)
    ///
    /// 패널은 PipeMiniGame 의 +Z 방향을 정면으로 가정한다 — VR 벽면 패널로 사용 시
    /// 부모 PipeMiniGame 자체를 벽 표면에 위치/회전시키면 된다.
    /// </summary>
    public static class PipeMiniGameSetup
    {
        // ----- 디멘션 -----
        const float CellSize = 0.18f;        // 셀 한 변 (m)
        const float ArmThickness = 0.025f;   // 파이프 두께
        const float HubSize = 0.05f;         // 중앙 허브 큐브 한 변
        const float Margin = 0.04f;          // 패널 가장자리 여백
        const float PanelZ = -0.025f;        // 패널 중심 Z (셀 뒤쪽)
        const float PanelThickness = 0.02f;
        const float MarkerSize = 0.07f;      // Source/Sink 마커 sphere 지름

        // ----- 레이아웃 정의 -----
        struct CellDef
        {
            public PipeShape Shape;
            public int Rotation;
            public bool IsFixed;
        }

        static CellDef D(PipeShape s, int r) => new CellDef { Shape = s, Rotation = r, IsFixed = false };
        static CellDef Src(int r) => new CellDef { Shape = PipeShape.Source, Rotation = r, IsFixed = true };
        static CellDef Snk(int r) => new CellDef { Shape = PipeShape.Sink, Rotation = r, IsFixed = true };
        static CellDef Empty() => new CellDef { Shape = PipeShape.None };

        // 5 cols × 3 rows. 행 인덱스 0 = 위 (Y=2), 2 = 아래 (Y=0).
        // 풀이 경로: Source(0,1) → (1,1)Elbow rot 3 → (1,2)Elbow rot 1 → (2,2)Straight rot 1
        //          → (3,2)Elbow rot 2 → (3,1)Elbow rot 0 → Sink(4,1).
        // 초기 회전은 모두 풀이와 다르게 둬 사용자가 회전시키게 한다.
        static readonly CellDef[,] DefaultLayout = new CellDef[3, 5]
        {
            // Y=2 (top) — 경로 일부 (Elbow, Straight, Elbow)
            { Empty(),         D(PipeShape.Elbow, 0),     D(PipeShape.Straight, 0),  D(PipeShape.Elbow, 0),     Empty() },
            // Y=1 (middle) — Source, Elbow, 빈칸, Elbow, Sink
            { Src(0),          D(PipeShape.Elbow, 0),     Empty(),                    D(PipeShape.Elbow, 1),     Snk(0) },
            // Y=0 (bottom) — 장식용 파이프들 (경로엔 영향 없음)
            { D(PipeShape.Tee, 1), D(PipeShape.Straight, 0), D(PipeShape.Cross, 0),  D(PipeShape.Straight, 0), D(PipeShape.Tee, 3) },
        };

        // ----- Menu -----

        [MenuItem("Tools/PipePuz/Build Pipe MiniGame")]
        public static void Build()
        {
            var go = GameObject.Find("PipeMiniGame");
            if (go == null)
            {
                EditorUtility.DisplayDialog("PipeMiniGame",
                    "씬에서 'PipeMiniGame' 오브젝트를 찾을 수 없습니다.\n" +
                    "Pipe Scene 에 빈 GameObject 'PipeMiniGame' 을 만들어 벽면 위치에 둔 다음 다시 시도하세요.",
                    "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Pipe MiniGame");

            // 자식 정리
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(go.transform.GetChild(i).gameObject);
            }
            var oldBoard = go.GetComponent<PipeMiniGameBoard>();
            if (oldBoard != null) Undo.DestroyObjectImmediate(oldBoard);

            // 머티리얼
            var panelMat = MakeUrpMaterial("PMG_PanelMat", new Color(0.32f, 0.62f, 0.78f), false);
            var disconnectedMat = MakeUrpMaterial("PMG_Disconnected", new Color(1f, 0.85f, 0.2f), false); // 노랑
            var connectedMat = MakeUrpMaterial("PMG_Connected", new Color(0.9f, 0.18f, 0.18f), false);    // 빨강
            var sourceMat = MakeUrpMaterial("PMG_Source", new Color(0.2f, 0.85f, 0.4f), false);           // Source 마커 — 초록
            var sinkMat = MakeUrpMaterial("PMG_Sink", new Color(1f, 0.6f, 0.2f), false);                  // Sink 마커 — 주황
            var fixedFrameMat = MakeUrpMaterial("PMG_FixedFrame", new Color(0.85f, 0.85f, 0.85f), false); // 고정 셀 테두리

            // 보드 컴포넌트
            var board = go.AddComponent<PipeMiniGameBoard>();
            int H = DefaultLayout.GetLength(0);
            int W = DefaultLayout.GetLength(1);
            board.Width = W;
            board.Height = H;
            board.DisconnectedMaterial = disconnectedMat;
            board.ConnectedMaterial = connectedMat;
            board.SourceMaterial = sourceMat;
            board.SinkMaterial = sinkMat;
            board.Cells = new PipeMiniGameCell[W * H];

            // 배경 패널
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(go.transform, false);
            float panelW = W * CellSize + 2f * Margin;
            float panelH = H * CellSize + 2f * Margin;
            panel.transform.localPosition = new Vector3(0f, 0f, PanelZ);
            panel.transform.localScale = new Vector3(panelW, panelH, PanelThickness);
            AssignMat(panel, panelMat);

            // 격자 원점 — 가운데 정렬
            float originX = -((W - 1) * CellSize) * 0.5f;
            float originY = -((H - 1) * CellSize) * 0.5f;

            for (int gy = 0; gy < H; gy++)
            {
                for (int gx = 0; gx < W; gx++)
                {
                    // 레이아웃 행은 위에서 아래(=Y 내림차순) 이므로 변환.
                    var def = DefaultLayout[H - 1 - gy, gx];
                    if (def.Shape == PipeShape.None) continue;

                    var cellLocal = new Vector3(originX + gx * CellSize, originY + gy * CellSize, 0f);
                    var cell = CreateCell(go.transform, gx, gy, cellLocal, def, board, disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
                    board.Cells[gx + gy * W] = cell;
                }
            }

            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[PipeMiniGame] 빌드 완료. PipeMiniGame 안에 Panel 과 Cell_x_y_<Shape> 들이 생성되었습니다.");
        }

        // ----- Cell builder -----

        static PipeMiniGameCell CreateCell(Transform parent, int x, int y, Vector3 localPos, CellDef def,
            PipeMiniGameBoard board, Material pipeMat, Material sourceMat, Material sinkMat, Material fixedFrameMat)
        {
            var cellGo = new GameObject($"Cell_{x}_{y}_{def.Shape}");
            cellGo.transform.SetParent(parent, false);
            cellGo.transform.localPosition = localPos;
            cellGo.transform.localRotation = Quaternion.identity;

            // 클릭 콜라이더 — 셀 영역 전체 (조금 작게).
            var col = cellGo.AddComponent<BoxCollider>();
            col.size = new Vector3(CellSize * 0.95f, CellSize * 0.95f, ArmThickness * 2.5f);

            // 회전 루트 — Rotation 만큼 Z 회전을 받는다.
            var pipeRoot = new GameObject("PipeRoot");
            pipeRoot.transform.SetParent(cellGo.transform, false);
            pipeRoot.transform.localPosition = Vector3.zero;

            var pipeRenderers = new List<Renderer>();

            // 허브 — 항상 중앙에 작은 큐브.
            var hub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hub.name = "Hub";
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.transform.SetParent(pipeRoot.transform, false);
            hub.transform.localPosition = Vector3.zero;
            hub.transform.localScale = new Vector3(HubSize, HubSize, ArmThickness);
            AssignMat(hub, pipeMat);
            pipeRenderers.Add(hub.GetComponent<Renderer>());

            // 베이스 마스크에 따라 arm 4 방향 중 켜진 것만 생성.
            var baseMask = PipeShapeDef.BaseMask(def.Shape);
            foreach (var dir in new[] { Direction.N, Direction.E, Direction.S, Direction.W })
            {
                if ((baseMask & dir) == 0) continue;
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = $"Arm_{dir}";
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                arm.transform.SetParent(pipeRoot.transform, false);
                var (offset, scale) = ArmTransform(dir);
                arm.transform.localPosition = offset;
                arm.transform.localScale = scale;
                AssignMat(arm, pipeMat);
                pipeRenderers.Add(arm.GetComponent<Renderer>());
            }

            // Source / Sink 마커 — 흐름 색에 영향 받지 않는 별도 sphere.
            if (def.Shape == PipeShape.Source || def.Shape == PipeShape.Sink)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Marker";
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.transform.SetParent(pipeRoot.transform, false);
                marker.transform.localPosition = new Vector3(0f, 0f, ArmThickness * 0.6f);
                marker.transform.localScale = Vector3.one * MarkerSize;
                AssignMat(marker, def.Shape == PipeShape.Source ? sourceMat : sinkMat);
            }

            // 고정 셀이면 셀 뒤에 살짝 큰 회색 테두리 큐브를 둬서 시각적으로 구분.
            if (def.IsFixed)
            {
                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frame.name = "FixedFrame";
                Object.DestroyImmediate(frame.GetComponent<Collider>());
                frame.transform.SetParent(cellGo.transform, false);
                frame.transform.localPosition = new Vector3(0f, 0f, -ArmThickness * 0.6f);
                frame.transform.localScale = new Vector3(CellSize * 0.92f, CellSize * 0.92f, ArmThickness * 0.4f);
                AssignMat(frame, fixedFrameMat);
            }

            // 초기 시각 회전 적용.
            pipeRoot.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * def.Rotation);

            // 셀 컴포넌트 — 비고정 셀에만 XRSimpleInteractable 부착.
            var cell = cellGo.AddComponent<PipeMiniGameCell>();
            cell.X = x;
            cell.Y = y;
            cell.Shape = def.Shape;
            cell.Rotation = def.Rotation;
            cell.IsFixed = def.IsFixed;
            cell.PipeRoot = pipeRoot.transform;
            cell.PipeRenderers = pipeRenderers.ToArray();
            cell.Board = board;

            if (!def.IsFixed)
            {
                cellGo.AddComponent<XRSimpleInteractable>();
            }

            return cell;
        }

        static (Vector3 offset, Vector3 scale) ArmTransform(Direction dir)
        {
            float halfArm = CellSize * 0.25f;       // arm 중심까지 거리
            float armLen = CellSize * 0.5f + 0.005f; // arm 길이 — 셀 가장자리 살짝 넘김(이웃과 시각적 연결)
            switch (dir)
            {
                case Direction.N: return (new Vector3(0f, +halfArm, 0f), new Vector3(ArmThickness, armLen, ArmThickness));
                case Direction.S: return (new Vector3(0f, -halfArm, 0f), new Vector3(ArmThickness, armLen, ArmThickness));
                case Direction.E: return (new Vector3(+halfArm, 0f, 0f), new Vector3(armLen, ArmThickness, ArmThickness));
                case Direction.W: return (new Vector3(-halfArm, 0f, 0f), new Vector3(armLen, ArmThickness, ArmThickness));
                default:          return (Vector3.zero, Vector3.one * 0.001f);
            }
        }

        // ----- Material / utility -----

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
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return m;
        }

        static void AssignMat(GameObject go, Material m)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = m;
        }
    }
}
