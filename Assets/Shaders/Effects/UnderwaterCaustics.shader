Shader "Custom/UnderwaterCaustics"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0, 1)) = 0.3
        _CausticsColor ("Caustics Color", Color) = (0.8, 0.95, 1, 0.3)
        _Scale ("Scale", Range(0.5, 5)) = 2
        _Speed ("Speed", Range(0, 2)) = 0.5
        _Distortion ("Distortion", Range(0, 1)) = 0.3
        _Layers ("Layers", Int) = 2
        _LayerOffset ("Layer Offset", Range(0, 1)) = 0.3
        _LightDirection ("Light Direction", Vector) = (0.2, -0.8, 0, 0)
        _DirectionalBias ("Directional Bias", Range(0, 1)) = 0.3
        _CausticsTime ("Time", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Blend SrcAlpha One // Additive blending
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float2 worldPos : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _Intensity;
            float4 _CausticsColor;
            float _Scale;
            float _Speed;
            float _Distortion;
            int _Layers;
            float _LayerOffset;
            float2 _LightDirection;
            float _DirectionalBias;
            float _CausticsTime;

            // Hash function for random values
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                          dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Voronoi noise for caustic pattern
            float voronoi(float2 p)
            {
                float2 n = floor(p);
                float2 f = frac(p);

                float minDist = 1.0;
                float minDist2 = 1.0;

                for (int j = -1; j <= 1; j++)
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 g = float2(i, j);
                        float2 o = hash2(n + g);

                        // Animate the points
                        o = 0.5 + 0.5 * sin(_CausticsTime * _Speed + 6.2831 * o);

                        float2 r = g - f + o;
                        float d = dot(r, r);

                        if (d < minDist)
                        {
                            minDist2 = minDist;
                            minDist = d;
                        }
                        else if (d < minDist2)
                        {
                            minDist2 = d;
                        }
                    }
                }

                // Return edge-based caustic pattern
                float edge = minDist2 - minDist;
                return sqrt(edge);
            }

            // Multi-layer caustics with distortion
            float caustics(float2 uv, float time)
            {
                float result = 0.0;
                float weight = 1.0;
                float totalWeight = 0.0;

                for (int i = 0; i < _Layers; i++)
                {
                    float2 offset = float2(
                        sin(time * 0.3 + i * 1.7) * _Distortion,
                        cos(time * 0.4 + i * 2.3) * _Distortion
                    );

                    float2 scaledUV = (uv + offset + _LayerOffset * i) * _Scale * (1.0 + i * 0.3);

                    // Add directional bias
                    scaledUV += _LightDirection * time * 0.1 * _DirectionalBias;

                    float v = voronoi(scaledUV);

                    // Sharpen the caustics
                    v = pow(v, 2.0);

                    result += v * weight;
                    totalWeight += weight;
                    weight *= 0.5;
                }

                return result / totalWeight;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz).xy;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // Use world position for seamless tiling
                float2 uv = IN.worldPos;

                // Calculate caustics
                float c = caustics(uv, _CausticsTime);

                // Apply intensity curve
                c = pow(c, 1.5) * _Intensity * 2.0;

                // Add subtle shimmer
                float shimmer = sin(_CausticsTime * 3.0 + IN.uv.x * 10.0 + IN.uv.y * 10.0) * 0.1 + 0.9;
                c *= shimmer;

                // Fade based on light direction (brighter where light comes from)
                float2 lightDir = normalize(_LightDirection);
                float lightFade = dot(normalize(IN.uv - 0.5), -lightDir) * 0.5 + 0.5;
                lightFade = lerp(1.0, lightFade, _DirectionalBias);

                // Output
                float4 color = _CausticsColor;
                color.a *= c * lightFade * IN.color.a;

                return color;
            }
            ENDHLSL
        }
    }
}
