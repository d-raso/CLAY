Shader "CLAY/StarGlow"
{
    // A camera-facing billboard glow that gives a star its soft luminous "ball" look. Because the CPU keeps it
    // at a roughly constant on-screen size (it does NOT shrink with distance like the textured disc), it comes
    // to dominate — "stamping out" the surface/corona/flare detail — as soon as you pull back a short way, so
    // from any modest distance a star is just a glowing orb; the hard detail only shows when you're close. A
    // soft radial core plus wild, animated, fading radial rays. Additive, unlit, drawn on a UV quad.
    Properties
    {
        _Color("Glow Color", Color) = (1.0, 0.72, 0.4, 1)
        _Intensity("Intensity", Range(0, 4)) = 1.0
        _Seed("Seed", Float) = 0
        _Speed("Speed", Range(0, 2)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+40" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            half4 _Color;
            float _Intensity, _Seed, _Speed;

            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 x)
            {
                float3 p = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(p + float3(0,0,0)), hash(p + float3(1,0,0)), f.x),
                                 lerp(hash(p + float3(0,1,0)), hash(p + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(p + float3(0,0,1)), hash(p + float3(1,0,1)), f.x),
                                 lerp(hash(p + float3(0,1,1)), hash(p + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p) { float s = 0.0, a = 0.5; [unroll] for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.03; a *= 0.5; } return s; }

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            half4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;         // -1..1 across the quad
                float r = length(p);
                if (r >= 1.0) discard;
                float2 dir = p / max(r, 1e-4);         // seamless unit direction (no atan2 pole/seam)
                float t = _Time.y * _Speed;

                // Steep gaussian: EXTREMELY bright right at the star, dropping off fast.
                float glow = exp(-r * r * 8.0);

                // Soft, calm sunshine spokes — sparse, low-contrast, drifting SLOWLY so they don't flicker fast.
                float spoke = fbm(float3(dir * 3.0, t * 0.5) + _Seed);
                float rays = smoothstep(0.5, 1.0, spoke);
                float flick = 0.5 + 0.5 * fbm(float3(dir * 1.5, t * 0.35 + 19.0) + _Seed);
                float rayGlow = rays * flick * exp(-r * r * 4.0);

                float a = (glow + rayGlow * 0.45) * _Intensity;
                a *= smoothstep(1.0, 0.5, r);   // window to an exact zero at the quad edge — no hard rim anywhere
                return half4(_Color.rgb * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
