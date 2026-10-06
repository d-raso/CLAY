Shader "CLAY/SurfaceGrass"
{
    // Instanced grass clumps on the planet surface. Per-instance tint (the ground's own colour, so alien pigments carry
    // through); per-vertex: uv.y = height along the blade (0 base → 1 tip), colour.r = blade tone. Wind sway grows with
    // height², blades shorten toward the edge of the grass radius (no popping), two-sided with backlit translucency,
    // darker bases (self-shadowed sward), URP main-light shadows, SH ambient, fog and horizon curvature.
    Properties
    {
        _FadeStart("Fade start (m)", Float) = 35
        _FadeEnd("Fade end (m)", Float) = 60
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ FOG_EXP
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Include/SurfAmbient.hlsl"
            #include "Include/Clouds.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _FadeStart, _FadeEnd;
            CBUFFER_END
            float _CurvK;
            float4 _GrassWind;
            float4 _SurfOrigin; float _SnowCover;   // xy = wind direction (xz), z = strength, w = gust frequency

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float hf : TEXCOORD2; float tone : TEXCOORD3; float fog : TEXCOORD4; float3 tint : TEXCOORD5;
            };

            V vert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float hf = v.uv.y;
                float3 root = TransformObjectToWorld(float3(0, 0, 0));
                float dist = distance(root.xz, _WorldSpaceCameraPos.xz);
                // shrink toward the edge of the grass radius so clumps never pop in or out
                float fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, dist);
                float3 pos = v.positionOS.xyz;
                pos.y *= fade;
                float3 ws = TransformObjectToWorld(pos);
                // wind: a travelling gust wave plus fast flutter, bending the blade by height²
                float2 wd = _GrassWind.xy;
                float phase = dot(root.xz, wd) * 0.35 - _Time.y * _GrassWind.w;
                float gust = sin(phase) * 0.5 + 0.5;
                float flutter = sin(_Time.y * 5.3 + root.x * 3.1 + root.z * 2.7 + hf * 2.0) * 0.25;
                float bend = hf * hf * _GrassWind.z * (0.35 + gust * 0.65 + flutter);
                ws.xz += wd * bend * 0.35;
                ws.y -= bend * bend * 0.04;
                float2 d = ws.xz - _WorldSpaceCameraPos.xz; ws.y -= dot(d, d) * _CurvK;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.hf = hf; o.tone = v.color.r;
                o.tint = pow(max(UNITY_ACCESS_INSTANCED_PROP(Props, _Tint).rgb, 0.0), 2.2);   // sRGB ground colour → linear
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                if (!IS_FRONT_VFACE(facing, true, false)) N = -N;
                N = normalize(N + float3(0, 0.6, 0));                    // soft, sward-like shading (blades point up)
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);

                // colour: the ground's own vegetation colour, darker and richer at the base, paler toward the tips
                float3 alb = i.tint * lerp(0.55, 1.15, i.hf) * (0.8 + i.tone * 0.4);
                alb = lerp(alb, alb * float3(1.15, 1.08, 0.75), smoothstep(0.75, 1.0, i.hf) * 0.35);   // sun-cured tips

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 wp = i.positionWS + float3(_SurfOrigin.x, 0, _SurfOrigin.y);
                float3 lit = L.color * L.shadowAttenuation * L.distanceAttenuation * CloudShadow(wp, L.direction);
                alb = lerp(alb, pow(float3(0.92, 0.94, 0.97), 2.2), saturate(_SnowCover * 1.5 - 0.3) * i.hf);   // snow on the tips
                float ndl = saturate((dot(N, L.direction) + 0.3) / 1.3);
                float back = pow(saturate(dot(V, -L.direction)), 3.0) * (0.3 + 0.7 * i.hf);   // light through the blades
                float ao = lerp(0.35, 1.0, i.hf);                                              // the sward shades its own base
                float3 H = normalize(L.direction + V);
                float spec = pow(saturate(dot(N, H)), 24.0) * 0.12 * i.hf;                     // waxy sheen
                float3 col = alb * (SurfSH(N) * ao + lit * (ndl + back * 0.8)) + lit * spec;
                col = MixFog(col, i.fog);
                col = (any(isnan(col)) || any(isinf(col))) ? float3(0, 0, 0) : max(col, 0);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex dv
            #pragma fragment df
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _FadeStart, _FadeEnd;
            CBUFFER_END
            float _CurvK; float4 _GrassWind;
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V dv(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                float hf = v.uv.y;
                float3 root = TransformObjectToWorld(float3(0, 0, 0));
                float fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, distance(root.xz, _WorldSpaceCameraPos.xz));
                float3 pos = v.positionOS.xyz; pos.y *= fade;
                float3 ws = TransformObjectToWorld(pos);
                float phase = dot(root.xz, _GrassWind.xy) * 0.35 - _Time.y * _GrassWind.w;
                float bend = hf * hf * _GrassWind.z * (0.35 + (sin(phase) * 0.5 + 0.5) * 0.65 + sin(_Time.y * 5.3 + root.x * 3.1 + root.z * 2.7 + hf * 2.0) * 0.25);
                ws.xz += _GrassWind.xy * bend * 0.35; ws.y -= bend * bend * 0.04;
                float2 d = ws.xz - _WorldSpaceCameraPos.xz; ws.y -= dot(d, d) * _CurvK;
                o.positionCS = TransformWorldToHClip(ws); return o;
            }
            half4 df(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
