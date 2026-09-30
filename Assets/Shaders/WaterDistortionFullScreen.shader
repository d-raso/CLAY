// Full-screen water distortion for FullScreenPassRendererFeature (URP 14+).
// Does NOT include Blit.hlsl — uses a self-contained fullscreen triangle vertex shader
// to avoid sampler/struct compatibility issues across Unity 6 / URP versions.
//
// SETUP:
//   1. Select your URP Renderer Data asset
//   2. Add Renderer Feature -> "Full Screen Pass Renderer Feature"
//   3. Create a Material using this shader (Custom/WaterDistortionFullScreen)
//   4. Assign that material to the feature's "Pass Material" field
//   5. Set Injection Point to "After Rendering Transparents"

Shader "Custom/WaterDistortionFullScreen"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "WaterDistortionFS"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // FullScreenPassRendererFeature provides _BlitTexture.
            // Declaring as TEXTURE2D (not TEXTURE2D_X) works for non-XR 2D games.
            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            // Set globally by WaterDistortionCamera each frame
            float _DistortionStrength;
            float _ChromaticAberration;
            float _NoiseSpeed;
            float _NoiseScale;

            // Set by FlowFieldManager, PlanetaryEnvironment, CellRippleSystem
            float4 _GlobalFlowDirection;
            float  _WaterTemperature;
            float  _WaterTurbulence;
            float4 _Ripple0, _Ripple1, _Ripple2, _Ripple3;

            // Cell proximity mask (u, v, radiusUV, featherUV) + min distortion at center
            float4 _CellDistortMask;
            float  _CellMinDistort;

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 texcoord : TEXCOORD0; };

            // Full-screen triangle: three verts cover the entire clip-space quad
            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Generates UVs [0,1] covering the screen via a single oversized triangle
                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
                #endif
                output.texcoord = uv;
                return output;
            }

            // ── Noise ────────────────────────────────────────────────────────
            float h(float2 p){ return frac(sin(dot(p,float2(127.1,311.7)))*43758.5); }
            float vn(float2 p)
            {
                float2 i=floor(p),f=frac(p),u=f*f*(3-2*f);
                return lerp(lerp(h(i),h(i+float2(1,0)),u.x),
                            lerp(h(i+float2(0,1)),h(i+float2(1,1)),u.x),u.y);
            }
            float2 NoiseVec(float2 p)
            {
                float2 v  = float2(vn(p), vn(p+float2(5.2,1.3))) * 0.6;
                float2 p2 = p*2.1 + float2(1.7,9.2);
                       v += float2(vn(p2), vn(p2+float2(3.7,7.1))) * 0.3;
                return v * 2.0 - 1.0;
            }

            // ── Cell ripple ring ─────────────────────────────────────────────
            // A single smooth expanding crest (no sin oscillation → no jitter),
            // with mild angular variation so it isn't a sterile perfect circle.
            float2 RippleOff(float2 uv, float4 rp)
            {
                if (rp.w <= 0.001) return (float2)0;
                float asp  = _ScreenParams.x / _ScreenParams.y;
                float2 d   = float2((uv.x - rp.x) * asp, uv.y - rp.y);
                float  dist = length(d);
                if (dist < 0.0001) return (float2)0;
                float band  = dist - rp.z;                 // signed dist from ring
                float crest = exp(-band * band * 90.0);    // one soft gaussian ring
                float ang   = atan2(d.y, d.x);
                crest *= 0.82 + 0.18 * sin(ang * 4.0 + rp.z * 18.0); // gentle wobble
                return normalize(d) * crest * rp.w;
            }

            // ── Fragment ─────────────────────────────────────────────────────
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv  = input.texcoord;
                float  t   = _Time.y * _NoiseSpeed;
                float2 fl  = _GlobalFlowDirection.xy * 0.05;

                float2 noise = NoiseVec(uv * _NoiseScale + float2(t*0.25, t*0.16) + fl);

                float tmp    = saturate((_WaterTemperature + 15.0) / 75.0);
                float shimmer = sin(uv.y * 32.0 + t * 5.0)
                              * sin(uv.x * 18.0 + t * 3.6) * tmp;

                float str = _DistortionStrength * (1.0 + saturate(_WaterTurbulence) * 0.8);
                float2 off = noise * str + float2(shimmer * str * 0.35, 0);

                // ── Cell proximity mask ───────────────────────────────────────
                // The cell is closest to the viewer → least water in front of it →
                // least lensing. Fade the open-water distortion down to _CellMinDistort
                // inside the cell's screen footprint so it doesn't elongate.
                float asp   = _ScreenParams.x / _ScreenParams.y;
                float2 cd   = float2((uv.x - _CellDistortMask.x) * asp, uv.y - _CellDistortMask.y);
                float  cdist = length(cd);
                float  mask = smoothstep(_CellDistortMask.z,
                                         _CellDistortMask.z + _CellDistortMask.w, cdist);
                float  distortScale = lerp(_CellMinDistort, 1.0, mask);
                off *= distortScale;

                // Ripples are the cell's own wake — keep them but also softened near center
                float ripStr = (str + 0.008) * distortScale;
                off += RippleOff(uv, _Ripple0) * ripStr;
                off += RippleOff(uv, _Ripple1) * ripStr;
                off += RippleOff(uv, _Ripple2) * ripStr;
                off += RippleOff(uv, _Ripple3) * ripStr;

                float  ca    = _ChromaticAberration * distortScale;
                float2 caDir = length(off) > 0.001 ? normalize(off)*ca : float2(ca, 0);

                float r = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv+off+caDir).r;
                float g = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv+off      ).g;
                float b = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv+off-caDir).b;
                return float4(r, g, b, 1);
            }
            ENDHLSL
        }
    }
}
