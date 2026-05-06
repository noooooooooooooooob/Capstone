using UnityEngine;

namespace Capstone.Puzzle
{
    /// <summary>
    /// 가상의 평면(벽) 한 쪽에 있는 "원본" 라디에이터 트리를
    /// 반대편의 "거울" 라디에이터 트리로 그대로 좌우반전시킨다.
    ///
    /// 주요 용도
    /// 1) 에디터에서 한 번만 적용해서 RadiatorB를 RadiatorA의 거울상으로 만들고 싶을 때
    /// 2) 런타임에도 계속 동기화시켜 RadiatorA 쪽 위치/회전이 바뀌면 RadiatorB가 자동으로 따라가게 할 때
    ///
    /// 동작 원리
    /// - <see cref="virtualWall"/>의 forward 벡터를 평면 노멀로 사용 (Plane Y/Z 가능)
    /// - <see cref="sourceRoot"/> 트리의 모든 자식 Transform 위치를 평면 기준으로 반사
    /// - 회전도 반사된 좌표계에 맞도록 변환 (Reflection Quaternion)
    /// - 자식 이름이 일치하지 않으면 무시 (즉, 미러 대상 트리는 동일한 자식 구조를 가져야 한다)
    ///
    /// 사용 절차
    /// 1) 빈 GameObject "VirtualWall"을 만들고 RadiatorA와 RadiatorB 사이 가운데에 배치
    ///    (벽 면이 두 라디에이터 사이를 가로지르도록 회전 — forward가 A→B 방향이면 OK)
    /// 2) RadiatorA / RadiatorB가 동일 자식 구조를 갖도록 일단 한쪽을 복제해서 다른 쪽에 붙여둔다
    /// 3) 원본 (예: RadiatorA)에 이 컴포넌트를 추가하고 sourceRoot, mirrorRoot, virtualWall 셋팅
    /// 4) Inspector의 "Apply Mirror Now" 버튼으로 한 번만 적용 (LiveUpdate 끄기)
    ///    또는 LiveUpdate를 켜두면 매 프레임 따라간다
    /// </summary>
    [DisallowMultipleComponent]
    public class RadiatorMirror : MonoBehaviour
    {
        [Header("미러 설정")]
        [Tooltip("원본 라디에이터 루트(예: RadiatorA Transform)")]
        [SerializeField] Transform sourceRoot;

        [Tooltip("거울 라디에이터 루트(예: RadiatorB Transform). sourceRoot와 동일한 자식 구조여야 한다.")]
        [SerializeField] Transform mirrorRoot;

        [Tooltip("가상벽 Transform. forward(파란색 축)가 평면의 노멀로 사용된다.")]
        [SerializeField] Transform virtualWall;

        [Header("옵션")]
        [Tooltip("켜면 매 LateUpdate 마다 거울상태로 다시 맞춘다. 끄면 한 번만 적용됨.")]
        [SerializeField] bool liveUpdate = false;

        [Tooltip("스케일도 미러링한다(평면 노멀 축 부호 반전). 보통은 끔.")]
        [SerializeField] bool mirrorScale = false;

        [Tooltip("자식 이름의 좌/우 키워드를 자동으로 바꿔서 매칭한다. (예: '_L' ↔ '_R', 'Left' ↔ 'Right')")]
        [SerializeField] bool swapLeftRightNames = false;

        void Reset()
        {
            // 부착된 GameObject 자신을 sourceRoot로 기본 추정
            if (sourceRoot == null) sourceRoot = transform;
        }

        void LateUpdate()
        {
            if (!liveUpdate) return;
            ApplyMirror();
        }

        /// <summary>인스펙터에서 우클릭 → "Apply Mirror Now" 로도 호출 가능.</summary>
        [ContextMenu("Apply Mirror Now")]
        public void ApplyMirror()
        {
            if (sourceRoot == null || mirrorRoot == null || virtualWall == null)
            {
                Debug.LogWarning("[RadiatorMirror] sourceRoot / mirrorRoot / virtualWall 중 비어있는 항목이 있습니다.", this);
                return;
            }

            Vector3 planePoint = virtualWall.position;
            Vector3 planeNormal = virtualWall.forward.normalized;

            // 1) 루트 자체의 미러
            ApplyToTransform(sourceRoot, mirrorRoot, planePoint, planeNormal);

            // 2) 자식 재귀
            MirrorChildrenRecursive(sourceRoot, mirrorRoot, planePoint, planeNormal);
        }

        void MirrorChildrenRecursive(Transform src, Transform dst, Vector3 planePoint, Vector3 planeNormal)
        {
            for (int i = 0; i < src.childCount; i++)
            {
                Transform sChild = src.GetChild(i);
                Transform dChild = FindMatchingChild(dst, sChild.name);
                if (dChild == null) continue;

                ApplyToTransform(sChild, dChild, planePoint, planeNormal);
                MirrorChildrenRecursive(sChild, dChild, planePoint, planeNormal);
            }
        }

        Transform FindMatchingChild(Transform parent, string sourceName)
        {
            // 1) 동일 이름
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == sourceName) return parent.GetChild(i);
            }

            if (!swapLeftRightNames) return null;

            // 2) 좌/우 키워드 스왑 후 검색
            string swapped = SwapLeftRightTokens(sourceName);
            if (swapped == sourceName) return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == swapped) return parent.GetChild(i);
            }
            return null;
        }

        static string SwapLeftRightTokens(string s)
        {
            // 단순 토큰 스왑 — 필요시 케이스 추가
            string r = s;
            r = ReplaceWord(r, "Left", "Right");
            r = ReplaceWord(r, "left", "right");
            r = r.Replace("_L", "@@L@@").Replace("_R", "_L").Replace("@@L@@", "_R");
            return r;
        }

        static string ReplaceWord(string source, string a, string b)
        {
            // 두 방향 동시 스왑
            if (source.Contains(a) && !source.Contains(b)) return source.Replace(a, b);
            if (source.Contains(b) && !source.Contains(a)) return source.Replace(b, a);
            return source;
        }

        // ---------------------------------------------------------------------
        // 한 쌍의 Transform에 대해 위치/회전을 평면 반사로 적용
        // ---------------------------------------------------------------------
        void ApplyToTransform(Transform src, Transform dst, Vector3 planePoint, Vector3 planeNormal)
        {
            // 평면 반사된 월드 위치
            Vector3 reflectedPos = ReflectPoint(src.position, planePoint, planeNormal);

            // 평면 반사된 월드 회전 (정규 축 미러)
            Quaternion reflectedRot = ReflectRotation(src.rotation, planeNormal);

            dst.SetPositionAndRotation(reflectedPos, reflectedRot);

            if (mirrorScale)
            {
                // 부모 좌표계에서 평면 노멀 방향 축의 부호만 뒤집는다 (대략적). 비균등 스케일이 생길 수 있음.
                Vector3 ls = src.localScale;
                dst.localScale = new Vector3(-ls.x, ls.y, ls.z);
            }
            else
            {
                dst.localScale = src.localScale;
            }
        }

        static Vector3 ReflectPoint(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
        {
            float d = Vector3.Dot(point - planePoint, planeNormal);
            return point - 2f * d * planeNormal;
        }

        static Quaternion ReflectRotation(Quaternion rot, Vector3 planeNormal)
        {
            // 회전 행렬 R에 대해 거울 회전: M*R*M, 단 M = I - 2 n n^T (반사행렬)
            // 쿼터니언 표현: 각 기저축을 반사하고 다시 LookRotation으로 합성
            Vector3 fwd = ReflectDirection(rot * Vector3.forward, planeNormal);
            Vector3 up = ReflectDirection(rot * Vector3.up, planeNormal);
            // 반사 후엔 좌수계가 되므로 LookRotation에서 다시 정상 우수계로 보정
            return Quaternion.LookRotation(fwd, up);
        }

        static Vector3 ReflectDirection(Vector3 dir, Vector3 planeNormal)
        {
            return dir - 2f * Vector3.Dot(dir, planeNormal) * planeNormal;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (virtualWall == null) return;
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.4f);
            Matrix4x4 m = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(virtualWall.position, virtualWall.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, new Vector3(4f, 4f, 0.01f));
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.3f);
            Gizmos.matrix = m;
        }
#endif
    }
}
