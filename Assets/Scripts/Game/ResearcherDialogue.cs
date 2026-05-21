using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ResearcherDialogue", menuName = "Capstone/Researcher Dialogue")]
public class ResearcherDialogue : ScriptableObject
{
    [Serializable]
    public class Line
    {
        [Tooltip("GameManager / PuzzleController가 이 id로 호출")]
        public string id;

        [Tooltip("자막 좌측에 표시될 화자 이름")]
        public string speaker = "연구원 A";

        [TextArea(2, 5)]
        public string text;

        [Tooltip("자막 표시 시간(초). 다음 대사는 이 시간 후 큐 처리됨.")]
        public float duration = 4f;

        [Tooltip("선택 — 같이 재생할 보이스 클립. AudioManager가 있으면 RPC 수신 측에서 재생.")]
        public AudioClip voiceClip;
    }

    [Tooltip("스테이지 전체 대사 풀. id로 검색.")]
    public Line[] lines;

    public Line Find(string id)
    {
        if (string.IsNullOrEmpty(id) || lines == null) return null;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i] != null && lines[i].id == id) return lines[i];
        return null;
    }
}
