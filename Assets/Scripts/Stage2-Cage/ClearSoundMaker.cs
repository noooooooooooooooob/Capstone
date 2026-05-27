using UnityEngine;
using UnityEngine.Events;

public class ClearSoundMaker : MonoBehaviour
{
    public static ClearSoundMaker Instance;
    public AudioClip victorySound;
    public int totalCages = 4;

    public UnityEvent OnSolved;

    private int correctCount = 0;
    private bool solved = false;

    void Awake()
    {
        Instance = this;
    }

    public void OnCreatureCorrectlyCaged()
    {
        if (solved) return;

        correctCount++;
        Debug.Log($"Correct: {correctCount}/{totalCages}");
        if (correctCount < totalCages) return;

        solved = true;
        if (victorySound != null)
        {
            Vector3 position = Camera.main != null ? Camera.main.transform.position : transform.position;
            AudioSource.PlayClipAtPoint(victorySound, position);
        }

        OnSolved?.Invoke();
    }
}
