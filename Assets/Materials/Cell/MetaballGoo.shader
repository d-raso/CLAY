Shader "CLAY/MetaballGoo"
{
    // Localized 2D metaballs for the cell-pop: a quad whose fragment sums a field from a handful of
    // moving "blobs" (fed each frame by MetaballRunner) and thresholds it into a single fused, organic
    // gooey silhouette. Overlapping blobs MERGE instead of staying separate dots. Unlit + transparent.
    Properties
    {
        [HDR] _Tint("Goo Tint", Color) = (0.4, 1, 0.5, 1)
        [HDR] _RimColor("Rim Highlight", Color) = (1, 1, 1, 1)
        _Threshold("Surface Threshold", Float) = 1.0
        _EdgeSoftness("Edge Softness", Float) = 0.35
        _NoiseScale("Internal Mottle Scale", Float) = 8
        _Opacity("Max Opacity", Range(0, 1)) = 1
        _GlobalAlpha("Global Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_BLOBS 32

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _Blobs[MAX_BLOBS];   // xy = centre (uv 0..1), z = radius (uv units)
            int    _BlobCount;
            half4  _Tint, _RimColor;
            float  _Threshold, _EdgeSoftness, _NoiseScale, _Opacity, _GlobalAlpha;

            float hash21(float2 p) { p = frac(p * float2(123.34, 345.45)); p += dot(p, p + 34.345); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float fbm(float2 p) { float s = 0, amp = 0.5; for (int o = 0; o < 3; o++) { s += vnoise(p) * amp; p *= 2; amp *= 0.5; } return s; }

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            half4 frag(v2f i) : SV_Target
            {
                float field = 0.0;
                [loop] for (int k = 0; k < _BlobCount; k++)
                {
                    float2 d = i.uv - _Blobs[k].xy;
                    float br = _Blobs[k].z;
                    field += (br * br) / (dot(d, d) + 1e-5);
                }

                float a = smoothstep(_Threshold - _EdgeSoftness, _Threshold + _EdgeSoftness, field);
                // Bright wet rim right at the fused surface.
                float rim = smoothstep(_Threshold, _Threshold + _EdgeSoftness, field)
                          * (1.0 - smoothstep(_Threshold + _EdgeSoftness, _Threshold + 3.0 * _EdgeSoftness, field));

                // Internal mottle: darker organic patches so the goo reads as matter, not flat light.
                float mottle = fbm(i.uv * _NoiseScale);
                half3 col = _Tint.rgb * lerp(0.45, 1.0, mottle);
                col += _RimColor.rgb * rim * 0.2;                     // faint wet edge only
                return half4(col, saturate(a) * _GlobalAlpha * _Opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
