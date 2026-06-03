using System;
using System.Collections;
using System.Collections.Generic;
using Capstone.Network;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 스테이지 전체 진행을 관리하는 네트워크 싱글톤.
/// - 4개 PuzzleController를 인덱스 순서대로 활성화 (순차 진행)
/// - 연구원 대사를 ResearcherDialogue 자산에서 찾아 RPC로 모든 피어에 브로드캐스트
/// - SubtitleHUD는 OnDialoguePlayed 이벤트를 구독해 로컬 화면에 자막 표시
///
/// Shared 모드 가정: 호스트 피어가 GameManager NetworkObject의 State Authority를 보유.
/// 퍼즐 진행 분기는 항상 권한자 측에서 수행, 다른 피어는 [Networked] 상태로 동기화 받음.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("퍼즐 (4개, 인덱스 순서대로 — 0→1→2→3 순차 진행)")]
    [Tooltip("방 모서리에 배치된 4개 기계 퍼즐. 비어 있는 슬롯은 GameManager가 스킵.")]
    public PuzzleController[] puzzles = new PuzzleController[4];

    [Header("대사 데이터")]
    public ResearcherDialogue dialogue;

    [Header("인트로/엔딩 대사 id (ResearcherDialogue 자산 참조)")]
    public string[] introDialogueIds;
    public string[] outroDialogueIds;

    [Header("타이밍")]
    [Tooltip("Spawned 후 인트로 시작까지 대기 시간(권한자 한정)")]
    public float introDelay = 1f;

    [Tooltip("보이스 클립 종료 후 자막이 남아있는 여유 시간(초). SubtitleHUD.voiceTail과 일치시킬 것.")]
    public float dialogueTail = 0.3f;

    [Header("관전자 대사 진행")]
    [Tooltip("켜면 관전자(3번째 입장자)가 R 키를 누를 때마다 대사가 한 줄씩(오디오+자막) 진행됨. " +
             "관전자가 접속해 있지 않으면 보이스 클립 길이에 맞춰 자동 진행(게임이 멈추지 않도록).")]
    public bool spectatorDrivenDialogue = true;

    [Tooltip("Editor에서 관전자 없이도 키보드 R 키로 대사 진행을 테스트 (빌드/실기에선 컨트롤러 A 버튼 + 관전자 사이드에서만 동작).")]
    public bool editorKeyboardAdvanceForTest = true;

    /// <summary>관전자 대사 진행 입력 = 오른손 컨트롤러 A 버튼(primaryButton). 코드에서 생성/활성.</summary>
    InputAction _advanceButtonAction;

    [Networked] public int CurrentPuzzleIndex { get; set; }
    [Networked] public NetworkBool AllCompleted { get; set; }

    /// <summary>관전자가 R 키로 "다음 대사" 를 요청했는지 (StateAuthority 측에서만 소비).</summary>
    bool _dialogueAdvanceRequested;

    /// <summary>Spawned() 가 호출되어 [Networked] 프로퍼티 접근이 안전한지 여부.
    /// Spawn 전에 CurrentPuzzleIndex 등을 읽으면 Fusion 이 InvalidOperationException 을 던지므로,
    /// 외부(Stage3OrbGate 등)에서 네트워크 상태를 폴링할 때 이 값으로 먼저 가드한다.</summary>
    public bool IsSpawnedAndReady => Object != null && Object.IsValid;

    /// <summary>클리어 타임(초)을 모든 피어에 브로드캐스트 — 누가 트리거했든 동일 값 표시. LeaderboardSubmitter가 구독.</summary>
    public event Action<float> OnClearTimeBroadcast;

    bool _debugCompletingAll;

    /// <summary>이미 재생된 이벤트 NPC 대사 큐(중복 방지). 권한자 측이 유일한 판정 주체라
    /// 양쪽 피어가 같은 트리거를 발사해도 한 번만 재생된다.</summary>
    readonly HashSet<string> _firedNpcCues = new HashSet<string>();

    /// <summary>(speaker, text, duration, voiceClip) — SubtitleHUD가 구독해 화면에 표시 + 보이스 재생.</summary>
    public event Action<string, string, float, AudioClip> OnDialoguePlayed;

    /// <summary>퍼즐이 활성화될 때 — 모든 피어에서 호출됨 (RPC 경로).</summary>
    public event Action<int> OnPuzzleActivated;

    /// <summary>전체 게임 클리어 — 모든 피어에서 호출됨.</summary>
    public event Action OnAllPuzzlesCompleted;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GameManager] 중복 인스턴스 감지 — 새 것을 무시.");
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        // 오른손 컨트롤러 A 버튼(Quest Touch = primaryButton)에 바인딩.
        _advanceButtonAction = new InputAction("AdvanceDialogue", InputActionType.Button,
                                               "<XRController>{RightHand}/primaryButton");
        _advanceButtonAction.Enable();
    }

    void OnDisable()
    {
        if (_advanceButtonAction != null)
        {
            _advanceButtonAction.Disable();
            _advanceButtonAction.Dispose();
            _advanceButtonAction = null;
        }
    }

    void Update()
    {
        if (!spectatorDrivenDialogue) return;

        bool isSpectator = LocalPlayerSide.Current == PlayerSide.Spectator;
        bool editorTest = editorKeyboardAdvanceForTest && Application.isEditor;

        bool advance = false;

        // 실기/빌드: 관전자가 오른손 컨트롤러 A 버튼을 누르면 다음 대사.
        if (isSpectator && _advanceButtonAction != null && _advanceButtonAction.WasPressedThisFrame())
            advance = true;

        // Editor 테스트 편의: 키보드 R.
        if (editorTest && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            advance = true;

        if (advance)
            RequestAdvanceDialogue();
    }

    /// <summary>
    /// 관전자가 "다음 대사" 를 요청 — 로컬이 권한자가 아니면 RPC로 호스트(StateAuthority)에 전달.
    /// RequestAdvanceToNextPuzzle 패턴과 동일.
    /// </summary>
    public void RequestAdvanceDialogue()
    {
        if (Object != null && Object.IsValid && !HasStateAuthority)
        {
            RequestAdvanceDialogueRpc();
            return;
        }
        _dialogueAdvanceRequested = true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RequestAdvanceDialogueRpc()
    {
        _dialogueAdvanceRequested = true;
    }

    /// <summary>호스트 측에서 관전자가 한 명이라도 접속해 있는지 확인 (대사 수동/자동 진행 분기).</summary>
    bool AnySpectatorPresent()
    {
        var players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        foreach (var p in players)
            if (p != null && p.IsSpectator) return true;
        return false;
    }

    /// <summary>측정한 클리어 타임을 모든 피어에 브로드캐스트(RPC). 어느 클라가 호출해도 됨 — 권한 무관.</summary>
    public void BroadcastClearTime(float seconds)
    {
        if (Object != null && Object.IsValid) RpcBroadcastClearTime(seconds);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    void RpcBroadcastClearTime(float seconds)
    {
        OnClearTimeBroadcast?.Invoke(seconds);
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentPuzzleIndex = -1;
            AllCompleted = false;
        }

        AutoSetupPuzzles();

        // 모든 피어가 퍼즐 컨트롤러의 완료 이벤트를 듣지만,
        // 실제 진행(다음 퍼즐 활성화)은 권한자만 수행.
        for (int i = 0; i < puzzles.Length; i++)
        {
            if (puzzles[i] == null) continue;
            int captured = i;
            puzzles[i].OnCompleted += _ => HandlePuzzleCompletedLocal(captured);
        }

        if (HasStateAuthority)
            StartCoroutine(BootSequence());
    }

    private void AutoSetupPuzzles()
    {
        // Find existing wrappers
        var foundWrappers = FindObjectsByType<PuzzleController>(FindObjectsSortMode.None);
        foreach (var w in foundWrappers)
        {
            if (w.puzzleIndex >= 0 && w.puzzleIndex < puzzles.Length)
            {
                puzzles[w.puzzleIndex] = w;
                WireKnownPuzzleEvents(w.gameObject, w);
            }
        }

        // Auto-wrap known puzzle logic if slots are empty
        TryWrapPuzzle<PipePuz.SmokePuzzle.PipeAllPuzzleController>(0, "Fix the leaking pipes to restore air flow.");
        TryWrapPuzzle<ClearSoundMaker>(1, "Capture each creature and lock it in the matching cage.");
        TryWrapPuzzle<PipePuz.Zoo.ZooPuzzleController>(1, "Capture all escaped creatures back into their cages.");
        TryWrapPuzzle<PipePuz.LightBeam.LightBeamController>(2, "Align the light beams to power the main reactor.");
    }

    private void TryWrapPuzzle<T>(int index, string hint) where T : MonoBehaviour
    {
        if (index < 0 || index >= puzzles.Length || puzzles[index] != null) return;

        T logic = FindFirstObjectByType<T>();
        if (logic != null)
        {
            PuzzleController wrapper = logic.GetComponent<PuzzleController>();
            if (wrapper == null) wrapper = logic.gameObject.AddComponent<PuzzleController>();

            wrapper.puzzleIndex = index;
            wrapper.puzzleHint = hint;
            
            WireKnownPuzzleEvents(logic.gameObject, wrapper);

            puzzles[index] = wrapper;
            Debug.Log($"[GameManager] Auto-wrapped {typeof(T).Name} into slot {index}");
        }
    }

    void WireKnownPuzzleEvents(GameObject source, PuzzleController wrapper)
    {
        if (source == null || wrapper == null) return;

        var pipe = source.GetComponent<PipePuz.SmokePuzzle.PipeAllPuzzleController>();
        if (pipe != null)
            pipe.OnSolved.AddListener(() => wrapper.CompletePuzzle());

        var zoo = source.GetComponent<PipePuz.Zoo.ZooPuzzleController>();
        if (zoo != null)
            zoo.OnSolved.AddListener(() => wrapper.CompletePuzzle());

        var cageRoom = source.GetComponent<ClearSoundMaker>();
        if (cageRoom != null)
            cageRoom.OnSolved.AddListener(() => wrapper.CompletePuzzle());

        var beam = source.GetComponent<PipePuz.LightBeam.LightBeamController>();
        if (beam != null)
            beam.OnAllReceiversHit.AddListener(() => wrapper.CompletePuzzle());
    }

    IEnumerator BootSequence()
    {
        yield return new WaitForSeconds(introDelay);

        yield return PlayDialogueSequence(introDialogueIds);

        AdvanceToNextPuzzle();
    }

    /// <summary>외부에서 직접 진행을 강제하고 싶을 때 (디버그/스킵 버튼).</summary>
    public void AdvanceToNextPuzzle()
    {
        if (!HasStateAuthority) return;

        int next = CurrentPuzzleIndex + 1;

        // 비어 있는 슬롯은 스킵
        while (next < puzzles.Length && puzzles[next] == null) next++;

        if (next >= puzzles.Length)
        {
            CurrentPuzzleIndex = puzzles.Length;
            AllCompleted = true;
            StartCoroutine(OutroSequence());
            return;
        }

        CurrentPuzzleIndex = next;
        ActivatePuzzleRpc(next);
        StartCoroutine(PlayPuzzleStartDialogue(next));
    }

    public void RequestAdvanceToNextPuzzle()
    {
        if (Object != null && Object.IsValid && !HasStateAuthority)
        {
            RequestAdvanceToNextPuzzleRpc();
            return;
        }

        AdvanceToNextPuzzle();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RequestAdvanceToNextPuzzleRpc()
    {
        AdvanceToNextPuzzle();
    }

    /// <summary>
    /// Debug/test hook: completes the currently active puzzle through the same
    /// PuzzleController.CompletePuzzle path used by real puzzle solve events.
    /// Safe to call from either headset; the request is routed to state authority.
    /// </summary>
    public void DebugCompleteCurrentPuzzle()
    {
        DebugCompletePuzzle(CurrentPuzzleIndex);
    }

    /// <summary>
    /// Debug/test hook for a specific active puzzle. This intentionally does not
    /// jump game state; non-current puzzle requests are ignored so the normal
    /// completion dialogue and progression path stays intact.
    /// </summary>
    public void DebugCompletePuzzle(int puzzleIndex)
    {
        if (Object != null && Object.IsValid && !HasStateAuthority)
        {
            DebugCompletePuzzleRpc(puzzleIndex);
            return;
        }

        CompletePuzzleAsSolved(puzzleIndex);
    }

    /// <summary>
    /// Debug/test hook: completes each remaining current puzzle in sequence,
    /// waiting for GameManager's normal advance coroutine between completions.
    /// </summary>
    public void DebugCompleteAllRemainingPuzzles()
    {
        if (Object != null && Object.IsValid && !HasStateAuthority)
        {
            DebugCompleteAllRemainingPuzzlesRpc();
            return;
        }

        if (!_debugCompletingAll)
            StartCoroutine(DebugCompleteAllRemainingRoutine());
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void DebugCompletePuzzleRpc(int puzzleIndex)
    {
        CompletePuzzleAsSolved(puzzleIndex);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void DebugCompleteAllRemainingPuzzlesRpc()
    {
        if (!_debugCompletingAll)
            StartCoroutine(DebugCompleteAllRemainingRoutine());
    }

    void CompletePuzzleAsSolved(int puzzleIndex)
    {
        if (!HasStateAuthority) return;
        if (AllCompleted) return;
        if (puzzleIndex != CurrentPuzzleIndex)
        {
            Debug.LogWarning($"[GameManager] Debug skip ignored. Requested puzzle {puzzleIndex}, current puzzle is {CurrentPuzzleIndex}.");
            return;
        }
        if (puzzleIndex < 0 || puzzleIndex >= puzzles.Length)
        {
            Debug.LogWarning($"[GameManager] Debug skip ignored. Invalid puzzle index: {puzzleIndex}.");
            return;
        }

        var puzzle = puzzles[puzzleIndex];
        if (puzzle == null)
        {
            Debug.LogWarning($"[GameManager] Debug skip ignored. Puzzle slot {puzzleIndex} is empty.");
            return;
        }
        if (puzzle.IsCompleted) return;

        Debug.Log($"[GameManager] Debug completing puzzle {puzzleIndex} through PuzzleController.CompletePuzzle().");
        puzzle.CompletePuzzle();
    }

    IEnumerator DebugCompleteAllRemainingRoutine()
    {
        _debugCompletingAll = true;

        while (!AllCompleted)
        {
            int before = CurrentPuzzleIndex;
            CompletePuzzleAsSolved(before);

            float timeout = 30f;
            while (!AllCompleted && CurrentPuzzleIndex == before && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (timeout <= 0f)
            {
                Debug.LogWarning("[GameManager] Debug skip all stopped while waiting for next puzzle.");
                break;
            }

            yield return null;
        }

        _debugCompletingAll = false;
    }

    IEnumerator PlayPuzzleStartDialogue(int index)
    {
        var puzzle = puzzles[index];
        if (puzzle == null) yield break;
        yield return PlayDialogueSequence(puzzle.startDialogueIds);
    }

    IEnumerator OutroSequence()
    {
        OnAllPuzzlesCompleted?.Invoke();
        yield return PlayDialogueSequence(outroDialogueIds);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void ActivatePuzzleRpc(int index)
    {
        if (index < 0 || index >= puzzles.Length) return;
        if (puzzles[index] == null) return;
        puzzles[index].Begin();
        OnPuzzleActivated?.Invoke(index);
    }

    void HandlePuzzleCompletedLocal(int index)
    {
        if (!HasStateAuthority) return;
        if (index != CurrentPuzzleIndex) return; // 중복 완료 보호
        StartCoroutine(AdvanceAfterCompleteDialogue(index));
    }

    IEnumerator AdvanceAfterCompleteDialogue(int index)
    {
        var puzzle = puzzles[index];
        if (puzzle != null)
            yield return PlayDialogueSequence(puzzle.completeDialogueIds);
        AdvanceToNextPuzzle();
    }

    /// <summary>
    /// 권한자가 호출 — 자막을 모든 피어에 동기 재생.
    /// 퍼즐 컨트롤러는 이 메서드를 통해 sub-step 시점에 자유롭게 대사를 트리거.
    /// 권한자가 아닌 측에서 호출하면 무시 (RPC 권한 충돌 방지).
    /// </summary>
    public void PlayDialogue(string id)
    {
        if (!HasStateAuthority) return;
        var line = dialogue != null ? dialogue.Find(id) : null;
        if (line == null)
        {
            Debug.LogWarning($"[GameManager] dialogue id 없음: '{id}'");
            return;
        }
        PlayDialogueRpc(id);
    }

    IEnumerator PlayDialogueAndWait(string id)
    {
        var line = dialogue != null ? dialogue.Find(id) : null;
        if (line == null) yield break;
        PlayDialogueRpc(id);
        yield return new WaitForSeconds(DialogueDisplayTime(line));
    }

    /// <summary>
    /// 대사 묶음을 순서대로 재생. 관전자가 접속해 있고 spectatorDrivenDialogue가 켜져 있으면
    /// 매 줄마다 관전자의 R 키 입력을 기다렸다가 다음 오디오+자막을 재생(수동 진행).
    /// 관전자가 없으면 기존처럼 보이스 클립 길이에 맞춰 자동 진행 — 2인 플레이가 멈추지 않도록.
    /// (StateAuthority 코루틴에서만 호출됨)
    /// </summary>
    IEnumerator PlayDialogueSequence(IEnumerable<string> ids)
    {
        if (ids == null) yield break;

        bool manual = spectatorDrivenDialogue && AnySpectatorPresent();

        foreach (var id in ids)
        {
            var line = dialogue != null ? dialogue.Find(id) : null;
            if (line == null) continue;

            if (manual)
            {
                // 관전자가 R 키를 누를 때까지 대기 → 그때 다음 대사 재생.
                _dialogueAdvanceRequested = false;
                yield return new WaitUntil(() => _dialogueAdvanceRequested);
                _dialogueAdvanceRequested = false;

                PlayDialogueRpc(id);

                // 같은 프레임의 중복 트리거가 다음 줄을 즉시 건너뛰지 않도록 한 프레임 양보.
                yield return null;
            }
            else
            {
                PlayDialogueRpc(id);
                yield return new WaitForSeconds(DialogueDisplayTime(line));
            }
        }
    }

    /// <summary>자막이 화면에 떠 있어야 할 시간. 보이스가 있으면 클립 길이에 맞춰 다음 대사로 넘어감.</summary>
    float DialogueDisplayTime(ResearcherDialogue.Line line)
    {
        if (line.voiceClip != null) return line.voiceClip.length + dialogueTail;
        return line.duration;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void PlayDialogueRpc(string id)
    {
        // AudioClip은 네트워크 직렬화 불가 → id만 전송하고, 각 피어가 동일한
        // ResearcherDialogue 자산에서 라인을 로컬 조회해 같은 보이스 클립을 재생.
        var line = dialogue != null ? dialogue.Find(id) : null;
        if (line == null)
        {
            Debug.LogWarning($"[GameManager] PlayDialogueRpc: id '{id}' 로컬 조회 실패");
            return;
        }
        OnDialoguePlayed?.Invoke(line.speaker, line.text, line.duration, line.voiceClip);
    }

    // ===== 이벤트 기반 NPC 대사 (버튼/도착/퍼즐 해결 등에서 호출) =====

    /// <summary>
    /// 게임 이벤트에서 호출하는 NPC 대사 트리거.
    /// - 어느 피어(P1/P2/관전자)에서 호출하든 StateAuthority로 라우팅되어 한 번만 처리됨.
    /// - 같은 cue 가 다시 발사돼도 무시(1회 재생).
    /// - id 를 여러 개 넘기면 (예: "A1","A2") 보이스 클립 길이에 맞춰 순서대로 재생.
    /// 자막/음성은 기존 PlayDialogueRpc 경로로 모든 피어에 동기 표시된다.
    /// </summary>
    public void TriggerNpcCue(params string[] ids)
    {
        if (ids == null || ids.Length == 0) return;
        string key = string.Join(",", ids);

        // 권한자가 아니면 호스트(StateAuthority)에 요청을 넘긴다. (RequestAdvanceDialogue 패턴과 동일)
        if (Object != null && Object.IsValid && !HasStateAuthority)
        {
            TriggerNpcCueRpc(key);
            return;
        }

        FireNpcCueAuthoritative(key);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void TriggerNpcCueRpc(string key)
    {
        FireNpcCueAuthoritative(key);
    }

    /// <summary>권한자(또는 네트워크 미스폰 에디터 단독) 측에서만 실제 재생. 중복 cue 는 무시.</summary>
    void FireNpcCueAuthoritative(string key)
    {
        // 스폰된 네트워크 객체인데 권한이 없으면 무시 (RPC 권한 충돌 방지).
        if (Object != null && Object.IsValid && !HasStateAuthority) return;
        if (string.IsNullOrEmpty(key)) return;
        if (!_firedNpcCues.Add(key)) return; // 이미 재생한 cue — 중복 트리거 무시
        StartCoroutine(PlayNpcCueRoutine(key.Split(',')));
    }

    IEnumerator PlayNpcCueRoutine(string[] ids)
    {
        foreach (var id in ids)
        {
            var line = dialogue != null ? dialogue.Find(id) : null;
            if (line == null)
            {
                Debug.LogWarning($"[GameManager] NPC cue id 없음: '{id}'");
                continue;
            }
            EmitDialogue(id, line);
            yield return new WaitForSeconds(DialogueDisplayTime(line));
        }
    }

    /// <summary>네트워크 스폰 상태면 RPC로 전 피어 동기 재생, 미스폰(에디터 단독)이면 로컬에서 바로 표시.</summary>
    void EmitDialogue(string id, ResearcherDialogue.Line line)
    {
        if (Object != null && Object.IsValid)
            PlayDialogueRpc(id);
        else
            OnDialoguePlayed?.Invoke(line.speaker, line.text, line.duration, line.voiceClip);
    }

    /// <summary>현재 활성 퍼즐의 힌트 문자열 (UI에서 사용).</summary>
    public string CurrentPuzzleHint
    {
        get
        {
            int i = CurrentPuzzleIndex;
            if (i < 0 || i >= puzzles.Length || puzzles[i] == null) return string.Empty;
            return puzzles[i].puzzleHint;
        }
    }
}
