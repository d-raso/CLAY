Shader "CLAY/CellStage/Mote"
{
    // Prebiotic molecules, one batch. uv0.z = kind. Shapes teach the chemistry without words:
    //   0/1: round base with a NOTCH / with a TAB (they fit together)   2/3: triangular base, notch / tab
    //   4: lipid — polar head + two fatty tails   5: clay flake (layered hexagon)   6: other organic (a sugar ring)
    //   7: soft floor shadow (used for everything that floats)
    //   uv1.x = brightness (stranded / inside), uv1.y = paired-glow
    // Shaded as small glossy domes (normal from the shape's distance field) so they read as matter, not stickers.
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
            #include "UnityCG.cginc"
            float _Day;
            float _CellIllum;   // 0 brightfield · 1 darkfield · 2 phase contrast
            struct appdata { float4 vertex : POSITION; float3 uv0 : TEXCOORD0; float4 p1 : TEXCOORD1; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; float4 p1 : TEXCOORD1; float4 color : COLOR; float2 wp : TEXCOORD2; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv0; o.p1 = v.p1; o.color = v.color; o.wp = mul(unity_ObjectToWorld, v.vertex).xy; return o; }

            float sdCircle(float2 p, float r) { return length(p) - r; }
            float sdTri(float2 p, float r)
            {
                const float k = 1.7320508;
                p.x = abs(p.x) - r; p.y = p.y + r / k;
                if (p.x + k * p.y > 0.0) p = float2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
                p.x -= clamp(p.x, -2.0 * r, 0.0);
                return -length(p) * sign(p.y);
            }
            float sdHex(float2 p, float r)
            {
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(p); p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }
            float sdSeg(float2 p, float2 a, float2 b) { float2 pa = p - a, ba = b - a; float h = saturate(dot(pa, ba) / dot(ba, ba)); return length(pa - ba * h); }
            float smin(float a, float b, float k) { float h = saturate(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1.0 - h); }

            float Shape(float2 p, int kind)
            {
                // Puzzle-piece pairs: the TAB is the exact shape and size of its partner's NOTCH (round ↔ round,
                // triangle ↔ triangle), on the +x side, so docked partners (rotated 180°) interlock visibly.
                // flOw-style: soft jelly forms (smooth unions / subtractions, rounded everything) — the pairing still
                // reads: the round pair is an orb with a soft BITE ↔ an orb with a BULB; the teardrops likewise.
                // ICONS (deliberately symbolic, not "real" molecules): two families of puzzle pieces —
                //   round pair: a circle with a NOTCH ↔ a circle with a TAB; triangle pair likewise. Tab = partner's notch.
                if (kind == 0) return max(sdCircle(p, 0.6), -sdCircle(p - float2(0.6, 0), 0.3));                       // round, notch
                if (kind == 1) return min(sdCircle(p - float2(-0.12, 0), 0.5), sdCircle(p - float2(0.45, 0), 0.28));     // round, tab
                if (kind == 2) return max(sdTri(p.yx * float2(1, -1), 0.62), -sdTri((p - float2(0.272, 0)).yx * float2(1, -1), 0.15));   // triangle, notch
                if (kind == 3) return min(sdTri((p + float2(0.12, 0)).yx * float2(1, -1), 0.52), sdTri((p - float2(0.267, 0)).yx * float2(1, 1), 0.145));   // triangle, tab
                if (kind == 8) return sdSeg(p, float2(-0.72, 0), float2(0.72, 0)) - 0.12;                     // chain bond (kept inside the quad so halos never clip)                                    // chain bond
                if (kind == 4)
                {
                    float w = sin(p.x * 6.0 + _Time.y * 3.0) * 0.05;
                    float t1 = sdSeg(p + float2(0, 0.1 + w), float2(-0.15, 0), float2(0.85, 0)) - 0.055;
                    float t2 = sdSeg(p + float2(0, -0.1 - w), float2(-0.15, 0), float2(0.8, 0)) - 0.055;
                    return smin(sdCircle(p + float2(0.45, 0), 0.3), min(t1, t2), 0.1);
                }
                if (kind == 5) return sdHex(p, 0.55);
                return abs(sdCircle(p, 0.4)) - 0.13;
            }

            // ── virus geometry (3D signed distances) ──
            float sdIcosa(float3 p, float r)
            {
                const float q = 2.61803398875;
                const float3 n1 = normalize(float3(q, 1, 0));
                const float3 n2 = float3(0.57735026919, 0.57735026919, 0.57735026919);
                p = abs(p / r);
                float a = dot(p, n1.xyz), b = dot(p, n1.zxy), c = dot(p, n1.yzx);
                float d = dot(p, n2) - n1.x;
                return max(max(max(a, b), c) - n1.x, d) * r;
            }
            float sdCap(float3 p, float3 a, float3 b, float r) { float3 pa = p - a, ba = b - a; float h = saturate(dot(pa, ba) / dot(ba, ba)); return length(pa - ba * h) - r; }
            float VirusSDF(float3 p, int vtype, float R, float spikeL, float dent, float sd, out float mat)
            {
                mat = 0.0;
                float body;
                if (vtype == 1) body = length(p) - R * 1.05;                         // an enveloped, roundish particle
                else body = sdIcosa(p, R);                                           // the faceted capsid
                body += dent * sin(p.x * 23.0 + sd * 40.0) * sin(p.y * 19.0) * sin(p.z * 21.0);
                if (spikeL <= 0.0) return body;
                float spikes = 9.0;
                const float G = 1.61803398875;
                if (vtype == 0)
                {
                    // a spike from each of the 12 vertices, ending in a knob
                    float3 v[12] = { float3(0, 1, G), float3(0, -1, G), float3(0, 1, -G), float3(0, -1, -G),
                                     float3(1, G, 0), float3(-1, G, 0), float3(1, -G, 0), float3(-1, -G, 0),
                                     float3(G, 0, 1), float3(-G, 0, 1), float3(G, 0, -1), float3(-G, 0, -1) };
                    [unroll] for (int k = 0; k < 12; k++)
                    {
                        float3 dir = normalize(v[k]);
                        float3 a = dir * R * 1.12, b = dir * (R * 1.12 + spikeL);
                        spikes = min(spikes, min(sdCap(p, a, b, 0.022), length(p - b) - 0.055));
                    }
                }
                else
                {
                    // a crown: many short club-shaped spikes spread evenly over the envelope
                    [unroll] for (int k = 0; k < 24; k++)
                    {
                        float y = 1.0 - (k + 0.5) / 12.0, rr = sqrt(saturate(1.0 - y * y)), ph = k * 2.399963;
                        float3 dir = float3(cos(ph) * rr, y, sin(ph) * rr);
                        float3 a = dir * R, b = dir * (R + spikeL);
                        spikes = min(spikes, min(sdCap(p, a, b, 0.018), length(p - b) - 0.045));
                    }
                }
                mat = spikes < body ? 1.0 : 0.0;
                return min(body, spikes);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv.xy; int kind = (int)round(i.uv.z);
                // refraction wobble: seen through moving water, shapes waver (world-space, so the whole field moves together)
                if (kind <= 6 || kind == 8)
                {
                    float2 w = i.wp; float t = _Time.y;
                    p += float2(sin(w.y * 2.3 + t * 1.7) + sin(w.x * 1.31 - t * 1.13), cos(w.x * 2.1 + t * 1.45) + sin(w.y * 1.73 + t * 0.97)) * 0.045;
                }
                if (kind >= 5 && kind <= 6)   // DEBRIS, as under phase contrast (clay, organics; lipids keep their head-and-tails icon): translucent, granular, a bright halo at the edge
                {
                    float sd = frac(i.color.r * 91.7 + i.color.g * 37.3 + i.color.b * 13.1) * 40.0;   // a stable per-particle seed
                    float ang = atan2(p.y, p.x), r = length(p);
                    float edge;
                    if (kind == 4)      edge = r - 0.55 - 0.04 * sin(ang * 3.0 + sd);                                   // a vesicle
                    else if (kind == 5) { float sec = 6.2831853 / (5.0 + floor(frac(sd) * 3.0)); float l = fmod(ang + 6.2831853 + sd, sec) - sec * 0.5;
                                          edge = r * cos(l) - 0.48 * (0.85 + 0.25 * frac(sin(floor((ang + sd) / sec) * 12.9) * 43758.5)); }   // a crystal shard
                    else                edge = r - 0.5 - 0.09 * sin(ang * 5.0 + sd) - 0.06 * sin(ang * 9.0 - sd * 1.7) - 0.04 * sin(ang * 14.0 + sd * 0.6);   // a clump
                    float inside = 1.0 - smoothstep(-0.03, 0.03, edge);
                    float halo = exp(-max(edge, 0.0) * 14.0) * smoothstep(-0.06, 0.02, edge);                   // the phase halo
                    if (inside + halo < 0.01) discard;
                    // the body: translucent, darker toward its rim, granular inside
                    float rim = smoothstep(-0.25, 0.0, edge);
                    float grain = frac(sin(dot(floor(p * 9.0 + sd), float2(12.9898, 78.233))) * 43758.5);
                    float specks = step(0.82, grain) * (1.0 - rim);
                    float3 body = i.color.rgb * (0.55 + 0.25 * (1.0 - rim)) + specks * 0.18;
                    if (kind == 4) body = lerp(i.color.rgb * 0.35, i.color.rgb * 0.9, smoothstep(0.08, 0.0, abs(edge + 0.06)));   // vesicles: a bright skin, clear inside
                    float a = inside * (kind == 4 ? 0.35 + 0.6 * smoothstep(0.1, 0.0, abs(edge + 0.06)) : 0.75 + 0.2 * rim);
                    float3 haloC = lerp(i.color.rgb, float3(1, 1, 1), 0.6);
                    float3 c = lerp(haloC, body, inside);
                    c *= lerp(0.55, 1.0, _Day) * i.p1.x;
                    return fixed4(c, max(a, halo * 0.55) * i.color.a);
                }
                if (kind == 7)   // soft disk: a floor shadow (black) or a soft glow/haze (any colour)
                {
                    float s = 1.0 - smoothstep(0.2, 1.0, length(p));
                    return fixed4(i.color.rgb * lerp(0.4, 1.0, _Day * 0.5 + 0.5), s * s * i.color.a);
                }
                if (kind == 10 || (kind >= 12 && kind <= 14))   // organic FOOD: each family follows its own rules, no two alike
                {
                    float s1 = i.p1.z, s2 = frac(s1 * 7.13 + 0.31), s3 = frac(s1 * 13.7 + 0.71), s4 = frac(s1 * 3.97 + 0.53);
                    float sd = s1 * 37.0, tt = _Time.y;
                    float dd, R = 0.55;
                    float2 q = p;
                    if (kind == 10)        // a droplet: oval, softly lobed, sometimes budding a satellite
                    {
                        q.x *= 1.0 + 0.35 * s2;
                        R = 0.5 + 0.1 * s3;
                        float a = atan2(q.y, q.x);
                        dd = length(q) - R - (0.02 + 0.05 * s4) * sin(a * (2.0 + floor(s2 * 3.0)) + sd + tt * 0.8);
                        if (s3 > 0.55) dd = smin(dd, length(p - float2(0.6, (s4 - 0.5) * 0.6)) - (0.12 + 0.08 * s2), 0.1);
                    }
                    else if (kind == 12)   // a cluster of 3–6 globules of different sizes
                    {
                        dd = 9.0;
                        float n = 3.0 + floor(s2 * 4.0);
                        [unroll] for (int k = 0; k < 6; k++)
                        {
                            float hk = frac(sin(k * 12.9 + sd) * 43758.5);
                            float a = k * 2.399 + sd, rr = (k == 0 ? 0.0 : 0.28 + 0.14 * s3);
                            float2 cc = float2(cos(a), sin(a)) * rr + 0.03 * float2(sin(tt * 0.9 + k), cos(tt * 0.7 + k * 1.3));
                            if (k < n) dd = smin(dd, length(p - cc) - (0.15 + 0.13 * hk), 0.08 + 0.08 * s4);
                        }
                    }
                    else if (kind == 13)   // a floc: ragged, fuzzy, its raggedness its own
                    {
                        float a = atan2(p.y, p.x);
                        float amp = 0.5 + s2;
                        float rim = 0.45 + amp * (0.08 * sin(a * (2.0 + floor(s3 * 3.0)) + sd) + 0.05 * sin(a * (6.0 + floor(s4 * 4.0)) - sd * 1.3 + tt * 0.4) + 0.03 * sin(a * 13.0 + sd * 2.1));
                        dd = length(p * float2(1.0, 1.0 + 0.3 * s4)) - rim;
                    }
                    else                   // a scrap of torn membrane: an arc, its length / curl / thickness varying
                    {
                        float a = atan2(p.y, p.x);
                        float arcR = 0.38 + 0.18 * s2 + 0.04 * sin(tt * 0.6 + sd);
                        float ring = abs(length(p) - arcR) - (0.05 + 0.07 * s3) * (0.7 + 0.3 * sin(a * 2.0 + sd));
                        float cut = (0.6 * s4 - 0.1) - cos(a - sd);
                        dd = max(ring, -cut * 0.5);
                    }
                    float soft = kind == 13 ? 0.09 : 0.04;
                    float ab = 1.0 - smoothstep(-soft, soft, dd);
                    if (ab < 0.003) discard;
                    float inner = saturate(-dd / 0.25);
                    float hl = exp(-dot(p - float2(-0.2, 0.22), p - float2(-0.2, 0.22)) / (0.012 + 0.012 * s3));
                    float3 c = i.color.rgb * lerp(kind == 10 ? 0.5 : 0.6, 1.05, inner) + hl * (kind == 13 ? 0.1 : kind == 10 ? 0.6 : 0.4);
                    if (kind == 13) c *= 0.85 + 0.3 * frac(sin(dot(floor(p * 9.0), float2(12.9, 78.2)) + sd) * 43758.5);   // fibrous
                    if (kind == 10) c += (frac(sin(dot(floor(p * 6.0), float2(4.1, 9.7)) + sd) * 43758.5) > 0.93) * 0.08;  // inclusions
                    c *= lerp(0.5, 1.0, _Day);
                    return fixed4(c, ab * lerp(kind == 10 ? 0.55 : 0.75, 0.92, inner) * i.color.a);
                }
                if (kind == 18)   // a VIRUS, ray-traced in 3D inside its quad (uv1: x squash, y soundness, z seed → type/rotation, w size)
                {
                    float coh = saturate(i.p1.y), sd = i.p1.z, tt = _Time.y;
                    int vtype = (int)floor(frac(sd * 7.31) * 3.0);            // 0 spiked icosahedron · 1 enveloped, crowned · 2 bare faceted capsid
                    // tumbling orientation
                    float ax = tt * (0.25 + 0.2 * frac(sd * 3.7)) + sd * 20.0, ay = tt * (0.18 + 0.15 * frac(sd * 5.3)) + sd * 11.0;
                    float3x3 rx = float3x3(1, 0, 0, 0, cos(ax), -sin(ax), 0, sin(ax), cos(ax));
                    float3x3 ry = float3x3(cos(ay), 0, sin(ay), 0, 1, 0, -sin(ay), 0, cos(ay));
                    float3x3 rot = mul(ry, rx);
                    float3 ro = mul(rot, float3(p.x / max(i.p1.x, 1.0), p.y, -3.0)), rd = mul(rot, float3(0, 0, 1));
                    float R = vtype == 2 ? 0.62 : 0.48;
                    float spikeL = vtype == 2 ? 0.0 : (vtype == 0 ? 0.36 : 0.26);
                    float dent = (1.0 - coh) * 0.05;
                    float t = 0.0, hit = 0.0, mat = 0.0;
                    [loop] for (int k = 0; k < 56; k++)
                    {
                        float3 q = ro + rd * t;
                        float d = VirusSDF(q, vtype, R, spikeL, dent, sd, mat);
                        if (d < 0.002) { hit = 1.0; break; }
                        t += d;
                        if (t > 6.0) break;
                    }
                    if (hit < 0.5) discard;
                    float3 q = ro + rd * t;
                    const float e = 0.004; float m2;
                    float3 n = normalize(float3(VirusSDF(q + float3(e, 0, 0), vtype, R, spikeL, dent, sd, m2) - VirusSDF(q - float3(e, 0, 0), vtype, R, spikeL, dent, sd, m2),
                                                VirusSDF(q + float3(0, e, 0), vtype, R, spikeL, dent, sd, m2) - VirusSDF(q - float3(0, e, 0), vtype, R, spikeL, dent, sd, m2),
                                                VirusSDF(q + float3(0, 0, e), vtype, R, spikeL, dent, sd, m2) - VirusSDF(q - float3(0, 0, e), vtype, R, spikeL, dent, sd, m2)));
                    // light in view space: from the upper left, a little toward the viewer
                    float3 Lw = mul(rot, normalize(float3(-0.5, 0.6, -0.65)));
                    float3 Vw = -rd;
                    float lam = saturate(dot(n, Lw));
                    float spec = pow(saturate(dot(reflect(-Lw, n), Vw)), 24.0);
                    float rim = pow(1.0 - saturate(dot(n, Vw)), 2.5);
                    float3 baseC = i.color.rgb * (mat > 0.5 ? 1.15 : 1.0);                   // spikes a touch lighter
                    float3 c = baseC * (0.28 + 0.75 * lam) + spec * 0.35 + rim * lerp(baseC, float3(1, 1, 1), 0.5) * 0.45;
                    c *= lerp(0.55, 1.0, _Day);
                    return fixed4(c, i.color.a);
                }
                if (kind == 17)   // a VIRUS CAPSID: a faceted geometric shell (seen face-on: a hexagon of triangular facets) with spikes
                {
                    float R = 0.6, apo = R * 0.8660254;
                    float ang = atan2(p.y, p.x), r = length(p);
                    float secA = 1.0471976;
                    float local = fmod(ang + 6.2831853, secA) - secA * 0.5;
                    float hexD = r * cos(local) - apo;                                    // regular hexagon, vertices at k·60°
                    float spikes = 9.0;
                    [unroll] for (int k = 0; k < 6; k++)
                    {
                        float a2 = k * secA; float2 dir = float2(cos(a2), sin(a2));
                        spikes = min(spikes, sdSeg(p, dir * R, dir * 0.86) - 0.03);
                        spikes = min(spikes, length(p - dir * 0.88) - 0.07);
                    }
                    float d = min(hexD, spikes);
                    float ab = 1.0 - smoothstep(-0.02, 0.02, d);
                    if (ab < 0.003) discard;
                    float sec = floor((ang + 6.2831853) / secA);
                    float3 fn = normalize(float3(cos((sec + 0.5) * secA) * 0.6, sin((sec + 0.5) * secA) * 0.6, 1.0));   // each facet tilted outward
                    float lam = saturate(dot(fn, normalize(float3(-0.45, 0.55, 0.7))));
                    float edge = smoothstep(0.035, 0.0, abs(local) * r - 0.0) * step(0.08, r) * step(hexD, 0.0);   // ridges between facets
                    float rim = smoothstep(-0.05, 0.0, hexD) * step(hexD, 0.0);
                    float3 c = i.color.rgb * (0.45 + 0.75 * lam) + (edge + rim) * 0.35;
                    if (spikes < hexD) c = i.color.rgb * 1.15 + 0.1;                         // the spikes catch the light
                    float window = 1.0 - smoothstep(0.18, 0.3, r);                           // a clearer middle: the genome inside shows through
                    c *= lerp(0.55, 1.0, _Day);
                    return fixed4(c, ab * i.color.a * lerp(0.92, 0.45, window));
                }
                if (kind == 15)   // a GENE: a folded strand whose shape IS its sequence — each base adds a fold
                {
                    // base → how the strand turns and how it looks there
                    //   0: a fat bulb, turning left · 1: slim, gentle right · 2: beaded, nearly straight · 3: twisted, hard right
                    float code = floor(i.p1.z * 256.0 + 0.01), q = i.p1.w, tt = _Time.y;
                    int bs[4] = { (int)fmod(code, 4.0), (int)fmod(floor(code / 4.0), 4.0), (int)fmod(floor(code / 16.0), 4.0), (int)fmod(floor(code / 64.0), 4.0) };
                    const float turnT[4] = { 1.35, -0.8, 0.25, -1.9 };
                    const float radT[4] = { 0.11, 0.065, 0.08, 0.085 };
                    float2 pts[9]; float2 dir = float2(1, 0), pos = float2(0, 0); pts[0] = pos; float2 cen = pos;
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        int b = bs[k / 2];
                        // a well-made gene folds cleanly; a poor one trembles and tangles
                        float turn = turnT[b] * 0.5 + (1.0 - q) * sin(k * 3.7 + code + tt * 2.3) * 0.8 + 0.06 * sin(tt * 0.8 + k);
                        float cs = cos(turn), sn = sin(turn);
                        dir = float2(dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs);
                        pos += dir * 0.17; pts[k + 1] = pos; cen += pos;
                    }
                    cen /= 9.0;
                    float best = 9, along = 0; int seg = 0; float2 perp = 0;
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        float2 a = pts[k] - cen, b2 = pts[k + 1] - cen, pa = p - a, ba = b2 - a;
                        float hh = saturate(dot(pa, ba) / dot(ba, ba));
                        float2 dv = pa - ba * hh; float d = length(dv);
                        if (d < best) { best = d; along = (k + hh) / 8.0; seg = k; perp = dv; }
                    }
                    int bb = bs[seg / 2];
                    float R = radT[bb] * max(i.p1.x, 0.5);
                    if (bb == 2) R *= 0.75 + 0.35 * abs(sin(along * 60.0));               // beaded
                    if (bb == 0) R *= 1.0 + 0.3 * sin(frac(along * 4.0) * 3.14159);       // bulbous
                    if (bb == 3) R *= 0.8 + 0.25 * sin(along * 90.0 + tt * 2.0);          // twisted
                    R *= 1.0 + 0.08 * sin(tt * 2.5 + along * 6.0) * i.p1.y;               // working genes breathe
                    float ab = 1.0 - smoothstep(R - 0.02, R + 0.01, best);
                    if (ab < 0.003) discard;
                    float rr = saturate(best / R);
                    float3 n = normalize(float3(perp / max(best, 1e-4) * rr, sqrt(saturate(1.0 - rr * rr))));
                    float3 L = normalize(float3(-0.45, 0.55, 0.7));
                    float lam = saturate(dot(n, L));
                    float spec = pow(saturate(dot(reflect(-L, n), float3(0, 0, 1))), 24.0);
                    float rim = pow(1.0 - n.z, 2.0);
                    float3 c = i.color.rgb * (0.4 + 0.7 * lam) + spec * 0.45 + rim * i.color.rgb * 0.3;
                    c += i.p1.y * i.color.rgb * 0.25 * (0.5 + 0.5 * sin(tt * 4.0 - along * 12.0));   // activity runs along it
                    c *= lerp(0.55, 1.0, _Day);
                    return fixed4(c, ab * i.color.a);
                }
                if (kind == 16)   // a NUCLEOTIDE: a small 3D bead whose shape is its base (the same shapes genes fold with)
                {
                    int bt = (int)floor(i.p1.z * 4.0);
                    float d = bt == 0 ? length(p) - 0.55
                            : bt == 1 ? sdSeg(p, float2(-0.45, 0), float2(0.45, 0)) - 0.24
                            : bt == 2 ? abs(length(p) - 0.4) - 0.16
                            : smin(length(p - float2(-0.25, 0.14)) - 0.3, length(p - float2(0.3, -0.12)) - 0.24, 0.12);
                    float ab = 1.0 - smoothstep(-0.03, 0.03, d);
                    if (ab < 0.003) discard;
                    const float e = 0.02;
                    float dx = (bt == 0 ? length(p + float2(e, 0)) - 0.55 : bt == 1 ? sdSeg(p + float2(e, 0), float2(-0.45, 0), float2(0.45, 0)) - 0.24 : bt == 2 ? abs(length(p + float2(e, 0)) - 0.4) - 0.16 : smin(length(p + float2(e, 0) - float2(-0.25, 0.14)) - 0.3, length(p + float2(e, 0) - float2(0.3, -0.12)) - 0.24, 0.12)) - d;
                    float dy = (bt == 0 ? length(p + float2(0, e)) - 0.55 : bt == 1 ? sdSeg(p + float2(0, e), float2(-0.45, 0), float2(0.45, 0)) - 0.24 : bt == 2 ? abs(length(p + float2(0, e)) - 0.4) - 0.16 : smin(length(p + float2(0, e) - float2(-0.25, 0.14)) - 0.3, length(p + float2(0, e) - float2(0.3, -0.12)) - 0.24, 0.12)) - d;
                    float depthIn = saturate(-d / 0.25);
                    float3 n = normalize(float3(dx / e, dy / e, 0.0) * (1.0 - depthIn) + float3(0, 0, max(depthIn, 0.15)));
                    float3 L = normalize(float3(-0.45, 0.55, 0.7));
                    float lam = saturate(dot(n, L)), spec = pow(saturate(dot(reflect(-L, n), float3(0, 0, 1))), 20.0);
                    float3 c = i.color.rgb * (0.45 + 0.7 * lam) + spec * 0.4;
                    c *= lerp(0.55, 1.0, _Day);
                    return fixed4(c, ab * i.color.a);
                }
                if (kind == 11)   // an RNA filament segment: a thin twisting ribbon with a lit spine
                {
                    float tw = sin(p.x * 7.0 + _Time.y * 0.8 + i.p1.z * 6.0);
                    float halfW = 0.16 * (0.55 + 0.45 * abs(tw));                      // the ribbon turns as it twists
                    float dd = sdSeg(p, float2(-0.85, 0), float2(0.85, 0)) - halfW;
                    float ab = 1.0 - smoothstep(-0.03, 0.03, dd);
                    if (ab < 0.003) discard;
                    float spine = exp(-p.y * p.y / (halfW * halfW * 0.25));
                    float3 c = i.color.rgb * (0.55 + 0.6 * spine * (0.6 + 0.4 * tw));
                    c += i.p1.y * float3(1.0, 0.9, 0.6) * 0.35;
                    c *= lerp(0.5, 1.0, _Day);
                    return fixed4(c, ab * 0.85 * i.color.a);
                }
                if (kind == 9)   // seabed mineral plate (p1.z = height/width aspect, p1.w = surface type)
                {
                    float asp = i.p1.z; int surf = (int)round(i.p1.w);
                    float2 q = abs(p) - float2(0.94, 0.94 * asp) + 0.07;
                    float db = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.07;
                    float aab = fwidth(db) * 1.5;
                    float ab = 1.0 - smoothstep(-aab, aab, db);
                    if (ab < 0.003) discard;
                    float2 w = p * 18.0;
                    float n1 = frac(sin(dot(floor(w), float2(127.1, 311.7))) * 43758.5453);
                    float n2 = frac(sin(dot(floor(w * 2.7), float2(269.5, 183.3))) * 43758.5453);
                    float tex;
                    if (surf == 0)      tex = 0.9 + 0.1 * n2;                                       // clay: fine, smooth
                    else if (surf == 2) tex = 0.75 + 0.35 * step(0.55, n1) * n2;                     // basalt: pitted, vesicular
                    else if (surf == 3) tex = 0.8 + 0.3 * step(0.5, frac((p.x + p.y * 0.2) * 9.0)); // pyrite: striated crystal faces
                    else                tex = 0.85 + 0.15 * n1;                                     // carbonate: grainy
                    float edge = 1.0 - smoothstep(0.0, aab * 4.0, abs(db) - 0.005);
                    float3 pc = i.color.rgb * tex * (0.9 + 0.15 * p.y) + edge * 0.12;
                    pc *= lerp(0.4, 1.0, _Day);
                    return fixed4(pc, ab * i.color.a);
                }
                float d = Shape(p, kind);
                float blur = saturate(i.p1.z);                                   // out of the focal plane → soft
                float aa = fwidth(d) * 1.2 + blur * 0.22;
                float a = 1.0 - smoothstep(-aa, aa, d);
                // phase-contrast halo: a faint bright ring just outside the specimen (fades when out of focus)
                float haloK = _CellIllum > 0.5 && _CellIllum < 1.5 ? 0.75 : _CellIllum > 1.5 ? 0.5 : 0.28;   // darkfield / phase light up the edges
                float halo = exp(-max(d, 0.0) / (0.07 + blur * 0.1)) * (1.0 - a) * haloK * (1.0 - blur * 0.7);
                if (a + halo < 0.003) discard;
                if (kind <= 6)
                {
                    // completely FLAT and glowing: one self-lit colour, no outline/shading, plus a soft halo of the same colour
                    float3 fill = i.color.rgb * 1.15 + 0.05;
                    float3 ic = fill * i.p1.x + i.p1.y * float3(1.0, 0.9, 0.6) * 0.45;
                    float glowA = exp(-max(d, 0.0) / (0.1 + blur * 0.12)) * (1.0 - a) * 0.45 * i.color.a;
                    float ia = a * lerp(1.0, 0.7, blur) * i.color.a;
                    float outA = saturate(ia + glowA);
                    float3 oc = (ic * ia + fill * glowA) / max(outA, 1e-4);
                    oc *= lerp(0.55, 1.0, _Day);                                         // they glow: dimmer at night, never dark
                    return fixed4(oc, outA);
                }

                // dome: height from the inside distance → a normal → lit like a little bead of matter
                float h = sqrt(saturate(-d / 0.22));
                // outward direction = the distance field's SCREEN-space gradient, so the light stays fixed while molecules spin
                float2 grad = float2(ddx(d), ddy(d));
                #if UNITY_UV_STARTS_AT_TOP
                grad.y = -grad.y;
                #endif
                float2 outw = grad / max(length(grad), 1e-6);
                float3 n = normalize(float3(outw * (1.0 - h) * 1.6, h + 0.25));
                float3 L = normalize(float3(-0.45, 0.55, 0.75));
                float diff = 0.45 + 0.55 * saturate(dot(n, L));
                float spec = pow(saturate(dot(reflect(-L, n), float3(0, 0, 1))), 24.0) * 0.6;
                float3 base = i.color.rgb;
                float depthIn = saturate(-d / 0.3);                                   // 0 at the edge → 1 deep inside
                // a richer two-tone body: lighter, slightly desaturated core → deeper, more saturated rim
                float lum = dot(base, float3(0.3, 0.59, 0.11));
                float3 core = lerp(base, lum.xxx, 0.25) * 1.25 + 0.06;
                float3 deep = base * base * 1.1;
                base = lerp(deep, core, smoothstep(0.0, 0.9, depthIn));
                float detail = 0.0;                                                   // fine inner structure (lines/dots), lighter
                float ink = 1.0 - blur;
                if (kind <= 3)
                {
                    // nucleobases: an aromatic ring inside (hexagon on the round pair, pentagon on the triangular pair)
                    float nside = kind < 2 ? 6.0 : 5.0;
                    float2 c0 = kind < 2 ? float2(-0.08, 0.0) : float2(-0.12, 0.0);
                    float2 q = p - c0;
                    float an = atan2(q.y, q.x), seg = 6.2831853 / nside;
                    float polyR = 0.2 * cos(seg * 0.5) / cos(fmod(an + 6.2831853 + seg * 0.5, seg) - seg * 0.5);
                    detail = 1.0 - smoothstep(0.0, 0.03 + aa, abs(length(q) - polyR));
                    // atoms at the ring's corners
                    float ca = round(an / seg) * seg;
                    float2 vtx = c0 + float2(cos(ca), sin(ca)) * 0.2;
                    detail = max(detail, 1.0 - smoothstep(0.035, 0.06 + aa, length(p - vtx)));
                }
                else if (kind == 4)
                {
                    // lipid: a pearly head; translucent tails fading toward their tips
                    float head = smoothstep(-0.2, -0.45, p.x);
                    base = lerp(base * 0.9, lerp(base, float3(1, 1, 1), 0.35) * 1.15, head);
                    a *= lerp(1.0, 0.55, saturate((p.x + 0.1) / 0.9));
                }
                else if (kind == 5)
                {
                    // clay: translucent stacked sheets with an iridescent edge
                    base *= 0.82 + 0.18 * step(0.5, frac((p.y + p.x * 0.3) * 6.0));
                    float3 irid = 0.5 + 0.5 * cos(6.2831853 * (p.x * 0.6 + p.y * 0.4 + float3(0.0, 0.33, 0.67)));
                    base = lerp(base, irid, (1.0 - depthIn) * 0.35);
                }
                else if (kind == 6)
                {
                    // other organic: a sugar ring with little side groups
                    float an = atan2(p.y, p.x);
                    float bumps = pow(saturate(cos(an * 5.0)), 6.0);
                    detail = bumps * (1.0 - smoothstep(0.0, 0.05, abs(length(p) - 0.58)));
                }
                // translucent specimen: glassy, lighter body, a darker refracting rim, a small specular glint
                float3 col = base * (0.75 + 0.35 * diff) + spec * (1.0 - blur);
                col = lerp(col, lerp(col, float3(1, 1, 1), 0.55), detail * 0.6 * ink);
                col += base * depthIn * 0.12;                                          // a faint inner glow
                // flOw: a small luminous core in each specimen
                if (kind <= 3 || kind == 6) col += lerp(base, float3(1, 1, 1), 0.5) * pow(saturate(1.0 - length(p - float2(-0.08, 0.0)) / 0.22), 2.0) * 0.55 * ink;
                // DIC: emboss — the edge facing the shear direction is lit, the opposite edge shadowed; colours muted
                if (_CellIllum > 2.5)
                {
                    float rel = dot(outw, normalize(float2(0.707, 0.707)));
                    float edgeBand = saturate(1.0 - depthIn * 2.5);
                    col *= 1.0 + rel * edgeBand * 0.9 * ink;                              // full colour, embossed edges
                }
                float rim = 1.0 - smoothstep(0.0, aa * 2.5, abs(d) - 0.01);
                col = lerp(col, col * 0.5, rim * 0.55 * (1.0 - blur));
                if (_CellIllum > 0.5 && _CellIllum < 1.5) col += base * rim * 0.9 * (1.0 - blur);           // darkfield: edges glow
                float body = lerp(0.62, 0.95, rim) * lerp(1.0, 0.7, blur);        // see-through middle, solid edge
                col *= i.p1.x;
                col += i.p1.y * float3(1.0, 0.9, 0.6) * 0.45;
                float3 haloC = lerp(base, float3(1, 1, 1), 0.6);
                float outA = a * body + halo;
                col = (col * a * body + haloC * halo) / max(outA, 1e-4);
                col *= lerp(0.35, 1.0, _Day);
                return fixed4(col, saturate(outA) * i.color.a);
            }
            ENDCG
        }
    }
}
