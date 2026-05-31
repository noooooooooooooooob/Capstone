using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz.Firefight
{
    /// <summary>
    /// 수직 펌프. Valve.cs / EMHandle.cs 와 같은 패턴 —
    /// XRBaseInteractable 의 select 이벤트만 사용하고, 핸들은 직접 손을 따라가지 않는다.
    /// 잡힌 손의 위치를 TrackTop → TrackBottom 선분에 투영해 그 t 값만큼만 Handle 의
    /// position 을 갱신 — 수평 방향은 절대 안 움직임.
    ///
    /// ── 네트워크 동기화 (Fusion Shared Mode) ─────────────────────────────
    /// 손잡이(Handle = HandleAssembly)에는 이미 NetworkObject + NetworkTransform 이 베이크돼 있다.
    /// 그래서 "핸들 자체를 새 NetworkBehaviour 로 만들 필요 없이" 다음 규칙만 지키면 동기화된다.
    ///
    ///   1) 잡은 피어(권위 보유)만 손 위치로 Handle 을 구동한다 → NetworkTransform 이 그 위치를 전송.
    ///   2) 그 외 피어(프록시 또는 안 잡은 권위 피어)는 Handle.position 을 절대 덮어쓰지 않는다.
    ///      → NetworkTransform 이 받은 위치를 적용하게 두고, 그 위치에서 _value 를 역산만 한다.
    ///   3) Stroke 감지는 모든 피어에서 _value(동기화된 핸들 위치) 기준으로 실행 →
    ///      각 피어가 자기 FirefightController.PumpBoost 를 호출 → 압력이 양쪽 동일하게 누적.
    ///      (FirefightController 를 따로 네트워킹하지 않아도 압력이 일치한다.)
    ///
    /// 주의: 펌프 손잡이에 붙은 GrabNetworkSyncPause 는 "잡는 동안 NetworkTransform 을 꺼버려"
    ///       펌핑 중 위치 전송을 막는다(자유 그랩용 컴포넌트라 슬라이더형 펌프엔 해롭다).
    ///       Awake 에서 그 일시정지 동작을 방어적으로 끈다.
    /// </summary>
    public class FirefightPump : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("실제로 위아래로 이동할 Transform (HandleAssembly). NetworkObject/NetworkTransform 보유.")]
        public Transform Handle;

        [Tooltip("잡기 감지용 XRBaseInteractable. Handle 의 자식 또는 같은 GO 에 부착.")]
        public XRBaseInteractable GripInteractable;

        [Tooltip("트랙의 위쪽 끝 (handle 이 여기 있을 때 value = 0).")]
        public Transform TrackTop;

        [Tooltip("트랙의 아래쪽 끝 (handle 이 여기 있을 때 value = 1).")]
        public Transform TrackBottom;

        [Tooltip("압력 적립 대상.")]
        public FirefightController Controller;

        [Header("Stroke")]
        [Range(0.05f, 0.4f)]
        [Tooltip("value 가 이 값보다 작거나 (1 - 이 값) 보다 크면 양 끝 touch 로 간주.")]
        public float StrokeThreshold = 0.15f;

        [Tooltip("한 stroke 당 압력 보너스 (0..1).")]
        public float PumpBoost = 0.3f;

        [Tooltip("연속 stroke 사이의 최소 간격(s). spam 방지.")]
        public float MinStrokeInterval = 0.2f;

        [Header("Read-only state")]
        [Range(0f, 1f)]
        [SerializeField] float _value;
        public float CurrentValue => _value;
        public bool IsHeld => _activeInteractor != null;

        IXRSelectInteractor _activeInteractor;
        bool _topReached;
        bool _bottomReached;
        float _lastStrokeTime;

        // 핸들(HandleAssembly)의 NetworkObject — 권위 판정용. 단독 에디터 플레이면 null/Invalid.
        NetworkObject _handleNo;

        // 네트워크가 살아있고(러너 스폰 완료) 권위 판정이 유효한가.
        bool NetworkActive => _handleNo != null && _handleNo.IsValid;

        // 이 피어가 핸들을 직접 구동해도 되는가. 단독 플레이면 항상 true.
        bool HasDriveAuthority => !NetworkActive || _handleNo.HasStateAuthority;

        void Awake()
        {
            if (GripInteractable != null)
            {
                GripInteractable.selectEntered.AddListener(OnGrabbed);
                GripInteractable.selectExited.AddListener(OnReleased);
            }

            // 권위 판정에 쓸 NetworkObject 확보 (Handle 우선, 없으면 GripInteractable 쪽).
            if (Handle != null) _handleNo = Handle.GetComponentInParent<NetworkObject>();
            if (_handleNo == null && GripInteractable != null)
                _handleNo = GripInteractable.GetComponentInParent<NetworkObject>();

            // 펌프는 슬라이더형이라 "잡는 동안 NetworkTransform 끄기"가 오히려 동기화를 막는다.
            // 자유 그랩용 GrabNetworkSyncPause 의 일시정지 동작을 방어적으로 해제.
            if (Handle != null)
            {
                var pause = Handle.GetComponent<Stage1.GrabNetworkSyncPause>();
                if (pause != null) pause.pauseNetworkTransformWhileHeld = false;
            }
        }

        void OnDestroy()
        {
            if (GripInteractable != null)
            {
                GripInteractable.selectEntered.RemoveListener(OnGrabbed);
                GripInteractable.selectExited.RemoveListener(OnReleased);
            }
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            _activeInteractor = args.interactorObject;

            // 잡는 즉시 권위를 끌어온다(GrabAuthorityHandover 와 중복돼도 무해).
            // 권위가 넘어와야 내 NetworkTransform 이 핸들 위치를 상대에게 전송한다.
            if (NetworkActive && !_handleNo.HasStateAuthority)
                _handleNo.RequestStateAuthority();

            Debug.Log($"[FirefightPump] GRABBED by {args.interactorObject}");
        }

        void OnReleased(SelectExitEventArgs args)
        {
            _activeInteractor = null;
            Debug.Log("[FirefightPump] RELEASED");
        }

        void LateUpdate()
        {
            if (Handle == null || TrackTop == null || TrackBottom == null) return;

            Vector3 a = TrackTop.position;
            Vector3 b = TrackBottom.position;
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-6f) return;
            Vector3 dir = ab / len;

            // 이 피어가 직접 핸들을 움직이는가: 내가 잡고 있고 + (단독 플레이거나 권위 보유).
            bool driveLocally = IsHeld && HasDriveAuthority;

            if (driveLocally)
            {
                // 잡힌 손의 attach 위치를 트랙 선분에 투영. 손이 좌우로 움직여도 t 는 변화 없음.
                Vector3 attachPos = _activeInteractor.GetAttachTransform(GripInteractable).position;
                Vector3 rel = attachPos - a;
                float t = Mathf.Clamp(Vector3.Dot(rel, dir), 0f, len);

                Handle.position = a + dir * t;
                // 회전은 트랙 회전 따라 고정 — 손목 비틀어도 핸들 안 돌아감.
                Handle.rotation = TrackTop.rotation;
                _value = t / len;
            }
            else
            {
                // 프록시(또는 안 잡은 권위 피어): Handle.position 은 NetworkTransform 이 소유한다.
                // 절대 덮어쓰지 말 것 — 받은 위치를 그대로 두고, 거기서 _value 만 역산한다.
                Vector3 rel = Handle.position - a;
                float t = Mathf.Clamp(Vector3.Dot(rel, dir), 0f, len);
                _value = t / len;
            }

            // ── Stroke 감지는 모든 피어에서 _value(동기화된 핸들 위치) 기준으로 실행 ──
            // 양쪽이 동일한 핸들 움직임을 보므로 동일한 stroke 를 감지 → 압력이 양쪽 동일하게 누적.
            if (_value < StrokeThreshold) _topReached = true;
            if (_value > 1f - StrokeThreshold) _bottomReached = true;

            if (_topReached && _bottomReached)
            {
                if (Time.time - _lastStrokeTime >= MinStrokeInterval)
                {
                    _lastStrokeTime = Time.time;
                    if (Controller != null)
                    {
                        Controller.PumpBoost(PumpBoost);
                        Debug.Log($"[FirefightPump] STROKE — value={_value:F2}, pressure now {Controller.CurrentPressure:F2}");
                    }
                    else
                    {
                        Debug.LogWarning("[FirefightPump] STROKE but Controller is null!");
                    }
                }
                _topReached = false;
                _bottomReached = false;
            }
        }
    }
}
