using UnityEngine;
using UnityEditor;

public static class RevertRoof
{
    [MenuItem("Debug/Revert All Stage3 Meshes To DoubleSided (Stage1 Scene)")]
    public static void Revert()
    {
        var doubleMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01_DoubleSided.mat");
        var normalMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Barking_Dog/3D Free Modular Kit/Meshes/Materials/Diffuse_01.mat");

        if (doubleMat == null || normalMat == null) { Debug.LogError("Material not found"); return; }

        GameObject stage3Root = null;
        foreach (var r in UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (r.name.Trim() == "Stage 3") { stage3Root = r; break; }

        if (stage3Root == null) { Debug.LogError("Could not find 'Stage 3' root."); return; }

        int changed = 0;
        foreach (var r in stage3Root.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
        {
            var mats = r.sharedMaterials;
            bool mod = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == normalMat) { mats[i] = doubleMat; mod = true; }
            if (mod) { Undo.RecordObject(r, "Revert DoubleSided"); r.sharedMaterials = mats; EditorUtility.SetDirty(r); changed++; }
        }

        if (changed > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log("Reverted " + changed + " renderer(s) to DoubleSided and saved.");
        }
        else
            Debug.Log("Nothing to revert — all already DoubleSided.");
    }
}
