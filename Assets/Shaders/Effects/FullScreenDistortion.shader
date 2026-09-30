Shader "Hidden/FullScreenDistortion"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DistortionStrength ("Distortion", Float) = 0.02
        _ChromaticAberration ("Chromatic", Float) = 0.005
        _NoiseSpeed ("Speed", Float) = 0.5
        _NoiseScale ("Scale", Float) = 2.0
        _EffectTime ("Time", Float) = 0
        _DebugMode ("Debug", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        LOD 100

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _DistortionStrength;
            float _ChromaticAberration;
            float _NoiseSpeed;
            float _NoiseScale;
            float _EffectTime;
            float _DebugMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            // Hash function
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
            }

            // Gradient noise
            float gnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(lerp(dot(hash2(i), f),
                                 dot(hash2(i + float2(1, 0)), f - float2(1, 0)), u.x),
                            lerp(dot(hash2(i + float2(0, 1)), f - float2(0, 1)),
                                 dot(hash2(i + float2(1, 1)), f - float2(1, 1)), u.x), u.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // Calculate distortion from noise
                float2 noiseUV = uv * _NoiseScale;
                float time = _EffectTime * _NoiseSpeed;

                float2 distortion;
                distortion.x = gnoise(noiseUV + float2(time, 0));
                distortion.y = gnoise(noiseUV + float2(0, time) + 50);

                // Add second layer
                float2 noiseUV2 = uv * _NoiseScale * 2.0;
                distortion.x += gnoise(noiseUV2 + float2(time * 0.7, 0) + 100) * 0.5;
                distortion.y += gnoise(noiseUV2 + float2(0, time * 0.7) + 150) * 0.5;

                distortion = distortion / 1.5 * _DistortionStrength;

                // Chromatic aberration
                float2 dir = normalize(distortion + 0.0001);
                float2 chromaOffset = dir * _ChromaticAberration;

                // Sample with distortion
                float2 distortedUV = uv + distortion;

                float r = tex2D(_MainTex, distortedUV + chromaOffset).r;
                float g = tex2D(_MainTex, distortedUV).g;
                float b = tex2D(_MainTex, distortedUV - chromaOffset).b;

                float4 col = float4(r, g, b, 1);

                // Debug: magenta tint
                if (_DebugMode > 0.5)
                {
                    col.rgb = lerp(col.rgb, float3(1, 0, 1), 0.5);
                }

                return col;
            }
            ENDCG
        }
    }
}
