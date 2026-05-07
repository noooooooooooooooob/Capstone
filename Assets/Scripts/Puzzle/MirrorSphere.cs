using Fusion;
using UnityEngine;

namespace Capstone.Puzzle
{
    /// <summary>
    /// 가상벽을 기준으로 한 쌍이 좌우대칭으로 움직이는 구체.
    ///
    /// 역할
    /// - <see cref="Side.B"/> (RadiatorB 쪽): 사용자가 XRGrabInteractable 로 잡고 이동시킬 수 있는 구체.
    ///   자기 위치를 가상벽의 B 측 반공간에 머무르도록 클램프하고 [Networked] 위치를 갱신.
    /// - <see cref="Side.A"/> (RadiatorA 쪽): 비상호작용. 매 프레임 B 의 [Networked] 위치를
    ///   가상벽으로 반사한 위치에 자기를 위치시킨다.
    ///
    /// 두 클라이언트 모두 같은 [Networked] BWorldPos 를 보므로 자동으로 일관됨.
    /// (B 의 NetworkObject 에 StateAuthority 가 있는 클라이언트가 권위 측이며, 그렇지 않은 쪽은
    ///  RPC 로 위임한다.)
    /// </summary>
    [DisallowMultipleComponent]
    public class MirrorSphere : NetworkBehaviour
    {
        public enum Side
        {
            A, // 미러 시각 전용 (사용자 조작 불가)
            B, // 사용자가 잡고 움직이는 쪽
        }

        [Header("미러 설정")]
        [SerializeField] Side side = Side.B;

        [Tooltip("가상벽 Transform. forward(파란 축)가 A→B 방향이라고 가정 (RadiatorMirror 와 동일).")]
        [SerializeField] Transform virtualWall;

        [Tooltip("같은 쌍을 이루는 반대편 MirrorSphere. A 측에서 B 의 NetworkedBWorldPos 를 직접 읽기 위해 사용.")]
        [SerializeField] MirrorSphere counterpart;

        [Header("이동 제약")]
        [Tooltip("벽 평면에 이 거리 이하로 가까워지지 못하도록 (m)")]
        [SerializeField] float minDistanceFromWall = 0.05f;

        // === 네트워크 동기화 ==============================================
        // B 쪽이 갱신하고 A 쪽이 읽는다. A 쪽의 NetworkedBWorldPos 는 사용 안 됨.
        [Networked] public Vector3 NetworkedBWorldPos { get; set; }
        // ==================================================================

        public Side WhichSide => side;

        public override void Spawned()
        {
            // 처음 들어왔을 때 B 의 위치를 즉시 권위에 반영
            if (side == Side.B && HasStateAuthority)
            {
                NetworkedBWorldPos = transform.position;
            }
            UpdateVisualPosition();
        }

        public override void Render()
        {
            UpdateVisualPosition();
        }

        // FixedUpdateNetwork 에서 입력 위치 처리 (시뮬레이션 측)
        public override void FixedUpdateNetwork()
        {
            if (side != Side.B) return;
            // B: 사용자가 transform.position 을 직접 바꿨다면(예: XRGrabInteractable),
            // 그 값을 클램프하고 권위 측에서 [Networked] 에 반영.
            Vector3 clamped = ClampToBSide(transform.position);
            transform.position = clamped;

            if (Object != null && Object.IsValid)
            {
                if (HasStateAuthority)
                    NetworkedBWorldPos = clamped;
                else
                    RPC_PushBPos(clamped);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_PushBPos(Vector3 worldPos)
        {
            NetworkedBWorldPos = worldPos;
        }

        // ---------------------------------------------------------------------
        // 매 프레임 시각 보간
        // ---------------------------------------------------------------------
        void UpdateVisualPosition()
        {
            if (virtualWall == null) return;

            Vector3 wallPos = virtualWall.position;
            Vector3 wallNormal = virtualWall.forward;
            if (wallNormal.sqrMagnitude < 1e-6f) return;
            wallNormal.Normalize();

            if (side == Side.B)
            {
                // 권위 측이 아닌 클라이언트(또는 일반 Render)에서는 [Networked] 값을 쓰는 게 맞다.
                // 단, XRGrab 으로 사용자가 직접 transform 을 끌고 있을 때는 transform.position 이 더 최신.
                if (Object != null && Object.IsValid && !HasStateAuthority)
                    transform.position = ClampToBSide(NetworkedBWorldPos);
                else
                    transform.position = ClampToBSide(transform.position);
            }
            else // Side.A
            {
                Vector3 source = (counterpart != null && counterpart.Object != null && counterpart.Object.IsValid)
                    ? counterpart.NetworkedBWorldPos
                    : (counterpart != null ? counterpart.transform.position : transform.position);
                transform.position = ReflectPoint(source, wallPos, wallNormal);
            }
        }

        // ---------------------------------------------------------------------
        // 헬퍼
        // ---------------------------------------------------------------------
        Vector3 ClampToBSide(Vector3 p)
        {
            if (virtualWall == null) return p;
            Vector3 wallPos = virtualWall.position;
            Vector3 n = virtualWall.forward;
            if (n.sqrMagnitude < 1e-6f) return p;
            n.Normalize();

            // 부호 있는 거리: B 측은 +n 방향이 양수
            float d = Vector3.Dot(p - wallPos, n);
            if (d < minDistanceFromWall)
            {
                p += (minDistanceFromWall - d) * n;
            }
            return p;
        }

        static Vector3 ReflectPoint(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
        {
            float d = Vector3.Dot(point - planePoint, planeNormal);
            return point - 2f * d * planeNormal;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (virtualWall == null) return;
            Gizmos.color = side == Side.B ? new Color(1f, 0.7f, 0.2f, 0.8f) : new Color(0.4f, 0.7f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);
            // 벽 가시화
            Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
            Gizmos.DrawLine(transform.position, virtualWall.position);
        }
#endif
    }
}
