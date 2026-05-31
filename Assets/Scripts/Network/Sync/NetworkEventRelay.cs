using Fusion;
using UnityEngine;
using UnityEngine.Events;

namespace Capstone.Network.Sync
{
    /// <summary>
    /// 클릭/스위치 등 "이산 효과"를 모든 피어에 복제 (Fusion Shared Mode).
    ///
    /// 위치(NetworkTransform)나 표시(NetworkActiveSync)로 표현되지 않는 효과 —
    /// 사운드 재생, 점수 증가, 조명 토글, 퍼즐 단계 진행 등 — 를 상대 화면에서도
    /// 똑같이 일어나게 하려면 이 컴포넌트를 쓴다.
    ///
    /// 연결 방법:
    ///   1) 이 오브젝트의 버튼(XR Interactable)의 select/activate 또는 기존 onClick 이벤트에
    ///      NetworkEventRelay.Relay() 를 연결한다.
    ///   2) onRelayed 에, 버튼이 로컬에서 하던 것과 "동일한 대상/메서드"를 연결한다.
    ///   → 누가 누르든 전 피어가 onRelayed 를 1회씩 실행한다.
    ///
    /// 주의: 효과가 "오브젝트를 이동/숨김" 하는 것이라면, 그 대상 오브젝트에
    ///       NetworkTransform / NetworkActiveSync 를 두는 편이 더 견고하다.
    ///       Relay 는 어디에도 동기화 상태가 남지 않는 1회성 이벤트에 적합하다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class NetworkEventRelay : NetworkBehaviour
    {
        [Tooltip("모든 피어에서 실행될 효과. 버튼이 로컬에서 호출하던 것과 동일한 대상/메서드를 연결.")]
        public UnityEvent onRelayed = new UnityEvent();

        [Tooltip("진단 로그 출력.")]
        public bool verboseLog = false;

        /// <summary>로컬 상호작용 콜백에서 호출. 전 피어(호출자 포함)가 onRelayed 를 1회 실행한다.</summary>
        public void Relay()
        {
            if (Object == null || !Object.IsValid)
            {
                // 네트워크 미초기화(에디터 단독) — 로컬만 실행.
                onRelayed?.Invoke();
                return;
            }
            // InvokeLocal 기본 true → 호출한 피어 포함 전 피어가 1회씩 실행(중복 없음).
            RpcRelay();
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        void RpcRelay()
        {
            onRelayed?.Invoke();
            if (verboseLog) Debug.Log($"[NetworkEventRelay:{name}] onRelayed 실행", this);
        }
    }
}
