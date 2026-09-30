Shader "Custom/WorldBackground"
{
    Properties
    {
        _Scale    ("World Scale",   Float)      = 0.08
        _Speed    ("Anim Speed",    Float)      = 0.015
        _Brightness("Brightness",   Float)      = 1.0
        _GlowStr  ("Glow",  Range(0,1))        = 0.40
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Background" }
        ZWrite Off ZTest LEqual Cull Off
        Pass
        {
            Name "WorldBG"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Scale, _Speed, _Brightness, _GlowStr;
            CBUFFER_END

            // Biome palette + state (pushed by PlanetaryEnvironment)
            float4 _BiomeDeepColor;     // darkest tone (depths)
            float4 _BiomeMidColor;      // base water tone
            float4 _BiomeShallowColor;  // brightest tone (light pools)
            float  _BiomeMurkiness;     // 0 clear .. 1 turbid
            float  _WaterTurbulence, _NutrientDensity, _WaterTemperature;

            struct Attr { float4 pos : POSITION; };
            struct Vary  { float4 cs : SV_POSITION; float2 wp : TEXCOORD0; float2 raw : TEXCOORD1; };

            Vary vert(Attr i)
            {
                Vary o;
                VertexPositionInputs v = GetVertexPositionInputs(i.pos.xyz);
                o.cs  = v.positionCS;
                o.raw = v.positionWS.xy;
                o.wp  = v.positionWS.xy * _Scale;
                return o;
            }

            float h(float2 p){ return frac(sin(dot(p,float2(127.1,311.7)))*43758.5); }
            float vn(float2 p)
            {
                float2 i=floor(p), f=frac(p), u=f*f*(3-2*f);
                return lerp(lerp(h(i),       h(i+float2(1,0)), u.x),
                            lerp(h(i+float2(0,1)), h(i+float2(1,1)), u.x), u.y);
            }
            float fbm(float2 p, int o)
            {
                float v=0, a=0.5;
                UNITY_UNROLL for(int k=0;k<3;k++)
                { v += (k<o ? vn(p)*a : 0); p=p*2.1+float2(1.7,9.2); a*=0.5; }
                return v;
            }

            float4 frag(Vary i) : SV_Target
            {
                float2 wp  = i.wp;
                float  t   = _Time.y * _Speed;

                // Biome palette with a built-in fallback (so the world is never black
                // if no PlanetaryEnvironment is pushing biome globals yet).
                float3 deepC = _BiomeDeepColor.rgb;
                float3 midC  = _BiomeMidColor.rgb;
                float3 shalC = _BiomeShallowColor.rgb;
                float  murk  = _BiomeMurkiness;
                if (dot(midC, 1.0.xxx) < 0.001)
                {
                    deepC = float3(0.04, 0.16, 0.20);
                    midC  = float3(0.10, 0.40, 0.42);
                    shalC = float3(0.38, 0.78, 0.70);
                    murk  = 0.25;
                }

                // ── Layered blob structure (3 spatial scales) ─────────────────
                // Domain-warped flow gives everything an organic, drifting motion.
                float2 fw = float2(fbm(wp*0.80 + t*0.020, 3),
                                   fbm(wp*0.80 + float2(3.7,1.2) + t*0.016, 3));

                // LARGE blobs — slow, broad regions of light vs depth
                float big   = fbm(wp*0.35 + fw*1.5 + t*0.010, 3);
                // MEDIUM blobs — the main "stuff floating in the water" structure
                float med   = fbm(wp*1.10 + fw*2.0 + t*0.014, 3);
                // FINE detail — texture/grain
                float fine  = fbm(wp*3.20 + fw*0.6 + t*0.022, 2);

                // ── Map blob fields onto the biome's 3-stop palette ───────────
                // big drives the deep→mid→shallow depth feel; med adds shallow blobs.
                float depth = saturate(big * 1.3 - 0.15);
                float3 col  = lerp(deepC, midC, smoothstep(0.0, 0.55, depth));
                col = lerp(col, shalC, smoothstep(0.45, 1.0, depth) * 0.85);

                // Medium blobs lighten/darken locally for body
                col *= (0.80 + med * 0.55);
                // Fine grain
                col *= (0.92 + fine * 0.16);

                // Bioluminescent wisps at the brightest blob peaks
                float wisp = smoothstep(0.74, 0.92, med);
                col += shalC * wisp * 0.35;

                // ── Murkiness: fade contrast toward a flat turbid haze ────────
                // Turbid water loses contrast and tends toward a muddy mid tone.
                float3 murkTone = midC * 0.65 + deepC * 0.35;
                col = lerp(col, murkTone, murk * 0.6);
                // Murk also flattens the brightness range
                col = lerp(col, (col.r+col.g+col.b)*0.333 * murkTone * 2.0, murk * 0.15);

                // ── Subtle environment cues ───────────────────────────────────
                col += _WaterTurbulence * 0.015;
                col = lerp(col, col * float3(1.15,1.12,0.75), _NutrientDensity * 0.15);

                return float4(saturate(col)*_Brightness, 1);
            }
            ENDHLSL
        }
    }
}
