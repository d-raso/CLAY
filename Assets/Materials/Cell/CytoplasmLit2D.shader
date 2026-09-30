Shader "CLAY/CytoplasmLit2D"
{
    // A URP 2D Sprite-Lit material (reacts to Light2D via normal map + mask) with a dissolve channel.
    // This is the project's "2D-PBR" cytoplasm standard: albedo + normal relief + emissive dissolve.
    // Built on URP 12's Sprite-Lit-Default so the Light2D path is identical to Unity's own.
    Properties
    {
        [MainTexture] _MainTex("Cytoplasm Albedo", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map (relief)", 2D) = "bump" {}

        [Header(Dissolve)]
        _DissolveTex("Dissolve Noise (R)", 2D) = "black" {}
        _Dissolve("Dissolve Amount", Range(0,1)) = 0
        _EdgeWidth("Dissolve Edge Width", Range(0.001, 0.4)) = 0.08
        [HDR] _EdgeColor("Dissolve Edge Color", Color) = (0.5, 2.2, 1.2, 1)

        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        // Legacy fallback props (match Sprite-Lit-Default).
        _Color("Tint", Color) = (1,1,1,1)
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

        // ── Lit pass (Light2D) ───────────────────────────────────────────────────────────────
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

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color        : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            TEXTURE2D(_DissolveTex);  SAMPLER(sampler_DissolveTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _EdgeColor;
                float _Dissolve;
                float _EdgeWidth;
            CBUFFER_END

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
                half noise = SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, input.uv).r;
                clip(noise - _Dissolve);                  // erode away the dissolved cytoplasm
                half4 col = CommonLitFragment(input, input.color);
                // Bright wet rim at the dissolve front.
                half edge = 1.0 - smoothstep(_Dissolve, _Dissolve + _EdgeWidth, noise);
                col.rgb += _EdgeColor.rgb * edge * step(0.0001, _Dissolve);
                return col;
            }
            ENDHLSL
        }

        // ── Normals pass (feeds the 2D light normal buffer) ──────────────────────────────────
        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4   color           : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            TEXTURE2D(_DissolveTex);  SAMPLER(sampler_DissolveTex);

            CBUFFER_START( UnityPerMaterial )
                half4 _Color;
                half4 _EdgeColor;
                float _Dissolve;
                float _EdgeWidth;
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
                half noise = SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, input.uv).r;
                clip(noise - _Dissolve);
                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }

        // ── Unlit fallback (non-2D renderer / no lights) ─────────────────────────────────────
        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            TEXTURE2D(_DissolveTex);  SAMPLER(sampler_DissolveTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _EdgeColor;
                float _Dissolve;
                float _EdgeWidth;
            CBUFFER_END

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                Varyings o = CommonUnlitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half noise = SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, input.uv).r;
                clip(noise - _Dissolve);
                half4 col = CommonUnlitFragment(input, input.color);
                half edge = 1.0 - smoothstep(_Dissolve, _Dissolve + _EdgeWidth, noise);
                col.rgb += _EdgeColor.rgb * edge * step(0.0001, _Dissolve);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback "Sprite-Lit-Default"
}
