using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Stage1
{
    /// <summary>
    /// A physical world-space button that skips the current or a specific puzzle when pressed.
    /// Works with XR Interaction Toolkit (Ray or Poke).
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class Stage1PhysicalDebugButton : MonoBehaviour
    {
        public enum DebugAction { SkipCurrent, SkipAll, SkipSpecific }

        [Header("Debug Settings")]
        public DebugAction action = DebugAction.SkipCurrent;
        [Tooltip("Only used if action is set to SkipSpecific")]
        public int puzzleIndexToSkip = 0;

        [Header("Visual Feedback")]
        public float pressDepth = 0.015f;
        public float cooldown = 1.0f;
        public Color normalColor = Color.red;
        public Color pressedColor = Color.yellow;

        private Vector3 _originalLocalPos;
        private XRSimpleInteractable _interactable;
        private Renderer _renderer;
        private bool _isOnCooldown = false;

        void Start()
        {
            _originalLocalPos = transform.localPosition;
            _renderer = GetComponent<Renderer>();
            if (_renderer) _renderer.material.color = normalColor;

            _interactable = GetComponent<XRSimpleInteractable>();
            _interactable.selectEntered.AddListener(OnButtonPressed);
        }

        private void OnButtonPressed(SelectEnterEventArgs args)
        {
            if (_isOnCooldown) return;
            StartCoroutine(PressSequence());
        }

        private IEnumerator PressSequence()
        {
            _isOnCooldown = true;

            // Visual Press
            transform.localPosition = _originalLocalPos - (transform.up * pressDepth);
            if (_renderer) _renderer.material.color = pressedColor;

            // Execute Debug Logic
            ExecuteSkip();

            yield return new WaitForSeconds(0.2f);

            // Visual Return
            transform.localPosition = _originalLocalPos;
            if (_renderer) _renderer.material.color = normalColor;

            yield return new WaitForSeconds(cooldown);
            _isOnCooldown = false;
        }

        private void ExecuteSkip()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[DebugButton] GameManager Instance not found!");
                return;
            }

            switch (action)
            {
                case DebugAction.SkipCurrent:
                    GameManager.Instance.DebugCompleteCurrentPuzzle();
                    break;

                case DebugAction.SkipAll:
                    GameManager.Instance.DebugCompleteAllRemainingPuzzles();
                    break;

                case DebugAction.SkipSpecific:
                    GameManager.Instance.DebugCompletePuzzle(puzzleIndexToSkip);
                    break;
            }
        }

        private void OnDestroy()
        {
            if (_interactable != null)
                _interactable.selectEntered.RemoveListener(OnButtonPressed);
        }
    }
}
