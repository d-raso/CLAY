Shader "Custom/UnderwaterSprite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Distortion)]
        _DistortionStrength ("Distortion Strength", Range(0, 0.1)) = 0.02
        _DistortionSpeed ("Distortion Speed", Range(0, 2)) = 0.5
        _DistortionScale ("Distortion Scale", Range(0.5, 10)) = 3

        [Header(Underwater Tint)]
        _UnderwaterTint ("Underwater Tint", Color) = (0.7, 0.85, 1, 1)
        _TintStrength ("Tint Strength", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

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

            float4 _MainTex_ST;
            float4 _Color;
            float _DistortionStrength;
            float _DistortionSpeed;
            float _DistortionScale;
            float4 _UnderwaterTint;
            float _TintStrength;

            // Simple noise
            float2 hash(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(lerp(dot(hash(i), f),
                                 dot(hash(i + float2(1, 0)), f - float2(1, 0)), u.x),
                            lerp(dot(hash(i + float2(0, 1)), f - float2(0, 1)),
                                 dot(hash(i + float2(1, 1)), f - float2(1, 1)), u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz).xy;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y * _DistortionSpeed;

                // Calculate UV distortion based on world position for consistency
                float2 noiseCoord = IN.worldPos * _DistortionScale;

                float2 distortion;
                distortion.x = noise(noiseCoord + float2(time, 0));
                distortion.y = noise(noiseCoord + float2(0, time) + 100);

                // Add second layer
                distortion.x += noise(noiseCoord * 2.0 + float2(time * 0.7, 0) + 200) * 0.5;
                distortion.y += noise(noiseCoord * 2.0 + float2(0, time * 0.7) + 300) * 0.5;

                distortion = distortion / 1.5 * _DistortionStrength;

                // Apply distortion to UV
                float2 distortedUV = IN.uv + distortion;

                // Sample texture
                float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distortedUV);
                col *= IN.color;

                // Apply underwater tint
                col.rgb = lerp(col.rgb, col.rgb * _UnderwaterTint.rgb, _TintStrength);

                return col;
            }
            ENDHLSL
        }
    }
}
