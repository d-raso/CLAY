Shader "CLAY/CellStage/Feature"
{
    // Biome STRUCTURES on the pool floor and floating overhead (Stage0/Features.cs). One batch; uv0.z = kind.
    //   0 boulder · 1 basalt columns · 2 chemical garden (mineral tubes) · 3 carbonate lace spire · 4 mud volcano
    //   5 sinter rimstone terraces · 6 pebble scatter · 7 sand ripples · 8 pumice raft · 9 foam raft
    //   10 sulfur crystals · 11 trapped gas bubbles
    // uv1 = (seed, overhead 0/1, softness, alpha) · uv2 = second colour (rgb) · colour = main colour
    // Shaded from the shape's own height field (lit like the soup floor), tinted by the water above it.
    Properties
    {
        _Height ("Height", 2D) = "black" {}
        _Day ("Daylight", Float) = 1
    }
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
            sampler2D _Height;
            float _Day, _Tide; float4 _ArenaSize, _WaterDeep, _WaterShallow;
            struct appdata { float4 vertex : POSITION; float3 uv0 : TEXCOORD0; float4 p1 : TEXCOORD1; float4 p2 : TEXCOORD2; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; float4 p1 : TEXCOORD1; float4 p2 : TEXCOORD2; float4 color : COLOR; float2 wp : TEXCOORD3; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv0; o.p1 = v.p1; o.p2 = v.p2; o.color = v.color; o.wp = mul(unity_ObjectToWorld, v.vertex).xy; return o; }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float2 hash22(float2 p) { float n = hash21(p); return float2(n, hash21(p + n + 17.0)); }
            float3 mod289(float3 x) { return x - floor(x / 289.0) * 289.0; }
            float3 permute(float3 x) { return mod289((x * 34.0 + 1.0) * x); }
            float snoise(float2 v)
            {
                const float4 C = float4(0.211324865, 0.366025404, -0.577350269, 0.024390244);
                float2 i = floor(v + dot(v, C.yy)); float2 x0 = v - i + dot(i, C.xx);
                float2 i1 = x0.x > x0.y ? float2(1, 0) : float2(0, 1);
                float4 x12 = x0.xyxy + C.xxzz; x12.xy -= i1;
                i = i - floor(i / 289.0) * 289.0;
                float3 p = permute(permute(i.y + float3(0, i1.y, 1)) + i.x + float3(0, i1.x, 1));
                float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0); m = m * m; m = m * m;
                float3 x = 2.0 * frac(p * C.www) - 1.0; float3 h = abs(x) - 0.5; float3 ox = floor(x + 0.5); float3 a0 = x - ox;
                m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);
                float3 g; g.x = a0.x * x0.x + h.x * x0.y; g.yz = a0.yz * x12.xz + h.yz * x12.yw;
                return 130.0 * dot(m, g);
            }
            float fbm(float2 p) { return snoise(p) * 0.5 + snoise(p * 2.07 + 3.1) * 0.25 + snoise(p * 4.13 - 1.7) * 0.125; }
            // worley: (nearest distance, second distance, id hash)
            float3 worley(float2 p)
            {
                float2 c = floor(p); float d1 = 9, d2 = 9, id = 0;
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                {
                    float2 g = c + float2(x, y); float2 o = g + hash22(g);
                    float d = length(p - o);
                    if (d < d1) { d2 = d1; d1 = d; id = hash21(g); } else if (d < d2) d2 = d;
                }
                return float3(d1, d2, id);
            }
            float sdSeg(float2 p, float2 a, float2 b) { float2 pa = p - a, ba = b - a; float h = saturate(dot(pa, ba) / dot(ba, ba)); return length(pa - ba * h); }

            // the height field of each structure (≤ 0 outside it); `m` = material mix 0..1 (main → second colour)
            float Feat(int kind, float2 p, float s, out float m)
            {
                m = 0;
                float r = length(p), a = atan2(p.y, p.x);
                float edge = r - (0.72 + 0.12 * snoise(float2(cos(a), sin(a)) * 1.3 + s) + 0.05 * snoise(p * 4.0 + s));
                if (kind == 12 || kind == 13)   // vent chimneys: a mound crowded with lumpy mineral stacks, each with an open mouth
                {
                    float h = 0, bands = 0;
                    [unroll] for (int k = 0; k < 6; k++)
                    {
                        float hk = frac(sin(k * 12.9898 + s * 7.0) * 43758.5);
                        float2 c = k == 0 ? float2(0, 0) : float2(cos(k * 2.399 + s), sin(k * 2.399 + s)) * (0.3 + 0.25 * hk);
                        float rk = k == 0 ? 0.42 : 0.16 + 0.14 * hk;
                        float e = length(p - c) - rk * (1.0 + 0.2 * snoise((p - c) * 4.0 + s + k));
                        float stack = saturate(-e / rk);
                        h = max(h, sqrt(stack) * (k == 0 ? 1.0 : 0.6 + 0.35 * hk));
                    }
                    float mound = saturate(1.0 - r / (0.92 + 0.08 * snoise(p * 3.0 + s)));
                    h = max(h, mound * mound * 0.35);
                    float crust = fbm(p * 7.0 + s);
                    bands = kind == 13 ? smoothstep(0.6, 0.9, frac(h * 7.0 + crust * 0.6)) : 0.0;   // carbonate: growth terraces
                    m = kind == 12 ? saturate(crust * 1.6 + 0.2) * (0.3 + 0.7 * (1.0 - h)) : bands * 0.6 + saturate(crust) * 0.3;
                    if (h <= 0.001) return -(max(r, 0.96) - 0.95) * 3.0 - 0.02;   // (no hard radius cut: the mound itself fades out)
                    // cauliflower clumps at two scales (mineral growth), strongest on the stacks
                    float3 w1 = worley(p * 9.0 + s), w2 = worley(p * 21.0 + s * 1.7);
                    h += ((1.0 - saturate(w1.x * 1.6)) * 0.12 + (1.0 - saturate(w2.x * 1.6)) * 0.06) * saturate(h * 3.0);
                    return h + crust * 0.03 * saturate(h * 10.0);
                }
                if (kind == 0)        // boulder: lumpy dome, mineral grain, lichen-free (nothing lives yet)
                {
                    float e2 = edge + 0.18 * fbm(p * 2.3 + s * 1.7);                      // craggy outline
                    float3 fw = worley(p * 3.2 + s);
                    float facets = fw.x * 0.22 + smoothstep(0.0, 0.05, fw.y - fw.x) * 0.06;   // flat broken faces, sharp ridges
                    float h = pow(saturate(-e2 * 1.6), 0.7) * 0.55 - facets * saturate(-e2 * 3.0) + fbm(p * 9.0 + s) * 0.04;
                    m = saturate(fbm(p * 2.5 - s) * 1.3 + 0.35) * 0.7 + fw.z * 0.15;
                    return e2 < 0 ? h : -e2 * 3.0;
                }
                if (kind == 1)        // basalt columns: hexagonal tops at different heights inside a ragged cluster
                {
                    float3 w = worley(p * 4.2 + s);
                    float top = 0.35 + 0.65 * w.z;
                    float joint = smoothstep(0.02, 0.09, w.y - w.x);
                    m = w.z * 0.5 + (1.0 - joint) * 0.5;
                    return edge < 0 ? top * (0.4 + 0.6 * joint) : -edge * 3.0;
                }
                if (kind == 2)        // chemical garden: hollow mineral tubes grown outward and upward, mouths open
                {
                    float d = 9; float tip = 9;
                    float2 pw = p + float2(snoise(p * 2.5 + s), snoise(p * 2.5 - s)) * 0.06;     // nothing perfectly straight
                    for (int k = 0; k < 6; k++)
                    {
                        float hk = frac(sin(k * 7.7 + s * 13.0) * 4375.5);
                        float ka = k * 2.399 + s * 6.0, len = 0.4 + 0.45 * hk;
                        float2 dir = float2(cos(ka), sin(ka)), sideV = float2(-dir.y, dir.x);
                        float2 e1 = dir * len * 0.35 + sideV * 0.12 * sin(k * 1.3 + s);
                        float2 e2 = dir * len * 0.7 + sideV * 0.2 * sin(k * 2.1 + s * 2.0);
                        float2 e3 = dir * len + sideV * 0.14 * sin(k * 3.7 + s);
                        float along = saturate(length(pw) / len);
                        float thick = (0.085 - 0.035 * along) * (0.8 + 0.4 * sin(length(pw) * 30.0 + k));  // knobbly growth rings
                        float sd = min(min(sdSeg(pw, 0, e1), sdSeg(pw, e1, e2)), sdSeg(pw, e2, e3)) - thick;
                        // a side branch off the middle
                        float2 bdir = normalize(dir + sideV * (hk > 0.5 ? 0.9 : -0.9));
                        sd = min(sd, sdSeg(pw, e1, e1 + bdir * len * 0.35) - thick * 0.7);
                        d = min(d, sd);
                        tip = min(tip, min(length(pw - e3), length(pw - (e1 + bdir * len * 0.35))));
                    }
                    d = min(d, r - 0.16);
                    float h = saturate(-d * 6.0) * (0.6 + 0.4 * (1.0 - r));
                    float mouth = smoothstep(0.07, 0.02, tip);
                    m = saturate(r * 1.4) * 0.7 + mouth * 0.3;
                    return d < 0 ? h * (1.0 - mouth * 0.9) : -d * 4.0;
                }
                if (kind == 3)        // carbonate lace spire: a porous white chimney, honeycombed by worley holes
                {
                    float3 w = worley(p * 7.0 + s);
                    float hole = smoothstep(0.32, 0.18, w.x);
                    float dome = saturate(-edge * 1.6);
                    m = hole;
                    return edge < 0 ? (sqrt(dome) * (1.0 - hole * 0.75)) : -edge * 3.0;
                }
                if (kind == 4)        // mud volcano: a gullied cone with a wet crater
                {
                    float cone = saturate(1.0 - r / 0.85) + 0.04 * sin(a * 9.0 + s * 5.0 + snoise(p * 3.0) * 2.0) * saturate(r);
                    float crater = smoothstep(0.16, 0.08, r);
                    m = crater + saturate(snoise(p * 5.0 + s) * 0.5) * 0.3;
                    return r < 0.85 ? cone * (1.0 - crater * 0.7) : (0.85 - r) * 3.0;
                }
                if (kind == 5)        // sinter rimstone: nested scalloped rims holding shallow pools
                {
                    float rr = r * 5.0 + snoise(p * 3.0 + s) * 0.6 + 0.12 * sin(a * 11.0 + s);
                    float rim = smoothstep(0.75, 0.95, frac(rr));
                    m = 1.0 - rim;
                    float h = 0.25 + rim * 0.35 - r * 0.2;
                    return edge < 0 ? h : -edge * 3.0;
                }
                if (kind == 6)        // pebbles: separate rounded stones of many tones
                {
                    float2 q = float2(p.x * 4.5, p.y * 6.5) + float2(snoise(p * 2.0 + s), snoise(p * 2.0 - s)) * 0.6;
                    float3 w = worley(q + s);
                    float stone = saturate(1.0 - w.x / (0.3 + 0.18 * w.z));
                    stone = stone * stone * (3.0 - 2.0 * stone);
                    m = w.z * 0.8 + fbm(p * 12.0 + w.z * 9.0) * 0.4;
                    float keep = smoothstep(0.3, 0.5, w.z) * saturate(-edge * 3.0);
                    return stone * keep * 0.28 - 0.02;
                }
                if (kind == 7)        // sand ripple marks: asymmetric crests, bending and forking
                {
                    float2 q = p + float2(snoise(p * 1.2 + s), snoise(p * 1.2 - s)) * 0.25;
                    float ph = frac(q.x * 6.0 + snoise(q * 2.0 + s) * 0.8);
                    float crest = ph < 0.7 ? ph / 0.7 : (1.0 - ph) / 0.3;            // gentle stoss, steep lee
                    m = crest;
                    float fade = saturate(-edge * 2.5);
                    return fade > 0 ? 0.08 + crest * 0.18 * fade : -0.1;
                }
                if (kind == 8)        // pumice raft: frothy grey stone floating overhead
                {
                    float3 w = worley(p * 9.0 + s);
                    float pore = smoothstep(0.25, 0.1, w.x);
                    m = pore;
                    return edge < 0 ? saturate(-edge * 2.0) * (1.0 - pore * 0.6) : -edge * 3.0;
                }
                if (kind == 9)        // foam: a raft of soap-film bubbles
                {
                    float3 w = worley(p * 6.0 + s + float2(_Time.y * 0.02, 0));
                    float film = smoothstep(0.08, 0.0, w.y - w.x);
                    m = film;
                    return edge < 0 ? (0.2 + film * 0.5) * saturate(-edge * 3.0) : -edge * 3.0;
                }
                if (kind == 10)       // sulfur crystals: a cluster of sharp yellow blades
                {
                    float e2 = edge + 0.25 * fbm(p * 3.0 + s);
                    float3 w = worley(p * 11.0 + s);
                    float facet = (1.0 - w.x) * 0.12 + w.z * 0.06;                        // tiny flat crystal faces
                    float inner = saturate(-e2 * 2.5);
                    m = w.z * inner;                                                      // bright crystals in the middle, powder at the rim
                    return e2 < 0 ? inner * 0.22 + facet * inner : -e2 * 3.0;
                }
                // 11: gas bubbles trapped against the floor — silvery domes
                float3 w = worley(p * 4.0 + s);
                float rb = 0.12 + 0.12 * w.z;
                float b = saturate(1.0 - w.x / rb) * smoothstep(0.55, 0.7, w.z) * saturate(-edge * 4.0);
                m = 1.0;
                return b * 0.35 - 0.02;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv.xy; int kind = (int)round(i.uv.z);
                float seed = i.p1.x, overhead = i.p1.y, soft = i.p1.z;
                // seen through moving water: everything on the floor wavers a little
                float t = _Time.y;
                if (overhead < 0.5) p += float2(sin(i.wp.y * 1.3 + t * 1.1), cos(i.wp.x * 1.2 + t * 0.9)) * 0.0015;
                float m, mx, my;
                float h = Feat(kind, p, seed, m);
                const float e = 0.012;
                float hx = Feat(kind, p + float2(e, 0), seed, mx), hy = Feat(kind, p + float2(0, e), seed, my);
                float aw = 0.02 + soft;
                float alpha = smoothstep(-aw, aw, h);
                if (kind == 6 || kind == 7) alpha = smoothstep(0.0, 0.06, h);           // buried: blends into the floor
                if (kind == 0 || kind == 1 || kind == 2 || kind == 4 || kind == 12 || kind == 13) alpha = min(alpha, smoothstep(0.0, 0.26, h + (snoise(p * 4.0 + seed) - 0.3) * 0.08));   // a wide, ragged skirt: the mass grows out of the ground
                if (kind == 11) alpha = smoothstep(0.0, 0.05, h) * 0.6;
                if (alpha < 0.004) discard;
                // normal from the height field; lit from the soup floor's light
                float ns = kind == 12 || kind == 13 ? 0.15 : 0.085;
                float3 n = normalize(float3(-(hx - h) / e * ns, -(hy - h) / e * ns, 1.0));
                float3 L = normalize(float3(-0.45, 0.55, 0.7));
                float lam = saturate(dot(n, L));
                float spec = pow(saturate(dot(reflect(-L, n), float3(0, 0, 1))), kind == 11 ? 60.0 : 18.0);
                float3 col = lerp(i.color.rgb, i.p2.rgb, saturate(m));
                col *= 1.0 + snoise(p * 9.0 + seed) * 0.06;
                float gloss = kind == 11 ? 0.9 : kind == 9 ? 0.4 : kind == 2 ? 0.25 : kind == 10 ? 0.15 : 0.05;
                float3 c = col * (0.45 + 0.75 * lam) + spec * gloss;
                if (kind == 11) { float rim = smoothstep(0.05, 0.3, h) * (1.0 - smoothstep(0.25, 0.33, h)); c = lerp(c * 0.6, float3(0.9, 0.95, 1.0), rim * 0.7); }                          // a silver gas–water mirror
                c *= 0.72 + 0.4 * saturate(h);                                                      // tops catch light, bases sit in shadow
                // edges brighten where the surface turns away (as under an electron microscope): a strong sense of form
                if (kind == 12 || kind == 13 || kind == 0 || kind == 1 || kind == 4)
                    c += pow(saturate((1.0 - n.z) * 2.2), 1.2) * lerp(col, float3(1, 1, 1), 0.5) * 0.9;
                if (overhead < 0.5)
                {
                    float2 uv = i.wp / _ArenaSize.xy;
                    float depth = _Tide - tex2D(_Height, uv).r - h * 0.015;
                    float wet = smoothstep(-0.004, 0.008, depth);                                    // a soft waterline, not a cut
                    float dW = saturate(depth * 5.0);
                    c *= lerp(1.0, 0.85, wet);                                                       // wet rock is darker
                    c = lerp(c, c * _WaterShallow.rgb * 1.3, wet * 0.6);                             // seen through the same water as the floor
                    c = lerp(c, _WaterDeep.rgb, dW * 0.55);
                    // low parts sink into the water's own colour: the base of a mass is lost in it, like the floor around
                    float3 waterC = lerp(_WaterShallow.rgb, _WaterDeep.rgb, 0.45) * 0.85;
                    c = lerp(c, waterC, (1.0 - saturate(h * 3.5)) * 0.55 * wet);
                    // caustic light dancing over submerged surfaces (the same water that lights the floor)
                    float cau = snoise(i.wp * 0.9 + float2(t * 0.3, -t * 0.25)) + snoise(i.wp * 1.7 - float2(t * 0.2, t * 0.35)) * 0.5;
                    c += wet * (1.0 - dW) * saturate(cau - 0.35) * 0.22 * _Day;
                    c += (1.0 - wet) * smoothstep(-0.03, 0.0, depth) * 0.05;                         // damp sheen just above the water
                }
                else c *= 0.8;                                                                       // overhead: seen from below, backlit
                if (kind == 12 || kind == 13)
                {
                    float mouth = 0;
                    [unroll] for (int k = 0; k < 6; k++)
                    {
                        float hk = frac(sin(k * 12.9898 + seed * 7.0) * 43758.5);
                        float2 cc = k == 0 ? float2(0, 0) : float2(cos(k * 2.399 + seed), sin(k * 2.399 + seed)) * (0.3 + 0.25 * hk);
                        float rk = k == 0 ? 0.42 : 0.16 + 0.14 * hk;
                        float2 dm = p - cc;
                        float ma = atan2(dm.y, dm.x);
                        float mr = length(dm) * (1.0 + 0.14 * snoise(float2(cos(ma), sin(ma)) * 1.2 + seed + k * 3.1));   // gently irregular, not star-shaped
                        mouth = max(mouth, smoothstep(rk * 0.34, rk * 0.04, mr) * (0.6 + 0.4 * hk));
                    }
                    float flick = 0.8 + 0.2 * sin(t * 3.0 + seed);
                    c = lerp(c, c * 0.45, mouth * 0.8);                                                // the open throat
                    c += mouth * mouth * (kind == 12 ? float3(0.9, 0.42, 0.14) * 0.45 : float3(0.8, 0.85, 0.8) * 0.2) * flick * i.p2.w;   // a dull ember deep in the throat
                }
                c *= lerp(0.45, 1.0, _Day);
                return fixed4(c, alpha * i.p1.w * i.color.a);
            }
            ENDCG
        }
    }
}
