Shader "Hidden/WaterDistortionSimple"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DistortionStrength ("Distortion Strength", Range(0, 0.1)) = 0.02
        _ChromaticAberration ("Chromatic Aberration", Range(0, 0.02)) = 0.005
        _Layer1Speed ("Layer 1 Speed", Float) = 0.5
        _Layer1Scale ("Layer 1 Scale", Float) = 2.0
        _Layer2Speed ("Layer 2 Speed", Float) = 0.3
        _Layer2Scale ("Layer 2 Scale", Float) = 4.0
        _EffectTime ("Time", Float) = 0
        _DebugMode ("Debug Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float _DistortionStrength;
            float _ChromaticAberration;
            float _Layer1Speed;
            float _Layer1Scale;
            float _Layer2Speed;
            float _Layer2Scale;
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

            // Simple noise function
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                          dot(p, float2(269.5, 183.3)));
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

            float2 GetDistortion(float2 uv, float time)
            {
                float2 distortion = float2(0, 0);

                // Layer 1
                float2 uv1 = uv * _Layer1Scale;
                distortion.x += noise(uv1 + float2(time * _Layer1Speed, 0));
                distortion.y += noise(uv1 + float2(0, time * _Layer1Speed) + 100);

                // Layer 2
                float2 uv2 = uv * _Layer2Scale;
                distortion.x += noise(uv2 + float2(time * _Layer2Speed, 0) + 200) * 0.5;
                distortion.y += noise(uv2 + float2(0, time * _Layer2Speed) + 300) * 0.5;

                return distortion / 1.5;
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

                // Get distortion
                float2 distortion = GetDistortion(uv, _EffectTime) * _DistortionStrength;

                // Chromatic aberration
                float2 distortDir = normalize(distortion + 0.0001);
                float2 chromaOffset = distortDir * _ChromaticAberration;

                // Sample with distortion
                float2 distortedUV = uv + distortion;

                float r = tex2D(_MainTex, distortedUV + chromaOffset).r;
                float g = tex2D(_MainTex, distortedUV).g;
                float b = tex2D(_MainTex, distortedUV - chromaOffset).b;
                float a = tex2D(_MainTex, distortedUV).a;

                float4 result = float4(r, g, b, a);

                // Debug mode - tint magenta
                if (_DebugMode > 0.5)
                {
                    result.rgb = lerp(result.rgb, float3(1, 0, 1), 0.5);
                }

                return result;
            }
            ENDCG
        }
    }
}
