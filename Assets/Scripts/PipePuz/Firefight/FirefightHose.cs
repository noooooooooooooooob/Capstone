using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.Firefight
{
    /// <summary>
    /// 사용자가 잡고 조준하는 호스. (Fusion Shared Mode 동기화)
    ///
    /// ── 왜 NetworkBehaviour 인가 ───────────────────────────────────────────
    /// 예전엔 분사/데미지를 로컬 _grab.isSelected + 로컬 Controller.CurrentPressure 로 게이팅했다.
    /// 두 값 모두 "잡은 피어에서만" 의미가 있어서 상대 화면에선 물도 안 나오고 불도 안 꺼졌다.
    ///   · _grab.isSelected : 잡은 피어에서만 true (원격엔 복제 안 됨)
    ///   · CurrentPressure  : 네트워크 값이 아니라 각 피어가 따로 계산 → 관측자 쪽이 0일 수 있음
    ///
    /// 해결: 호스를 잡은 사람(State Authority)이 "분사 중인가 + 현재 압력"을 [Networked] 로 방송한다.
    ///       모든 피어는 그 방송값(NetSpraying / NetPressure)만 보고
    ///         (1) WaterStream 물줄기 이펙트를 재생하고,
    ///         (2) Nozzle(위치/방향은 NetworkTransform 으로 동기화됨) 에서 SphereCast 해
    ///             자기 로컬 FirefightFire 에 데미지를 적용한다.
    ///       → 누가 잡든 양쪽 화면에서 물이 보이고, 불도 양쪽에서 함께 꺼진다.
    ///
    /// 주의: 베이스 클래스를 NetworkBehaviour 로 바꿨으므로, 씬을 한 번 저장(또는 플레이 진입)해
    ///       Fusion 이 이 호스 NetworkObject 의 NetworkedBehaviours 를 재베이크해야 한다.
    /// </summary>
    public class FirefightHose : NetworkBehaviour
    {
        [Header("Refs")]
        [Tooltip("물줄기의 출발 Transform. forward 가 분사 방향.")]
        public Transform Nozzle;

        [Tooltip("물줄기 ParticleSystem.")]
        public ParticleSystem WaterStream;

        [Tooltip("불을 hit 할 때 사용할 LayerMask. 기본 모든 레이어.")]
        public LayerMask FireMask = ~0;

        public FirefightController Controller;

        [Header("Tuning")]
        [Tooltip("Pressure 1.0 일 때 최대 사정거리(m).")]
        public float MaxRange = 5f;

        [Tooltip("적중 시 fire 가 받는 데미지율(1.0 = 1 초에 strength 1 만큼 감소).")]
        public float DamagePerSecond = 0.5f;

        [Tooltip("SphereCast 의 굵기 반경(m). 정확한 조준 부담을 줄여줌 — 시각적인 물줄기 굵기와 비슷하게.")]
        public float HitRadius = 0.08f;

        [Header("Stream visual tuning")]
        public float MaxEmissionRate = 150f;
        public float MaxStartSpeed = 10f;
        public float MaxStartSize = 0.06f;
        public float MinPressureToFire = 0.05f;

        XRGrabInteractable _grab;

        // ── 잡은 사람(권위)이 방송하는 분사 상태 ──
        // 모든 피어가 이 값으로 물 이펙트 + 데미지를 실행한다.
        [Networked] public NetworkBool NetSpraying { get; set; }
        [Networked] public float NetPressure { get; set; }

        // 지금 조준해 맞추고 있는 불의 네트워크 ID. -1 = 아무것도 안 맞춤.
        // 권위(잡은 사람)가 SphereCast 로 판정해 방송하고, 모든 피어는 "같은 불"에 데미지를 준다.
        // (각 피어가 따로 레이캐스트하면 관측자 쪽은 원격 아바타/손 콜라이더에 막혀 빗나가
        //  불이 안 꺼졌다 — 그래서 타겟을 방송해 통일한다.)
        [Networked] public int NetTargetFireId { get; set; }

        // 러너 스폰 완료(네트워크 유효) 여부.
        bool NetReady => Object != null && Object.IsValid;

        /// <summary>누군가 실제로 분사 중인가(모든 피어 공통). 외부 조회용.</summary>
        public bool IsActive
        {
            get
            {
                if (NetReady) return (bool)NetSpraying;
                // 네트워크 미초기화(단독 에디터): 로컬 상태로 폴백.
                if (_grab == null || !_grab.isSelected) return false;
                if (Controller == null) return false;
                return Controller.CurrentPressure > MinPressureToFire;
            }
        }

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
        }

        /// <summary>
        /// 권위(=호스를 잡은 피어)만 실제 분사 상태/압력을 계산해 [Networked] 로 방송한다.
        /// 비권위 피어는 여기서 아무것도 쓰지 않고 방송값을 수신만 한다.
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            float p = Controller != null ? Controller.CurrentPressure : 0f;
            bool spraying = _grab != null && _grab.isSelected && p > MinPressureToFire;

            NetSpraying = spraying;
            NetPressure = p;

            // 권위가 직접 레이캐스트해서 "지금 맞추는 불"을 결정 → 모든 피어에 방송.
            var fire = spraying ? RaycastFire(p) : null;
            NetTargetFireId = fire != null ? fire.NetId : -1;
        }

        void Update()
        {
            if (Nozzle == null) return;

            // 분사 여부/압력/타겟: 네트워크가 살아있으면 방송값을, 아니면 로컬 계산을 사용.
            bool spraying;
            float pressure;
            FirefightFire target;
            if (NetReady)
            {
                spraying = (bool)NetSpraying;
                pressure = NetPressure;
                target = (spraying && NetTargetFireId >= 0) ? FirefightFire.ById(NetTargetFireId) : null;
            }
            else
            {
                pressure = Controller != null ? Controller.CurrentPressure : 0f;
                spraying = _grab != null && _grab.isSelected && pressure > MinPressureToFire;
                target = spraying ? RaycastFire(pressure) : null;
            }

            // (1) 물줄기 이펙트 — 모든 피어에서 재생/정지.
            UpdateStreamVisual(spraying, pressure);

            if (!spraying) return;

            // (2) 데미지 — 모든 피어가 "동일한 방송 타겟 불"에 같은 비율로 적용 → 양쪽에서 함께 꺼진다.
            if (target != null)
                target.ApplyDamage(DamagePerSecond * Time.deltaTime);
        }

        /// <summary>Nozzle 에서 SphereCast 해 맞은 FirefightFire 반환(없으면 null). 권위/단독 모드에서만 호출.</summary>
        FirefightFire RaycastFire(float pressure)
        {
            if (Nozzle == null) return null;
            float range = MaxRange * Mathf.Clamp01(pressure);
            if (range < 0.05f) return null;

            Vector3 origin = Nozzle.position + Nozzle.forward * 0.05f;
            float castRange = Mathf.Max(0.05f, range - 0.05f);
            if (Physics.SphereCast(origin, HitRadius, Nozzle.forward, out var hit, castRange, FireMask, QueryTriggerInteraction.Collide))
                return hit.collider.GetComponent<FirefightFire>()
                    ?? hit.collider.GetComponentInParent<FirefightFire>();
            return null;
        }

        void UpdateStreamVisual(bool active, float pressure)
        {
            if (WaterStream == null) return;
            var emission = WaterStream.emission;
            var main = WaterStream.main;

            if (active)
            {
                emission.rateOverTime = MaxEmissionRate * pressure;
                main.startSpeed = MaxStartSpeed * pressure;
                main.startSize = MaxStartSize;
                if (!WaterStream.isPlaying) WaterStream.Play();
            }
            else
            {
                emission.rateOverTime = 0f;
                if (WaterStream.isPlaying)
                    WaterStream.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }
}
