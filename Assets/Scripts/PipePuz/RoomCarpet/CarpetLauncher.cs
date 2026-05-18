using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// 카펫 발사기 (총).
    ///
    /// P1 이 XRGrabInteractable 로 잡고, 컨트롤러의 activate(트리거) 입력으로 카펫을 발사.
    /// 발사된 카펫은 Held 단계를 건너뛰고 곧바로 Flying 상태로 muzzle 방향으로 날아간다.
    ///
    /// 디스펜서·손던지기와 공존 — 같은 <see cref="DisappearingCarpet"/> 라이프사이클을 따르므로
    /// 사거리(양력), 안착(CarpetFloor), 5초 수명, 깜빡임 경고 모두 동일하게 동작.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class CarpetLauncher : MonoBehaviour
    {
        [Header("Muzzle")]
        [Tooltip("카펫이 생성될 위치/회전. 비워두면 런처 자기 transform.")]
        public Transform Muzzle;

        [Tooltip("발사 속도(m/s). 카펫이 muzzle.forward 방향으로 이 속도로 발사됨.")]
        public float MuzzleSpeed = 8f;

        [Tooltip("발사 각속도(rad/s). 카펫이 위/아래 축으로 회전하며 날아가는 프리스비 효과. 0 이면 회전 없음.")]
        public float MuzzleSpin = 2.5f;

        [Header("Carpet config (디스펜서와 동일하게 두면 시각·물리 일관)")]
        public Material CarpetMaterial;
        public Vector2 CarpetSize = new Vector2(0.9f, 1.2f);
        public float CarpetThickness = 0.02f;
        public float CarpetLifetime = 5f;
        public float CarpetWarningSeconds = 1.5f;

        [Header("Floating mode (Cliff variant)")]
        [Tooltip("켜면 발사된 카펫이 y=FloatingY 에서 anchor — RoomCliff 같은 절벽 모드에서 공중 발판.")]
        public bool UseFloatingMode = false;
        public float FloatingY = 0.05f;

        [Header("Refs")]
        [Tooltip("발사된 카펫이 들어갈 부모. 비워두면 부모 없음(루트). " +
                 "Controller 가 ActiveCarpetsRoot 기반으로 안전 검사하므로 보통 채워야 함.")]
        public Transform ActiveCarpetsRoot;

        [Header("Tuning")]
        [Tooltip("연발 발사 간 최소 간격(s). 0 이면 매 프레임 발사 가능.")]
        public float Cooldown = 0.5f;

        [Tooltip("activate 이벤트가 들어와도 잡혀 있지 않으면 무시. 안전장치.")]
        public bool RequireHeldToFire = true;

        [Tooltip("카펫이 muzzle 위치에서 forward 방향으로 추가 이동할 거리(m). " +
                 "카펫이 머즐 비주얼과 겹쳐 보이지 않도록 작은 양수값 권장.")]
        public float SpawnAhead = 0.05f;

        [Tooltip("발사된 카펫의 콜라이더와 런처 자신의 콜라이더 충돌을 Physics.IgnoreCollision 로 무시. " +
                 "총 자체에 카펫이 부딪혀 튕기는 문제 방지.")]
        public bool IgnoreSelfCollision = true;

        XRGrabInteractable _grab;
        bool _isHeld;
        float _nextFireTime;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            if (_grab != null)
            {
                _grab.selectEntered.AddListener(OnSelectEntered);
                _grab.selectExited.AddListener(OnSelectExited);
                _grab.activated.AddListener(OnActivated);
            }
        }

        void OnDestroy()
        {
            if (_grab != null)
            {
                _grab.selectEntered.RemoveListener(OnSelectEntered);
                _grab.selectExited.RemoveListener(OnSelectExited);
                _grab.activated.RemoveListener(OnActivated);
            }
        }

        void OnSelectEntered(SelectEnterEventArgs args) { _isHeld = true; }
        void OnSelectExited(SelectExitEventArgs args)   { _isHeld = false; }

        void OnActivated(ActivateEventArgs args)
        {
            if (RequireHeldToFire && !_isHeld) return;
            Fire();
        }

        /// <summary>
        /// 카펫 한 발 발사. cooldown 통과 시에만 실제 발사. 외부(디버그)에서도 호출 가능.
        /// </summary>
        public void Fire()
        {
            if (Time.time < _nextFireTime) return;
            _nextFireTime = Time.time + Cooldown;

            Transform origin = Muzzle != null ? Muzzle : transform;

            var go = CarpetDispenser.BuildCarpetGameObject(
                name: "Carpet (Launched)",
                material: CarpetMaterial,
                size: CarpetSize,
                thickness: CarpetThickness,
                lifetime: CarpetLifetime,
                warningSeconds: CarpetWarningSeconds);

            if (ActiveCarpetsRoot != null)
                go.transform.SetParent(ActiveCarpetsRoot, false);

            // 카펫을 muzzle 보다 살짝 앞으로 spawn — 머즐 비주얼과 겹침 방지.
            Vector3 spawnPos = origin.position + origin.forward * SpawnAhead;
            go.transform.SetPositionAndRotation(spawnPos, origin.rotation);

            // 자기 충돌 무시 — 카펫이 런처 본체에 부딪혀 튕기는 것을 막는 핵심 안전망.
            if (IgnoreSelfCollision)
            {
                var carpetCol = go.GetComponent<Collider>();
                if (carpetCol != null)
                {
                    var ownColliders = GetComponentsInChildren<Collider>(includeInactive: true);
                    for (int i = 0; i < ownColliders.Length; i++)
                    {
                        var own = ownColliders[i];
                        if (own == null || own == carpetCol) continue;
                        Physics.IgnoreCollision(carpetCol, own, true);
                    }
                }
            }

            var carpet = go.GetComponent<DisappearingCarpet>();
            // 디스펜서 발사가 아니므로 Dispenser 참조는 null 로 둠 → 첫 grab 시 디스펜서 연쇄 spawn 발생 안 함.
            // Floating mode 전파.
            carpet.UseFloatingMode = UseFloatingMode;
            carpet.FloatingY = FloatingY;
            Vector3 vel = origin.forward * MuzzleSpeed;
            Vector3 spin = MuzzleSpin != 0f ? origin.up * MuzzleSpin : Vector3.zero;
            carpet.Launch(vel, spin);
        }
    }
}
