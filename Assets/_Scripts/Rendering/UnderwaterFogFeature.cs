using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature for underwater fog/murkiness effect.
/// Creates distance-based visibility falloff and depth coloring.
/// </summary>
public class UnderwaterFogFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public bool enabled = true;
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Header("Fog Color")]
        public Color nearColor = new Color(0.15f, 0.35f, 0.35f, 1f);
        public Color farColor = new Color(0.02f, 0.08f, 0.12f, 1f);

        [Header("Distance")]
        [Range(1f, 100f)]
        public float fogStart = 5f;
        [Range(5f, 200f)]
        public float fogEnd = 40f;

        [Header("Density")]
        [Range(0f, 1f)]
        public float density = 0.5f;
        [Range(0f, 2f)]
        public float noiseStrength = 0.3f;
        public float noiseScale = 2f;
        public float noiseSpeed = 0.1f;

        [Header("Light Shafts")]
        [Range(0f, 1f)]
        public float lightShaftIntensity = 0.2f;
        public Vector2 lightDirection = new Vector2(0.3f, -0.8f);
    }

    public Settings settings = new Settings();
    private UnderwaterFogPass fogPass;
    private Material fogMaterial;

    public override void Create()
    {
        Shader shader = Shader.Find("Hidden/UnderwaterFog");
        if (shader == null)
        {
            // Shader doesn't exist yet, we'll create it
            return;
        }

        fogMaterial = CoreUtils.CreateEngineMaterial(shader);
        fogPass = new UnderwaterFogPass(fogMaterial, settings);
        fogPass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!settings.enabled || fogMaterial == null || fogPass == null)
            return;

        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        fogPass.Setup(settings);
        renderer.EnqueuePass(fogPass);
    }

    protected override void Dispose(bool disposing)
    {
        if (fogMaterial != null)
        {
            CoreUtils.Destroy(fogMaterial);
            fogMaterial = null;
        }
    }
}

public class UnderwaterFogPass : ScriptableRenderPass
{
    private Material material;
    private UnderwaterFogFeature.Settings settings;
    private RTHandle tempRT;

    private static readonly int NearColorID = Shader.PropertyToID("_NearColor");
    private static readonly int FarColorID = Shader.PropertyToID("_FarColor");
    private static readonly int FogStartID = Shader.PropertyToID("_FogStart");
    private static readonly int FogEndID = Shader.PropertyToID("_FogEnd");
    private static readonly int DensityID = Shader.PropertyToID("_Density");
    private static readonly int NoiseStrengthID = Shader.PropertyToID("_NoiseStrength");
    private static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseSpeedID = Shader.PropertyToID("_NoiseSpeed");
    private static readonly int LightShaftIntensityID = Shader.PropertyToID("_LightShaftIntensity");
    private static readonly int LightDirectionID = Shader.PropertyToID("_LightDirection");
    private static readonly int TimeID = Shader.PropertyToID("_Time");

    public UnderwaterFogPass(Material material, UnderwaterFogFeature.Settings settings)
    {
        this.material = material;
        this.settings = settings;
        profilingSampler = new ProfilingSampler("UnderwaterFog");
    }

    public void Setup(UnderwaterFogFeature.Settings settings)
    {
        this.settings = settings;
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        RenderingUtils.ReAllocateIfNeeded(ref tempRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_UnderwaterFogTemp");
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (material == null) return;

        CommandBuffer cmd = CommandBufferPool.Get("UnderwaterFog");

        material.SetColor(NearColorID, settings.nearColor);
        material.SetColor(FarColorID, settings.farColor);
        material.SetFloat(FogStartID, settings.fogStart);
        material.SetFloat(FogEndID, settings.fogEnd);
        material.SetFloat(DensityID, settings.density);
        material.SetFloat(NoiseStrengthID, settings.noiseStrength);
        material.SetFloat(NoiseScaleID, settings.noiseScale);
        material.SetFloat(NoiseSpeedID, settings.noiseSpeed);
        material.SetFloat(LightShaftIntensityID, settings.lightShaftIntensity);
        material.SetVector(LightDirectionID, settings.lightDirection.normalized);
        material.SetFloat(TimeID, Time.time);

        RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

        Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempRT, material, 0);
        Blitter.BlitCameraTexture(cmd, tempRT, cameraColorTarget);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
    }
}
