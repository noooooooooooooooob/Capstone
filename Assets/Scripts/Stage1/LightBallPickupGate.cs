using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Stage1
{
    [RequireComponent(typeof(XRGrabInteractable))]
    public class LightBallPickupGate : MonoBehaviour
    {
        [Header("Debug Settings")]
        [SerializeField] private bool debugForceUnlockPickup;

        [Header("Refs")]
        [SerializeField] private FireHazardController fireHazardController;
        [SerializeField] private XRGrabInteractable grabInteractable;

        void Awake()
        {
            if (grabInteractable == null)
            {
                grabInteractable = GetComponent<XRGrabInteractable>();
            }

            if (fireHazardController == null)
            {
                fireHazardController = FireHazardController.Instance;
            }

            RefreshPickupState();
        }

        void OnEnable()
        {
            if (fireHazardController == null)
            {
                fireHazardController = FireHazardController.Instance;
            }

            if (fireHazardController != null)
            {
                fireHazardController.AllFiresExtinguished.AddListener(UnlockPickup);
                fireHazardController.FiresActivated.AddListener(LockPickup);
            }

            RefreshPickupState();
        }

        void OnDisable()
        {
            if (fireHazardController == null) return;

            fireHazardController.AllFiresExtinguished.RemoveListener(UnlockPickup);
            fireHazardController.FiresActivated.RemoveListener(LockPickup);
        }

        void Update()
        {
            if (fireHazardController == null)
            {
                fireHazardController = FireHazardController.Instance;
            }

            RefreshPickupState();
        }

        void RefreshPickupState()
        {
            if (grabInteractable == null) return;

            bool canPickup = debugForceUnlockPickup || (fireHazardController != null && fireHazardController.CanPickupLightBall);
            grabInteractable.enabled = canPickup;
        }

        void LockPickup()
        {
            if (debugForceUnlockPickup) return;
            if (grabInteractable != null) grabInteractable.enabled = false;
        }

        void UnlockPickup()
        {
            if (grabInteractable != null) grabInteractable.enabled = true;
        }
    }
}
