using UnityEngine;

/// <summary>
/// 인터랙터블 GameObject에 부착해 어느 사이드(P1/P2)가 조작할 수 있는지 표시한다.
/// 권한 게이팅은 OwnerSelectFilter, 시각 단서는 OwnerVisualCue(2단계)가 이 마커를 읽는다.
/// </summary>
[DisallowMultipleComponent]
public class OwnerSide : MonoBehaviour
{
    [Tooltip("이 오브젝트를 조작할 수 있는 플레이어 사이드.")]
    [SerializeField] PlayerSide owner = PlayerSide.P1;

    public PlayerSide Owner => owner;

    /// <summary>로컬 사이드가 결정됐고 이 오브젝트의 소유자와 일치하는지.</summary>
    public bool IsLocalOwner =>
        LocalPlayerSide.Current.HasValue && LocalPlayerSide.Current.Value == owner;
}
