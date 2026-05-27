using UnityEngine;
using TMPro;

/// <summary>
/// Stage 2 main display panel controller.
///
/// Shows "Creature went loose. Capture to stabilize." on start.
/// Switches to "Room Stabilized, Proceed to next room" once all creatures
/// are caged (driven by ClearSoundMaker.OnSolved).
/// </summary>
public class Stage2DisplayPanel : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI statusText;

    [Header("Messages")]
    [TextArea] public string alertMessage    = "Creature went loose.\nCapture to stabilize.";
    [TextArea] public string resolvedMessage = "Room Stabilized.\nProceed to next room.";

    [Header("Colors")]
    public Color alertColor    = new Color(1f, 0.4f, 0.1f);   // orange-red
    public Color resolvedColor = new Color(0.2f, 1f, 0.3f);   // green

    void Start()
    {
        ShowAlert();

        // Subscribe to ClearSoundMaker if it is already alive.
        // If it spawns later, Cage.cs calls ClearSoundMaker.Instance so it is
        // always instantiated before the first creature is caged.
        if (ClearSoundMaker.Instance != null)
            ClearSoundMaker.Instance.OnSolved.AddListener(ShowResolved);
    }

    void OnDestroy()
    {
        if (ClearSoundMaker.Instance != null)
            ClearSoundMaker.Instance.OnSolved.RemoveListener(ShowResolved);
    }

    void ShowAlert()
    {
        if (statusText == null) return;
        statusText.text  = alertMessage;
        statusText.color = alertColor;
    }

    /// <summary>Called by ClearSoundMaker.OnSolved when all creatures are caged.</summary>
    public void ShowResolved()
    {
        if (statusText == null) return;
        statusText.text  = resolvedMessage;
        statusText.color = resolvedColor;
    }
}
