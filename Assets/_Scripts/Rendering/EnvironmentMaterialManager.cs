using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages shared materials for environment layers that use the EnvironmentDistortion shader.
/// Provides layer-based materials for procedural background generation.
/// Works with ParallaxManager and ProceduralBackgroundGenerator.
/// </summary>
public class EnvironmentMaterialManager : MonoBehaviour
{
    public static EnvironmentMaterialManager Instance { get; private set; }

    [System.Serializable]
    public class LayerMaterialSettings
    {
        public string layerName;
        [Range(0f, 0.15f)]
        public float distortionStrength = 0.03f;
        [Range(0f, 2f)]
        public float flowInfluence = 0.5f;
        [Range(0f, 1f)]
        public float tintStrength = 0.2f;
        public Color underwaterTint = new Color(0.7f, 0.85f, 1f, 1f);

        [HideInInspector]
        public Material material;
    }

    [Header("Layer Material Settings")]
    [Tooltip("Configure distortion per parallax layer")]
    public LayerMaterialSettings[] layerSettings = new LayerMaterialSettings[]
    {
        new LayerMaterialSettings { layerName = "FarBackground",  distortionStrength = 0.06f, flowInfluence = 0.8f, tintStrength = 0.4f },
        new LayerMaterialSettings { layerName = "MidBackground",  distortionStrength = 0.04f, flowInfluence = 0.6f, tintStrength = 0.3f },
        new LayerMaterialSettings { layerName = "NearBackground", distortionStrength = 0.025f, flowInfluence = 0.4f, tintStrength = 0.2f },
        new LayerMaterialSettings { layerName = "PlayArea",       distortionStrength = 0f,    flowInfluence = 0f,   tintStrength = 0f },  // No distortion for play area
        new LayerMaterialSettings { layerName = "Foreground",     distortionStrength = 0.015f, flowInfluence = 0.3f, tintStrength = 0.1f },
        new LayerMaterialSettings { layerName = "NearForeground", distortionStrength = 0.008f, flowInfluence = 0.2f, tintStrength = 0.05f },
    };

    [Header("Shader Settings")]
    [Range(0.1f, 3f)]
    public float distortionSpeed = 0.5f;
    [Range(1f, 15f)]
    public float distortionScale = 4f;

    [Header("References")]
    public Shader environmentShader;

    // Material cache by layer name
    private Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
    private Dictionary<int, Material> materialCacheByIndex = new Dictionary<int, Material>();

    // Shader property IDs
    private static readonly int DistortionStrengthID = Shader.PropertyToID("_DistortionStrength");
    private static readonly int DistortionSpeedID = Shader.PropertyToID("_DistortionSpeed");
    private static readonly int DistortionScaleID = Shader.PropertyToID("_DistortionScale");
    private static readonly int FlowInfluenceID = Shader.PropertyToID("_FlowInfluence");
    private static readonly int UnderwaterTintID = Shader.PropertyToID("_UnderwaterTint");
    private static readonly int TintStrengthID = Shader.PropertyToID("_TintStrength");
    private static readonly int DistortionLayerID = Shader.PropertyToID("_DistortionLayer");

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        InitializeMaterials();
    }

    void InitializeMaterials()
    {
        // Find shader if not assigned
        if (environmentShader == null)
        {
            environmentShader = Shader.Find("Custom/EnvironmentDistortion");
            if (environmentShader == null)
            {
                Debug.LogError("EnvironmentMaterialManager: Could not find 'Custom/EnvironmentDistortion' shader!");
                return;
            }
        }

        // Create materials for each layer
        for (int i = 0; i < layerSettings.Length; i++)
        {
            var settings = layerSettings[i];
            Material mat = new Material(environmentShader);
            mat.name = $"EnvDistortion_{settings.layerName}";

            // Configure material
            mat.SetFloat(DistortionStrengthID, settings.distortionStrength);
            mat.SetFloat(DistortionSpeedID, distortionSpeed);
            mat.SetFloat(DistortionScaleID, distortionScale);
            mat.SetFloat(FlowInfluenceID, settings.flowInfluence);
            mat.SetColor(UnderwaterTintID, settings.underwaterTint);
            mat.SetFloat(TintStrengthID, settings.tintStrength);

            // Set layer value for depth-based effects (0=far, 2=near)
            float layerValue = Mathf.Clamp((float)i / (layerSettings.Length - 1) * 2f, 0f, 2f);
            mat.SetFloat(DistortionLayerID, layerValue);

            settings.material = mat;
            materialCache[settings.layerName] = mat;
            materialCacheByIndex[i] = mat;
        }

        Debug.Log($"EnvironmentMaterialManager: Created {layerSettings.Length} layer materials");
    }

    /// <summary>
    /// Get the distortion material for a layer by name
    /// </summary>
    public Material GetMaterial(string layerName)
    {
        if (materialCache.TryGetValue(layerName, out Material mat))
            return mat;

        Debug.LogWarning($"EnvironmentMaterialManager: No material for layer '{layerName}'");
        return null;
    }

    /// <summary>
    /// Get the distortion material for a layer by index
    /// </summary>
    public Material GetMaterial(int layerIndex)
    {
        if (materialCacheByIndex.TryGetValue(layerIndex, out Material mat))
            return mat;

        Debug.LogWarning($"EnvironmentMaterialManager: No material for layer index {layerIndex}");
        return null;
    }

    /// <summary>
    /// Apply the appropriate distortion material to a SpriteRenderer based on layer
    /// </summary>
    public void ApplyToSprite(SpriteRenderer sr, string layerName)
    {
        Material mat = GetMaterial(layerName);
        if (mat != null)
        {
            sr.material = mat;
        }
    }

    /// <summary>
    /// Apply the appropriate distortion material to a SpriteRenderer based on layer index
    /// </summary>
    public void ApplyToSprite(SpriteRenderer sr, int layerIndex)
    {
        Material mat = GetMaterial(layerIndex);
        if (mat != null)
        {
            sr.material = mat;
        }
    }

    /// <summary>
    /// Update all materials with new global settings
    /// </summary>
    public void UpdateGlobalSettings()
    {
        foreach (var settings in layerSettings)
        {
            if (settings.material != null)
            {
                settings.material.SetFloat(DistortionSpeedID, distortionSpeed);
                settings.material.SetFloat(DistortionScaleID, distortionScale);
            }
        }
    }

    /// <summary>
    /// Check if a layer should have distortion (PlayArea typically doesn't)
    /// </summary>
    public bool LayerHasDistortion(int layerIndex)
    {
        if (layerIndex >= 0 && layerIndex < layerSettings.Length)
        {
            return layerSettings[layerIndex].distortionStrength > 0.001f;
        }
        return false;
    }

    void OnDestroy()
    {
        // Clean up materials
        foreach (var settings in layerSettings)
        {
            if (settings.material != null)
            {
                if (Application.isPlaying)
                    Destroy(settings.material);
                else
                    DestroyImmediate(settings.material);
            }
        }
        materialCache.Clear();
        materialCacheByIndex.Clear();

        if (Instance == this)
            Instance = null;
    }

    void OnValidate()
    {
        // Update materials when settings change in editor
        if (Application.isPlaying)
        {
            for (int i = 0; i < layerSettings.Length; i++)
            {
                var settings = layerSettings[i];
                if (settings.material != null)
                {
                    settings.material.SetFloat(DistortionStrengthID, settings.distortionStrength);
                    settings.material.SetFloat(FlowInfluenceID, settings.flowInfluence);
                    settings.material.SetColor(UnderwaterTintID, settings.underwaterTint);
                    settings.material.SetFloat(TintStrengthID, settings.tintStrength);
                }
            }
            UpdateGlobalSettings();
        }
    }
}
