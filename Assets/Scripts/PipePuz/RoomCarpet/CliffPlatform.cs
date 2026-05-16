using UnityEngine;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 절벽(Cliff) 모드에서 P2 가 안전하게 설 수 있는 영구 발판 마커.
    ///
    /// <see cref="CliffController"/> 가 매 프레임 카메라(머리) 아래로 raycast 해서
    /// CliffPlatform 콜라이더에 닿으면 "마지막 발판" 으로 갱신.
    /// 낙하 임계값 이하로 떨어지면 마지막 발판의 <see cref="Dock"/> 위치로 리스폰.
    ///
    /// 떠 있는 카펫(<see cref="DisappearingCarpet"/> floating mode)은 의도적으로 발판으로 추적하지 않음 —
    /// 카펫은 일시적이라 카펫 위에서 떨어지면 카펫이 있던 자리가 아니라 그 전에 밟았던 영구 발판으로
    /// 되돌려야 게임 진행에 적합.
    /// </summary>
    public class CliffPlatform : MonoBehaviour
    {
        [Tooltip("리스폰 시 XR Origin 의 카메라가 정렬될 위치. 비워두면 platform 자기 transform 사용.")]
        public Transform Dock;

        public Transform GetDock() => Dock != null ? Dock : transform;
    }
}
