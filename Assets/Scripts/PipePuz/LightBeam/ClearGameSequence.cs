using System.Collections;
using UnityEngine;

namespace PipePuz.LightBeam
{
    /// <summary>
    /// 문이 처음 열리는 순간 호출되어 'Clear' UI 표시 후 일정 시간 뒤 게임 종료.
    /// BeamGatedDoor.OnFirstOpen 에 Trigger() 를 wire 해서 사용.
    /// </summary>
    public class ClearGameSequence : MonoBehaviour
    {
        [Tooltip("Clear 텍스트(또는 UI 루트). 시작 시 비활성 상태로 두고, Trigger 시 활성화.")]
        public GameObject ClearText;

        [Tooltip("Clear 표시 후 게임 종료까지 대기 시간(초).")]
        public float QuitDelay = 3f;

        [Tooltip("Console 로그.")]
        public bool LogTrigger = true;

        bool _fired;

        public void Trigger()
        {
            if (_fired) return;
            _fired = true;
            if (LogTrigger)
                Debug.Log($"[ClearGameSequence:{name}] Trigger — Clear UI on, {QuitDelay}s 후 게임 종료.");
            if (ClearText != null) ClearText.SetActive(true);
            StartCoroutine(QuitAfter(QuitDelay));
        }

        IEnumerator QuitAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
