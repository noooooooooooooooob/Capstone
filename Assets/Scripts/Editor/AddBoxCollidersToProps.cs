using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/Add Box Colliders To Props
/// Finds "shelf showcase", "showcase", and "table with drawers" in the active scene,
/// removes any existing collider, then adds a BoxCollider sized to the combined
/// Renderer bounds of the object and all its children.
/// </summary>
public static class AddBoxCollidersToProps
{
    static readonly string[] TargetNames =
    {
        "shelf showcase",
        "showcase",
        "table with drawers",
    };

    [MenuItem("Tools/Add Box Colliders To Props")]
    public static void Run()
    {
        int fixed_count = 0;

        foreach (string targetName in TargetNames)
        {
            // Find every instance in the scene with this name.
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var go in allObjects)
            {
                if (!go.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Undo.RecordObject(go, "Add Box Collider To Prop");

                // Remove any existing collider on the root.
                var existing = go.GetComponent<Collider>();
                if (existing != null)
                {
                    Undo.DestroyObjectImmediate(existing);
                }

                // Calculate combined world-space bounds from all Renderers.
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    Debug.LogWarning($"[AddBoxColliders] '{go.name}' has no Renderers — skipped.");
                    continue;
                }

                Bounds worldBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    worldBounds.Encapsulate(renderers[i].bounds);

                // Convert world bounds center to the root object's local space.
                Vector3 localCenter = go.transform.InverseTransformPoint(worldBounds.center);

                // Size in local space (assumes uniform scale; safe for these static props).
                Vector3 localSize = new Vector3(
                    worldBounds.size.x / go.transform.lossyScale.x,
                    worldBounds.size.y / go.transform.lossyScale.y,
                    worldBounds.size.z / go.transform.lossyScale.z);

                var col = Undo.AddComponent<BoxCollider>(go);
                col.center = localCenter;
                col.size   = localSize;

                Debug.Log($"[AddBoxColliders] Added BoxCollider to '{go.name}' — center {localCenter}, size {localSize}");
                fixed_count++;
            }
        }

        if (fixed_count == 0)
            Debug.LogWarning("[AddBoxColliders] No matching objects found in the active scene. Make sure the scene containing these props is open.");
        else
            Debug.Log($"[AddBoxColliders] Done — {fixed_count} object(s) updated.");
    }
}
