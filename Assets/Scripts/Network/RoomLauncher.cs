using System;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace Capstone.Network
{
    public class RoomLauncher : MonoBehaviour
    {
        public const int RoomCodeLength = 6;
        const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        [SerializeField] int playerCount = 2;

        public NetworkRunner Runner { get; private set; }
        public bool IsBusy { get; private set; }

        public event Action<string> OnConnecting;
        public event Action<string> OnConnected;
        public event Action<string> OnFailed;

        public async Task<StartGameResult> EnterRoom(string roomCode)
        {
            if (IsBusy) return default;
            if (string.IsNullOrEmpty(roomCode))
            {
                OnFailed?.Invoke("session name is empty");
                return default;
            }

            IsBusy = true;
            try
            {
                if (Runner == null)
                {
                    var go = new GameObject("NetworkRunner");
                    DontDestroyOnLoad(go);
                    Runner = go.AddComponent<NetworkRunner>();
                    Runner.ProvideInput = true;
                }

                OnConnecting?.Invoke(roomCode);

                var args = new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    SessionName = roomCode,
                    PlayerCount = playerCount,
                };

                var result = await Runner.StartGame(args);
                if (result.Ok) OnConnected?.Invoke(roomCode);
                else OnFailed?.Invoke(result.ShutdownReason.ToString());
                return result;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task LeaveRoom()
        {
            if (Runner != null && Runner.IsRunning)
            {
                await Runner.Shutdown();
            }
        }

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
    }
}
