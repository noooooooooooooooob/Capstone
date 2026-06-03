using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 현재 열려 있는 씬(Main Scene)에서 연구원(이 박사) NPC 대사 트리거를 자동으로 연결한다.
///
/// 메뉴: Capstone ▸ NPC ▸ Setup Dialogue Triggers (현재 씬)
///
/// 연결 대상(컴포넌트에 <see cref="NpcCueBinder"/> 부착):
///   A4  PipeMiniGame2Board.OnSolved              → Stage1 파이프 미니게임2 완성
///   B2  ClearSoundMaker.OnSolved (+ ZooPuzzleController) → Stage2 케이지 클리어
///   C1  Stage3 RoomSeen/Room (Stage1 Modular) 의 문 OnOpened → 문 열림
///   C2  Stage3 RoomSeen/RoomCliff (Stage1 Skin) 의 LightOrbSocket.OnOrbInserted → 구체 도킹
///   C3  Stage3 RoomSeen 의 LightBeamController.OnAllReceiversHit → 광선 퍼즐 클리어
///   B1  새 트리거 볼륨(NPC_CueTrigger_B1_CageRoom) 생성 → 직접 위치/크기 지정 필요
///
/// A1/A2/A3 은 MainControlSystem.cs 가 코드로 직접 호출하므로 여기서 손대지 않는다.
/// </summary>
public static class NpcDialogueSetupTool
{
    const string RoomSeenName = "RoomSeen";
    const string CliffSkinName = "RoomCliff (Stage1 Skin)";
    const string ModularRoomName = "Room (Stage1 Modular)";

    [MenuItem("Capstone/NPC/Setup Dialogue Triggers (현재 씬)")]
    public static void SetupCurrentScene()
    {
        var log = new StringBuilder();
        int wired = 0;

        // 0) GameManager / dialogue 자산 점검
        var gm = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gm == null)
            log.AppendLine("⚠ GameManager 를 씬에서 찾지 못함 — 트리거는 붙지만 런타임에 재생되지 않음.");
        else if (gm.dialogue == null)
            log.AppendLine("⚠ GameManager.dialogue 가 비어 있음 — ResearcherDialogue 자산(Dialogue.asset)을 연결하세요.");

        // A4 — Pipe MiniGame2
        var board = Object.FindFirstObjectByType<PipePuz.MiniGame2.PipeMiniGame2Board>(FindObjectsInactive.Include);
        if (board != null && AddBinder(board.gameObject, NpcCueBinder.TriggerSource.PipeMiniGame2Solved, new[] { "A4" }, log))
            wired++;
        else if (board == null)
            log.AppendLine("• A4: PipeMiniGame2Board 없음 — 건너뜀.");

        // B2 — Stage2 클리어 (ClearSoundMaker + ZooPuzzleController 둘 다 지원)
        var clear = Object.FindFirstObjectByType<ClearSoundMaker>(FindObjectsInactive.Include);
        if (clear != null && AddBinder(clear.gameObject, NpcCueBinder.TriggerSource.Stage2Solved, new[] { "B2" }, log))
            wired++;
        var zoo = Object.FindFirstObjectByType<PipePuz.Zoo.ZooPuzzleController>(FindObjectsInactive.Include);
        if (zoo != null && (clear == null || zoo.gameObject != clear.gameObject))
        {
            if (AddBinder(zoo.gameObject, NpcCueBinder.TriggerSource.Stage2Solved, new[] { "B2" }, log))
                wired++;
        }
        if (clear == null && zoo == null)
            log.AppendLine("• B2: ClearSoundMaker / ZooPuzzleController 둘 다 없음 — 건너뜀.");

        // Stage3 루트
        Transform roomSeen = FindByExactName(RoomSeenName);
        if (roomSeen == null)
        {
            log.AppendLine($"⚠ '{RoomSeenName}' 를 찾지 못함 — C1/C2/C3 자동 연결 건너뜀. 직접 NpcCueBinder 를 붙이세요.");
        }
        else
        {
            // C2 — LightOrbSocket (RoomCliff (Stage1 Skin) 하위)
            Transform cliffSkin = FindChildByNameContains(roomSeen, "Stage1 Skin") ?? FindChildByNameContains(roomSeen, CliffSkinName);
            Transform c2Root = cliffSkin != null ? cliffSkin : roomSeen;
            var socket = c2Root.GetComponentInChildren<PipePuz.LightBeam.LightOrbSocket>(true);
            if (socket != null && AddBinder(socket.gameObject, NpcCueBinder.TriggerSource.LightOrbInserted, new[] { "C2" }, log))
                wired++;
            else if (socket == null)
                log.AppendLine("• C2: RoomCliff (Stage1 Skin) 하위에서 LightOrbSocket 을 찾지 못함 — 건너뜀.");

            // C1 — 문 (Room (Stage1 Modular) 하위)
            Transform modular = FindChildByNameContains(roomSeen, "Stage1 Modular") ?? FindChildByNameContains(roomSeen, ModularRoomName);
            Transform c1Root = modular != null ? modular : roomSeen;
            GameObject doorGo = FindDoorWithOpenedEvent(c1Root);
            if (doorGo != null && AddBinder(doorGo, NpcCueBinder.TriggerSource.DoorOpened, new[] { "C1" }, log))
                wired++;
            else if (doorGo == null)
                log.AppendLine("• C1: Room (Stage1 Modular) 하위에서 OnOpened 이벤트가 있는 문(AutoSlidingDoor/Stage1SlidingDoor)을 찾지 못함 — 건너뜀.");

            // C3 — LightBeamController (RoomSeen 하위)
            var beams = roomSeen.GetComponentsInChildren<PipePuz.LightBeam.LightBeamController>(true);
            if (beams.Length == 0)
                log.AppendLine("• C3: RoomSeen 하위에서 LightBeamController 를 찾지 못함 — 건너뜀.");
            foreach (var beam in beams)
            {
                if (AddBinder(beam.gameObject, NpcCueBinder.TriggerSource.LightBeamSolved, new[] { "C3" }, log))
                    wired++;
            }
            if (beams.Length > 1)
                log.AppendLine($"  ↳ C3: LightBeamController 가 {beams.Length}개 발견됨 — 모두 연결함. Stage3 외 것이 섞였다면 불필요한 binder 는 제거하세요.");
        }

        // B1 — 케이지룸 진입 트리거 볼륨(직접 위치 지정 필요)
        EnsureB1TriggerVolume(log, ref wired);

        // 저장 표시
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        log.Insert(0, $"=== NPC Dialogue Setup 완료 — {wired}개 트리거 연결 ===\n");
        log.AppendLine("\n남은 수동 단계: B1 트리거 볼륨(NPC_CueTrigger_B1_CageRoom)을 CageRoom 입구에 맞게 위치·크기 조정 후 씬 저장.");
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("NPC Dialogue Setup",
            $"{wired}개 트리거를 연결했습니다.\n자세한 내용은 Console 로그를 확인하세요.\n\n※ B1 트리거 볼륨은 직접 위치를 잡아야 합니다.", "확인");
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────────────

    static bool AddBinder(GameObject go, NpcCueBinder.TriggerSource source, string[] cueIds, StringBuilder log)
    {
        // 같은 source 의 binder 가 이미 있으면 cueIds 만 갱신(중복 부착 방지).
        foreach (var existing in go.GetComponents<NpcCueBinder>())
        {
            if (existing.source == source)
            {
                Undo.RecordObject(existing, "Update NpcCueBinder");
                existing.cueIds = cueIds;
                EditorUtility.SetDirty(existing);
                log.AppendLine($"• {string.Join(",", cueIds)}: '{go.name}' 의 기존 binder 갱신 (source={source}).");
                return true;
            }
        }

        var binder = Undo.AddComponent<NpcCueBinder>(go);
        binder.source = source;
        binder.cueIds = cueIds;
        binder.fireOnce = true;
        EditorUtility.SetDirty(binder);
        log.AppendLine($"• {string.Join(",", cueIds)}: '{go.name}' 에 NpcCueBinder 부착 (source={source}).");
        return true;
    }

    static void EnsureB1TriggerVolume(StringBuilder log, ref int wired)
    {
        const string b1Name = "NPC_CueTrigger_B1_CageRoom";
        Transform existing = FindByExactName(b1Name);
        if (existing != null)
        {
            log.AppendLine($"• B1: '{b1Name}' 이미 존재 — binder 만 점검.");
            AddBinder(existing.gameObject, NpcCueBinder.TriggerSource.TriggerVolumeEnter, new[] { "B1" }, log);
            wired++;
            return;
        }

        var go = new GameObject(b1Name);
        Undo.RegisterCreatedObjectUndo(go, "Create B1 Trigger");
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(3f, 3f, 3f);
        AddBinder(go, NpcCueBinder.TriggerSource.TriggerVolumeEnter, new[] { "B1" }, log);
        wired++;
        log.AppendLine($"• B1: '{b1Name}' 생성(BoxCollider trigger). ★ CageRoom 입구에 위치/크기를 직접 조정하세요.");
    }

    /// <summary>OnOpened 이벤트를 가진 문 컴포넌트를 root 하위(자기 포함)에서 찾는다.</summary>
    static GameObject FindDoorWithOpenedEvent(Transform root)
    {
        var autoDoor = root.GetComponentInChildren<PipePuz.RoomCarpet.AutoSlidingDoor>(true);
        if (autoDoor != null) return autoDoor.gameObject;
        var stage1Door = root.GetComponentInChildren<Stage1.Stage1SlidingDoor>(true);
        if (stage1Door != null) return stage1Door.gameObject;
        return null;
    }

    static Transform FindByExactName(string exactName)
    {
        foreach (var t in AllSceneTransforms())
            if (t.name == exactName) return t;
        return null;
    }

    static Transform FindChildByNameContains(Transform root, string contains)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name.Contains(contains)) return t;
        return null;
    }

    static IEnumerable<Transform> AllSceneTransforms()
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                yield return t;
    }
}
