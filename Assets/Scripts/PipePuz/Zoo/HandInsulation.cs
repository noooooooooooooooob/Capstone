using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.Zoo
{
    /// <summary>
    /// 각 손 GameObject(예: XR Origin 의 LeftHand/RightHand 컨트롤러 자식)에 부착.
    /// 절연 여부와 감전 상태를 표현한다. SnakeCreature 가 잡힐 수 있는지 판정의 단일 소스.
    /// 절연은 GloveAttachment 가 자식으로 들어와 있으면 자동으로 인식된다.
    /// </summary>
    public class HandInsulation : MonoBehaviour
    {
        [Header("Manual override (테스트용)")]
        [Tooltip("디버그 / 테스트용. 런타임에 강제로 절연 상태를 지정한다.")]
        [SerializeField] bool forceInsulated = false;

        [Header("Shock")]
        [Tooltip("감전 시 손 락아웃 지속 시간(s). 이 시간 동안 IsInsulated 무관하게 캡처 시도가 무시된다.")]
        [SerializeField] float shockLockoutSeconds = 1.5f;

        [Tooltip("감전 시 발생할 이벤트(햅틱/사운드/카메라 흔들기 등).")]
        public UnityEvent OnShockEvent;

        float _shockUntil;
        GloveAttachment _attached;

        public bool IsInsulated
        {
            get
            {
                if (Time.time < _shockUntil) return false; // 락아웃 중에는 절연 무의미 — 어차피 못 잡음
                if (forceInsulated) return true;
                return _attached != null;
            }
        }

        public bool IsShocked => Time.time < _shockUntil;

        /// <summary>GloveAttachment.Attach 가 호출하는 등록 메서드.</summary>
        public void RegisterGlove(GloveAttachment glove) { _attached = glove; }
        public void UnregisterGlove(GloveAttachment glove) { if (_attached == glove) _attached = null; }

        /// <summary>SnakeCreature.OnElectrocute 에서 호출.</summary>
        public void OnShock()
        {
            _shockUntil = Time.time + shockLockoutSeconds;
            OnShockEvent?.Invoke();
        }
    }

    /// <summary>
    /// 장갑 본체에 부착. 손 socket(또는 손에 의해 잡힌 상태)에 인접/부착된 동안
    /// HandInsulation 에 등록되어 IsInsulated 를 true 로 만든다.
    ///
    /// 1차 구현: 부모가 HandInsulation 을 가진 트랜스폼인지로 판정.
    /// 보다 정교한 SocketAttach 패턴은 추후 XR Socket Interactor 연동으로 교체.
    /// </summary>
    public class GloveAttachment : MonoBehaviour
    {
        HandInsulation _registered;

        void OnTransformParentChanged()
        {
            UpdateRegistration();
        }

        void OnEnable()
        {
            UpdateRegistration();
        }

        void OnDisable()
        {
            if (_registered != null) { _registered.UnregisterGlove(this); _registered = null; }
        }

        void UpdateRegistration()
        {
            var hand = GetComponentInParent<HandInsulation>();
            if (hand == _registered) return;
            if (_registered != null) _registered.UnregisterGlove(this);
            _registered = hand;
            if (_registered != null) _registered.RegisterGlove(this);
        }
    }
}
