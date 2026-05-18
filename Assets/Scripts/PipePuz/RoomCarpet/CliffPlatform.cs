using UnityEngine;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 절벽(Cliff) 모드에서 안전 발판 마커.
    /// <see cref="CliffController"/> 가 매 프레임 카메라 아래 raycast 로 CliffPlatform 콜라이더에 닿으면
    /// "마지막 발판" 으로 갱신. 낙하 임계값 이하로 떨어지면 마지막 발판의 <see cref="Dock"/> 위치로 리스폰.
    /// </summary>
    public class CliffPlatform : MonoBehaviour
    {
        [Tooltip("리스폰 시 XR Origin 카메라가 정렬될 위치. 비워두면 platform 자기 transform 사용.")]
        public Transform Dock;

        public Transform GetDock() => Dock != null ? Dock : transform;
    }
}
