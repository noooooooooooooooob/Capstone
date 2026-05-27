using UnityEngine;
using UnityEditor;
using System.IO;

public static class MatHelper
{
    const string DOUBLE = "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01_DoubleSided.mat";
    const string NORMAL = "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01.mat";

    // Dump all MeshRenderers under "Roof" within "Stage 3" root, showing their material names
    [MenuItem("Debug/Inspect Stage3 Roof Materials")]
    public static void InspectRoofMaterials()
    {
        GameObject stage3Root = null;
        foreach (var r in UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (r.name.Trim() == "Stage 3") { stage3Root = r; break; }

        if (stage3Root == null) { Debug.LogError("Stage 3 root not found."); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Roof MeshRenderers under '" + stage3Root.name + "' ===");

        int count = 0;
        foreach (var r in stage3Root.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
        {
            if (!HasAncestorNamed(r.transform, "Roof")) continue;
            count++;
            string path = r.gameObject.name;
            var p = r.transform.parent; int d = 0;
            while (p != null && d < 5) { path = p.name + "/" + path; p = p.parent; d++; }
            var matNames = new System.Collections.Generic.List<string>();
            foreach (var m in r.sharedMaterials)
                matNames.Add(m != null ? m.name : "NULL");
            sb.AppendLine(path + " | mats: [" + string.Join(", ", matNames) + "]");
        }
        sb.Insert(0, "Total roof renderers: " + count + "\n");
        File.WriteAllText("Assets/roof_mat_report.txt", sb.ToString());
        AssetDatabase.Refresh();
        Debug.Log("Report saved to Assets/roof_mat_report.txt  (" + count + " roof renderers)");
    }

    // Swap DoubleSided -> Normal under "Roof" within "Stage 3" root
    [MenuItem("Debug/Swap Stage3 Roof DoubleSided -> Normal (Stage1 Scene)")]
    public static void Swap()
    {
        var doubleMat = AssetDatabase.LoadAssetAtPath<Material>(DOUBLE);
        var normalMat = AssetDatabase.LoadAssetAtPath<Material>(NORMAL);
        if (doubleMat == null) { Debug.LogError("Cannot load DoubleSided mat"); return; }
        if (normalMat == null) { Debug.LogError("Cannot load Normal mat"); return; }

        GameObject stage3Root = null;
        foreach (var r in UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (r.name.Trim() == "Stage 3") { stage3Root = r; break; }

        if (stage3Root == null) { Debug.LogError("Stage 3 root not found."); return; }

        int changed = 0;
        foreach (var r in stage3Root.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
        {
            if (!HasAncestorNamed(r.transform, "Roof")) continue;
            var mats = r.sharedMaterials;
            bool mod = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == doubleMat) { mats[i] = normalMat; mod = true; }
            if (mod) { Undo.RecordObject(r, "Swap Roof"); r.sharedMaterials = mats; EditorUtility.SetDirty(r); changed++; }
        }
        if (changed > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log("Swapped " + changed + " roof renderer(s). Ctrl+S to save.");
        }
        else Debug.LogWarning("No DoubleSided roof renderers found under Stage 3 in this scene.");
    }

    static bool HasAncestorNamed(Transform t, string name)
    {
        var cur = t.parent;
        while (cur != null) { if (cur.name == name) return true; cur = cur.parent; }
        return false;
    }
}
