using UnityEngine;
using UnityEditor;

public static class Stage2DoorInspector
{
    [MenuItem("Debug/Inspect Stage2 Doors")]
    public static void Inspect()
    {
        var doors = GameObject.FindObjectsByType<Stage2SlidingDoor>(FindObjectsSortMode.None);
        if (doors.Length == 0)
        {
            Debug.Log("NO Stage2SlidingDoor components found. Searching by name...");
            string[] names = { "Stage2DoorLeft","Stage2DoorRight","stage2DoorLeft","stage2DoorRight","Stage2_DoorLeft","Stage2_DoorRight" };
            foreach (var n in names)
            {
                var g = GameObject.Find(n);
                if (g == null) { Debug.Log(n + ": not found"); continue; }
                string comps = string.Join(", ", System.Array.ConvertAll(g.GetComponents<Component>(), c => c.GetType().Name));
                Debug.Log("Found: " + g.name + " | " + comps);
                foreach (Transform ch in g.transform)
                    PrintNode(ch, 1);
            }
            return;
        }

        foreach (var d in doors)
        {
            var col = d.DetectionVolume;
            string detection = col != null
                ? col.GetType().Name + " isTrigger=" + col.isTrigger
                : "none — fallback sphere radius=" + d.DetectionRadius + "m";
            Debug.Log(
                "[Stage2SlidingDoor] " + d.gameObject.name +
                "\n  SlideDirection = " + d.SlideDirection +
                "\n  SlideDistance  = " + d.SlideDistance +
                "\n  Detection      : " + detection +
                "\n  LocalPosition  : " + d.transform.localPosition +
                "\n  Parent         : " + (d.transform.parent ? d.transform.parent.name : "none (root)")
            );
            foreach (Transform ch in d.transform)
                PrintNode(ch, 1);
        }
    }

    static void PrintNode(Transform t, int depth)
    {
        string indent = new string(' ', depth * 2);
        var cols = t.GetComponents<Collider>();
        string colInfo = "";
        foreach (var c in cols)
            colInfo += " [" + c.GetType().Name + (c.isTrigger ? " TRIGGER" : "") + " size=" + c.bounds.size + "]";
        Debug.Log(indent + t.name + " active=" + t.gameObject.activeSelf + colInfo);
        if (depth < 2)
            foreach (Transform ch in t) PrintNode(ch, depth + 1);
    }
}
