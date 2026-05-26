using UnityEngine;
using UnityEditor;
using Stage1;
using UnityEngine.SceneManagement;

public static class ReverseDoors
{
    [MenuItem("Tools/Reverse Starting Doors")]
    public static void Run()
    {
        foreach (var d in Object.FindObjectsByType<Stage1SlidingDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (d.gameObject.name != "stage_1_door_left_starting" && d.gameObject.name != "stage_1_door_right_starting")
                continue;

            var so = new SerializedObject(d);
            var leftProp  = so.FindProperty("leftDoor");
            var rightProp = so.FindProperty("rightDoor");

            // Swap whichever slot has the mesh into the other slot
            var leftMesh  = leftProp.objectReferenceValue;
            var rightMesh = rightProp.objectReferenceValue;
            leftProp.objectReferenceValue  = rightMesh;
            rightProp.objectReferenceValue = leftMesh;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(d);

            var newLeft  = so.FindProperty("leftDoor").objectReferenceValue;
            var newRight = so.FindProperty("rightDoor").objectReferenceValue;
            Debug.Log("[ReverseDoors] " + d.gameObject.name + " → leftDoor=" + (newLeft != null ? newLeft.name : "null") + " rightDoor=" + (newRight != null ? newRight.name : "null"));
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[ReverseDoors] Done.");
    }
}
