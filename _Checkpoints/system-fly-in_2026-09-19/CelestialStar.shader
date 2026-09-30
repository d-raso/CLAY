Shader "Clay/CelestialStar"
{
    // A camera-following celestial sphere of STARS rendered as point geometry (not a baked image). Each vertex is a
    // sky DIRECTION; the mesh transform tracks the camera position so stars never parallax (= infinite distance).
    // Points are billboarded to a CONSTANT SCREEN SIZE, so narrowing the FOV (a telescope) spreads them apart and
    // resolves close pairs with no pixelation. _MagLimit culls stars fainter than the current aperture reveals.
    Properties
    {
        _PointSize ("Point Size (px)", Float) = 2.0
        _MagLimit  ("Magnitude Limit (min brightness)", Float) = 0.0
        _Gain      ("Brightness Gain", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "CelestialStars"
            Tags { "LightMode"="UniversalForward" }
            Blend One One          // additive (transparent path → honored by the URP 2D renderer)
            ZWrite Off
            ZTest LEqual           // at radius R → occluded by nearer system geometry, fills empty sky
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PointSize, _MagLimit, _Gain;
            CBUFFER_END

            struct Attributes
            {
                float3 dir : POSITION;      // sky direction (unit); scaled by the transform radius
                float2 corner : TEXCOORD0;  // quad corner in [-1,1]
                float4 color : COLOR;       // rgb = colour × apparent brightness (may exceed 1)
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.dir);          // transform sits at the camera → cam + dir*R
                float4 clip = TransformWorldToHClip(wp);
                // Per-star size (from vertex colour alpha): bright stars are bigger → real dynamic range, not grain.
                float sz = _PointSize * max(IN.color.a, 0.05);
                float2 ndc = IN.corner * sz * 2.0 / _ScreenParams.xy;
                clip.xy += ndc * clip.w;
                OUT.positionCS = clip;
                OUT.color = IN.color;
                OUT.uv = IN.corner;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float m = max(IN.color.r, max(IN.color.g, IN.color.b));
                if (m < _MagLimit) discard;                          // aperture / magnitude cutoff
                float d = saturate(1.0 - dot(IN.uv, IN.uv));         // soft round point
                return half4(IN.color.rgb * (_Gain * d), 0.0);       // additive → alpha unused; keep it clean
            }
            ENDHLSL
        }
    }
    Fallback Off
}
