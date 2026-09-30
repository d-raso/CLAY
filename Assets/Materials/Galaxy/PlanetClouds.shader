Shader "CLAY/PlanetClouds"
{
    // A thin animated cloud shell for terrestrial worlds. Domain-warped fBm on the sphere direction gives
    // wispy, evolving cloud masses; a coverage threshold carves clear sky between them with soft feathered
    // edges. Lighting is done in WORLD space (UnityObjectToWorldNormal) so the day/night terminator stays
    // fixed toward the star even as the shell spins with the planet — clouds brighten on the lit side, go to
    // a dim blue-grey on the night side, and never light-follow the rotation. Clouds also drift slowly
    // relative to the surface (an extra in-shader rotation) so weather isn't locked to the ground. Unlit CG,
    // alpha-blended over the surface, for the URP 2D renderer.
    Properties
    {
        _CloudColor("Cloud Color (day)", Color) = (1, 1, 1, 1)
        _SunDir("Sun Direction (world)", Vector) = (0, 0, -1, 0)
        _Sun1Col("Main Sun Tint", Color) = (1, 1, 1, 1)
        _Sun2Dir("Second Sun Dir (obj, w = strength)", Vector) = (0, 0, 0, 0)
        _Sun2Col("Second Sun Tint", Color) = (1, 1, 1, 1)
        _Coverage("Coverage", Range(0, 1)) = 0.5
        _Seed("Seed (xyz)", Vector) = (0, 0, 0, 0)
        _Speed("Drift Speed", Range(0, 1)) = 0.06
        _Sharp("Edge Sharpness", Range(0.02, 0.5)) = 0.16
        _Ambient("Night Ambient", Range(0, 0.5)) = 0.08
        _Density("Opacity", Range(0, 1)) = 0.9
        _Swirl("Zonal Wind / Swirl", Range(0, 1)) = 0.4
        _Haze("Haze Floor (overcast)", Range(0, 1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 od : TEXCOORD0; };

            half4 _CloudColor;
            float4 _SunDir, _Seed, _CamPosObj, _Sun2Dir;
            half4 _Sun1Col, _Sun2Col;
            float _Coverage, _Speed, _Sharp, _Ambient, _Density, _Swirl, _Haze;

            // cheap hash-based value noise in 3D
            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 x)
            {
                float3 p = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(p + float3(0,0,0)), hash(p + float3(1,0,0)), f.x),
                                 lerp(hash(p + float3(0,1,0)), hash(p + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(p + float3(0,0,1)), hash(p + float3(1,0,1)), f.x),
                                 lerp(hash(p + float3(0,1,1)), hash(p + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { s += a * vnoise(p); p *= 2.02; a *= 0.5; }
                return s;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.od = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 od = normalize(i.od);
                float t = _Time.y * _Speed;
                float3 seed = _Seed.xyz;

                // Uniform slow drift of the WHOLE deck around the spin axis (weather ≠ ground). No latitude-
                // dependent drift: that differential rotation wound the field into ever-thinner horizontal stripes
                // as time accumulated (the "pure stripes" bug). The turbulence now comes from the domain warp.
                float ca = cos(t), sa = sin(t);
                float3 rn = float3(ca * od.x + sa * od.z, od.y, -sa * od.x + ca * od.z);

                // Zonal-band TENDENCY without striping: compress the longitudinal axis so masses elongate east-west
                // at high _Swirl, while latitude varies normally. At low _Swirl this is ~isotropic → turbulent,
                // Earth-like cumulus puffs. (Terrestrial worlds use low _Swirl, so they read as churning weather.)
                float lon = 1.0 / (1.0 + _Swirl * 2.5);
                float3 sc = float3(rn.x * lon, rn.y, rn.z * lon);

                // Domain-warped fBm — the warp (q) is what makes the masses swirl, curl and churn (turbulence),
                // evolving in time so clouds form and dissipate rather than rigidly rotating.
                float3 q = float3(fbm(sc * 2.4 + seed + float3(0, 0, t * 0.18)),
                                  fbm(sc * 2.4 + seed + 5.2),
                                  fbm(sc * 2.4 + seed + 9.7 + t * 0.12));
                float n = fbm(sc * 3.0 + q * 2.0);

                // Coverage carves clear sky; _Sharp feathers the edges. Higher _Coverage → more cloud. _Haze adds
                // a uniform floor so thick/greenhouse worlds read as fully overcast rather than broken cloud.
                float lo = (1.0 - _Coverage) - _Sharp;
                float density = saturate(smoothstep(lo, lo + 2.0 * _Sharp, n) + _Haze);
                if (density < 0.003) discard;

                // Object-space lighting: _SunDir is the object-space direction toward the star (CPU-updated
                // each frame, so it tracks the shell's spin). Avoids the world-normal transform that loses
                // precision — and flattens the terminator — at the tiny scales of distant planets.
                float ndl1 = saturate(dot(od, _SunDir.xyz));
                float ndl2 = _Sun2Dir.w > 0.0005 ? saturate(dot(od, normalize(_Sun2Dir.xyz))) * _Sun2Dir.w : 0.0;
                float ndl = saturate(ndl1 + ndl2);
                float lit = _Ambient + (1.0 - _Ambient) * ndl;

                // Cheap self-shadowing for VOLUME: sample the cloud field one step toward the sun. Thicker there →
                // this parcel sits in the shadow of taller cloud sunward of it, so darken it. Gives the deck relief
                // (bright tops, shaded flanks) instead of reading as a flat painted layer.
                float nsun = fbm(sc * 3.0 + q * 2.0 + _SunDir.xyz * 0.28);
                float selfShadow = saturate((nsun - n) * 2.5) * ndl;

                // Sunlit clouds are bright; night clouds a dim cool grey. Stronger density contrast adds body.
                half3 lday = _CloudColor.rgb * (0.72 + 0.28 * density);
                half3 lnight = _CloudColor.rgb * 0.10 + half3(0.02, 0.03, 0.05);
                half3 sunTint = (ndl1 * _Sun1Col.rgb + ndl2 * _Sun2Col.rgb) / max(ndl1 + ndl2, 1e-3);
                half3 col = lerp(lnight, lday * sunTint, lit);
                col *= 1.0 - selfShadow * 0.55;

                // Edge-on thickness: near the limb the sightline passes through much more cloud, so it should read
                // as a THICK glowing rim, not a flat lid sitting on the atmosphere. Boost both the scattering
                // brightness and the opacity at grazing angles (needs the object-space camera position from CPU).
                float3 V = normalize(_CamPosObj.xyz - od);
                float limb = pow(1.0 - saturate(dot(od, V)), 2.0);
                col += _CloudColor.rgb * limb * 0.25 * lit;

                // Opacity falls to zero on the night side (ndl²) so the dark hemisphere and the limb just beyond
                // it stay clear — night clouds were alpha-blending a dark grey over the starfield.
                float a = density * _Density * ndl * ndl;
                a = saturate(a + a * limb * 1.6);              // thicker toward the limb → volume, not a flat top
                return half4(col, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
