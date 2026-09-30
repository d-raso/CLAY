Shader "CLAY/PlanetRing"
{
    // A translucent planetary ring — a flat annulus with procedural radial ringlets and Cassini-style gaps,
    // sun-angle lighting, and a shadow cast by the planet (the ring darkens where the planet blocks the sun).
    // Inner edge, width and opacity are adjustable. Two-sided, alpha-blended, unlit CG for the URP 2D renderer.
    // The mesh carries radial UV.x (0 at the mesh inner edge → 1 at the outer). Set _SunDir and _PlanetR/frame.
    Properties
    {
        _ColorA("Ring color A", Color) = (0.80, 0.74, 0.62, 1)
        _ColorB("Ring color B", Color) = (0.55, 0.50, 0.42, 1)
        _SunDir("Sun Direction (world)", Vector) = (0, 0, -1, 0)
        _Seed("Seed", Float) = 0
        _Density("Opacity", Range(0, 1)) = 0.85
        _RingInner("Inner radius (start)", Range(0, 0.9)) = 0.45
        _RingWidth("Width", Range(0.05, 1)) = 0.5
        _PlanetR("Planet radius (world)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wn : TEXCOORD1; float3 wp : TEXCOORD2; };

            half4 _ColorA, _ColorB;
            float4 _SunDir;
            float _Seed, _Density, _RingInner, _RingWidth, _PlanetR;

            float hash(float n) { return frac(sin(n) * 43758.5453); }
            float vnoise(float x) { float i = floor(x), f = frac(x); f = f * f * (3 - 2 * f); return lerp(hash(i), hash(i + 1), f); }
            float fbm(float x) { float s = 0, a = 0.5; [unroll] for (int k = 0; k < 5; k++) { s += a * vnoise(x); x *= 2.03; a *= 0.5; } return s; }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wn = UnityObjectToWorldNormal(float3(0, 1, 0));           // ring plane normal
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;             // world position (for the shadow)
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float u = i.uv.x;                                          // 0 mesh-inner → 1 mesh-outer
                float inner = _RingInner, outer = saturate(_RingInner + _RingWidth);

                // Visible band with soft inner/outer edges; r is normalized within the band for the pattern.
                float edge = smoothstep(inner, inner + 0.02, u) * (1.0 - smoothstep(outer - 0.02, outer, u));
                if (edge < 0.001) discard;
                float r = saturate((u - inner) / max(outer - inner, 1e-3));

                // Radial ringlets + a couple of darker gaps.
                float rs = r * 55.0 + _Seed;
                float bands = fbm(rs) * 0.6 + fbm(rs * 0.35) * 0.5;
                float density = saturate(bands) * _Density * edge;
                density *= 1.0 - 0.9 * exp(-pow((r - 0.42) * 24.0, 2.0));
                density *= 1.0 - 0.7 * exp(-pow((r - 0.70) * 36.0, 2.0));

                half3 col = lerp(_ColorA.rgb, _ColorB.rgb, saturate(fbm(rs * 0.5 + 13.0)));

                // Sun-angle lighting: bright when the sun faces the ring plane, dim edge-on.
                float3 N = normalize(i.wn);
                float3 sunT = normalize(-_SunDir.xyz);
                float lit = 0.3 + 0.8 * abs(dot(N, sunT));

                // Planet shadow: the planet is a sphere of radius _PlanetR at the ring's origin. A fragment is
                // shadowed if it sits behind the planet (away from the sun) within the planet's silhouette.
                float3 planetC = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 d = i.wp - planetC;
                float along = dot(d, sunT);
                float perp = length(d - along * sunT);
                float shadow = (along < 0.0) ? (1.0 - smoothstep(_PlanetR * 0.92, _PlanetR * 1.08, perp)) : 0.0;
                lit *= 1.0 - shadow * 0.88;

                return half4(col * lit, density);
            }
            ENDCG
        }
    }
    Fallback Off
}
