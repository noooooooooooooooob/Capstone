using System;

/// <summary>
/// 단일 공유 방 기획에서의 플레이어 사이드.
/// NetworkPlayer.Slot (0/1)와 1:1 매핑된다.
/// 인터랙션 권한 게이팅과 시각 단서 분기의 단일 키.
/// </summary>
public enum PlayerSide
{
    P1 = 0,
    P2 = 1,
}

/// <summary>
/// 로컬 디바이스의 플레이어 사이드 캐시.
/// Shared 모드에서 자기 NetworkPlayer가 Spawned 될 때 Set,
/// Despawn 될 때 Clear가 호출된다.
/// 결정되기 전에는 Current가 null — 권한 필터는 보수적으로 차단해야 한다.
/// </summary>
public static class LocalPlayerSide
{
    /// <summary>현재 로컬 사이드. null이면 아직 결정 전(또는 Despawn 후).</summary>
    public static PlayerSide? Current { get; private set; }

    /// <summary>사이드 변경 시 호출 — 시각 단서 컴포넌트가 이걸 구독해 즉시 갱신.</summary>
    public static event Action<PlayerSide?> Changed;

    public static void Set(PlayerSide side)
    {
        if (Current == side) return;
        Current = side;
        Changed?.Invoke(Current);
    }

    public static void Clear()
    {
        if (!Current.HasValue) return;
        Current = null;
        Changed?.Invoke(Current);
    }

    /// <summary>NetworkPlayer.Slot → PlayerSide.</summary>
    public static PlayerSide FromSlot(int slot) => slot == 0 ? PlayerSide.P1 : PlayerSide.P2;
}
