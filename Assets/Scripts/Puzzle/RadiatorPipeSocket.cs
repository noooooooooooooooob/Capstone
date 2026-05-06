using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Capstone.Puzzle
{
    /// <summary>
    /// RadiatorB에 부착되는 파이프 소켓의 네트워크 상태.
    /// (RadiatorA 쪽은 단순 시각용이라 필요 없음. 이 컴포넌트는 RadiatorB에만 붙인다.)
    ///
    /// 책임
    /// 1) <see cref="XRSocketInteractor"/> 의 selectEntered / selectExited 를 감지해
    ///    [Networked] <see cref="ConnectedKind"/> 를 갱신
    /// 2) 어떤 파이프가 끼워졌는지에 따라 파이프 머티리얼 색을 변경
    ///    - Broke 연결 시: brokeColor (예: 회색 / 녹슨 색)
    ///    - New   연결 시: newColor   (예: 라디에이터 본체와 같은 색 — "성공" 신호)
    /// 3) <see cref="PipeLeakFog"/> 등 외부 비주얼이 ConnectedKind 를 읽어 연기 표현 결정
    ///
    /// 셋업
    /// ─ RadiatorB의 파이프 슬롯 위치에 빈 GameObject를 만들어 "PipeSocket" 이라 이름 짓고
    ///   XRSocketInteractor + 이 컴포넌트를 추가
    /// ─ 같은 부모(또는 RadiatorB) 어딘가에 NetworkObject 가 있어야 NetworkBehaviour 동작
    /// ─ Pipe broke 프리팹/오브젝트는 처음부터 소켓 안에 들어가 있도록 배치
    ///   (Inspector의 Starting Selected Interactable 설정)
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSocketInteractor))]
    public class RadiatorPipeSocket : NetworkBehaviour
    {
        public enum SocketState : byte
        {
            None  = 0,
            Broke = 1,
            New   = 2,
        }

        [Header("연결")]
        [Tooltip("이 컴포넌트와 같이 붙어있는 XRSocketInteractor. 비워두면 자동 탐색.")]
        [SerializeField] XRSocketInteractor socket;

        [Header("색 변경")]
        [Tooltip("Broke 파이프가 끼워졌을 때 적용할 색")]
        [SerializeField] Color brokeColor = new Color(0.45f, 0.40f, 0.35f, 1f);

        [Tooltip("New 파이프가 끼워졌을 때 적용할 색 (성공 신호)")]
        [SerializeField] Color newColor = new Color(0.85f, 0.30f, 0.20f, 1f);

        [Tooltip("어디에도 끼워지지 않은 상태에서의 기본 색 (들고 있는 파이프 본체에 적용 안 됨, 시각용)")]
        [SerializeField] Color emptyColor = Color.white;

        // === 네트워크 동기화 상태 ===========================================
        [Networked] public SocketState ConnectedKind { get; set; }
        // ====================================================================

        public bool IsBrokeConnected => ConnectedKind == SocketState.Broke;
        public bool IsNewConnected   => ConnectedKind == SocketState.New;

        Pipe _lastPipe;
        bool _listenersHooked;

        void Reset()
        {
            if (socket == null) socket = GetComponent<XRSocketInteractor>();
        }

        // NOTE: NetworkBehaviour 와 충돌하지 않도록 OnEnable / OnDisable 대신 Awake / OnDestroy 사용.
        void Awake()
        {
            HookListeners();
        }

        void OnDestroy()
        {
            UnhookListeners();
        }

        void HookListeners()
        {
            if (_listenersHooked) return;
            if (socket == null) socket = GetComponent<XRSocketInteractor>();
            if (socket == null) return;
            socket.selectEntered.AddListener(OnSocketEntered);
            socket.selectExited.AddListener(OnSocketExited);
            _listenersHooked = true;
        }

        void UnhookListeners()
        {
            if (!_listenersHooked || socket == null) return;
            socket.selectEntered.RemoveListener(OnSocketEntered);
            socket.selectExited.RemoveListener(OnSocketExited);
            _listenersHooked = false;
        }

        public override void Spawned()
        {
            // 시작 시 이미 들어 있는 파이프가 있다면 그 종류로 초기 상태 결정 (호스트만)
            if (HasStateAuthority)
            {
                Pipe initial = ResolveCurrentPipe();
                ConnectedKind = initial != null
                    ? (initial.Kind == PipeKind.Broke ? SocketState.Broke : SocketState.New)
                    : SocketState.None;
            }
            ApplyVisualForCurrentState();
        }

        public override void Render()
        {
            // 다른 클라이언트가 ConnectedKind 를 변경했을 수도 있으므로 매 프레임 시각 보정
            ApplyVisualForCurrentState();
        }

        // ---------------------------------------------------------------------
        // 소켓 이벤트
        // ---------------------------------------------------------------------
        void OnSocketEntered(SelectEnterEventArgs args)
        {
            var pipe = ExtractPipe(args.interactableObject as XRBaseInteractable);
            if (pipe == null) return;

            SocketState newState = pipe.Kind == PipeKind.Broke ? SocketState.Broke : SocketState.New;
            RequestSetState(newState, pipe);
        }

        void OnSocketExited(SelectExitEventArgs args)
        {
            // 빠질 때는 무조건 None
            RequestSetState(SocketState.None, null);
        }

        // ---------------------------------------------------------------------
        // 네트워크 상태 변경 (HasStateAuthority 가 아니면 RPC 위임)
        // ---------------------------------------------------------------------
        void RequestSetState(SocketState s, Pipe currentPipe)
        {
            _lastPipe = currentPipe;
            if (Object == null || !Object.IsValid)
            {
                // 네트워크가 아직 없을 때도 로컬 비주얼은 적용
                ApplyVisualForCurrentState(s, currentPipe);
                return;
            }
            if (HasStateAuthority) ConnectedKind = s;
            else                   RPC_SetState(s);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_SetState(SocketState s)
        {
            ConnectedKind = s;
        }

        // ---------------------------------------------------------------------
        // 비주얼
        // ---------------------------------------------------------------------
        void ApplyVisualForCurrentState()
        {
            ApplyVisualForCurrentState(ConnectedKind, _lastPipe ?? ResolveCurrentPipe());
        }

        void ApplyVisualForCurrentState(SocketState s, Pipe pipe)
        {
            if (pipe == null) return;

            switch (s)
            {
                case SocketState.Broke: pipe.SetTint(brokeColor); break;
                case SocketState.New:   pipe.SetTint(newColor);   break;
                default:                pipe.SetTint(emptyColor); break;
            }
        }

        Pipe ResolveCurrentPipe()
        {
            if (socket == null || !socket.hasSelection) return null;
            return ExtractPipe(socket.firstInteractableSelected as XRBaseInteractable);
        }

        static Pipe ExtractPipe(XRBaseInteractable inter)
        {
            if (inter == null) return null;
            return inter.GetComponentInParent<Pipe>() ?? inter.GetComponent<Pipe>();
        }
    }
}
