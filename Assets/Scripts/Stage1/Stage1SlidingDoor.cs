using PipePuz.MiniGame2;
using PipePuz.SmokePuzzle;
using UnityEngine;
using UnityEngine.Events;

namespace Stage1
{
    public class Stage1SlidingDoor : MonoBehaviour
    {
        [Header("Puzzle Hooks")]
        [SerializeField] private PipeAllPuzzleController pipePuzzle;
        [SerializeField] private PipeMiniGame2Board miniGameBoard;
        [SerializeField] private PipeSystemManager pipeSystemManager;

        [Header("Sliding")]
        [SerializeField] private Transform leftDoor;
        [SerializeField] private Transform rightDoor;
        [SerializeField] private Vector3 slideAxisLocal = Vector3.right;
        [SerializeField] private float slideDistance = 3f;
        [SerializeField] private float openSpeed = 2.5f;
        [SerializeField] private bool startOpen;

        [Header("Events")]
        public UnityEvent OnOpened;
        public UnityEvent OnClosed;

        private Vector3 leftClosedLocalPosition;
        private Vector3 rightClosedLocalPosition;
        private bool shouldOpen;
        private bool openedEventFired;

        public bool IsOpen => shouldOpen;

        private void Awake()
        {
            AutoResolveDoorPanels();

            if (leftDoor != null)
                leftClosedLocalPosition = leftDoor.localPosition;

            if (rightDoor != null)
                rightClosedLocalPosition = rightDoor.localPosition;

            shouldOpen = startOpen;
            if (startOpen)
            {
                SnapToOpen();
                openedEventFired = true;
            }
        }

        private void OnEnable()
        {
            if (pipePuzzle != null)
            {
                pipePuzzle.OnSolved.AddListener(OpenDoor);
            }

            if (miniGameBoard != null)
            {
                miniGameBoard.OnSolved.AddListener(OpenDoor);
            }
        }

        private void OnDisable()
        {
            if (pipePuzzle != null)
            {
                pipePuzzle.OnSolved.RemoveListener(OpenDoor);
            }

            if (miniGameBoard != null)
            {
                miniGameBoard.OnSolved.RemoveListener(OpenDoor);
            }
        }

        private void Update()
        {
            if (!shouldOpen && pipeSystemManager != null && pipeSystemManager.IsRepaired)
            {
                OpenDoor();
            }

            AnimateDoor();
        }

        public void OpenDoor()
        {
            shouldOpen = true;
        }

        public void CloseDoor()
        {
            shouldOpen = false;
            openedEventFired = false;
        }

        public void ToggleDoor()
        {
            if (shouldOpen)
            {
                CloseDoor();
            }
            else
            {
                OpenDoor();
            }
        }

        [ContextMenu("Debug Open Door")]
        private void DebugOpenDoor()
        {
            OpenDoor();
        }

        [ContextMenu("Debug Close Door")]
        private void DebugCloseDoor()
        {
            CloseDoor();
        }

        private void AnimateDoor()
        {
            Vector3 axis = slideAxisLocal.sqrMagnitude > 0.0001f ? slideAxisLocal.normalized : Vector3.right;
            bool allAtTarget = true;

            if (leftDoor != null)
            {
                Vector3 leftTarget = shouldOpen ? leftClosedLocalPosition - axis * slideDistance : leftClosedLocalPosition;
                leftDoor.localPosition = Vector3.MoveTowards(leftDoor.localPosition, leftTarget, openSpeed * Time.deltaTime);

                if ((leftDoor.localPosition - leftTarget).sqrMagnitude > 0.0001f)
                {
                    allAtTarget = false;
                }
            }

            if (rightDoor != null)
            {
                Vector3 rightTarget = shouldOpen ? rightClosedLocalPosition + axis * slideDistance : rightClosedLocalPosition;
                rightDoor.localPosition = Vector3.MoveTowards(rightDoor.localPosition, rightTarget, openSpeed * Time.deltaTime);

                if ((rightDoor.localPosition - rightTarget).sqrMagnitude > 0.0001f)
                {
                    allAtTarget = false;
                }
            }

            if (shouldOpen && allAtTarget && !openedEventFired)
            {
                openedEventFired = true;
                OnOpened?.Invoke();
            }
            else if (!shouldOpen && allAtTarget && openedEventFired)
            {
                openedEventFired = false;
                OnClosed?.Invoke();
            }
        }

        private void SnapToOpen()
        {
            Vector3 axis = slideAxisLocal.sqrMagnitude > 0.0001f ? slideAxisLocal.normalized : Vector3.right;

            if (leftDoor != null)
            {
                leftDoor.localPosition = leftClosedLocalPosition - axis * slideDistance;
            }

            if (rightDoor != null)
            {
                rightDoor.localPosition = rightClosedLocalPosition + axis * slideDistance;
            }
        }

        private void AutoResolveDoorPanels()
        {
            if (leftDoor != null && rightDoor != null)
                return;

            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == transform)
                    continue;

                string childName = child.name.ToLowerInvariant();
                if (leftDoor == null && childName == "stage_1_door_left")
                {
                    leftDoor = child;
                }
                else if (rightDoor == null && childName == "stage_1_door_right")
                {
                    rightDoor = child;
                }
            }
        }
    }
}
