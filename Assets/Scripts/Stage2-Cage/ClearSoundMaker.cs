using Fusion;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Stage 2 케이지 퍼즐의 네트워크 완료 판정.
/// 각 케이지가 정답 생물로 잠기면 ReportCageLocked(cageIndex)를 호출 → 권한자가
/// LockedMask 비트를 세팅(어느 피어에서 보고하든 중복/누락 없이 1회만 집계).
/// 모든 케이지가 잠기면 [Networked] Solved 가 동기화되어 모든 피어에서
/// 승리음 재생 + OnSolved(퍼즐 완료) 가 발생한다.
///
/// 요구: 이 GameObject 에 NetworkObject 컴포넌트가 있어야 한다(씬 배치 NetworkObject).
/// </summary>
public class ClearSoundMaker : NetworkBehaviour
{
    public static ClearSoundMaker Instance;
    public AudioClip victorySound;
    public int totalCages = 4;

    public UnityEvent OnSolved;

    [Networked] public int LockedMask { get; set; }
    [Networked, OnChangedRender(nameof(OnSolvedChanged))] public NetworkBool Solved { get; set; }

    /// <summary>
    /// 네트워크 동기화된 클리어 상태를 어느 피어(호스트/클라이언트)에서든 안전하게 폴링.
    /// 스폰 전·파괴 후에는 false. Solved 가 동기화되면 양쪽에서 동일하게 true 가 된다.
    /// </summary>
    public static bool IsSolved =>
        Instance != null && Instance.Object != null && Instance.Object.IsValid && Instance.Solved;

    void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        Instance = this;
        AssignCageIndices();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 씬의 모든 케이지에 결정적(태그→이름 순) 인덱스를 부여 — 양쪽 피어에서 동일한 비트 의미.
    void AssignCageIndices()
    {
        var cages = FindObjectsByType<Cage>(FindObjectsSortMode.None);
        System.Array.Sort(cages, (a, b) =>
        {
            int c = string.CompareOrdinal(a.correctCreatureTag, b.correctCreatureTag);
            return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
        });
        for (int i = 0; i < cages.Length; i++)
            cages[i].puzzleCageIndex = i;
    }

    /// <summary>케이지가 정답 생물로 잠겼을 때 호출(어느 피어에서든). 권한자에 RPC로 집계.</summary>
    public void ReportCageLocked(int cageIndex)
    {
        if (cageIndex < 0) return;
        if (Object == null || !Object.IsValid) return;
        ReportCageLockedRpc(cageIndex);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void ReportCageLockedRpc(int cageIndex)
    {
        if (cageIndex < 0 || cageIndex >= 32) return;

        int bit = 1 << cageIndex;
        if ((LockedMask & bit) != 0) return; // 이미 집계된 케이지
        LockedMask |= bit;

        int count = PopCount(LockedMask);
        Debug.Log($"[ClearSoundMaker] Correct: {count}/{totalCages}");

        if (!Solved && count >= totalCages)
            Solved = true;
    }

    // Solved 가 false→true 로 동기화될 때 모든 피어에서 실행.
    void OnSolvedChanged()
    {
        if (!Solved) return;

        if (victorySound != null)
        {
            Vector3 position = Camera.main != null ? Camera.main.transform.position : transform.position;
            AudioSource.PlayClipAtPoint(victorySound, position);
        }

        OnSolved?.Invoke();
    }

    static int PopCount(int mask)
    {
        int count = 0;
        while (mask != 0)
        {
            mask &= mask - 1;
            count++;
        }
        return count;
    }
}
