#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Fusion;

public class NetworkInteractableSetup : EditorWindow
{
    // NetworkObject 설정
    bool allowStateAuthorityOverride = true;
    int objectInterest = 1; // 0 = Default, 1 = AreaOfInterest

    // NetworkTransform 설정
    bool syncScale;
    bool syncParent;
    bool disableSharedModeInterpolation;

    Vector2 scrollPos;

    [MenuItem("Tools/Network Interactable Setup")]
    static void Open()
    {
        GetWindow<NetworkInteractableSetup>("Network Interactable Setup");
    }

    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.LabelField("선택된 오브젝트에 네트워크 컴포넌트 일괄 추가", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(8);

        // --- NetworkObject 설정 ---
        EditorGUILayout.LabelField("NetworkObject", EditorStyles.boldLabel);
        allowStateAuthorityOverride = EditorGUILayout.Toggle("Allow State Authority Override", allowStateAuthorityOverride);
        objectInterest = EditorGUILayout.IntPopup("Object Interest", objectInterest,
            new[] { "Default", "Area Of Interest" }, new[] { 0, 1 });

        EditorGUILayout.Space(4);

        // --- NetworkTransform 설정 ---
        EditorGUILayout.LabelField("NetworkTransform", EditorStyles.boldLabel);
        syncScale = EditorGUILayout.Toggle("Sync Scale", syncScale);
        syncParent = EditorGUILayout.Toggle("Sync Parent", syncParent);
        disableSharedModeInterpolation = EditorGUILayout.Toggle("Disable Shared Mode Interpolation", disableSharedModeInterpolation);

        EditorGUILayout.Space(8);

        // --- 선택된 오브젝트 목록 ---
        var selected = Selection.gameObjects;
        EditorGUILayout.LabelField($"선택된 오브젝트: {selected.Length}개");

        if (selected.Length > 0)
        {
            EditorGUI.indentLevel++;
            foreach (var go in selected)
                EditorGUILayout.LabelField($"- {go.name}");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        EditorGUI.BeginDisabledGroup(selected.Length == 0);
        if (GUILayout.Button("컴포넌트 추가", GUILayout.Height(32)))
        {
            foreach (var go in selected)
                SetupObject(go);
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndScrollView();
    }

    void SetupObject(GameObject go)
    {
        Undo.RegisterCompleteObjectUndo(go, "Setup Network Object");

        // NetworkObject
        NetworkObject no = go.GetComponent<NetworkObject>();
        if (no == null) no = Undo.AddComponent<NetworkObject>(go);

        var noSO = new SerializedObject(no);
        noSO.FindProperty("ObjectInterest").intValue = objectInterest;
        int flags = noSO.FindProperty("Flags").intValue;
        if (allowStateAuthorityOverride)
            flags |= (int)NetworkObjectFlags.AllowStateAuthorityOverride;
        else
            flags &= ~(int)NetworkObjectFlags.AllowStateAuthorityOverride;
        noSO.FindProperty("Flags").intValue = flags;
        noSO.ApplyModifiedProperties();

        // NetworkTransform
        NetworkTransform nt = go.GetComponent<NetworkTransform>();
        if (nt == null) nt = Undo.AddComponent<NetworkTransform>(go);

        var ntSO = new SerializedObject(nt);
        ntSO.FindProperty("SyncScale").boolValue = syncScale;
        ntSO.FindProperty("SyncParent").boolValue = syncParent;
        ntSO.FindProperty("DisableSharedModeInterpolation").boolValue = disableSharedModeInterpolation;
        ntSO.ApplyModifiedProperties();

        EditorUtility.SetDirty(go);
        Debug.Log($"[NetworkInteractableSetup] {go.name} — NetworkObject + NetworkTransform 추가 완료");
    }

    void OnSelectionChange() => Repaint();
}
#endif
