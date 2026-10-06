Shader "CLAY/PlanetAtmosphere"
{
    // A real atmospheric shell that hazes the WHOLE planet disc, not just a backlit halo. For each view ray
    // it computes the length of atmosphere the ray passes through (the column: the chord through the outer
    // shell minus the chord blocked by the solid planet). That column is thin over the disc centre and long
    // at the limb, fading smoothly to zero at the shell edge — so the planet is tinted/obscured by haze,
    // brightest and thickest at the limb, with a soft outer falloff and no hard shell edge. Renders the near
    // side (Cull Back) and alpha-blends over the planet. Unlit CG for the URP 2D renderer; set _SunDir/frame.
    Properties
    {
        _AtmColor("Atmosphere Color", Color) = (0.45, 0.6, 1.0, 1)
        _SunDir("Sun Direction (world)", Vector) = (0, 0, -1, 0)
        _Sun1Col("Main Sun Tint", Color) = (1, 1, 1, 1)
        _Sun2Dir("Second Sun Dir (obj, w = strength)", Vector) = (0, 0, 0, 0)
        _Sun2Col("Second Sun Tint", Color) = (1, 1, 1, 1)
        _InnerR("Inner Radius (planet surface)", Float) = 0.42
        _OuterR("Outer Radius (shell)", Float) = 0.5
        _Intensity("Density", Range(0, 3)) = 0.55
        _Softness("Softness", Range(0.5, 3)) = 1.2
        _DayBias("Day Bias", Range(0, 1)) = 0.2
        _Aurora("Aurora strength", Range(0, 1)) = 0
        _AuroraColor("Aurora colour", Color) = (0.3, 1, 0.5, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" }
        // Additive (scattering) rather than alpha-over. Alpha-over with the dim night-limb colour subtracted
        // light from the star background, leaving a dark halo ringing each planet. Scattering only ever ADDS
        // light, so the sunlit limb glows and the night side / terminator adds nothing — stars shine through.
        Blend SrcAlpha One
        ZWrite Off
        Cull Back                          // near side, so it sits over the planet
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 od : TEXCOORD0; };

            half4 _AtmColor;
            float4 _SunDir, _CamPosObj, _Sun2Dir;
            half4 _Sun1Col, _Sun2Col;
            float _InnerR, _OuterR, _Intensity, _Softness, _DayBias, _Aurora;
            half4 _AuroraColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.od = v.vertex.xyz;   // object-space position (shell radius = _OuterR here)
                return o;
            }

                // small smooth 2D value noise for the aurora oval
                float AurH(float2 q) { return frac(sin(dot(q, float2(127.1, 311.7))) * 43758.5453); }
                float AurN(float2 q)
                {
                    float2 i = floor(q), f = frac(q); f = f * f * (3.0 - 2.0 * f);
                    return lerp(lerp(AurH(i), AurH(i + float2(1, 0)), f.x), lerp(AurH(i + float2(0, 1)), AurH(i + float2(1, 1)), f.x), f.y);
                }
            half4 frag(v2f i) : SV_Target
            {
                // View ray in object space. Camera position supplied by the CPU — the GPU world-to-object
                // inverse degenerates at distant planets' tiny scales, which collapsed the soft haze column.
                float3 O = _CamPosObj.xyz;
                float3 D = normalize(i.od - O);
                float b = length(O + D * (-dot(O, D)));            // impact parameter (ray ↔ centre distance)

                // Atmosphere column = chord through the shell minus chord blocked by the solid planet.
                float atmChord    = sqrt(max(_OuterR * _OuterR - b * b, 0.0));
                float planetChord = sqrt(max(_InnerR * _InnerR - b * b, 0.0));
                float column = (atmChord - planetChord) / max(_OuterR - _InnerR, 1e-4);   // ~1 over disc, larger at limb
                column = pow(saturate(column * 0.5), _Softness);
                // Smoothly fade the density to zero across the outer part of the shell so the limb has no hard
                // digital cutoff — a soft radial falloff from the surface (t=1) to the shell edge (t=0).
                float t = saturate((_OuterR - b) / max(_OuterR - _InnerR, 1e-4));
                column *= smoothstep(0.0, 0.8, t);

                // Sun term in OBJECT space: _SunDir is the object-space direction toward the star (recomputed
                // each frame on the CPU, so it already tracks the shell's spin). Object-space avoids the
                // world-normal transform, which loses precision at the tiny scales of distant planets.
                float3 nrm = normalize(i.od);
                float sun = saturate(dot(nrm, _SunDir.xyz) * (1.0 - _DayBias) + _DayBias);

                // Night side must NOT darken the sky behind the planet (that read as a black orb occluding stars):
                // the atmosphere is only visible where sunlit, fading to fully transparent on the night side.
                float sun2 = 0.0;
                if (_Sun2Dir.w > 0.0005)
                    sun2 = saturate(dot(nrm, normalize(_Sun2Dir.xyz)) * (1.0 - _DayBias) + _DayBias) * _Sun2Dir.w;
                float lit = saturate(sun * sun + sun2 * sun2);
                half3 col = _AtmColor.rgb * ((0.15 + 0.85 * sun) * _Sun1Col.rgb + 0.85 * sun2 * _Sun2Col.rgb);
                float a = saturate(column * _Intensity * lit);
                // aurorae: flickering curtains on rings ~20° from the poles, only where it's dark
                if (_Aurora > 0.001)
                {
                    // tilted magnetic pole; the oval wobbles, bulges and folds instead of being a perfect ring
                    float3 mp = normalize(float3(0.16, 1.0, 0.09));
                    float3 nn = nrm.y < 0 ? -nrm : nrm; float3 mpp = mp;
                    float lat = dot(nn, mpp);
                    float3 e1 = normalize(cross(mpp, float3(0, 0, 1))), e2 = cross(mpp, e1);
                    float lon = atan2(dot(nn, e2), dot(nn, e1));
                    float at = _Time.y;
                    float2 c2 = float2(cos(lon), sin(lon));
                    float wob = AurN(c2 * 1.6 + at * 0.03) * 0.06 + AurN(c2 * 4.0 - at * 0.07) * 0.025;   // oval shape
                    float centre = 0.925 + wob - 0.02 + c2.x * 0.015;                               // pushed toward the night side
                    float width = 0.02 + 0.025 * AurN(c2 * 2.3 + 11.0 + at * 0.05);
                    float ad = lat - centre;
                    float ring = exp(-ad * ad / (width * width)) + 0.35 * exp(-pow((ad - width * 1.8) / (width * 0.6), 2.0)) * AurN(c2 * 3.0 + 5.0);
                    // curtains: folded rays along the oval, drifting, with bright knots — not even stripes
                    float fold = AurN(c2 * 9.0 + float2(at * 0.15, -at * 0.11) + ad * 18.0);
                    float rays = AurN(c2 * 16.0 + fold * 1.8 + float2(at * 0.4, ad * 30.0));
                    float patchy = smoothstep(0.25, 0.75, AurN(c2 * 2.5 + float2(-at * 0.05, at * 0.04)));
                    float curtain = (0.25 + 0.75 * rays * rays) * (0.3 + 0.7 * patchy);
                    float night = saturate(1.0 - sun * 1.8);
                    float aur = ring * curtain * night * _Aurora * saturate(column * 2.0 + 0.3);
                    col = col * (a / max(a + aur, 1e-4)) + _AuroraColor.rgb * 1.8 * (aur / max(a + aur, 1e-4));
                    a = saturate(a + aur);
                }
                return half4(col, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
