using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// P1 이 2층에서 잡고 미는 빔 조준 슬라이더 — Knob 을 한 축으로만 슬라이드시켜 emitter 의 Z 위치를 바꾼다.
    ///
    /// 구현 메모:
    ///   - <see cref="Knob"/> 에는 <c>XRSimpleInteractable</c> (또는 호환되는 XRBaseInteractable) 가 붙어 있어야 함.
    ///     XRSimple 을 쓰는 이유: <c>XRGrabInteractable</c> 은 잡는 순간 attach-pose snap 으로 객체를
    ///     interactor 쪽으로 텔레포트하는 부작용이 있어 슬라이더 위치 제어와 충돌함.
    ///   - 이 스크립트가 100% 위치를 제어. <see cref="OnGrabbed"/> 에서 손/knob 의 root-local X 차이를 anchor 로 저장.
    ///   - 매 프레임(Update + LateUpdate) 손의 root-local X 변화량을 knob X 에 더하고 [Min, Max] 로 clamp.
    ///     Y, Z 는 초기값에 고정 — knob 는 절대 트랙을 벗어나지 못함.
    ///   - 즉시 emitter.position.z 를 갱신. LightBeamController(DefaultExecutionOrder 100) 가 같은 프레임에
    ///     새 위치로 빔 재계산 → 실시간 반응.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class BeamAimController : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Z 위치를 조정할 대상 이미터.")]
        public LightBeamEmitter TargetEmitter;

        [Tooltip("플레이어가 잡는 손잡이. XRSimpleInteractable + Collider 부착돼야 함.")]
        public Transform Knob;

        [Header("Knob track (root local X 축)")]
        [Tooltip("knob 의 localPosition.x 최소값 (트랙 한쪽 끝).")]
        public float MinKnobLocalX = -0.30f;

        [Tooltip("knob 의 localPosition.x 최대값 (트랙 반대쪽 끝).")]
        public float MaxKnobLocalX = +0.30f;

        [Header("Emitter target Z range (LOCAL — emitter 부모 기준)")]
        [Tooltip("knob 가 MinKnobLocalX 일 때 emitter 의 localPosition.z (InvertMapping=false 기준). " +
                 "world 가 아니라 LOCAL 인 이유: RoomCliff 부모를 옮겨도 챔버 안에서 emitter 위치가 일관됨.")]
        public float MinEmitterZ = 4f;

        [Tooltip("knob 가 MaxKnobLocalX 일 때 emitter 의 localPosition.z (InvertMapping=false 기준).")]
        public float MaxEmitterZ = 17f;

        [Tooltip("켜면 knob X → emitter Z 매핑을 반전. " +
                 "MinKnobLocalX 가 MaxEmitterZ 로, MaxKnobLocalX 가 MinEmitterZ 로 매핑됨.")]
        public bool InvertMapping = false;

        [Header("Behavior")]
        [Tooltip("놓았을 때 천천히 중앙으로 복귀.")]
        public bool ReturnToCenterWhenReleased = false;

        [Tooltip("위 옵션이 true 일 때 초당 X 변화량.")]
        public float ReturnSpeed = 0.2f;

        Vector3 _initialKnobLocalPos;
        XRBaseInteractable _interactable;
        IXRSelectInteractor _holdingInteractor;
        bool _isHeld;
        float _anchorHandLocalX;
        float _anchorKnobLocalX;
        bool _initialized;

        void Awake()
        {
            if (Knob != null)
            {
                _initialKnobLocalPos = Knob.localPosition;
                _interactable = Knob.GetComponent<XRBaseInteractable>();
                if (_interactable != null)
                {
                    _interactable.selectEntered.AddListener(OnGrabbed);
                    _interactable.selectExited.AddListener(OnReleased);
                }
            }
            _initialized = true;
        }

        void OnDestroy()
        {
            if (_interactable != null)
            {
                _interactable.selectEntered.RemoveListener(OnGrabbed);
                _interactable.selectExited.RemoveListener(OnReleased);
            }
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            _holdingInteractor = args.interactorObject;
            _isHeld = true;
            // 잡는 순간 손/knob 의 root-local X 위치 차이를 기록 → 이후 손 X 변화량만큼 knob X 가 따라감.
            if (_holdingInteractor != null && Knob != null)
            {
                Vector3 handLocal = transform.InverseTransformPoint(_holdingInteractor.transform.position);
                _anchorHandLocalX = handLocal.x;
                _anchorKnobLocalX = Knob.localPosition.x;
            }
        }

        void OnReleased(SelectExitEventArgs args)
        {
            _holdingInteractor = null;
            _isHeld = false;
        }

        // Update + LateUpdate 둘 다에서 동기화 — XR 이 손 위치를 어느 단계에서 갱신하든 즉시 반영.
        void Update() => ApplyKnobAndEmitter();
        void LateUpdate() => ApplyKnobAndEmitter();

        void ApplyKnobAndEmitter()
        {
            if (!_initialized || Knob == null || TargetEmitter == null) return;

            float targetX;
            if (_isHeld && _holdingInteractor != null)
            {
                // 현재 손의 root-local X 위치 - anchor 손 X = X 방향 이동량
                Vector3 handLocal = transform.InverseTransformPoint(_holdingInteractor.transform.position);
                float deltaX = handLocal.x - _anchorHandLocalX;
                targetX = _anchorKnobLocalX + deltaX;
            }
            else if (ReturnToCenterWhenReleased)
            {
                float center = (MinKnobLocalX + MaxKnobLocalX) * 0.5f;
                targetX = Mathf.MoveTowards(Knob.localPosition.x, center, ReturnSpeed * Time.deltaTime);
            }
            else
            {
                // 놓인 상태: 마지막 위치 유지.
                targetX = Knob.localPosition.x;
            }

            // 트랙 범위 강제 clamp — knob 은 절대 트랙을 벗어나지 못함.
            targetX = Mathf.Clamp(targetX, MinKnobLocalX, MaxKnobLocalX);

            // Y, Z 는 초기값 고정 — knob 가 위아래/옆으로 일탈 못 함.
            Knob.localPosition = new Vector3(targetX, _initialKnobLocalPos.y, _initialKnobLocalPos.z);

            // knob X → emitter localPosition.z. 같은 프레임 안에 즉시 적용.
            // LOCAL 좌표 — RoomCliff GameObject 를 옮겨도 emitter 가 챔버 내 정확한 z 에 머물도록.
            float t = Mathf.InverseLerp(MinKnobLocalX, MaxKnobLocalX, targetX);
            if (InvertMapping) t = 1f - t;
            float emitterLocalZ = Mathf.Lerp(MinEmitterZ, MaxEmitterZ, t);
            Vector3 ep = TargetEmitter.transform.localPosition;
            ep.z = emitterLocalZ;
            TargetEmitter.transform.localPosition = ep;
        }

        /// <summary>knob 의 정규화 위치 (0=Min쪽 끝, 1=Max쪽 끝). 디버그/UI 용.</summary>
        public float NormalizedPosition
        {
            get
            {
                if (Knob == null) return 0.5f;
                return Mathf.InverseLerp(MinKnobLocalX, MaxKnobLocalX, Knob.localPosition.x);
            }
        }
    }
}
