using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Editor utility for setting up EnvironmentDistortion on multiple sprites at once.
/// </summary>
public class EnvironmentDistortionSetup : EditorWindow
{
    private string[] sortingLayerNames;
    private Dictionary<string, EnvironmentDistortionRenderer.EnvironmentLayer> layerMapping;
    private bool[] applyToLayer;

    [MenuItem("Tools/CLAY/Environment Distortion Setup")]
    public static void ShowWindow()
    {
        GetWindow<EnvironmentDistortionSetup>("Env Distortion Setup");
    }

    void OnEnable()
    {
        RefreshSortingLayers();
    }

    void RefreshSortingLayers()
    {
        sortingLayerNames = new string[SortingLayer.layers.Length];
        applyToLayer = new bool[SortingLayer.layers.Length];
        layerMapping = new Dictionary<string, EnvironmentDistortionRenderer.EnvironmentLayer>();

        for (int i = 0; i < SortingLayer.layers.Length; i++)
        {
            sortingLayerNames[i] = SortingLayer.layers[i].name;

            // Auto-detect environment layers by name
            string lowerName = sortingLayerNames[i].ToLower();
            if (lowerName.Contains("background") || lowerName.Contains("bg"))
            {
                applyToLayer[i] = true;
                if (lowerName.Contains("far"))
                    layerMapping[sortingLayerNames[i]] = EnvironmentDistortionRenderer.EnvironmentLayer.FarBackground;
                else
                    layerMapping[sortingLayerNames[i]] = EnvironmentDistortionRenderer.EnvironmentLayer.Background;
            }
            else if (lowerName.Contains("midground") || lowerName.Contains("mid"))
            {
                applyToLayer[i] = true;
                layerMapping[sortingLayerNames[i]] = EnvironmentDistortionRenderer.EnvironmentLayer.Midground;
            }
            else if (lowerName.Contains("foreground") || lowerName.Contains("fg"))
            {
                applyToLayer[i] = true;
                layerMapping[sortingLayerNames[i]] = EnvironmentDistortionRenderer.EnvironmentLayer.Foreground;
            }
            else if (lowerName.Contains("environment") || lowerName.Contains("env"))
            {
                applyToLayer[i] = true;
                layerMapping[sortingLayerNames[i]] = EnvironmentDistortionRenderer.EnvironmentLayer.Background;
            }
            else
            {
                applyToLayer[i] = false;
                layerMapping[sortingLayerNames[i]] = EnvironmentDistortionRenderer.EnvironmentLayer.Background;
            }
        }
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Environment Distortion Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "This tool adds EnvironmentDistortionRenderer components to SpriteRenderers " +
            "based on their sorting layer. Select which layers are environment layers and " +
            "their distortion depth.",
            MessageType.Info);

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Sorting Layers", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical("box");
        for (int i = 0; i < sortingLayerNames.Length; i++)
        {
            EditorGUILayout.BeginHorizontal();

            applyToLayer[i] = EditorGUILayout.Toggle(applyToLayer[i], GUILayout.Width(20));
            EditorGUILayout.LabelField(sortingLayerNames[i], GUILayout.Width(150));

            if (applyToLayer[i])
            {
                layerMapping[sortingLayerNames[i]] = (EnvironmentDistortionRenderer.EnvironmentLayer)
                    EditorGUILayout.EnumPopup(layerMapping[sortingLayerNames[i]]);
            }
            else
            {
                EditorGUILayout.LabelField("(not applied)", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Apply to Scene", GUILayout.Height(30)))
        {
            ApplyToScene();
        }

        if (GUILayout.Button("Apply to Selection", GUILayout.Height(30)))
        {
            ApplyToSelection();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        if (GUILayout.Button("Remove All EnvironmentDistortionRenderer Components"))
        {
            RemoveAllComponents();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);

        int count = Object.FindObjectsByType<EnvironmentDistortionRenderer>(FindObjectsSortMode.None).Length;
        EditorGUILayout.LabelField($"Sprites with distortion: {count}");
    }

    void ApplyToScene()
    {
        SpriteRenderer[] allSprites = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        int applied = 0;

        Undo.SetCurrentGroupName("Apply Environment Distortion");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (var sprite in allSprites)
        {
            if (TryApplyToSprite(sprite))
                applied++;
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"EnvironmentDistortionSetup: Applied to {applied} sprites in scene.");
    }

    void ApplyToSelection()
    {
        int applied = 0;

        Undo.SetCurrentGroupName("Apply Environment Distortion to Selection");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (var obj in Selection.gameObjects)
        {
            // Get sprite renderers in selection and children
            SpriteRenderer[] sprites = obj.GetComponentsInChildren<SpriteRenderer>();
            foreach (var sprite in sprites)
            {
                if (TryApplyToSprite(sprite))
                    applied++;
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"EnvironmentDistortionSetup: Applied to {applied} sprites in selection.");
    }

    bool TryApplyToSprite(SpriteRenderer sprite)
    {
        string sortingLayerName = sprite.sortingLayerName;

        // Find the index for this sorting layer
        int layerIndex = -1;
        for (int i = 0; i < sortingLayerNames.Length; i++)
        {
            if (sortingLayerNames[i] == sortingLayerName)
            {
                layerIndex = i;
                break;
            }
        }

        if (layerIndex < 0 || !applyToLayer[layerIndex])
            return false;

        // Check if already has component
        EnvironmentDistortionRenderer existing = sprite.GetComponent<EnvironmentDistortionRenderer>();
        if (existing != null)
        {
            // Update existing
            Undo.RecordObject(existing, "Update Environment Distortion");
            existing.environmentLayer = layerMapping[sortingLayerName];
            return true;
        }

        // Add new component
        EnvironmentDistortionRenderer renderer = Undo.AddComponent<EnvironmentDistortionRenderer>(sprite.gameObject);
        renderer.environmentLayer = layerMapping[sortingLayerName];
        return true;
    }

    void RemoveAllComponents()
    {
        EnvironmentDistortionRenderer[] all = Object.FindObjectsByType<EnvironmentDistortionRenderer>(FindObjectsSortMode.None);

        Undo.SetCurrentGroupName("Remove All Environment Distortion");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (var comp in all)
        {
            Undo.DestroyObjectImmediate(comp);
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"EnvironmentDistortionSetup: Removed {all.Length} components.");
    }
}
