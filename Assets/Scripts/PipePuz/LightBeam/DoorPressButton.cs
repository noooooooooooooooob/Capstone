using UnityEngine;
using UnityEngine.Events;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 편의용 누름 버튼 — XR Interactor 의 select(잡기/그랩/트리거 클릭) 시
    /// XRSimpleInteractable.selectEntered → Press() 가 호출되어
    /// 연결된 BeamGatedDoor.ForceOpen() 으로 잠금/근접 조건 무시하고 강제 영구 열림.
    /// OnPressed 이벤트로 추가 동작(예: 타이머 시작)을 인스펙터에서 연결 가능.
    /// </summary>
    public class DoorPressButton : MonoBehaviour
    {
        [Tooltip("열어줄 BeamGatedDoor. (선택 — 비워두고 OnPressed만 쓸 수도 있음)")]
        public BeamGatedDoor Door;

        [Tooltip("한 번 눌리면 더 이상 처리하지 않음 (재누름 무시).")]
        public bool OneShot = true;

        [Tooltip("Console 로그 출력.")]
        public bool LogPress = true;

        [Tooltip("버튼이 눌렸을 때 호출 — 인스펙터에서 추가 동작 연결 (예: 타이머 시작).")]
        public UnityEvent OnPressed;

        bool _consumed;

        public void Press()
        {
            if (OneShot && _consumed) return;
            _consumed = true;

            OnPressed?.Invoke();

            if (Door != null)
            {
                Door.ForceOpen();
                if (LogPress)
                    Debug.Log($"[DoorPressButton:{name}] Press() → Door '{Door.name}'.ForceOpen() 호출.");
            }
            else if (LogPress)
            {
                Debug.Log($"[DoorPressButton:{name}] Press() — Door 없음, OnPressed만 호출.");
            }
        }
    }
}
