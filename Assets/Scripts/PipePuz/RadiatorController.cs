using UnityEngine;

namespace PipePuz
{
    public enum RadiatorState
    {
        Broken,
        Fixed
    }

    /// <summary>
    /// 한 라디에이터(A 또는 B) 단위의 상태 컨트롤러.
    /// - PipeSocket 안의 내용물(Pipe_New / Pipe_Broke / 비어있음)에 따라 Broken/Fixed 결정.
    /// - 자기 쪽 Valve 의 Openness 변화를 구독해서 SmokeController 의 강도를 갱신.
    /// - PipeSocket / Smoke 가 없는 라디에이터(보통 RadiatorA)는 항상 Fixed 로 동작.
    /// </summary>
    public class RadiatorController : MonoBehaviour
    {
        [Header("Refs (이 라디에이터에 속한 컴포넌트들)")]
        public Valve Valve;
        public PipeSocket Socket;          // 없으면 항상 Fixed
        public SmokeController Smoke;      // 없으면 연기 갱신 생략

        [Header("State (런타임용 표시)")]
        [SerializeField]
        RadiatorState _state = RadiatorState.Fixed;
        public RadiatorState State => _state;

        void Start()
        {
            // Socket 이 없는 쪽은 항상 Fixed.
            if (Socket == null)
            {
                _state = RadiatorState.Fixed;
            }
            else
            {
                // PipeSocket.Start 가 호출되면 OnSocketContentChanged 가 따라 들어오므로
                // 여기서는 일단 현재 보이는 상태로 초기화.
                _state = (Socket.CurrentKind == PipeKind.New) ? RadiatorState.Fixed : RadiatorState.Broken;
            }

            if (Valve != null)
            {
                Valve.OpennessChanged += OnValveChanged;
            }
            UpdateSmoke();
        }

        void OnDestroy()
        {
            if (Valve != null)
            {
                Valve.OpennessChanged -= OnValveChanged;
            }
        }

        public void OnSocketContentChanged(PipeKind? currentKind)
        {
            _state = (currentKind == PipeKind.New) ? RadiatorState.Fixed : RadiatorState.Broken;
            UpdateSmoke();
        }

        void OnValveChanged(float openness)
        {
            UpdateSmoke();
        }

        void UpdateSmoke()
        {
            if (Smoke == null) return;

            // Fixed 면 Valve 와 무관하게 연기 없음.
            if (_state == RadiatorState.Fixed)
            {
                Smoke.SetIntensity(0f);
                return;
            }

            // Broken: Valve 가 열림(1)에서 닫힘(0)으로 갈수록 연기 줄어들고,
            // 완전히 닫히면(0) 사라진다.
            float openness = (Valve != null) ? Valve.Openness : 1f;
            Smoke.SetIntensity(openness);
        }
    }
}
