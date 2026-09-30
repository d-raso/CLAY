Shader "CLAY/CellBody2D"
{
    // Scripted (HLSL) replacement for the BioSlime graph so every cell can look different: a
    // translucent body tinted by _Tint, mottled with procedural veins in _Tint2, a bright rim, and a
    // per-cell _Seed so no two share a pattern. 2D Sprite-Lit (reacts to Light2D). Structure mirrors
    // URP's Sprite-Lit-Default; only the fragment colour is procedural. Death fade via _BodyOpacity/_Dissolve.
    Properties
    {
        [HDR] _Tint("Body Tint", Color) = (0.4, 1, 0.5, 1)
        [HDR] _Tint2("Vein / Organelle Tint", Color) = (0.15, 0.5, 0.25, 1)
        [HDR] _RimColor("Rim Glow", Color) = (0.6, 1, 0.7, 1)
        _RimPower("Rim Sharpness", Range(0.5, 8)) = 2.5
        _RimStrength("Rim Strength", Range(0, 3)) = 1.2
        _NoiseScale("Vein Scale", Range(1, 12)) = 5
        _VeinContrast("Vein Contrast", Range(0, 2)) = 1
        [HDR] _SpotColor("Spot / Organelle Color", Color) = (1, 1, 1, 1)
        _SpotScale("Spot Scale", Range(2, 24)) = 12
        _SpotStrength("Spot Strength", Range(0, 1)) = 0
        _Seed("Per-cell Seed", Float) = 0
        _BodyOpacity("Body Opacity", Range(0, 1)) = 0.7
        _Dissolve("Dissolve", Range(0, 1)) = 0

        [MaterialToggle] _ZWrite("ZWrite", Float) = 0
        _Color("Tint (legacy)", Color) = (1,1,1,1)
        [HideInInspector] _MainTex("Diffuse", 2D) = "white" {}
        [HideInInspector] _MaskTex("Mask", 2D) = "white" {}
        [HideInInspector] _NormalMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        // ── Lit pass (Light2D) ──
        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex LitVertex
            #pragma fragment LitFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes { COMMON_2D_INPUTS half4 color : COLOR; UNITY_SKINNED_VERTEX_INPUTS };
            struct Varyings   { COMMON_2D_LIT_OUTPUTS half4 color : COLOR; };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half4 _Tint; half4 _Tint2; half4 _RimColor; half4 _SpotColor;
                float _RimPower; float _RimStrength; float _NoiseScale; float _VeinContrast;
                float _SpotScale; float _SpotStrength; float _Seed; float _BodyOpacity; float _Dissolve;
            CBUFFER_END

            // Procedural slime body (no textures) — reads the JellyMesh polar UVs (centre = 0.5,0.5).
            float hash21(float2 p){ p = frac(p * float2(123.34,345.45) + _Seed); p += dot(p, p + 34.345); return frac(p.x * p.y); }
            float vnoise(float2 p){ float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                float a=hash21(i), b=hash21(i+float2(1,0)), c=hash21(i+float2(0,1)), d=hash21(i+float2(1,1));
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y); }
            float fbm(float2 p){ float s=0, amp=0.5; [unroll] for(int o=0;o<4;o++){ s+=vnoise(p)*amp; p*=2; amp*=0.5; } return s; }
            void CellSurface(float2 uv, out half3 albedo, out half alpha, out half clipVal)
            {
                float r = saturate(length(uv - 0.5) * 2.0);
                float veins = saturate((fbm(uv * _NoiseScale + _Seed) - 0.5) * _VeinContrast + 0.5);
                half3 body = lerp(_Tint.rgb, _Tint2.rgb, veins);            // two-tone base
                float spot = smoothstep(0.55, 0.78, fbm(uv * _SpotScale + _Seed * 1.7 + 5.3)) * _SpotStrength;
                body = lerp(body, _SpotColor.rgb, spot);                    // organelle spots (a 3rd colour)
                albedo = body + _RimColor.rgb * (pow(saturate(r), _RimPower) * _RimStrength);
                alpha  = (1.0 - smoothstep(0.92, 1.0, r)) * _BodyOpacity * lerp(0.8, 1.0, veins);
                clipVal = (veins * 0.6 + (1.0 - r) * 0.4) - _Dissolve;
            }

            Varyings LitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                half3 albedo; half alpha; half clipVal;
                CellSurface(input.uv, albedo, alpha, clipVal);
                clip(clipVal);
                half4 main = input.color * half4(albedo, alpha);
                SurfaceData2D surfaceData; InputData2D inputData;
                InitializeSurfaceData(main.rgb, main.a, half4(1,1,1,1), half3(0,0,1), surfaceData);
                InitializeInputData(input.uv, input.lightingUV, inputData);
                return CombinedShapeLightShared(surfaceData, inputData);
            }
            ENDHLSL
        }

        // ── Normals pass (feeds the 2D light normal buffer) ──
        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes { COMMON_2D_NORMALS_INPUTS float4 color : COLOR; UNITY_SKINNED_VERTEX_INPUTS };
            struct Varyings   { COMMON_2D_NORMALS_OUTPUTS half4 color : COLOR; };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half4 _Tint; half4 _Tint2; half4 _RimColor; half4 _SpotColor;
                float _RimPower; float _RimStrength; float _NoiseScale; float _VeinContrast;
                float _SpotScale; float _SpotStrength; float _Seed; float _BodyOpacity; float _Dissolve;
            CBUFFER_END

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }
            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
