Shader "CLAY/Star"
{
    // A live stellar photosphere: evolving convection granulation (bright cell cores, dark intergranular
    // lanes), drifting sunspots, temperature-mapped colour (cool lanes → base → hot cell cores), classic
    // limb darkening, and a hot chromosphere rim. Self-emissive (no external light). Object-space view via
    // _CamPosObj (CPU-supplied) so it stays crisp at any scale under the URP 2D renderer.
    Properties
    {
        _Color("Base Color", Color)         = (1.0, 0.80, 0.42, 1)
        _HotColor("Hot Cell Color", Color)  = (1.0, 0.97, 0.88, 1)
        _CoolColor("Cool Lane Color", Color)= (0.75, 0.26, 0.08, 1)
        _Seed("Seed (xyz)", Vector)         = (0, 0, 0, 0)
        _Speed("Churn Speed", Range(0, 2))  = 0.6
        _Gran("Granulation Scale", Range(2, 40)) = 12
        _SpotAmount("Sunspot Amount", Range(0, 0.6)) = 0.12
        _Brightness("Brightness", Range(0.5, 4)) = 1.7
        _RimGlow("Rim glow (0 near → 1 far)", Range(0, 1)) = 0
        _CamPosObj("Camera (object space)", Vector) = (0, 0, 10, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 od : TEXCOORD0; };

            half4 _Color, _HotColor, _CoolColor;
            float4 _Seed, _CamPosObj;
            float _Speed, _Gran, _SpotAmount, _Brightness, _RimGlow;

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
                float3 n = normalize(i.od);
                float3 seed = _Seed.xyz;
                float t = _Time.y * _Speed;

                // Domain-warped convection granulation — cells boil and evolve over time (bright cores, dark lanes).
                float3 warp = float3(fbm(n * _Gran * 0.5 + seed + float3(0, t * 0.3, 0)),
                                     fbm(n * _Gran * 0.5 + seed + 4.0 + float3(t * 0.25, 0, 0)),
                                     fbm(n * _Gran * 0.5 + seed + 8.0)) - 0.5;
                float cells = fbm(n * _Gran + warp * 1.9 + float3(0, 0, t * 0.5));
                float fine  = fbm(n * _Gran * 2.8 + warp * 2.2 + float3(t * 0.6, 0, 0));
                float finer = fbm(n * _Gran * 6.0 + warp * 2.4 + float3(0, t * 0.9, 0));
                float bright = saturate(cells * 0.62 + fine * 0.30 + finer * 0.18);

                // Soft intergranular lanes — gentle, so the surface reads as subtle mottling, not a hard shell.
                float lane = 1.0 - abs(cells * 2.0 - 1.0);
                bright *= 0.74 + 0.26 * (1.0 - lane);
                bright = saturate((bright - 0.5) * 1.3 + 0.5);

                // Sunspots — large, slow, cool dark patches.
                float spotField = fbm(n * 2.0 + seed * 3.0 + float3(0, t * 0.05, 0));
                float spot = smoothstep(1.0 - _SpotAmount - 0.06, 1.0 - _SpotAmount + 0.02, spotField);

                // Temperature → colour: cool lanes → base → hot cell cores; spots cooler and darker still.
                half3 col = lerp(_CoolColor.rgb, _Color.rgb, smoothstep(0.20, 0.55, bright));
                col = lerp(col, _HotColor.rgb, smoothstep(0.60, 0.95, bright));
                col = lerp(col, _CoolColor.rgb * 0.4, spot * 0.9);

                // View + camera-distance-driven RIM GLOW, baked into the sphere. mu = 1 at the disc centre, 0 at
                // the limb; fres is the fresnel edge factor. _RimGlow runs 0 (close) → 1 (far).
                float3 V = normalize(_CamPosObj.xyz - i.od);
                float mu = saturate(dot(n, V));
                float fres = 1.0 - mu;

                // GRANULE RELIEF: emboss from the brightness gradient so convection cells catch highlight/shadow →
                // real 3-D texture instead of a flat painted mottle.
                float bx = ddx(bright), by = ddy(bright);
                float3 gnrm = normalize(float3(-bx * 8.0, -by * 8.0, 1.0));
                float relief = saturate(0.45 + 0.95 * dot(gnrm, normalize(float3(0.45, 0.55, 0.7))));

                // Strong limb darkening + REDDENING toward the limb (cooler edge) → a round glowing gas ball.
                float limb = 0.30 + 0.70 * pow(mu, 0.7);
                col = lerp(col, _CoolColor.rgb * 0.75, fres * fres * 0.6);

                // Subtle photosphere seen up close: hot cores, dark lanes, limb darkening + granule relief.
                float hot = smoothstep(0.62, 0.98, bright);
                col = lerp(col, _HotColor.rgb * 1.4, hot * 0.4);
                col *= (0.5 + 0.9 * bright) * (1.0 - spot * 0.7) * limb * (0.72 + 0.42 * relief) * _Brightness;

                // Chromosphere rim: a thin hot edge up close that WIDENS (lower fresnel power → reaches further
                // in) and BRIGHTENS as the camera pulls back, until it floods the disc.
                float rimPow = lerp(3.0, 0.35, _RimGlow);
                float rimAmt = lerp(0.55, 3.2, _RimGlow);
                col += _HotColor.rgb * pow(fres, rimPow) * rimAmt;

                // From far, also lift the whole disc toward the hot colour so the centre doesn't stay dark — the
                // star fuses into a single glowing orb of light rather than a bright ring around a dim middle.
                col += _HotColor.rgb * _RimGlow * _RimGlow * (0.4 + 0.6 * bright) * 0.9;
                return half4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
