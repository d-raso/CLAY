Shader "CLAY/SurfaceWater"
{
    // Planet-surface seas. A camera-centred radial grid (dense at your feet, out to the horizon) displaced by Gerstner
    // swells whose speed obeys the planet's gravity (deep-water dispersion c = √(g/k)) and whose height follows wind,
    // gravity and the liquid. Shading: REFRACTION of the scene behind the surface (opaque texture) with per-channel
    // Beer–Lambert ABSORPTION over the water depth (depth texture) — the liquid's chemistry decides its colour; Fresnel
    // REFLECTION of the sky colours; sun glint; SHORE foam where the water is shallow and CREST foam in strong wind.
    Properties
    {
        _Absorb("Absorption per metre (rgb)", Vector) = (0.45, 0.09, 0.05, 0)
        _Scatter("In-scatter colour (rgb)", Color) = (0.03, 0.12, 0.16, 1)
        _SkyZenith("Sky zenith", Color) = (0.25, 0.45, 0.8, 1)
        _SkyHorizon("Sky horizon", Color) = (0.6, 0.72, 0.85, 1)
        _WaveAmp("Wave amplitude scale", Float) = 1
        _WaveLen("Base wavelength (m)", Float) = 24
        _Gravity("Gravity (m/s²)", Float) = 9.81
        _Choppy("Choppiness", Range(0, 1)) = 0.6
        _Foam("Foam amount", Range(0, 2)) = 1
        _Smooth("Surface smoothness", Range(0, 1)) = 0.95
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ FOG_EXP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Absorb, _Scatter, _SkyZenith, _SkyHorizon;
                float _WaveAmp, _WaveLen, _Gravity, _Choppy, _Foam, _Smooth;
            CBUFFER_END
            float _CurvK;
            float4 _SurfOrigin;     // floating-origin offset (so the waves don't jump on rebase)
            float4 _WaterWind;      // xy = wind direction, z = strength

            struct A { float4 positionOS : POSITION; };
            struct V
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float4 screen : TEXCOORD2; float crest : TEXCOORD3; float fog : TEXCOORD4; float2 wp : TEXCOORD5;
            };

            float h12(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float n2(float2 x) { float2 p = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
                return lerp(lerp(h12(p), h12(p + float2(1,0)), f.x), lerp(h12(p + float2(0,1)), h12(p + float2(1,1)), f.x), f.y); }

            // Six Gerstner swells fanned around the wind direction; each obeys deep-water dispersion ω = √(g·k).
            void Gerstner(float2 p, out float3 disp, out float3 nrm, out float crest)
            {
                disp = 0; float3 tx = float3(1, 0, 0), tz = float3(0, 0, 1); crest = 0;
                float2 wd = normalize(_WaterWind.xy + 1e-4);
                [unroll] for (int i = 0; i < 6; i++)
                {
                    float fi = i;
                    float ang = (h12(float2(fi, 3.7)) - 0.5) * 1.6;                       // ±45° around the wind
                    float2 dir = float2(wd.x * cos(ang) - wd.y * sin(ang), wd.x * sin(ang) + wd.y * cos(ang));
                    float L = _WaveLen * pow(0.62, fi) * (0.8 + 0.4 * h12(float2(fi, 9.1)));
                    float k = 6.2831853 / L;
                    float w = sqrt(_Gravity * k);
                    float a = L * 0.012 * _WaveAmp * (1.0 - fi * 0.08);
                    float q = _Choppy / max(k * a * 6.0, 1e-3);                             // steepness, never looping over
                    float ph = k * dot(dir, p) - w * _Time.y + h12(float2(fi, 1.3)) * 6.28;
                    float s = sin(ph), c = cos(ph);
                    disp += float3(q * a * dir.x * c, a * s, q * a * dir.y * c);
                    float wa = k * a;
                    tx += float3(-q * dir.x * dir.x * wa * s, dir.x * wa * c, -q * dir.x * dir.y * wa * s);
                    tz += float3(-q * dir.x * dir.y * wa * s, dir.y * wa * c, -q * dir.y * dir.y * wa * s);
                    crest += saturate(s) * wa;
                }
                nrm = normalize(cross(tz, tx));
            }

            V vert(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float2 wp = ws.xz + _SurfOrigin.xy;
                float3 disp, nrm; float crest;
                // swells fade out with distance (they'd only alias), leaving a flat, reflective far sea
                float dist = distance(ws.xz, _WorldSpaceCameraPos.xz);
                float near = 1.0 - smoothstep(300.0, 2500.0, dist);
                Gerstner(wp, disp, nrm, crest);
                ws += disp * near;
                o.normalWS = normalize(lerp(float3(0, 1, 0), nrm, near));
                o.crest = crest * near;
                float2 d = ws.xz - _WorldSpaceCameraPos.xz; ws.y -= dot(d, d) * _CurvK;
                o.positionWS = ws; o.wp = wp;
                o.positionCS = TransformWorldToHClip(ws);
                o.screen = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 SkyColour(float3 r)
            {
                float t = saturate(r.y);
                return lerp(_SkyHorizon.rgb, _SkyZenith.rgb, pow(t, 0.5));
            }

            half4 frag(V i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                bool below = !IS_FRONT_VFACE(facing, true, false);
                float3 Vd = normalize(_WorldSpaceCameraPos - i.positionWS);
                // detail ripples on top of the swells
                float2 p = i.wp; float t = _Time.y; float e = 0.12;
                float r0 = n2(p * 0.9 + t * 0.35) * 0.6 + n2(p * 2.7 - t * 0.6) * 0.4;
                float rx = n2((p + float2(e, 0)) * 0.9 + t * 0.35) * 0.6 + n2((p + float2(e, 0)) * 2.7 - t * 0.6) * 0.4;
                float rz = n2((p + float2(0, e)) * 0.9 + t * 0.35) * 0.6 + n2((p + float2(0, e)) * 2.7 - t * 0.6) * 0.4;
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                float rip = 0.25 * saturate(1.0 - dist / 400.0) * (0.4 + _WaterWind.z);
                float3 N = normalize(i.normalWS + float3(-(rx - r0) / e * rip, 0, -(rz - r0) / e * rip));
                if (below) N = -N;

                // scene behind the surface: refracted colour + water depth along the view
                float2 suv = i.screen.xy / i.screen.w;
                float rawD = SampleSceneDepth(suv);
                float sceneEye = LinearEyeDepth(rawD, _ZBufferParams);
                float surfEye = i.screen.w;
                float thick = max(sceneEye - surfEye, 0.0);
                float2 refrUV = suv + N.xz * 0.04 * saturate(thick * 0.5);
                float rawD2 = SampleSceneDepth(refrUV);
                if (LinearEyeDepth(rawD2, _ZBufferParams) < surfEye) refrUV = suv;          // don't refract things in front
                else thick = max(LinearEyeDepth(rawD2, _ZBufferParams) - surfEye, 0.0);
                float3 scene = SampleSceneColor(refrUV);

                // Beer–Lambert absorption per channel + in-scattered liquid colour
                float3 trans = exp(-_Absorb.rgb * thick);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 lit = L.color * L.distanceAttenuation * lerp(0.4, 1.0, L.shadowAttenuation);
                float3 inscat = _Scatter.rgb * (SampleSH(float3(0, 1, 0)) + lit * saturate(L.direction.y + 0.2));
                float3 body = scene * trans + inscat * (1.0 - trans);

                // reflection: sky + sun glint
                float fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(N, Vd)), 5.0);
                float3 R = reflect(-Vd, N);
                float3 refl = SkyColour(R);
                float3 H = normalize(L.direction + Vd);
                float sp = exp2(_Smooth * 11.0 + 1.0);
                float glint = pow(saturate(dot(N, H)), sp) * (sp + 8.0) / 25.0 * 0.6;
                float3 col = below ? body : lerp(body, refl, fres) + lit * glint;

                // foam: along the shore (thin water) and on wind-driven crests
                float fn = n2(p * 1.8 + t * 0.4) * 0.6 + n2(p * 5.1 - t * 0.7) * 0.4;
                float shore = (1.0 - smoothstep(0.0, 0.9, thick)) * smoothstep(0.35, 0.65, fn + (1.0 - saturate(thick)) * 0.4);
                float crestF = smoothstep(0.55, 0.9, i.crest * (0.6 + _WaterWind.z) + fn * 0.3) * saturate(_WaterWind.z * 1.5);
                float foam = saturate((shore + crestF) * _Foam) * (below ? 0.0 : 1.0);
                col = lerp(col, (SampleSH(float3(0, 1, 0)) + lit * saturate(dot(float3(0, 1, 0), L.direction) + 0.3)) * 0.9, foam);

                col = MixFog(col, i.fog);
                col = (any(isnan(col)) || any(isinf(col))) ? float3(0, 0, 0) : max(col, 0);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
