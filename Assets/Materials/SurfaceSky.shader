Shader "CLAY/SurfaceSky"
{
    // Sky dome for the planet surface: drawn first (Background queue, no depth write) on a camera-centred sphere.
    // Zenith → horizon gradient with a haze band whose colour equals the fog colour exactly, so distant terrain
    // dissolves into the sky instead of meeting a flat wall. Sun disc + forward-scattering (Mie-like) glow, and a
    // darker ground haze below the horizon.
    Properties
    {
        _Zenith("Zenith", Color) = (0.2, 0.4, 0.8, 1)
        _Horizon("Horizon", Color) = (0.7, 0.8, 0.9, 1)
        _Ground("Ground Haze", Color) = (0.3, 0.3, 0.3, 1)
        _SunColor("Sun Color", Color) = (1, 0.95, 0.85, 1)
        _SunDir("Sun Direction", Vector) = (0, 1, 0, 0)
        _SunSize("Sun Size", Range(0.0005, 0.05)) = 0.004
        _Haze("Haze Thickness", Range(0.02, 1)) = 0.25
        _Glow("Sun Glow", Range(0, 4)) = 1
        _Stars("Star Visibility", Range(0, 1)) = 0
        _BandDir("Galactic Plane Normal", Vector) = (0.3, 0.8, 0.5, 0)
        _Sun2Dir("Second Sun Direction (w = relative brightness)", Vector) = (0, -1, 0, 0)
        _Sun2Color("Second Sun Color", Color) = (1, 0.8, 0.6, 1)
        _Sun2Size("Second Sun Size", Range(0.0005, 0.05)) = 0.003
        _KR("Rayleigh zenith optical depth (rgb)", Vector) = (0.04, 0.1, 0.24, 0)
        _KM("Mie colour (rgb) + zenith depth (w)", Vector) = (1, 1, 1, 0.02)
        _KA("Absorption zenith depth (rgb)", Vector) = (0, 0, 0, 0)
        _SunI("Sun intensity (x = sun1, y = sun2)", Vector) = (1, 0, 0, 0)
        _SkyGain("Sky gain", Float) = 14
        _Air("Has atmosphere", Float) = 0
        _StarCube("System-view star background", Cube) = "black" {}
        _UseCube("Use star cubemap", Float) = 0
        _CubeGain("Cubemap gain", Float) = 1.5
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Front ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Include/Clouds.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith, _Horizon, _Ground, _SunColor, _SunDir, _BandDir;
                float _SunSize, _Haze, _Glow, _Stars, _UseCube, _CubeGain;
                float4 _Sun2Dir, _Sun2Color; float _Sun2Size;
                float4 _KR, _KM, _KA, _SunI; float _SkyGain, _Air;
            CBUFFER_END
            float4x4 _StarRot;
            float4 _SurfOrigin;   // local sky direction → the system view's world frame (rotates with the planet's spin)
            TEXTURECUBE(_StarCube); SAMPLER(sampler_StarCube);

            float h31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
            float3 h33(float3 p) { p = frac(p * float3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return frac((p.xxy + p.yxx) * p.zyx); }
            // One star layer: each cell of a direction-space grid may hold one star at a jittered point.
            float3 StarLayer(float3 d, float scale, float density)
            {
                float3 p = d * scale, ip = floor(p), fp = frac(p);
                float h = h31(ip);
                if (h < 1.0 - density) return 0;
                float3 c = h33(ip) * 0.7 + 0.15;
                float r = length(fp - c);
                float mag = (h - (1.0 - density)) / density;                 // 0..1 brightness
                float core = smoothstep(0.09, 0.0, r) * mag * mag * 2.5;
                float3 tint = lerp(float3(1.0, 0.72, 0.5), float3(0.7, 0.82, 1.0), h33(ip + 7.0).x);   // red dwarfs ↔ hot blue stars
                return core * tint;
            }
            float vn3(float3 x) { float3 p = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(h31(p), h31(p + float3(1,0,0)), f.x), lerp(h31(p + float3(0,1,0)), h31(p + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(h31(p + float3(0,0,1)), h31(p + float3(1,0,1)), f.x), lerp(h31(p + float3(0,1,1)), h31(p + float3(1,1,1)), f.x), f.y), f.z); }
            // ── single-scattering atmosphere (Rayleigh + Mie + absorption), per planet ──
            // Relative air mass along a direction (Kasten & Young), finite at and just below the horizon.
            float AirMass(float y)
            {
                float z = degrees(acos(clamp(y, -0.05, 1.0)));
                return 1.0 / (max(y, -0.05) + 0.50572 * pow(max(96.07995 - z, 0.5), -1.6364));
            }
            float3 Scatter(float3 d, float3 sd, float3 sunCol)
            {
                float mu = dot(d, sd);
                float phR = 0.0596831 * (1.0 + mu * mu);                          // 3/(16π)(1+μ²)
                const float g = 0.76;
                float phM = 0.0795775 * (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * mu, 1e-4), 1.5);
                float3 kR = _KR.rgb, kM = _KM.w * _KM.rgb, kA = _KA.rgb;
                float3 kT = kR + _KM.w + kA;                                       // total extinction per air mass
                float3 viewT = exp(-kT * AirMass(d.y));                            // transmittance to space along the view
                float3 sunT = exp(-kT * AirMass(sd.y));                            // sunlight reaching the air (reddened)
                float3 scat = (kR * phR + kM * phM) / max(kT, 1e-4);
                float daylight = smoothstep(-0.12, 0.02, sd.y);                    // twilight: sun just below the horizon
                return sunCol * sunT * scat * (1.0 - viewT) * _SkyGain * daylight;
            }

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.dir = ws - _WorldSpaceCameraPos;
                o.positionCS = TransformWorldToHClip(ws);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sd = normalize(_SunDir.xyz);
                float up = d.y;
                // haze band hugs the horizon; its width is the atmosphere's thickness
                float t = saturate(up / max(_Haze, 0.02));
                float3 sky = lerp(_Horizon.rgb, _Zenith.rgb, pow(t, 0.55));
                float3 below = lerp(_Horizon.rgb, _Ground.rgb, saturate(-up * 4.0));
                float3 col = up >= 0 ? sky : below;
                if (_Air > 0.5)
                {
                    // physically based sky: blue by day, red at sunset, hazy toward the horizon; the second sun adds its own
                    float3 dv = float3(d.x, max(d.y, 0.0), d.z);
                    col = Scatter(normalize(dv), sd, _SunColor.rgb * _SunI.x);
                    if (_Sun2Dir.w > 0.0005) col += Scatter(normalize(dv), normalize(_Sun2Dir.xyz), _Sun2Color.rgb * _SunI.y);
                    col = lerp(col, col * 0.6 + _Ground.rgb * 0.15, saturate(-up * 3.0));   // below the horizon line
                    col += _Zenith.rgb * 0.15;                                             // night-sky airglow / floor
                }
                // starfield + faint galactic band — what a dark sky actually is (so black reads as space, not a wall)
                if (_Stars > 0.001 && _UseCube > 0.5)
                {
                    // the exact sky of the system view: galaxy stars, band and nebulae, turning with the planet
                    float3 wd = mul((float3x3)_StarRot, d);
                    float3 bg = SAMPLE_TEXTURECUBE_LOD(_StarCube, sampler_StarCube, wd, 0).rgb;
                    col += bg * _CubeGain * _Stars * saturate(up * 25.0 + 0.2);
                }
                else if (_Stars > 0.001)
                {
                    float3 st = StarLayer(d, 180.0, 0.018) + StarLayer(d, 420.0, 0.03) * 0.55 + StarLayer(d, 900.0, 0.05) * 0.3;
                    float bandD = abs(dot(d, normalize(_BandDir.xyz)));
                    float band = exp(-bandD * bandD * 40.0) * (0.55 + 0.45 * vn3(d * 9.0)) * (0.6 + 0.4 * vn3(d * 23.0));
                    st += StarLayer(d, 1400.0, 0.12) * band * 0.6;                        // dense faint stars in the band
                    float3 glow = float3(0.55, 0.52, 0.62) * band * 0.035;
                    float horizonFade = saturate(up * 25.0 + 0.2);                    // stars dim right at the horizon line
                    col += (st + glow) * _Stars * horizonFade;
                }
                // forward-scattering glow around the sun + the disc itself
                float cs = saturate(dot(d, sd));
                float sunUp = saturate(sd.y * 4.0 + 0.3);
                col += _SunColor.rgb * (pow(cs, 8.0) * 0.35 + pow(cs, 64.0) * 0.6) * _Glow * sunUp;
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.6, cs);
                float3 discT = _Air > 0.5 ? exp(-(_KR.rgb + _KM.w + _KA.rgb) * AirMass(sd.y)) : float3(1, 1, 1);   // reddened at the horizon
                col += _SunColor.rgb * 4.0 * discT * disc * saturate(sd.y * 20.0 + 1.0);   // ADD: a hazy sun dims, it never goes darker than the sky
                // a second star: its own glow and disc (brightness relative to the main sun)
                if (_Sun2Dir.w > 0.0005)
                {
                    float3 sd2 = normalize(_Sun2Dir.xyz);
                    float cs2 = saturate(dot(d, sd2));
                    float up2 = saturate(sd2.y * 4.0 + 0.3);
                    col += _Sun2Color.rgb * (pow(cs2, 8.0) * 0.35 + pow(cs2, 64.0) * 0.6) * _Glow * up2 * _Sun2Dir.w;
                    float disc2 = smoothstep(1.0 - _Sun2Size, 1.0 - _Sun2Size * 0.6, cs2);
                    col = lerp(col, _Sun2Color.rgb * (1.5 + 2.5 * _Sun2Dir.w), disc2 * saturate(sd2.y * 20.0 + 1.0));
                }
                // ── CLOUDS: raymarch the weather deck (18 steps), lit by the sun through the air (Beer + powder + HG phase)
                if (_Air > 0.5 && _CloudShape.x > 0.01)
                {
                    float3 ro = float3(_WorldSpaceCameraPos.x + _SurfOrigin.x, _WorldSpaceCameraPos.y, _WorldSpaceCameraPos.z + _SurfOrigin.y);
                    float base = _CloudShape.z, top = base + _CloudShape.w;
                    float t0 = 0, t1 = -1;
                    if (ro.y < base)      { if (d.y > 0.01) { t0 = (base - ro.y) / d.y; t1 = (top - ro.y) / d.y; } }
                    else if (ro.y > top)  { if (d.y < -0.01) { t0 = (top - ro.y) / d.y; t1 = (base - ro.y) / d.y; } }
                    else                  { t0 = 0; t1 = d.y > 0.01 ? (top - ro.y) / d.y : (d.y < -0.01 ? (base - ro.y) / d.y : 30000.0); }
                    t1 = min(t1, t0 + 30000.0);
                    if (t1 > t0 && t0 < 90000.0)
                    {
                        const int STEPS = 18;
                        float dt = (t1 - t0) / STEPS;
                        float t = t0 + dt * h31(d * 997.0 + _Time.y);                    // jitter hides banding
                        float T = 1; float3 acc = 0;
                        float mu = dot(d, sd);
                        float g1 = 0.6, g2 = -0.2;                                        // forward lobe + a soft back lobe
                        float ph = lerp(0.0795775 * (1 - g2 * g2) / pow(1 + g2 * g2 - 2 * g2 * mu, 1.5),
                                        0.0795775 * (1 - g1 * g1) / pow(1 + g1 * g1 - 2 * g1 * mu, 1.5), 0.7) * 12.566;
                        float3 sunC = _SunColor.rgb * _SunI.x * exp(-(_KR.rgb + _KM.w + _KA.rgb) * AirMass(sd.y));   // sunlight reaching the deck
                        float3 amb = _Zenith.rgb * 1.2 + _Horizon.rgb * 0.4;
                        [loop] for (int k = 0; k < STEPS; k++)
                        {
                            float3 pos = ro + d * t;
                            float dens = CloudDensity(pos);
                            if (dens > 0.001)
                            {
                                float toward = CloudDensity(pos + sd * 250.0) + CloudDensity(pos + sd * 800.0) * 0.6;
                                float light = exp(-toward * 3.0) * (1.0 - exp(-dens * 4.0) * 0.5);   // Beer + powder
                                float hh = saturate((pos.y - base) / max(_CloudShape.w, 1.0));
                                float3 c = sunC * light * ph * 0.35 + amb * (0.45 + 0.55 * hh);
                                float a = 1.0 - exp(-dens * dt * 0.0035);
                                acc += T * a * c;
                                T *= 1.0 - a;
                                if (T < 0.02) break;
                            }
                            t += dt;
                        }
                        // distant clouds melt into the haze
                        float haze = saturate((t0 - 20000.0) / 60000.0);
                        acc = lerp(acc, _Horizon.rgb * (1.0 - T), haze);
                        col = col * T + acc;
                    }
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
