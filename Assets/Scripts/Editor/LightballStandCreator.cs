#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Stage1.Editor
{
    public class LightballStandCreator
    {
        [MenuItem("Tools/Stage 1/Create Lightball Stand")]
        public static void CreateStand()
        {
            GameObject lightBall = GameObject.Find("LightBall");
            if (lightBall == null)
            {
                Debug.LogError("[LightballStandCreator] Could not find 'LightBall' in the scene.");
                return;
            }

            GameObject stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "LightBall_DisplayStand";
            
            // Basic aesthetic settings
            Undo.RegisterCreatedObjectUndo(stand, "Create Lightball Stand");
            
            Vector3 pos = lightBall.transform.position;
            stand.transform.position = new Vector3(pos.x, pos.y - 0.5f, pos.z);
            stand.transform.localScale = new Vector3(0.4f, 0.5f, 0.4f);
            
            // Remove the top cylinder collider if needed or keep it simple
            // We want it to look like a stand
            
            var renderer = stand.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material metalMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BatteryCasing.mat");
                if (metalMat != null) renderer.sharedMaterial = metalMat;
                else renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            }

            Debug.Log("[LightballStandCreator] Created display stand under LightBall.");
        }
    }
}
#endif
