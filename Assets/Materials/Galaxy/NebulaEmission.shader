Shader "Clay/NebulaEmission"
{
    // Additive, soft, coloured glow puffs for emission (HII) and reflection nebulae. Per-instance colour from the
    // StarColor component; an fBm interior makes each puff an irregular wisp so clustered puffs read as an organic
    // glowing cloud. BRG-safe (only the projection matrices, no camera position).
    Properties
    {
        _StarColor ("Color", Color) = (1, 1, 1, 1)
        _Softness  ("Edge Softness", Range(0.3, 3)) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "NebulaForward"
            Tags { "LightMode"="UniversalForward" }
            Blend One One            // additive
            ZWrite Off
            ZTest Always
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
            float fbm(float2 p) { float v = 0, a = 0.5; for (int i = 0; i < 5; i++) { v += a * vnoise(p); p *= 2.05; a *= 0.5; } return v; }

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
                OUT.seed = frac(float2(center.x * 0.113 + center.z * 0.071, center.y * 0.197 + center.x * 0.031)) * 41.0;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * 2.0;
                float2 uv = IN.uv;

                // IRREGULAR soft envelope (noise-warped radius) so the puff is a ragged nebulous shape, NOT a round
                // disc of colour. The warp also tears holes so overlapping puffs read as wispy cloud.
                float envN = fbm(uv * 1.6 + IN.seed + 3.0);
                float envelope = pow(saturate((1.0 - length(p)) * (0.45 + envN)), _Softness);

                // Two-level domain warp → tangled, delicate structure.
                float2 w1 = float2(fbm(uv * 1.8 + IN.seed), fbm(uv * 1.8 + IN.seed + 5.1));
                float2 w2 = float2(fbm(uv * 3.4 + IN.seed + w1 * 1.5), fbm(uv * 3.4 + IN.seed + 8.2 + w1 * 1.5));
                float2 q = uv * 4.5 + IN.seed + (w2 - 0.5) * 2.6;

                // Multi-scale ridged veins → wispy filaments with dark gaps.
                float r1 = 1.0 - abs(fbm(q));
                float r2 = 1.0 - abs(fbm(q * 2.2 + 3.3));
                float veins = pow(saturate(r1 * 0.6 + r2 * 0.4), 2.6);
                float glow = envelope * saturate(veins + 0.1 * envN - 0.05);

                float3 col = IN.color.rgb * glow;
                return half4(col, 1.0);   // additive (alpha ignored)
            }
            ENDHLSL
        }
    }
    Fallback Off
}
