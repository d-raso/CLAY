Shader "Clay/GalacticDust"
{
    // Interstellar dust: soft billboards that OCCLUDE and REDDEN the star field (alpha blend, not additive), so
    // dense fBm-carved lanes read as dark bands cutting through the arms. Per-instance colour+opacity come from
    // the StarColor component (rgb = dust tint, a = optical depth / opacity). Drawn AFTER the stars so it darkens
    // them. No proximity resolve — dust stays a soft cloud when you fly through it.
    Properties
    {
        _StarColor ("Dust Color (a=opacity)", Color) = (0.09, 0.05, 0.035, 1)
        _Softness  ("Edge Softness", Range(0.3, 3)) = 1.1
        _NearFade  ("Near Camera Fade Dist", Float) = 6
        // Mie back-scatter: dust between the camera and the galactic core glows amber; elsewhere it stays dark.
        _CoreGlow  ("Core Back-scatter Color", Color) = (1.0, 0.55, 0.25, 1)
        _CoreScatter ("Core Back-scatter Strength", Range(0, 3)) = 1.0
        _CoreGlowDist ("Core Glow Radius", Float) = 36
        _MieG      ("Mie Anisotropy", Range(0, 0.95)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "DustForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha   // occlude / redden the star field
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _StarColor;
                float _Softness;
                float _NearFade;
                float4 _CoreGlow;
                float _CoreScatter;
                float _CoreGlowDist;
                float _MieG;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _StarColor)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _StarColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _StarColor)
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float2 seed : TEXCOORD2;   // per-instance noise offset so no two clouds share a shape
                float nearFade : TEXCOORD3;   // fades dust out when the camera is very close (flythrough)
                float backlit : TEXCOORD4;    // Mie back-scatter from the galactic core (amber glow)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Cheap value-noise fBm so each billboard is an irregular wisp, not a smooth disc. Overlapping wisps
            // then read as tangled cloud/filament texture rather than a field of orbs.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float fbm(float2 p)
            {
                float v = 0.0, amp = 0.5;
                for (int i = 0; i < 5; i++) { v += amp * vnoise(p); p *= 2.07; amp *= 0.5; }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float4x4 m = GetObjectToWorldMatrix();
                float3 center = float3(m._m03, m._m13, m._m23);
                float size = length(float3(m._m00, m._m10, m._m20));
                float3 right = float3(UNITY_MATRIX_V._m00, UNITY_MATRIX_V._m01, UNITY_MATRIX_V._m02);
                float3 up    = float3(UNITY_MATRIX_V._m10, UNITY_MATRIX_V._m11, UNITY_MATRIX_V._m12);
                float3 worldPos = center + (right * IN.positionOS.x + up * IN.positionOS.y) * size;

                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv = IN.uv;
                OUT.color = _StarColor;
                OUT.seed = frac(float2(center.x * 0.113 + center.z * 0.071, center.y * 0.197 + center.x * 0.037)) * 37.0;
                // Fade dust out as the camera gets very close to it (clip w ≈ view depth — no camera position
                // needed), so flying THROUGH a cloud doesn't blank the screen.
                float centerW = TransformWorldToHClip(center).w;
                OUT.nearFade = smoothstep(0.0, max(_NearFade, 1e-3), centerW);

                // Dust near the galactic core is illuminated by it → glows amber; dust farther out stays dark.
                // Proximity-driven (view-independent): it's the dust being LIT, not a view-aligned transmission.
                float coreProx = saturate(1.0 - length(center) / max(_CoreGlowDist, 1e-3));
                OUT.backlit = coreProx * coreProx;   // falls off toward the rim
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * 2.0;      // -1..1
                float r = length(p);
                float radial = pow(saturate(1.0 - r), _Softness);

                // Irregular, wispy interior: DOMAIN-WARP the fBm so the billboard breaks into tendrils/filaments
                // (a plain fBm just gives soft blobs), then sharpen so there are real holes and threads. The
                // radial term feathers the edge. Overlapping wisps read as tangled cloud, not orbs.
                float2 uv = IN.uv;
                float2 warp = float2(fbm(uv * 2.3 + IN.seed), fbm(uv * 2.3 + IN.seed + 8.7));
                float2 q = uv * 4.5 + IN.seed + (warp - 0.5) * 1.6;
                float n = fbm(q) * 0.72 + fbm(q * 2.7 + 3.1) * 0.28;   // finer secondary octave → crisper filaments
                float wisp = smoothstep(0.18, 0.85, n);   // feathered filaments — no hard alpha cutoff vs. the stars
                wisp = wisp * wisp * (1.5 - 0.5 * wisp);   // gentle sharpen that still fades softly at the edges

                float a = radial * wisp * IN.color.a * IN.nearFade;

                // Back-lit dust glows amber (Mie forward-scatter of core light); front-lit/outer dust stays dark.
                float3 dcol = IN.color.rgb + _CoreGlow.rgb * (IN.backlit * _CoreScatter);
                return half4(dcol, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
