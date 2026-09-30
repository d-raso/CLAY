using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CLAY
{
    /// <summary>
    /// Global temporal-upscaling controller. Configures the active URP asset to render internally at a reduced
    /// resolution and upscale it with STP (Spatiotemporal Post-processing) — Unity's built-in, hardware-agnostic
    /// temporal upscaler that runs on compute shaders (works on Turing / GTX 1660 Ti; no RT or Tensor cores needed).
    ///
    /// Because it edits the URP PIPELINE ASSET, the setting applies to EVERY camera in EVERY scene automatically —
    /// galaxy, system viewer, flora lab, UI-world cameras, all of it. Put one of these on a bootstrap object.
    ///
    /// Quality presets set the internal render scale; STP reconstructs a crisp full-resolution image from the
    /// low-res color + depth + motion vectors each frame. For anything that MOVES with a custom (non-URP-Lit)
    /// shader, add a "MotionVectors" pass to that shader or it will smear — see the note at the bottom of the file.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GraphicsUpscaleController : MonoBehaviour
    {
        public enum Quality
        {
            Performance,   // 0.50x  (best FPS)
            Balanced,      // 0.60x
            Quality,       // 0.75x
            Native         // 1.00x  (no upscale — STP still does temporal AA)
        }

        [Tooltip("Applied at startup and whenever changed at runtime.")]
        public Quality quality = Quality.Balanced;

        [Tooltip("Keep this object (and the setting) alive across scene loads.")]
        public bool persistAcrossScenes = true;

        static GraphicsUpscaleController _instance;

        // Auto-apply at game start so upscaling is on globally without placing anything in a scene.
        // (Render scale is honored by both the 2D and 3D URP renderers; STP temporal reconstruction runs on the
        // 3D/Universal renderer and gracefully falls back to a spatial upscale on the 2D renderer.)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp == null) return;
            urp.renderScale = 0.70f;                     // a bit higher than "Balanced" to reduce pixelation; menu can lower it
            // FSR (spatial) — stateless, works on the 2D renderer, needs no motion vectors, reconstructs hard edges.
            // (STP is temporal/3D-only; on this project's 2D renderer it pixelated moving edges and its history
            //  broke when the Flora Lab toggled renderers. Revisit STP once motion vectors exist everywhere.)
            urp.upscalingFilter = UpscalingFilterSelection.FSR;
            urp.fsrSharpness = 0.92f;
            urp.msaaSampleCount = 4;   // anti-alias the low-res buffer BEFORE upscaling → clean opaque edges (cheap at 0.6x)
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            if (persistAcrossScenes) DontDestroyOnLoad(gameObject);
            Apply();
        }

        /// <summary>Runtime hook for a settings menu.</summary>
        public void SetQuality(Quality q) { quality = q; Apply(); }
        public void SetQuality(int q) => SetQuality((Quality)Mathf.Clamp(q, 0, 3));

        public void Apply()
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp == null) { Debug.LogWarning("[Upscale] Active render pipeline is not URP; upscaling not applied."); return; }

            float scale = quality switch
            {
                Quality.Performance => 0.50f,
                Quality.Balanced    => 0.60f,
                Quality.Quality     => 0.75f,
                _                   => 1.00f,
            };

            urp.renderScale = scale;
            // FSR (spatial): robust on the 2D renderer + BRG galaxy, no motion vectors / temporal state needed.
            urp.upscalingFilter = quality == Quality.Native ? UpscalingFilterSelection.Auto : UpscalingFilterSelection.FSR;
            urp.fsrSharpness = 0.92f;
            urp.msaaSampleCount = 4;   // anti-alias opaque edges before the upscale

            Debug.Log($"[Upscale] {quality} → renderScale {scale:0.00}, STP temporal upscaling (global).");
        }

        // ── MOTION VECTORS: the one thing to get right game-wide ──────────────────────────────────────────────
        // STP reprojects last frame using motion vectors. What's already covered vs. what you must add:
        //   • URP Lit / Unlit / Shader Graph materials  → motion vectors automatic (nothing to do).
        //   • STATIC objects with custom shaders         → covered by camera motion (from depth); no smear.
        //   • MOVING objects with CUSTOM CG/HLSL shaders → NOT covered; they smear. This includes CLAY's
        //     orbiting planets/giants/ocean/atmosphere/clouds (they move each frame) and any DrawMeshInstanced-
        //     Indirect draws (the galaxy stars). Add a "MotionVectors" LightMode pass to those shaders.
        //   • Also enable "Opaque Motion Vectors" on your UniversalRendererData asset(s) so the motion pass runs.
        //
        // Drop-in MotionVectors pass for a moving custom shader (per-object; uses Unity's prev-frame matrices):
        //
        //   Pass {
        //       Name "MotionVectors"
        //       Tags { "LightMode" = "MotionVectors" }
        //       HLSLPROGRAM
        //       #pragma vertex vert
        //       #pragma fragment frag
        //       #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        //       struct A { float4 posOS:POSITION; };
        //       struct V { float4 posCS:SV_POSITION; float4 cur:TEXCOORD0; float4 prev:TEXCOORD1; };
        //       V vert(A a){ V o;
        //           o.posCS = TransformObjectToHClip(a.posOS.xyz);                                   // jittered raster
        //           o.cur   = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M,          a.posOS)); // unjittered
        //           o.prev  = mul(_PrevViewProjMatrix,        mul(UNITY_MATRIX_PREV_M,     a.posOS)); // prev frame
        //           return o; }
        //       half4 frag(V i):SV_Target{
        //           float2 c=i.cur.xy/i.cur.w, p=i.prev.xy/i.prev.w; float2 v=(c-p)*0.5;
        //           #if UNITY_UV_STARTS_AT_TOP
        //               v.y=-v.y;
        //           #endif
        //           return half4(v,0,0); }
        //       ENDHLSL
        //   }
        //
        // For DrawMeshInstancedIndirect (stars), use the instanced variant from the earlier galaxy motion pass
        // (reads _StarPositions and _PrevVP), issued into the camera motion target from a ScriptableRenderPass.
    }
}
