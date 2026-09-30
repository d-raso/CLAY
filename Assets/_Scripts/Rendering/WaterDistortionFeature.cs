using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP ScriptableRendererFeature — full-screen water distortion effect.
/// Uses Hidden/FullScreenDistortion shader (legacy CG blit-style).
///
/// SETUP: Select your URP Renderer Data asset → Add Renderer Feature → Water Distortion Feature.
/// Settings are driven at runtime by WaterDistortionCamera on the Main Camera.
/// </summary>
public class WaterDistortionFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public bool  enabled             = true;
        [Range(0f,0.2f)]  public float distortionStrength  = 0.08f;
        [Range(0f,0.05f)] public float chromaticAberration = 0.012f;
        [Range(0.1f,5f)]  public float noiseSpeed          = 0.9f;
        [Range(0.5f,15f)] public float noiseScale          = 5f;
        public bool debugMode = false;
    }

    public Settings settings = new Settings();

    WaterDistortionPass pass;
    Material            mat;

    public override void Create()
    {
        var shader = Shader.Find("Hidden/FullScreenDistortion");
        if (!shader) { Debug.LogError("[WaterDistFeat] Hidden/FullScreenDistortion shader not found!"); return; }
        mat  = CoreUtils.CreateEngineMaterial(shader);
        pass = new WaterDistortionPass(mat, settings);
        Debug.Log("[WaterDistFeat] Ready.");
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData rd)
    {
        if (!settings.enabled || mat == null || pass == null) return;
        if (rd.cameraData.cameraType != CameraType.Game) return;
        pass.ConfigureInput(ScriptableRenderPassInput.Color);
        pass.SetTarget(renderer.cameraColorTargetHandle);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData rd)
    {
        if (!settings.enabled || mat == null || pass == null) return;
        if (rd.cameraData.cameraType != CameraType.Game) return;
        pass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        pass.settings = settings;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool d) { if (mat) CoreUtils.Destroy(mat); }

    public void SetDistortionStrength(float s) => settings.distortionStrength = Mathf.Clamp(s,0,0.2f);
    public void SetEnabled(bool on)            => settings.enabled = on;
}

public class WaterDistortionPass : ScriptableRenderPass
{
    public WaterDistortionFeature.Settings settings;
    Material   mat;
    RTHandle   target;

    static readonly int sStr  = Shader.PropertyToID("_DistortionStrength");
    static readonly int sCa   = Shader.PropertyToID("_ChromaticAberration");
    static readonly int sSp   = Shader.PropertyToID("_NoiseSpeed");
    static readonly int sSc   = Shader.PropertyToID("_NoiseScale");
    static readonly int sTi   = Shader.PropertyToID("_EffectTime");
    static readonly int sDb   = Shader.PropertyToID("_DebugMode");
    static readonly int sFlo  = Shader.PropertyToID("_GlobalFlowDirection");
    static readonly int sTmp  = Shader.PropertyToID("_WaterTemperature");
    static readonly int sTurb = Shader.PropertyToID("_WaterTurbulence");
    static readonly int sR0   = Shader.PropertyToID("_Ripple0");
    static readonly int sR1   = Shader.PropertyToID("_Ripple1");
    static readonly int sR2   = Shader.PropertyToID("_Ripple2");
    static readonly int sR3   = Shader.PropertyToID("_Ripple3");
    static readonly int sTmpRT= Shader.PropertyToID("_WaterDistTmp");

    public WaterDistortionPass(Material m, WaterDistortionFeature.Settings s)
    {
        mat = m; settings = s;
        profilingSampler = new ProfilingSampler("WaterDistortion");
    }

    public void SetTarget(RTHandle h) { target = h; }

    public override void Execute(ScriptableRenderContext ctx, ref RenderingData rd)
    {
        if (mat == null) return;
        var ct = target ?? rd.cameraData.renderer.cameraColorTargetHandle;
        if (ct == null || ct.rt == null) return;

        // Push all properties to the material
        mat.SetFloat(sStr,  settings.distortionStrength);
        mat.SetFloat(sCa,   settings.chromaticAberration);
        mat.SetFloat(sSp,   settings.noiseSpeed);
        mat.SetFloat(sSc,   settings.noiseScale);
        mat.SetFloat(sTi,   Time.time);
        mat.SetFloat(sDb,   settings.debugMode ? 1f : 0f);
        // Environment globals (set by FlowFieldManager, PlanetaryEnvironment, CellRippleSystem)
        mat.SetVector(sFlo, Shader.GetGlobalVector("_GlobalFlowDirection"));
        mat.SetFloat(sTmp,  Shader.GetGlobalFloat("_WaterTemperature"));
        mat.SetFloat(sTurb, Shader.GetGlobalFloat("_WaterTurbulence"));
        mat.SetVector(sR0,  Shader.GetGlobalVector("_Ripple0"));
        mat.SetVector(sR1,  Shader.GetGlobalVector("_Ripple1"));
        mat.SetVector(sR2,  Shader.GetGlobalVector("_Ripple2"));
        mat.SetVector(sR3,  Shader.GetGlobalVector("_Ripple3"));

        var cmd  = CommandBufferPool.Get("WaterDistortion");
        var desc = rd.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        cmd.GetTemporaryRT(sTmpRT, desc);
        cmd.Blit(ct.rt, sTmpRT, mat, 0);
        cmd.Blit(sTmpRT, ct.rt);
        cmd.ReleaseTemporaryRT(sTmpRT);
        ctx.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}
