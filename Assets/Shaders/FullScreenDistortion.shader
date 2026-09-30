Shader "Hidden/FullScreenDistortion"
{
    Properties { _MainTex("Screen", 2D) = "white" {} }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _DistortionStrength;
            float _ChromaticAberration;
            float _NoiseSpeed;
            float _NoiseScale;
            float _EffectTime;
            float _DebugMode;
            float4 _GlobalFlowDirection;
            float  _WaterTemperature;
            float  _WaterTurbulence;
            // Cell movement ripples: (screenU, screenV, radiusUV, strength)
            float4 _Ripple0, _Ripple1, _Ripple2, _Ripple3;

            float2 hash2(float2 p)
            {
                p = float2(dot(p,float2(127.1,311.7)), dot(p,float2(269.5,183.3)));
                return frac(sin(p)*43758.5453) * 2 - 1;
            }
            float gnoise(float2 p)
            {
                float2 i=floor(p), f=frac(p), u=f*f*(3-2*f);
                return lerp(lerp(dot(hash2(i),           f),
                                 dot(hash2(i+float2(1,0)),f-float2(1,0)),u.x),
                            lerp(dot(hash2(i+float2(0,1)),f-float2(0,1)),
                                 dot(hash2(i+float2(1,1)),f-float2(1,1)),u.x),u.y);
            }
            float fbm(float2 p)
            {
                float v=0, a=0.52;
                float2x2 rot = float2x2(1.6,1.2,-1.2,1.6);
                for(int i=0;i<4;i++){ v+=gnoise(p)*a; p=mul(rot,p); a*=0.48; }
                return v;
            }

            // Ripple: outward ring lens distortion from a single ripple event
            float2 AddRipple(float2 uv, float4 rp, float aspect)
            {
                if (rp.w <= 0.001) return float2(0,0);
                float2 diff = float2((uv.x - rp.x) * aspect, uv.y - rp.y);
                float  dist = length(diff);
                if (dist < 0.0001) return float2(0,0);
                // Ring wave: sinc-like pulse centred on the expanding radius
                float phase = (dist - rp.z) * 60.0; // ring spacing
                float ring  = sin(phase) * exp(-phase*phase * 0.04) * rp.w;
                // Falloff with distance from ring centre
                ring *= exp(-abs(dist - rp.z) * 18.0);
                return normalize(diff) * ring;
            }

            half4 frag(v2f_img i) : SV_Target
            {
                float2 uv  = i.uv;
                float  t   = _EffectTime * _NoiseSpeed;
                float2 flo = _GlobalFlowDirection.xy;
                float  asp = _ScreenParams.x / _ScreenParams.y;

                // Layer 1: large slow warp following flow
                float2 uv1 = uv * _NoiseScale + float2(t*0.25, t*0.08) + flo * t * 0.04;
                float2 warp = float2(fbm(uv1), fbm(uv1 + float2(5.2, 1.7)));

                // Layer 2: medium ripples, counter-drift
                float2 uv2 = uv * _NoiseScale * 2.8 + float2(-t*0.6, t*0.4) - flo * t * 0.02;
                float2 rip = float2(gnoise(uv2), gnoise(uv2 + float2(3.1, 8.4)));

                // Layer 3: fine fast surface tension
                float2 uv3 = uv * _NoiseScale * 6.0 + float2(t*1.1, -t*0.9);
                float2 surf = float2(gnoise(uv3), gnoise(uv3 + float2(7.3, 2.1)));

                float2 off = warp * 0.55 + rip * 0.30 + surf * 0.15;

                // Heat shimmer
                float temp01 = saturate((_WaterTemperature + 15.0) / 75.0);
                float shimmer = sin(uv.y * 28 + t * 4.5) * sin(uv.x * 16 + t * 3.3) * temp01;
                off.x += shimmer * 0.5;
                off.y += cos(uv.x * 22 + t * 3.8) * temp01 * 0.3;

                // Turbulence amplifies everything
                float turb = saturate(_WaterTurbulence);
                float str = _DistortionStrength * (1.0 + turb * 2.5);
                float2 d = off * str;

                // Cell movement ripples — expanding ring lens distortions
                float ripStr = str * 0.9 + 0.012; // ripples always slightly visible
                d += AddRipple(uv, _Ripple0, asp) * ripStr;
                d += AddRipple(uv, _Ripple1, asp) * ripStr;
                d += AddRipple(uv, _Ripple2, asp) * ripStr;
                d += AddRipple(uv, _Ripple3, asp) * ripStr;

                if (_DebugMode > 0.5) return half4(saturate(d*5+0.5), 0, 1);

                // Chromatic aberration along total distortion direction
                float ca = _ChromaticAberration * (1.0 + temp01 * 0.6);
                float2 caDir = length(d) > 0.001 ? normalize(d) * ca : float2(ca, 0);
                half r = tex2D(_MainTex, uv + d + caDir).r;
                half g = tex2D(_MainTex, uv + d).g;
                half b = tex2D(_MainTex, uv + d - caDir).b;
                return half4(r, g, b, 1);
            }
            ENDCG
        }
    }
}
