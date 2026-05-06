using UnityEngine;

namespace Stage1
{
    public class RoomLightingController : MonoBehaviour
    {
        [SerializeField] private Light[] roomLights;
        [SerializeField] private float lightIntesity;

        public void DimLights()
        {
            Debug.Log("Dimming lights");
            foreach (var l in roomLights)
            {
                if (l != null) l.intensity = 0f;
            }
        }

        public void RestoreLights()
        {
            Debug.Log("Restoring lights");
            foreach (var l in roomLights)
            {
                if (l != null) l.intensity = lightIntesity;
            }
        }
    }
}
