using UnityEngine;

namespace Stage1
{
    /// <summary>
    /// 해동 성공 시 MelterColorChip이 배터리에 자동으로 부착하는 색상 마커.
    /// 어느 색 ThawingMachine에서 해동됐는지를 기록.
    /// MultiBatterySlotPanel이 슬롯별 매칭 색만 받기 위해 사용.
    /// </summary>
    [DisallowMultipleComponent]
    public class BatteryColorTag : MonoBehaviour
    {
        public LightBallColor color = LightBallColor.Red;
    }
}
