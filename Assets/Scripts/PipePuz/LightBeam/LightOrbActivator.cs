using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// <see cref="LightOrbSocket"/> 의 이벤트를 받아 <see cref="LightBeamEmitter"/> 와
    /// <see cref="ColorOrderPanel"/> 의 활성 상태를 제어하는 작은 브리지.
    ///
    /// orb 가 socket 에 삽입 → emitter 켜짐 + panel 색상 시퀀스 표시.
    /// orb 가 빠짐 → emitter 꺼짐 + panel 빈 슬롯 색.
    /// </summary>
    public class LightOrbActivator : MonoBehaviour
    {
        public LightOrbSocket Socket;
        public LightBeamEmitter Emitter;
        public ColorOrderPanel Panel;

        void Awake()
        {
            if (Socket != null)
            {
                Socket.OnOrbInserted.AddListener(HandleInserted);
                Socket.OnOrbRemoved.AddListener(HandleRemoved);
            }
        }

        void Start()
        {
            // 시작 시 socket 상태에 맞춰 강제 동기화.
            if (Socket != null && Socket.HasOrb) HandleInserted();
            else HandleRemoved();
        }

        void OnDestroy()
        {
            if (Socket != null)
            {
                Socket.OnOrbInserted.RemoveListener(HandleInserted);
                Socket.OnOrbRemoved.RemoveListener(HandleRemoved);
            }
        }

        void HandleInserted()
        {
            if (Emitter != null) Emitter.TurnOn();
            if (Panel != null) Panel.Activate();
        }

        void HandleRemoved()
        {
            if (Emitter != null) Emitter.TurnOff();
            if (Panel != null) Panel.Deactivate();
        }
    }
}
