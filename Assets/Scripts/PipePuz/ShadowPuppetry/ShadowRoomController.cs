using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.ShadowPuppetry
{
    /// <summary>
    /// Shadow Puppetry 퍼즐의 매 프레임 그림자 계산 + 플랫폼/시각 갱신을 담당.
    ///
    /// 알고리즘 (선형, 단순):
    ///   for each (caster, platform):
    ///     for each of caster 의 AABB 8 코너 C:
    ///       d = C - lightPos
    ///       t = ((wallAnchor - lightPos) · wallNormal) / (d · wallNormal)
    ///       if t > 1 (벽이 캐스터 뒤): hit = lightPos + t*d
    ///         hit 을 벽 로컬 (right, up) 평면에 투영해 (lx, ly) 누적
    ///     valid 코너가 4 개 이상이면 bounding rectangle 형성
    ///     EMA smoothing → platform.Apply(...) 호출
    ///
    /// 스위치가 눌리면 OnSolved 발행.
    /// </summary>
    public class ShadowRoomController : MonoBehaviour
    {
        [Header("Refs")]
        public ShadowFlashlight Flashlight;

        [Tooltip("벽 surface 의 anchor. Transform.position 은 벽 표면 위의 한 점, " +
                 "transform.forward 는 벽 표면에서 방 쪽으로 향하는 법선이어야 한다.")]
        public Transform WallSurface;

        public ShadowCaster[] Casters;
        public ShadowPlatform[] Platforms;
        public ShadowSwitch Switch;

        [Header("Geometry")]
        [Tooltip("플랫폼이 벽에서 방 쪽으로 튀어나오는 깊이(m).")]
        public float PlatformDepth = 0.45f;

        [Tooltip("플랫폼의 수직 두께(m). 윗면이 그림자 윗변과 정확히 같은 Y 가 되도록 내부에서 보정.")]
        public float PlatformThickness = 0.06f;

        [Tooltip("벽 surface 보다 다크 quad 를 살짝 띄울 거리(z-fight 방지).")]
        public float WallSurfaceOffset = 0.005f;

        [Header("Smoothing")]
        [Range(0f, 0.95f)]
        [Tooltip("0 = 즉시 갱신(떨림 있음), 0.95 = 매우 부드러움(반응 느림). EMA 의 historical weight.")]
        public float Smoothing = 0.35f;

        [Header("Events")]
        public UnityEvent OnSolved;

        // Smoothing 상태 (각 plataform 마다 EMA 누적값).
        float[] _sMinX, _sMaxX, _sMinY, _sMaxY;
        bool[] _sInit;
        bool _solved;

        void Start()
        {
            int n = Mathf.Min(Casters != null ? Casters.Length : 0,
                              Platforms != null ? Platforms.Length : 0);
            _sMinX = new float[n];
            _sMaxX = new float[n];
            _sMinY = new float[n];
            _sMaxY = new float[n];
            _sInit = new bool[n];

            if (Switch != null) Switch.OnPressed.AddListener(HandleSolved);
        }

        void OnDestroy()
        {
            if (Switch != null) Switch.OnPressed.RemoveListener(HandleSolved);
        }

        void HandleSolved()
        {
            if (_solved) return;
            _solved = true;
            OnSolved?.Invoke();
            Debug.Log("[ShadowRoom] Solved!");
        }

        void Update()
        {
            if (Flashlight == null || WallSurface == null) return;
            if (Casters == null || Platforms == null) return;

            Vector3 lightPos = Flashlight.LightPosition;
            Vector3 wallAnchor = WallSurface.position;
            Vector3 wallNormal = WallSurface.forward.normalized;
            Vector3 wallRight = WallSurface.right.normalized;
            Vector3 wallUp = WallSurface.up.normalized;

            int n = Mathf.Min(Casters.Length, Platforms.Length);
            for (int i = 0; i < n; i++)
            {
                var caster = Casters[i];
                var platform = Platforms[i];
                if (caster == null || platform == null) continue;

                if (!TryComputeShadowRect(caster, lightPos, wallAnchor, wallNormal, wallRight, wallUp,
                                         out float minX, out float maxX, out float minY, out float maxY))
                {
                    platform.SetActive(false);
                    _sInit[i] = false;
                    continue;
                }

                if (_sInit[i])
                {
                    _sMinX[i] = Mathf.Lerp(minX, _sMinX[i], Smoothing);
                    _sMaxX[i] = Mathf.Lerp(maxX, _sMaxX[i], Smoothing);
                    _sMinY[i] = Mathf.Lerp(minY, _sMinY[i], Smoothing);
                    _sMaxY[i] = Mathf.Lerp(maxY, _sMaxY[i], Smoothing);
                }
                else
                {
                    _sMinX[i] = minX; _sMaxX[i] = maxX;
                    _sMinY[i] = minY; _sMaxY[i] = maxY;
                    _sInit[i] = true;
                }

                platform.SetActive(true);
                ApplyToPlatform(platform, _sMinX[i], _sMaxX[i], _sMinY[i], _sMaxY[i],
                                wallAnchor, wallNormal, wallRight, wallUp);
            }
        }

        void ApplyToPlatform(ShadowPlatform platform, float minX, float maxX, float minY, float maxY,
                             Vector3 wallAnchor, Vector3 wallNormal, Vector3 wallRight, Vector3 wallUp)
        {
            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;
            float width = Mathf.Max(0.05f, maxX - minX);
            float height = Mathf.Max(0.05f, maxY - minY);
            float topY = maxY;

            // 플랫폼: 윗면이 그림자 윗변과 일치하도록 Y 보정. 벽에서 방 쪽으로 PlatformDepth/2 만큼 이동.
            Vector3 platformPos = wallAnchor
                + centerX * wallRight
                + (topY - PlatformThickness * 0.5f) * wallUp
                + (PlatformDepth * 0.5f) * wallNormal;

            // platform 의 +Z(forward) = wallNormal (방 쪽), +Y(up) = wallUp.
            Quaternion rot = Quaternion.LookRotation(wallNormal, wallUp);
            Vector3 platformSize = new Vector3(width, PlatformThickness, PlatformDepth);

            // 다크 quad: 그림자 사각형 전체 (centerY, height 사용), 벽 surface 보다 살짝 앞.
            Vector3 visualPos = wallAnchor
                + centerX * wallRight
                + centerY * wallUp
                + WallSurfaceOffset * wallNormal;
            Vector3 visualSize = new Vector3(width, height, 1f);

            platform.Apply(platformPos, rot, platformSize, visualPos, rot, visualSize);
        }

        bool TryComputeShadowRect(ShadowCaster caster, Vector3 lightPos,
                                  Vector3 wallAnchor, Vector3 wallNormal, Vector3 wallRight, Vector3 wallUp,
                                  out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = float.MaxValue; maxX = float.MinValue;
            minY = float.MaxValue; maxY = float.MinValue;

            var corners = caster.GetBoundsCorners();
            int valid = 0;
            float toWall = Vector3.Dot(wallAnchor - lightPos, wallNormal);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 d = corners[i] - lightPos;
                float denom = Vector3.Dot(d, wallNormal);
                if (Mathf.Abs(denom) < 1e-6f) continue; // ray parallel to wall
                float t = toWall / denom;
                if (t <= 1.0f) continue; // 벽이 캐스터 앞 또는 광원 뒤 → 그림자 안 만들어짐.

                Vector3 hit = lightPos + t * d;
                Vector3 rel = hit - wallAnchor;
                float lx = Vector3.Dot(rel, wallRight);
                float ly = Vector3.Dot(rel, wallUp);
                if (lx < minX) minX = lx;
                if (lx > maxX) maxX = lx;
                if (ly < minY) minY = ly;
                if (ly > maxY) maxY = ly;
                valid++;
            }

            return valid >= 4;
        }
    }
}
