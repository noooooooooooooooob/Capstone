using UnityEngine;

public class StageTransitionTrigger : MonoBehaviour
{
    [Tooltip("The puzzle slot that should become active after this transition.")]
    public int targetPuzzleIndex = 1;

    [Tooltip("Only advance after the previous puzzle has completed.")]
    public bool requirePreviousPuzzleComplete = true;

    bool triggered;

    void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (GameManager.Instance == null) return;
        if (!IsPlayerCollider(other)) return;

        var manager = GameManager.Instance;
        if (manager.AllCompleted) return;
        if (manager.CurrentPuzzleIndex >= targetPuzzleIndex) return;

        int previousIndex = targetPuzzleIndex - 1;
        if (requirePreviousPuzzleComplete)
        {
            if (previousIndex < 0 || previousIndex >= manager.puzzles.Length) return;

            var previousPuzzle = manager.puzzles[previousIndex];
            if (previousPuzzle == null || !previousPuzzle.IsCompleted) return;
        }

        triggered = true;
        manager.RequestAdvanceToNextPuzzle();
    }

    static bool IsPlayerCollider(Collider other)
    {
        if (other.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null)
            return true;

        return other.CompareTag("Player") || other.transform.root.CompareTag("Player");
    }
}
