using UnityEngine;
using UnityEngine.Events;

namespace Stage1
{
    public class FireHazardController : MonoBehaviour
    {
        public static FireHazardController Instance;

        [Tooltip("The parent object or list of fire objects to activate.")]
        [SerializeField] private GameObject[] fireObjects;

        public UnityEvent FiresActivated = new UnityEvent();
        public UnityEvent AllFiresExtinguished = new UnityEvent();

        bool firesHaveActivated;
        bool allFiresExtinguishedNotified;

        public bool CanPickupLightBall
        {
            get
            {
                if (!firesHaveActivated) return false;
                if (fireObjects == null || fireObjects.Length == 0) return false;

                foreach (var f in fireObjects)
                {
                    if (f != null && f.activeInHierarchy) return false;
                }

                return true;
            }
        }

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            DeactivateFires();
        }

        void Update()
        {
            if (!firesHaveActivated || allFiresExtinguishedNotified) return;
            if (!CanPickupLightBall) return;

            allFiresExtinguishedNotified = true;
            AllFiresExtinguished.Invoke();
            Debug.Log("[FireHazardController] All fires extinguished. LightBall pickup unlocked.");
        }

        public void ActivateFires()
        {
            firesHaveActivated = true;
            allFiresExtinguishedNotified = false;

            if (fireObjects == null) return;
            foreach (var f in fireObjects)
            {
                if (f) f.SetActive(true);
            }

            FiresActivated.Invoke();
            Debug.Log("[FireHazardController] Fires activated!");
        }

        public void DeactivateFires()
        {
            firesHaveActivated = false;
            allFiresExtinguishedNotified = false;

            if (fireObjects == null) return;
            foreach (var f in fireObjects)
            {
                if (f) f.SetActive(false);
            }
            Debug.Log("[FireHazardController] Fires deactivated.");
        }

    }
}

