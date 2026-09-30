Shader "Hidden/WaterDistortion"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DistortionStrength ("Distortion Strength", Range(0, 0.1)) = 0.02
        _ChromaticAberration ("Chromatic Aberration", Range(0, 0.02)) = 0.005
        _Layer1Speed ("Layer 1 Speed", Float) = 0.5
        _Layer2Speed ("Layer 2 Speed", Float) = 0.3
        _Layer3Speed ("Layer 3 Speed", Float) = 0.7
        _Layer1Scale ("Layer 1 Scale", Float) = 2.0
        _Layer2Scale ("Layer 2 Scale", Float) = 4.0
        _Layer3Scale ("Layer 3 Scale", Float) = 8.0
        _FlowFieldInfluence ("Flow Field Influence", Range(0, 1)) = 0.5
        _LayerCount ("Layer Count", Int) = 2
        _EffectTime ("Time", Float) = 0
        _DebugMode ("Debug Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "WaterDistortion"
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _THREE_LAYERS

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            // Flow field texture (set globally by FlowFieldManager)
            sampler2D _FlowFieldTexture;
            float4 _FlowFieldBoundsMin;
            float4 _FlowFieldBoundsMax;

            // Parameters
            float _DistortionStrength;
            float _ChromaticAberration;
            float _DebugMode;
            float _Layer1Speed;
            float _Layer2Speed;
            float _Layer3Speed;
            float _Layer1Scale;
            float _Layer2Scale;
            float _Layer3Scale;
            float _FlowFieldInfluence;
            int _LayerCount;
            float _EffectTime;

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

            // Simplex noise for smooth distortion
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
                float3 p = permute(permute(i.y + float3(0.0, i1.y, 1.0))
                                 + i.x + float3(0.0, i1.x, 1.0));

                float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy),
                              dot(x12.zw, x12.zw)), 0.0);
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

            // Sample flow field at screen position
            float2 SampleFlowField(float2 screenUV)
            {
                return tex2D(_FlowFieldTexture, screenUV).xy;
            }

            // Calculate distortion offset from noise layers
            float2 GetDistortion(float2 uv, float time)
            {
                float2 distortion = float2(0, 0);

                // Layer 1 - Large, slow waves
                float2 uv1 = uv * _Layer1Scale;
                float n1x = snoise(uv1 + float2(time * _Layer1Speed, 0));
                float n1y = snoise(uv1 + float2(0, time * _Layer1Speed) + 100);
                distortion += float2(n1x, n1y) * 1.0;

                // Layer 2 - Medium detail
                float2 uv2 = uv * _Layer2Scale;
                float n2x = snoise(uv2 + float2(time * _Layer2Speed, 0) + 200);
                float n2y = snoise(uv2 + float2(0, time * _Layer2Speed) + 300);
                distortion += float2(n2x, n2y) * 0.5;

                // Layer 3 - Fine detail (optional based on quality)
                #if _THREE_LAYERS
                if (_LayerCount >= 3)
                {
                    float2 uv3 = uv * _Layer3Scale;
                    float n3x = snoise(uv3 + float2(time * _Layer3Speed, 0) + 400);
                    float n3y = snoise(uv3 + float2(0, time * _Layer3Speed) + 500);
                    distortion += float2(n3x, n3y) * 0.25;
                }
                #endif

                // Normalize based on layer count
                float normFactor = (_LayerCount >= 3) ? 1.75 : 1.5;
                distortion /= normFactor;

                return distortion;
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

                // Get noise-based distortion
                float2 noiseDistortion = GetDistortion(uv, _EffectTime);

                // Sample flow field and add its influence
                float2 flowDistortion = SampleFlowField(uv) * _FlowFieldInfluence;

                // Combine distortions
                float2 totalDistortion = (noiseDistortion + flowDistortion) * _DistortionStrength;

                // Apply chromatic aberration along distortion direction
                float2 distortDir = normalize(totalDistortion + 0.0001);
                float2 chromaOffset = distortDir * _ChromaticAberration;

                // Sample with distortion + chromatic aberration
                float2 distortedUV = uv + totalDistortion;

                float r = tex2D(_MainTex, distortedUV + chromaOffset).r;
                float g = tex2D(_MainTex, distortedUV).g;
                float b = tex2D(_MainTex, distortedUV - chromaOffset).b;
                float a = tex2D(_MainTex, distortedUV).a;

                float4 result = float4(r, g, b, a);

                // Debug mode: tint screen magenta to confirm shader is running
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
