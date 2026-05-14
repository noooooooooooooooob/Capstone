using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PipePuz.MiniGame;

namespace PipePuz.MiniGame2.EditorTools
{
    /// <summary>
    /// 메뉴 Tools/PipePuz/Build Pipe MiniGame 2.
    ///
    /// PipeMiniGame2 GameObject 안에 다음을 자동 생성한다:
    ///   - 5×3 wall panel (배경)
    ///   - 슬롯 15 개 (Source / Sink 둘은 미리 설치)
    ///   - 바닥에 누워있는 7 개의 파이프 (Straight ×3 + Elbow ×4) — 사용자가 잡아서 슬롯에 배치
    ///
    /// 사용 흐름:
    ///   1. 바닥에서 파이프 잡기 (XR Grab)
    ///   2. 잡은 채로 트리거 누름 → 90° 회전
    ///   3. 빈 슬롯 근처에서 놓기 → 자동 snap
    ///   4. Source(좌) → Sink(우) 흐름이 연결되면 OnSolved
    /// </summary>
    public static class PipeMiniGame2Setup
    {
        const float CellSize = 0.18f;
        const float ArmThickness = 0.025f;
        const float HubSize = 0.05f;
        const float Margin = 0.04f;
        const float PanelThickness = 0.02f;
        const float MarkerSize = 0.07f;

        const float WallY = 1.40f;     // wall panel 중심 Y
        const float PanelZ = -0.025f;  // wall panel center Z (slot 보다 뒤쪽)
        const float FloorY = 0.06f;    // 바닥에 놓인 파이프 Y

        struct CellDef
        {
            public PipeShape Shape;
            public int Rotation;
            public bool IsFixed;
        }

        [MenuItem("Tools/PipePuz/Build Pipe MiniGame 2")]
        public static void Build()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Pipe MiniGame 2");

            // 1) PipeMiniGame2 root GO 확보 — 없으면 자동 생성.
            var go = GameObject.Find("PipeMiniGame2");
            if (go == null)
            {
                go = new GameObject("PipeMiniGame2");
                Undo.RegisterCreatedObjectUndo(go, "Create PipeMiniGame2");
                Debug.LogWarning("[PipeMiniGame2] 'PipeMiniGame2' GameObject 가 씬에 없어서 world (0,0,0) 에 새로 만들었습니다. " +
                                 "원하는 위치로 옮긴 후 Build 메뉴를 다시 누르세요.");
            }

            // 2) 이전 빌드의 orphan root GO 정리 (PipeMiniGame2 가 한 번 삭제됐다 다시 만들어진 케이스 등).
            CleanupOrphans(go);

            // 3) 기존 자식 정리.
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(go.transform.GetChild(i).gameObject);
            }
            var oldBoard = go.GetComponent<PipeMiniGame2Board>();
            if (oldBoard != null) Undo.DestroyObjectImmediate(oldBoard);

            // 머티리얼.
            var panelMat = MakeUrpMaterial("PMG2_Panel", new Color(0.32f, 0.62f, 0.78f), false);
            var cellBgMat = MakeUrpMaterial("PMG2_CellBg", new Color(0.85f, 0.95f, 1f, 0.3f), true);
            var disconnectedMat = MakeUrpMaterial("PMG2_Disconnected", new Color(1f, 0.85f, 0.2f), false);
            var connectedMat = MakeUrpMaterial("PMG2_Connected", new Color(0.9f, 0.18f, 0.18f), false);
            var sourceMat = MakeUrpMaterial("PMG2_Source", new Color(0.2f, 0.9f, 0.4f), false);
            var sinkMat = MakeUrpMaterial("PMG2_Sink", new Color(0.9f, 0.6f, 0.2f), false);
            var fixedFrameMat = MakeUrpMaterial("PMG2_FixedFrame", new Color(0.85f, 0.85f, 0.85f), false);
            var slotOutlineMat = MakeUrpMaterial("PMG2_SlotOutline", new Color(0.8f, 0.95f, 1f, 0.30f), true);

            // 보드 컴포넌트.
            var board = go.AddComponent<PipeMiniGame2Board>();
            int W = 5, H = 3;
            board.Width = W;
            board.Height = H;
            board.DisconnectedMaterial = disconnectedMat;
            board.ConnectedMaterial = connectedMat;
            board.SnapDistance = 0.20f;
            board.Slots = new PipeMiniGame2Slot[W * H];
            board.AllPipes = new List<PipeMiniGame2Pipe>();

            // 배경 패널.
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(go.transform, false);
            float panelW = W * CellSize + 2f * Margin;
            float panelH = H * CellSize + 2f * Margin;
            panel.transform.localPosition = new Vector3(0f, WallY, PanelZ);
            panel.transform.localScale = new Vector3(panelW, panelH, PanelThickness);
            AssignMat(panel, panelMat);

            // 슬롯 생성 — 그리드 정렬.
            float originX = -((W - 1) * CellSize) * 0.5f;
            float originY = WallY - ((H - 1) * CellSize) * 0.5f;

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    var slotGo = new GameObject($"Slot_{x}_{y}");
                    slotGo.transform.SetParent(go.transform, false);
                    slotGo.transform.localPosition = new Vector3(originX + x * CellSize, originY + y * CellSize, 0f);
                    slotGo.transform.localRotation = Quaternion.identity;

                    // Empty outline — 빈 슬롯 시각 가이드.
                    var outline = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    outline.name = "EmptyOutline";
                    Object.DestroyImmediate(outline.GetComponent<Collider>());
                    outline.transform.SetParent(slotGo.transform, false);
                    outline.transform.localPosition = Vector3.zero;
                    outline.transform.localScale = new Vector3(CellSize * 0.92f, CellSize * 0.92f, 0.004f);
                    AssignMat(outline, slotOutlineMat);

                    var slot = slotGo.AddComponent<PipeMiniGame2Slot>();
                    slot.X = x;
                    slot.Y = y;
                    slot.Board = board;
                    slot.EmptyOutline = outline;

                    board.Slots[x + y * W] = slot;
                }
            }

            // Source pipe (고정, slot (0, 1)).
            var sourceSlot = board.Slots[0 + 1 * W];
            var sourcePipe = CreatePipe("Source", PipeShape.Source, 0, true,
                disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
            sourcePipe.transform.SetParent(go.transform, false);
            sourceSlot.AcceptPipe(sourcePipe);
            sourcePipe.Board = board;
            board.AllPipes.Add(sourcePipe);

            // Sink pipe (고정, slot (4, 1)).
            var sinkSlot = board.Slots[4 + 1 * W];
            var sinkPipe = CreatePipe("Sink", PipeShape.Sink, 0, true,
                disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
            sinkPipe.transform.SetParent(go.transform, false);
            sinkSlot.AcceptPipe(sinkPipe);
            sinkPipe.Board = board;
            board.AllPipes.Add(sinkPipe);

            // 바닥에 놓일 파이프 7 개 — 3 Straight + 4 Elbow.
            var pipeShapes = new[]
            {
                PipeShape.Straight, PipeShape.Straight, PipeShape.Straight,
                PipeShape.Elbow,    PipeShape.Elbow,    PipeShape.Elbow,    PipeShape.Elbow,
            };
            var floorPositions = new[]
            {
                new Vector3(-0.45f, FloorY, 0.40f),
                new Vector3(-0.15f, FloorY, 0.40f),
                new Vector3(+0.15f, FloorY, 0.40f),
                new Vector3(+0.45f, FloorY, 0.40f),
                new Vector3(-0.30f, FloorY, 0.60f),
                new Vector3( 0.00f, FloorY, 0.60f),
                new Vector3(+0.30f, FloorY, 0.60f),
            };

            for (int i = 0; i < pipeShapes.Length; i++)
            {
                var pipe = CreatePipe($"Pipe_{pipeShapes[i]}_{i}", pipeShapes[i], 0, false,
                    disconnectedMat, sourceMat, sinkMat, fixedFrameMat);
                pipe.transform.SetParent(go.transform, false);
                pipe.transform.localPosition = floorPositions[i];
                // 바닥에 눕히기: 파이프의 평면(XY)이 수평이 되도록 X 축 -90° 회전.
                pipe.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                pipe.Board = board;
                board.AllPipes.Add(pipe);
            }

            EditorUtility.SetDirty(go);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            var p = go.transform.position;
            Debug.Log($"[PipeMiniGame2] 빌드 완료. World 위치: ({p.x:F2}, {p.y:F2}, {p.z:F2}). " +
                      $"Wall panel 중심: ({p.x:F2}, {p.y + WallY:F2}, {p.z + PanelZ:F2}). " +
                      $"Hierarchy 에서 PipeMiniGame2 선택 후 Scene View 에서 F 키로 포커스하세요.");
        }

        /// <summary>
        /// 씬 root 에 흩어진 PipeMiniGame2 컴포넌트 (이전 빌드의 orphan 들) 을 찾아 정리.
        /// PipeMiniGame2 GO 가 한 번 삭제되었다가 다시 생기는 경우 children 이 root-level 로 풀려있는데,
        /// 그것들을 다 제거해서 깨끗한 상태에서 새로 빌드한다.
        /// </summary>
        static void CleanupOrphans(GameObject expectedParent)
        {
            var toDestroy = new HashSet<GameObject>();

            var pipes = Object.FindObjectsByType<PipeMiniGame2Pipe>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var p in pipes)
            {
                if (p == null) continue;
                if (p.transform.IsChildOf(expectedParent.transform)) continue;
                var root = p.transform.root.gameObject;
                if (root != expectedParent) toDestroy.Add(root);
            }

            var slots = Object.FindObjectsByType<PipeMiniGame2Slot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var s in slots)
            {
                if (s == null) continue;
                if (s.transform.IsChildOf(expectedParent.transform)) continue;
                var root = s.transform.root.gameObject;
                if (root != expectedParent) toDestroy.Add(root);
            }

            int n = toDestroy.Count;
            foreach (var rgo in toDestroy)
            {
                if (rgo != null) Undo.DestroyObjectImmediate(rgo);
            }
            if (n > 0)
            {
                Debug.LogWarning($"[PipeMiniGame2] 씬 root 의 orphan {n} 개 정리됨 " +
                                  "(이전 빌드에서 부모 GO 가 삭제돼 흩어진 Slot/Pipe 들).");
            }
        }

        // ----- Pipe 생성 helper -----

        static PipeMiniGame2Pipe CreatePipe(string name, PipeShape shape, int rotation, bool isFixed,
            Material pipeMat, Material sourceMat, Material sinkMat, Material fixedFrameMat)
        {
            var pipeGo = new GameObject(name);

            // PipeRoot — 회전 적용 대상.
            var pipeRoot = new GameObject("PipeRoot");
            pipeRoot.transform.SetParent(pipeGo.transform, false);
            pipeRoot.transform.localPosition = Vector3.zero;
            pipeRoot.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * rotation);

            var pipeRenderers = new List<Renderer>();
            var baseMask = PipeShapeDef.BaseMask(shape);

            // 허브 (중앙 작은 큐브).
            var hub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hub.name = "Hub";
            Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.transform.SetParent(pipeRoot.transform, false);
            hub.transform.localPosition = Vector3.zero;
            hub.transform.localScale = new Vector3(HubSize, HubSize, ArmThickness);
            AssignMat(hub, pipeMat);
            pipeRenderers.Add(hub.GetComponent<Renderer>());

            // 각 방향 arm.
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

            // Source / Sink 마커.
            if (shape == PipeShape.Source || shape == PipeShape.Sink)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Marker";
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.transform.SetParent(pipeRoot.transform, false);
                marker.transform.localPosition = new Vector3(0f, 0f, ArmThickness * 0.6f);
                marker.transform.localScale = Vector3.one * MarkerSize;
                AssignMat(marker, shape == PipeShape.Source ? sourceMat : sinkMat);
            }

            // 고정 파이프엔 회색 frame 시각.
            if (isFixed)
            {
                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frame.name = "FixedFrame";
                Object.DestroyImmediate(frame.GetComponent<Collider>());
                frame.transform.SetParent(pipeGo.transform, false);
                frame.transform.localPosition = new Vector3(0f, 0f, -ArmThickness * 0.6f);
                frame.transform.localScale = new Vector3(CellSize * 0.92f, CellSize * 0.92f, ArmThickness * 0.4f);
                AssignMat(frame, fixedFrameMat);
            }

            // 컴포넌트.
            var pipe = pipeGo.AddComponent<PipeMiniGame2Pipe>();
            pipe.Shape = shape;
            pipe.Rotation = rotation;
            pipe.IsFixed = isFixed;
            pipe.PipeRoot = pipeRoot.transform;
            pipe.PipeRenderers = pipeRenderers.ToArray();

            // 비고정 파이프만 잡기 가능.
            if (!isFixed)
            {
                var col = pipeGo.AddComponent<BoxCollider>();
                col.size = new Vector3(CellSize * 0.9f, CellSize * 0.9f, ArmThickness * 3f);

                var rb = pipeGo.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                var grab = pipeGo.AddComponent<XRGrabInteractable>();
                grab.throwOnDetach = false;
                grab.smoothPosition = false;
            }

            return pipe;
        }

        static (Vector3 offset, Vector3 scale) ArmTransform(Direction dir)
        {
            float halfArm = CellSize * 0.25f;
            float armLen = CellSize * 0.5f + 0.005f;
            switch (dir)
            {
                case Direction.N: return (new Vector3(0f, +halfArm, 0f), new Vector3(ArmThickness, armLen, ArmThickness));
                case Direction.S: return (new Vector3(0f, -halfArm, 0f), new Vector3(ArmThickness, armLen, ArmThickness));
                case Direction.E: return (new Vector3(+halfArm, 0f, 0f), new Vector3(armLen, ArmThickness, ArmThickness));
                case Direction.W: return (new Vector3(-halfArm, 0f, 0f), new Vector3(armLen, ArmThickness, ArmThickness));
                default: return (Vector3.zero, Vector3.one * 0.001f);
            }
        }

        // ----- Material helpers -----

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
