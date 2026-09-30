Shader "CLAY/GasGiant"
{
    // A live, animated gas-giant surface computed entirely per-pixel — never blurry, never static. Latitude
    // bands are domain-warped by animated fBm turbulence and sheared by differential rotation (equator spins
    // faster than the poles), with churning storm vortices, so the surface flows over time. Unlit CG so it
    // works under the URP 2D renderer. Set _SunDir per body each frame; _Seed decorrelates each planet.
    Properties
    {
        _ColEq("Equator hue", Color)  = (0.82, 0.62, 0.40, 1)
        _ColMid("Mid-lat hue", Color) = (0.72, 0.58, 0.44, 1)
        _ColPole("Polar hue", Color)  = (0.52, 0.52, 0.58, 1)
        _StormColor("Storm", Color)   = (0.86, 0.42, 0.26, 1)
        _Chem1("Chem constituent 1", Color) = (0.92, 0.9, 0.82, 1)
        _Chem2("Chem constituent 2", Color) = (0.6, 0.32, 0.22, 1)
        _ChemAmt("Chemical complexity", Range(0,1)) = 0.3
        _SpotColor("Great-spot color", Color) = (0.72, 0.30, 0.20, 1)
        _Spot("Great spot (lat,lon,size,strength)", Vector) = (0.25, 1.0, 0.4, 0)
        _SunDir("Sun Direction (world)", Vector) = (0, 0, -1, 0)
        _Seed("Seed Offset", Vector) = (0, 0, 0, 0)
        _BandFreq("Band Frequency", Range(2, 20)) = 8
        _Turb("Turbulence", Range(0, 2)) = 0.7
        _FineTurb("Fine Turbulence", Range(0, 1.5)) = 0.5
        _BandVar("Band Width Variation", Range(0, 2)) = 0.8
        _Speed("Flow Speed", Range(0, 1)) = 0.12
        _Ambient("Ambient", Range(0, 0.4)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 od : TEXCOORD0; float3 wn : TEXCOORD1; };

            half4 _ColEq, _ColMid, _ColPole, _StormColor, _Chem1, _Chem2, _SpotColor;
            float4 _Spot;
            float _ChemAmt, _FineTurb, _BandVar;
            float4 _SunDir, _Seed, _CamPosObj;
            float4 _RingNormalObj;   // xyz = ring-plane normal (object space), w = 1 if a ring exists
            float4 _RingRadii;       // x = inner radius, y = outer radius (object-space units, planet r = 0.5)
            float _BandFreq, _Turb, _Speed, _Ambient;

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }
            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(i + float3(0,0,0)), hash(i + float3(1,0,0)), f.x),
                                 lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x),
                                 lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.02; a *= 0.5; }
                return s;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.od = v.vertex.xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.od);
                float3 seed = _Seed.xyz;
                float t = _Time.y * _Speed;
                float lat = n.y;

                // Uniform planetary rotation (spin the whole pattern rigidly around the pole axis). A
                // latitude-dependent rate would shear the two hemispheres in opposite directions and make the
                // bands converge into a chevron at the equator, so keep it uniform.
                float ang = t * 0.6;
                float ca = cos(ang), sa = sin(ang);
                float3 rn = float3(ca * n.x - sa * n.z, n.y, sa * n.x + ca * n.z);

                // ── Great spot: instead of overlaying a clean oval on top, WARP the sampling frame so the bands
                // and clouds themselves spiral into the vortex (embedded in the flow, with an irregular edge).
                float stormMask = 0.0;
                if (_Spot.w > 0.01)
                {
                    float slon = atan2(rn.z, rn.x);
                    float dlon = atan2(sin(slon - _Spot.y), cos(slon - _Spot.y));
                    float2 sd = float2(dlon * 0.5, (lat - _Spot.x) * 1.5) / max(_Spot.z, 1e-3);
                    float edgeN = fbm(rn * 6.0 + seed * 9.0) - 0.5;         // ragged, non-perfect outline
                    float rr = length(sd) * (1.0 + edgeN * 0.45);
                    stormMask = 1.0 - smoothstep(0.7, 1.2, rr);
                    // Tangential swirl displacement (perpendicular to radial), stronger toward the centre, turning.
                    float3 T = normalize(cross(rn, float3(0, 1, 0)) + float3(1e-4, 0, 0));
                    float3 Bt = cross(rn, T);
                    float2 tang = float2(-sd.y, sd.x);
                    // Many turns toward the eye → a tight spiral vortex, not just a gentle band warp.
                    float swirl = stormMask * (1.2 + 3.0 * (1.0 - min(rr, 1.0))) * _Spot.z;
                    rn = normalize(rn + (T * tang.x + Bt * tang.y) * swirl * 1.7);
                }

                // ── Turbulent flow, stretched EAST-WEST — real zonal winds elongate features along the bands.
                // Sampling latitude (y) at higher frequency makes features thin in latitude and long in
                // longitude. Two iterated domain warps give curling, flowing turbulence (iq-style).
                float3 aniso = float3(1.0, 2.2, 1.0);
                float3 pw = rn * aniso;
                float3 q = float3(fbm(pw * 1.3 + seed + float3(0.0, t * 0.22, 0.0)),
                                  fbm(pw * 1.3 + seed + float3(5.2, 1.3, t * 0.17)),
                                  fbm(pw * 1.3 + seed + float3(1.7, 9.2, 3.3))) - 0.5;
                float3 r = float3(fbm(pw * 1.3 + 3.0 * q + float3(1.7, 9.2, t * 0.20)),
                                  fbm(pw * 1.3 + 3.0 * q + float3(8.3, 2.8, t * 0.18)),
                                  fbm(pw * 1.3 + 3.0 * q + float3(2.1, 6.5, t * 0.15))) - 0.5;

                // Multi-scale cloud density: coarse swirls + fine wisps (so it's not smooth/marbly). Fine
                // detail frequency and strength scale with _FineTurb.
                float clouds = fbm(pw * 2.2 + 4.0 * r + float3(t * 0.08, 0, 0));
                float fine   = fbm(pw * (4.5 + _FineTurb * 6.0) + 3.0 * r + float3(t * 0.15, 0, 0));

                // Bands: latitude with a gentle turbulent warp — belts wander but stay coherent.
                float latW = lat + r.y * (0.12 + _Turb * 0.22) + q.y * 0.1;
                // Non-uniform band WIDTHS: perturb the band phase with low-frequency, per-planet terms so some
                // belts are narrow and some wide. _BandVar scales how uneven the widths are.
                float phase = latW * _BandFreq
                            + _BandVar * (0.9 * sin(latW * 3.1 + seed.x * 6.2831)
                                        + 0.5 * sin(latW * 5.7 + seed.y * 6.2831)
                                        + 0.3 * sin(latW * 9.3 + seed.z * 6.2831));
                float band = 0.5 + 0.5 * sin(phase * 3.14159265);

                // Turbulent mixing concentrated at belt/zone BOUNDARIES (festoons, curls, filaments) — the
                // hallmark of a real gas giant — plus fine wisps everywhere.
                float boundary = 1.0 - abs(band * 2.0 - 1.0);            // 1 at band edges, 0 at band centres
                float v = lerp(band, clouds, (0.25 + _Turb * 0.5) * (0.4 + 0.6 * boundary));
                v += (fine - 0.5) * (0.08 + _FineTurb * 0.4);
                v = saturate(v);

                // Colour: a latitude-dependent HUE (equator → mid → pole, symmetric N/S — physical, and being
                // a colour lookup it doesn't cause the rotation-shear artifact), with the band value only
                // SOFTLY modulating belt (darker/warmer) vs zone (brighter). Soft transition = low contrast.
                float aLat = abs(latW);
                half3 latHue = aLat < 0.5 ? lerp(_ColEq.rgb, _ColMid.rgb, aLat * 2.0)
                                          : lerp(_ColMid.rgb, _ColPole.rgb, (aLat - 0.5) * 2.0);

                // Three tonal shades of the hue — dark / mid / light — for depth.
                half3 toneDark  = latHue * half3(0.56, 0.52, 0.50);
                half3 toneMid   = latHue;
                half3 toneLight = saturate(latHue * 1.22 + 0.12);

                // The tone LEVEL blends the band with the cloud turbulence, so colour varies WITHIN each band
                // (nebulous, layered gas) instead of every band being one flat colour.
                float level = smoothstep(0.18, 0.82, v * 0.5 + clouds * 0.5);
                half3 col = level < 0.5 ? lerp(toneDark, toneMid, level * 2.0)
                                        : lerp(toneMid, toneLight, (level - 0.5) * 2.0);

                // Per-band ALBEDO and HUE: slowly-varying with the band phase, so adjacent belts/zones have
                // different brightness and a slight colour shift (real giants' bands differ band-to-band).
                float bandCoord = phase * 0.5;
                col *= 0.76 + 0.48 * fbm(float3(bandCoord, seed.x * 3.0, 0.0));
                float bh = fbm(float3(bandCoord + 5.0, seed.y * 3.0, 0.0)) - 0.5;
                col *= half3(1.0 + bh * 0.26, 1.0 + bh * 0.03, 1.0 - bh * 0.24);

                // Large-scale warm/cool hue drift so no band is a single hue, plus fine grain so it reads as
                // gas rather than paint.
                float warm = fbm(pw * 0.9 + seed * 7.0 + float3(t * 0.04, 0, 0)) - 0.5;
                col *= half3(1.0 + warm * 0.14, 1.0 + warm * 0.03, 1.0 - warm * 0.12);
                col *= (0.92 - _FineTurb * 0.04) + (0.12 + _FineTurb * 0.16) * fine;

                // Chemical complexity: patches of distinct cloud constituents in their own accent hues, each
                // driven by its own noise field. Higher _ChemAmt → stronger, more colour variety.
                float chem1 = smoothstep(0.5, 0.8, fbm(pw * 1.7 + seed * 11.0 + float3(t * 0.05, 0, 0)));
                col = lerp(col, col * 0.2 + _Chem1.rgb * 0.8, chem1 * _ChemAmt * 0.75);
                float chem2 = smoothstep(0.55, 0.84, fbm(pw * 2.6 + r * 2.0 + seed * 13.0 + float3(0, t * 0.06, 0)));
                col = lerp(col, col * 0.2 + _Chem2.rgb * 0.8, chem2 * _ChemAmt * 0.75);

                // Great spot — a big persistent oval vortex at a fixed body-frame position (like Jupiter's Great
                // Red Spot / Neptune's Great Dark Spot), elongated E-W, with an internal swirl. Rotates with the
                // planet (uses the rotated body longitude), so it drifts across the disc as it spins.
                // The storm's swirl is already baked into the clouds above (via the sampling-frame warp); here
                // just tint the region toward the storm hue and deepen its contrast so it reads as a storm.
                if (stormMask > 0.001)
                {
                    // Fill the vortex with the planet's DARKEST tone; the swirled cloud value keeps internal
                    // filament structure so it reads as a churning storm rather than a flat dark oval.
                    half3 stormCore = toneDark * 0.5;
                    col = lerp(col, stormCore * (0.7 + 0.55 * v), saturate(stormMask * 1.25));
                }

                // Small storm vortices — oval spots biased to mid-latitudes, swirled by the same flow.
                float storm = smoothstep(0.74, 0.9, fbm(rn * 3.2 + 3.0 * r + seed * 3.0 + float3(t * 0.12, 0.0, t * 0.1)));
                storm *= smoothstep(0.06, 0.3, abs(lat)) * (1.0 - smoothstep(0.55, 0.82, abs(lat)));
                col = lerp(col, _StormColor.rgb, storm * 0.6);

                // Day/night lighting + gentle limb darkening, in OBJECT space (scale-safe): _SunDir is the
                // object-space direction toward the star and _CamPosObj the object-space camera position. The
                // world-normal / world-to-object transforms lose precision at distant planets' tiny scales,
                // which had inverted the giant's lit side.
                float3 N = normalize(i.od);
                float3 L = normalize(_SunDir.xyz);
                float ndl = saturate(dot(N, L));
                float lit = _Ambient * 0.35 + (1.0 - _Ambient * 0.35) * ndl;   // darker night side

                // Ring shadow cast ONTO the planet: from this surface point, trace toward the sun; if the ray
                // crosses the ring plane (through the planet centre) inside the ring's annulus, it's shadowed.
                if (_RingNormalObj.w > 0.5)
                {
                    float3 Rn = normalize(_RingNormalObj.xyz);
                    float denom = dot(L, Rn);
                    if (abs(denom) > 1e-4)
                    {
                        float s = -dot(i.od, Rn) / denom;               // distance to the ring plane along L
                        if (s > 0.0)                                    // ring is between the surface and the sun
                        {
                            float rad = length(i.od + s * L);
                            float sh = smoothstep(_RingRadii.x - 0.03, _RingRadii.x + 0.03, rad)
                                     * (1.0 - smoothstep(_RingRadii.y - 0.03, _RingRadii.y + 0.03, rad));
                            // Fade the shadow out smoothly as the surface nears the terminator, instead of a
                            // hard step at ndl≈0 (which sliced the shadow off abruptly where it reached the night
                            // side). Now it dissolves gently into the naturally dark night hemisphere.
                            lit *= 1.0 - sh * 0.6 * smoothstep(0.0, 0.10, ndl);
                        }
                    }
                }

                float3 V = normalize(_CamPosObj.xyz - i.od);
                float limb = lerp(0.7, 1.0, saturate(dot(N, V)));
                half3 outc = col * lit * limb;
                // Faint full-limb halo so the night silhouette reads as a body against the stars, not a dark hole.
                float edge = pow(1.0 - saturate(dot(N, V)), 3.0);
                outc += col * edge * 0.05;
                float g = dot(outc, half3(0.299, 0.587, 0.114));
                outc = lerp(half3(g, g, g), outc, 0.86);   // gently desaturate for realism
                return half4(outc, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
