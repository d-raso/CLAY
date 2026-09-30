Shader "Clay/AbsorptionNebula"
{
    // Dark absorption nebulae (Pillars-of-Creation style): dense dust that OCCLUDES the bright galaxy/nebula
    // behind it, with SHARP sculpted silhouette edges (hard-thresholded ridged fBm) so clustered puffs read as
    // "mountains of gas" rather than soft poofs. Alpha-blended, drawn after the emission so it cuts into the glow;
    // ZTest LEqual so foreground stars (which wrote depth) punch through. BRG-safe (only projection matrices).
    Properties
    {
        _StarColor ("Dark Color (a=opacity)", Color) = (0.02, 0.012, 0.01, 1)
        _Edge      ("Edge Hardness", Range(0.02, 0.4)) = 0.14
        _Threshold ("Silhouette Threshold", Range(0.2, 0.8)) = 0.52
        _EdgeTurb  ("Edge Turbulence", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+60" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "AbsorptionForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
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
                float _Edge;
                float _Threshold;
                float _EdgeTurb;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _StarColor)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _StarColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _StarColor)
            #endif

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float fbm(float2 p) { float v = 0, a = 0.5; for (int i = 0; i < 5; i++) { v += a * vnoise(p); p *= 2.06; a *= 0.5; } return v; }

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float2 seed : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

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
                OUT.seed = frac(float2(center.x * 0.127 + center.z * 0.061, center.y * 0.181 + center.x * 0.043)) * 53.0;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * 2.0;
                float radial = saturate(1.0 - length(p));

                // TWO-LEVEL domain warp → complex, tangled, dramatic sculpting (warp the warp).
                float2 uv = IN.uv;
                float2 w1 = float2(fbm(uv * 1.6 + IN.seed), fbm(uv * 1.6 + IN.seed + 5.7));
                float2 w2 = float2(fbm(uv * 3.1 + IN.seed + w1 * 1.6), fbm(uv * 3.1 + IN.seed + 9.3 + w1 * 1.6));
                float2 q = uv * 4.2 + IN.seed + (w2 - 0.5) * 3.2;

                // MULTI-SCALE ridged veins → layered, mountainous structure at several scales.
                float r1 = 1.0 - abs(fbm(q));
                float r2 = 1.0 - abs(fbm(q * 2.3 + 3.1));
                float r3 = 1.0 - abs(fbm(q * 4.9 + 7.7));
                float dense = (r1 * 0.5 + r2 * 0.32 + r3 * 0.18) * (0.35 + 0.65 * radial);

                // Fine erosion breaks the silhouette into wispy tendrils at its edge.
                float detail = 0.7 + 0.4 * fbm(uv * 11.0 + IN.seed + 2.0);
                // Narrow smoothstep band → sharp "mountains of gas" silhouette (_Edge controls crispness).
                float mass = smoothstep(_Threshold - _Edge, _Threshold + _Edge, dense * detail);

                // DENSITY-MAPPED EDGE TURBULENCE: high-freq ridged ripples applied only at the gradient boundary
                // (mass·(1-mass) peaks there), leaving dense interiors smooth — Kelvin-Helmholtz frayed borders.
                float edgeBand = mass * (1.0 - mass) * 4.0;
                float kh = (1.0 - abs(fbm(uv * 20.0 + IN.seed + 4.0)));
                mass = saturate(mass + (kh - 0.5) * edgeBand * _EdgeTurb);

                return half4(IN.color.rgb, saturate(mass) * IN.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
