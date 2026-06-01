using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace PipePuz.RoomCarpet
{
    /// <summary>
    /// Stage3(RoomCarpet) 카펫의 네트워크 스폰 매니저 (Fusion Shared Mode).
    ///
    /// 카펫은 디스펜서/런처가 런타임에 만들기 때문에 일반 NetworkObject 베이킹으로는 동기화되지 않는다.
    /// Stage1 의 BatteryDispenser 와 동일하게 <see cref="NetworkRunner.Spawn"/> 으로 NetworkObject 카펫
    /// 프리팹을 띄워 전 피어에 복제한다.
    ///
    /// 동작:
    ///   - 디스펜서: 권위(StateAuthority)가 매 틱 각 디스펜서에 "Spawned 상태(아직 안 잡힌) 카펫" 이
    ///     정확히 하나 있도록 유지한다. 누가 잡으면(Held) 다음 틱에 권위가 새 카펫을 보충한다.
    ///   - 런처: 발사하는 피어가 직접 Runner.Spawn 으로 카펫을 띄우고(=그 피어가 권위) 발사 속도를 부여.
    ///
    /// 배치: 다른 NetworkObject 의 자식이 되지 않도록 독립된 루트 GameObject 에 둔다(중첩 NetworkObject 금지).
    /// 요구: 같은 GameObject 에 NetworkObject. <see cref="carpetPrefab"/> 에 Carpet_Net 프리팹 할당.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class Stage3CarpetNetwork : NetworkBehaviour
    {
        /// <summary>씬에 활성화된 (네트워크 준비된) 매니저. 디스펜서/런처가 참조해 스폰을 위임한다.</summary>
        public static Stage3CarpetNetwork Active { get; private set; }

        [Tooltip("Carpet_Net 프리팹(NetworkObject + NetworkTransform + NetworkGrabbableSync + DisappearingCarpet + CarpetNetworkSync). 셋업 툴이 생성/할당.")]
        public NetworkObject carpetPrefab;

        [Tooltip("디스펜서마다 항상 1개의 대기 카펫을 유지한다(권위 측에서).")]
        public bool maintainDispensers = true;

        [Tooltip("대기 카펫을 집은 뒤 다음 카펫이 디스펜서에 다시 채워지기까지의 딜레이(초).")]
        public float RespawnDelay = 1f;

        readonly List<CarpetDispenser> _dispensers = new List<CarpetDispenser>();
        readonly Dictionary<CarpetDispenser, NetworkId> _idle = new Dictionary<CarpetDispenser, NetworkId>();
        // 보충 딜레이 타이머(중복 생성 방지 겸용). 첫 카펫은 즉시 생성.
        readonly Dictionary<CarpetDispenser, TickTimer> _respawnTimer = new Dictionary<CarpetDispenser, TickTimer>();
        readonly HashSet<CarpetDispenser> _spawnedOnce = new HashSet<CarpetDispenser>();

        public bool IsReady => Object != null && Object.IsValid && carpetPrefab != null;

        public override void Spawned()
        {
            Active = this;
            _dispensers.Clear();
            _dispensers.AddRange(FindObjectsByType<CarpetDispenser>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Active == this) Active = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !maintainDispensers || carpetPrefab == null) return;

            for (int i = 0; i < _dispensers.Count; i++)
            {
                var d = _dispensers[i];
                if (d == null || !d.isActiveAndEnabled) continue;
                EnsureIdleCarpet(d);
            }
        }

        void EnsureIdleCarpet(CarpetDispenser d)
        {
            // 1) 추적 중인 대기 카펫이 아직 'Spawned'(미파지) 상태면 유지하고 타이머 리셋.
            if (_idle.TryGetValue(d, out var id) &&
                Runner.TryFindObject(id, out var existing) && existing != null)
            {
                var c = existing.GetComponent<DisappearingCarpet>();
                if (c != null && c.CurrentState == DisappearingCarpet.State.Spawned)
                {
                    _respawnTimer.Remove(d);
                    return;
                }
            }

            // 2) 첫 카펫은 즉시 생성.
            if (!_spawnedOnce.Contains(d))
            {
                SpawnIdle(d);
                return;
            }

            // 3) 이후 보충은 RespawnDelay 만큼 기다린 뒤 1번만 생성.
            //    (타이머가 가드 역할 → '딜레이 없이 매 틱 생성'으로 인한 중복 스폰을 막는다)
            if (!_respawnTimer.TryGetValue(d, out var timer))
            {
                _respawnTimer[d] = TickTimer.CreateFromSeconds(Runner, RespawnDelay);
                return;
            }
            if (timer.ExpiredOrNotRunning(Runner))
            {
                SpawnIdle(d);
                _respawnTimer.Remove(d);
            }
        }

        void SpawnIdle(CarpetDispenser d)
        {
            Transform pt = d.SpawnPoint != null ? d.SpawnPoint : d.transform;
            var spawned = Runner.Spawn(carpetPrefab, pt.position, pt.rotation, Runner.LocalPlayer, (r, obj) =>
            {
                var sync = obj.GetComponent<CarpetNetworkSync>();
                if (sync != null) sync.ConfigureFloating(d.UseFloatingMode, d.FloatingY);
            });
            if (spawned != null)
            {
                _idle[d] = spawned.Id;
                _spawnedOnce.Add(d);
            }
        }

        /// <summary>런처가 호출. 발사하는 피어가 카펫을 띄우고(=권위) 발사 속도를 부여한다.</summary>
        public void SpawnLaunched(Vector3 pos, Quaternion rot, Vector3 velocity, Vector3 spin,
                                  bool floating, float floatingY, Collider[] ignoreWith)
        {
            if (!IsReady) return;
            Runner.Spawn(carpetPrefab, pos, rot, Runner.LocalPlayer, (r, obj) =>
            {
                var sync = obj.GetComponent<CarpetNetworkSync>();
                if (sync != null)
                {
                    sync.ConfigureFloating(floating, floatingY);
                    sync.QueueLaunch(velocity, spin, ignoreWith);
                }
            });
        }
    }
}
