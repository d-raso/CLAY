Shader "CLAY/PlanetSurface"
{
    // Planet surface shaded PER-PIXEL from a baked equirectangular texture (RGB = albedo, A = height), so
    // colour and relief no longer depend on mesh vertex density. The height channel drives a per-pixel bump
    // normal. A day/night terminator (from a script-supplied sun direction) and a faint rim complete it.
    // Unlit CGPROGRAM so it renders under the project's URP 2D Renderer. Stars set _UseTex = 0 and use
    // _DayColor + _Emission instead. Sample uses OBJECT-space direction so the map spins with the planet.
    Properties
    {
        _SurfaceTex("Surface (RGB albedo, A height)", 2D) = "gray" {}
        _DayColor("Day / Star Color", Color) = (0.6, 0.7, 0.9, 1)
        _NightColor("Night Color", Color) = (0.02, 0.03, 0.06, 1)
        _SunDir("Sun Direction (world)", Vector) = (0, 0, -1, 0)
        _Sun1Col("Main Sun Tint", Color) = (1, 1, 1, 1)
        _Sun2Dir("Second Sun Dir (obj, w = strength)", Vector) = (0, 0, 0, 0)
        _Sun2Col("Second Sun Tint", Color) = (1, 1, 1, 1)
        _Ambient("Ambient", Range(0,1)) = 0.06
        _RimColor("Rim Color", Color) = (0.4, 0.55, 0.8, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _Emission("Emission (stars)", Range(0,4)) = 0
        _UseTex("Use Surface Texture", Float) = 0
        _BumpScale("Bump Scale", Range(0, 60)) = 16
        _CamFill("Camera Fill (night visibility)", Range(0, 0.6)) = 0.22
        _TexelU("Texel U", Float) = 0.00048828
        _TexelV("Texel V", Float) = 0.00097656
        _HasOcean("Has Ocean", Float) = 0
        _SeaLevel("Sea Level", Float) = 0.5
        _OceanSpecPower("Ocean Spec Power", Float) = 200
        _OceanSpecGain("Ocean Spec Gain", Float) = 0.7
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

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            // Everything lighting-related is carried in OBJECT space: the object-space surface point (od) and
            // the object-space view direction (ov). The star direction is supplied in object space too
            // (_SunDir). This sidesteps UnityObjectToWorldNormal entirely — at the tiny object scales used for
            // distant planets its inverse-transpose matrix loses precision and collapses the world normals to a
            // near-constant, which flattened the terminator and made zoomed-in planets look unlit.
            struct v2f { float4 pos : SV_POSITION; float3 od : TEXCOORD0; };

            sampler2D _SurfaceTex;
            half4 _DayColor, _NightColor, _RimColor;
            float4 _SunDir, _Sun2Dir;
            half4 _Sun1Col, _Sun2Col;
            float _Ambient, _RimPower, _Emission, _UseTex, _BumpScale, _CamFill, _TexelU, _TexelV;
            float _HasOcean, _SeaLevel, _OceanSpecPower, _OceanSpecGain;
            float4 _CamPosObj;   // camera position in this object's local space (supplied by the CPU)

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.od = v.vertex.xyz;                       // object-space surface point
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 od = normalize(i.od);
                float3 V  = normalize(_CamPosObj.xyz - i.od);   // object-space view direction (CPU cam pos)
                float3 L  = normalize(_SunDir.xyz);   // object-space direction TOWARD the star (lit side)
                half3 albedo;
                float3 nObj;                          // object-space shading normal
                float isWater = 0.0;

                if (_UseTex > 0.5)
                {
                    // equirectangular lookup from the surface direction
                    float2 uv = float2(atan2(od.z, od.x) * 0.15915494 + 0.5,
                                       0.5 - asin(clamp(od.y, -1.0, 1.0)) * 0.31830989);
                    // The longitude UV wraps 1→0 at ±180°, spiking the derivative and collapsing mips into a
                    // visible seam. Unwrap the U gradient and sample with explicit gradients to kill the seam.
                    float2 dx = ddx(uv), dy = ddy(uv);
                    dx.x = frac(dx.x + 0.5) - 0.5;
                    dy.x = frac(dy.x + 0.5) - 0.5;
                    float4 s = tex2Dgrad(_SurfaceTex, uv, dx, dy);
                    albedo = s.rgb;

                    // per-pixel bump from the height channel (object-space tangent frame on the sphere)
                    float h0 = s.a;
                    float hu = tex2Dgrad(_SurfaceTex, uv + float2(_TexelU, 0), dx, dy).a;
                    float hv = tex2Dgrad(_SurfaceTex, uv + float2(0, _TexelV), dx, dy).a;
                    float3 T = normalize(cross(od, float3(0, 1, 0)) + float3(1e-4, 0, 0));
                    float3 B = cross(od, T);
                    nObj = normalize(od - _BumpScale * ((hu - h0) * T + (hv - h0) * B));

                    // Water texels store height == sea level; flatten them (no seabed bump) → smooth sea surface.
                    isWater = (_HasOcean > 0.5 && h0 <= _SeaLevel + 0.006) ? 1.0 : 0.0;
                    if (isWater > 0.5) nObj = od;
                }
                else { albedo = _DayColor.rgb; nObj = od; }

                // Lambert day/night done ENTIRELY in object space — normal, light and view are all object-space
                // unit vectors, so the terminator is identical at any planet scale (no world-normal transform).
                float ndl = saturate(dot(nObj, L));
                // Dark night side: only a small fraction of the ambient lifts the unlit hemisphere, and the night
                // colour itself is very dark, so the shadowed side reads as genuinely dark (the silhouette still
                // reads thanks to the faint limb halo below).
                float lit = saturate(_Ambient * 0.35 + (1.0 - _Ambient * 0.35) * ndl);
                half3 night = _UseTex > 0.5 ? albedo * 0.02 : _NightColor.rgb;
                half3 col = lerp(night, albedo * _Sun1Col.rgb, lit);
                // second star of a multiple system: its own terminator, tinted by its colour
                float ndl2 = 0.0;
                if (_Sun2Dir.w > 0.0005)
                {
                    ndl2 = saturate(dot(nObj, normalize(_Sun2Dir.xyz))) * _Sun2Dir.w;
                    col += albedo * _Sun2Col.rgb * ndl2;
                }

                // Ocean sun-glint + fresnel sheen on water texels (replaces the old separate ocean shell).
                if (isWater > 0.5)
                {
                    float3 H = normalize(L + V);
                    float glint = pow(saturate(dot(od, H)), _OceanSpecPower) * _OceanSpecGain * ndl;
                    float fres = pow(1.0 - saturate(dot(od, V)), 3.0);
                    col += half3(1.0, 0.97, 0.9) * glint;
                    col += albedo * fres * 0.35 * ndl;
                }

                // Sunlit limb glow (object-space fresnel), only on the day side.
                float rim = pow(1.0 - saturate(dot(nObj, V)), _RimPower);
                col += _RimColor.rgb * rim * 0.4 * saturate(ndl + ndl2);
                // A faint full-limb halo (both hemispheres) so the planet's silhouette edge stays visible against
                // the starfield — the night-side occlusion then reads as a body's edge, not a hole in space.
                col += _RimColor.rgb * rim * rim * 0.05;
                col += _DayColor.rgb * _Emission;
                // Gently desaturate for a more natural, less candy-coloured look.
                float g = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(half3(g, g, g), col, 0.86);
                return half4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
