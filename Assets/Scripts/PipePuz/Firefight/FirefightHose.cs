using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.Firefight
{
    /// <summary>
    /// 사용자가 잡고 조준하는 호스.
    /// 잡혀있고 (XRGrabInteractable.isSelected) Controller.CurrentPressure > 0 일 때 자동 분사.
    /// 매 프레임 Nozzle.forward 로 Raycast (max range = MaxRange × pressure),
    /// hit 한 FirefightFire 에 ApplyDamage 호출.
    /// WaterStream ParticleSystem 의 emission rate / startSpeed / startSize 를 pressure 비례 갱신.
    /// </summary>
    public class FirefightHose : MonoBehaviour
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

        [Header("Debug — 네트워크 동기 진단 (확인 후 끄세요)")]
        [Tooltip("0.5초마다 각 피어에서 호스 위치/권한/이동 여부를 콘솔에 찍는다.")]
        public bool logNetSync = true;
        NetworkObject _no;
        float _netLogTimer;
        Vector3 _lastLoggedPos;

        public bool IsActive
        {
            get
            {
                if (_grab == null || !_grab.isSelected) return false;
                if (Controller == null) return false;
                return Controller.CurrentPressure > MinPressureToFire;
            }
        }

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _no = GetComponent<NetworkObject>();
            _lastLoggedPos = transform.position;
        }

        void Update()
        {
            if (logNetSync)
            {
                _netLogTimer += Time.deltaTime;
                if (_netLogTimer >= 0.5f)
                {
                    _netLogTimer = 0f;
                    bool moved = (transform.position - _lastLoggedPos).sqrMagnitude > 1e-8f;
                    bool valid = _no != null && _no.IsValid;
                    Debug.Log($"[Hose net] pos={transform.position} moved={moved} " +
                              $"valid={valid} hasAuth={(valid && _no.HasStateAuthority)} " +
                              $"auth={(valid ? _no.StateAuthority.ToString() : "-")} " +
                              $"grabbed={(_grab != null && _grab.isSelected)} ntEnabled={(_no != null && _no.GetComponent<NetworkTransform>() != null && _no.GetComponent<NetworkTransform>().enabled)}");
                    _lastLoggedPos = transform.position;
                }
            }

            if (Nozzle == null) return;

            float pressure = Controller != null ? Controller.CurrentPressure : 0f;
            bool active = IsActive;

            UpdateStreamVisual(active, pressure);

            if (!active) return;

            // 사정거리 = pressure 비례.
            float range = MaxRange * Mathf.Clamp01(pressure);
            if (range < 0.05f) return;

            // SphereCast — Raycast 의 직선 정확도 부담 제거. HitRadius 만큼의 굵은 줄기로 hit 검사.
            // 자기 자신(hose 콜라이더) 와의 충돌 피하려 Nozzle 보다 살짝 앞에서 시작.
            Vector3 origin = Nozzle.position + Nozzle.forward * 0.05f;
            float castRange = Mathf.Max(0.05f, range - 0.05f);
            if (Physics.SphereCast(origin, HitRadius, Nozzle.forward, out var hit, castRange, FireMask, QueryTriggerInteraction.Collide))
            {
                var fire = hit.collider.GetComponent<FirefightFire>()
                        ?? hit.collider.GetComponentInParent<FirefightFire>();
                if (fire != null)
                {
                    fire.ApplyDamage(DamagePerSecond * Time.deltaTime);
                }
            }
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
