Shader "CLAY/StarFlares"
{
    // Chromospheric flares / prominences: a thin, intensely hot band hugging the star's limb, broken into
    // turbulent tongues of fire whose height varies around the star and churns over time — so flames appear to
    // lick and erupt off the surface. Additive, keyed to the view ray's impact parameter so it sits just above
    // the photosphere. Object-space ray via _CamPosObj. Unlit CG for the URP 2D renderer.
    Properties
    {
        _Color("Flare Color", Color)     = (1.0, 0.42, 0.12, 1)
        _HotColor("Hot Base Color", Color) = (1.0, 0.85, 0.5, 1)
        _InnerR("Star radius (shell space)", Float) = 0.42
        _Band("Flame band thickness", Range(0.02, 0.4)) = 0.12
        _Intensity("Intensity", Range(0, 4)) = 1.5
        _Seed("Seed", Float) = 0
        _Speed("Speed", Range(0, 3)) = 1.2
        _CamPosObj("Camera (object space)", Vector) = (0, 0, 10, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+28" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 od : TEXCOORD0; };

            half4 _Color, _HotColor;
            float4 _CamPosObj;
            float _InnerR, _Band, _Intensity, _Seed, _Speed;

            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 x)
            {
                float3 p = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(p + float3(0,0,0)), hash(p + float3(1,0,0)), f.x),
                                 lerp(hash(p + float3(0,1,0)), hash(p + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(p + float3(0,0,1)), hash(p + float3(1,0,1)), f.x),
                                 lerp(hash(p + float3(0,1,1)), hash(p + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p) { float s = 0.0, a = 0.5; [unroll] for (int i = 0; i < 5; i++) { s += a * vnoise(p); p *= 2.03; a *= 0.5; } return s; }

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.od = v.vertex.xyz; return o; }

            half4 frag(v2f i) : SV_Target
            {
                float3 O = _CamPosObj.xyz;
                float3 D = normalize(i.od - O);
                float b = length(O + D * (-dot(O, D)));          // impact parameter
                float3 dir = normalize(i.od);
                float t = _Time.y * _Speed;

                // Per-direction flame HEIGHT — some spots erupt into tall tongues, most stay low; it churns.
                float h = fbm(dir * 5.0 + float3(0, 0, t * 0.6) + _Seed);
                float tall = 0.35 + 1.5 * pow(saturate(h), 2.0);

                // Position within the flame band above the limb (0 at surface → 1 at the tongue tip).
                float x = (b - _InnerR) / max(_Band * tall, 1e-3);
                if (x < 0.0 || x > 1.0) discard;
                float radial = 1.0 - x;                          // dense at the base, thinning to the tip

                // Soft fiery filaments, flowing OUTWARD and churning — feathery licking flames, not hard spikes.
                float flame = fbm(dir * 13.0 + float3(0, 0, -t * 2.2) + _Seed * 1.7);
                float fil = smoothstep(0.34, 0.9, flame);

                float a = radial * radial * radial * fil * tall * _Intensity * 0.8;   // cubic falloff → softer, wispier
                half3 col = lerp(_Color.rgb, _HotColor.rgb, saturate(radial));   // hot at the base, redder at the tip
                return half4(col * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
