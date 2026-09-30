Shader "Hidden/WaterDistortion"
{
    Properties { _MainTex("Source", 2D) = "white" {} }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "WaterDistortion"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _THREE_LAYERS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            float _DistortionStrength, _ChromaticAberration;
            float _Layer1Speed, _Layer2Speed, _Layer3Speed;
            float _Layer1Scale, _Layer2Scale, _Layer3Scale;
            float _FlowFieldInfluence, _EffectTime, _DebugMode;
            int   _LayerCount;

            // Set by FlowFieldManager and PlanetaryEnvironment each frame
            float4 _GlobalFlowDirection;
            float  _WaterTemperature;
            float  _WaterTurbulence;

            struct Attr { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Vary { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            Vary vert(Attr i)
            {
                Vary o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.uv  = i.uv;
                return o;
            }

            // Smooth gradient noise
            float2 hash2(float2 p) { p = float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))); return frac(sin(p)*43758.5)*2-1; }
            float gnoise(float2 p)
            {
                float2 i=floor(p), f=frac(p), u=f*f*(3-2*f);
                return lerp(lerp(dot(hash2(i),f),dot(hash2(i+float2(1,0)),f-float2(1,0)),u.x),
                            lerp(dot(hash2(i+float2(0,1)),f-float2(0,1)),dot(hash2(i+float2(1,1)),f-float2(1,1)),u.x),u.y);
            }

            // FBM: 4 octaves of noise for rich detail
            float fbm(float2 p)
            {
                float v=0, a=0.5;
                for(int i=0;i<4;i++){ v+=gnoise(p)*a; p=p*2.1+float2(1.7,9.2); a*=0.5; }
                return v;
            }

            float4 frag(Vary i) : SV_Target
            {
                float2 uv = i.uv;
                float  t  = _EffectTime;
                float2 flo = _GlobalFlowDirection.xy * _FlowFieldInfluence;

                // Temperature drives heat shimmer — vertical ripple bands
                float tempNorm = saturate((_WaterTemperature + 10f) / 70f); // -10..60 → 0..1
                float shimmerStrength = tempNorm * tempNorm * 0.04;
                float shimmerFreq = 18.0;
                float shimmerSpeed = 2.5;
                float shimmer = sin(uv.y * shimmerFreq + t * shimmerSpeed) *
                                sin(uv.x * shimmerFreq * 0.7 + t * shimmerSpeed * 1.3);
                float2 heatOffset = float2(shimmer * shimmerStrength, 0);

                // Turbulence drives broad low-freq warp
                float turbNorm = saturate(_WaterTurbulence);

                // Layer 1 — large slow waves following flow
                float2 uv1 = uv * _Layer1Scale + t * (_Layer1Speed * float2(0.6,0.4) + flo * 0.4);
                float2 off = float2(fbm(uv1), fbm(uv1 + float2(5.2,1.7)));

                // Layer 2 — medium detail, opposite drift
                float2 uv2 = uv * _Layer2Scale - t * (_Layer2Speed * float2(0.4,0.7) + flo * 0.25);
                float2 off2 = float2(gnoise(uv2), gnoise(uv2 + float2(3.3,7.1)));
                off = lerp(off, (off + off2) * 0.5, min(1.0, _LayerCount));

                // Layer 3 — fine high-freq detail
                #ifdef _THREE_LAYERS
                float2 uv3 = uv * _Layer3Scale + t * _Layer3Speed * float2(-0.3, 0.5);
                float2 off3 = float2(gnoise(uv3 * 2.3), gnoise(uv3 * 2.3 + 9.1));
                off = (off * 2 + off3) / 3.0;
                #endif

                // Turbulence amplifies the distortion field
                float baseStr = _DistortionStrength * (1.0 + turbNorm * 2.5);
                float2 d = off * baseStr + heatOffset;

                if (_DebugMode > 0.5) return float4(off * 0.5 + 0.5, 0, 1);

                // Sample with chromatic aberration split
                float ca = _ChromaticAberration * (1.0 + tempNorm);
                float2 caDir = normalize(off + float2(0.001, 0));
                float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d + caDir * ca).r;
                float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d).g;
                float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d - caDir * ca).b;
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d).a;
                return float4(r, g, b, a);
            }
            ENDHLSL
        }
    }
}
