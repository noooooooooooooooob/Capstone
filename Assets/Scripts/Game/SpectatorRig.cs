using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

/// <summary>
/// 3번째 입장자(관전자)의 로컬 XR 리그를 "그냥 카메라"로 만든다.
///  - 중력 제거: XRI GravityProvider 비활성 → 떨어지지 않음
///  - 벽 통과: CharacterController 비활성 → ContinuousMoveProvider가
///    CC 대신 XROrigin 트랜스폼을 직접 이동(useCharacterControllerIfExists는
///    "enabled된 CC"만 사용) → 콜라이더와 충돌하지 않고 통과
///  - 머리 클램프 제거: CharacterControllerHeadFollow 비활성 → 머리도 벽을 통과
///  - 이동 분리:
///      · 왼쪽 조이스틱 → ContinuousMoveProvider(수평 앞뒤좌우, enableStrafe로 좌우 보장)
///      · 오른쪽 조이스틱 → SpectatorVerticalFly(상하 비행)
///
/// 관전자는 PlayerSide.Spectator라 어떤 오브젝트의 소유자와도 일치하지 않으므로
/// 인터랙션은 OwnerSelectFilter에서 자동 차단된다 — 여기선 이동/충돌만 처리.
///
/// 로컬 전용 변경이며 네트워크에 영향을 주지 않는다. 비대칭 권한/씬 공유 설계는
/// 그대로 유지된다(관전자는 같은 방 좌표계에서 자유 비행하는 카메라일 뿐).
/// </summary>
public static class SpectatorRig
{
    public static void Apply(XROrigin origin)
    {
        if (origin == null)
        {
            Debug.LogWarning("[SpectatorRig] origin이 null — 관전자 리그 구성 실패.");
            return;
        }

        // 중력 제거.
        var gravity = origin.GetComponentInChildren<GravityProvider>(true);
        if (gravity != null)
        {
            gravity.useGravity = false;
            gravity.enabled = false;
        }

        // 벽 통과: CC를 끄면 이동 프로바이더가 트랜스폼을 직접 옮긴다.
        var cc = origin.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // 머리(헤드셋) 벽 클램프 제거 — 비활성 CC에 cc.Move를 호출하지 않도록 함께 끈다.
        var headFollow = origin.GetComponent<CharacterControllerHeadFollow>();
        if (headFollow != null) headFollow.enabled = false;

        // 왼쪽 조이스틱 = 수평 이동. enableFly는 끄고(시선 기준 상하 비행 방지),
        // enableStrafe를 켜서 좌우(strafe)까지 확실히 동작하게 한다.
        var move = origin.GetComponentInChildren<ContinuousMoveProvider>(true);
        if (move != null)
        {
            move.enableFly = false;
            move.enableStrafe = true;
        }

        // 오른쪽 조이스틱 = 상하 비행. 전용 컴포넌트를 XR Origin에 부착(중복 방지).
        if (origin.GetComponent<SpectatorVerticalFly>() == null)
            origin.gameObject.AddComponent<SpectatorVerticalFly>();

        Debug.Log("[SpectatorRig] 관전자 리그 구성 완료 — 중력/충돌/머리 클램프 비활성, 좌:수평 우:상하 이동.");
    }
}
