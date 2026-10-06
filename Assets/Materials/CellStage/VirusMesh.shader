Shader "CLAY/CellStage/VirusMesh"
{
    // Virus bodies as a MOLECULAR SURFACE (like a cryo-EM map): the mesh gives the overall form (head, fibres, tail);
    // over it, a dense layer of fused protein subunits — lumpy, softly lit, with crevices in shadow — coloured by the
    // compound at each region. The outline is bumpy too. Lit by the pool: daylight, the water's tint, drifting light.
    // Vertex data: colour; normal (the form's tilt); uv0.x = 1 at the core → 0 at the rim; uv1 = position in body
    // space (radius units, so the subunits stay attached to the body as it moves).
    Properties { _Day ("Daylight", Float) = 1 }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            float _Day; float4 _WaterTint;
            struct A { float4 v : POSITION; float3 n : NORMAL; float4 c : COLOR; float2 uv : TEXCOORD0; float2 lp : TEXCOORD1; };
            struct V { float4 p : SV_POSITION; float3 n : TEXCOORD0; float4 c : COLOR; float2 wp : TEXCOORD1; float2 uv : TEXCOORD2; float2 lp : TEXCOORD3; };
            V vert(A a) { V o; o.p = UnityObjectToClipPos(a.v); o.n = a.n; o.c = a.c; o.wp = mul(unity_ObjectToWorld, a.v).xy; o.uv = a.uv; o.lp = a.lp; return o; }
            float2 h2(float2 p) { p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3))); return frac(sin(p) * 43758.5453); }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y); }
            // the subunit layer: rounded blobs packed on a jittered grid (two sizes), as a height field
            float Lumps(float2 q)
            {
                float h = 0.0;
                float2 c = floor(q), f = frac(q);
                [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y), o = h2(c + g) * 0.7 + 0.15;
                    float d = length(g + o - f);
                    float r = 0.55 + 0.25 * hash(c + g);
                    h = max(h, sqrt(saturate(1.0 - (d / r) * (d / r))));
                }
                float2 q2 = q * 2.3 + 7.1, c2 = floor(q2), f2 = frac(q2); float h2v = 0.0;
                [unroll] for (int y2 = -1; y2 <= 1; y2++) [unroll] for (int x2 = -1; x2 <= 1; x2++)
                {
                    float2 g = float2(x2, y2), o = h2(c2 + g) * 0.7 + 0.15;
                    float d = length(g + o - f2);
                    h2v = max(h2v, sqrt(saturate(1.0 - d * d / 0.36)));
                }
                return h * 0.75 + h2v * 0.25;
            }
            fixed4 frag(V i) : SV_Target
            {
                float2 q = i.lp * 6.5;
                const float e = 0.06;
                float h = Lumps(q), hx = Lumps(q + float2(e, 0)), hy = Lumps(q + float2(0, e));
                // a bumpy outline: near the rim, the gaps between subunits are empty
                if (i.uv.x < 0.18 && h < 0.35 * (1.0 - i.uv.x / 0.18)) discard;
                float3 n = normalize(normalize(i.n) + float3(-(hx - h) / e, -(hy - h) / e, 0.0) * 0.18);
                float3 L = normalize(float3(-0.45, 0.55, 0.7));
                float lam = saturate(dot(n, L));
                float spec = pow(saturate(dot(reflect(-L, n), float3(0, 0, 1))), 14.0);
                float ao = lerp(0.55, 1.0, h);                                       // crevices between subunits sit in shadow
                float t = _Time.y;
                float2 cq = i.wp * 1.7 + float2(t * 0.35, -t * 0.27);
                float caus = vn(cq) * vn(cq * 1.9 + 3.1);
                float light = (0.38 + 0.75 * lam * (0.85 + 0.5 * caus)) * ao * lerp(0.4, 1.0, _Day);
                float3 c = i.c.rgb * light + spec * 0.18 * _Day * h;
                c = lerp(c, c * _WaterTint.rgb * 1.3, 0.2);
                return fixed4(c, i.c.a);
            }
            ENDCG
        }
    }
}
