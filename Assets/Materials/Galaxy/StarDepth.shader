Shader "Clay/StarDepth"
{
    // Invisible depth-only companions to the stars: they write depth at each star's core so the dust pass can
    // depth-test against them (dust behind a star is culled → the star shows through; dust in front draws over).
    // The additive star COLOUR pass is a SEPARATE population left untouched (ZTest Always), so overlapping stars
    // keep blooming and the core stays bright. Drawn in the opaque (Geometry) queue, before the transparents.
    // BRG-safe (only the projection, which is bound). Sizing MUST match Clay/InstancedStar so the occluder disc
    // lines up with the star's drawn size (otherwise dust gets punched with holes bigger than the star point).
    Properties
    {
        _MinPointNdc ("Star Resolve Size", Float) = 0.0022
        _MaxPointNdc ("Star Max Screen Size", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "StarDepthOnly"
            Tags { "LightMode"="UniversalForward" }
            ColorMask 0
            ZWrite On
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
                float _MinPointNdc;
                float _MaxPointNdc;
            CBUFFER_END

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                float4x4 m = GetObjectToWorldMatrix();
                float3 center = float3(m._m03, m._m13, m._m23);
                float size = length(float3(m._m00, m._m10, m._m20));
                float3 right = float3(UNITY_MATRIX_V._m00, UNITY_MATRIX_V._m01, UNITY_MATRIX_V._m02);
                float3 up    = float3(UNITY_MATRIX_V._m10, UNITY_MATRIX_V._m11, UNITY_MATRIX_V._m12);

                // EXACT same sizing as Clay/InstancedStar (min enlarge + max clamp) so the depth disc lines up
                // with the drawn star and never punches a dust hole bigger than the star point.
                float4 cCS = TransformWorldToHClip(center);
                float4 eCS = TransformWorldToHClip(center + right * size);
                float ndcHalf = length(eCS.xy / max(eCS.w, 1e-4) - cCS.xy / max(cCS.w, 1e-4));
                float grow;
                float lo = _MinPointNdc / max(ndcHalf, 1e-6);
                if (lo > 1.0) grow = lo;
                else grow = min(1.0, _MaxPointNdc / max(ndcHalf, 1e-6));
                float drawSize = size * grow;

                float3 worldPos = center + (right * IN.positionOS.x + up * IN.positionOS.y) * drawSize;
                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * 2.0;
                clip(0.32 - length(p));   // tight core disc → dust hole ≈ the star's bright point, not a big gap
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
