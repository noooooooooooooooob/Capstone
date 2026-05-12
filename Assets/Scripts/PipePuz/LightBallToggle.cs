using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz
{
    /// <summary>
    /// LightBall 의 ON/OFF 상태 관리.
    /// 시작 시 꺼져있고, 연결된 <see cref="Button"/> 의 selectEntered 가 들어올 때마다
    /// ON ↔ OFF 가 토글된다 (한 번 누르면 켜지고, 한 번 더 누르면 꺼진다).
    /// TurnOn() / TurnOff() / Toggle() 는 외부에서도 호출 가능.
    ///
    /// 상태 갱신 시:
    ///   - TargetLight.enabled = on
    ///   - BallRenderer.sharedMaterial = on ? BallOnMaterial : BallOffMaterial
    ///   - ButtonRenderer.sharedMaterial = on ? ButtonOnMaterial : ButtonOffMaterial
    /// </summary>
    public class LightBallToggle : MonoBehaviour
    {
        [Header("LightBall refs")]
        [Tooltip("실제로 enable/disable 될 Light 컴포넌트.")]
        public Light TargetLight;

        [Tooltip("LightBall 시각 sphere 의 Renderer. ON/OFF 시 머티리얼 교체.")]
        public Renderer BallRenderer;

        [Header("Button refs")]
        [Tooltip("누르면 LightBall 이 켜지는 버튼. XRSimpleInteractable / XRGrabInteractable 모두 가능.")]
        public XRBaseInteractable Button;

        [Tooltip("버튼 머리 시각의 Renderer. ON/OFF 시 머티리얼 교체.")]
        public Renderer ButtonRenderer;

        [Header("Materials")]
        public Material BallOffMaterial;
        public Material BallOnMaterial;
        public Material ButtonOffMaterial;
        public Material ButtonOnMaterial;

        [Header("Initial")]
        [Tooltip("씬 시작 시 켜진 상태로 둘지. 기본 false (꺼짐).")]
        public bool StartOn = false;

        [Header("Events")]
        public UnityEvent OnTurnedOn;
        public UnityEvent OnTurnedOff;

        public bool IsOn { get; private set; }

        void Awake()
        {
            if (Button != null) Button.selectEntered.AddListener(OnButtonPressed);
            ApplyState(StartOn);
        }

        void OnDestroy()
        {
            if (Button != null) Button.selectEntered.RemoveListener(OnButtonPressed);
        }

        void OnButtonPressed(SelectEnterEventArgs args)
        {
            // 토글: 누를 때마다 ON ↔ OFF 가 뒤집힌다.
            ApplyState(!IsOn);
        }

        public void TurnOn() => ApplyState(true);
        public void TurnOff() => ApplyState(false);
        public void Toggle() => ApplyState(!IsOn);

        public void ApplyState(bool on)
        {
            IsOn = on;

            if (TargetLight != null) TargetLight.enabled = on;

            if (BallRenderer != null)
            {
                var mat = on ? BallOnMaterial : BallOffMaterial;
                if (mat != null) BallRenderer.sharedMaterial = mat;
            }
            if (ButtonRenderer != null)
            {
                var mat = on ? ButtonOnMaterial : ButtonOffMaterial;
                if (mat != null) ButtonRenderer.sharedMaterial = mat;
            }

            if (on) OnTurnedOn?.Invoke();
            else OnTurnedOff?.Invoke();
        }
    }
}
