using Fusion;
using UnityEngine;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// RoomCliff (Stage1 Skin) 의 거울 발판(Platform_Mirror1~4) 위치를 매 세션마다 랜덤화한다.
    /// (Fusion Shared Mode)
    ///
    /// 동작:
    ///   · 권위(StateAuthority)가 스폰 시 0 이 아닌 <see cref="LayoutSeed"/> 를 한 번 굴린다.
    ///   · seed 는 [Networked] 라 모든 피어(늦게 합류한 피어 포함)에 동일하게 복제된다.
    ///   · 모든 피어가 동일 seed → 동일 결정적(deterministic) PRNG → 동일 좌표를 계산해 적용.
    ///     좌표 자체를 전송하지 않고 seed 만 전송하므로 대역폭이 거의 들지 않고, 양쪽 화면이 완벽히 일치한다.
    ///
    /// 무엇을 바꾸나:
    ///   · 각 발판의 LOCAL x / z 만 랜덤화. y 는 그대로 둔다(공중 발판 높이 유지).
    ///   · 거울 stand·Dock·Visual 콜라이더는 모두 발판의 자식 → 발판이 움직이면 함께 따라감.
    ///     따라서 라이트빔 퍼즐(런타임 재계산)·절벽 리스폰 dock 등 기존 기능은 그대로 유지된다.
    ///
    /// 제약:
    ///   · 모든 발판은 서로 <see cref="minSpacing"/> 이상 떨어진다(카펫 없이 점프로 못 건너가도록).
    ///   · <see cref="avoidPoints"/>(입구 발판·리시버 등 고정물) 로부터도 <see cref="avoidSpacing"/> 이상 유지.
    ///   · 좌표는 챔버 내부 사각형 [<see cref="areaMin"/>, <see cref="areaMax"/>] (발판 부모 LOCAL 공간) 안.
    ///
    /// 배치: 다른 NetworkObject 의 자식이 되지 않는 독립 루트 GameObject 에 둔다(중첩 NetworkObject 금지).
    /// 요구: 같은 GameObject 에 NetworkObject. 셋업 툴(Tools/Network/Stage3/4)이 생성/할당한다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class CliffPlatformRandomizer : NetworkBehaviour
    {
        [Header("Targets — 랜덤화할 거울 발판 (Platform_Mirror1~4)")]
        [Tooltip("위치를 랜덤화할 발판들. 셋업 툴이 자동 할당. 비어 있으면 동작 안 함.")]
        public Transform[] mirrorPlatforms;

        [Header("Area (발판 부모 LOCAL 공간 기준, X/Z)")]
        [Tooltip("랜덤 영역 최소 (x, z). RoomCliff 좌측 챔버 내부.")]
        public Vector2 areaMin = new Vector2(-20f, 5.5f);

        [Tooltip("랜덤 영역 최대 (x, z). 이미터/벽과 여유를 둔 챔버 내부.")]
        public Vector2 areaMax = new Vector2(-2.5f, 16.5f);

        [Header("Spacing")]
        [Tooltip("발판끼리 최소 거리(m). 점프로 못 건너가도록 충분히 크게.")]
        public float minSpacing = 5f;

        [Tooltip("고정물(avoidPoints)로부터 최소 거리(m).")]
        public float avoidSpacing = 3.5f;

        [Tooltip("겹침을 피하기 위해 거리 검사를 할 고정 지점들 (x, z). 예: 입구 발판, 리시버.")]
        public Vector2[] avoidPoints =
        {
            new Vector2(-5.707f, 4.65f), // Platform_Entry
            new Vector2(-10f,    4.3f),  // Receiver
        };

        [Header("Tuning")]
        [Tooltip("발판 하나를 배치하기 위해 시도하는 최대 횟수. 실패 시 간격을 점진적으로 완화.")]
        public int maxAttemptsPerPlatform = 250;

        [Tooltip("진단 로그.")]
        public bool verboseLog = false;

        /// <summary>0 = 아직 미정. 권위가 굴려서 모든 피어로 복제 → 동일 레이아웃을 보장.</summary>
        [Networked, OnChangedRender(nameof(OnSeedChanged))]
        public int LayoutSeed { get; set; }

        public override void Spawned()
        {
            // 권위가 최초 1회 seed 를 굴린다(0 은 '미정' 의미라 피한다).
            if (HasStateAuthority && LayoutSeed == 0)
            {
                int s = unchecked((int)(System.DateTime.UtcNow.Ticks ^ ((long)GetInstanceID() << 17)));
                if (s == 0) s = 1;
                LayoutSeed = s;
            }

            // 늦게 합류한 피어(이미 seed 가 복제돼 있어 OnChanged 가 안 뜸) 와
            // 권위 자신(방금 굴린 값) 모두 여기서 즉시 적용.
            if (LayoutSeed != 0) ApplyLayout(LayoutSeed);
        }

        // seed 가 네트워크로 갱신될 때(프록시 측) 호출.
        void OnSeedChanged()
        {
            if (LayoutSeed != 0) ApplyLayout(LayoutSeed);
        }

        /// <summary>에디터/디버그용: 다음 레이아웃을 강제로 다시 굴린다(권위에서만 의미 있음).</summary>
        [ContextMenu("Re-roll Layout (authority)")]
        public void ReRoll()
        {
            if (Object == null || !Object.IsValid || !HasStateAuthority)
            {
                Debug.LogWarning("[CliffRandomizer] ReRoll 은 StateAuthority + 스폰 후에만 가능.", this);
                return;
            }
            int s;
            do { s = Random.Range(int.MinValue, int.MaxValue); } while (s == 0 || s == LayoutSeed);
            LayoutSeed = s;
            ApplyLayout(s); // 권위 로컬도 즉시 반영.
        }

        void ApplyLayout(int seed)
        {
            if (mirrorPlatforms == null || mirrorPlatforms.Length == 0) return;

            var rng = new DetRng((uint)seed);
            int n = mirrorPlatforms.Length;
            var chosen = new Vector2[n];

            // 간격 완화 단계: 모든 발판을 둘 수 없으면 minSpacing 을 조금씩 줄여 재시도(무한 루프 방지).
            float spacing = Mathf.Max(0f, minSpacing);
            float avoid = Mathf.Max(0f, avoidSpacing);

            for (int relax = 0; relax < 12; relax++)
            {
                bool ok = true;
                var localRng = rng; // 각 완화 단계에서 동일 seed 시퀀스로 재시작 → 결정적.
                for (int i = 0; i < n; i++)
                {
                    if (mirrorPlatforms[i] == null) { chosen[i] = Vector2.zero; continue; }
                    if (!TryPlace(ref localRng, chosen, i, spacing, avoid, out chosen[i]))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    rng = localRng;
                    break;
                }
                // 완화: 간격 10% 축소.
                spacing *= 0.9f;
                avoid *= 0.9f;
                if (verboseLog)
                    Debug.Log($"[CliffRandomizer] 배치 실패 → 간격 완화 (spacing={spacing:F2}).", this);
            }

            for (int i = 0; i < n; i++)
            {
                var t = mirrorPlatforms[i];
                if (t == null) continue;
                Vector3 lp = t.localPosition;
                lp.x = chosen[i].x;
                lp.z = chosen[i].y; // Vector2.y == z
                t.localPosition = lp;          // y 는 건드리지 않음.
            }

            if (verboseLog)
            {
                var sb = new System.Text.StringBuilder($"[CliffRandomizer] seed={seed} 적용:\n");
                for (int i = 0; i < n; i++)
                    if (mirrorPlatforms[i] != null)
                        sb.AppendLine($"   {mirrorPlatforms[i].name} → ({chosen[i].x:F2}, y={mirrorPlatforms[i].localPosition.y:F2}, {chosen[i].y:F2})");
                Debug.Log(sb.ToString(), this);
            }
        }

        bool TryPlace(ref DetRng rng, Vector2[] chosen, int index, float spacing, float avoid, out Vector2 result)
        {
            float minX = Mathf.Min(areaMin.x, areaMax.x);
            float maxX = Mathf.Max(areaMin.x, areaMax.x);
            float minZ = Mathf.Min(areaMin.y, areaMax.y);
            float maxZ = Mathf.Max(areaMin.y, areaMax.y);

            for (int attempt = 0; attempt < maxAttemptsPerPlatform; attempt++)
            {
                var p = new Vector2(
                    Mathf.Lerp(minX, maxX, rng.NextFloat()),
                    Mathf.Lerp(minZ, maxZ, rng.NextFloat()));

                bool good = true;

                // 이미 배치된 발판들과의 간격.
                for (int j = 0; j < index; j++)
                {
                    if (mirrorPlatforms[j] == null) continue;
                    if ((chosen[j] - p).sqrMagnitude < spacing * spacing) { good = false; break; }
                }
                // 고정물과의 간격.
                if (good && avoidPoints != null)
                {
                    for (int k = 0; k < avoidPoints.Length; k++)
                        if ((avoidPoints[k] - p).sqrMagnitude < avoid * avoid) { good = false; break; }
                }

                if (good) { result = p; return true; }
            }

            result = Vector2.zero;
            return false;
        }

        /// <summary>
        /// 결정적 PRNG (xorshift32). System.Random 의 런타임(Mono/IL2CPP) 간 구현 차이에 의존하지 않도록
        /// 직접 구현 — 같은 seed 면 어느 기기에서든 동일한 시퀀스를 보장한다.
        /// </summary>
        struct DetRng
        {
            uint _s;
            public DetRng(uint seed) { _s = seed != 0 ? seed : 0x9E3779B9u; }

            uint NextUInt()
            {
                _s ^= _s << 13;
                _s ^= _s >> 17;
                _s ^= _s << 5;
                return _s;
            }

            /// <summary>[0, 1) 범위 float.</summary>
            public float NextFloat() => (NextUInt() & 0xFFFFFF) / 16777216f;
        }
    }
}
