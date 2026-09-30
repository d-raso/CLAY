Shader "Custom/EnvironmentDistortion"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Distortion)]
        _DistortionStrength ("Distortion Strength", Range(0, 0.15)) = 0.03
        _DistortionSpeed ("Distortion Speed", Range(0, 3)) = 0.5
        _DistortionScale ("Distortion Scale", Range(0.5, 15)) = 4

        [Header(Flow Field)]
        _FlowInfluence ("Flow Field Influence", Range(0, 2)) = 0.5

        [Header(Underwater Tint)]
        _UnderwaterTint ("Underwater Tint", Color) = (0.7, 0.85, 1, 1)
        _TintStrength ("Tint Strength", Range(0, 1)) = 0.15

        [Header(Layer)]
        _DistortionLayer ("Distortion Layer (0=bg, 2=fg)", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "PreviewType"="Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float2 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _DistortionStrength;
            float _DistortionSpeed;
            float _DistortionScale;
            float _FlowInfluence;
            float4 _UnderwaterTint;
            float _TintStrength;
            float _DistortionLayer;

            // Flow field (set globally by FlowFieldManager)
            sampler2D _FlowFieldTexture;
            float4 _FlowFieldBoundsMin;
            float4 _FlowFieldBoundsMax;

            // Weather globals (set by WeatherSystem)
            float _WeatherStormIntensity;
            float _WeatherWindStrength;
            float2 _WeatherWindDirection;
            float _WeatherTurbulence;

            // Simple noise function
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(lerp(dot(hash2(i + float2(0, 0)), f - float2(0, 0)),
                                 dot(hash2(i + float2(1, 0)), f - float2(1, 0)), u.x),
                            lerp(dot(hash2(i + float2(0, 1)), f - float2(0, 1)),
                                 dot(hash2(i + float2(1, 1)), f - float2(1, 1)), u.x), u.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float time = _Time.y * _DistortionSpeed;

                // Layer-based distortion multiplier (background = more, foreground = less)
                float layerMult = lerp(1.5, 0.5, _DistortionLayer / 2.0);

                // === Base noise distortion ===
                float2 noiseCoord = i.worldPos * _DistortionScale * 0.1;

                float2 baseDistortion;
                baseDistortion.x = noise(noiseCoord + float2(time, 0));
                baseDistortion.y = noise(noiseCoord + float2(0, time) + 100);

                // Second layer for detail
                float2 noiseCoord2 = noiseCoord * 2.0;
                baseDistortion.x += noise(noiseCoord2 + float2(time * 0.7, 0) + 200) * 0.5;
                baseDistortion.y += noise(noiseCoord2 + float2(0, time * 0.7) + 300) * 0.5;

                baseDistortion /= 1.5;

                // === Flow field influence ===
                float2 boundsSize = _FlowFieldBoundsMax.xy - _FlowFieldBoundsMin.xy;
                float2 flowUV = (i.worldPos - _FlowFieldBoundsMin.xy) / max(boundsSize, 0.001);
                flowUV = saturate(flowUV);
                float2 flowVelocity = tex2D(_FlowFieldTexture, flowUV).xy;
                float2 flowDistortion = flowVelocity * _FlowInfluence;

                // === Weather effects ===
                float2 weatherDistortion = float2(0, 0);

                // Storm adds chaotic distortion
                if (_WeatherStormIntensity > 0.01)
                {
                    float2 stormCoord = i.worldPos * _DistortionScale * 0.3;
                    float stormTime = time * 2.0;
                    weatherDistortion.x += noise(stormCoord + float2(stormTime * 1.5, stormTime) + 500) * _WeatherStormIntensity * 0.5;
                    weatherDistortion.y += noise(stormCoord + float2(stormTime, stormTime * 1.3) + 600) * _WeatherStormIntensity * 0.5;
                }

                // Wind adds directional bias
                weatherDistortion += _WeatherWindDirection * _WeatherWindStrength * 0.2;

                // === Combine all distortions ===
                float2 totalDistortion = (baseDistortion + flowDistortion + weatherDistortion) * _DistortionStrength * layerMult;

                // Apply distortion to UV
                float2 distortedUV = i.uv + totalDistortion;

                // Sample texture
                fixed4 col = tex2D(_MainTex, distortedUV);
                col *= i.color;

                // === Underwater tint ===
                float depthFactor = 1.0 - (_DistortionLayer / 2.0);
                float tintAmount = _TintStrength + depthFactor * 0.1;

                // Weather affects tint
                float3 weatherTint = _UnderwaterTint.rgb;
                if (_WeatherStormIntensity > 0.01)
                {
                    weatherTint = lerp(weatherTint, float3(0.5, 0.6, 0.5), _WeatherStormIntensity * 0.3);
                }

                col.rgb = lerp(col.rgb, col.rgb * weatherTint, tintAmount);

                return col;
            }
            ENDCG
        }
    }

    FallBack "Sprites/Default"
}
