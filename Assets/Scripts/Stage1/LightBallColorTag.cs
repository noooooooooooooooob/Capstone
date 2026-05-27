using UnityEngine;

namespace Stage1
{
    /// <summary>
    /// LightBall에 부착하는 색상 마커 (시각/디버그 식별용).
    /// 게임 로직은 MelterColorChip이 머신 색을 기준으로 결정.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightBallColorTag : MonoBehaviour
    {
        public LightBallColor color = LightBallColor.Red;
    }
}
