using UnityEngine;
using UnityEditor;
using System.IO;

public static class Stage2DoorDiag
{
    [MenuItem("Debug/Dump Stage2 Doors To File")]
    public static void Dump()
    {
        var sb = new System.Text.StringBuilder();
        var doors = Object.FindObjectsByType<Stage2SlidingDoor>(FindObjectsSortMode.None);
        sb.AppendLine("Stage2SlidingDoor count: " + doors.Length);

        foreach (var d in doors)
        {
            var go = d.gameObject;
            sb.AppendLine("=== " + go.name + " ===");
            sb.AppendLine("  SlideDirection : " + d.SlideDirection);
            sb.AppendLine("  SlideDistance  : " + d.SlideDistance);
            sb.AppendLine("  OpenSpeed      : " + d.OpenSpeed);
            sb.AppendLine("  CloseDelay     : " + d.CloseDelay);
            sb.AppendLine("  DetectionVolume: " + (d.DetectionVolume != null ? d.DetectionVolume.gameObject.name + " isTrigger=" + d.DetectionVolume.isTrigger : "null"));
            sb.AppendLine("  DetectionRadius: " + d.DetectionRadius);
            sb.AppendLine("  LocalPosition  : " + go.transform.localPosition);
            sb.AppendLine("  WorldPosition  : " + go.transform.position);
            sb.AppendLine("  Parent         : " + (go.transform.parent != null ? go.transform.parent.name : "ROOT"));

            var cols = go.GetComponentsInChildren<Collider>(true);
            sb.AppendLine("  ChildColliders (" + cols.Length + "):");
            foreach (var c in cols)
                sb.AppendLine("    " + c.gameObject.name + " trigger=" + c.isTrigger + " active=" + c.enabled + " " + c.GetType().Name);
        }

        // also dump instance IDs
        foreach (var d in doors)
            sb.AppendLine("InstanceID " + d.gameObject.GetInstanceID() + " = " + d.gameObject.name + " (parent: " + (d.transform.parent ? d.transform.parent.name : "ROOT") + ") SlideDir=" + d.SlideDirection);

        string path = "Assets/stage2_door_diag.txt";
        File.WriteAllText(path, sb.ToString());
        AssetDatabase.Refresh();
        Debug.Log("Dumped to " + path);
    }
}
