using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 편의용 누름 버튼 — XR Interactor 의 select(잡기/그랩/트리거 클릭) 시
    /// XRSimpleInteractable.selectEntered → Press() 가 호출되어
    /// 연결된 BeamGatedDoor.ForceOpen() 으로 잠금/근접 조건 무시하고 강제 영구 열림.
    /// </summary>
    public class DoorPressButton : MonoBehaviour
    {
        [Tooltip("열어줄 BeamGatedDoor.")]
        public BeamGatedDoor Door;

        [Tooltip("한 번 눌리면 더 이상 처리하지 않음 (재누름 무시).")]
        public bool OneShot = true;

        [Tooltip("Console 로그 출력.")]
        public bool LogPress = true;

        bool _consumed;

        public void Press()
        {
            if (OneShot && _consumed) return;
            if (Door == null)
            {
                if (LogPress) Debug.LogWarning($"[DoorPressButton:{name}] Press() — Door 가 비어있음.");
                return;
            }
            _consumed = true;
            Door.ForceOpen();
            if (LogPress)
                Debug.Log($"[DoorPressButton:{name}] Press() → Door '{Door.name}'.ForceOpen() 호출.");
        }
    }
}
