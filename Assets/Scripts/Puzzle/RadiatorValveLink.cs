using UnityEngine;

namespace Capstone.Puzzle
{
    /// <summary>
    /// 하나의 <see cref="RadiatorValve"/> 네트워크 상태를 두 개 이상의 시각적 핸들에 동시에 적용한다.
    /// (RadiatorA의 ValveHandle 과 RadiatorB의 ValveHandle 이 같은 ValveAngle 을 표시하도록.)
    ///
    /// 셋업
    /// ─ 마스터(예: RadiatorA): 기존 RadiatorValve 컴포넌트가 자기 ValveHandleA 를 회전시킴
    /// ─ 종속(예: RadiatorB):  이 컴포넌트만 추가, master = RadiatorA의 RadiatorValve, followerHandle = ValveHandleB
    ///
    /// 그랩 입력
    /// ─ 양쪽 ValveHandle 모두에 XRControllerValveGrabber + ValveRotationGrab 을 부착하되,
    ///   두 ValveRotationGrab 의 valve 필드는 모두 동일한 마스터 RadiatorValve 를 가리킨다.
    ///   (양쪽 어디에서 돌려도 마스터의 ApplyRotationDelta 가 호출되므로 자동으로 양쪽이 같이 돈다.)
    ///
    /// 비고
    /// ─ ValveAngle 은 [Networked] 이므로 네트워크 동기화는 자동.
    /// ─ rotationAxisLocal 은 종속 핸들의 로컬 축 — 좌우 대칭이라면 마스터와 부호가 반대일 수 있다.
    ///   아래 옵션의 invertAxis 로 손쉽게 뒤집을 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RadiatorValveLink : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("네트워크 상태(ValveAngle)를 가진 마스터 RadiatorValve")]
        [SerializeField] RadiatorValve master;

        [Tooltip("이 컴포넌트가 회전을 적용할 종속 핸들 Transform (예: RadiatorB의 ValveHandle)")]
        [SerializeField] Transform followerHandle;

        [Header("회전 축")]
        [Tooltip("종속 핸들의 로컬 회전 축. 마스터와 같으면 그대로, 거울 대칭이면 invertAxis 로 부호 반전.")]
        [SerializeField] Vector3 rotationAxisLocal = new Vector3(0f, 0f, 1f);

        [Tooltip("켜면 회전 방향을 반대로 적용 (좌우 대칭 미러링 시 유용).")]
        [SerializeField] bool invertAxis = false;

        void Reset()
        {
            if (master == null) master = GetComponentInParent<RadiatorValve>();
        }

        void LateUpdate()
        {
            if (master == null || followerHandle == null) return;

            float angle = invertAxis ? -master.ValveAngle : master.ValveAngle;
            // RadiatorValve.ApplyVisual 과 동일한 부호 규약: 잠금이 + 인 ValveAngle을 -로 변환.
            followerHandle.localRotation = Quaternion.AngleAxis(-angle, rotationAxisLocal.normalized);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (followerHandle == null) return;
            Gizmos.color = Color.yellow;
            Vector3 origin = followerHandle.position;
            Vector3 axis = followerHandle.TransformDirection(rotationAxisLocal.normalized);
            Gizmos.DrawLine(origin, origin + axis * 0.3f);
        }
#endif
    }
}
