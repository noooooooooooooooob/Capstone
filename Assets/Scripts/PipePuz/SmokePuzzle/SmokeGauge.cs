using UnityEngine;

namespace PipePuz.SmokePuzzle
{
    /// <summary>
    /// 반원 게이지. 새 동작:
    ///   · Pointer 는 더 이상 smoke 양으로 자동 이동하지 않는다.
    ///     대신 Valve(SuppressionWheel)의 회전(Normalized01)을 그대로 따라간다.
    ///     → 사용자가 밸브를 시계방향으로 돌리면 Pointer 도 시계방향, 반시계면 반시계.
    ///   · 빨간 영역은 "고정된 작은 타깃 호" 다(예전처럼 차오르는 fill 아님).
    ///     Pointer 가 이 영역 안에 들어오면 <see cref="PointerInRedZone"/> 가 true 가 되고,
    ///     PipeAllPuzzleController 가 이를 읽어 smoke 를 멈춘다(영역을 벗어나면 다시 발생).
    ///
    /// Pointer 각도 매핑: t∈[0,1] → angle = Lerp(180°, 0°, t)
    ///   t=0 → 180°(좌), t=0.5 → 90°(상), t=1 → 0°(우). t 가 커질수록 Pointer 는 시계방향.
    /// 빨간 호도 같은 t 공간에서 [center±width/2] 로 그려 Pointer 와 정확히 겹친다.
    /// </summary>
    public class SmokeGauge : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("연기 퍼즐 컨트롤러. 비워도 됨 — Valve 자동 연결과 디버그용으로만 참조.")]
        public PipeAllPuzzleController Controller;

        [Tooltip("Pointer 를 구동할 Valve. 비우면 Controller.Wheel 또는 씬에서 자동 검출.")]
        public SuppressionWheel Valve;

        [Tooltip("회전 포인터(화살표) 의 root. 자식 비주얼이 +X 방향으로 길게 뻗어 있어야 한다.")]
        public Transform Pointer;

        [Tooltip("빨간 타깃 호 mesh 를 들고 있는 MeshFilter(정면 사본). Awake 에 동적 Mesh 가 할당된다.")]
        public MeshFilter RedFillFilter;

        [Tooltip("빨간 타깃 호 mesh 의 뒷면 사본 MeshFilter. RedFillFilter 와 같은 동적 Mesh 를 공유한다.")]
        public MeshFilter RedFillFilterBack;

        [Header("Red target zone (고정된 작은 영역)")]
        [Range(0f, 1f)]
        [Tooltip("빨간 타깃 영역의 중심 위치(0=좌끝, 0.5=상단, 1=우끝). '끝까지'가 아닌 특정 지점.")]
        public float RedZoneCenter01 = 0.5f;

        [Range(0.01f, 0.5f)]
        [Tooltip("빨간 타깃 영역의 폭(전체 sweep 대비 비율). 작을수록 정밀 조준이 필요.")]
        public float RedZoneWidth01 = 0.12f;

        [Tooltip("켜면 밸브 회전과 Pointer 방향을 반대로 매핑. 실제 게이지가 뒤집혀 보이면 사용.")]
        public bool InvertPointer = false;

        [Header("Geometry")]
        [Tooltip("반원 반지름(m).")]
        public float Radius = 0.18f;

        [Tooltip("반원/호를 잘게 쪼개는 세그먼트 수. 클수록 부드럽다.")]
        public int Segments = 48;

        /// <summary>현재 Pointer 가 빨간 타깃 영역 안에 있는지. Controller 가 읽어 smoke 를 멈춘다.</summary>
        public bool PointerInRedZone { get; private set; }

        Mesh _redMesh;
        Vector3[] _verts;
        int[] _tris;
        float _builtCenter = -1f, _builtWidth = -1f;

        void Awake()
        {
            _redMesh = new Mesh { name = "SmokeGauge_RedZone" };
            _redMesh.MarkDynamic();
            if (RedFillFilter != null) RedFillFilter.sharedMesh = _redMesh;
            if (RedFillFilterBack != null) RedFillFilterBack.sharedMesh = _redMesh;
            AllocBuffers();

            // Valve 자동 연결: 명시 안 했으면 Controller.Wheel → 씬 검색 순으로.
            ResolveValve();

            BuildRedZoneMesh();
            UpdatePointer(ReadPointerT());
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

        // 로컬(싱글/권위) 경로: 매 프레임 Valve 를 읽어 갱신.
        // 네트워크 프록시 경로: SuppressionWheelNetworkSync 가 휠 각도를 주입한 직후
        //   Render() 에서 RefreshFromValve() 를 호출 → 휠과 정확히 같은 타이밍에 Pointer 갱신
        //   (Update 타이밍에 의존하지 않으므로 "휠은 도는데 Pointer 안 움직임" 문제 해소).
        void Update() => RefreshFromValve();

        /// <summary>
        /// 현재 Valve(SuppressionWheel) 회전값으로 Pointer 와 빨간영역 판정을 갱신한다.
        /// SmokeGauge.Update 와 SuppressionWheelNetworkSync.Render 양쪽에서 호출된다.
        /// </summary>
        public void RefreshFromValve()
        {
            // Valve 가 아직 없으면(스폰/초기화 순서) 지연 해석.
            if (Valve == null) ResolveValve();

            // 인스펙터에서 영역을 조정하면 다시 굽는다.
            if (!Mathf.Approximately(_builtCenter, RedZoneCenter01) ||
                !Mathf.Approximately(_builtWidth, RedZoneWidth01))
                BuildRedZoneMesh();

            float t = ReadPointerT();
            UpdatePointer(t);

            float half = RedZoneWidth01 * 0.5f;
            PointerInRedZone = Mathf.Abs(t - RedZoneCenter01) <= half;
        }

        void ResolveValve()
        {
            if (Valve == null && Controller != null) Valve = Controller.Wheel;
            if (Valve == null) Valve = FindFirstObjectByType<SuppressionWheel>();
        }

        /// <summary>Valve 회전을 Pointer t(0~1) 로 변환. InvertPointer 면 방향 반전.</summary>
        float ReadPointerT()
        {
            float raw = Valve != null ? Valve.Normalized01 : 0f;
            return InvertPointer ? 1f - raw : raw;
        }

        /// <summary>고정된 빨간 타깃 호를 [center±width/2] t 범위에 굽는다(한 번만, 파라미터 변경 시 재생성).</summary>
        void BuildRedZoneMesh()
        {
            if (_redMesh == null || _verts == null) return;

            float half = RedZoneWidth01 * 0.5f;
            float tStart = Mathf.Clamp01(RedZoneCenter01 - half);
            float tEnd   = Mathf.Clamp01(RedZoneCenter01 + half);

            // t → angle = Lerp(180°, 0°, t) (Pointer 와 동일 매핑).
            float aStart = Mathf.Lerp(180f, 0f, tStart);
            float aEnd   = Mathf.Lerp(180f, 0f, tEnd);

            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                float ang = Mathf.Lerp(aStart, aEnd, u) * Mathf.Deg2Rad;
                _verts[i + 1] = new Vector3(Mathf.Cos(ang) * Radius, Mathf.Sin(ang) * Radius, 0f);
            }
            _redMesh.Clear();
            _redMesh.vertices = _verts;
            _redMesh.triangles = _tris;
            _redMesh.RecalculateBounds();

            _builtCenter = RedZoneCenter01;
            _builtWidth = RedZoneWidth01;
        }

        void UpdatePointer(float t)
        {
            if (Pointer == null) return;
            // t=0 → 180°(좌), t=1 → 0°(우). t 증가 = 시계방향.
            float angle = Mathf.Lerp(180f, 0f, Mathf.Clamp01(t));
            Pointer.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
