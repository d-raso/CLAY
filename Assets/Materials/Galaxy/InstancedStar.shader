Shader "Clay/InstancedStar"
{
    // One instanced, additive, camera-facing soft point per star. The per-instance colour (_StarColor, blackbody
    // × brightness computed on the CPU) is uploaded by Entities Graphics from the StarColor component. URP + DOTS
    // instancing.
    Properties
    {
        _StarColor ("Star Color", Color) = (1, 1, 1, 1)
        // On-screen half-size (NDC) below which a star is treated as sub-pixel: clamped to this size and
        // flux-dimmed, which preserves the galaxy's SURFACE BRIGHTNESS (angular luminosity) with distance.
        _MinPointNdc ("Star Resolve Size", Float) = 0.0022
        // On-screen half-size ABOVE which a near star is shrunk & dimmed, so it doesn't balloon when flying through.
        _MaxPointNdc ("Star Max Screen Size", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "StarForward"
            Tags { "LightMode"="UniversalForward" }
            Blend One One            // additive
            ZWrite Off
            ZTest Always             // never culled by the StarDepth prepass → overlapping stars keep blooming
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
                float _MinPointNdc;
                float _MaxPointNdc;
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
                float bright : TEXCOORD2;   // flux-conservation dim for sub-pixel (far) stars
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // Camera-facing billboard: expand the unit quad in the view's right/up axes around the instance
                // centre, scaled by the instance's (uniform) scale — so every star is a round soft point.
                float4x4 m = GetObjectToWorldMatrix();
                float3 center = float3(m._m03, m._m13, m._m23);
                float size = length(float3(m._m00, m._m10, m._m20));
                float3 right = float3(UNITY_MATRIX_V._m00, UNITY_MATRIX_V._m01, UNITY_MATRIX_V._m02);
                float3 up    = float3(UNITY_MATRIX_V._m10, UNITY_MATRIX_V._m11, UNITY_MATRIX_V._m12);

                // Measure on-screen size WITHOUT the camera position (BRG-safe — only uses the projection that
                // TransformWorldToHClip already relies on): project the centre and a point one half-size along
                // 'right' and compare their NDC positions → the billboard's on-screen half-size.
                float4 cCS = TransformWorldToHClip(center);
                float4 eCS = TransformWorldToHClip(center + right * size);
                float2 cN = cCS.xy / max(cCS.w, 1e-4);
                float2 eN = eCS.xy / max(eCS.w, 1e-4);
                float ndcHalf = length(eN - cN);

                // Below ~a pixel (far away), clamp to a minimum screen size but DIM by the area it grew, conserving
                // flux: a lone far star is faint, many overlapping ones sum into milky unresolved light. Near stars
                // (already bigger than the minimum) are untouched — distinct points.
                // Near stars keep their world size (grow = 1) so the core stays bright and placement is stable.
                // Only sub-pixel (far) stars are clamped up to the minimum size and flux-dimmed by 1/grow² — as
                // ~grow² more stars fall into each screen pixel with distance, their sum stays constant, so the
                // galaxy holds the SAME surface brightness (angular luminosity) at every distance.
                // Far (sub-pixel): enlarge to the min size and flux-dim (1/grow²) → surface brightness held.
                // Near (bigger than max): shrink toward the max size AND dim → stars don't balloon or blow out
                // when flying through. Middle: world size, full brightness.
                float grow, bright;
                float lo = _MinPointNdc / max(ndcHalf, 1e-6);
                if (lo > 1.0)
                {
                    grow = lo;
                    bright = 1.0 / (grow * grow);
                }
                else
                {
                    float hi = _MaxPointNdc / max(ndcHalf, 1e-6);
                    grow = min(1.0, hi);
                    bright = min(1.0, hi);
                }
                float drawSize = size * grow;

                float3 worldPos = center + (right * IN.positionOS.x + up * IN.positionOS.y) * drawSize;

                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv = IN.uv;
                OUT.color = _StarColor;
                OUT.bright = bright;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float2 p = (IN.uv - 0.5) * 2.0;      // -1..1
                float r = length(p);
                float glow = saturate(1.0 - r);
                glow *= glow;                         // soft round falloff, zero at the edge
                float a = glow * IN.bright;           // flux-conserving dim for sub-pixel stars
                float3 col = IN.color.rgb * a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
