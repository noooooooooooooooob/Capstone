using UnityEngine;
using UnityEditor;
using System.IO;

public static class SwapRoofMaterial
{
    // ── Diagnostic: dump everything using DoubleSided ──────────────────────
    [MenuItem("Debug/List All DoubleSided Renderers")]
    public static void ListAll()
    {
        string doublePath = "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01_DoubleSided.mat";
        var doubleMat = AssetDatabase.LoadAssetAtPath<Material>(doublePath);
        if (doubleMat == null) { Debug.LogError("Material not found: " + doublePath); return; }

        var sb = new System.Text.StringBuilder();
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        foreach (var r in renderers)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == doubleMat)
                {
                    // Build full hierarchy path
                    string path = r.gameObject.name;
                    Transform p = r.transform.parent;
                    int depth = 0;
                    while (p != null && depth < 6) { path = p.name + "/" + path; p = p.parent; depth++; }
                    sb.AppendLine(path);
                    count++;
                    break;
                }
            }
        }
        sb.Insert(0, "Total renderers using Diffuse_01_DoubleSided: " + count + "\n");
        File.WriteAllText("Assets/doublesided_report.txt", sb.ToString());
        AssetDatabase.Refresh();
        Debug.Log("Report written to Assets/doublesided_report.txt  (" + count + " renderers)");
    }

    // ── Fix: swap only renderers whose ancestor group is named "Roof" ────────
    [MenuItem("Debug/Swap Stage3 Roof Material DoubleSided -> Normal")]
    public static void Swap()
    {
        string doublePath = "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01_DoubleSided.mat";
        string normalPath = "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01.mat";

        var doubleMat = AssetDatabase.LoadAssetAtPath<Material>(doublePath);
        var normalMat = AssetDatabase.LoadAssetAtPath<Material>(normalPath);
        if (doubleMat == null) { Debug.LogError("Could not load: " + doublePath); return; }
        if (normalMat == null) { Debug.LogError("Could not load: " + normalPath); return; }

        int changed = 0;
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var r in renderers)
        {
            // Only swap if this renderer sits inside a "Roof" organisational group
            if (!HasAncestorNamedRoof(r.transform)) continue;

            var mats = r.sharedMaterials;
            bool modified = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == doubleMat) { mats[i] = normalMat; modified = true; }
            }
            if (modified)
            {
                Undo.RecordObject(r, "Swap Roof Material");
                r.sharedMaterials = mats;
                EditorUtility.SetDirty(r);
                changed++;
                Debug.Log("Swapped: " + r.gameObject.name);
            }
        }

        if (changed > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log("Done — " + changed + " roof renderer(s) swapped. Ctrl+S to save.");
        }
        else
        {
            Debug.LogWarning("No Roof renderers found with Diffuse_01_DoubleSided.");
        }
    }

    /// <summary>
    /// Returns true if any ancestor of <paramref name="t"/> is named exactly "Roof"
    /// (i.e. the organisational group, not individual mesh GameObjects).
    /// </summary>
    static bool HasAncestorNamedRoof(Transform t)
    {
        var cur = t.parent;
        while (cur != null)
        {
            if (cur.name == "Roof") return true;
            cur = cur.parent;
        }
        return false;
    }
}
