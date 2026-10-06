Shader "CLAY/CellStage/Blob"
{
    // Protocell bubbles, one batch. A lipid vesicle is nearly invisible in water: a faint, soft, slightly brighter
    // membrane with a subtle soap-film tint where it's thinnest and a gentle highlight on the lit side — no hard
    // outlines or banded gradients. Everything the player needs is still on the membrane:
    //   uv1 = (strain, glow, wrinkle, pinch)   uv2 = (seed, thickness, burst flash, selected)
    // The quad spans ±2.3 cell radii so the replicator halo fades out well before the edge.
    Properties { _Day ("Daylight", Float) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Day;
            float _CellIllum;
            struct appdata { float4 vertex : POSITION; float3 uv0 : TEXCOORD0; float4 p1 : TEXCOORD1; float4 p2 : TEXCOORD2; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; float4 p1 : TEXCOORD1; float4 p2 : TEXCOORD2; float4 color : COLOR; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv0; o.p1 = v.p1; o.p2 = v.p2; o.color = v.color; return o; }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv.xy;
                float strain = i.p1.x, glow = i.p1.y, wrinkle = i.p1.z, pinch = i.p1.w;
                float seed = i.p2.x, thick = i.p2.y, flash = i.p2.z, sel = i.p2.w;
                float t = _Time.y;

                float off = pinch * 0.55;
                float rl = 1.0 - pinch * 0.28;
                float ang = atan2(p.y, p.x);
                float wob = 0.03 * sin(ang * 3.0 + t * 0.7 + seed) + 0.02 * sin(ang * 5.0 - t * 1.1 + seed * 2.0)
                          + strain * 0.02 * sin(ang * 23.0 + t * 9.0)
                          + wrinkle * 0.05 * sin(ang * 13.0 + seed * 3.0);
                float d1 = length(p - float2(off, 0)) / (rl + wob) - 1.0;
                float d2 = length(p + float2(off, 0)) / (rl + wob) - 1.0;
                float k = 0.25 * (1.0 - pinch * 0.8) + 0.02;
                float hh = saturate(0.5 + 0.5 * (d2 - d1) / k);
                float d = lerp(d2, d1, hh) - k * hh * (1.0 - hh);
                float r = length(p);

                // soft membrane: a gaussian-ish ridge (no hard edges), slightly thicker when relaxed, thinner when strained
                float w = 0.045 + thick * 0.03 - strain * 0.015;
                float memb = exp(-(d * d) / (w * w));
                // a vesicle is mostly transparent: faint body + soft ridge
                float inside = saturate(-d * 6.0);
                float3 tint = i.color.rgb;
                float3 film = 0.5 + 0.5 * cos(6.2831 * (thick * 1.8 - strain * 0.6 + ang * 0.05 + float3(0.0, 0.33, 0.67)));
                float3 membC = lerp(tint, film, 0.22 + strain * 0.25) * 1.1 + 0.08;
                float litSide = saturate(dot(normalize(p + 1e-4), normalize(float2(-0.6, 0.8))));
                membC *= 0.75 + 0.35 * litSide;
                float hi = pow(litSide, 10.0) * memb * 0.35;                               // soft highlight, not a glint

                float pulse = 0.65 + 0.35 * sin(t * 2.6 + seed);
                float3 col = tint * 0.35;
                float a = inside * (0.07 + glow * 0.12 * pulse);
                col = lerp(col, membC, memb);
                a = max(a, memb * (0.55 - wrinkle * 0.15));
                col += hi; a = saturate(a + hi * 0.5);
                if (_CellIllum > 0.5 && _CellIllum < 1.5) { col += membC * memb * 0.8; a = max(a, memb * 0.8); }      // darkfield: a ring of light
                if (_CellIllum > 1.5) { float ph = exp(-pow((d - 0.12) / 0.05, 2.0)) * (1.0 - inside); col = lerp(col, float3(1, 1, 1), ph * 0.6); a = max(a, ph * 0.5); }   // phase halo
                // replicator glow: warm light from inside + a soft halo fading before the quad edge
                col += glow * pulse * float3(1.0, 0.9, 0.65) * 0.3 * inside;
                float halo = glow * exp(-max(d, 0.0) * 3.5) * (1.0 - inside) * pulse * 0.35 * (1.0 - smoothstep(1.5, 2.2, r));
                col = lerp(col, float3(1.0, 0.92, 0.65), halo / max(a + halo, 1e-3));
                a = saturate(a + halo);
                // respawn picker: a soft pulsing ring
                float selRing = sel * exp(-pow((d - 0.22 - 0.04 * sin(t * 4.0)) / 0.035, 2.0));
                col = lerp(col, float3(1, 1, 1), selRing * 0.9); a = max(a, selRing * 0.8);
                if (flash > 0.0)
                {
                    float fr = exp(-pow((r - (1.0 + (1.0 - flash) * 0.8)) / 0.05, 2.0)) * step(0.4, frac(ang * 3.0 + seed));
                    col = lerp(col, membC + 0.2, fr); a = max(a * flash, fr * flash * 0.8);
                }
                col *= lerp(0.35, 1.0, _Day) + glow * 0.3;
                return fixed4(col, a * i.color.a);
            }
            ENDCG
        }
    }
}
