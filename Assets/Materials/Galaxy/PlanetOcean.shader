Shader "CLAY/PlanetOcean"
{
    // A translucent water shell that sits at sea level: deeper/darker at the centre of the disc, brighter
    // and more reflective toward the limb (Fresnel), with a sharp specular sun-glint on the day side. Sits
    // just above the seabed so land displaced above sea level pokes through it. Unlit CG so it works under
    // the URP 2D renderer. Set _SunDir per body each frame (world-space star→planet direction).
    Properties
    {
        _DeepColor("Deep Color", Color)     = (0.02, 0.13, 0.28, 1)
        _ShallowColor("Shallow Color", Color) = (0.10, 0.42, 0.60, 1)
        _SunDir("Sun Direction (world)", Vector) = (0, 0, -1, 0)
        _Fresnel("Fresnel Power", Range(0.5, 6)) = 2.5
        _SpecColor2("Specular Color", Color) = (1, 0.97, 0.9, 1)
        _SpecPower("Specular Power", Range(4, 400)) = 90
        _SpecGain("Specular Gain", Range(0, 4)) = 1.6
        _Opacity("Opacity", Range(0, 1)) = 0.82
        _SurfaceTex("Surface height (A)", 2D) = "black" {}
        _SeaLevel("Sea Level", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };

            half4 _DeepColor, _ShallowColor, _SpecColor2;
            float4 _SunDir, _CamPosObj;
            float _Fresnel, _SpecPower, _SpecGain, _Opacity, _SeaLevel;
            sampler2D _SurfaceTex;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = v.normal;                     // object-space normal
                o.v = v.vertex.xyz;                 // object-space position (view built in frag from _CamPosObj)
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.n);
                // Clip the ocean to real basins: sample the baked surface height at this direction and drop the
                // fragment where land rises above sea level. This makes the coastline follow the land map exactly
                // instead of z-fighting the displaced surface mesh (the "ocean jitter").
                float3 od = normalize(i.v);
                float2 suv = float2(atan2(od.z, od.x) * 0.15915494 + 0.5,
                                    0.5 - asin(clamp(od.y, -1.0, 1.0)) * 0.31830989);
                float land = tex2D(_SurfaceTex, suv).a;
                clip(_SeaLevel + 0.004 - land);                 // land (A > seaLevel) → discarded

                float3 V = normalize(_CamPosObj.xyz - i.v);    // object-space view direction (CPU cam pos)
                float3 Ld = normalize(_SunDir.xyz);            // object-space direction toward the star
                float ndl = saturate(dot(N, Ld));
                float night = smoothstep(-0.15, 0.25, dot(N, Ld));

                float fres = pow(1.0 - saturate(dot(N, V)), _Fresnel);
                half3 col = lerp(_DeepColor.rgb, _ShallowColor.rgb, fres);
                col *= (0.12 + 0.88 * ndl);                     // dark on the night side

                // Blinn specular glint (sun reflection on the water).
                float3 H = normalize(Ld + V);
                float spec = pow(saturate(dot(N, H)), _SpecPower) * _SpecGain * ndl;
                col += _SpecColor2.rgb * spec;

                float a = saturate(_Opacity * (0.45 + 0.55 * fres) * (0.35 + 0.65 * night) + spec);
                return half4(col, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
