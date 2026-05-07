using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Stage1
{
    public class WallVisibilityController : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private List<MeshRenderer> wallRenderers = new List<MeshRenderer>();
        [SerializeField] private float fadeDuration = 2.0f;

        private Coroutine fadeCoroutine;
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Instantly hides all walls in the list.
        /// </summary>
        [ContextMenu("Set Walls Invisible")]
        public void SetWallsInvisible()
        {
            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
                fadeCoroutine = null;
            }

            foreach (var renderer in wallRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }
        }

        /// <summary>
        /// Re-enables the walls and slowly increases their opacity.
        /// Note: Ensure the materials use a "Transparent" or "Fade" surface type for this to work correctly.
        /// </summary>
        [ContextMenu("Fade In Walls")]
        public void FadeInWalls()
        {
            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
            }

            fadeCoroutine = StartCoroutine(FadeRoutine(0f, 1f));
        }

        private IEnumerator FadeRoutine(float startAlpha, float endAlpha)
        {
            // Enable all renderers and set initial alpha
            foreach (var renderer in wallRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                    SetAlpha(renderer, startAlpha);
                }
            }

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / fadeDuration);
                float currentAlpha = Mathf.Lerp(startAlpha, endAlpha, normalizedTime);

                foreach (var renderer in wallRenderers)
                {
                    if (renderer != null)
                    {
                        SetAlpha(renderer, currentAlpha);
                    }
                }

                yield return null;
            }

            // Ensure we hit the target alpha
            foreach (var renderer in wallRenderers)
            {
                if (renderer != null)
                {
                    SetAlpha(renderer, endAlpha);
                }
            }

            fadeCoroutine = null;
        }

        private void SetAlpha(MeshRenderer renderer, float alpha)
        {
            // Using .material creates an instance of the material for this object.
            // This allows us to change the alpha without affecting other objects using the same shared material.
            Color color = renderer.material.GetColor(BaseColorProperty);
            color.a = alpha;
            renderer.material.SetColor(BaseColorProperty, color);
        }
    }
}
