using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CLAY.Surface
{
    /// <summary>
    /// The "SurfaceSSAO" renderer feature on GalaxyRenderer3D (inactive by default so the galaxy/system views never pay
    /// for it). Screens that own the camera — the planet surface, the Terrain Lab — switch it on and off. Uses the Depth
    /// source (normals reconstructed from depth) because the custom terrain/flora/grass shaders have DepthOnly passes
    /// but no DepthNormals pass.
    /// </summary>
    public static class SSAO
    {
        const string FeatureName = "SurfaceSSAO";

        public static ScriptableRendererFeature Find()
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp == null) return null;
            foreach (var d in urp.rendererDataList)
                if (d is UniversalRendererData ud)
                    foreach (var f in ud.rendererFeatures)
                        if (f != null && f.name == FeatureName) return f;
            Debug.LogWarning("[Surface] SSAO feature 'SurfaceSSAO' not found on the renderer assets");
            return null;
        }

        public static void SetActive(ScriptableRendererFeature f, bool on) { if (f != null && f.isActive != on) f.SetActive(on); }

        // settings are internal to URP: reach Intensity through reflection (cached)
        static System.Reflection.FieldInfo settingsField, intensityField;
        static float lastIntensity = -1f;
        public static void SetIntensity(ScriptableRendererFeature f, float v)
        {
            if (f == null || Mathf.Abs(v - lastIntensity) < 0.02f) return;
            const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            settingsField ??= f.GetType().GetField("m_Settings", BF);
            var settings = settingsField?.GetValue(f);
            if (settings == null) return;
            intensityField ??= settings.GetType().GetField("Intensity", BF | System.Reflection.BindingFlags.Public);
            intensityField?.SetValue(settings, v);
            lastIntensity = v;
        }
    }
}
