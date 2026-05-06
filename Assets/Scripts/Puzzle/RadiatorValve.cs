using Fusion;
using UnityEngine;

namespace Capstone.Puzzle
{
    /// <summary>
    /// 라디에이터 밸브의 네트워크 동기화 회전 상태.
    /// - ValveAngle: 0 = 완전 열림, fullCloseAngle = 완전 잠김 (시계방향 누적 각도)
    /// - 시계방향 회전 → ValveAngle 증가 → 잠금
    /// - 반시계방향 회전 → ValveAngle 감소 → 열림
    /// 권위(state authority)는 Photon Fusion 표준 규칙을 따른다.
    /// 호스트가 아닌 클라이언트가 회전을 주도할 때는 RPC로 호스트에 위임.
    /// </summary>
    [DisallowMultipleComponent]
    public class RadiatorValve : NetworkBehaviour
    {
        [Header("회전 대상")]
        [Tooltip("실제로 회전 애니메이션이 적용될 핸들(휠) Transform")]
        [SerializeField] Transform handleTransform;

        [Tooltip("핸들이 회전하는 로컬 축. 기본값 Z축은 파이프가 +Z 방향으로 뻗어 나오는 경우.")]
        [SerializeField] Vector3 rotationAxisLocal = new Vector3(0f, 0f, 1f);

        [Header("회전 한계")]
        [Tooltip("완전히 잠그는 데 필요한 누적 회전 각도(도). 720 = 두 바퀴.")]
        [SerializeField] float fullCloseAngle = 720f;

        [Tooltip("이 각도 이상이면 잠긴 것으로 판정")]
        [SerializeField] float closedThreshold = 700f;

        [Tooltip("이 각도 이하이면 열린 것으로 판정 (히스테리시스용)")]
        [SerializeField] float openedThreshold = 30f;

        // === 네트워크 동기화 상태 =============================================
        [Networked] public float ValveAngle { get; set; }
        [Networked] public bool IsClosed { get; set; }
        // =====================================================================

        /// <summary>완전 잠금까지 필요한 누적 회전 각도(도). 외부 비주얼이 진행도 계산에 사용.</summary>
        public float FullCloseAngle => fullCloseAngle;

        /// <summary>0(완전 열림) ~ 1(완전 잠금) 정규화 진행도. 비주얼/오디오 효과 연동용.</summary>
        public float NormalizedClose =>
            fullCloseAngle <= Mathf.Epsilon ? 0f : Mathf.Clamp01(ValveAngle / fullCloseAngle);

        public override void Spawned()
        {
            ApplyVisual();
        }

        public override void Render()
        {
            // 매 프레임 보간된 시각적 회전 적용 (모든 클라이언트에서)
            ApplyVisual();
        }

        /// <summary>
        /// 외부 인터랙션 코드(예: <see cref="ValveRotationGrab"/>)에서 호출.
        /// 양수 = 시계방향(잠금), 음수 = 반시계방향(열림).
        /// </summary>
        public void ApplyRotationDelta(float deltaDegrees)
        {
            if (Object == null || !Object.IsValid) return;

            if (HasStateAuthority)
            {
                ApplyDeltaServer(deltaDegrees);
            }
            else
            {
                RPC_RequestRotation(deltaDegrees);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_RequestRotation(float deltaDegrees)
        {
            ApplyDeltaServer(deltaDegrees);
        }

        void ApplyDeltaServer(float deltaDegrees)
        {
            float newAngle = Mathf.Clamp(ValveAngle + deltaDegrees, 0f, fullCloseAngle);
            ValveAngle = newAngle;

            // 히스테리시스로 떨림 방지
            if (newAngle >= closedThreshold) IsClosed = true;
            else if (newAngle <= openedThreshold) IsClosed = false;
        }

        void ApplyVisual()
        {
            if (handleTransform == null) return;
            // ValveAngle은 "잠긴 정도"이고 시계방향이 양수가 되는 규약이므로,
            // Unity 기본 회전(반시계가 양수)을 맞추기 위해 부호를 뒤집는다.
            handleTransform.localRotation = Quaternion.AngleAxis(-ValveAngle, rotationAxisLocal.normalized);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (handleTransform == null) return;
            Gizmos.color = Color.cyan;
            Vector3 origin = handleTransform.position;
            Vector3 axis = handleTransform.TransformDirection(rotationAxisLocal.normalized);
            Gizmos.DrawLine(origin, origin + axis * 0.3f);
        }
#endif
    }
}
