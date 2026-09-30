Shader "Hidden/UnderwaterFog"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _NearColor ("Near Color", Color) = (0.15, 0.35, 0.35, 1)
        _FarColor ("Far Color", Color) = (0.02, 0.08, 0.12, 1)
        _FogStart ("Fog Start", Float) = 5
        _FogEnd ("Fog End", Float) = 40
        _Density ("Density", Range(0, 1)) = 0.5
        _NoiseStrength ("Noise Strength", Range(0, 2)) = 0.3
        _NoiseScale ("Noise Scale", Float) = 2
        _NoiseSpeed ("Noise Speed", Float) = 0.1
        _LightShaftIntensity ("Light Shaft Intensity", Range(0, 1)) = 0.2
        _LightDirection ("Light Direction", Vector) = (0.3, -0.8, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "UnderwaterFog"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float4 _NearColor;
            float4 _FarColor;
            float _FogStart;
            float _FogEnd;
            float _Density;
            float _NoiseStrength;
            float _NoiseScale;
            float _NoiseSpeed;
            float _LightShaftIntensity;
            float2 _LightDirection;
            float _Time;

            // Simplex noise
            float3 mod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float2 mod289(float2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float3 permute(float3 x) { return mod289(((x * 34.0) + 1.0) * x); }

            float snoise(float2 v)
            {
                const float4 C = float4(0.211324865405187, 0.366025403784439,
                                       -0.577350269189626, 0.024390243902439);
                float2 i = floor(v + dot(v, C.yy));
                float2 x0 = v - i + dot(i, C.xx);
                float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
                float4 x12 = x0.xyxy + C.xxzz;
                x12.xy -= i1;
                i = mod289(i);
                float3 p = permute(permute(i.y + float3(0.0, i1.y, 1.0)) + i.x + float3(0.0, i1.x, 1.0));
                float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
                m = m * m;
                m = m * m;
                float3 x = 2.0 * frac(p * C.www) - 1.0;
                float3 h = abs(x) - 0.5;
                float3 ox = floor(x + 0.5);
                float3 a0 = x - ox;
                m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);
                float3 g;
                g.x = a0.x * x0.x + h.x * x0.y;
                g.yz = a0.yz * x12.xz + h.yz * x12.yw;
                return 130.0 * dot(m, g);
            }

            // Fractal Brownian Motion for organic noise
            float fbm(float2 p, int octaves)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;

                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * snoise(p * frequency);
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                return value;
            }

            // Light shaft calculation
            float calculateLightShaft(float2 uv, float time)
            {
                float2 lightDir = normalize(_LightDirection);

                // Create animated rays
                float2 rayUV = uv * 3.0;
                rayUV += lightDir * time * 0.2;

                // Multiple layers of light rays
                float rays = 0.0;
                rays += snoise(rayUV * 1.0 + float2(time * 0.1, 0)) * 0.5;
                rays += snoise(rayUV * 2.0 + float2(0, time * 0.15)) * 0.3;
                rays += snoise(rayUV * 4.0 + float2(time * 0.05, time * 0.05)) * 0.2;

                // Fade based on direction from top
                float topFade = saturate(1.0 - uv.y * 0.5);

                // Convert to positive and apply falloff
                rays = saturate(rays * 0.5 + 0.5);
                rays = pow(rays, 2.0) * topFade;

                return rays * _LightShaftIntensity;
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

                // Sample scene color
                float4 sceneColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // For 2D, we'll use screen position as "depth" proxy
                // You could also pass actual sprite depths via render texture
                float2 screenPos = uv * 2.0 - 1.0;
                float pseudoDepth = length(screenPos) * 0.5; // Distance from center

                // Add noise variation to fog
                float2 noiseUV = uv * _NoiseScale + _Time * _NoiseSpeed;
                float noise = fbm(noiseUV, 3) * _NoiseStrength;

                // Calculate fog factor with noise
                float fogRange = _FogEnd - _FogStart;
                float adjustedDepth = pseudoDepth + noise * 0.2;
                float fogFactor = saturate((adjustedDepth * fogRange + _FogStart) / fogRange);
                fogFactor = pow(fogFactor, 2.0 - _Density); // Density curve
                fogFactor *= _Density;

                // Interpolate fog color based on "depth"
                float4 fogColor = lerp(_NearColor, _FarColor, fogFactor);

                // Add murky noise to the fog itself
                float murk = fbm(uv * _NoiseScale * 0.5 + _Time * _NoiseSpeed * 0.5, 4);
                murk = murk * 0.5 + 0.5; // Normalize to 0-1
                fogColor.rgb += (murk - 0.5) * 0.1 * _NoiseStrength;

                // Calculate light shafts
                float lightShaft = calculateLightShaft(uv, _Time);

                // Apply light shaft to scene (brighten where shafts are)
                float3 litScene = sceneColor.rgb + lightShaft * float3(0.8, 0.9, 1.0) * 0.3;

                // Blend scene with fog
                float3 finalColor = lerp(litScene, fogColor.rgb, fogFactor * 0.6);

                // Add subtle vignette for depth
                float vignette = 1.0 - length(screenPos) * 0.3;
                vignette = saturate(vignette);
                finalColor *= vignette;

                return float4(finalColor, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
