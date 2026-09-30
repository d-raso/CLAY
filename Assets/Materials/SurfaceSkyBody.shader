Shader "CLAY/SurfaceSkyBody"
{
    // A moon / planet seen from the ground. It is BEHIND the atmosphere, so the sky's own light is added in front of
    // it: the body contributes only its sunlit reflection (additive over the sky dome) and its unlit side is simply
    // the sky. At night _Occlude rises so the disc still blocks the stars behind it.
    Properties
    {
        _BaseMap("Map", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _SunDir("Sun Direction (world)", Vector) = (0,1,0,0)
        _SunColor("Sun Color", Color) = (1,1,1,1)
        _Transmit("Atmospheric transmittance", Range(0,1)) = 1
        _Occlude("Occlude background", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _BaseColor, _SunDir, _SunColor;
                float _Transmit, _Occlude;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; float3 od : TEXCOORD1; };
            V vert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.n = TransformObjectToWorldNormal(v.normalOS);
                o.od = v.positionOS.xyz;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 od = normalize(i.od);
                float2 uv = float2(atan2(od.z, od.x) * 0.15915494 + 0.5, 0.5 - asin(clamp(od.y, -1.0, 1.0)) * 0.31830989);
                float3 alb = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0).rgb * _BaseColor.rgb;
                float ndl = saturate(dot(normalize(i.n), normalize(_SunDir.xyz)));
                float terminator = smoothstep(0.0, 0.08, ndl);                 // soft terminator
                float3 lit = alb * _SunColor.rgb * ndl * terminator * 1.6 * _Transmit;
                return half4(lit, _Occlude);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
