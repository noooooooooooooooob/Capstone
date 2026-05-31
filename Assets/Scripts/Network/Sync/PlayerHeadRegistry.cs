using System.Collections.Generic;
using UnityEngine;

namespace Capstone.Network.Sync
{
    /// <summary>
    /// 현재 룸에 존재하는 모든 플레이어(로컬 + 원격)의 머리 트랜스폼 레지스트리.
    ///
    /// 용도: 근접 트리거 기반 오브젝트(자동문 등)가 "두 플레이어 중 누구라도 가까이 있으면 반응"
    /// 하도록, 결정론적으로 동일하게 계산하게 한다. 양쪽 클라이언트가 같은 머리 집합(원격 머리는
    /// NetworkTransform 으로 위치가 동기화됨)을 보므로, 별도 네트워크 상태 없이도 문 개폐가 일치한다.
    ///
    /// 등록/해제는 NetworkPlayer.Spawned / Despawned 에서 자동으로 이뤄진다.
    /// </summary>
    public static class PlayerHeadRegistry
    {
        static readonly List<Transform> _heads = new List<Transform>();

        /// <summary>등록된 모든 플레이어 머리(로컬 + 원격). 읽기 전용 순회용.</summary>
        public static IReadOnlyList<Transform> Heads => _heads;

        public static void Register(Transform head)
        {
            if (head == null || _heads.Contains(head)) return;
            _heads.Add(head);
        }

        public static void Unregister(Transform head)
        {
            if (head == null) return;
            _heads.Remove(head);
        }
    }
}
