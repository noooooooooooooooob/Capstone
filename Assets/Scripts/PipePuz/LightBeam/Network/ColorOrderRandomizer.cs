using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// ColorOrderPanel 의 거울 통과 색 순서(RequiredSequence)를 매 세션 랜덤화한다. (Fusion Shared Mode)
    ///
    /// 동작:
    ///   · 권위(StateAuthority)가 스폰 시 0 이 아닌 <see cref="OrderSeed"/> 를 한 번 굴린다.
    ///   · seed 는 [Networked] 라 모든 피어(늦게 합류 포함)에 동일 복제된다.
    ///   · 모든 피어가 동일 seed → 동일 결정적 셔플 → **동일한 색 순서**를 패널에 적용한다.
    ///     (순서 배열 대신 seed 만 전송하므로 host·guest 가 항상 일치한다.)
    ///
    /// 기존 동작은 그대로 유지된다:
    ///   · LightBeamController 가 매 프레임 빔이 거친 거울 ColorId 시퀀스를 패널의 RequiredSequence 와 비교.
    ///   · 색 순서대로 모든 거울을 거치고 Receiver 에 도달해야만 Receiver 가 빛나고(SetBeamHit) 퍼즐이 풀린다.
    ///   · 이 스크립트는 "어떤 순서를 요구하는가" 만 랜덤/동기화할 뿐, 검증 로직은 건드리지 않는다.
    ///
    /// 배치: 독립 루트 GameObject 에 둔다(중첩 NetworkObject 금지). 같은 GameObject 에 NetworkObject 필요.
    /// 셋업 툴(Tools/Network/Stage3/5)이 생성/배선한다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class ColorOrderRandomizer : NetworkBehaviour
    {
        [Header("Target")]
        [Tooltip("순서를 랜덤화할 ColorOrderPanel. 셋업 툴이 할당. 비어 있으면 동작 안 함.")]
        public ColorOrderPanel panel;

        [Tooltip("섞을 색 ID 개수. 0 이하면 panel.MaxSequenceLength 사용(보통 거울 수 = 4). " +
                 "결과는 {0..count-1} 의 한 순열.")]
        public int colorCount = 4;

        [Tooltip("진단 로그.")]
        public bool verboseLog = false;

        /// <summary>0 = 미정. 권위가 굴려서 모든 피어로 복제 → 동일 순서 보장.</summary>
        [Networked, OnChangedRender(nameof(OnSeedChanged))]
        public int OrderSeed { get; set; }

        public override void Spawned()
        {
            if (HasStateAuthority && OrderSeed == 0)
            {
                int s = unchecked((int)(System.DateTime.UtcNow.Ticks ^ ((long)GetInstanceID() << 23)));
                if (s == 0) s = 1;
                OrderSeed = s;
            }

            // 늦게 합류한 피어(OnChanged 안 뜸)와 권위 자신 모두 즉시 적용.
            if (OrderSeed != 0) ApplyOrder(OrderSeed);
        }

        void OnSeedChanged()
        {
            if (OrderSeed != 0) ApplyOrder(OrderSeed);
        }

        /// <summary>에디터/디버그용: 순서를 강제로 다시 굴린다(권위에서만 의미 있음).</summary>
        [ContextMenu("Re-roll Color Order (authority)")]
        public void ReRoll()
        {
            if (Object == null || !Object.IsValid || !HasStateAuthority)
            {
                Debug.LogWarning("[ColorOrderRandomizer] ReRoll 은 StateAuthority + 스폰 후에만 가능.", this);
                return;
            }
            int s;
            do { s = Random.Range(int.MinValue, int.MaxValue); } while (s == 0 || s == OrderSeed);
            OrderSeed = s;
            ApplyOrder(s);
        }

        void ApplyOrder(int seed)
        {
            if (panel == null) return;

            int count = colorCount > 0 ? colorCount : Mathf.Max(1, panel.MaxSequenceLength);

            // {0,1,...,count-1} 초기화 후 결정적 Fisher-Yates 셔플.
            var order = new List<int>(count);
            for (int i = 0; i < count; i++) order.Add(i);

            var rng = new DetRng((uint)seed);
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1); // 0..i
                (order[i], order[j]) = (order[j], order[i]);
            }

            panel.SetSequence(order); // RequiredSequence 갱신 + 디스플레이 새로고침.

            if (verboseLog)
                Debug.Log($"[ColorOrderRandomizer] seed={seed} → 순서 [{string.Join(", ", order)}] 적용.", this);
        }

        /// <summary>결정적 PRNG (xorshift32) — 같은 seed 면 어느 기기/런타임에서든 동일 시퀀스.</summary>
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

            /// <summary>[0, maxExclusive) 범위 int.</summary>
            public int NextInt(int maxExclusive)
            {
                if (maxExclusive <= 1) return 0;
                return (int)(NextUInt() % (uint)maxExclusive);
            }
        }
    }
}
