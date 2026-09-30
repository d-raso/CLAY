using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Scriptable Renderer Feature for GPU-accelerated dynamic underwater lighting.
/// Includes Voronoi-based caustics and optional volumetric god rays.
/// </summary>
public class DynamicLightingFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public bool enabled = true;
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Header("Caustics")]
        [Range(0f, 1f)]
        public float causticsIntensity = 0.3f;
        public Color causticsColor = new Color(0.8f, 0.95f, 1f, 1f);
        public float causticsScale1 = 5f;
        public float causticsScale2 = 7f;
        public float causticsSpeed = 0.5f;
        [Range(1f, 10f)]
        public float causticsSharpness = 3f;

        [Header("God Rays")]
        public bool godRaysEnabled = false;
        [Range(0f, 1f)]
        public float godRaysIntensity = 0.15f;
        public Color godRaysColor = new Color(1f, 0.98f, 0.9f, 1f);
        public Vector2 lightSourcePosition = new Vector2(0.5f, 1f);
        [Range(8, 32)]
        public int godRaysSamples = 16;
        [Range(0.9f, 1f)]
        public float godRaysDecay = 0.96f;
        [Range(0.1f, 2f)]
        public float godRaysDensity = 0.5f;

        [Header("Flow Field")]
        [Range(0f, 1f)]
        public float flowFieldInfluence = 0.3f;
    }

    public Settings settings = new Settings();

    private DynamicLightingPass lightingPass;
    private Material lightingMaterial;

    public override void Create()
    {
        Shader shader = Shader.Find("Hidden/DynamicLighting");
        if (shader == null)
        {
            Debug.LogWarning("DynamicLightingFeature: Could not find 'Hidden/DynamicLighting' shader.");
            return;
        }

        lightingMaterial = CoreUtils.CreateEngineMaterial(shader);
        lightingPass = new DynamicLightingPass(lightingMaterial, settings);
        lightingPass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!settings.enabled || lightingMaterial == null || lightingPass == null)
            return;

        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        // Skip if caustics are disabled
        if (settings.causticsIntensity <= 0.001f && !settings.godRaysEnabled)
            return;

        lightingPass.Setup(settings);
        renderer.EnqueuePass(lightingPass);
    }

    protected override void Dispose(bool disposing)
    {
        if (lightingMaterial != null)
        {
            CoreUtils.Destroy(lightingMaterial);
            lightingMaterial = null;
        }
    }

    /// <summary>
    /// Set caustics intensity at runtime
    /// </summary>
    public void SetCausticsIntensity(float intensity)
    {
        settings.causticsIntensity = Mathf.Clamp01(intensity);
    }

    /// <summary>
    /// Set caustics color at runtime
    /// </summary>
    public void SetCausticsColor(Color color)
    {
        settings.causticsColor = color;
    }

    /// <summary>
    /// Enable/disable god rays at runtime
    /// </summary>
    public void SetGodRaysEnabled(bool enabled)
    {
        settings.godRaysEnabled = enabled;
    }

    /// <summary>
    /// Set god rays intensity at runtime
    /// </summary>
    public void SetGodRaysIntensity(float intensity)
    {
        settings.godRaysIntensity = Mathf.Clamp01(intensity);
    }
}

/// <summary>
/// Render pass for dynamic underwater lighting
/// </summary>
public class DynamicLightingPass : ScriptableRenderPass
{
    private Material material;
    private DynamicLightingFeature.Settings settings;

    // Shader property IDs
    private static readonly int CausticsIntensityID = Shader.PropertyToID("_CausticsIntensity");
    private static readonly int CausticsColorID = Shader.PropertyToID("_CausticsColor");
    private static readonly int CausticsScale1ID = Shader.PropertyToID("_CausticsScale1");
    private static readonly int CausticsScale2ID = Shader.PropertyToID("_CausticsScale2");
    private static readonly int CausticsSpeedID = Shader.PropertyToID("_CausticsSpeed");
    private static readonly int CausticsSharpnessID = Shader.PropertyToID("_CausticsSharpness");
    private static readonly int GodRaysEnabledID = Shader.PropertyToID("_GodRaysEnabled");
    private static readonly int GodRaysIntensityID = Shader.PropertyToID("_GodRaysIntensity");
    private static readonly int GodRaysColorID = Shader.PropertyToID("_GodRaysColor");
    private static readonly int LightSourcePosID = Shader.PropertyToID("_LightSourcePos");
    private static readonly int GodRaysSamplesID = Shader.PropertyToID("_GodRaysSamples");
    private static readonly int GodRaysDecayID = Shader.PropertyToID("_GodRaysDecay");
    private static readonly int GodRaysDensityID = Shader.PropertyToID("_GodRaysDensity");
    private static readonly int FlowFieldInfluenceID = Shader.PropertyToID("_FlowFieldInfluence");
    private static readonly int TimeID = Shader.PropertyToID("_EffectTime");

    public DynamicLightingPass(Material material, DynamicLightingFeature.Settings settings)
    {
        this.material = material;
        this.settings = settings;
        profilingSampler = new ProfilingSampler("DynamicLighting");
    }

    public void Setup(DynamicLightingFeature.Settings settings)
    {
        this.settings = settings;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (material == null)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("DynamicLighting");

        // Update material properties - Caustics
        material.SetFloat(CausticsIntensityID, settings.causticsIntensity);
        material.SetColor(CausticsColorID, settings.causticsColor);
        material.SetFloat(CausticsScale1ID, settings.causticsScale1);
        material.SetFloat(CausticsScale2ID, settings.causticsScale2);
        material.SetFloat(CausticsSpeedID, settings.causticsSpeed);
        material.SetFloat(CausticsSharpnessID, settings.causticsSharpness);

        // God rays
        material.SetFloat(GodRaysEnabledID, settings.godRaysEnabled ? 1f : 0f);
        material.SetFloat(GodRaysIntensityID, settings.godRaysIntensity);
        material.SetColor(GodRaysColorID, settings.godRaysColor);
        material.SetVector(LightSourcePosID, settings.lightSourcePosition);
        material.SetInt(GodRaysSamplesID, settings.godRaysSamples);
        material.SetFloat(GodRaysDecayID, settings.godRaysDecay);
        material.SetFloat(GodRaysDensityID, settings.godRaysDensity);

        // Flow field and time
        material.SetFloat(FlowFieldInfluenceID, settings.flowFieldInfluence);
        material.SetFloat(TimeID, Time.time);

        // Enable god rays keyword
        if (settings.godRaysEnabled)
            material.EnableKeyword("_GOD_RAYS_ON");
        else
            material.DisableKeyword("_GOD_RAYS_ON");

        // Get camera color target
        RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

        // Draw fullscreen with additive blending
        Blitter.BlitCameraTexture(cmd, cameraColorTarget, cameraColorTarget, material, 0);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
