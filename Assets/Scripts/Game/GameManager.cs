using System;
using System.Collections;
using Fusion;
using UnityEngine;

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

    [Networked] public int CurrentPuzzleIndex { get; set; }
    [Networked] public NetworkBool AllCompleted { get; set; }

    /// <summary>(speaker, text, duration) — SubtitleHUD가 구독해 화면에 표시.</summary>
    public event Action<string, string, float> OnDialoguePlayed;

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

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentPuzzleIndex = -1;
            AllCompleted = false;
        }

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

    IEnumerator BootSequence()
    {
        yield return new WaitForSeconds(introDelay);

        if (introDialogueIds != null)
        {
            foreach (var id in introDialogueIds)
                yield return PlayDialogueAndWait(id);
        }

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

    IEnumerator PlayPuzzleStartDialogue(int index)
    {
        var puzzle = puzzles[index];
        if (puzzle == null || puzzle.startDialogueIds == null) yield break;
        foreach (var id in puzzle.startDialogueIds)
            yield return PlayDialogueAndWait(id);
    }

    IEnumerator OutroSequence()
    {
        OnAllPuzzlesCompleted?.Invoke();
        if (outroDialogueIds == null) yield break;
        foreach (var id in outroDialogueIds)
            yield return PlayDialogueAndWait(id);
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
        if (puzzle != null && puzzle.completeDialogueIds != null)
        {
            foreach (var id in puzzle.completeDialogueIds)
                yield return PlayDialogueAndWait(id);
        }
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
        PlayDialogueRpc(line.speaker, line.text, line.duration);
    }

    IEnumerator PlayDialogueAndWait(string id)
    {
        var line = dialogue != null ? dialogue.Find(id) : null;
        if (line == null) yield break;
        PlayDialogueRpc(line.speaker, line.text, line.duration);
        yield return new WaitForSeconds(line.duration);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void PlayDialogueRpc(string speaker, string text, float duration)
    {
        OnDialoguePlayed?.Invoke(speaker, text, duration);
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
