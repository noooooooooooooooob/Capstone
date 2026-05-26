using UnityEditor;
using UnityEngine;

public static class SwapToAstronaut
{
    const string AstronautFbxPath =
        "Assets/VR Body/low-poly-astronaut-avatar/source/Astronaut/Astronaut_Character.fbx";
    const string PrefabPath = "Assets/Prefab/Network Player.prefab";

    const string LeftHandName = "Mano_Izq_Astronauta";
    const string RightHandName = "Mano_Dch_Astronauta";

    [MenuItem("Tools/Swap Head Cube to Astronaut")]
    static void Execute()
    {
        var astronautFbx = AssetDatabase.LoadAssetAtPath<GameObject>(AstronautFbxPath);
        if (astronautFbx == null)
        {
            Debug.LogError($"[SwapToAstronaut] FBX not found: {AstronautFbxPath}");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var headAnchor = root.transform.Find("Head Anchor");
        var leftAnchor = root.transform.Find("Left Hand Anchor");
        var rightAnchor = root.transform.Find("Right Hand Anchor");

        if (headAnchor == null || leftAnchor == null || rightAnchor == null)
        {
            Debug.LogError("[SwapToAstronaut] Anchor not found");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        // --- Clean up: remove everything except anchors ---
        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            var child = root.transform.GetChild(i);
            if (child != headAnchor && child != leftAnchor && child != rightAnchor)
                Object.DestroyImmediate(child.gameObject);
        }
        foreach (var anchor in new[] { headAnchor, leftAnchor, rightAnchor })
        {
            for (int i = anchor.childCount - 1; i >= 0; i--)
            {
                var child = anchor.GetChild(i);
                if (child.name != "VoiceNetworkObject")
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        // --- Body: full astronaut under Head Anchor (remote sees full body) ---
        var body = (GameObject)PrefabUtility.InstantiatePrefab(astronautFbx, headAnchor);
        body.name = "Astronaut_Body";
        body.transform.localPosition = new Vector3(0f, -1.55f, 0f);
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one;

        // --- Left hand: separate instance under Left Hand Anchor ---
        var leftHand = (GameObject)PrefabUtility.InstantiatePrefab(astronautFbx, leftAnchor);
        leftHand.name = "Astronaut_LeftHand";
        leftHand.transform.localPosition = Vector3.zero;
        leftHand.transform.localRotation = Quaternion.identity;
        leftHand.transform.localScale = Vector3.one;
        DisableAllExcept(leftHand, LeftHandName);

        // --- Right hand: separate instance under Right Hand Anchor ---
        var rightHand = (GameObject)PrefabUtility.InstantiatePrefab(astronautFbx, rightAnchor);
        rightHand.name = "Astronaut_RightHand";
        rightHand.transform.localPosition = Vector3.zero;
        rightHand.transform.localRotation = Quaternion.identity;
        rightHand.transform.localScale = Vector3.one;
        DisableAllExcept(rightHand, RightHandName);

        // --- NetworkPlayer references ---
        var np = root.GetComponent<Capstone.Network.NetworkPlayer>();
        if (np != null)
        {
            var so = new SerializedObject(np);
            var hide = so.FindProperty("hideOnLocal");
            hide.arraySize = 1;
            hide.GetArrayElementAtIndex(0).objectReferenceValue = body;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log("[SwapToAstronaut] Done — Body under Head, hands under Hand Anchors.");
    }

    static void DisableAllExcept(GameObject root, string keepName)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.name == keepName)
                continue;
            renderer.enabled = false;
        }
    }
}
