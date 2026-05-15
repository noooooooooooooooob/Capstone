#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Stage1;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Stage1.Editor
{
    public class Stage1SetupTool
    {
        [MenuItem("Tools/Stage 1/Run Complete Setup")]
        public static void RunSetup()
        {
            // 1. Setup Fire Hazards
            SetupFireHazards();

            // 2. Create Lightball Stand
            LightballStandCreator.CreateStand();

            Debug.Log("[Stage1SetupTool] Complete Setup Finished!");
        }

        private static void SetupFireHazards()
        {
            // 1. Cleanup 'FireballRoom Objects' - Remove components accidentally added to non-fire objects
            GameObject fireRoot = GameObject.Find("FireballRoom Objects");
            if (fireRoot != null)
            {
                foreach (Transform child in fireRoot.transform)
                {
                    FireHazard h = child.GetComponent<FireHazard>();
                    if (h != null) Undo.DestroyObjectImmediate(h);
                    
                    // Don't reset colliders on root objects as they might need them
                }
            }

            // 2. Find actual fire objects (starting with Fire_)
            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>(true);
            List<GameObject> fires = new List<GameObject>();
            foreach (var go in allObjects)
            {
                if (go.name.StartsWith("Fire_") && go.scene.isLoaded)
                {
                    fires.Add(go);
                }
            }

            if (fires.Count == 0)
            {
                Debug.LogWarning("[Stage1SetupTool] No objects starting with 'Fire_' found in scene.");
            }

            // Ensure LightBall is grabbable
            GameObject lightBall = GameObject.Find("LightBall");
            if (lightBall != null)
            {
                if (lightBall.GetComponent<XRGrabInteractable>() == null)
                    lightBall.AddComponent<XRGrabInteractable>();
                
                Rigidbody rb = lightBall.GetComponent<Rigidbody>();
                if (rb == null) rb = lightBall.AddComponent<Rigidbody>();
                rb.useGravity = true;
            }

            // Create Fire Manager
            GameObject fireManager = GameObject.Find("FireManager");
            if (fireManager == null)
            {
                fireManager = new GameObject("FireManager");
                Undo.RegisterCreatedObjectUndo(fireManager, "Create FireManager");
            }

            FireHazardController controller = fireManager.GetComponent<FireHazardController>();
            if (controller == null) controller = fireManager.AddComponent<FireHazardController>();

            // Create Respawn Point near entrance
            GameObject respawn = GameObject.Find("FireRespawnPoint");
            if (respawn == null)
            {
                respawn = new GameObject("FireRespawnPoint");
                respawn.transform.position = new Vector3(0, 0, 0); 
                Undo.RegisterCreatedObjectUndo(respawn, "Create RespawnPoint");
            }

            foreach (GameObject fire in fires)
            {
                // Attach FireHazard to each fire
                FireHazard hazard = fire.GetComponent<FireHazard>();
                if (hazard == null) hazard = fire.AddComponent<FireHazard>();
                hazard.respawnPoint = respawn.transform;

                // Ensure it has a trigger collider
                Collider col = fire.GetComponent<Collider>();
                if (col == null) 
                {
                    col = fire.AddComponent<SphereCollider>();
                }
                col.isTrigger = true;
            }

            // Assign fires to controller
            SerializedObject so = new SerializedObject(controller);
            SerializedProperty prop = so.FindProperty("fireObjects");
            prop.arraySize = fires.Count;
            for (int i = 0; i < fires.Count; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = fires[i];
            }
            so.ApplyModifiedProperties();

            // Assign lightBallLight to MainControlSystem
            MainControlSystem mcs = GameObject.FindObjectOfType<MainControlSystem>();
            if (mcs != null && lightBall != null)
            {
                Light l = lightBall.GetComponentInChildren<Light>();
                if (l != null)
                {
                    SerializedObject mcsSO = new SerializedObject(mcs);
                    mcsSO.FindProperty("lightBallLight").objectReferenceValue = l;
                    mcsSO.ApplyModifiedProperties();
                }
            }

            Debug.Log($"[Stage1SetupTool] Setup {fires.Count} fire hazards (starting with 'Fire_') and linked them to FireManager.");
        }
    }
}
#endif
