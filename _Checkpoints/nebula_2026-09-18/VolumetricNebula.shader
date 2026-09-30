Shader "Clay/VolumetricNebula"
{
    // Raymarched volumetric nebula in a bounding box. Marches a procedural 3D density field (domain-warped fBm +
    // ridged detail), light-marches toward a central star for self-shadowing (→ dark pillars / real depth), and
    // accumulates emission + illuminated scattering with Beer-Lambert transmittance. A NORMAL material on a mesh
    // (not DOTS/BRG), so the camera position is available — meant for hero close-up nebulae, separate from the
    // instanced galaxy dust/nebulae.
    Properties
    {
        _Emission   ("Emission (outer gas) Color", Color) = (1.0, 0.30, 0.55, 1)
        _Emission2  ("Emission (hot core) Color", Color) = (0.25, 0.85, 0.8, 1)
        _Emission3  ("Emission (secondary gas) Color", Color) = (0.55, 0.35, 0.95, 1)
        _LightColor ("Star Light Color", Color) = (1.0, 0.85, 0.7, 1)
        _Shadowed   ("Dust Albedo (brown)", Color) = (0.16, 0.10, 0.07, 1)
        _LightPos   ("Star Pos (object space)", Vector) = (0, 0, 0, 0)
        _Freq       ("Base Frequency", Float) = 2.5
        _Warp       ("Domain Warp", Float) = 0.4
        _DensityMul ("Density", Float) = 6.0
        _Threshold  ("Coverage Threshold", Range(0,1)) = 0.35
        _Absorb     ("Absorption", Float) = 6.0
        _ShadowStrength ("Shadow Strength", Float) = 3.0
        _EmissionMul("Emission Strength", Float) = 1.2
        _Anisotropy ("Scatter Anisotropy (HG g)", Range(-0.9, 0.9)) = 0.45
        _ScatterMul ("Scatter Strength", Float) = 2.0
        _Ambient    ("Ambient Fill", Float) = 0.25
        _Steps      ("Raymarch Steps", Range(16,192)) = 96
        _LightSteps ("Light Steps", Range(2,12)) = 6
        _Seed       ("Seed", Vector) = (0,0,0,0)
        _Class      ("Nebula Class (0 planetary,1 big,2 fragment)", Float) = 1
    }
    SubShader
    {
        // Drawn AFTER the galaxy dust (dust is Transparent+50) and writes NO depth: two semi-transparent volumes
        // can't depth-occlude each other cleanly (that needs OIT) — a depth write punched nebula-shaped holes in
        // the dust. So the glowing nebula composites over the dust; its OWN internal dust gives the dark structure.
        Tags { "RenderType"="Transparent" "Queue"="Transparent+70" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "NebulaVolume"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha   // premultiplied alpha
            ZWrite Off
            ZTest LEqual
            Cull Front                   // render back faces → works when the camera is inside the box too

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Emission, _Emission2, _Emission3, _LightColor, _Shadowed, _LightPos, _Seed;
                float _Freq, _Warp, _DensityMul, _Threshold, _Absorb, _ShadowStrength, _EmissionMul, _Steps, _LightSteps;
                float _Anisotropy, _ScatterMul, _Ambient, _Class;
            CBUFFER_END

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i + float3(0,0,0)), n100 = hash13(i + float3(1,0,0));
                float n010 = hash13(i + float3(0,1,0)), n110 = hash13(i + float3(1,1,0));
                float n001 = hash13(i + float3(0,0,1)), n101 = hash13(i + float3(1,0,1));
                float n011 = hash13(i + float3(0,1,1)), n111 = hash13(i + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,f.x), lerp(n010,n110,f.x), f.y),
                            lerp(lerp(n001,n101,f.x), lerp(n011,n111,f.x), f.y), f.z);
            }
            float fbm3(float3 p) { float v = 0, a = 0.5; [unroll] for (int i = 0; i < 4; i++) { v += a * vnoise(p); p *= 2.03; a *= 0.5; } return v; }
            float fbm2(float3 p) { float v = 0, a = 0.6; [unroll] for (int i = 0; i < 2; i++) { v += a * vnoise(p); p *= 2.5;  a *= 0.5; } return v; }

            // Returns (gas, dust): fine wispy EMISSIVE gas + a separate dense dark ABSORBING dust field. The dust
            // silhouettes against the gas → Pillars-of-Creation structure. Domain-warped, fractal boundary.
            float2 Density(float3 p)
            {
                float3 s = _Seed.xyz;

                // SOFT BOX MARGIN: fade only in the thin outer shell near the faces so the volume never clips into a
                // visible cube — but the SHAPE is otherwise free (not forced into a sphere). Interior stays full.
                float3 m3 = saturate((0.5 - abs(p)) * 7.0);
                float boxFade = m3.x * m3.y * m3.z;

                // Domain-warped coords → organic tendrils, not a smooth ball.
                float3 q = p * _Freq + s;
                float3 w1 = float3(fbm3(q), fbm3(q + 7.3), fbm3(q - 4.1)) - 0.5;
                q += w1 * _Warp * 3.0;
                float3 w2 = float3(fbm3(q * 1.7 + 11.0), fbm3(q * 1.7 - 3.0), fbm3(q * 1.7 + 22.0)) - 0.5;
                q += w2 * _Warp * 1.5;

                // ---- CLASS 0: PLANETARY (Cat's-Eye) — thin ionized bipolar SHELLS, faint, almost no dust.
                if (_Class < 0.5)
                {
                    float3 pb = p; pb.y *= 1.4;
                    float3 nd = normalize(p + 1e-4);
                    // Perturb the shell RADIUS by directional noise so the ring is wavy/lobed, not a perfect circle,
                    // and add explicit BIPOLAR lobes along the axis (butterfly/Cat's-Eye), plus knotty breakup.
                    float warpR = (fbm3(nd * 3.0 + s) - 0.5) * 0.13            // large lobes
                                + (fbm3(nd * 7.0 + s + 5.0) - 0.5) * 0.05;     // finer waviness
                    float rr = length(pb) + warpR;
                    float shell = exp(-pow((rr - 0.28) / 0.03, 2.0));          // thin wavy rim
                    float bip = exp(-pow((rr - 0.17) / 0.07, 2.0)) * smoothstep(0.25, 0.85, abs(nd.y)); // bipolar lobes
                    float knot = pow(saturate(fbm3(pb * 9.0 + s)), 1.5);       // condensations along the shell
                    float fil = 0.4 + 1.3 * (1.0 - abs(fbm3(pb * 5.0 + 5.0))); // filamentary breakup
                    float g = (shell * (0.45 + 1.3 * knot) + bip * 0.75) * fil + 0.06 * exp(-rr * rr * 8.0);
                    g = saturate(g - _Threshold * 0.2) * boxFade;
                    return float2(g, 0.0) * _DensityMul;
                }

                // IRREGULAR CLOUD BODY: amplified fBm MINUS a radial penalty. A direction survives only where the
                // noise beats the growing radial term, so the boundary reaches a DIFFERENT distance in every
                // direction → torn lobes and tendrils (never a sphere), and always dies before the faces (never a
                // cube). Warp first for curl. This subtract-a-radius trick is what actually breaks convexity.
                float3 cp = p * 2.6 + s + 3.0;
                cp += (float3(fbm3(cp + 1.0), fbm3(cp + 9.0), fbm3(cp + 20.0)) - 0.5) * (1.5 + _Warp);
                float body = fbm3(cp) * 2.3;
                float envelope = saturate(body - length(p) * 2.5 - _Threshold * 0.6) * boxFade;

                // GAS filaments — sharp squared ridges (visible threads), multiplied INTO the torn body.
                float ridge = 1.0 - abs(fbm3(q * 1.6 + 3.3));
                float fine  = 1.0 - abs(fbm3(q * 3.4 + 8.1));
                float vfine = 1.0 - abs(fbm3(q * 7.0 + 17.0));
                float fil = saturate(ridge * ridge * 0.7 + fine * fine * 0.5 + vfine * 0.35);
                float gas = saturate(envelope * (0.35 + 1.5 * fil));

                // DUST clumps — dark lanes that carve the gas, a MINORITY (not an opaque shell).
                float dbase = fbm3(q * 1.2 + 40.0) * 0.5 + 0.5;
                float dfine = 1.0 - abs(fbm3(q * 4.0 + 60.0));
                float dclump = saturate((dbase - 0.54) * 3.4);
                float dust = dclump * (0.5 + 0.6 * dfine) * envelope;
                dust = saturate((dust - 0.04) * 1.6);

                // ---- CLASS 2: FRAGMENT — erode into scattered floaty bits (broken wisps, not a connected mass).
                if (_Class > 1.5)
                {
                    float frag = fbm3(p * 1.9 + s + 50.0) * 0.5 + 0.5;
                    float mask = smoothstep(0.5, 0.72, frag);
                    gas *= mask; dust *= mask;
                }

                gas = saturate(gas * (1.0 - saturate(dust * 1.2)));          // gas thinned inside dust → dark lanes
                return float2(gas, dust) * _DensityMul;
            }

            // Cheap combined density for the light march (gas + dust, 2 octaves, no warp).
            float DensityLo(float3 p)
            {
                float3 q = p * _Freq + _Seed.xyz;
                float shape = fbm2(q) * 0.5 + 0.5;
                float dridge = 1.0 - abs(fbm2(q * 0.8 + 40.0));
                float fall = saturate(1.0 - length(p) * 1.7);
                return saturate((shape * 0.4 + pow(saturate(dridge), 2.0) * 0.8) * fall - _Threshold) * _DensityMul;
            }

            // Beer-Lambert transmittance from a point toward the star (self-shadowing → pillars).
            float LightTransmittance(float3 p)
            {
                float3 ld = normalize(_LightPos.xyz - p);
                float stepL = 0.08;
                float tau = 0;
                [loop] for (int i = 0; i < (int)_LightSteps; i++)
                {
                    p += ld * stepL;
                    tau += DensityLo(p) * stepL;
                }
                return exp(-tau * _Absorb * _ShadowStrength);
            }

            float Hash12(float2 p) { p = frac(p * float2(0.1031, 0.11369)); p += dot(p, p.yx + 19.19); return frac(p.x * p.y); }

            // Henyey-Greenstein phase function — how much light scatters toward the camera given the angle between
            // the view ray and the light. g>0 = forward scattering (bright halo when looking toward the star).
            float HG(float cosT, float g)
            {
                float g2 = g * g;
                float denom = 1.0 + g2 - 2.0 * g * cosT;
                return (1.0 - g2) / (12.566371 * pow(max(denom, 1e-4), 1.5));   // 4π = 12.566
            }

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Object-space ray from the camera through this fragment.
                float3 roO = TransformWorldToObject(GetCameraPositionWS());
                float3 rdO = normalize(IN.positionOS - roO);

                // DISTANCE FALLOFF measured in WORLD space (so non-uniform Shape stretch doesn't skew brightness):
                // camera distance ÷ the box's mean world size. Faint from a telescope view, resolves up close.
                float4x4 o2w = GetObjectToWorldMatrix();
                float3 owPos = float3(o2w._m03, o2w._m13, o2w._m23);
                float sizeW = (length(float3(o2w._m00, o2w._m10, o2w._m20))
                             + length(float3(o2w._m01, o2w._m11, o2w._m21))
                             + length(float3(o2w._m02, o2w._m12, o2w._m22))) / 3.0;
                float camDistW = distance(GetCameraPositionWS(), owPos) / max(sizeW, 1e-3);
                float distFade = max(saturate(2.8 / max(camDistW, 1e-3)), 0.04);

                // Intersect the unit cube [-0.5, 0.5].
                float3 invD = 1.0 / rdO;
                float3 t0 = (-0.5 - roO) * invD;
                float3 t1 = ( 0.5 - roO) * invD;
                float3 tmin = min(t0, t1), tmax = max(t0, t1);
                float tNear = max(max(tmin.x, tmin.y), tmin.z);
                float tFar  = min(min(tmax.x, tmax.y), tmax.z);
                tNear = max(tNear, 0.0);
                if (tFar <= tNear) return 0;

                int steps = (int)_Steps;
                float stepSize = (tFar - tNear) / steps;
                // Per-pixel jitter of the start offset → trades banding for noise, so far fewer steps look clean.
                float jitter = Hash12(IN.positionCS.xy);
                float3 pos = roO + rdO * (tNear + jitter * stepSize);
                float3 adv = rdO * stepSize;

                float3 col = 0;
                float trans = 1.0;
                [loop] for (int i = 0; i < steps; i++)
                {
                    float2 dd = Density(pos);
                    float gas = dd.x, dust = dd.y;

                    if (gas + dust > 0.002)
                    {
                        float li = LightTransmittance(pos);                             // starlight reaching here
                        float3 L = normalize(_LightPos.xyz - pos);
                        float phase = HG(dot(rdO, L), _Anisotropy);                     // directional scattering
                        float ion = saturate(1.0 - length(pos - _LightPos.xyz) * 1.3);
                        // Spatial colour variation (not monochrome): a low-freq field mixes a second gas hue through
                        // the cloud, then the hot ionized core colour blends in toward the star.
                        float cvar = saturate(fbm3(pos * 3.0 + _Seed.xyz + 15.0) * 1.4 + 0.1);
                        float3 baseGas = lerp(_Emission.rgb, _Emission3.rgb, cvar);
                        float3 emitCol = lerp(baseGas, _Emission2.rgb, ion * ion * 0.5);

                        // Source radiances: gas EMITS (+ scatters starlight); dust REFLECTS starlight (rim-lit brown).
                        float3 gasRad  = emitCol * (_EmissionMul * 0.55) + _LightColor.rgb * _ScatterMul * li * phase;
                        float3 dustRad = _Shadowed.rgb * (_Ambient + li * (0.6 + _ScatterMul * phase * 0.6))
                                       + emitCol * (li * 0.25);

                        // EMISSION-ABSORPTION composite: BOTH media extinguish, so the glow SELF-SHADOWS instead of
                        // summing to a white ball — brightness plateaus at the source radiance (bright rims, darker
                        // depths), and the dust carves real dark lanes/cavities. distFade (applied at the end) keeps
                        // distant nebulae faint, so this richer, denser look can never bloom into an orb from afar.
                        float sg = gas * 0.9;                          // enough self-occlusion to reveal filaments
                        float sd = dust * _Absorb * 1.6;               // dust carves visible dark lanes
                        float sigma = sg + sd;
                        float a = 1.0 - exp(-sigma * stepSize);
                        float3 src = (gasRad * sg + dustRad * sd) / max(sigma, 1e-4);
                        col   += trans * a * src;
                        trans *= 1.0 - a;
                        if (trans < 0.003) break;
                    }
                    pos += adv;
                }

                return half4(col, 1.0 - trans) * distFade;   // premultiplied → uniformly dims + fades with distance
            }
            ENDHLSL
        }
    }
    Fallback Off
}
