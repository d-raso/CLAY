Shader "CLAY/StarCorona"
{
    // An additive corona halo around a star: a shell sphere whose glow is keyed to the view ray's distance
    // from the star centre (impact parameter), so it forms a soft luminous ring hugging the limb and fading
    // outward — transparent across the star's own disc so it never washes the photosphere. Animated radial
    // streamers give it a living, wispy corona. Object-space ray via _CamPosObj (CPU-supplied) for scale
    // safety. Additive-blended, unlit CG for the URP 2D renderer.
    Properties
    {
        _Color("Corona Color", Color) = (1.0, 0.72, 0.34, 1)
        _InnerR("Star radius (shell space)", Float) = 0.25
        _OuterR("Outer radius (shell)", Float) = 0.5
        _Intensity("Intensity", Range(0, 4)) = 1.3
        _Curl("Arc / swirl", Range(0, 4)) = 0.5
        _Detail("Detail (fine noise)", Range(0, 1)) = 0.5
        _Seed("Seed", Float) = 0
        _Speed("Speed", Range(0, 2)) = 0.35
        _CamPosObj("Camera (object space)", Vector) = (0, 0, 10, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+30" }
        Blend One One            // additive glow
        ZWrite Off
        Cull Off                 // visible from inside too, so it doesn't vanish when you get very close
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 od : TEXCOORD0; };

            half4 _Color;
            float4 _CamPosObj;
            float _InnerR, _OuterR, _Intensity, _Curl, _Detail, _Seed, _Speed;

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

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.od = v.vertex.xyz; return o; }

            half4 frag(v2f i) : SV_Target
            {
                // Impact parameter: closest approach of the view ray to the star centre (object space).
                float3 O = _CamPosObj.xyz;
                float3 D = normalize(i.od - O);
                float b = length(O + D * (-dot(O, D)));

                // Glow is DENSEST right at the limb (b = _InnerR) and falls off outward — so it reads as light
                // radiating off the photosphere, not a ring floating behind the star. Zero across the disc.
                float x = saturate((b - _InnerR) / max(_OuterR - _InnerR, 1e-3));   // 0 at limb → 1 at shell edge
                float disc = smoothstep(_InnerR * 0.9, _InnerR, b);                 // soft cut over the star's disc
                float fall = pow(1.0 - x, 2.1);                                     // soft radial falloff (extends out)

                // Radial streamers in SCREEN space: build a basis perpendicular to the camera→star axis and take
                // the fragment's direction within it, so the streaks radiate outward in the IMAGE. Sampling the
                // noise on the unit screen-circle (cos,sin) — not an object-space angle — avoids the two-pole
                // pinch (atan2 on the sphere is degenerate at ±z) and the wrap seam. Stretched along b = streaks.
                float3 fd = normalize(i.od - O);
                float3 axis = normalize(-O);                         // camera → star centre (object space)
                float3 up0 = abs(axis.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 rgt = normalize(cross(up0, axis));
                float3 upp = cross(axis, rgt);
                float2 sp = float2(dot(fd, rgt), dot(fd, upp));
                float2 circ = sp / max(length(sp), 1e-4);
                // A radius-dependent twist makes the streamers ARC outward instead of running dead-straight.
                float curl = (b - _InnerR) * _Curl;
                float cc = cos(curl), ss = sin(curl);
                float2 circR = float2(cc * circ.x - ss * circ.y, ss * circ.x + cc * circ.y);
                // Thick wild radial arcs that flicker in and out, brightest at the base and fading outward, with
                // an extra fine-noise octave (_Detail) breaking them up so they read as turbulent plasma.
                float sN = fbm(float3(circR * 4.0, b * 4.0 - _Time.y * _Speed) + _Seed);
                sN += _Detail * (fbm(float3(circR * 11.0, b * 9.0 - _Time.y * _Speed * 1.6) + _Seed * 1.7) - 0.5);
                float streak = smoothstep(0.34, 0.82, sN);
                float flick = 0.35 + 0.65 * fbm(float3(circR * 2.0, _Time.y * _Speed * 0.6 + 11.0) + _Seed);
                float baseBoost = 0.6 + 1.3 * (1.0 - x);       // brighter at the bottom of the arc

                float a = disc * fall * (0.25 + 1.7 * streak * flick) * baseBoost * _Intensity;
                return half4(_Color.rgb * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
