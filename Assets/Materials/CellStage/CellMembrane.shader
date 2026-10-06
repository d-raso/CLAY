Shader "CLAY/CellStage/Membrane"
{
    // A living lipid vesicle, drawn as a real BILAYER.
    //  • Close up: the two leaflets resolve into rings of tiny glossy lipid HEADS (outer and inner surface) with faint
    //    TAIL striations between them; this detail fades by screen size (no shimmer), leaving a soft double line.
    //  • Fluid mosaic: lipid rafts drift around the membrane (colour, thickness, sheen vary and flow).
    //  • Thermal undulation: the outline flickers in several modes; strain adds a fast shiver.
    //  • Inside: translucent, faintly refractive interior with slow swirling mottling; light glows through the rim.
    //  • Alien: soft iridescence, replicator glow lighting the bilayer from within, sparks where strained.
    // Same per-quad inputs as the old Blob shader (drop-in):
    //   uv1 = (strain, glow, wrinkle, pinch)   uv2 = (seed, thickness, burst flash, selected)
    // The quad spans ±2.3 radii so halos fade out well inside it.
    Properties { _Day ("Daylight", Float) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            float _Day;
            float _CellIllum;
            struct appdata { float4 vertex : POSITION; float3 uv0 : TEXCOORD0; float4 p1 : TEXCOORD1; float4 p2 : TEXCOORD2; float4 p3 : TEXCOORD3; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; float4 p1 : TEXCOORD1; float4 p2 : TEXCOORD2; float4 p3 : TEXCOORD3; float4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv0; o.p1 = v.p1; o.p2 = v.p2; o.p3 = v.p3; o.color = v.color; return o; }

            float hashI(int2 q) { uint h = (uint)q.x * 1597334677u ^ (uint)q.y * 3812015801u; h = (h ^ (h >> 16)) * 0x45d9f3bu; h ^= h >> 16; return h * (1.0 / 4294967295.0); }
            float vn(float2 p)
            {
                p = mul(float2x2(0.8, -0.6, 0.6, 0.8), p);
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                int2 q = int2(i);
                return lerp(lerp(hashI(q), hashI(q + int2(1, 0)), f.x), lerp(hashI(q + int2(0, 1)), hashI(q + int2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p) { return vn(p) * 0.5 + vn(p * 2.03 + 5.1) * 0.3 + vn(p * 4.1 + 9.7) * 0.2; }
            // nearest / second-nearest feature distance and the nearest feature's position (irregular shards)
            void worleyF(float2 p, out float d1, out float d2, out float2 fp)
            {
                float2 c = floor(p); d1 = 9; d2 = 9; fp = 0;
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                {
                    int2 g = int2(c) + int2(x, y);
                    float2 o = float2(g) + float2(hashI(g), hashI(g + int2(17, 31)));
                    float d = length(p - o);
                    if (d < d1) { d2 = d1; d1 = d; fp = o; } else if (d < d2) d2 = d;
                }
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv.xy;
                // body shape (S1 after the ring): a rod (capsule), curved like a vibrio, or lobed
                float elong = i.p3.x, bendK = i.p3.y, lobes = i.p3.z;
                // SPECIES LOOK: one seed → independent traits, so species differ in kind, not just colour
                float sp = i.p3.w;
                float spOn = step(0.0001, sp);
                float h1 = frac(sp * 7.31 + 0.11), h2 = frac(sp * 13.7 + 0.53), h3 = frac(sp * 3.97 + 0.29), h4 = frac(sp * 19.3 + 0.71), h5 = frac(sp * 5.11 + 0.37), h6 = frac(sp * 23.9 + 0.83);
                int pattern = (int)floor(h1 * 6.0);                           // interior: plain · spots · stripes · net · speckle · rings
                float rimStyle = h2;                                          // membrane edge: smooth · rippled · beaded · spiky
                float sat = lerp(0.35, 1.35, h3);                             // muted … vivid
                float opac = lerp(0.6, 1.25, h4);                             // glassy … dense
                int feature = (int)floor(h5 * 4.0);                           // none · gel halo · storage granules · soft poles
                float patScale = lerp(3.0, 7.0, h6);
                p.y -= bendK * (p.x * p.x - 0.35) * 0.55;
                float halfL = elong * 0.55;
                p.x = sign(p.x) * max(abs(p.x) - halfL, abs(p.x) * 0.25 / (1.0 + halfL));
                float strain = i.p1.x, glow = i.p1.y, wrinkle = i.p1.z, pinch = i.p1.w;
                float seed = i.p2.x, thick = i.p2.y, flash = i.p2.z, sel = i.p2.w;
                float alive = saturate(i.uv.z);                               // 0 protocell … 1 living cell
                float shardMask = 1.0, burstProg = 0.0;
                if (flash > 0.0)
                {
                    // the bag relaxes and spreads like goo: softly expanding and wobbling (no rigid pieces)
                    burstProg = 1.0 - flash;
                    p /= 1.0 + burstProg * 0.45;
                    p += (float2(fbm(p * 2.0 + seed + _Time.y * 0.5), fbm(p * 2.0 + seed * 1.7 - _Time.y * 0.5)) - 0.5) * burstProg * 0.6;
                }
                float t = _Time.y;
                float ang = atan2(p.y, p.x);
                float2 cs = float2(cos(ang), sin(ang));                       // seamless angular coordinate for noise
                float r = length(p);

                // ── shape: thermal undulation (several modes), strain shiver, drying pucker; two lobes while dividing ──
                float und = 0.022 * sin(ang * 2.0 + t * 0.45 + seed) + 0.018 * sin(ang * 3.0 - t * 0.7 + seed * 1.7)
                          + 0.012 * sin(ang * 5.0 + t * 1.1 + seed * 2.3) + 0.006 * sin(ang * 8.0 - t * 1.9 + seed)
                          + strain * 0.018 * sin(ang * 29.0 + t * 11.0)
                          + wrinkle * 0.05 * sin(ang * 13.0 + seed * 3.0)
                          + lobes * 0.075 * sin(ang * 3.0 + seed * 2.0) + lobes * 0.03 * sin(ang * 5.0 + seed)
                          + spOn * (rimStyle > 0.66 ? 0.045 * (fbm(cs * 2.5 + seed * 3.0 + t * 0.03) - 0.5)        // lumpy
                                  : rimStyle > 0.33 ? 0.02 * (fbm(cs * 6.0 + seed * 5.0 + t * 0.12) - 0.5)          // finely puckered
                                  : 0.0);
                float off = pinch * 0.55, rl = 1.0 - pinch * 0.28;
                float d1 = length(p - float2(off, 0)) / (rl + und) - 1.0;
                float d2 = length(p + float2(off, 0)) / (rl + und) - 1.0;
                float k = 0.25 * (1.0 - pinch * 0.8) + 0.02;
                float hh = saturate(0.5 + 0.5 * (d2 - d1) / k);
                float d = lerp(d2, d1, hh) - k * hh * (1.0 - hh);            // signed distance (radius units), <0 inside

                float px = fwidth(d) + 1e-5;                                  // pixel size in radius units (detail LOD)
                float3 tint = i.color.rgb;

                // ── fluid mosaic: rafts drifting around the membrane ──
                float raft = fbm(cs * 2.2 + float2(t * 0.06, -t * 0.045) + seed);
                float raft2 = fbm(cs * 5.0 - float2(t * 0.11, t * 0.07) + seed * 1.3);
                float width = (0.055 + thick * 0.045) * (0.85 + 0.35 * raft) * (1.0 - strain * 0.35) * (1.0 + alive * 1.3);   // bilayer thickness (living: much thicker)

                // leaflet positions: outer heads at the edge, inner heads one bilayer-width in
                float dO = d + width * 0.12, dI = d + width * 0.88;
                float leafO = exp(-pow(dO / max(width * 0.16, px * 0.8), 2.0));
                float leafI = exp(-pow(dI / max(width * 0.16, px * 0.8), 2.0));
                float core = smoothstep(width * 0.95, width * 0.55, -d) * smoothstep(-width * 0.05, width * 0.15, -d);   // hydrophobic interior band

                // lipid heads as beads along each leaflet (visible only when they span a few pixels)
                float N = 110.0;
                float spacing = 6.2831853 / N;                               // in radius units (≈ angle at r = 1)
                float lod = saturate((spacing / px - 2.5) / 3.0);
                float beadPh = frac((ang / 6.2831853) * N + raft2 * 0.6 + t * 0.02);
                float bead = smoothstep(0.5, 0.15, abs(beadPh - 0.5));
                float beadI = smoothstep(0.5, 0.15, abs(frac(beadPh + 0.5) - 0.5));   // inner leaflet offset by half a bead
                float tails = 0.5 + 0.5 * cos(beadPh * 6.2831853);           // striations between the leaflets
                float headsO = leafO * lerp(1.0, 0.55 + 0.75 * bead, lod);
                float headsI = leafI * lerp(1.0, 0.55 + 0.75 * beadI, lod);
                float tailBand = core * lerp(0.55, 0.35 + 0.35 * tails, lod);

                // ── colour: tint + iridescent film that shifts with raft thickness and angle ──
                float film = thick * 1.6 + raft * 0.9 - strain * 0.5 + ang * 0.04 + t * 0.02;
                float3 irid = 0.5 + 0.5 * cos(6.2831853 * (film + float3(0.0, 0.33, 0.67)));
                float3 headC = lerp(tint * 1.25 + 0.12, irid, 0.3 + strain * 0.25);
                float3 tailC = lerp(tint * 0.7, irid * 0.6, 0.25);
                float lit = 0.6 + 0.45 * saturate(dot(cs, normalize(float2(-0.6, 0.8))));    // upper-left light catches the rim
                float spec = pow(saturate(dot(cs, normalize(float2(-0.6, 0.8)))), 14.0) * leafO * 0.5 * (1.0 - wrinkle * 0.6);

                // ── interior: translucent, slowly swirling, light transmitted through the rim ──
                float inside = 1.0 - smoothstep(-px, px, d);
                float pulse = 0.62 + 0.38 * sin(t * 2.4 + seed);
                float swirl = fbm(p * 2.6 + float2(sin(t * 0.13 + seed), cos(t * 0.11 + seed)) * 0.8);
                float rimLight = smoothstep(-0.45, 0.0, d) * inside;         // light glowing through the membrane edge
                float3 col = tint * (0.22 + 0.12 * swirl) + tint * rimLight * 0.25;
                float a = inside * (0.07 + 0.05 * swirl + rimLight * 0.08);
                // living cytoplasm: dense, crowded, slowly churning, granular
                float gran = fbm(p * 9.0 + float2(sin(t * 0.2 + seed), cos(t * 0.17 + seed)) * 1.5);
                float churn = fbm(p * 3.2 + float2(t * 0.07, -t * 0.05) + seed);
                // a living cell has VOLUME: a translucent sphere of cytoplasm, lit across its body (lighter toward the
                // light, deeper away), light transmitted bright through the rim (fresnel), churning granular interior
                float rr2 = saturate(r * r / max((rl + und) * (rl + und), 1e-3));
                float dome = sqrt(saturate(1.0 - rr2));
                float3 nrm = normalize(float3(p * (1.0 - dome) * 1.4, dome + 0.15));
                float lam = saturate(dot(nrm, normalize(float3(-0.5, 0.6, 0.65))));
                float fres = pow(1.0 - dome, 2.5);
                float3 cyto = tint * (0.35 + 0.25 * churn) * (0.55 + 0.6 * lam) + gran * 0.07;
                cyto += lerp(tint, float3(1, 1, 1), 0.4) * fres * 0.45;                 // light glowing through the edge
                cyto += pow(saturate(dot(reflect(-normalize(float3(-0.5, 0.6, 0.65)), nrm), float3(0, 0, 1))), 30.0) * 0.35;   // glossy highlight
                col = lerp(col, cyto, alive * inside);
                a = lerp(a, inside * (0.55 + 0.15 * churn + fres * 0.2), alive);
                col += glow * pulse * lerp(float3(1.0, 0.9, 0.65), float3(0.7, 0.92, 1.0), alive) * 0.35 * inside * (0.6 + 0.4 * swirl);   // a replicator glows within
                a += glow * pulse * 0.12 * inside;

                // ── bilayer on top ──
                float3 memC = tailC;
                float memA = tailBand * 0.45;
                memC = lerp(memC, headC * lit, saturate(headsO));
                memA = max(memA, headsO * (0.95 - wrinkle * 0.2));
                memC = lerp(memC, headC * lit * 0.85, saturate(headsI) * (1.0 - saturate(headsO)));
                memA = max(memA, headsI * 0.75);
                memC += glow * pulse * lerp(float3(1.0, 0.85, 0.55), float3(0.7, 0.92, 1.0), alive) * 0.35 * saturate(headsI + tailBand);  // lit from within
                memC += spec;
                // a LIVING cell has no crisp bilayer rings (they read as a bevel): one soft, thick membrane that
                // melts into the cell's volume — brightest where light passes through it, no beads, no lines
                {
                    float soft = exp(-pow((d + width * 0.35) / max(width * 0.9, px * 2.0), 2.0));
                    float3 softC = lerp(tint * 1.05, float3(1, 1, 1), 0.22) * (0.75 + 0.35 * lit);
                    memC = lerp(memC, softC, alive);
                    memA = lerp(memA, soft * 0.55, alive);
                }
                col = lerp(col, memC, saturate(memA));
                a = max(a, saturate(memA));

                // transporter pores: bright gates studding the living membrane, gently pulsing
                if (alive > 0.01)
                {
                    // transporter pores: faint pale specks in the membrane (not lines)
                    float porePh = frac(ang / 6.2831853 * 26.0 + seed * 0.13);
                    float2 pq = float2((porePh - 0.5) * 6.2831853 / 26.0, (d + width * 0.5) / max(width, px));
                    float pore = exp(-dot(pq * float2(55.0, 3.0), pq * float2(55.0, 3.0)));
                    col = lerp(col, lerp(col, float3(1, 1, 1), 0.5), saturate(pore * alive * 0.6));
                }
                // strain sparks: tiny bright points flickering along a stretched membrane
                float spark = strain * leafO * step(0.985, hashI(int2(floor(beadPh * 7.0 + ang * 40.0), floor(t * 6.0)))) * 1.5;
                col += spark * float3(1.0, 0.95, 0.85); a = max(a, spark * 0.8);

                // replicator halo outside the cell, fading well inside the quad
                float halo = glow * exp(-max(d, 0.0) * 3.5) * (1.0 - inside) * pulse * 0.35 * (1.0 - smoothstep(1.5, 2.2, r));
                col = lerp(col, float3(1.0, 0.92, 0.65), halo / max(a + halo, 1e-3));
                a = saturate(a + halo);

                // microscope illumination
                if (_CellIllum > 0.5 && _CellIllum < 1.5) { col += headC * (headsO + headsI) * 0.6; a = max(a, saturate(headsO) * 0.9); }
                if (_CellIllum > 1.5) { float ph = exp(-pow((d - 0.1) / 0.05, 2.0)) * (1.0 - inside); col = lerp(col, float3(1, 1, 1), ph * 0.55); a = max(a, ph * 0.45); }

                // DIC: the membrane in relief — lit along the shear side, shadowed on the other; colours muted
                if (_CellIllum > 2.5 && alive < 0.5)
                {
                    float rel = dot(cs, float2(0.707, 0.707));
                    col *= 1.0 + rel * saturate(memA + rimLight) * 0.85;               // full colour, embossed bilayer
                    a = max(a, saturate(memA) * 0.9);
                }
                // respawn picker: a soft pulsing ring
                float selRing = sel * exp(-pow((d - 0.22 - 0.04 * sin(t * 4.0)) / 0.035, 2.0));
                col = lerp(col, float3(1, 1, 1), selRing * 0.9); a = max(a, selRing * 0.8);
                // burst: the bilayer tears into curling fragments flying outward
                if (flash > 0.0)
                {
                    // the membrane tears open in soft patches; the contents seep out as a cloud that spreads and thins
                    // holes open in the membrane where 2D noise falls below a rising threshold (irregular blotches, not
                    // slices); the contents inside just thin and fade
                    float holes = smoothstep(burstProg * 1.2 - 0.2, burstProg * 1.2, fbm(p * 2.6 + seed * 3.1));
                    float band = exp(-pow((d + 0.05) / 0.25, 2.0));                    // the membrane region
                    float torn = lerp(1.0, holes, band);
                    float fadeB = 1.0 - smoothstep(0.45, 1.0, burstProg);
                    float interiorFade = lerp(1.0, 1.0 - smoothstep(0.0, 0.7, burstProg), (1.0 - band) * step(d, 0.0));
                    a *= torn * interiorFade * fadeB;
                    float cloud = exp(-max(d, 0.0) * (3.0 - burstProg * 2.0)) * (1.0 - torn * 0.5) * (1.0 - smoothstep(0.3, 1.0, burstProg)) * 0.35;
                    col = lerp(col, tint * 0.9, cloud); a = max(a, cloud);
                    // a quick soft flash of the released contents at the start
                    float puff = exp(-r * r * 1.2) * saturate(flash * 3.0 - 2.0) * 0.5;
                    col += tint * puff; a = max(a, puff);
                }
                if (spOn > 0.5 && alive > 0.01 && flash <= 0.0)
                {
                    // everything domain-warped and slowly churning: organic, never geometric
                    float2 q = p * patScale * 0.6 + seed;
                    q += (float2(fbm(q * 0.7 + t * 0.02), fbm(q * 0.7 + 5.2 - t * 0.02)) - 0.5) * 2.2;
                    float pat = 0.0;
                    if (pattern == 1) pat = smoothstep(0.55, 0.72, fbm(q));                                         // soft mottled blotches
                    else if (pattern == 2) { float c2 = fbm(q * 1.8); pat = smoothstep(0.62, 0.75, c2) * (0.6 + 0.4 * fbm(q * 4.0)); }   // granular clusters
                    else if (pattern == 3) { float rr2 = 1.0 - abs(fbm(q * 1.2) * 2.0 - 1.0); pat = smoothstep(0.8, 0.95, rr2); }       // faint veins
                    else if (pattern == 4) pat = smoothstep(0.78, 0.86, vn(q * 3.5)) * smoothstep(0.35, 0.6, fbm(q * 0.6));       // patchy freckles
                    else if (pattern == 5) pat = smoothstep(0.3, 0.75, 0.5 + 0.5 * sin(q.x * 1.3 + fbm(q) * 6.0)) * 0.7;         // marbling
                    float3 patC = lerp(tint * 0.55, lerp(tint, float3(1, 1, 1), 0.45), h2);   // darker or paler marks
                    col = lerp(col, patC, pat * 0.45 * inside * alive);
                    // saturation and opacity
                    float lum = dot(col, float3(0.3, 0.59, 0.11));
                    col = lerp(lum.xxx, col, lerp(1.0, sat, alive));
                    a = lerp(a, saturate(a * opac), inside * alive);
                    // distinguishing features
                    if (feature == 1)        // a soft, uneven gel halo around the cell
                    {
                        float hw = 0.12 + 0.06 * (fbm(cs * 3.0 + seed + t * 0.02) - 0.5);
                        float cap = smoothstep(hw + 0.1, hw * 0.3, d) * step(0.0, d);
                        col = lerp(col, lerp(tint, float3(1, 1, 1), 0.7), cap * 0.2); a = max(a, cap * 0.18 * alive);
                    }
                    else if (feature == 2)   // storage granules: a few dense, irregular inclusions
                    {
                        float gq = fbm(p * 5.0 + seed * 7.0 + t * 0.01);
                        float gr = smoothstep(0.7, 0.76, gq) * inside;
                        col = lerp(col, tint * 0.35, gr * 0.6);
                    }
                    else if (feature == 3)   // a soft brightening toward the ends of the body
                    {
                        float pole = smoothstep(0.5, 1.0, abs(p.x)) * inside * (0.6 + 0.4 * fbm(p * 3.0 + seed));
                        col += lerp(tint, float3(1, 1, 1), 0.5) * pole * 0.18;
                    }
                }
                col *= lerp(0.35, 1.0, _Day) + glow * 0.3;
                return fixed4(col, a * i.color.a);
            }
            ENDCG
        }
    }
}
