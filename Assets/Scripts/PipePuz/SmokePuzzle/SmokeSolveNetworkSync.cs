using Fusion;
using UnityEngine;

namespace PipePuz.SmokePuzzle
{
    /// <summary>
    /// PipeAllPuzzleController(연기 퍼즐)의 "풀림" 상태를 모든 피어에 전파한다.
    ///
    /// 문제: 보드 풀림(IsSolved) 판정은 각 클라이언트가 자기 슬롯/파이프 상태로 독립 계산한다.
    /// 한쪽에서만 풀림으로 인식되면 그 플레이어만 연기가 사라지는 불일치가 생긴다.
    ///
    /// 해결: 어느 클라이언트든 로컬 보드가 풀리는 순간 RPC 로 전 피어에 알리고,
    /// 모든 피어가 PipeAllPuzzleController.ExternalSolvedLatch 를 켠다 → 양쪽 다 연기 제거.
    ///
    /// 요구: 같은 GameObject 에 NetworkObject + PipeAllPuzzleController.
    /// (NetworkObject 가 없으면 RPC 가 불가하므로 오프라인처럼 로컬 판정만 동작)
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PipeAllPuzzleController))]
    [DisallowMultipleComponent]
    public class SmokeSolveNetworkSync : NetworkBehaviour
    {
        PipeAllPuzzleController _ctrl;
        bool _broadcast;

        void Awake()
        {
            _ctrl = GetComponent<PipeAllPuzzleController>();
        }

        public override void Spawned()
        {
            // 늦게 합류한 피어: 이미 누가 풀어둔 상태라면 보드가 곧 IsSolved 를 보고하거나,
            // 다른 피어가 다시 RPC 를 쏘진 않으므로, 합류 시 자기 보드가 풀려있으면 바로 전파.
            if (_ctrl != null && _ctrl.LocalBoardSolved)
            {
                _broadcast = true;
                RpcSolved();
            }
        }

        public override void Render()
        {
            if (_ctrl == null || _broadcast) return;
            if (Object == null || !Object.IsValid) return;

            if (_ctrl.LocalBoardSolved)
            {
                _broadcast = true;
                RpcSolved();
            }
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        void RpcSolved()
        {
            if (_ctrl != null) _ctrl.ExternalSolvedLatch = true;
        }
    }
}
