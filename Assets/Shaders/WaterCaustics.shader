// Caustics as PHYSICAL light focusing, not random texture.
//
// A wavy water surface acts as a field of lenses. Where the surface is concave it
// focuses sunlight into bright branching lines; where convex, it spreads light thin.
// We model a water "height" field h(x,y,t) (the same kind of domain-warped FBM that
// drives the screen distortion) and compute its focusing = -Laplacian(h). Concave
// regions (negative Laplacian) concentrate light → bright caustic web.
//
// Rendered as additive world-space quads by WaterCaustics.cs.

Shader "Custom/WaterCaustics"
{
    Properties
    {
        _Scale    ("Pattern Scale",  Float)    = 0.14
        _Speed    ("Animation Speed",Float)    = 0.18
        _Intensity("Brightness", Range(0,1.2)) = 0.30
        _Color    ("Light Tint",     Color)    = (0.95,1.0,0.85,1)
        _Sharpness("Caustic Sharpness", Range(1,8)) = 3.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" }
        ZWrite Off Cull Off
        Blend One One   // Additive — acts as a light layer
        Pass
        {
            Name "Caustics"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Scale, _Speed, _Intensity, _Sharpness;
                float4 _Color;
            CBUFFER_END
            // Biome-driven globals (pushed by PlanetaryEnvironment)
            float  _WaterTurbulence;
            float4 _GlobalFlowDirection;
            float  _CausticStrength;
            float4 _CausticColor;

            struct Attr { float4 pos : POSITION; };
            struct Vary  { float4 cs : SV_POSITION; float2 wp : TEXCOORD0; };

            Vary vert(Attr i)
            {
                Vary o;
                VertexPositionInputs v = GetVertexPositionInputs(i.pos.xyz);
                o.cs = v.positionCS;
                o.wp = v.positionWS.xy * _Scale;
                return o;
            }

            float hsh(float2 p){ return frac(sin(dot(p,float2(127.1,311.7)))*43758.5); }
            float vn(float2 p)
            {
                float2 i=floor(p),f=frac(p),u=f*f*(3-2*f);
                return lerp(lerp(hsh(i),hsh(i+float2(1,0)),u.x),
                            lerp(hsh(i+float2(0,1)),hsh(i+float2(1,1)),u.x),u.y);
            }
            float fbm(float2 p)
            {
                float v=vn(p)*0.55; p=p*2.1+float2(1.7,9.2);
                v+=vn(p)*0.30;      p=p*2.1+float2(4.1,2.7);
                v+=vn(p)*0.15;      return v;
            }

            // Water surface height at a point — domain-warped so the lens pattern
            // ripples and drifts organically rather than tiling.
            float WaterHeight(float2 p, float t, float2 flow)
            {
                float2 warp = float2(fbm(p*0.9 + t*0.20 + flow),
                                     fbm(p*0.9 + float2(5.2,1.3) - t*0.16 + flow));
                return fbm(p + warp*1.6 + t*0.12);
            }

            half4 frag(Vary i) : SV_Target
            {
                float2 wp = i.wp;
                float  t  = _Time.y * _Speed;
                float2 fl = _GlobalFlowDirection.xy * 0.04;

                // Finite-difference Laplacian of the height field = light focusing.
                float e  = 0.045;
                float hC = WaterHeight(wp,                  t, fl);
                float hL = WaterHeight(wp - float2(e,0),    t, fl);
                float hR = WaterHeight(wp + float2(e,0),    t, fl);
                float hD = WaterHeight(wp - float2(0,e),    t, fl);
                float hU = WaterHeight(wp + float2(0,e),    t, fl);
                float lap = (hL + hR + hD + hU - 4.0*hC) / (e*e);

                // Concave surface (lap < 0) focuses → bright. Sharpen into thin webs.
                float focus = saturate(-lap * 0.06);
                float caustic = pow(focus, _Sharpness);

                // A dim secondary tier fills broad areas with gentle shimmer
                float glow = pow(saturate(-lap * 0.03), _Sharpness * 0.5) * 0.25;

                // Fallback caustic tint/strength if no biome is driving them yet.
                float3 cColor = _CausticColor.rgb;
                float  cStr   = _CausticStrength;
                if (dot(cColor, 1.0.xxx) < 0.001) { cColor = float3(0.95,1.0,0.85); cStr = 0.7; }

                float strength = _Intensity * max(cStr, 0.02)
                               * (1.0 + saturate(_WaterTurbulence) * 0.4);

                float3 tint = _Color.rgb * cColor;
                float3 col  = tint * ((caustic + glow) * strength);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
