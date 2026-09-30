using UnityEngine;

/// <summary>
/// Manages quality settings for the advanced underwater visual effects.
/// Allows runtime adjustment of flow field resolution, distortion layers, and god rays.
/// </summary>
public class VisualEffectsQualityManager : MonoBehaviour
{
    public static VisualEffectsQualityManager Instance { get; private set; }

    public enum QualityLevel
    {
        Low,    // 32x32 flow, 1 distortion layer, no god rays
        Medium, // 64x64 flow, 2 distortion layers, no god rays
        High    // 128x128 flow, 3 distortion layers, god rays enabled
    }

    [Header("Current Quality")]
    public QualityLevel currentQuality = QualityLevel.Medium;

    [Header("References")]
    public FlowFieldManager flowFieldManager;

    // Cached render feature references (set manually or via FindObjectOfType)
    private WaterDistortionFeature waterDistortion;
    private DynamicLightingFeature dynamicLighting;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        if (flowFieldManager == null)
        {
            flowFieldManager = FindFirstObjectByType<FlowFieldManager>();
        }

        ApplyQualitySettings(currentQuality);
    }

    /// <summary>
    /// Apply quality settings to all visual effects systems
    /// </summary>
    public void ApplyQualitySettings(QualityLevel quality)
    {
        currentQuality = quality;

        // Flow Field Quality
        if (flowFieldManager != null)
        {
            var flowQuality = quality switch
            {
                QualityLevel.Low => FlowFieldManager.QualityLevel.Low,
                QualityLevel.Medium => FlowFieldManager.QualityLevel.Medium,
                QualityLevel.High => FlowFieldManager.QualityLevel.High,
                _ => FlowFieldManager.QualityLevel.Medium
            };
            flowFieldManager.SetQuality(flowQuality);
        }

        // Water Distortion (if we have a reference to the feature settings)
        // Note: ScriptableRendererFeatures are typically configured in the editor,
        // but we can expose settings via a static reference pattern if needed
        ApplyDistortionQuality(quality);

        // Dynamic Lighting
        ApplyLightingQuality(quality);

        Debug.Log($"VisualEffectsQualityManager: Applied {quality} quality settings");
    }

    void ApplyDistortionQuality(QualityLevel quality)
    {
        // These would typically be set via the render feature settings
        // For runtime control, you'd need to expose the feature via a static reference
        int layerCount = quality switch
        {
            QualityLevel.Low => 1,
            QualityLevel.Medium => 2,
            QualityLevel.High => 3,
            _ => 2
        };

        float distortionStrength = quality switch
        {
            QualityLevel.Low => 0.01f,
            QualityLevel.Medium => 0.02f,
            QualityLevel.High => 0.025f,
            _ => 0.02f
        };

        // Set global shader properties that the distortion shader can read
        Shader.SetGlobalInt("_GlobalDistortionLayers", layerCount);
        Shader.SetGlobalFloat("_GlobalDistortionStrength", distortionStrength);
    }

    void ApplyLightingQuality(QualityLevel quality)
    {
        bool godRaysEnabled = quality == QualityLevel.High;

        int godRaysSamples = quality switch
        {
            QualityLevel.Low => 8,
            QualityLevel.Medium => 16,
            QualityLevel.High => 24,
            _ => 16
        };

        // Set global shader properties
        Shader.SetGlobalFloat("_GlobalGodRaysEnabled", godRaysEnabled ? 1f : 0f);
        Shader.SetGlobalInt("_GlobalGodRaysSamples", godRaysSamples);
    }

    /// <summary>
    /// Cycle through quality levels (useful for testing/keybinds)
    /// </summary>
    public void CycleQuality()
    {
        currentQuality = currentQuality switch
        {
            QualityLevel.Low => QualityLevel.Medium,
            QualityLevel.Medium => QualityLevel.High,
            QualityLevel.High => QualityLevel.Low,
            _ => QualityLevel.Medium
        };
        ApplyQualitySettings(currentQuality);
    }

    /// <summary>
    /// Set quality by index (0=Low, 1=Medium, 2=High)
    /// </summary>
    public void SetQualityByIndex(int index)
    {
        currentQuality = index switch
        {
            0 => QualityLevel.Low,
            1 => QualityLevel.Medium,
            2 => QualityLevel.High,
            _ => QualityLevel.Medium
        };
        ApplyQualitySettings(currentQuality);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
