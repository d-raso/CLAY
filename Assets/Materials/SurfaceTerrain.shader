Shader "CLAY/SurfaceTerrain"
{
    // Planet-surface ground. Albedo comes from per-vertex colour (derived on the CPU from the orbital albedo so the
    // ground matches the view from space). uv0+uv1 carry 8 weights over this planet's TERRAIN-TYPE PALETTE
    // (_TypeIdA/_TypeIdB = SurfaceGeo.TerrainType ids); each type is a procedural PBR surface (relief, detail albedo,
    // tint, smoothness, metalness, glints, emission, backscatter), blended by weight AND height so the taller
    // material wins at a boundary (boulders poke through sand). colour.a = wetness. Plus macro variation, cavity AO,
    // URP shadows, SH ambient, extra suns, fog and horizon curvature (_CurvK = 1/2R).
    Properties
    {
        _Grain("Bump Strength", Range(0,1)) = 0.35
        _GrainScale("Grain Scale (m)", Float) = 3
        _NoCurve("No Curvature (true-sphere meshes)", Float) = 0
        _RockGlint("Meteorite (0 rock, 1 iron-nickel, 2 impact glass)", Float) = 0
        _IsRock("Loose rock mesh (not ground)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ FOG_EXP
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Include/SurfAmbient.hlsl"
            #include "Include/Clouds.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Grain, _GrainScale, _NoCurve, _RockGlint, _IsRock;
            CBUFFER_END
            float _CurvK;
            float4 _SurfOrigin;
            float _RainWet, _SnowCover;
            float4 _RiverColor;   // linear colour of the world's liquid in rivers and lakes   // weather: how soaked the ground is, how much snow has settled   // floating-origin offset (mod 65536 m) so ground patterns don't jump on rebase
            float4 _TypeIdA, _TypeIdB;   // the planet's terrain-type palette (−1 = empty slot)

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float4 mat : TEXCOORD0; float4 mat2 : TEXCOORD1; float4 wtr : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : COLOR; float fog : TEXCOORD2; float3 flatWS : TEXCOORD3; float4 mat : TEXCOORD4; float4 mat2 : TEXCOORD5; float4 wtr : TEXCOORD6; };

            float h13(float3 p) { p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
            float vn(float3 x)
            {
                float3 p = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(h13(p), h13(p + float3(1,0,0)), f.x), lerp(h13(p + float3(0,1,0)), h13(p + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(h13(p + float3(0,0,1)), h13(p + float3(1,0,1)), f.x), lerp(h13(p + float3(0,1,1)), h13(p + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p, int oct) { float s = 0, a = 0.5; [loop] for (int i = 0; i < oct; i++) { s += a * vn(p); p = p * 2.07 + 11.3; a *= 0.5; } return s; }
            float ridged(float3 p) { float s = 0, a = 0.5; [loop] for (int i = 0; i < 3; i++) { float n = 1.0 - abs(vn(p) * 2.0 - 1.0); s += a * n * n; p = p * 2.13 + 7.7; a *= 0.5; } return s; }
            // cellular F2-F1 in the ground plane → crack network (≈0 on cracks)
            float cells(float3 p)
            {
                float3 i = floor(float3(p.x, 0, p.z)), f = float3(frac(p.x), 0, frac(p.z)); float d1 = 8, d2 = 8;
                [loop] for (int z = -1; z <= 1; z++) [loop] for (int x = -1; x <= 1; x++)
                {
                    float3 o = float3(x, 0, z);
                    float3 r = o + float3(h13(i + o), 0, h13(i + o + 17.1)) - f;
                    float d = dot(r, r); if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                return sqrt(d2) - sqrt(d1);
            }

            // ── TERRAIN TYPES — ONE generic procedural PBR surface, driven by a parameter table ─────────────────
            // (ids = SurfaceGeo.TerrainType). Each type is 7 float4 rows:
            //  0: ridgeAmp, ridgeFreq, bumpAmp, bumpFreq          — jointed/ridged rock, soft undulation
            //  1: cellFreq, domeAmp(−=bowl), edgeAmp(+ridge/−crack), edgeWidth   — pebbles, boulders, craters, polygons
            //  2: cellPresence, toneVar, rippleAmp, rippleFreq    — sparse cells, patchiness, wind ripples
            //  3: smoothness, metalness, sparkle, retro           — PBR response (retro = regolith opposition surge)
            //  4: tint.rgb, tintAmt                               — the material's own colour
            //  5: tint2.rgb, tint2Mix                             — second colour in patches (rust, allotropes, blue ice)
            //  6: seamEmission.rgb, cavityGain                    — glowing seams (lava), crevice darkening
            static const float4 TT[147] = {
                // 0 Rocky — jointed, weathered bedrock
                float4(0.30,0.45,0.12,2.3), float4(0.33,0,-0.10,0.04), float4(0,0.18,0,0), float4(0.22,0,0,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 1 Bouldery — boulder field / impact ejecta
                float4(0,0,0.03,4.0), float4(0.55,0.28,0,0), float4(0.35,0.40,0,0), float4(0.20,0,0,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 2 Gravel — desert pavement with varnished pebbles
                float4(0,0,0.03,1.3), float4(7.0,0.025,-0.004,0.06), float4(0,0.55,0,0), float4(0.30,0,0,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 3 Sandy — loose grains, small ripples
                float4(0,0,0.05,0.5), float4(0,0,0,0), float4(0,0.10,0.03,5.0), float4(0.08,0,0.6,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 4 Dunes — sharp-crested ripples, streaked
                float4(0,0,0.12,0.2), float4(0,0,0,0), float4(0,0.10,0.045,3.2), float4(0.06,0,0.5,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 5 Fine soil — silty clods and the odd pebble
                float4(0,0,0.05,1.6), float4(3.3,0.02,0,0), float4(0.82,0.35,0,0), float4(0.05,0,0,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 6 Dried lake — glazed hardpan, faint polygons
                float4(0,0,0.004,0.9), float4(0.14,0,-0.010,0.015), float4(0,0.06,0,0), float4(0.32,0,0,0), float4(0.86,0.82,0.74,0.10), float4(0,0,0,0), float4(0,0,0,0.6),
                // 7 Cracked — desiccation polygons, deep cracks
                float4(0,0,0.006,1.0), float4(0.60,0.015,-0.05,0.05), float4(0,0.10,0,0), float4(0.10,0,0,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 8 Metallic rock — polished iron-nickel with rust blooms
                float4(0.15,0.30,0.01,2.0), float4(0,0,0,0), float4(0,0.10,0,0), float4(0.62,0.90,0,0), float4(0.56,0.55,0.53,0.5), float4(0.42,0.20,0.09,0.9), float4(0,0,0,1),
                // 9 Molten — cooling crust plates over glowing seams
                float4(0,0,0.08,0.3), float4(0.35,0,-0.06,0.16), float4(0,0.05,0.03,6.0), float4(0.40,0,0,0), float4(0.06,0.05,0.05,0.9), float4(0.12,0.06,0.05,0.4), float4(3.5,0.9,0.1,0),
                // 10 Ice sheet — glacial ice, crevasse lines
                float4(0,0,0.15,0.05), float4(0.06,0,-0.05,0.02), float4(0,0.05,0,0), float4(0.82,0,0.3,0), float4(0.66,0.82,0.94,0.25), float4(0.90,0.95,0.99,0.8), float4(0,0,0,0.5),
                // 11 Snow — drifts and sastrugi
                float4(0,0,0.18,0.25), float4(0,0,0,0), float4(0,0.05,0.04,1.1), float4(0.42,0,1.4,0), float4(0.96,0.97,1.00,0.25), float4(0.78,0.87,1.00,0.6), float4(0,0,0,0.5),
                // 12 Grassy — tufts and bare gaps
                float4(0,0,0.07,7.0), float4(0,0,0,0), float4(0,0.45,0,0), float4(0.12,0,0,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 13 Regolith — dust gardened by micro-impacts (bowls), strong opposition surge
                float4(0,0,0.015,2.0), float4(0.45,-0.08,0,0), float4(0.62,0.08,0,0), float4(0.02,0,0,1), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,0.4),
                // 14 Basalt flow — dark glassy ropes and lobes, clinker
                float4(0.15,1.6,0.20,0.12), float4(0,0,0,0), float4(0,0.20,0.02,7.0), float4(0.35,0,0,0), float4(0.08,0.075,0.07,0.35), float4(0,0,0,0), float4(0,0,0,1),
                // 15 Salt flat — brilliant crust, raised pressure-ridge polygons
                float4(0,0,0.004,3.0), float4(0.32,0,0.05,0.06), float4(0,0.05,0,0), float4(0.30,0,1.0,0), float4(0.93,0.91,0.87,0.25), float4(0,0,0,0), float4(0,0,0,0.3),
                // 16 Sulfur — Io-like yellow / orange / red-brown crust
                float4(0,0,0.04,0.6), float4(0,0,0,0), float4(0,0.30,0,0), float4(0.25,0,0.8,0), float4(0.90,0.80,0.22,0.7), float4(0.80,0.35,0.10,0.9), float4(0,0,0,1),
                // 17 Patterned ground — sorted-stone circles
                float4(0,0,0.02,1.5), float4(0.35,0,0.07,0.22), float4(0,0.30,0,0), float4(0.12,0,0,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 18 Scree — angular talus
                float4(0,0,0.10,0.4), float4(2.2,0.12,-0.02,0.10), float4(0,0.50,0,0), float4(0.18,0,0,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
                // 19 Frost — hoarfrost in the hollows of cold rock
                float4(0.25,0.45,0.01,0.9), float4(0,0,0,0), float4(0,0.20,0,0), float4(0.30,0,1.3,0), float4(0.90,0.93,0.97,0.2), float4(0,0,0,0), float4(0,0,0,1),
                // 20 Mossy — soft cushions
                float4(0,0,0.08,0.9), float4(0,0,0,0), float4(0,0.35,0,0), float4(0.06,0,0,0), float4(0,0,0,0), float4(0,0,0,0), float4(0,0,0,1),
            };

            struct TS
            {
                float h, cav, smooth, metal, sparkle, retro, tintAmt;
                float3 mul, tint, emit;
                float2 nxz;       // detail normal (x, z) from the baked normal map
            };

            // baked texture sets (TerrainTextures.cs): A = normal.xz, height, AO · B = tone/2, roughness, emission, metal
            TEXTURE2D_ARRAY(_TerrA); SAMPLER(sampler_TerrA);
            TEXTURE2D_ARRAY(_TerrB);
            float _TerrScale[32];
            float _TerrReady;
            static float2 gDpx, gDpy;   // d(p.xz)/dscreen, taken OUTSIDE the type loop (no gradients inside loops)
            static float gDist;          // camera distance of this fragment

            // Hex-tiling (Mikkelsen 2022): a triangle grid in uv space; each of its vertices owns a randomly rotated and
            // offset copy of the tile, and every point blends the three nearest copies — no regular repeat to lock onto.
            void TriGrid(float2 st, out float w1, out float w2, out float w3, out float2 v1, out float2 v2, out float2 v3)
            {
                st *= 3.4641016;                                            // 2·√3
                float2 skewed = float2(st.x, -0.57735027 * st.x + 1.15470054 * st.y);
                float2 base = floor(skewed);
                float3 tmp = float3(frac(skewed), 0); tmp.z = 1.0 - tmp.x - tmp.y;
                float sg = step(0.0, -tmp.z), s2 = 2.0 * sg - 1.0;
                w1 = -tmp.z * s2; w2 = sg - tmp.y * s2; w3 = sg - tmp.x * s2;
                v1 = base + float2(sg, sg); v2 = base + float2(sg, 1.0 - sg); v3 = base + float2(1.0 - sg, sg);
            }
            struct HexS { float4 ta, tb; float2 n; };
            HexS HexSample(float2 uv, float2 vtx, int t, float2 gx, float2 gy, float rotAmt)
            {
                float3 hc = float3(vtx, t * 7.13);
                float ang = (h13(hc) - 0.5) * 6.2831853 * rotAmt, c = cos(ang), sn = sin(ang);
                float2 off = float2(h13(hc + 3.7), h13(hc + 11.3)) * 17.0;
                float2 u  = float2(c * uv.x - sn * uv.y, sn * uv.x + c * uv.y) + off;
                float2 ux = float2(c * gx.x - sn * gx.y, sn * gx.x + c * gx.y);
                float2 uy = float2(c * gy.x - sn * gy.y, sn * gy.x + c * gy.y);
                HexS o;
                o.ta = SAMPLE_TEXTURE2D_ARRAY_GRAD(_TerrA, sampler_TerrA, u, t, ux, uy);
                o.tb = SAMPLE_TEXTURE2D_ARRAY_GRAD(_TerrB, sampler_TerrA, u, t, ux, uy);
                float2 nn = o.ta.rg * 2.0 - 1.0;
                o.n = float2(c * nn.x + sn * nn.y, -sn * nn.x + c * nn.y);   // rotate the normal back to world
                return o;
            }

            // Worley in the ground plane: nearest / second distance + a per-cell random
            void vor(float2 x, out float f1, out float f2, out float id)
            {
                float2 i = floor(x), f = frac(x); f1 = 8; f2 = 8; id = 0;
                [loop] for (int z = -1; z <= 1; z++) [loop] for (int xx = -1; xx <= 1; xx++)
                {
                    float2 o = float2(xx, z);
                    float3 hc = float3(i + o, 0);
                    float2 r = o + float2(h13(hc), h13(hc + 17.1)) - f;
                    float d = dot(r, r);
                    if (d < f1) { f2 = f1; f1 = d; id = h13(hc + 5.3); } else if (d < f2) f2 = d;
                }
                f1 = sqrt(f1); f2 = sqrt(f2);
            }
            static float gPix;   // metres per pixel here (set once per fragment, outside the type loop)
            float aaLine(float d, float w, float freq) { float a = gPix * freq * 1.5 + 1e-4; return 1.0 - smoothstep(w, w + a, d); }

            TS EvalType(int t, float3 p, float near, float mid, float time)
            {
                int o = clamp(t, 0, 20) * 7;
                float4 d = TT[o + 3], e = TT[o + 4], f = TT[o + 5], g = TT[o + 6];
                TS s;
                s.h = 0.5; s.cav = 0; s.mul = 1; s.emit = 0; s.nxz = 0;
                s.smooth = d.x; s.metal = d.y; s.sparkle = d.z; s.retro = d.w;
                float mix2 = f.w > 0 ? smoothstep(0.45, 0.7, fbm(p * 0.03 + 4.0, 2)) * f.w : 0;
                s.tint = lerp(e.rgb, f.rgb, mix2); s.tintAmt = e.w;
                if (_TerrReady < 0.5) return s;

                float S = max(_TerrScale[t], 0.1);
                float2 uv = p.xz / S;
                float2 gx = gDpx / S, gy = gDpy / S;
                float w1, w2, w3; float2 v1, v2, v3;
                // directional surfaces (ripples, sastrugi, flow ropes) must keep ONE orientation and phase, or neighbouring
                // hex patches disagree: they get a plain tile; everything else gets rotated + offset hex patches
                bool directional = (t == 3 || t == 4 || t == 9 || t == 11 || t == 14);
                // every type is hex-tiled; directional ones (ripples, sastrugi, flow) only wobble ±15° so the grain stays
                // coherent — where two patches meet, the ripples cross-fade like a natural defect instead of repeating
                float rotAmt = directional ? 0.085 : 1.0;
                TriGrid(uv * (directional ? 0.4 : 0.55), w1, w2, w3, v1, v2, v3);
                HexS h1 = HexSample(uv, v1, t, gx, gy, rotAmt), h2 = HexSample(uv, v2, t, gx, gy, rotAmt), h3 = HexSample(uv, v3, t, gx, gy, rotAmt);
                // sharpen the blend and let the taller texel win (keeps pebbles / plates crisp across seams)
                float3 W = pow(max(float3(w1, w2, w3), 1e-4), directional ? 3.0 : 6.0) * (0.25 + float3(h1.ta.b, h2.ta.b, h3.ta.b));
                W /= (W.x + W.y + W.z);
                float4 TA = h1.ta * W.x + h2.ta * W.y + h3.ta * W.z;
                float4 TB = h1.tb * W.x + h2.tb * W.y + h3.tb * W.z;
                s.nxz = h1.n * W.x + h2.n * W.y + h3.n * W.z;
                s.h = TA.b;
                s.cav = 1.0 - TA.a;
                float4 Bavg = SAMPLE_TEXTURE2D_ARRAY_LOD(_TerrB, sampler_TerrA, float2(0.5, 0.5), t, 9.0);
                float farT = saturate((gDist / S - 12.0) / 50.0);
                s.mul = lerp(TB.r, Bavg.r, farT * 0.85) * 2.0;
                s.smooth = saturate(1.0 - TB.g) * (0.35 + d.x);                        // map roughness, scaled per type
                s.metal = d.y * TB.a * (1.0 - mix2);
                float pulse = 0.75 + 0.25 * sin(time * 1.3 + vn(p * 0.4) * 12.0);
                s.emit = g.rgb * TB.b * pulse;
                s.tintAmt *= 1.0 - TB.b * step(0.001, g.x);                         // glowing seams aren't crust
                return s;
            }

            V vert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.flatWS = ws;
                float2 d = ws.xz - _WorldSpaceCameraPos.xz; ws.y -= dot(d, d) * _CurvK * (1.0 - _NoCurve);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.mat = v.mat; o.mat2 = v.mat2; o.wtr = v.wtr;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                float near = saturate(1.0 - dist / 220.0);            // micro detail
                float mid = saturate(1.0 - dist / 2500.0);            // meso detail
                float3 p = i.flatWS + float3(_SurfOrigin.x, 0, _SurfOrigin.y);

                // palette weights (8); an all-zero set (the far globe) → the Rocky slot
                float w8[8] = { i.mat.x, i.mat.y, i.mat.z, i.mat.w, i.mat2.x, i.mat2.y, i.mat2.z, i.mat2.w };
                float ids[8] = { _TypeIdA.x, _TypeIdA.y, _TypeIdA.z, _TypeIdA.w, _TypeIdB.x, _TypeIdB.y, _TypeIdB.z, _TypeIdB.w };
                float wsum = 0; for (int k = 0; k < 8; k++) wsum += w8[k];
                bool valid = wsum > 0.01;
                if (!valid) { w8[0] = 1; wsum = 1; }
                float wet = valid ? saturate(i.color.a) : 0;
                wet = max(wet, _RainWet * saturate(N.y * 1.5 - 0.3) * (valid ? 1.0 : 0.0));   // rain soaks what faces the sky

                // vertex colours are sRGB (they come from the orbital map / palette); the project renders in LINEAR
                // space, so decode them — without this every colour came out pale and washed toward pink/beige
                float3 alb = pow(max(i.color.rgb, 0.0), 2.2);
                alb *= 0.84 + fbm(p * 0.012, 3) * 0.32;              // macro variation at every distance
                alb *= 0.88 + fbm(p * 0.07 + 13.0, 2) * 0.24;       // metre-to-decametre patchiness (hides tile repeats)
                alb = max(alb, 0.012);                               // even carbon soot reflects ~1%: never a void

                gDpx = ddx(p.xz); gDpy = ddy(p.xz); gDist = dist;
                gPix = max(length(abs(ddx_fine(p.xz)) + abs(ddy_fine(p.xz))), 1e-4);
                // evaluate the types present here and blend them by weight × height (the taller surface wins)
                float h = 0, cav = 0, smooth = 0, metal = 0, sparkle = 0, retro = 0, tw = 0, tintAmt = 0;
                float3 mul = 0, tint = 0, emit = 0; float2 nxz = 0;
                [loop] for (int k = 0; k < 8; k++)
                {
                    if (w8[k] < 0.02 || ids[k] < -0.5) continue;
                    TS t = EvalType((int)(ids[k] + 0.5), p, near, mid, _Time.y);
                    float bw = w8[k] * (0.15 + t.h * t.h * 4.0);                       // height blend: the taller surface wins
                    nxz += t.nxz * bw;
                    h += t.h * bw; cav += t.cav * bw; smooth += t.smooth * bw; metal += t.metal * bw;
                    sparkle += t.sparkle * bw; retro += t.retro * bw; mul += t.mul * bw;
                    tint += t.tint * t.tintAmt * bw; tintAmt += t.tintAmt * bw; emit += t.emit * bw;
                    tw += bw;
                }
                tw = max(tw, 1e-4);
                h /= tw; cav /= tw; nxz /= tw; smooth /= tw; metal /= tw; sparkle /= tw; retro /= tw; mul /= tw; emit /= tw;
                float3 tintC = tintAmt > 1e-4 ? tint / tintAmt : 0; tintAmt /= tw;
                tintC = pow(max(tintC, 0.0), 2.2);                    // table tints are sRGB too
                // material colour: the type's own colour (ice, salt, sulfur, crust…) partly over the orbital colour
                alb = lerp(alb, tintC, saturate(tintAmt) * 0.6) * lerp(float3(1, 1, 1), mul, mid * 0.85 + 0.15);
                alb *= 1.0 - wet * 0.3;                               // damp ground is darker
                float3 rockBump = 0;
                [branch] if (_IsRock > 0.5)
                {
                    float3 rp = p * 2.3;
                    // rough, pitted surface: gradient of a 3D noise (chips, pits, lichen-scale grit) — no smooth plastic
                    float3 rbP = p * 6.0; const float rbE = 0.07;
                    float rb0 = fbm(rbP, 3);
                    float3 rbG = float3(fbm(rbP + float3(rbE, 0, 0), 3), fbm(rbP + float3(0, rbE, 0), 3), fbm(rbP + float3(0, 0, rbE), 3)) - rb0;
                    float gr0 = vn(p * 40.0);
                    float3 g2 = float3(vn(p * 40.0 + float3(0.15, 0, 0)), vn(p * 40.0 + float3(0, 0.15, 0)), vn(p * 40.0 + float3(0, 0, 0.15))) - gr0;
                    rockBump = (rbG / rbE) * 0.35 + (g2 / 0.15) * 0.08;
                    float mott = fbm(rp, 3), grain = vn(rp * 9.0);
                    alb = pow(max(i.color.rgb, 0.0), 2.2) * (0.78 + mott * 0.4) * (0.92 + grain * 0.16);
                    alb = max(alb, 0.02);
                    smooth = 0.12; metal = 0; sparkle = 0; retro = 0; emit = 0; tintAmt = 0; mul = 1;
                    cav = (1.0 - mott) * 0.25; nxz = 0; h = 0.5;
                }
                float rockGlint = 0;
                [branch] if (_RockGlint > 0.5)
                {
                    // meteorites: iron-nickel (polished grey metal, rust in the hollows) or black impact glass (tektite)
                    bool iron = _RockGlint < 1.5;
                    float rust = smoothstep(0.45, 0.75, fbm(p * 3.0 + 7.0, 3)) * (iron ? 1.0 : 0.0);
                    float3 metalC = pow(float3(0.58, 0.56, 0.53), 2.2);
                    float3 rustC = pow(float3(0.42, 0.22, 0.11), 2.2);
                    alb = iron ? lerp(metalC, rustC, rust) : pow(float3(0.05, 0.05, 0.055), 2.2);
                    metal = iron ? 0.95 * (1.0 - rust) : 0.0;
                    smooth = iron ? lerp(0.78, 0.3, rust) : 0.92;
                    smooth *= lerp(1.0, 0.45, saturate(dist / 150.0));   // sub-pixel mirrors far away just twinkle — soften them
                    sparkle = 2.5; tintAmt = 0; mul = 1; emit = 0;
                    rockGlint = 1;
                }
                smooth = lerp(smooth, 0.85, wet * 0.8);
                // settled snow: collects on surfaces facing up, thins on steep ground and where the relief pokes through
                float snowAmt = saturate(_SnowCover * 1.4 * saturate(N.y * 2.2 - 1.2) - (1.0 - h) * 0.3 * (1.0 - _SnowCover));
                alb = lerp(alb, pow(float3(0.93, 0.95, 0.98), 2.2), snowAmt);
                smooth = lerp(smooth, 0.45, snowAmt); sparkle = max(sparkle, snowAmt * 1.2);

                // detail normal from the baked maps (mip-mapped → no shimmer, no pixel-quad blockiness); fades to the
                // geometric normal far away, where the tile would only alias
                float dn = saturate(1.0 - dist / 900.0);
                float3 Nd = normalize(float3(N.x + nxz.x * dn * 1.4, N.y, N.z + nxz.y * dn * 1.4));
                if (_IsRock > 0.5) { float3 bt = rockBump - N * dot(rockBump, N); Nd = normalize(N - bt * dn); }
                if (all(isfinite(Nd))) N = Nd;

                // RIVERS & LAKES: the vertex marks liquid surface (+ flow direction). Glassy, the liquid's colour,
                // ripples carried downstream; the bank blends into the wet bed.
                float wmask = _IsRock > 0.5 ? 0.0 : saturate(i.wtr.x);
                if (wmask > 0.01)
                {
                    float2 fl = i.wtr.yz; float tt = _Time.y;
                    float2 q = p.xz - fl * tt * 1.4;
                    float e = 0.2;
                    float r0 = vn(float3(q.x * 0.9, tt * 0.25, q.y * 0.9)) + vn(float3(q.x * 2.6, tt * 0.5, q.y * 2.6)) * 0.4;
                    float rx = vn(float3((q.x + e) * 0.9, tt * 0.25, q.y * 0.9)) + vn(float3((q.x + e) * 2.6, tt * 0.5, q.y * 2.6)) * 0.4;
                    float rz = vn(float3(q.x * 0.9, tt * 0.25, (q.y + e) * 0.9)) + vn(float3(q.x * 2.6, tt * 0.5, (q.y + e) * 2.6)) * 0.4;
                    float rip = 0.18 * (0.3 + length(fl)) * saturate(1.0 - dist / 600.0);
                    float3 Nw = normalize(float3(-(rx - r0) / e * rip, 1.0, -(rz - r0) / e * rip));
                    N = normalize(lerp(N, Nw, wmask));
                    alb = lerp(alb, _RiverColor.rgb, wmask);
                    smooth = lerp(smooth, 0.94, wmask); metal *= 1.0 - wmask; cav *= 1.0 - wmask; emit *= 1.0 - wmask;
                    sparkle *= 1.0 - wmask; retro *= 1.0 - wmask;
                }

                // Loose rocks: sample the shadow half a metre toward the sun. A pebble can't shadow itself any more (the
                // sample point is outside it), but hill / tree shadows still land on it, and a big boulder's far side
                // stays dark (the point is still inside the boulder).
                float3 shadowPos = i.positionWS;
                if (_IsRock > 0.5) shadowPos += _MainLightPosition.xyz * 0.5 + N * 0.05;
                Light L = GetMainLight(TransformWorldToShadowCoord(shadowPos));
                float3 Vd = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 H = normalize(L.direction + Vd);
                float ndl = saturate(dot(N, L.direction));
                float ndh = saturate(dot(N, H));
                float ao = 1.0 - saturate(cav) * 0.6;
                // Small loose rocks are only a few shadow-map texels across: they self-shadow into black blobs. They
                // still receive most of the ground's shadow (a rock under a tree is darker), but never their own.
                float shadowA = L.shadowAttenuation;
                float3 lit = L.color * shadowA * L.distanceAttenuation * CloudShadow(p, L.direction);

                // energy-normalised Blinn-Phong + Schlick fresnel; metals reflect in their own colour
                float sp = exp2(smooth * 10.0 + 1.0);
                float fres = 0.04 + 0.96 * pow(1.0 - saturate(dot(N, Vd)), 5.0);
                float spec = pow(ndh, sp) * (sp + 8.0) / 25.0 * lerp(0.04, fres, smooth) * smooth;
                float3 specC = lerp(float3(1, 1, 1), alb * 2.2, metal);
                float3 diffC = alb * (1.0 - metal * 0.85);
                // opposition surge: rough dusty regolith brightens sharply when the sun is behind you (Hapke)
                float surge = 1.0 + retro * 0.7 * pow(saturate(dot(Vd, L.direction)), 8.0);
                // glints: sparse hashed facets catching the sun (sand grains, snow crystals)
                if (sparkle > 0.01 && (near > 0.01 || rockGlint > 0.5))
                {
                    float3 cp = floor(p * 160.0);   // grain-sized glints (coarse cells read as square speckles)
                    float3 fn = normalize(float3(h13(cp + 3.1) - 0.5, 1.0, h13(cp + 7.7) - 0.5));
                    spec += pow(saturate(dot(fn, H)), 400.0) * step(0.93, h13(cp)) * sparkle * max(near * near, rockGlint * 0.6 * saturate(1.0 - dist / 120.0)) * 5.0;
                }
                float3 col = diffC * (SurfSH(N) * ao + lit * ndl * surge) + lit * spec * ndl * specC;
                col += alb * SurfSH(reflect(-Vd, N)) * metal * smooth * 0.8;   // metals mirror the sky
                col += SurfSH(reflect(-Vd, N)) * (fres - 0.04) * (0.25 + smooth * 0.75) * (1.0 - metal) * ao;   // dielectric sky sheen at grazing angles
                col += emit;                                                     // molten rock glows
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                // other suns of a multiple-star system (and any other lights)
                uint lc = GetAdditionalLightsCount();
                for (uint li = 0u; li < lc; li++)
                {
                    Light al = GetAdditionalLight(li, i.positionWS);
                    float3 H2 = normalize(al.direction + Vd);
                    float nd2 = saturate(dot(N, al.direction));
                    float3 c2 = al.color * al.distanceAttenuation * al.shadowAttenuation;
                    col += diffC * c2 * nd2 + c2 * specC * pow(saturate(dot(N, H2)), sp) * (sp + 8.0) / 25.0 * lerp(0.04, fres, smooth) * smooth * nd2;
                }
                #endif
                col = MixFog(col, i.fog);
                col = (any(isnan(col)) || any(isinf(col))) ? float3(0, 0, 0) : max(col, 0);   // belt and braces
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex sv
            #pragma fragment sf
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Grain, _GrainScale, _NoCurve, _RockGlint, _IsRock;
            CBUFFER_END
            float3 _LightDirection; float _CurvK;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V sv(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float2 d = ws.xz - _WorldSpaceCameraPos.xz; ws.y -= dot(d, d) * _CurvK * (1.0 - _NoCurve);
                float4 c = TransformWorldToHClip(ApplyShadowBias(ws, TransformObjectToWorldNormal(v.normalOS), _LightDirection));
            #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
            #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = c; return o;
            }
            half4 sf(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex dv
            #pragma fragment df
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Grain, _GrainScale, _NoCurve, _RockGlint, _IsRock;
            CBUFFER_END
            float _CurvK;
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V dv(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float2 d = ws.xz - _WorldSpaceCameraPos.xz; ws.y -= dot(d, d) * _CurvK * (1.0 - _NoCurve);
                o.positionCS = TransformWorldToHClip(ws); return o;
            }
            half4 df(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
