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

        [Tooltip("매칭 완료 시 비활성화할 루트. 비워두면 이 컴포넌트의 GameObject를 사용.")]
        [SerializeField] GameObject uiRoot;

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
            launcher.OnRoomFull += HandleRoomFull;
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
                launcher.OnRoomFull -= HandleRoomFull;
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

        void HandleRoomFull()
        {
            var root = uiRoot != null ? uiRoot : gameObject;
            root.SetActive(false);
        }

        void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text;
        }
    }
}
