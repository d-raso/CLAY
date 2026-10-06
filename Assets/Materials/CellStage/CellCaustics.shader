Shader "CLAY/CellStage/Caustics"
{
    // Additive caustic filaments over the tide pool's water — layered on top of the environment's own caustics so
    // the pool keeps crisp, drifting light detail. Same heightfield/tide as CLAY/CellStage/Soup.
    Properties
    {
        _Height ("Height", 2D) = "black" {}
        _Tide ("Tide level", Float) = 0.5
        _Day ("Daylight", Float) = 1
        _ArenaSize ("Arena size", Vector) = (192, 120, 0, 0)
        _WaterShallow ("unused", Color) = (0,0,0,0)
        _WaterDeep ("unused", Color) = (0,0,0,0)
        _Rock ("unused", Color) = (0,0,0,0)
        _Crust ("unused", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            // CAUSTICS (additive): sharp, drifting light filaments on the water — layered over the environment's own
            // caustics so the pool keeps crisp detail. Strongest in shallow sunlit water, softening with depth.
            ZWrite Off Cull Off
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragC
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _Height; float4 _Height_TexelSize;
            float _Tide, _Day; float4 _WaterShallow, _WaterDeep, _Rock, _Crust, _ArenaSize;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float H(float2 uv) { return tex2D(_Height, uv).r; }
            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float2 hash2(float2 p) { return float2(hash(p), hash(p + 17.31)); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y); }
            float fbm(float2 p) { float s = 0, a = 0.5; for (int k = 0; k < 5; k++) { s += a * vn(p); p = mul(float2x2(1.6, 1.2, -1.2, 1.6), p) + 3.1; a *= 0.5; } return s; }
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
            // Caustics as LIGHT FOCUSING (like the environment's WaterCaustics): the surface is a domain-warped
            // value-noise height field (never periodic, so no tiling at any distance); where it curves concave
            // (negative Laplacian) light converges into bright filaments. Two scales are derived from ONE surface so
            // they always agree (no clashing webs).
            float surfaceH(float2 p, float t)
            {
                float2 w = float2(fbm(p * 0.35 + float2(t * 0.11, -t * 0.07)), fbm(p * 0.35 + float2(5.2, 1.3) - t * 0.09));
                return fbm(p + w * 2.2 + t * 0.05);
            }
            float caustics(float2 wp, float t, float blur)
            {
                float2 p = wp * 0.55;
                float e = lerp(0.05, 0.11, blur);                   // a wider stencil = softer focus in deeper water
                float hC = surfaceH(p, t);
                float lap = (surfaceH(p + float2(e, 0), t) + surfaceH(p - float2(e, 0), t)
                           + surfaceH(p + float2(0, e), t) + surfaceH(p - float2(0, e), t) - 4.0 * hC) / (e * e);
                float focus = saturate(-lap * 0.05);
                return pow(focus, lerp(2.6, 1.6, blur)) + pow(saturate(-lap * 0.025), 1.3) * 0.15;
            }

            fixed4 fragC(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 wp = i.uv * _ArenaSize.xy;
                float depth = _Tide - H(i.uv);
                float wet = smoothstep(0.0, 0.004, depth);
                if (wet <= 0.0) return 0;
                float blur = saturate(depth * 5.0);
                float c = caustics(wp, t * 0.5, blur);
                float patchy = 0.4 + 0.6 * smoothstep(0.25, 0.75, fbm(wp * 0.05 + t * 0.015));
                float k = _Day * wet * lerp(0.14, 0.05, saturate(depth * 4.0)) * patchy;
                return fixed4(c * k * float3(0.92, 1.0, 0.95), 0);
            }
            ENDCG
        }
    }
}
