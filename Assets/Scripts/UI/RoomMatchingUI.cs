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
        [SerializeField] Button createButton;
        [SerializeField] Button joinButton;
        [SerializeField] TMP_Text statusText;
        [SerializeField] TMP_Text generatedCodeText;

        void Awake()
        {
            createButton.onClick.AddListener(OnClickCreate);
            joinButton.onClick.AddListener(OnClickJoin);

            if (roomCodeInput != null)
            {
                roomCodeInput.characterLimit = RoomLauncher.RoomCodeLength;
                roomCodeInput.onValueChanged.AddListener(OnCodeChanged);
            }

            launcher.OnConnecting += HandleConnecting;
            launcher.OnConnected += HandleConnected;
            launcher.OnFailed += HandleFailed;

            UpdateJoinInteractable(roomCodeInput != null ? roomCodeInput.text : string.Empty);
        }

        void OnDestroy()
        {
            if (createButton != null) createButton.onClick.RemoveListener(OnClickCreate);
            if (joinButton != null) joinButton.onClick.RemoveListener(OnClickJoin);
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
            UpdateJoinInteractable(sanitized);
        }

        void UpdateJoinInteractable(string sanitizedCode)
        {
            if (joinButton != null)
                joinButton.interactable = sanitizedCode.Length == RoomLauncher.RoomCodeLength && !launcher.IsBusy;
        }

        async void OnClickCreate()
        {
            if (launcher.IsBusy) return;
            var code = RoomLauncher.GenerateRoomCode();
            if (generatedCodeText != null) generatedCodeText.text = code;
            if (roomCodeInput != null) roomCodeInput.SetTextWithoutNotify(code);
            SetButtonsInteractable(false);
            await launcher.CreateRoom(code);
            SetButtonsInteractable(true);
        }

        async void OnClickJoin()
        {
            if (launcher.IsBusy) return;
            var code = RoomLauncher.Sanitize(roomCodeInput != null ? roomCodeInput.text : string.Empty);
            if (code.Length != RoomLauncher.RoomCodeLength)
            {
                SetStatus($"{RoomLauncher.RoomCodeLength}자리 코드를 입력하세요.");
                return;
            }
            SetButtonsInteractable(false);
            await launcher.JoinRoom(code);
            SetButtonsInteractable(true);
        }

        void SetButtonsInteractable(bool value)
        {
            if (createButton != null) createButton.interactable = value;
            UpdateJoinInteractable(roomCodeInput != null ? roomCodeInput.text : string.Empty);
            if (value && joinButton != null && roomCodeInput != null)
                joinButton.interactable = roomCodeInput.text.Length == RoomLauncher.RoomCodeLength;
        }

        void HandleConnecting(string code) => SetStatus($"연결 중... ({code})");
        void HandleConnected(string code) => SetStatus($"입장 완료: {code}");
        void HandleFailed(string reason) => SetStatus($"실패: {reason}");

        void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text;
        }
    }
}
