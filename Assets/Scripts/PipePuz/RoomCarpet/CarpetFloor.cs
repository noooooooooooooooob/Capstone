using UnityEngine;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 카펫이 안착할 수 있는 표면 마커. 비어있다 — DisappearingCarpet 의 OnCollisionEnter
    /// 에서 GetComponent&lt;CarpetFloor&gt;() 로 검사해서 안착 여부 결정.
    ///
    /// 시각은 위험해 보이는 표면(빨간 격자 등)이지만 카펫이 떨어졌을 때만 안전한 발판이 된다.
    /// </summary>
    public class CarpetFloor : MonoBehaviour
    {
    }
}
