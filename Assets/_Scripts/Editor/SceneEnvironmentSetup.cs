using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Automatically sets up the environment distortion system in the current scene.
/// Run via Tools > CLAY > Setup Environment Distortion System
/// </summary>
public class SceneEnvironmentSetup : EditorWindow
{
    [MenuItem("Tools/CLAY/Setup Environment Distortion System")]
    public static void SetupEnvironmentSystem()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Debug.Log("=== Setting up Environment Distortion System ===");

        // Find key objects
        GameObject environmentObj = GameObject.Find("Environment");
        GameObject flowFieldObj = GameObject.Find("FlowFieldManager");
        GameObject cameraObj = GameObject.Find("Main Camera");

        if (environmentObj == null)
        {
            Debug.LogError("Could not find 'Environment' GameObject. Please ensure it exists.");
            return;
        }

        int changes = 0;

        // 1. Add WeatherSystem to FlowFieldManager (or create if needed)
        if (flowFieldObj == null)
        {
            flowFieldObj = new GameObject("FlowFieldManager");
            flowFieldObj.AddComponent<FlowFieldManager>();
            Debug.Log("Created FlowFieldManager GameObject");
            changes++;
        }

        WeatherSystem weatherSystem = flowFieldObj.GetComponent<WeatherSystem>();
        if (weatherSystem == null)
        {
            weatherSystem = flowFieldObj.AddComponent<WeatherSystem>();
            Debug.Log("Added WeatherSystem to FlowFieldManager");
            changes++;
        }

        // 2. Add EnvironmentMaterialManager to Environment object
        EnvironmentMaterialManager matManager = environmentObj.GetComponent<EnvironmentMaterialManager>();
        if (matManager == null)
        {
            matManager = environmentObj.AddComponent<EnvironmentMaterialManager>();
            Debug.Log("Added EnvironmentMaterialManager to Environment");
            changes++;
        }

        // 3. Configure FlowFieldManager to use WeatherSystem
        FlowFieldManager flowField = flowFieldObj.GetComponent<FlowFieldManager>();
        if (flowField != null)
        {
            SerializedObject so = new SerializedObject(flowField);
            so.FindProperty("useWeatherSystem").boolValue = true;
            so.FindProperty("weatherInfluence").floatValue = 0.7f;
            so.ApplyModifiedProperties();
            Debug.Log("Configured FlowFieldManager to use WeatherSystem");
            changes++;
        }

        // 4. Configure InfiniteBackground to use distortion
        InfiniteBackground infBg = Object.FindFirstObjectByType<InfiniteBackground>();
        if (infBg != null)
        {
            SerializedObject so = new SerializedObject(infBg);
            var useDistortionProp = so.FindProperty("useDistortion");
            if (useDistortionProp != null)
            {
                useDistortionProp.boolValue = true;
                so.FindProperty("distortionLayerIndex").intValue = 0; // FarBackground
                so.ApplyModifiedProperties();
                Debug.Log("Configured InfiniteBackground to use distortion");
                changes++;
            }
        }

        // 5. Update camera distortion settings (make subtle)
        if (cameraObj != null)
        {
            WaterDistortionCamera camDistortion = cameraObj.GetComponent<WaterDistortionCamera>();
            if (camDistortion != null)
            {
                SerializedObject so = new SerializedObject(camDistortion);
                so.FindProperty("distortionStrength").floatValue = 0.008f;
                so.FindProperty("chromaticAberration").floatValue = 0.003f;
                so.FindProperty("noiseSpeed").floatValue = 0.3f;
                so.ApplyModifiedProperties();
                Debug.Log("Updated WaterDistortionCamera to subtle settings");
                changes++;
            }

            // Remove old WaterDistortionEffect if present (obsolete component)
            var oldEffect = cameraObj.GetComponent("WaterDistortionEffect");
            if (oldEffect != null)
            {
                Undo.DestroyObjectImmediate(oldEffect);
                Debug.Log("Removed obsolete WaterDistortionEffect component");
                changes++;
            }

            // Check for DistortionCaptureCamera child and remove if not needed
            Transform captureCamera = cameraObj.transform.Find("DistortionCaptureCamera");
            if (captureCamera != null)
            {
                // This was from an old approach - can be removed
                Debug.Log("Found DistortionCaptureCamera - you may want to delete this if not used");
            }
        }

        // 6. Mark scene dirty
        if (changes > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"=== Setup complete! Made {changes} changes. Save your scene. ===");
        }
        else
        {
            Debug.Log("=== Environment system already set up. No changes needed. ===");
        }

        // Show summary
        EditorUtility.DisplayDialog("Environment Distortion Setup",
            $"Setup complete!\n\n" +
            $"Changes made: {changes}\n\n" +
            $"Components added:\n" +
            $"- WeatherSystem on FlowFieldManager\n" +
            $"- EnvironmentMaterialManager on Environment\n\n" +
            $"The system will now:\n" +
            $"1. Apply distortion to procedural backgrounds\n" +
            $"2. Integrate flow field with weather\n" +
            $"3. Keep cells undistorted (PlayArea layer)\n\n" +
            $"Don't forget to save your scene!",
            "OK");
    }

    [MenuItem("Tools/CLAY/Verify Environment Setup")]
    public static void VerifySetup()
    {
        Debug.Log("=== Verifying Environment Distortion Setup ===");

        bool allGood = true;

        // Check WeatherSystem
        WeatherSystem weather = Object.FindFirstObjectByType<WeatherSystem>();
        if (weather == null)
        {
            Debug.LogWarning("Missing: WeatherSystem");
            allGood = false;
        }
        else
        {
            Debug.Log("OK: WeatherSystem found on " + weather.gameObject.name);
        }

        // Check EnvironmentMaterialManager
        EnvironmentMaterialManager matManager = Object.FindFirstObjectByType<EnvironmentMaterialManager>();
        if (matManager == null)
        {
            Debug.LogWarning("Missing: EnvironmentMaterialManager");
            allGood = false;
        }
        else
        {
            Debug.Log("OK: EnvironmentMaterialManager found on " + matManager.gameObject.name);
        }

        // Check FlowFieldManager
        FlowFieldManager flowField = Object.FindFirstObjectByType<FlowFieldManager>();
        if (flowField == null)
        {
            Debug.LogWarning("Missing: FlowFieldManager");
            allGood = false;
        }
        else
        {
            Debug.Log("OK: FlowFieldManager found, useWeatherSystem=" + flowField.useWeatherSystem);
        }

        // Check WaterDistortionCamera
        WaterDistortionCamera camDistortion = Object.FindFirstObjectByType<WaterDistortionCamera>();
        if (camDistortion == null)
        {
            Debug.LogWarning("Missing: WaterDistortionCamera on main camera");
            allGood = false;
        }
        else
        {
            Debug.Log($"OK: WaterDistortionCamera found, strength={camDistortion.distortionStrength}");
        }

        // Check shader exists
        Shader envShader = Shader.Find("Custom/EnvironmentDistortion");
        if (envShader == null)
        {
            Debug.LogWarning("Missing: Custom/EnvironmentDistortion shader");
            allGood = false;
        }
        else
        {
            Debug.Log("OK: EnvironmentDistortion shader found");
        }

        if (allGood)
        {
            Debug.Log("=== All components verified! System ready. ===");
        }
        else
        {
            Debug.LogWarning("=== Some components missing. Run 'Setup Environment Distortion System' ===");
        }
    }
}
