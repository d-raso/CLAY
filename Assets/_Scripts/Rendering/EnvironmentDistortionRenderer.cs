using UnityEngine;

/// <summary>
/// Applies the EnvironmentDistortion shader to a SpriteRenderer.
/// Attach to any environment sprite that should have water distortion effects.
/// Automatically sets up the material and configures layer-based distortion.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[ExecuteAlways]
public class EnvironmentDistortionRenderer : MonoBehaviour
{
    public enum EnvironmentLayer
    {
        FarBackground = 0,  // Strongest distortion, most tint
        Background = 1,
        Midground = 2,
        Foreground = 3,     // Least distortion, least tint
    }

    [Header("Layer Settings")]
    [Tooltip("Determines distortion intensity and tint. Far background = most effect, Foreground = least")]
    public EnvironmentLayer environmentLayer = EnvironmentLayer.Background;

    [Header("Distortion Override")]
    [Tooltip("Override the default distortion for this layer")]
    public bool overrideDistortion = false;
    [Range(0f, 0.15f)]
    public float distortionStrength = 0.03f;

    [Header("Flow Field")]
    [Range(0f, 2f)]
    public float flowInfluence = 0.5f;

    [Header("Tint Override")]
    [Tooltip("Override the underwater tint")]
    public bool overrideTint = false;
    public Color underwaterTint = new Color(0.7f, 0.85f, 1f, 1f);
    [Range(0f, 1f)]
    public float tintStrength = 0.15f;

    private SpriteRenderer spriteRenderer;
    private Material materialInstance;
    private static Shader environmentShader;

    // Shader property IDs
    private static readonly int DistortionStrengthID = Shader.PropertyToID("_DistortionStrength");
    private static readonly int DistortionLayerID = Shader.PropertyToID("_DistortionLayer");
    private static readonly int FlowInfluenceID = Shader.PropertyToID("_FlowInfluence");
    private static readonly int UnderwaterTintID = Shader.PropertyToID("_UnderwaterTint");
    private static readonly int TintStrengthID = Shader.PropertyToID("_TintStrength");

    // Layer presets
    private static readonly float[] LayerDistortion = { 0.05f, 0.035f, 0.02f, 0.01f };
    private static readonly float[] LayerTintStrength = { 0.35f, 0.25f, 0.15f, 0.08f };
    private static readonly float[] LayerDistortionValue = { 0f, 0.5f, 1.2f, 2f };

    void OnEnable()
    {
        SetupMaterial();
    }

    void OnDisable()
    {
        CleanupMaterial();
    }

    void OnValidate()
    {
        if (materialInstance != null)
        {
            UpdateMaterialProperties();
        }
    }

    void SetupMaterial()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) return;

        // Cache shader reference
        if (environmentShader == null)
        {
            environmentShader = Shader.Find("Custom/EnvironmentDistortion");
            if (environmentShader == null)
            {
                Debug.LogError("EnvironmentDistortionRenderer: Could not find 'Custom/EnvironmentDistortion' shader!");
                return;
            }
        }

        // Create material instance
        materialInstance = new Material(environmentShader);

        // Copy sprite texture
        if (spriteRenderer.sprite != null)
        {
            materialInstance.mainTexture = spriteRenderer.sprite.texture;
        }

        // Apply to sprite renderer
        spriteRenderer.material = materialInstance;

        UpdateMaterialProperties();
    }

    void CleanupMaterial()
    {
        if (materialInstance != null)
        {
            if (Application.isPlaying)
                Destroy(materialInstance);
            else
                DestroyImmediate(materialInstance);
            materialInstance = null;
        }
    }

    void UpdateMaterialProperties()
    {
        if (materialInstance == null) return;

        int layerIndex = (int)environmentLayer;

        // Set distortion based on layer or override
        float distortion = overrideDistortion ? distortionStrength : LayerDistortion[layerIndex];
        materialInstance.SetFloat(DistortionStrengthID, distortion);

        // Set layer value for shader
        materialInstance.SetFloat(DistortionLayerID, LayerDistortionValue[layerIndex]);

        // Set flow influence
        materialInstance.SetFloat(FlowInfluenceID, flowInfluence);

        // Set tint based on layer or override
        if (overrideTint)
        {
            materialInstance.SetColor(UnderwaterTintID, underwaterTint);
            materialInstance.SetFloat(TintStrengthID, tintStrength);
        }
        else
        {
            materialInstance.SetFloat(TintStrengthID, LayerTintStrength[layerIndex]);
        }
    }

    /// <summary>
    /// Update material at runtime (call after changing properties via script)
    /// </summary>
    public void RefreshMaterial()
    {
        UpdateMaterialProperties();
    }

    /// <summary>
    /// Set the environment layer at runtime
    /// </summary>
    public void SetLayer(EnvironmentLayer layer)
    {
        environmentLayer = layer;
        UpdateMaterialProperties();
    }
}
