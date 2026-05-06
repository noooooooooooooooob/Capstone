using Capstone.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Capstone.UI
{
    public class RoomMatchingUI : MonoBehaviour
    {
        [SerializeField] RoomLauncher launcher;
        [SerializeField] TMP_InputField roomCodeInput;
        [SerializeField] Button enterButton;
        [SerializeField] TMP_Text statusText;

        void Awake()
        {
            enterButton.onClick.AddListener(OnClickEnter);

            if (roomCodeInput != null)
            {
                roomCodeInput.characterLimit = RoomLauncher.RoomCodeLength;
                roomCodeInput.onValueChanged.AddListener(OnCodeChanged);
            }

            launcher.OnConnecting += HandleConnecting;
            launcher.OnConnected += HandleConnected;
            launcher.OnFailed += HandleFailed;
        }

        void OnDestroy()
        {
            if (enterButton != null) enterButton.onClick.RemoveListener(OnClickEnter);
            if (roomCodeInput != null) roomCodeInput.onValueChanged.RemoveListener(OnCodeChanged);

            if (launcher != null)
            {
                launcher.OnConnecting -= HandleConnecting;
                launcher.OnConnected -= HandleConnected;
                launcher.OnFailed -= HandleFailed;
            }
        }

        void OnCodeChanged(string value)
        {
            var sanitized = RoomLauncher.Sanitize(value);
            if (sanitized != value)
            {
                roomCodeInput.SetTextWithoutNotify(sanitized);
            }
        }

        async void OnClickEnter()
        {
            if (launcher.IsBusy) return;

            var code = RoomLauncher.Sanitize(roomCodeInput != null ? roomCodeInput.text : string.Empty);
            if (string.IsNullOrEmpty(code))
            {
                code = RoomLauncher.GenerateRoomCode();
                if (roomCodeInput != null) roomCodeInput.SetTextWithoutNotify(code);
            }

            enterButton.interactable = false;
            await launcher.EnterRoom(code);
            enterButton.interactable = true;
        }

        void HandleConnecting(string code) => SetStatus($"Connecting... ({code})");
        void HandleConnected(string code) => SetStatus($"Joined: {code}");
        void HandleFailed(string reason) => SetStatus($"Failed: {reason}");

        void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text;
        }
    }
}
