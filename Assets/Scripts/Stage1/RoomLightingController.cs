using UnityEngine;

namespace Stage1
{
    public class RoomLightingController : MonoBehaviour
    {
        [SerializeField] private Light[] roomLights;

        [Tooltip("Fallback restore intensity used only for lights whose original intensity is 0 at startup.")]
        [SerializeField] private float lightIntesity = 2f;

        [Tooltip("Intensity to use when lights are dimmed. Adjust this in the Inspector to tune the effect.")]
        [SerializeField] private float dimIntensity = 0.05f;

        // Per-light original intensities captured at Awake so restore is always accurate.
        private float[] _originalIntensities;

        private void Awake()
        {
            CaptureOriginalIntensities();
        }

        private void CaptureOriginalIntensities()
        {
            if (roomLights == null) return;
            _originalIntensities = new float[roomLights.Length];
            for (int i = 0; i < roomLights.Length; i++)
            {
                if (roomLights[i] == null) continue;
                float orig = roomLights[i].intensity;
                // If a light is already at 0 at startup (e.g. scene saved mid-poweroff),
                // fall back to the Inspector value so we don't restore to 0.
                _originalIntensities[i] = orig > 0f ? orig : lightIntesity;
            }
        }

        public void DimLights()
        {
            Debug.Log("[RoomLighting] Dimming lights");
            foreach (var l in roomLights)
                if (l != null) l.intensity = dimIntensity;
        }

        public void RestoreLights()
        {
            Debug.Log("[RoomLighting] Restoring lights");
            if (_originalIntensities == null || _originalIntensities.Length != roomLights.Length)
                CaptureOriginalIntensities();

            for (int i = 0; i < roomLights.Length; i++)
            {
                if (roomLights[i] == null) continue;
                roomLights[i].intensity = _originalIntensities[i];
            }
        }
    }
}
