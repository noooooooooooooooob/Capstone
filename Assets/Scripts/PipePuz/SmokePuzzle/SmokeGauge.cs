using UnityEngine;

namespace PipePuz.SmokePuzzle
{
    /// <summary>
    /// 반원 게이지. 좌측(-X) = 0, 우측(+X) = MaxSmoke 비율 1.
    /// 흰색 반원 배경 위에 빨간 sector mesh 가 좌측에서부터 비율만큼 채워지고,
    /// 화살표가 현재 비율 위치를 가리킨다.
    ///
    /// 빨간 fill mesh 는 매 프레임 verts 를 갱신한다. (segments=48 기준 가벼움)
    /// </summary>
    public class SmokeGauge : MonoBehaviour
    {
        [Header("Wiring")]
        public PipeAllPuzzleController Controller;

        [Tooltip("회전 포인터(화살표) 의 root. 자식 비주얼이 +X 방향으로 길게 뻗어 있어야 한다.")]
        public Transform Pointer;

        [Tooltip("빨간 fill mesh 를 들고 있는 MeshFilter (정면 사본). Awake 에 동적 Mesh 가 할당된다.")]
        public MeshFilter RedFillFilter;

        [Tooltip("빨간 fill mesh 의 뒷면 사본 MeshFilter. Background 보다 +Z 쪽에 두어 뒤에서도 보이게 한다. " +
                 "RedFillFilter 와 같은 동적 Mesh 를 공유한다.")]
        public MeshFilter RedFillFilterBack;

        [Header("Geometry")]
        [Tooltip("반원 반지름(m).")]
        public float Radius = 0.18f;

        [Tooltip("반원을 잘게 쪼개는 세그먼트 수. 클수록 부드럽다.")]
        public int Segments = 48;

        Mesh _redMesh;
        Vector3[] _verts;
        int[] _tris;

        void Awake()
        {
            _redMesh = new Mesh { name = "SmokeGauge_RedFill" };
            _redMesh.MarkDynamic();
            if (RedFillFilter != null) RedFillFilter.sharedMesh = _redMesh;
            if (RedFillFilterBack != null) RedFillFilterBack.sharedMesh = _redMesh;
            AllocBuffers();
            // 시작 시점 시각화 한 번 갱신 (Controller 가 아직 Awake 전이라도 안전하게 0 으로).
            UpdateMesh(0f);
            UpdatePointer(0f);
        }

        void AllocBuffers()
        {
            _verts = new Vector3[Segments + 2];
            _tris = new int[Segments * 3];
            _verts[0] = Vector3.zero;
            for (int i = 0; i < Segments; i++)
            {
                _tris[i * 3]     = 0;
                _tris[i * 3 + 1] = i + 1;
                _tris[i * 3 + 2] = i + 2;
            }
        }

        void Update()
        {
            float t = 0f;
            if (Controller != null && Controller.MaxSmoke > 0.0001f)
            {
                t = Mathf.Clamp01(Controller.CurrentSmoke / Controller.MaxSmoke);
            }
            UpdateMesh(t);
            UpdatePointer(t);
        }

        /// <summary>
        /// 반원: 좌(180°) → 우(0°) — 각 verts[i+1] = (cos·R, sin·R, 0).
        /// t=0 이면 빨간 영역 면적 0, t=1 이면 좌측 전체.
        /// 시각 안내: 우측이 위험(빨간)이지만 사용자는 보통 시계방향을 "차오름" 으로 본다.
        /// 여기서는 좌측부터 t 비율을 빨간색으로 칠하기로 결정 — 화살표가 좌측에서 우측으로 이동하면서 빨강 영역이 늘어남.
        /// (좌측이 0 = 안전, 우측이 1 = 빨강 가득 = 위험)
        /// Wait: 좌측 0 안전 이면 빨강은 우측에서 시작해서 좌측으로 채워져야 자연스럽다.
        ///       → 좌측 0 = 흰색, 우측 1 = 빨강. fill 영역 = [0°, t*180°] (우측 → 위쪽 → 좌측 순으로 채워짐)
        ///       → 화살표는 우측(0°) → 좌측(180°) 으로 이동.
        /// 즉 t=0 → 화살표 우측(0°), t=1 → 화살표 좌측(180°).
        /// 사용자 메시지: "연기가 나는 쪽은 빨간색" — 절댓값. 어느 쪽이 0인지는 명시 안 함.
        /// 시각적 직관: 화살표는 시계방향(우→상→좌) 으로 차오를 때 빨강이 따라옴. → 좌측 0, 우측 1 + 화살표가 우측이 가득.
        /// 둘 중 어느 게 더 자연스러운지 모호 — 일단 "좌측 0, 우측 1, fill 은 우측부터 t 만큼" 으로 간다.
        /// 이는 시계 게이지에서 "압력이 차오를 때 시계방향" 직관과 일치.
        /// fill 영역 = [180° - t*180°, 180°] (좌측이 0이고, 우측 가득 차면 t=1 → fill = [0°, 180°] = full)
        /// 화살표 = (1 - t) * 180° (t=0 일 때 180°(좌), t=1 일 때 0°(우))
        /// </summary>
        void UpdateMesh(float t)
        {
            if (_redMesh == null || _verts == null) return;

            float startDeg = 180f - t * 180f; // 좌측 fill 경계
            float endDeg = 180f;              // 좌측 끝
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                float ang = Mathf.Lerp(endDeg, startDeg, u) * Mathf.Deg2Rad;
                _verts[i + 1] = new Vector3(Mathf.Cos(ang) * Radius, Mathf.Sin(ang) * Radius, 0f);
            }
            _redMesh.vertices = _verts;
            _redMesh.triangles = _tris;
            _redMesh.RecalculateBounds();
        }

        void UpdatePointer(float t)
        {
            if (Pointer == null) return;
            // t=0 → angle=180° (좌), t=1 → angle=0° (우)
            // 사용자 직관 보정: t=0 일 때 우측, t=1 일 때 좌측을 원할 수도 있지만 위 주석의 결정을 따른다.
            // 좌측이 0, fill 은 좌측부터 채워짐 — 사용자가 인스펙터에서 invertPointer 토글 원하면 추가 가능.
            float angle = 180f - t * 180f;
            Pointer.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
