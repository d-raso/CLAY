Shader "CLAY/CellStage/Soup"
{
    // The tide pool rendered as PHYSICAL WATER seen from above.
    //
    // Surface: a sum of travelling waves (incommensurate wavelengths, hashed directions → never repeats), amplitude
    // modulated by slow large-scale "wave groups" (calm patches and choppy patches drift around). Slope (gradient)
    // and curvature (Hessian) are evaluated ANALYTICALLY per pixel.
    //
    // Light, all derived from that one surface:
    //  • Refraction — the floor is seen displaced along the surface slope, more where deeper.
    //  • Caustics   — light refracted by the surface lands at x + D(x), D = −K·depth·∇h. Where the surface focuses
    //                 rays the floor receives 1/|det(I − K·depth·Hess h)| more light. Evaluated separately for R/G/B
    //                 (slightly different K = dispersion) → faint rainbow fringes, sharpening with depth, then
    //                 blurring out into a glow deeper down (scattering).
    //  • Glints     — specular reflection of the sun off the wave normals (sharp sparkles) + a soft sky sheen.
    //  • Depth      — Beer–Lambert absorption (reds go first), in-scattered glow fed by the caustic light above.
    // Dry ground: relief-shaded rock, silt, pebbles, a wet band and salt crust at the waterline.
    Properties
    {
        _Height ("Height", 2D) = "black" {}
        _Tide ("Tide level", Float) = 0.5
        _Day ("Daylight", Float) = 1
        _CausticsOn ("Caustics on", Float) = 1
        _WaveEnergy ("Wave energy", Float) = 1
        _WaterShallow ("Water shallow", Color) = (0.35, 0.62, 0.6, 1)
        _WaterDeep ("Water deep", Color) = (0.05, 0.16, 0.22, 1)
        _Rock ("Rock", Color) = (0.42, 0.37, 0.31, 1)
        _Crust ("Evaporite crust", Color) = (0.86, 0.84, 0.78, 1)
        _RockHigh ("Rock (high ground)", Color) = (0.62, 0.56, 0.48, 1)
        _Accent ("Mineral accent", Color) = (0.55, 0.42, 0.3, 1)
        _Glow ("Waterline glow", Color) = (0.55, 0.95, 0.9, 1)
        _MudCracks ("Mud cracks (clay)", Float) = 0
        _GroundMix ("Ground mix (sand, mud, rock, crust)", Vector) = (0.4, 0.2, 0.3, 0.1)
        _GroundSeed ("Ground seed", Float) = 0
        _ArenaSize ("Arena size", Vector) = (192, 120, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _Height; float4 _Height_TexelSize;
            float _CellIllum; float _MudCracks; float4 _GroundMix; float _GroundSeed;   // 0 brightfield · 1 darkfield · 2 phase contrast (global)
            float _Tide, _Day, _CausticsOn, _WaveEnergy; float4 _WaterShallow, _WaterDeep, _Rock, _Crust, _ArenaSize, _RockHigh, _Accent, _Glow;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.sp = ComputeScreenPos(o.pos); return o; }

            // The height texture is 32-bit float, which many GPUs only point-sample (blocky shorelines) —
            // filter it here: a smooth (smoothstep-weighted) bilinear of the four texels.
            // Height: a cubic B-spline over 4×4 texels. Continuous slope AND curvature everywhere, so the relief
            // lighting shows no texel structure (bilinear / smoothstep blends put a bump at every texel → a dot grid).
            float4 BW(float f)   // cubic B-spline weights for the 4 taps at offsets −1, 0, 1, 2
            {
                float f2 = f * f, f3 = f2 * f;
                return float4((1.0 - 3.0 * f + 3.0 * f2 - f3), (4.0 - 6.0 * f2 + 3.0 * f3), (1.0 + 3.0 * f + 3.0 * f2 - 3.0 * f3), f3) / 6.0;
            }
            float Tex(float2 t) { return tex2Dlod(_Height, float4(t, 0, 0)).r; }
            float H(float2 uv)
            {
                float2 st = uv * _Height_TexelSize.zw - 0.5;
                float2 i = floor(st), f = st - i;
                float4 wx = BW(f.x), wy = BW(f.y);
                float2 ts = _Height_TexelSize.xy;
                float2 b = (i + 0.5) * ts;
                float h = 0.0;
                [unroll] for (int y = 0; y < 4; y++)
                {
                    float vy = b.y + (y - 1) * ts.y;
                    float row = wx.x * Tex(float2(b.x - ts.x, vy)) + wx.y * Tex(float2(b.x, vy))
                              + wx.z * Tex(float2(b.x + ts.x, vy)) + wx.w * Tex(float2(b.x + 2.0 * ts.x, vy));
                    h += row * (y == 0 ? wy.x : y == 1 ? wy.y : y == 2 ? wy.z : wy.w);
                }
                return h;
            }
            // integer hash: exact at any coordinate (the float sin/frac hashes band and repeat at large coords)
            float hashI(int2 q)
            {
                uint h = (uint)q.x * 1597334677u ^ (uint)q.y * 3812015801u;
                h = (h ^ (h >> 16)) * 0x45d9f3bu;
                h ^= h >> 16;
                return h * (1.0 / 4294967295.0);
            }
            float hash(float2 p) { return hashI(int2(floor(p))); }
            float2 hash2(float2 p) { int2 q = int2(floor(p)); return float2(hashI(q), hashI(q + int2(113, 271))); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y); }
            float fbm(float2 p) { p = mul(float2x2(0.8, -0.6, 0.6, 0.8), p); float s = 0, a = 0.5; for (int k = 0; k < 5; k++) { s += a * vn(p); p = mul(float2x2(1.6, 1.2, -1.2, 1.6), p) + 3.1; a *= 0.5; } return s; }
            float2 worley(float2 p)
            {
                float2 i = floor(p), f = frac(p); float d1 = 8, d2 = 8;
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y); float2 c = o + hash2(i + o) - f; float d = dot(c, c);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                return float2(sqrt(d1), sqrt(d2));
            }

            // 2D simplex noise (Ashima Arts / McEwan, MIT) — no square lattice
            float3 m289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float2 m289(float2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float3 permute(float3 x) { return m289(((x * 34.0) + 1.0) * x); }
            float snoise(float2 v)
            {
                const float4 C = float4(0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439);
                float2 i = floor(v + dot(v, C.yy));
                float2 x0 = v - i + dot(i, C.xx);
                float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
                float4 x12 = x0.xyxy + C.xxzz;
                x12.xy -= i1;
                i = m289(i);
                float3 p = permute(permute(i.y + float3(0.0, i1.y, 1.0)) + i.x + float3(0.0, i1.x, 1.0));
                float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
                m = m * m; m = m * m;
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
            float Ripple(float2 q) { return snoise(q) * 0.65 + snoise(q * 2.13 + 5.7) * 0.35; }

            // ── the wave surface: h, ∇h and the Hessian, analytically ──
            #define NWAVES 16
            void Waves(float2 p, float t, out float2 grad, out float3 hess)
            {
                grad = 0; hess = 0;   // hess = (hxx, hyy, hxy)
                // wave groups: slow large patches of calm and chop
                float group = 0.35 + 1.1 * fbm(p * 0.018 + float2(t * 0.012, -t * 0.009));
                // a slow warp field bends every wavefront: real crests are curved and broken, never straight lines
                // (straight sinusoids sum into a visible lattice of caustic lines)
                float2 warp = (float2(fbm(p * 0.045 + float2(3.7, t * 0.02)), fbm(p * 0.045 + float2(-t * 0.017, 9.1))) - 0.5) * 9.0;
                float2 pw = p + warp;
                [unroll] for (int i = 0; i < NWAVES; i++)
                {
                    float fi = i;
                    // directions: golden-angle spread from an offset start, plus a fixed irrational jitter — none near the axes
                    float ang = 0.4 + fi * 2.399963 + 0.45 * sin(fi * 2.31 + 0.5);
                    float lambda = 2.4 * pow(1.17, fi) * (0.9 + 0.2 * frac(fi * 0.618034));   // 2.4 … ~25 units, incommensurate
                    float k = 6.2831853 / lambda;
                    float2 kd = float2(cos(ang), sin(ang)) * k;
                    float w = sqrt(9.8 * k) * 0.22;                                    // dispersion: long waves travel faster
                    // steepness ∝ 1/√k: curvature (∝ slope·k) spreads evenly across wavelengths — no wave dominates
                    float slope = 0.07 * group / sqrt(k) * (0.6 + 0.4 * frac(fi * 0.381 + 0.2));
                    float A = slope / k * (0.62 + 0.38 * saturate(_WaveEnergy));   // calm water: gentler waves, but caustics never vanish
                    float ph = dot(kd, pw) - w * t + fi * 1.913;
                    float s = sin(ph), c = cos(ph);
                    grad += A * c * kd;
                    hess += -A * s * float3(kd.x * kd.x, kd.y * kd.y, kd.x * kd.y);
                }
                // fine capillary ripples: warped SIMPLEX noise (triangular lattice, then domain-warped) — a handful of
                // straight sines here interfered into a fine regular grid; noise ripples are chaotic like real chop
                float2 q = pw * 0.55 + float2(t * 0.12, -t * 0.09);          // broader, slower chop
                q += float2(snoise(q * 0.21 + 11.3), snoise(q * 0.21 - 7.9)) * 0.35;   // a gentle bend, not swirls
                const float re = 0.12;
                float2 rg = float2(Ripple(q + float2(re, 0)) - Ripple(q - float2(re, 0)),
                                   Ripple(q + float2(0, re)) - Ripple(q - float2(0, re))) / (2.0 * re);
                grad += rg * 0.55 * 0.009 * group * (0.6 + 0.4 * saturate(_WaveEnergy));                           // a faint surface texture on the big waves
            }

            // caustic gain for refraction strength K·depth: 1/|det(I − kd·Hess)|, softened & clamped
            float CausticGain(float3 hess, float kd)
            {
                float a = 1.0 - kd * hess.x, d = 1.0 - kd * hess.y, b = -kd * hess.z;
                float det = abs(a * d - b * b);
                return min(1.0 / max(det, 0.12), 7.0);
            }

            // ── the ground, flOw-style: soft layered forms, not gritty texture ──
            // Colour follows the planet's own geology palette (low → high ground, like the surface terrain's elevation
            // ramp) with broad mineral patches; form comes from smooth relief light, soft occlusion in hollows and
            // gentle contour terraces (strata), so the shoreline reads as calm, sculpted layers.
            float3 FloorColour(float2 wp, float wet, out float relief, out float3 n)
            {
                float2 uv = wp / _ArenaSize.xy;
                float h = H(uv);
                float2 e = _Height_TexelSize.xy * 1.5;
                float hL = H(uv - float2(e.x, 0)), hR = H(uv + float2(e.x, 0)), hD = H(uv - float2(0, e.y)), hU = H(uv + float2(0, e.y));
                float2 g = float2(hR - hL, hU - hD);
                n = normalize(float3(-g * 55.0, 1));
                relief = saturate(dot(n, normalize(float3(-0.45, 0.55, 0.7))));
                float lap = (hL + hR + hD + hU - 4.0 * h);
                float ao = saturate(1.0 + lap * 260.0);                                  // hollows sit in soft shade

                // elevation ramp in the planet's palette + broad, soft mineral patches
                float elev = saturate((h - _Tide + 0.05) * 3.0);
                float3 col = lerp(_Rock.rgb, _RockHigh.rgb, smoothstep(0.0, 1.0, elev));
                float patchA = smoothstep(0.45, 0.75, fbm(wp * 0.012 + 3.0));
                col = lerp(col, _Accent.rgb, patchA * 0.35);
                // strata: soft contour terraces stepping up the slope (the layered look)
                // (no contour terraces: at this scale they read as topographic lines)
                // real material detail (simplex — no lattice): fine grain, a medium mottle, gentle hue drift
                float grain = snoise(wp * 4.3) * 0.5 + snoise(wp * 11.7 + 3.1) * 0.3 + snoise(wp * 27.0 - 7.7) * 0.2;
                float mottle = snoise(wp * 0.45 + 9.0) * 0.6 + snoise(wp * 1.3 - 4.0) * 0.4;
                col *= 1.0 + grain * 0.07 + mottle * 0.08;
                col = lerp(col, col * float3(1.06, 1.0, 0.92), saturate(mottle * 0.5 + 0.5) * 0.3);
                // GROUND TYPES in random patches (like the planet surface's terrain types): each spot of floor is sand,
                // mud, bare rock or mineral crust, picked by warped low-frequency noise weighted by the biome's mix
                float2 gq = wp * 0.035 + _GroundSeed;
                gq += float2(snoise(gq * 1.7 + 4.1), snoise(gq * 1.7 - 2.3)) * 0.45;
                float4 gw = _GroundMix * (1.0 + 1.6 * float4(snoise(gq), snoise(gq + 17.3), snoise(gq - 9.1), snoise(gq + 31.7)));
                gw.z += saturate(elev - 0.6) * 0.8;                               // high ground: rock shows through
                gw.w += saturate((h - _Tide) * 20.0) * _GroundMix.w * 2.0;         // salt/carbonate crust where it dries
                gw = max(gw, 0.0); gw = gw * gw * gw; gw /= max(dot(gw, 1.0), 1e-4);
                {
                    float sandG = snoise(wp * 18.0) * 0.5 + snoise(wp * 41.0 + 2.7) * 0.5;
                    float3 sand = lerp(_RockHigh.rgb, _Crust.rgb, 0.3) * (1.0 + sandG * 0.12);
                    float3 mudC = lerp(_Rock.rgb, _Accent.rgb, 0.5) * 0.85 * (1.0 + snoise(wp * 2.2) * 0.05);
                    float rk = snoise(wp * 0.9 + 7.0) * 0.5 + snoise(wp * 3.1) * 0.3 + snoise(wp * 9.0) * 0.2;
                    float3 rockC = _Rock.rgb * (0.8 + rk * 0.3);
                    float2 cw = worley(wp * 1.4 + _GroundSeed);
                    float3 crustC = _Crust.rgb * (0.92 + 0.12 * smoothstep(0.0, 0.08, cw.y - cw.x)) * (1.0 + snoise(wp * 6.0) * 0.04);
                    float3 ground = sand * gw.x + mudC * gw.y + rockC * gw.z + crustC * gw.w;
                    col = lerp(col, ground, 0.7);
                }
                // mud patches only: drying mud cracks — curled plates, strongest on exposed, drying ground
                {
                    // curled mud plates as a real surface: a height field (domed plates, edges curling UP, deep gaps),
                    // lit from the same light as the terrain, so the cracks have form — not lines drawn on top
                    float dryness = saturate((h - _Tide + 0.012) * 22.0) * smoothstep(0.35, 0.7, gw.y);
                    {   // (no early-out: screen derivatives below need uniform control flow)
                        float2 cq = wp * 0.5 + float2(snoise(wp * 0.19), snoise(wp * 0.19 + 5.0)) * 0.7;
                        float2 wv = worley(cq);
                        float edge = wv.y - wv.x;                                     // 0 at a crack
                        float gap = smoothstep(0.015, 0.06, edge);                    // the open fissure
                        float curlUp = (1.0 - smoothstep(0.06, 0.22, edge)) * 0.55;    // plate rims curl up
                        float dome = (1.0 - smoothstep(0.0, 0.6, wv.x)) * 0.35;
                        float ph = (gap * (0.6 + curlUp + dome)) * dryness;
                        float2 gradP = float2(ddx(ph), ddy(ph)) / max(fwidth(wp.x), 1e-4);
                        #if UNITY_UV_STARTS_AT_TOP
                        gradP.y = -gradP.y;
                        #endif
                        float3 np = normalize(float3(-gradP * 0.35, 1.0));
                        float lp = saturate(dot(np, normalize(float3(-0.45, 0.55, 0.7))));
                        float3 plate = col * (1.08 + snoise(wv.xy * 7.0 + floor(cq)) * 0.05);   // each plate a slightly different tone
                        float3 shaded = plate * (0.55 + lp * 0.7);
                        float3 crack = col * 0.22;                                    // deep, dark gaps
                        col = lerp(col, lerp(crack, shaded, gap), dryness);
                    }
                }
                col *= (0.62 + relief * 0.48) * lerp(0.75, 1.0, ao);
                return col;
            }

            // microscope illumination: darkfield = a dark backdrop where only scattered light (caustic focus) shows;
            // phase contrast = muted, slightly grey, with lifted midtones
            // DIC (differential interference contrast): the classic "emboss" microscope look — a muted grey-sepia field
            // where every gradient along the shear direction shows as relief (bright on one flank, shadow on the other)
            static const float2 DICShear = float2(0.707, 0.707);
            float3 Illum(float3 col, float scatter, float relief)
            {
                if (_CellIllum > 2.5)
                {
                    return col * (1.0 + clamp(relief, -0.6, 0.6)) + scatter * 0.06;     // full colour, embossed relief
                }
                if (_CellIllum > 0.5 && _CellIllum < 1.5) return col * 0.3 + col * scatter * 0.6;
                if (_CellIllum > 1.5) { float l = dot(col, float3(0.3, 0.59, 0.11)); return lerp(col, l * float3(0.86, 0.92, 0.95), 0.45) * 1.05 + 0.03; }
                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 wp = i.uv * _ArenaSize.xy;
                float depth = _Tide - H(i.uv);
                float wet = smoothstep(-0.0015, 0.0015, depth);
                float sun = _Day;
                float3 L = normalize(float3(-0.22, 0.28, 1.0));            // high sun, a little to the upper left

                // ── dry ground ──
                if (wet <= 0.0)
                {
                    float relief; float3 n;
                    float3 col = FloorColour(wp, 0.0, relief, n);
                    float above = -depth;
                    float damp = 1.0 - smoothstep(0.0, 0.04, above);                    // wet band just above the water
                    float rime = smoothstep(0.006, 0.02, above) * (1.0 - smoothstep(0.03, 0.08, above))
                               * smoothstep(0.4, 0.7, fbm(wp * 0.25));                   // salt rime where the tide just left
                    col = lerp(col, col * 0.7, damp);
                    col = lerp(col, _Crust.rgb, rime * 0.4);
                    // luminous waterline: a soft glow hugging the shore, the signature of the pool's edge
                    float glow = exp(-above / 0.012) * (0.55 + 0.45 * sun);
                    col += _Glow.rgb * glow * 0.35;
                    col += damp * pow(saturate(dot(reflect(-L, n), float3(0, 0, 1))), 30.0) * 0.3 * sun;
                    col *= lerp(0.2, 1.0, sun);
                    return fixed4(Illum(col, 0.0, dot(n.xy, DICShear) * 1.4), 1);
                }

                // ── water ──
                float dW = clamp(depth, 0.0, 0.35) * 40.0;                 // depth in world units
                float2 g; float3 hess;
                // the surface at 1.3× scale (bigger, softer webs): chain rule — ∇ scales by 1/s, the Hessian by 1/s²
                const float WS = 1.3;
                Waves(wp / WS, t, g, hess);
                g /= WS; hess /= WS * WS;
                float3 nS = normalize(float3(-g, 1.0));

                // refraction: the floor seen through the moving surface
                float2 refr = -g * dW * 0.35;
                float relief; float3 nF;
                float3 floorC = FloorColour(wp + refr, 1.0, relief, nF);
                floorC *= 0.62 * (0.55 + relief * 0.6);                    // submerged rock is wet-dark

                // caustics with dispersion (R/G/B refract slightly differently); fade in from the shallows, blur out deep
                float K = 1.15 * 1.3;                                     // keep the focusing depth the same at the larger scale
                float3 gain = float3(CausticGain(hess, K * 0.975 * dW), CausticGain(hess, K * dW), CausticGain(hess, K * 1.03 * dW));
                float deepBlur = saturate((dW - 7.0) / 7.0);
                gain = lerp(gain, 1.0.xxx, deepBlur * 0.75);
                float3 caustic = lerp(1.0.xxx, gain, saturate(dW * 0.5) * _CausticsOn);
                float3 lightOnFloor = (0.35 + 0.65 * sun * caustic) * float3(1.0, 0.99, 0.95);

                // Beer–Lambert absorption + in-scattering
                float3 sigma = float3(0.11, 0.045, 0.035) * (1.3 - _WaterDeep.rgb);
                float3 trans = exp(-sigma * dW);
                float3 waterC = lerp(_WaterShallow.rgb, _WaterDeep.rgb, saturate(dW / 12.0));
                float glowAmt = (1.0 - trans.g) * (0.55 + 0.45 * sun);
                float scatterLight = 0.5 + 0.5 * saturate(dot(gain, 0.333.xxx) - 0.6) * _CausticsOn * sun;   // caustics are sunlight: none at night   // bright caustics above → glowing water
                float3 col = floorC * lightOnFloor * trans + waterC * glowAmt * (0.65 + 0.35 * scatterLight) * (0.3 + 0.7 * sun);

                // light shafts: sunbeams through the water column, seen from above as soft broad bands slanted along
                // the sun's direction (simplex noise across the beam, stretched along it — no lattice), swaying slowly
                {
                    float2 sunDir = normalize(L.xy);
                    float2 across = float2(-sunDir.y, sunDir.x);
                    float u = dot(wp, across) * 0.022, v = dot(wp, sunDir) * 0.006;      // broad beams (~40 units wide)
                    float beams = snoise(float2(u + t * 0.012, v - t * 0.006)) * 0.7 + snoise(float2(u * 1.7 - t * 0.02, v + 4.0)) * 0.3;
                    beams = smoothstep(0.05, 0.85, beams * 0.5 + 0.5);                  // soft, wide falloff — no thin lines
                    float shaftAmt = beams * saturate((dW - 1.5) / 10.0) * sun;
                    col += float3(0.96, 0.98, 1.0) * shaftAmt * 0.1;
                }

                // surface: sky sheen on steep slopes + sun glints (sparkles that ride the waves)
                float slope = 1.0 - nS.z;
                float3 sky = float3(0.55, 0.7, 0.85);
                col += sky * saturate(0.02 + slope * 3.0) * 0.18 * (0.3 + 0.7 * sun);
                float3 Hh = normalize(L + float3(0, 0, 1));
                float spec = pow(saturate(dot(nS, Hh)), 900.0) * 6.0 + pow(saturate(dot(nS, Hh)), 120.0) * 0.25;
                col += spec * sun * float3(1.0, 0.97, 0.9) * saturate(dW * 0.8);

                // the meniscus: a thin bright rim exactly at the waterline
                float edge = exp(-depth / 0.01);
                col += _Glow.rgb * edge * 0.3 * (0.5 + 0.5 * sun);

                col *= lerp(0.16, 1.0, sun * 0.85 + 0.15);
                // film grain: a fresh random value per screen pixel per frame (never a tiled texture)
                int2 pix = int2(i.sp.xy / i.sp.w * _ScreenParams.xy);
                float grain = hashI(pix + int2(_Time.y * 60.0 * 7.0, _Time.y * 60.0 * 13.0)) - 0.5;
                col *= 1.0 + grain * 0.06;
                return fixed4(Illum(col, saturate(dot(gain, 0.333.xxx) - 1.0) * _CausticsOn * sun, dot(-g, DICShear) * 5.0 * (0.25 + 0.75 * sun) + dot(nF.xy, DICShear) * 0.6), 1);
            }
            ENDCG
        }
    }
}
