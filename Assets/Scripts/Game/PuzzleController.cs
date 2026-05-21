using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 퍼즐 1개의 상태/이벤트 컨테이너.
/// 진행 분기(다음 퍼즐 활성화 등)는 GameManager가 OnCompleted 구독으로 처리.
/// 퍼즐 고유 로직은 모두 인스펙터 UnityEvent로 연결 (사운드/파티클/대사/완료 트리거).
/// </summary>
public class PuzzleController : MonoBehaviour
{
    [Tooltip("0~3 — GameManager.puzzles 슬롯의 인덱스. 진행 순서를 결정.")]
    public int puzzleIndex;

    [Header("힌트 (자막/UI에서 어떤 퍼즐인지 명확히 함)")]
    [Tooltip("이 퍼즐 전용 — 다른 퍼즐 힌트와 혼동되지 않도록 작성.")]
    [TextArea(2, 4)] public string puzzleHint;

    [Header("대사 시퀀스 (id는 ResearcherDialogue 자산 참조)")]
    [Tooltip("Begin() 호출 직후 GameManager가 순서대로 자동 재생")]
    public string[] startDialogueIds;

    [Tooltip("Complete 직후 GameManager가 순서대로 자동 재생 후 다음 퍼즐로 진행")]
    public string[] completeDialogueIds;

    [Header("인스펙터 이벤트 — 사운드/파티클/셰이더 토글/sub-step 연출 연결")]
    [Tooltip("Begin() 호출 시 1회 발생")]
    public UnityEvent OnPuzzleActivated;

    [Tooltip("CompletePuzzle() 호출 시 1회 발생")]
    public UnityEvent OnPuzzleCompletedEvent;

    /// <summary>GameManager가 구독 — 다음 퍼즐 진행을 트리거.</summary>
    public event Action<PuzzleController> OnCompleted;

    public bool IsActive { get; private set; }
    public bool IsCompleted { get; private set; }

    /// <summary>GameManager가 순차 진행 시 호출.</summary>
    public void Begin()
    {
        if (IsActive || IsCompleted) return;
        IsActive = true;
        OnPuzzleActivated?.Invoke();
    }

    /// <summary>
    /// UnityEvent에서 호출 — 퍼즐 완료 처리.
    /// GameManager가 OnCompleted를 받아 자동으로 다음 퍼즐을 활성화.
    /// </summary>
    public void CompletePuzzle()
    {
        if (IsCompleted) return;
        IsActive = false;
        IsCompleted = true;
        OnPuzzleCompletedEvent?.Invoke();
        OnCompleted?.Invoke(this);
    }

    /// <summary>
    /// UnityEvent에서 호출 — id에 해당하는 연구원 대사를 양쪽 피어에 자막 표시.
    /// 활성화 상태가 아니면 무시 (다른 퍼즐 트리거가 잘못 발사돼도 안전).
    /// </summary>
    public void PlayDialogue(string id)
    {
        if (!IsActive) return;
        if (GameManager.Instance == null) return;
        GameManager.Instance.PlayDialogue(id);
    }
}
