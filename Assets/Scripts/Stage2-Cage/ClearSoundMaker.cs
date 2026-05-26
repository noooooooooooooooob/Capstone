using UnityEngine;

public class ClearSoundMaker : MonoBehaviour
{
    public static ClearSoundMaker Instance;
    public AudioClip victorySound;
    public int totalCages = 4;
    private int correctCount = 0;

    void Awake()
    {
        Instance = this;
    }

    public void OnCreatureCorrectlyCaged()
    {
        correctCount++;
        Debug.Log($"Correct: {correctCount}/{totalCages}");
        if (correctCount >= totalCages && victorySound != null)
            AudioSource.PlayClipAtPoint(victorySound, Camera.main.transform.position);
    }
}