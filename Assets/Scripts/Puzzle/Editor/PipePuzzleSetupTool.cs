#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Capstone.Puzzle;
using Fusion;

namespace Capstone.Puzzle.EditorTools
{
    /// <summary>
    /// "Tools / Capstone / Setup Pipe Puzzle (RadiatorA / B)" 메뉴를 누르면
    /// 현재 열려 있는 씬의 RadiatorA, RadiatorB 를 찾아 다음을 자동으로 셋업한다:
    ///   1) VirtualWall(빈 GO) + MirrorController(RadiatorMirror)
    ///   2) RadiatorA.Valve.RadiatorValve, RadiatorB.Valve.RadiatorValveLink
    ///   3) 양쪽 ValveHandle 에 ValveRotationGrab + XRControllerValveGrabber + SphereCollider
    ///   4) RadiatorB 옆 PipeSocket(XRSocketInteractor + RadiatorPipeSocket)
    ///      + Pipe_Broke / Pipe_New (XRGrabInteractable + Pipe + Rigidbody + Collider)
    ///      + RadiatorA 측 Pipe_Extra_A (시각용)
    ///   5) RadiatorA / RadiatorB 에 PipeLeakFog (Translucent / Opaque)
    ///   6) NetworkObject 보장 (Fusion 동기화용)
    ///   7) 기존 RadiatorFogVisual 비활성화 (이번 퍼즐 규칙과 반대 동작이므로)
    ///
    /// 멱등하게 동작 (반복 실행해도 이중 부착 방지)하도록 항상 자식 이름 / 컴포넌트 존재 여부를 먼저 확인한다.
    /// 적용 후 Undo (Ctrl+Z) 가능.
    /// </summary>
    public static class PipePuzzleSetupTool
    {
        const string MENU_PATH = "Tools/Capstone/Setup Pipe Puzzle (RadiatorA & B)";

        // 셋업 대상 이름 (씬에서 정확히 이 이름이어야 함)
        const string RADIATOR_A_NAME = "RadiatorA";
        const string RADIATOR_B_NAME = "RadiatorB";

        // 자식 이름들
        const string VALVE_CHILD = "Valve";
        const string VALVE_HUB_CHILD = "ValveHub";
        const string VALVE_HANDLE_CHILD = "ValveHandle";

        // 셋업으로 만들 이름들
        const string VIRTUAL_WALL_NAME = "VirtualWall";
        const string MIRROR_CONTROLLER_NAME = "MirrorController";
        const string PIPE_SOCKET_B_NAME = "PipeSocket_B";
        const string PIPE_BROKE_NAME = "Pipe_Broke";
        const string PIPE_NEW_NAME = "Pipe_New";
        const string PIPE_EXTRA_A_NAME = "Pipe_Extra_A";
        const string LEAK_FOG_A_NAME = "LeakFog_A";
        const string LEAK_FOG_B_NAME = "LeakFog_B";

        [MenuItem(MENU_PATH)]
        public static void RunSetup()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorUtility.DisplayDialog("Pipe Puzzle Setup", "활성 씬이 로드되어 있지 않습니다.", "OK");
                return;
            }

            var radA = FindRootObjectByName(scene, RADIATOR_A_NAME);
            var radB = FindRootObjectByName(scene, RADIATOR_B_NAME);
            if (radA == null || radB == null)
            {
                EditorUtility.DisplayDialog("Pipe Puzzle Setup",
                    $"씬에서 '{RADIATOR_A_NAME}' 또는 '{RADIATOR_B_NAME}' 루트 GameObject 를 찾지 못했습니다.\n" +
                    "이름을 정확히 맞춘 다음 다시 실행하세요.", "OK");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Setup Pipe Puzzle");

            try
            {
                // 1. 가상벽 + 미러 컨트롤러
                var (wall, mirror) = EnsureVirtualWallAndMirror(radA, radB);

                // 2. 밸브 — 마스터 / 링크
                var valveA = EnsureRadiatorValveOnA(radA);
                EnsureRadiatorValveLinkOnB(radB, valveA);

                // 3. 양쪽 ValveHandle 그랩 셋업
                EnsureValveHandleGrab(radA, valveA);
                EnsureValveHandleGrab(radB, valveA);

                // 4. 라디에이터에 NetworkObject 보장
                EnsureNetworkObject(radA);
                EnsureNetworkObject(radB);

                // 5. 추가 파이프 슬롯 시각용 (RadiatorA)
                EnsurePipeExtraA(radA);

                // 6. RadiatorB 의 파이프 소켓 + 파이프
                var socket = EnsurePipeSocketB(radB);
                var brokePipe = EnsurePipe(radB, PIPE_BROKE_NAME, PipeKind.Broke,
                                           radB.transform.position + radB.transform.right * 0.4f + Vector3.up * 0.1f);
                var newPipe = EnsurePipe(radB, PIPE_NEW_NAME, PipeKind.New,
                                         radB.transform.position + radB.transform.right * 1.0f + Vector3.up * 0.05f);
                // 시작 시 broke 가 끼워져 있도록
                AssignStartingSelected(socket, brokePipe);

                // 7. 연기 효과 (양쪽)
                EnsureLeakFog(radA, socket, valveA, PipeLeakFog.FogStyle.Translucent, LEAK_FOG_A_NAME);
                EnsureLeakFog(radB, socket, valveA, PipeLeakFog.FogStyle.Opaque, LEAK_FOG_B_NAME);

                // 8. 기존 RadiatorFogVisual 비활성화 (반대 동작이므로)
                DisableExistingRadiatorFogVisual(radA);
                DisableExistingRadiatorFogVisual(radB);

                EditorSceneManagerSetSceneDirty(scene);
                Undo.CollapseUndoOperations(undoGroup);

                Debug.Log("[PipePuzzleSetup] 셋업 완료. Hierarchy 를 확인하세요.\n" +
                          $" - VirtualWall: {wall.name}\n - MirrorController: {mirror.gameObject.name}\n" +
                          $" - Master Valve: {valveA.gameObject.name}\n - Pipe Socket: {socket.gameObject.name}");

                EditorGUIUtility.PingObject(mirror);
                Selection.activeGameObject = mirror.gameObject;
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Pipe Puzzle Setup",
                    "셋업 도중 예외가 발생했습니다. Console 로그를 확인하세요.\n\n" + ex.Message, "OK");
            }
        }

        // =====================================================================
        // 1. VirtualWall + MirrorController
        // =====================================================================
        static (GameObject wall, RadiatorMirror mirror) EnsureVirtualWallAndMirror(GameObject radA, GameObject radB)
        {
            var scene = radA.scene;
            var wall = FindRootObjectByName(scene, VIRTUAL_WALL_NAME);
            if (wall == null)
            {
                wall = new GameObject(VIRTUAL_WALL_NAME);
                Undo.RegisterCreatedObjectUndo(wall, "Create VirtualWall");
            }

            // A 와 B 의 중점에 위치, A→B 방향이 forward 가 되도록 회전
            Vector3 mid = (radA.transform.position + radB.transform.position) * 0.5f;
            Vector3 dirAtoB = radB.transform.position - radA.transform.position;
            if (dirAtoB.sqrMagnitude < 1e-6f) dirAtoB = Vector3.right;
            Undo.RecordObject(wall.transform, "Place VirtualWall");
            wall.transform.position = mid;
            wall.transform.rotation = Quaternion.LookRotation(dirAtoB.normalized, Vector3.up);

            var ctlGo = FindRootObjectByName(scene, MIRROR_CONTROLLER_NAME);
            if (ctlGo == null)
            {
                ctlGo = new GameObject(MIRROR_CONTROLLER_NAME);
                Undo.RegisterCreatedObjectUndo(ctlGo, "Create MirrorController");
            }
            var mirror = ctlGo.GetComponent<RadiatorMirror>();
            if (mirror == null)
            {
                mirror = Undo.AddComponent<RadiatorMirror>(ctlGo);
            }

            // private SerializedField 들이라 SerializedObject 로 채운다
            var so = new SerializedObject(mirror);
            so.FindProperty("sourceRoot").objectReferenceValue = radA.transform;
            so.FindProperty("mirrorRoot").objectReferenceValue = radB.transform;
            so.FindProperty("virtualWall").objectReferenceValue = wall.transform;
            // 첫 셋업 시엔 liveUpdate 끄고, 사용자가 직접 Apply Mirror Now 누르도록.
            so.ApplyModifiedPropertiesWithoutUndo();

            return (wall, mirror);
        }

        // =====================================================================
        // 2. 마스터 RadiatorValve 보장 (RadiatorA)
        // =====================================================================
        static RadiatorValve EnsureRadiatorValveOnA(GameObject radA)
        {
            // RadiatorA/Valve 자식 또는 RadiatorA 자체에 부착
            Transform valveTr = FindChildByName(radA.transform, VALVE_CHILD) ?? radA.transform;

            var valve = valveTr.GetComponent<RadiatorValve>();
            if (valve == null)
                valve = Undo.AddComponent<RadiatorValve>(valveTr.gameObject);

            // ValveHandle 자동 연결
            Transform handle = FindDescendantByName(valveTr, VALVE_HANDLE_CHILD);
            if (handle != null)
            {
                var so = new SerializedObject(valve);
                so.FindProperty("handleTransform").objectReferenceValue = handle;
                // Z 축 기본값 유지
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return valve;
        }

        // =====================================================================
        // 3. RadiatorValveLink 보장 (RadiatorB)
        // =====================================================================
        static void EnsureRadiatorValveLinkOnB(GameObject radB, RadiatorValve master)
        {
            Transform valveTr = FindChildByName(radB.transform, VALVE_CHILD) ?? radB.transform;
            var link = valveTr.GetComponent<RadiatorValveLink>();
            if (link == null)
                link = Undo.AddComponent<RadiatorValveLink>(valveTr.gameObject);

            Transform handle = FindDescendantByName(valveTr, VALVE_HANDLE_CHILD);
            var so = new SerializedObject(link);
            so.FindProperty("master").objectReferenceValue = master;
            if (handle != null)
                so.FindProperty("followerHandle").objectReferenceValue = handle;
            // 거울 대칭이라 회전 방향이 보통 반대로 보임
            so.FindProperty("invertAxis").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // =====================================================================
        // 4. ValveHandle 그랩 (양쪽)
        // =====================================================================
        static void EnsureValveHandleGrab(GameObject radiatorRoot, RadiatorValve valve)
        {
            Transform valveTr = FindChildByName(radiatorRoot.transform, VALVE_CHILD);
            if (valveTr == null) return;
            Transform handle = FindDescendantByName(valveTr, VALVE_HANDLE_CHILD);
            if (handle == null) return;

            // SphereCollider (그랩 감지용)
            if (handle.GetComponent<Collider>() == null)
            {
                var col = Undo.AddComponent<SphereCollider>(handle.gameObject);
                col.radius = 0.12f;
                col.isTrigger = false;
            }

            // ValveRotationGrab
            var grab = handle.GetComponent<ValveRotationGrab>();
            if (grab == null) grab = Undo.AddComponent<ValveRotationGrab>(handle.gameObject);
            var grabSo = new SerializedObject(grab);
            grabSo.FindProperty("valve").objectReferenceValue = valve;     // 양쪽 모두 마스터를 가리킴
            grabSo.FindProperty("pivot").objectReferenceValue = handle;
            grabSo.ApplyModifiedPropertiesWithoutUndo();

            // XRControllerValveGrabber (XRSimpleInteractable 상속)
            var grabber = handle.GetComponent<XRControllerValveGrabber>();
            if (grabber == null) grabber = Undo.AddComponent<XRControllerValveGrabber>(handle.gameObject);

            var grabberSo = new SerializedObject(grabber);
            var valveGrabProp = grabberSo.FindProperty("valveGrab");
            if (valveGrabProp != null) valveGrabProp.objectReferenceValue = grab;
            grabberSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // =====================================================================
        // 5. RadiatorA 측 추가 파이프 (시각용)
        // =====================================================================
        static void EnsurePipeExtraA(GameObject radA)
        {
            if (FindChildByName(radA.transform, PIPE_EXTRA_A_NAME) != null) return;
            var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pipe.name = PIPE_EXTRA_A_NAME;
            Undo.RegisterCreatedObjectUndo(pipe, "Create Pipe_Extra_A");
            // 내장 콜라이더 제거 (시각용)
            var col = pipe.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            pipe.transform.SetParent(radA.transform, false);
            pipe.transform.localPosition = new Vector3(-0.55f, 0.05f, 0f);
            pipe.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            pipe.transform.localScale = new Vector3(0.06f, 0.25f, 0.06f);
        }

        // =====================================================================
        // 6. RadiatorB PipeSocket + Pipe 두 개
        // =====================================================================
        static RadiatorPipeSocket EnsurePipeSocketB(GameObject radB)
        {
            Transform existing = FindChildByName(radB.transform, PIPE_SOCKET_B_NAME);
            GameObject socketGo = existing != null ? existing.gameObject : new GameObject(PIPE_SOCKET_B_NAME);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(socketGo, "Create PipeSocket_B");
                socketGo.transform.SetParent(radB.transform, false);
                socketGo.transform.localPosition = new Vector3(0.55f, 0.05f, 0f);
                socketGo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            // 트리거 콜라이더 — 소켓 영역
            var trigger = socketGo.GetComponent<SphereCollider>();
            if (trigger == null) trigger = Undo.AddComponent<SphereCollider>(socketGo);
            trigger.isTrigger = true;
            trigger.radius = 0.18f;

            var socket = socketGo.GetComponent<XRSocketInteractor>();
            if (socket == null) socket = Undo.AddComponent<XRSocketInteractor>(socketGo);
            socket.socketActive = true;

            var pipeSocket = socketGo.GetComponent<RadiatorPipeSocket>();
            if (pipeSocket == null) pipeSocket = Undo.AddComponent<RadiatorPipeSocket>(socketGo);
            var so = new SerializedObject(pipeSocket);
            so.FindProperty("socket").objectReferenceValue = socket;
            so.ApplyModifiedPropertiesWithoutUndo();

            return pipeSocket;
        }

        static GameObject EnsurePipe(GameObject radB, string name, PipeKind kind, Vector3 worldPos)
        {
            Transform existing = FindChildByName(radB.transform, name);
            GameObject pipeGo;
            if (existing != null)
            {
                pipeGo = existing.gameObject;
            }
            else
            {
                pipeGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pipeGo.name = name;
                Undo.RegisterCreatedObjectUndo(pipeGo, "Create " + name);
                pipeGo.transform.SetParent(radB.transform, true);
                pipeGo.transform.position = worldPos;
                pipeGo.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                pipeGo.transform.localScale = new Vector3(0.06f, 0.25f, 0.06f);
            }

            // 기본 캡슐/실린더 콜라이더 → 트리거 아님, XRGrabInteractable 가 잡을 수 있도록
            var col = pipeGo.GetComponent<Collider>();
            if (col == null) col = Undo.AddComponent<CapsuleCollider>(pipeGo);

            // Rigidbody (Use Gravity 끄고, XRGrab 가 Kinematic 토글)
            var rb = pipeGo.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(pipeGo);
            rb.useGravity = false;
            rb.linearDamping = 1f;
            rb.angularDamping = 1f;

            // XRGrabInteractable
            var grab = pipeGo.GetComponent<XRGrabInteractable>();
            if (grab == null) grab = Undo.AddComponent<XRGrabInteractable>(pipeGo);
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;

            // Pipe 마커
            var pipe = pipeGo.GetComponent<Pipe>();
            if (pipe == null) pipe = Undo.AddComponent<Pipe>(pipeGo);
            var so = new SerializedObject(pipe);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            // coloredRenderers 자동 채우기
            var rendArrayProp = so.FindProperty("coloredRenderers");
            var rends = pipeGo.GetComponentsInChildren<Renderer>(true);
            rendArrayProp.arraySize = rends.Length;
            for (int i = 0; i < rends.Length; i++)
                rendArrayProp.GetArrayElementAtIndex(i).objectReferenceValue = rends[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            return pipeGo;
        }

        static void AssignStartingSelected(RadiatorPipeSocket pipeSocket, GameObject pipeGo)
        {
            if (pipeSocket == null || pipeGo == null) return;
            var socket = pipeSocket.GetComponent<XRSocketInteractor>();
            if (socket == null) return;
            var grab = pipeGo.GetComponent<XRGrabInteractable>();
            if (grab == null) return;

            // SerializedProperty 로 startingSelectedInteractable 설정
            var so = new SerializedObject(socket);
            var prop = so.FindProperty("m_StartingSelectedInteractable");
            if (prop != null)
            {
                prop.objectReferenceValue = grab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // 시작 위치를 소켓 위치로 맞춤
            pipeGo.transform.position = socket.transform.position;
            pipeGo.transform.rotation = socket.transform.rotation;
        }

        // =====================================================================
        // 7. PipeLeakFog (양쪽)
        // =====================================================================
        static void EnsureLeakFog(GameObject radiatorRoot, RadiatorPipeSocket socket, RadiatorValve valve,
                                  PipeLeakFog.FogStyle style, string goName)
        {
            Transform existing = FindChildByName(radiatorRoot.transform, goName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(goName);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Create " + goName);
                go.transform.SetParent(radiatorRoot.transform, false);
                go.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            }
            var fog = go.GetComponent<PipeLeakFog>();
            if (fog == null) fog = Undo.AddComponent<PipeLeakFog>(go);

            var so = new SerializedObject(fog);
            so.FindProperty("socket").objectReferenceValue = socket;
            so.FindProperty("valve").objectReferenceValue = valve;
            so.FindProperty("style").enumValueIndex = (int)style;

            // RadiatorA(Translucent)는 더 투명하게, RadiatorB(Opaque)는 진하게.
            if (style == PipeLeakFog.FogStyle.Translucent)
            {
                so.FindProperty("translucentAlpha").floatValue = 0.08f;
                so.FindProperty("maxEmissionRate").floatValue = 50f;
                so.FindProperty("maxRadius").floatValue = 1.2f;
            }
            else
            {
                so.FindProperty("opaqueAlpha").floatValue = 0.85f;
                so.FindProperty("maxEmissionRate").floatValue = 100f;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // =====================================================================
        // 8. NetworkObject 보장
        // =====================================================================
        static void EnsureNetworkObject(GameObject root)
        {
            if (root.GetComponent<NetworkObject>() == null)
                Undo.AddComponent<NetworkObject>(root);
        }

        // =====================================================================
        // 9. 기존 RadiatorFogVisual 비활성화
        // =====================================================================
        static void DisableExistingRadiatorFogVisual(GameObject root)
        {
            var existing = root.GetComponentsInChildren<RadiatorFogVisual>(true);
            foreach (var f in existing)
            {
                Undo.RecordObject(f, "Disable RadiatorFogVisual");
                f.enabled = false;
            }
        }

        // =====================================================================
        // 헬퍼
        // =====================================================================
        static GameObject FindRootObjectByName(Scene scene, string name)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == name) return go;
            }
            // 루트가 아닐 수도 있으므로 전체 검색 (비활성 포함)
            foreach (var go in scene.GetRootGameObjects())
            {
                var t = go.transform.Find(name);
                if (t != null) return t.gameObject;
                var deep = FindDescendantByName(go.transform, name);
                if (deep != null) return deep.gameObject;
            }
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

        static Transform FindDescendantByName(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c;
                var deeper = FindDescendantByName(c, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        static void EditorSceneManagerSetSceneDirty(Scene scene)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
#endif
