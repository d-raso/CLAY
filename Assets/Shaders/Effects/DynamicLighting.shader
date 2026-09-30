Shader "Hidden/DynamicLighting"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        [Header(Caustics)]
        _CausticsIntensity ("Caustics Intensity", Range(0, 1)) = 0.3
        _CausticsColor ("Caustics Color", Color) = (0.8, 0.95, 1.0, 1.0)
        _CausticsScale1 ("Caustics Scale 1", Float) = 5.0
        _CausticsScale2 ("Caustics Scale 2", Float) = 7.0
        _CausticsSpeed ("Caustics Speed", Float) = 0.5
        _CausticsSharpness ("Caustics Sharpness", Range(1, 10)) = 3.0

        [Header(God Rays)]
        _GodRaysEnabled ("God Rays Enabled", Float) = 0
        _GodRaysIntensity ("God Rays Intensity", Range(0, 1)) = 0.15
        _GodRaysColor ("God Rays Color", Color) = (1.0, 0.98, 0.9, 1.0)
        _LightSourcePos ("Light Source Position", Vector) = (0.5, 1.0, 0, 0)
        _GodRaysSamples ("God Rays Samples", Int) = 16
        _GodRaysDecay ("God Rays Decay", Range(0.9, 1.0)) = 0.96
        _GodRaysDensity ("God Rays Density", Range(0.1, 2.0)) = 0.5

        [Header(Flow Field)]
        _FlowFieldInfluence ("Flow Field Influence", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "DynamicLighting"
            Blend One One  // Additive blending
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _GOD_RAYS_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Flow field texture (set globally by FlowFieldManager)
            TEXTURE2D(_FlowFieldTexture);
            SAMPLER(sampler_FlowFieldTexture);

            // Caustics parameters
            float _CausticsIntensity;
            float4 _CausticsColor;
            float _CausticsScale1;
            float _CausticsScale2;
            float _CausticsSpeed;
            float _CausticsSharpness;

            // God rays parameters
            float _GodRaysEnabled;
            float _GodRaysIntensity;
            float4 _GodRaysColor;
            float4 _LightSourcePos;
            int _GodRaysSamples;
            float _GodRaysDecay;
            float _GodRaysDensity;

            // Flow field
            float _FlowFieldInfluence;
            float _EffectTime;

            // Hash function for Voronoi
            float2 hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Voronoi noise for caustic patterns
            float voronoi(float2 uv, float time)
            {
                float2 g = floor(uv);
                float2 f = frac(uv);

                float minDist = 1.0;
                float minDist2 = 1.0;

                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 neighbor = float2(x, y);
                        float2 cellPt = hash22(g + neighbor);

                        // Animate the cell point
                        cellPt = 0.5 + 0.5 * sin(time + 6.2831 * cellPt);

                        float2 diff = neighbor + cellPt - f;
                        float dist = length(diff);

                        if (dist < minDist)
                        {
                            minDist2 = minDist;
                            minDist = dist;
                        }
                        else if (dist < minDist2)
                        {
                            minDist2 = dist;
                        }
                    }
                }

                // Return edge distance for caustic lines
                return minDist2 - minDist;
            }

            // Calculate caustics from dual-layer Voronoi
            float GetCaustics(float2 uv, float time)
            {
                // Sample flow field for animation offset
                float2 flow = SAMPLE_TEXTURE2D(_FlowFieldTexture, sampler_FlowFieldTexture, uv).xy;
                float2 flowOffset = flow * _FlowFieldInfluence * _EffectTime;

                // Layer 1 - Primary caustics
                float2 uv1 = uv * _CausticsScale1 + flowOffset;
                float caustic1 = voronoi(uv1, time * _CausticsSpeed);

                // Layer 2 - Secondary caustics (different scale and offset time)
                float2 uv2 = uv * _CausticsScale2 + flowOffset * 0.7;
                float caustic2 = voronoi(uv2, time * _CausticsSpeed * 0.8 + 10.0);

                // Combine layers
                float combined = (caustic1 + caustic2) * 0.5;

                // Sharpen to create bright caustic lines
                combined = pow(combined, _CausticsSharpness);
                combined = saturate(combined * 2.0);

                return combined;
            }

            // Simple radial blur for god rays
            float GetGodRays(float2 uv, float2 lightPos)
            {
                float2 deltaUV = (uv - lightPos) * _GodRaysDensity / float(_GodRaysSamples);

                float2 currentUV = uv;
                float illumination = 0.0;
                float decay = 1.0;

                for (int i = 0; i < _GodRaysSamples; i++)
                {
                    currentUV -= deltaUV;

                    // Sample the scene brightness (simplified - just use UV-based falloff)
                    float2 sampleUV = saturate(currentUV);
                    float brightness = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV).r;

                    // Distance from light source creates ray shape
                    float distFromLight = length(currentUV - lightPos);
                    float rayShape = exp(-distFromLight * 2.0);

                    illumination += brightness * rayShape * decay;
                    decay *= _GodRaysDecay;
                }

                return illumination / float(_GodRaysSamples);
            }

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                // Full screen triangle
                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);

                #if UNITY_UV_STARTS_AT_TOP
                output.positionCS.y = -output.positionCS.y;
                #endif

                output.uv = uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float3 lighting = float3(0, 0, 0);

                // Caustics
                if (_CausticsIntensity > 0.001)
                {
                    float caustics = GetCaustics(uv, _EffectTime);
                    lighting += _CausticsColor.rgb * caustics * _CausticsIntensity;
                }

                // God rays (optional)
                #if _GOD_RAYS_ON
                if (_GodRaysEnabled > 0.5 && _GodRaysIntensity > 0.001)
                {
                    float godRays = GetGodRays(uv, _LightSourcePos.xy);
                    lighting += _GodRaysColor.rgb * godRays * _GodRaysIntensity;
                }
                #endif

                return float4(lighting, 0.0);
            }
            ENDHLSL
        }
    }
}
