using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Capstone.Network
{
    /// <summary>
    /// Photon 공식 ConnectionManager(Fusion Industries Addons) 패턴을 캡스톤에 맞춘 단일 컴포넌트.
    /// 책임: (1) 방 생성/입장 (2) 로컬 플레이어 프리팹 스폰 (3) 6자리 룸 코드 매칭 UX.
    /// 같은 GameObject에 NetworkRunner를 자동 부착 → Fusion이 INetworkRunnerCallbacks를 자동 구독.
    /// </summary>
    public class RoomLauncher : MonoBehaviour, INetworkRunnerCallbacks
    {
        [System.Flags]
        public enum ConnectionCriterias
        {
            RoomName = 1,
            SessionProperties = 2
        }

        [System.Serializable]
        public struct StringSessionProperty
        {
            public string propertyName;
            public string value;
        }

        public const int RoomCodeLength = 6;
        const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        [Header("Room configuration")]
        public GameMode gameMode = GameMode.Shared;
        public string roomName;
        public bool connectOnStart = false;
        [Tooltip("0이면 NetworkProjectConfig.DefaultPlayers 사용")]
        public int playerCount = 2;

        [Header("Room selection criteria")]
        public ConnectionCriterias connectionCriterias = ConnectionCriterias.RoomName;
        [Tooltip("매치메이킹용 키-값. 스테이지/난이도 등 추가 매칭 조건이 필요할 때 사용.")]
        public List<StringSessionProperty> additionalSessionProperties = new List<StringSessionProperty>();
        public Dictionary<string, SessionProperty> sessionProperties;

        [Header("Fusion settings")]
        [Tooltip("비워두면 같은 GameObject에 자동 부착")]
        public NetworkRunner runner;
        public LoadSceneMode loadSceneMode = LoadSceneMode.Single;
        public INetworkSceneManager sceneManager;

        [Header("Local user spawner")]
        public NetworkObject userPrefab;

        [Tooltip("플레이어 슬롯별 스폰 위치 (index 0 = Player 1, 1 = Player 2). " +
                 "비어 있거나 인덱스가 범위를 벗어나면 이 GameObject의 transform을 사용.")]
        public Transform[] spawnPoints;

        [Header("Events")]
        public UnityEvent onWillConnect = new UnityEvent();

        [Header("Info (read-only)")]
        public List<StringSessionProperty> actualSessionProperties = new List<StringSessionProperty>();

        // RoomMatchingUI 연동용 C# 이벤트 (Photon 원본의 UnityEvent와 별도로 유지)
        public event Action<string> OnConnecting;
        public event Action<string> OnConnected;
        public event Action<string> OnFailed;
        public event Action<int> OnPlayerCountChanged;

        public NetworkRunner Runner => runner;
        public bool IsBusy { get; private set; }

        // Host 모드 전용 — Shared 모드는 NetworkObject의 "Destroy When State Authority Leaves" 옵션이 처리.
        readonly Dictionary<PlayerRef, NetworkObject> _spawnedUsers = new Dictionary<PlayerRef, NetworkObject>();

        bool ShouldConnectWithRoomName => (connectionCriterias & ConnectionCriterias.RoomName) != 0;
        bool ShouldConnectWithSessionProperties => (connectionCriterias & ConnectionCriterias.SessionProperties) != 0;

        void Awake()
        {
            if (runner == null) runner = GetComponent<NetworkRunner>();
            if (runner == null) runner = gameObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;
        }

        async void Start()
        {
            // Runner가 우리 GO나 부모에 있으면 Fusion이 콜백 자동 구독.
            // 외부 Runner를 인스펙터로 연결한 경우에만 수동 등록.
            if (runner && new List<NetworkRunner>(GetComponentsInParent<NetworkRunner>()).Contains(runner) == false)
            {
                runner.AddCallbacks(this);
            }

            if (connectOnStart)
            {
                await EnterRoom(string.IsNullOrEmpty(roomName) ? GenerateRoomCode() : roomName);
            }
        }

        Dictionary<string, SessionProperty> AllConnectionSessionProperties
        {
            get
            {
                var dict = new Dictionary<string, SessionProperty>();
                actualSessionProperties.Clear();
                if (sessionProperties != null)
                {
                    foreach (var prop in sessionProperties)
                    {
                        dict.Add(prop.Key, prop.Value);
                        actualSessionProperties.Add(new StringSessionProperty { propertyName = prop.Key, value = prop.Value });
                    }
                }
                if (additionalSessionProperties != null)
                {
                    foreach (var p in additionalSessionProperties)
                    {
                        dict[p.propertyName] = p.value;
                        actualSessionProperties.Add(p);
                    }
                }
                return dict;
            }
        }

        public virtual NetworkSceneInfo CurrentSceneInfo()
        {
            var activeScene = SceneManager.GetActiveScene();
            SceneRef sceneRef = default;
            if (activeScene.buildIndex < 0 || activeScene.buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                Debug.LogError("[RoomLauncher] Current scene is not part of the build settings");
            }
            else
            {
                sceneRef = SceneRef.FromIndex(activeScene.buildIndex);
            }
            var info = new NetworkSceneInfo();
            if (sceneRef.IsValid) info.AddSceneRef(sceneRef, loadSceneMode);
            return info;
        }

        /// <summary>
        /// 룸 코드로 방 입장. RoomMatchingUI에서 호출.
        /// </summary>
        public async Task<StartGameResult> EnterRoom(string roomCode)
        {
            if (IsBusy) return default;
            if (ShouldConnectWithRoomName && string.IsNullOrEmpty(roomCode))
            {
                OnFailed?.Invoke("session name is empty");
                return default;
            }

            IsBusy = true;
            try
            {
                roomName = roomCode;
                if (sceneManager == null) sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
                onWillConnect?.Invoke();
                OnConnecting?.Invoke(roomCode);

                var args = new StartGameArgs
                {
                    GameMode = gameMode,
                    Scene = CurrentSceneInfo(),
                    SceneManager = sceneManager,
                };
                if (ShouldConnectWithRoomName) args.SessionName = roomName;
                if (ShouldConnectWithSessionProperties) args.SessionProperties = AllConnectionSessionProperties;
                if (playerCount > 0) args.PlayerCount = playerCount;

                var result = await runner.StartGame(args);
                if (result.Ok)
                {
                    // SessionProperties로만 매칭하면 실제 룸 이름은 서버가 부여 → 역으로 채워둠.
                    if (!ShouldConnectWithRoomName) roomName = runner.SessionInfo.Name;
                    OnConnected?.Invoke(roomName);
                }
                else
                {
                    OnFailed?.Invoke(result.ShutdownReason.ToString());
                }
                return result;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task LeaveRoom()
        {
            if (runner != null && runner.IsRunning) await runner.Shutdown();
        }

        #region Player spawn (ConnectionManager 패턴)

        public void OnPlayerJoinedSharedMode(NetworkRunner runner, PlayerRef player)
        {
            // Shared 모드: 각 피어가 자기 자신만 스폰 (자기 자신이 State + Input Authority).
            if (player != runner.LocalPlayer || userPrefab == null) return;

            int slot = ComputeSharedSlot(runner);
            var sp = GetSpawnPoint(slot);

            // 진단 로그 — NetworkTypeId가 invalid면 Fusion 프리팹 테이블에 미등록 상태.
            Debug.Log($"[RoomLauncher] Spawn userPrefab='{userPrefab.name}' " +
                      $"localPlayer={runner.LocalPlayer} slot={slot} pos={sp.position} " +
                      $"typeId={userPrefab.NetworkTypeId} valid={userPrefab.NetworkTypeId.IsValid}");

            // inputAuthority 인자는 Shared 모드에서 불필요 — 스폰 호출자가 자동으로 권한자.
            // onBeforeSpawned로 NetworkPlayer.Slot을 Spawned() 호출 전에 세팅.
            runner.Spawn(userPrefab, sp.position, sp.rotation, onBeforeSpawned: (r, no) =>
            {
                var np = no.GetComponent<NetworkPlayer>();
                if (np != null) np.Slot = slot;
            });
        }

        public void OnPlayerJoinedHostMode(NetworkRunner runner, PlayerRef player)
        {
            // Host 모드: 호스트가 들어오는 모든 player를 스폰하면서 input authority를 그 player에게 부여.
            if (runner.IsServer && userPrefab != null)
            {
                int slot = _spawnedUsers.Count;
                var sp = GetSpawnPoint(slot);

                var no = runner.Spawn(userPrefab, position: sp.position, rotation: sp.rotation, inputAuthority: player,
                    onBeforeSpawned: (r, n) =>
                    {
                        var np = n.GetComponent<NetworkPlayer>();
                        if (np != null) np.Slot = slot;
                    });
                _spawnedUsers[player] = no;
            }
        }

        /// <summary>
        /// Shared 모드에서 자기보다 먼저 들어온 PlayerId 수 = 자신의 슬롯.
        /// 모든 피어가 같은 ActivePlayers 집합을 보므로 결정적.
        /// </summary>
        int ComputeSharedSlot(NetworkRunner runner)
        {
            int rank = 0;
            foreach (var p in runner.ActivePlayers)
            {
                if (p == runner.LocalPlayer) continue;
                if (p.PlayerId < runner.LocalPlayer.PlayerId) rank++;
            }
            return rank;
        }

        Transform GetSpawnPoint(int slot)
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int i = Mathf.Clamp(slot, 0, spawnPoints.Length - 1);
                if (spawnPoints[i] != null) return spawnPoints[i];
            }
            return transform;
        }

        public void OnPlayerLeftHostMode(NetworkRunner runner, PlayerRef player)
        {
            if (_spawnedUsers.TryGetValue(player, out var no))
            {
                runner.Despawn(no);
                _spawnedUsers.Remove(player);
            }
        }

        #endregion

        #region INetworkRunnerCallbacks

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner.Topology == Topologies.ClientServer) OnPlayerJoinedHostMode(runner, player);
            else OnPlayerJoinedSharedMode(runner, player);
            RaisePlayerCount(runner);
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (runner.Topology == Topologies.ClientServer) OnPlayerLeftHostMode(runner, player);
            RaisePlayerCount(runner);
        }

        void RaisePlayerCount(NetworkRunner runner)
        {
            if (runner == null || runner.SessionInfo == null) return;
            OnPlayerCountChanged?.Invoke(runner.SessionInfo.PlayerCount);
        }

        public void OnConnectedToServer(NetworkRunner runner) => Debug.Log("[RoomLauncher] OnConnectedToServer");
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) => Debug.Log($"[RoomLauncher] OnDisconnectedFromServer: {reason}");
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) => Debug.Log($"[RoomLauncher] OnConnectFailed: {reason}");
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) => Debug.Log($"[RoomLauncher] Shutdown: {shutdownReason}");

        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey reliableKey, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey reliableKey, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

        #endregion

        #region Room code helpers

        public static string GenerateRoomCode()
        {
            var rng = new System.Random();
            var chars = new char[RoomCodeLength];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = CodeAlphabet[rng.Next(CodeAlphabet.Length)];
            return new string(chars);
        }

        public static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var upper = input.ToUpperInvariant();
            var sb = new System.Text.StringBuilder(Math.Min(upper.Length, RoomCodeLength));
            foreach (var c in upper)
            {
                if (CodeAlphabet.IndexOf(c) >= 0)
                {
                    sb.Append(c);
                    if (sb.Length >= RoomCodeLength) break;
                }
            }
            return sb.ToString();
        }

        #endregion
    }
}
