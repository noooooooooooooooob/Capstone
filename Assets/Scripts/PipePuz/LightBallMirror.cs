using UnityEngine;

namespace PipePuz
{
    /// <summary>
    /// 자기 transform 의 월드 위치를 <see cref="Plane"/> 기준으로 반사시켜 <see cref="Mirror"/> 로 복사한다.
    /// 보통 LightBallB 에 붙여 두고 Mirror 에 LightBallA 를 연결한다 — 사용자가 B 를 잡아 옮기면
    /// A 는 자동으로 Plane 너머의 대칭 위치로 따라간다.
    ///
    /// LateUpdate 에서 동작 — 잡기·물리 갱신이 끝난 뒤 마지막에 보정한다.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class LightBallMirror : MonoBehaviour
    {
        public enum PlaneAxis
        {
            Right,
            Up,
            Forward
        }

        [Tooltip("기준이 되는 Plane Transform. 이 Plane 의 법선 축을 따라 반사한다.")]
        public Transform Plane;

        [Tooltip("반사 결과를 적용할 대상 Transform (보통 LightBallA).")]
        public Transform Mirror;

        [Tooltip("Plane 의 법선이 Plane Transform 의 어느 축인지. " +
                 "Unity 의 Plane 프리미티브 메쉬는 로컬 Y(=Up) 가 법선이지만, " +
                 "현재 Radiator/Plane 은 Z 축으로 90° 회전돼 있어 world 기준 Up 벡터가 곧 법선이다.")]
        public PlaneAxis NormalAxis = PlaneAxis.Up;

        [Tooltip("회전(Quaternion) 도 거울 대칭으로 갱신할지. 위치만으로 충분하면 끄면 된다.")]
        public bool MirrorRotation = false;

        Vector3 GetPlaneNormal()
        {
            if (Plane == null) return Vector3.right;
            switch (NormalAxis)
            {
                case PlaneAxis.Right: return Plane.right;
                case PlaneAxis.Forward: return Plane.forward;
                default: return Plane.up;
            }
        }

        void LateUpdate()
        {
            if (Plane == null || Mirror == null) return;

            Vector3 sourcePos = transform.position;
            Vector3 planePos = Plane.position;
            Vector3 normal = GetPlaneNormal();
            float n2 = normal.sqrMagnitude;
            if (n2 < 1e-8f) return;
            normal /= Mathf.Sqrt(n2);

            // 반사: source - 2*((source - planePos)·normal)*normal
            Vector3 rel = sourcePos - planePos;
            float d = Vector3.Dot(rel, normal);
            Vector3 mirroredPos = sourcePos - 2f * d * normal;

            Mirror.position = mirroredPos;

            if (MirrorRotation)
            {
                // 거울 대칭 회전 — 법선 축에 대해 좌우반전.
                // q_mirror = R_n * q * R_n^-1, 여기서 R_n 은 normal 평면에 대한 reflection.
                // Quaternion 으로는 직접 표현이 어려우므로 forward / up 을 반전시켜 LookRotation 으로 재구성.
                Vector3 srcForward = transform.forward;
                Vector3 srcUp = transform.up;
                Vector3 mForward = Vector3.Reflect(srcForward, normal);
                Vector3 mUp = Vector3.Reflect(srcUp, normal);
                if (mForward.sqrMagnitude > 1e-8f && mUp.sqrMagnitude > 1e-8f)
                {
                    Mirror.rotation = Quaternion.LookRotation(mForward, mUp);
                }
            }
        }
    }
}
