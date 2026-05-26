using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Collections.Generic;

public class TempHallwayInspector
{
    [MenuItem("Tools/Temp/DumpRoom")]
    static void DumpRoom()
    {
        // Find Stage 1/Room
        GameObject room = null;
        foreach (var root in UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == "Stage 1")
            {
                var roomT = root.transform.Find("Room");
                if (roomT != null) { room = roomT.gameObject; break; }
            }
        }
        if (room == null) { Debug.LogError("Stage 1/Room not found"); return; }

        // Dump direct children of Room
        var sb = new StringBuilder();
        sb.AppendLine("=== Stage 1/Room direct children ===");
        sb.AppendLine("Room world pos: " + room.transform.position);
        foreach (Transform child in room.transform)
        {
            sb.AppendLine("[" + child.name + "] lp=(" +
                child.localPosition.x.ToString("F2") + "," +
                child.localPosition.y.ToString("F2") + "," +
                child.localPosition.z.ToString("F2") + ") ry=" +
                child.localEulerAngles.y.ToString("F0") +
                " children=" + child.childCount);
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/Temp/DumpFloors")]
    static void DumpFloors()
    {
        Transform floors = FindRoomChild("Floors");
        if (floors == null) return;
        DumpGroupByChunks(floors, "Floors", 60);
    }

    [MenuItem("Tools/Temp/DumpWalls")]
    static void DumpWalls()
    {
        Transform walls = FindRoomChild("Wall");
        if (walls == null) { walls = FindRoomChild("Walls"); }
        if (walls == null) return;
        DumpGroupByChunks(walls, walls.name, 50);
    }

    [MenuItem("Tools/Temp/DumpRoof")]
    static void DumpRoof()
    {
        Transform roof = FindRoomChild("Roof");
        if (roof == null) return;
        DumpGroupByChunks(roof, "Roof", 50);
    }

    static Transform FindRoomChild(string childName)
    {
        foreach (var root in UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == "Stage 1")
            {
                var roomT = root.transform.Find("Room");
                if (roomT != null)
                {
                    var child = roomT.Find(childName);
                    if (child != null) return child;
                }
            }
        }
        Debug.LogError("Could not find Stage 1/Room/" + childName);
        return null;
    }

    static void DumpGroupByChunks(Transform group, string label, int chunkSize)
    {
        var children = new List<Transform>();
        foreach (Transform c in group) children.Add(c);

        int total = children.Count;
        int chunks = (total + chunkSize - 1) / chunkSize;

        for (int chunk = 0; chunk < chunks; chunk++)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== " + label + " [" + (chunk * chunkSize + 1) + "-" + Mathf.Min((chunk+1)*chunkSize, total) + " of " + total + "] ===");
            sb.AppendLine("Group lp=" + group.localPosition + " lrot=" + group.localEulerAngles.y.ToString("F0"));
            for (int i = chunk * chunkSize; i < Mathf.Min((chunk + 1) * chunkSize, total); i++)
            {
                var c = children[i];
                sb.AppendLine("[" + c.name + "] lp=(" +
                    c.localPosition.x.ToString("F2") + "," +
                    c.localPosition.y.ToString("F2") + "," +
                    c.localPosition.z.ToString("F2") + ") ry=" +
                    c.localEulerAngles.y.ToString("F0"));
            }
            Debug.Log(sb.ToString());
        }
    }
}
