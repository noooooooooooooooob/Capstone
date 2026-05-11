using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PipePuz.EMStabilizer
{
    /// <summary>
    /// XR 슬라이더. Knob 에 부착된 XRGrabInteractable 가 사용자의 손을 따라 Knob 의 transform 을 움직이면,
    /// 이 스크립트가 LateUpdate 에서 그 위치를 TrackStart → TrackEnd 선분에 투영해 클램프 — 트랙 위에 강제 고정.
    ///
    /// Value: 0(트랙 시작) ~ 1(트랙 끝) 의 정규화된 위치. ValueChanged 이벤트로 보드 등에 전달.
    /// </summary>
    public class EMSlider : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("실제로 잡고 끌리는 노브. XRGrabInteractable + Rigidbody(kinematic) 이 붙어 있어야 한다.")]
        public Transform Knob;

        [Tooltip("트랙의 시작 위치(보통 0 끝).")]
        public Transform TrackStart;

        [Tooltip("트랙의 끝 위치(보통 1 끝).")]
        public Transform TrackEnd;

        [Header("State (read-only)")]
        [Range(0f, 1f)] public float Value;

        [Header("Init")]
        [Tooltip("씬 시작 시 적용할 초기 값(0~1).")]
        public float InitialValue = 0f;

        public event Action<float> ValueChanged;

        XRGrabInteractable _grab;

        public bool IsHeld => _grab != null && _grab.isSelected;

        void Awake()
        {
            if (Knob != null) _grab = Knob.GetComponent<XRGrabInteractable>();

            // 초기 위치 강제.
            Value = Mathf.Clamp01(InitialValue);
            SnapKnobToValue();
        }

        void LateUpdate()
        {
            if (Knob == null || TrackStart == null || TrackEnd == null) return;

            Vector3 a = TrackStart.position;
            Vector3 b = TrackEnd.position;
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-6f) return;
            Vector3 dir = ab / len;

            // 잡혔으면 손 위치(=knob.position) 를 트랙에 투영, 안 잡혔으면 마지막 Value 유지.
            float t;
            if (IsHeld)
            {
                Vector3 toKnob = Knob.position - a;
                t = Mathf.Clamp(Vector3.Dot(toKnob, dir), 0f, len);
            }
            else
            {
                t = Value * len;
            }

            // 노브를 트랙 위 정확한 위치에 고정.
            Knob.position = a + dir * t;
            // 회전도 트랙 방향에 정렬해 시각적 어색함 제거(트랙이 수평이면 노브의 right 가 트랙 방향).
            Knob.rotation = Quaternion.LookRotation(TrackStart.forward, TrackStart.up);

            float newValue = t / len;
            if (Mathf.Abs(newValue - Value) > 1e-4f)
            {
                Value = newValue;
                ValueChanged?.Invoke(Value);
            }
        }

        void SnapKnobToValue()
        {
            if (Knob == null || TrackStart == null || TrackEnd == null) return;
            Vector3 a = TrackStart.position;
            Vector3 b = TrackEnd.position;
            Knob.position = Vector3.Lerp(a, b, Value);
        }
    }
}
