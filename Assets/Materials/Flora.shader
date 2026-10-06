Shader "CLAY/Flora"
{
    // A real URP LIT shader for procedural plants: main directional light with soft SHADOWS (cast + receive),
    // additional lights, spherical-harmonic ambient, and a specular sheen. Per-vertex ALPHA blends
    // _BaseColor→_AccentColor (paint job); two-sided surfaces get an _Underside colour and flipped normals on back
    // faces. Used in the Flora Lab, which switches the camera to the project's 3D (Universal) renderer.
    Properties
    {
        _BaseColor("Base Color", Color) = (0.3, 0.5, 0.3, 1)
        _AccentColor("Accent Color", Color) = (0.6, 0.7, 0.3, 1)
        _Underside("Underside Color", Color) = (0.2, 0.3, 0.2, 1)
        _Translucency("Leaf Translucency", Range(0,1)) = 0
        _LeafDetail("Leaf Detail On", Float) = 0
        _LeafStyle("Leaf Style", Float) = 0
        _LeafBump("Leaf/Organ Bump", Range(0,0.2)) = 0.03
        _BarkWarp("Bark Warp", Range(0,1)) = 0.3
        _BarkNoise("Bark Fine Noise", Range(0,1)) = 0.15
        _Smoothness("Smoothness", Range(0,1)) = 0.35
        _SpecStrength("Specular", Range(0,2)) = 0.4
        _Glow("Bioluminescence", Range(0,2)) = 0
        _PlantHeight("Plant Height", Float) = 100
        _Understorey("Understorey Darkening", Range(0,1)) = 0
        _StemMat("Stem Material (0 spongy … 1 woody)", Range(0,1)) = 1
        _BarkType("Bark Type", Float) = 0
        _BarkScale("Bark Grain Scale", Range(0.3,3)) = 1
        _StemDetail("Stem Detail Strength", Range(0,1)) = 0
        _BumpStrength("Bump Strength", Range(0,0.3)) = 0.06
        _Cull("Cull", Float) = 2
        _TwoSided("Two Sided", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Include/SurfAmbient.hlsl"

            // Planet-surface horizon curvature: y drops by d²/2R with distance from the camera (0 = off, e.g. the lab).
            float _CurvK;
            float4 _SurfOrigin;
            float _BioGlow;
            #include "Include/Clouds.hlsl"
            float3 CurveWS(float3 ws) { float2 d = ws.xz - _WorldSpaceCameraPos.xz; ws.y -= dot(d, d) * _CurvK; return ws; }

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 uv : TEXCOORD0; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float hf : TEXCOORD2; float4 uv : TEXCOORD3; float4 color : COLOR; float fog : TEXCOORD4; };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _AccentColor, _Underside;
                float _Smoothness, _SpecStrength, _Glow, _TwoSided, _Cull, _PlantHeight, _Understorey, _StemMat, _BarkType, _BarkScale, _StemDetail, _BumpStrength, _Translucency, _LeafDetail, _LeafStyle, _BarkWarp, _BarkNoise, _LeafBump;
            CBUFFER_END

            // cheap 3D value noise for procedural stem surface (no UVs needed)
            float fhash13(float3 p) { p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
            float fnoise(float3 x)
            {
                float3 p = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                float n000 = fhash13(p), n100 = fhash13(p + float3(1,0,0)), n010 = fhash13(p + float3(0,1,0)), n110 = fhash13(p + float3(1,1,0));
                float n001 = fhash13(p + float3(0,0,1)), n101 = fhash13(p + float3(1,0,1)), n011 = fhash13(p + float3(0,1,1)), n111 = fhash13(p + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x), lerp(n010,n110,f.x), f.y), lerp(lerp(n001,n101,f.x), lerp(n011,n111,f.x), f.y), f.z);
            }

            float fbm2(float2 p) { float s = 0.0, a = 0.5; for (int i = 0; i < 4; i++) { s += a * fnoise(float3(p, 0.0)); p = p * 2.03 + 5.1; a *= 0.5; } return s; }
            float2 hash22(float2 p) { p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3))); return frac(sin(p) * 43758.5453); }
            // Worley F1 distance + the winning cell id (for per-plate colour).
            float worleyF1(float2 p, out float2 cell)
            {
                float2 n = floor(p), f = frac(p); float md = 9.0; cell = n;
                for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    float2 g = float2(i, j); float2 o = hash22(n + g); float2 d = g + o - f; float dd = dot(d, d);
                    if (dd < md) { md = dd; cell = n + g; }
                }
                return sqrt(md);
            }

            // Stem surface: x = height (0..1, for bump), yzw = albedo multiplier. Non-woody tissue by _StemMat;
            // woody bark by a DISCRETE archetype so bark reads as a real type (furrowed / plated / papery / …).
            float4 StemSurface(float2 uv, float mat, int bark, float2 scale)
            {
                // scale.x scales AROUND grain (folds in local circumference → world-consistent, no scrunch on
                // thin/tapered faces); scale.y scales ALONG grain (V is already world-length).
                float u = uv.x * scale.x, v = uv.y * scale.y;
                // Bark noise sliders: _BarkWarp domain-warps the whole pattern (organic irregularity, breaks up
                // repetition); _BarkNoise adds high-frequency roughness to the final height (grit / fine cracks).
                if (_BarkWarp > 0.001)
                {
                    // medium, along-grain meander: the grain wanders, but doesn't curl into swirls everywhere
                    u += (fbm2(float2(u * 1.6, v * 0.55)) - 0.5) * _BarkWarp * 1.6;
                    v += (fbm2(float2(u * 0.8, v * 1.3)) - 0.5) * _BarkWarp * 0.6;
                    // KNOTS: the whorl lives in a few sparse spots (old branch scars) where the grain flows around a point
                    float2 kc = float2(u * 0.9, v * 0.35);
                    float2 ki = floor(kc), kf = frac(kc) - 0.5;
                    float kh = frac(sin(dot(ki, float2(127.1, 311.7))) * 43758.5453);
                    float2 ko = (float2(frac(kh * 7.13), frac(kh * 3.71)) - 0.5) * 0.5;
                    float2 kd = kf - ko; kd.y *= 1.6;
                    float kr = length(kd);
                    float knot = (kh < 0.35 + _BarkWarp * 0.4) ? exp(-kr * kr / 0.018) : 0.0;
                    float kang = knot * 2.6;
                    float2 rot = float2(kd.x * cos(kang) - kd.y * sin(kang), kd.x * sin(kang) + kd.y * cos(kang));
                    u += (rot.x - kd.x) / 0.9;
                    v += (rot.y - kd.y) / (0.35 * 1.6);
                }
                if (mat < 0.5)   // spongy → herbaceous → fibrous
                {
                    float pore = smoothstep(0.34, 0.72, fbm2(float2(u * 10.0, v * 10.0)));
                    float rib = pow(0.5 + 0.5 * cos(u * 6.2831 * 6.0 + (fbm2(float2(u * 2, v * 1)) - 0.5) * 0.8), 1.4);
                    float fiber = pow(saturate(0.5 + 0.5 * sin(u * 6.2831 * 9.0 + fbm2(float2(u * 6, v * 1.2)) * 0.9)), 2.5);
                    float h = mat < 0.25 ? lerp(pore, rib, mat / 0.25) : lerp(rib, fiber, (mat - 0.25) / 0.25);
                    return float4(h, lerp(float3(0.82, 0.88, 0.78), float3(1.06, 1.0, 0.9), h));
                }

                float h = 0.5; float3 tint = float3(1, 1, 1);
                if (bark == 0)          // FURROWED (oak / mature pine): deep wavy vertical furrows
                {
                    float2 p = float2(u * 7.0, v * 0.8);
                    p.x += (fbm2(float2(u * 2.0, v * 0.35)) - 0.5) * 1.0;                 // warp → irregular, not stripey (big warp = swirls)
                    float ridge = abs(frac(p.x + fbm2(float2(p.x, p.y * 0.5))) - 0.5) * 2.0;
                    ridge = smoothstep(0.08, 0.7, ridge);
                    h = ridge * (0.82 + fbm2(float2(u * 20, v * 3.0)) * 0.18);
                    tint = lerp(float3(0.33, 0.24, 0.18), float3(1.05, 0.98, 0.85), h);
                }
                else if (bark == 1)     // PLATED (ponderosa): irregular polygonal plates, per-plate colour
                {
                    float2 cell; float w = worleyF1(float2(u * 6.0, v * 3.0) + (fbm2(float2(u * 5, v * 3)) - 0.5) * 1.2, cell);
                    float gap = smoothstep(0.0, 0.2, w);
                    h = gap * (0.7 + fbm2(float2(u * 22, v * 12)) * 0.3);
                    float2 cr = hash22(cell);
                    float3 plate = lerp(float3(0.72, 0.42, 0.24), float3(0.55, 0.46, 0.4), cr.x);   // reddish ↔ grey plates
                    tint = lerp(float3(0.18, 0.12, 0.09), plate * (0.85 + cr.y * 0.35), gap);
                }
                else if (bark == 2)     // PAPERY (birch): pale bark, dark horizontal lenticels, peeling patches
                {
                    float basev = 0.6 + fbm2(float2(u * 3, v * 1.5)) * 0.2;
                    float lent = smoothstep(0.78, 0.96, fbm2(float2(u * 3.0, v * 24.0)));           // horizontal dashes
                    float peel = smoothstep(0.82, 0.96, fbm2(float2(u * 5, v * 5)));
                    h = basev - lent * 0.35 + peel * 0.12;
                    tint = lerp(float3(0.92, 0.92, 0.9), float3(0.14, 0.11, 0.1), lent);
                    tint = lerp(tint, float3(0.62, 0.42, 0.32), peel * 0.55);
                }
                else if (bark == 3)     // STRINGY (cedar / redwood): irregular vertical fibrous strips
                {
                    float2 p = float2(u * 9.0, v * 0.7);
                    p.x += (fbm2(float2(u * 6, v * 0.8)) - 0.5) * 1.6;
                    float strip = pow(saturate(0.5 + 0.5 * sin(p.x * 6.2831 + fbm2(p) * 2.5)), 2.0);
                    h = strip * (0.8 + fbm2(float2(u * 10, v * 6)) * 0.2);
                    tint = lerp(float3(0.4, 0.27, 0.19), float3(0.82, 0.62, 0.42), h);
                }
                else if (bark == 4)     // SMOOTH (beech / young): subtle mottling and faint blotches
                {
                    float m = fbm2(float2(u * 4, v * 3));
                    float blotch = smoothstep(0.58, 0.82, fbm2(float2(u * 2, v * 2)));
                    h = 0.5 + (m - 0.5) * 0.35;
                    tint = lerp(float3(0.64, 0.62, 0.52), float3(0.5, 0.53, 0.47), blotch) * (0.85 + m * 0.3);
                }
                else if (bark == 5)     // SCALY (pine / palm boot): overlapping rounded scales in brick rows
                {
                    float2 sp = float2(u * 11.0, v * 6.0);
                    sp.x += floor(sp.y) * 0.5;                       // offset alternate rows → brickwork
                    sp += (fbm2(sp * 0.7) - 0.5) * 0.6;             // break the grid
                    float2 cell; float w = worleyF1(sp, cell);
                    float dome = 1.0 - smoothstep(0.0, 0.55, w);     // each scale a rounded dome
                    h = dome;
                    float2 cr = hash22(cell);
                    tint = lerp(float3(0.19, 0.14, 0.1), float3(0.52, 0.42, 0.3) * (0.8 + cr.x * 0.4), dome);
                }
                else                    // ROPEY (fig / some palms): interwoven twisted vertical strands
                {
                    float warp = (fbm2(float2(u * 5, v * 1.5)) - 0.5) * 2.0;
                    float a1 = sin((u * 6.2831 * 9.0) + v * 3.2 + warp);
                    float a2 = sin((u * 6.2831 * 9.0) - v * 3.2 + warp);
                    float rope = pow(saturate(0.5 + 0.5 * max(a1, a2)), 2.0);
                    h = rope * (0.8 + fbm2(float2(u * 14, v * 8)) * 0.2);
                    tint = lerp(float3(0.3, 0.22, 0.15), float3(0.62, 0.5, 0.36), h);
                }
                if (_BarkNoise > 0.001)
                    h = saturate(h + (fbm2(float2(u * 14.0, v * 10.0)) - 0.5) * _BarkNoise);
                return float4(saturate(h), tint);
            }

            // Height field for leaf/organ surfaces (drives the bump map). Mirrors each style's pattern so the relief
            // matches what you see: veins sink, cells dome, eye-spot rings rise, stripes ridge, mottles swell — plus a
            // universal fine organic grain so no surface is ever perfectly smooth.
            float LeafH(float2 uv, int ls)
            {
                float2 w = float2(fbm2(uv * 5.0) - 0.5, fbm2(uv * 5.0 + float2(9.0, -3.0)) - 0.5);
                float ac = uv.x + w.x * 0.10, alo = uv.y + w.y * 0.10;
                float2 luv = float2(ac, alo);
                float grain = fbm2(uv * 38.0) * 0.25 + fbm2(uv * 90.0) * 0.1;
                float h = 0.0;
                if (ls == 2 || ls == 4)
                {
                    float ribw = abs(ac - 0.5);
                    float midrib = 1.0 - smoothstep(0.0, 0.035, ribw);
                    float lat = (1.0 - smoothstep(0.0, 0.05, abs(frac(alo * 8.0 - ribw * 5.0) - 0.5))) * smoothstep(0.5, 0.12, ribw);
                    h = -saturate(midrib + lat * 0.8);                       // veins are channels
                }
                else if (ls == 3) h = fbm2(float2(ac * 3.5, alo * 5.0));
                else if (ls == 5) { float2 c; float wv = worleyF1(luv * float2(7.0, 11.0) + w * 1.5, c); h = 1.0 - smoothstep(0.0, 0.5, wv); }   // domed cells
                else if (ls == 6) h = fbm2(luv * 2.5 + w * 3.0) * 0.8;
                else if (ls == 7) { float2 c; float wv = worleyF1(luv * float2(5.0, 8.0) + w, c); float rr = lerp(0.18, 0.3, hash22(c).x);
                                    h = smoothstep(rr - 0.06, rr - 0.03, wv) - smoothstep(rr, rr + 0.03, wv); }   // raised rings
                else if (ls == 8) h = smoothstep(0.55, 0.85, sin((ac * 26.0 + (fbm2(luv * 3.0) - 0.5) * 6.0) * 3.14159));
                else h = fbm2(luv * 6.0) * 0.4;
                return h + grain;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = CurveWS(TransformObjectToWorld(v.positionOS.xyz));
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.hf = saturate(v.positionOS.y / max(_PlantHeight, 0.001));   // height fraction (base→canopy)
                o.uv = v.uv;   // xy = around/along, z = local circumference
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                // Distance LOD for the procedural surface work: it's invisible beyond a few tens of metres but is the
                // bulk of the per-pixel cost (thousands of overlapping leaves would otherwise stall the GPU).
                float camDist = distance(i.positionWS, _WorldSpaceCameraPos);
                bool front = IS_FRONT_VFACE(facing, true, false);
                if (_TwoSided > 0.5 && !front) N = -N;

                float3 baseC = lerp(_BaseColor.rgb, _AccentColor.rgb, saturate(i.color.a));
                if (_TwoSided > 0.5 && !front) baseC = _Underside.rgb;

                float localSmooth = _Smoothness;
                float localSpec = _SpecStrength;

                // ── Procedural stem surface: discrete bark archetype → normal BUMP + ALBEDO + per-pixel roughness ──
                if (_StemDetail > 0.001 && camDist < 60.0)
                {
                    int bt = (int)(_BarkType + 0.5);
                    // Grain scale: AROUND folds in the ring circumference (uv.z) so grain is world-consistent on any
                    // thickness; ALONG uses the slider alone (V is already world-length).
                    float2 gs = float2(_BarkScale * max(i.uv.z, 0.05), _BarkScale);
                    float4 s = StemSurface(i.uv.xy, _StemMat, bt, gs);
                    float e = 0.006;
                    float hu = StemSurface(i.uv.xy + float2(e, 0), _StemMat, bt, gs).x;
                    float hv = StemSurface(i.uv.xy + float2(0, e), _StemMat, bt, gs).x;
                    // Mikkelsen surface-gradient bump, DIVIDED BY det → world-consistent (this was the missing bit).
                    float3 dpx = ddx(i.positionWS), dpy = ddy(i.positionWS);
                    float3 r1 = cross(dpy, N), r2 = cross(N, dpx);
                    float det = dot(dpx, r1);
                    float dHx = (hu - s.x) / e * ddx(i.uv).x + (hv - s.x) / e * ddx(i.uv).y;
                    float dHy = (hu - s.x) / e * ddy(i.uv).x + (hv - s.x) / e * ddy(i.uv).y;
                    if (abs(det) > 1e-8)
                    {
                        float3 grad = (dHx * r1 + dHy * r2) / det;
                        float gl = length(grad); if (gl > 200.0) grad *= 200.0 / gl;
                        float3 Nb = N - _BumpStrength * grad;
                        if (dot(Nb, Nb) > 1e-6) N = normalize(Nb);
                    }
                    baseC *= s.yzw;                                               // per-type albedo (colour variation)
                    localSmooth = saturate(_Smoothness * lerp(0.55, 1.15, s.x));
                    localSpec = _SpecStrength * lerp(0.7, 1.2, s.x);
                }

                // Per-organ vertex tint (per-leaf coloration) — white on wood, so harmless there.
                baseC *= i.color.rgb;

                // ── Procedural leaf surface: organic warped venation + per-style finish (uv.xy = across, along) ──
                if (_LeafDetail > 0.5 && camDist < 90.0)
                {
                    int ls = (int)(_LeafStyle + 0.5);
                    // ── bump: surface-gradient (Mikkelsen) from the style's height field, world-consistent ──
                    if (_LeafBump > 0.0001 && camDist < 25.0)
                    {
                        float e = 0.004;
                        float h0 = LeafH(i.uv.xy, ls);
                        float hu = LeafH(i.uv.xy + float2(e, 0), ls);
                        float hv = LeafH(i.uv.xy + float2(0, e), ls);
                        float3 dpx = ddx(i.positionWS), dpy = ddy(i.positionWS);
                        float3 r1 = cross(dpy, N), r2 = cross(N, dpx);
                        float det = dot(dpx, r1);
                        float2 duvx = ddx(i.uv.xy), duvy = ddy(i.uv.xy);
                        float dHx = (hu - h0) / e * duvx.x + (hv - h0) / e * duvx.y;
                        float dHy = (hu - h0) / e * duvy.x + (hv - h0) / e * duvy.y;
                        if (abs(det) > 1e-8)
                        {
                            float3 grad = (dHx * r1 + dHy * r2) / det;
                            float gl = length(grad); if (gl > 200.0) grad *= 200.0 / gl;
                            float3 Nb = N - _LeafBump * grad;
                            if (dot(Nb, Nb) > 1e-6) N = normalize(Nb);
                        }
                    }
                    float across = i.uv.x, along = i.uv.y;
                    // Domain-warp the vein coordinates so nothing reads as mechanical stripes.
                    float2 w = float2(fbm2(float2(across * 5.0, along * 5.0)) - 0.5,
                                      fbm2(float2(across * 5.0 + 9.0, along * 5.0 - 3.0)) - 0.5);
                    float ac = across + w.x * 0.10;
                    float alo = along + w.y * 0.10;
                    float ribw = abs(ac - 0.5);
                    float midrib = 1.0 - smoothstep(0.0, 0.03 * (1.0 - alo * 0.5) + 0.008, ribw);   // tapers to the tip
                    float vph = alo * 8.0 - ribw * 5.0;                                             // laterals slant toward tip
                    float lat = 1.0 - smoothstep(0.0, 0.05, abs(frac(vph) - 0.5));
                    lat *= smoothstep(0.5, 0.12, ribw) * (1.0 - midrib);                            // fade at margin + midrib
                    float fine = smoothstep(0.74, 0.95, fbm2(float2(ac * 22.0, alo * 26.0)));       // fine reticulation
                    float veins = saturate(midrib * 1.1 + lat * 0.75 + fine * 0.18);
                    float mottle = fbm2(float2(ac * 3.5 + (fbm2(float2(alo * 4.0, ac * 4.0)) - 0.5) * 2.0, alo * 5.0));

                    float3 cA = _BaseColor.rgb * i.color.rgb, cB = _AccentColor.rgb * i.color.rgb;
                    float2 luv = float2(ac, alo);
                    if (ls == 5)        // CELLULAR: organic Voronoi cells, each its own shade, with dark sunken walls
                    {
                        float2 cell; float wv = worleyF1(luv * float2(7.0, 11.0) + w * 1.5, cell);
                        float2 h = hash22(cell);
                        float3 cellC = lerp(cA, cB, h.x * 0.8) * lerp(0.8, 1.15, h.y);
                        float wall = smoothstep(0.28, 0.5, wv);
                        baseC = lerp(cellC, cellC * 0.35, wall);
                        localSmooth = lerp(0.6, 0.15, wall); localSpec = 0.35;
                    }
                    else if (ls == 6)   // NEBULOUS: domain-warped swirls of base/accent with luminous wisps + a hue-shifted third tone
                    {
                        float n1 = fbm2(luv * 2.5 + w * 3.0);
                        float n2 = fbm2(luv * 5.0 + n1 * 3.5 + 7.3);
                        float3 c = lerp(cA, cB, smoothstep(0.3, 0.7, n1));
                        c = lerp(c, c.gbr * 1.1, smoothstep(0.58, 0.82, n2) * 0.7);   // third tone
                        c += cB * smoothstep(0.66, 0.9, n2) * 0.45;                    // wisps
                        baseC = c; localSmooth = 0.45; localSpec = 0.3;
                    }
                    else if (ls == 7)   // OCELLATE: eye-spots — rings of accent around dark pupils, scattered organically
                    {
                        float2 cell; float wv = worleyF1(luv * float2(5.0, 8.0) + w, cell);
                        float2 h = hash22(cell);
                        float rr = lerp(0.18, 0.3, h.x);
                        float ring = smoothstep(rr - 0.06, rr - 0.03, wv) - smoothstep(rr, rr + 0.03, wv);
                        float pupil = 1.0 - smoothstep(rr * 0.35, rr * 0.45, wv);
                        float3 c = cA;
                        c = lerp(c, cB * 1.2, ring * step(0.35, h.y));
                        c = lerp(c, cA * 0.15, pupil * step(0.35, h.y));
                        baseC = c; localSmooth = 0.35; localSpec = 0.2;
                    }
                    else if (ls == 8)   // STRIATE: fine warped pinstripes + a broad banding, like a zebra-plant or tiger-lily
                    {
                        float s1 = sin((ac * 26.0 + (fbm2(luv * 3.0) - 0.5) * 6.0) * 3.14159);
                        float s2 = sin((alo * 5.0 + (fbm2(luv * 1.5 + 3.0) - 0.5) * 2.0) * 3.14159);
                        float stripe = smoothstep(0.55, 0.85, s1) * (0.6 + 0.4 * s2);
                        baseC = lerp(cA, cB, stripe); localSmooth = 0.3; localSpec = 0.15;
                    }
                    else if (ls == 9)   // IRIDESCENT: thin-film hue shift with viewing angle over a subtle cellular sheen
                    {
                        float3 Vv = GetWorldSpaceNormalizeViewDir(i.positionWS);
                        float fres = pow(1.0 - saturate(abs(dot(N, Vv))), 1.5);
                        float ph = fres * 3.0 + fbm2(luv * 4.0) * 1.5;
                        float3 film = 0.5 + 0.5 * cos(6.2831 * (ph + float3(0.0, 0.33, 0.67)));
                        baseC = lerp(cA, film * max(max(cA.r, cA.g), cA.b) * 1.6, 0.35 + 0.5 * fres);
                        localSmooth = 0.9; localSpec = 0.9;
                    }
                    else if (ls == 0)   { baseC *= 0.92;                       localSmooth = 0.10; localSpec = 0.04; }               // MATTE
                    else if (ls == 1)   { baseC *= lerp(1.0, 0.92, veins*0.4); localSmooth = 0.82; localSpec = 0.85; }               // GLOSSY
                    else if (ls == 2)   { baseC *= lerp(1.0, 0.60, veins);     localSmooth = 0.30; localSpec = 0.12; }               // VEINED
                    else if (ls == 3)   { baseC *= lerp(0.66, 1.30, mottle);   localSmooth = 0.35; localSpec = 0.16; }               // MOTTLED
                    else                { baseC *= lerp(1.0, 0.72, veins);     localSmooth = lerp(0.90, 0.22, veins); localSpec = 0.80; } // WAXY: veins = matte channels in a glossy blade
                }

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainL = GetMainLight(shadowCoord);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                float ndl = saturate(dot(N, mainL.direction));
                float atten = mainL.shadowAttenuation * mainL.distanceAttenuation
                            * CloudShadow(i.positionWS + float3(_SurfOrigin.x, 0, _SurfOrigin.y), mainL.direction);
                // living tissue scatters light under its skin: soft WRAP diffuse instead of a hard Lambert terminator
                float ndlW = saturate((dot(N, mainL.direction) + 0.25) / 1.25);
                float3 diffuse = mainL.color * (ndlW * atten);

                float3 H = normalize(mainL.direction + V);
                float spec = pow(saturate(dot(N, H)), lerp(8.0, 90.0, localSmooth)) * localSpec * ndl * atten;

                // ambient occlusion: the base and interior of a plant sit in the shade of the rest of it
                // ambient occlusion: baked per vertex (uv.w — canopy interiors, undersides, crotches); older meshes without
                // it fall back to "the base sits in the shade of the rest"
                float bakedAO = i.uv.w > 0.001 ? i.uv.w : lerp(0.45, 1.0, saturate(i.hf * 1.4 + 0.15));
                float3 ambient = SurfSH(N) * bakedAO;
                diffuse *= lerp(1.0, bakedAO, 0.3);                        // light filtering through the canopy

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light al = GetAdditionalLight(li, i.positionWS);
                    diffuse += al.color * (saturate(dot(N, al.direction)) * al.shadowAttenuation * al.distanceAttenuation);
                }
                #endif

                float3 col = baseC * (ambient + diffuse) + mainL.color * spec;
                // grazing-angle sheen (Fresnel) — waxy cuticles and wet flesh catch the sky at the silhouette
                col += SurfSH(N) * pow(1.0 - saturate(dot(N, V)), 4.0) * 0.35 * (0.3 + localSmooth);

                // Leaf translucency: thin membranes glow when backlit (light hitting the far side + viewing toward the sun).
                if (_Translucency > 0.001)
                {
                    float backLit = saturate(dot(-N, mainL.direction));
                    float forward = pow(saturate(dot(V, -mainL.direction)), 2.5);
                    float3 trans = mainL.color * mainL.shadowAttenuation * (backLit * 0.4 + forward) * _Translucency;
                    col += baseC * trans * 1.4;
                }

                col += _AccentColor.rgb * saturate(i.color.a) * _Glow * 1.5 + _BaseColor.rgb * _Glow * 0.25;
                col += (_AccentColor.rgb * 0.8 + float3(0.1, 0.6, 0.55)) * _BioGlow * (0.3 + 0.7 * saturate(i.color.a + i.hf * 0.5));   // bioluminescent night
                col *= lerp(1.0 - _Understorey * 0.55, 1.0, i.hf);   // understorey darkening
                col = MixFog(col, i.fog);
                col = (any(isnan(col)) || any(isinf(col))) ? float3(0, 0, 0) : max(col, 0);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;
            float _CurvK;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            float4 GetShadowClip(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                { float2 d = posWS.xz - _WorldSpaceCameraPos.xz; posWS.y -= dot(d, d) * _CurvK; }
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - posWS);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 clip = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, ld));
            #if UNITY_REVERSED_Z
                clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
            #else
                clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return clip;
            }
            V shadowVert(A v) { V o; o.positionCS = GetShadowClip(v); return o; }
            half4 shadowFrag(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _CurvK;
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V depthVert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float2 d = ws.xz - _WorldSpaceCameraPos.xz; ws.y -= dot(d, d) * _CurvK;
                o.positionCS = TransformWorldToHClip(ws); return o;
            }
            half4 depthFrag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
