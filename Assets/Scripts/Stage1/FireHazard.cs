using UnityEngine;
using Unity.XR.CoreUtils;

namespace Stage1
{
    public class FireHazard : MonoBehaviour
    {
        [Tooltip("The location the player is sent to when touching the fire. If null, teleports to (0,0,0).")]
        public Transform respawnPoint;

        [Tooltip("Layer or Tag name for player identification.")]
        public string playerTag = "Player";

        void OnTriggerEnter(Collider other)
        {
            // Detect Hand or Player
            if (other.CompareTag(playerTag) || other.CompareTag("Hand") || other.gameObject.layer == LayerMask.NameToLayer("XRHand") || other.GetComponentInParent<XROrigin>())
            {
                TeleportPlayer();
            }
        }

        private void TeleportPlayer()
        {
            XROrigin origin = FindFirstObjectByType<XROrigin>();
            if (origin != null)
            {
                Vector3 targetPos = respawnPoint != null ? respawnPoint.position : Vector3.zero;
                
                // Align head height to target position
                float headHeight = origin.Camera.transform.position.y - origin.transform.position.y;
                Vector3 targetHead = new Vector3(targetPos.x, targetPos.y + headHeight, targetPos.z);
                
                origin.MoveCameraToWorldLocation(targetHead);

                if (respawnPoint != null)
                {
                    float currentYaw = origin.Camera.transform.eulerAngles.y;
                    float targetYaw = respawnPoint.eulerAngles.y;
                    float deltaYaw = Mathf.DeltaAngle(currentYaw, targetYaw);
                    origin.RotateAroundCameraUsingOriginUp(deltaYaw);
                }

                Debug.Log("[FireHazard] Player touched fire! Teleporting to entrance.");
            }
        }
    }
}
