using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace CLAY.GalaxyMap
{
    // Places one interstellar-dust particle per index. Positions are biased onto the spiral arms (for efficiency,
    // so few particles are wasted in empty inter-arm space), then a physically-motivated density field decides how
    // opaque each one is: exponential radial falloff × sech² vertical (thin dust plane) × sin⁴ spiral-arm
    // compression, minus domain-warped fBm turbulence that carves the smooth field into organic tangled lanes.
    // Does NOT touch the star/galaxy-shape generation — this is a separate population. Burst-compiled, parallel.
    [BurstCompile]
    public struct GenerateDustJob : IJobParallelFor
    {
        [ReadOnly] public GalaxyParameters Params;
        public float CloudScale;       // overall noise scale (smaller = larger features)
        public float Contrast;         // structure threshold (higher = sparser, wispier, more voids)
        public float ShadowStrength;   // self-shadow depth
        public float Warp;             // flow-warp displacement (how far the vortex field drags the domain)
        public float Coverage;         // 0 = only sparse structure, 1 = continuous dust sheet
        public float Clumpiness;       // 0 = uniform coverage, 1 = broken into large patches with voids
        public float Swirl;            // vortex strength — how much the flow field rotates (gas-giant swirls)
        public float BigNoise;         // relative frequency of the large-scale structure (bands / big vortices)
        public float FineNoise;        // relative frequency of the fine turbulence detail
        public float FineWeight;       // 0 = only big structure, 1 = fully modulated by fine detail
        public float ArmInfluence;     // 0 = ignore arms (loose noise cloud), 1 = strongly follow the arms
        public float Webbing;          // 0 = smooth clouds, 1 = marbled/webbed ridge veins
        public float WebAmount;        // 0 = off, 1 = density concentrated on a Voronoi filament WEB
        public float WebScale;         // Voronoi cell frequency (bigger = finer web)
        public NativeArray<GalaxyStar> Output;   // Position; Luminosity carries density(0..1); Temperature = light

        public void Execute(int index)
        {
            var random = new Random(Params.WorldSeed + (uint)index * 2654435761u + 1u);

            // --- BROAD placement across the whole disk (uniform angle), so dust can appear anywhere; the density
            //     field — not the placement — decides where it's thick, thin, clumped, or absent. ---
            float hR = math.max(Params.DiskRadius * 0.30f, 0.5f);
            float r = -hR * (math.log(math.max(random.NextFloat(), 1e-5f)) +
                             math.log(math.max(random.NextFloat(), 1e-5f)));
            float theta = random.NextFloat() * (math.PI * 2f);
            float hz = math.max(Params.DiskThickness * Params.DiskRadius * 0.45f, 0.05f);
            float y = Gaussian2(ref random).x * hz;
            float3 pos = new float3(r * math.cos(theta), y, r * math.sin(theta));

            float so = (Params.WorldSeed % 977u) * 0.137f;
            float density = math.saturate(SampleField(pos, Params, so) * Params.GasDustDensity * 3.5f);

            // --- Lighting: illumination from the galactic core, dimmed by SELF-SHADOW — optical depth marched
            //     toward the centre through the same field (Beer-Lambert). Lit on the core-facing side, dark
            //     behind dense lanes → the dust gets real 3-D form instead of reading flat. ---
            float rr = math.length(new float2(pos.x, pos.z));
            float illum = 0.15f + 0.85f * math.exp(-rr / math.max(Params.DiskRadius * 0.5f, 1e-3f));
            float3 toCenter = math.normalizesafe(-pos, new float3(-1f, 0f, 0f));
            float stepLen = rr / 3f + 0.05f;
            float tau = 0f;
            float3 sp = pos;
            for (int k = 0; k < 3; k++)
            {
                sp += toCenter * stepLen;
                tau += SampleField(sp, Params, so);
            }
            float shadow = math.exp(-tau * stepLen * ShadowStrength * 2.6f);
            float light = illum * math.lerp(0.12f, 1f, shadow);   // deeper shadows → stronger 3-D contrast in lanes

            // Off-plane drift so the dust needn't converge to the star disc (coherent low-freq waves), PLUS the
            // same integral-sign disk warp so it follows a bent disc, then the oval elongation. Applied to the
            // render position only — the density/lighting above were sampled in the flat structural frame.
            float dseed = (Params.WorldSeed % 613u) * 0.09f;
            float floatY = noise.snoise(pos * 0.02f + dseed) * Params.DustFloat * Params.DiskRadius * 0.4f;
            float warpPhase = (Params.WorldSeed % 628u) * 0.01f;
            float rN = rr / math.max(Params.DiskRadius, 1f);
            float warpY = Params.DiskWarpS * Params.DiskRadius * 0.6f * rN * rN
                        * math.sin(math.atan2(pos.z, pos.x) - warpPhase);
            pos.y += floatY + warpY;
            pos.x *= 1f + Params.Elongation;
            pos.z *= 1f - Params.Elongation * 0.35f;

            Output[index] = new GalaxyStar
            {
                Position = pos,
                Luminosity = density,           // opacity driver
                Temperature = light,            // 0..1 lighting (illumination × self-shadow) → tint in Bootstrap
                Age = random.NextFloat(),       // per-particle variety (size / tint)
            };
        }

        // The dust density field at an arbitrary point (radial × sech² vertical × bulge hole × spiral bias ×
        // domain-warped ridged-fBm filament structure). Shared by the particle's own density and the self-shadow
        // march so the shadows match the visible dust. Returns unscaled density (0..~1).
        private float SampleField(float3 pos, in GalaxyParameters P, float so)
        {
            int arms = math.max(1, P.ArmCount);
            float pitch = math.max(P.PitchAngle, 0.02f);
            float refR = math.max(P.BarStrength * P.DiskRadius * 0.35f, 1f);

            float rr = math.length(new float2(pos.x, pos.z));
            float ang = math.atan2(pos.z, pos.x);
            float hz = math.max(P.DiskThickness * P.DiskRadius * 0.45f, 0.05f);

            float radial = math.exp(-rr / math.max(P.DiskRadius * 0.9f, 1e-3f));
            float sech = 1f / math.cosh(pos.y / hz);
            float vertical = sech * sech;
            float bulgeHole = math.smoothstep(P.BulgeSize * 0.4f, P.BulgeSize * 1.5f, rr);

            // LOOSE arm bias: at ArmInfluence 0 the dust ignores the arms entirely (a pure noise cloud); at 1 it
            // strongly prefers them. Either way it's just a density weight, not a hard structural constraint.
            float armPhase = arms * (ang - math.log(math.max(rr, 1e-3f) / refR) / pitch);
            float sc = (math.sin(armPhase) + 1f) * 0.5f;
            float armBias = math.lerp(1f, 0.3f + 1.4f * (sc * sc), math.saturate(ArmInfluence));

            float cs = math.max(CloudScale, 1e-3f);
            float3 p = pos * cs + so;

            // CURL-NOISE warp (∇×F, divergence-free): advecting the domain along a curl field stretches the dust
            // into organic fluid FILAMENTS and mineral-like striations that wrap around each other, instead of
            // the blobby look of plain fBm. Iterated for turbulent, curdled flow (Swirl = curl frequency spread).
            float curlFreq = BigNoise * (0.5f + Swirl);
            for (int i = 0; i < 3; i++)
                p += CurlNoise(p * curlFreq) * (Warp * 0.6f);

            // MULTI-SCALE density: big structure (bands / large vortices) modulated by fine turbulence — like a
            // gas giant. FineWeight sets how much the fine detail breaks up the big forms.
            float bigV = Fbm(p * BigNoise) * 0.5f + 0.5f;
            float fineV = Fbm(p * FineNoise) * 0.5f + 0.5f;
            float cloud = math.saturate(math.lerp(bigV, bigV * fineV * 1.8f, math.saturate(FineWeight)));

            // MARBLING / WEBBING: ridged veins (1-|noise|) on the domain-warped coords form a connected filament
            // web — the "marbled" look of real dust. Webbing blends from smooth cloud → sharp veins.
            float v1 = 1f - math.abs(Fbm(p * BigNoise + 3.3f));
            float v2 = 1f - math.abs(Fbm(p * FineNoise * 0.6f + 9.1f));
            float web = math.pow(math.saturate(v1 * 0.6f + v2 * 0.4f), math.lerp(1.5f, 5f, math.saturate(Webbing)));
            float tex = math.lerp(cloud, web, math.saturate(Webbing));

            // Coverage raises the floor → continuous sheet; clumpiness reintroduces large-scale patchiness.
            float body = math.lerp(tex, 1f, math.saturate(Coverage));
            float large = math.saturate(Fbm(p * BigNoise * 0.4f) * 0.6f + 0.5f);
            float mask = math.lerp(1f, large, math.saturate(Clumpiness));

            // GALAXY-SCALE LANE SKELETON: the dust distribution is decided FIRST on the scale of the whole galaxy —
            // a coarse ridged + Voronoi network (in units of the disk radius, independent of CloudScale) whose
            // cell walls / ridge lines form the major connected filaments that span the disk. The fine local
            // structure above only FILLS these lanes, so the dust reads as coherent threads instead of scattered
            // specks. WebScale spreads the lane frequency; WebAmount blends uniform → fully lane-concentrated.
            float3 gp = pos / math.max(P.DiskRadius, 1f) * 2.4f + so * 0.3f;
            float gpWarp = Fbm(gp * 1.3f) * 0.5f;                              // gentle bend so lanes aren't grid-like
            gp += new float3(gpWarp, 0f, Fbm(gp * 1.3f + 5.0f) * 0.5f);
            float macroRidge = 1f - math.abs(Fbm(gp * (0.9f + WebScale * 0.12f)));
            float2 gvor = noise.cellular(gp * (1.1f + WebScale * 0.22f));
            float macroLane = 1f - math.saturate((gvor.y - gvor.x) * 3.2f);   // 1 along the coarse cell walls
            float2 gvor2 = noise.cellular(gp * (2.7f + WebScale * 0.45f));
            float fineLane = 1f - math.saturate((gvor2.y - gvor2.x) * 5.0f);  // finer secondary thread network
            float lanes = math.pow(math.saturate(macroRidge * 0.42f + macroLane * 0.72f + fineLane * 0.45f),
                                   math.lerp(1.6f, 4.0f, math.saturate(Webbing)));
            float laneMask = math.lerp(1f, lanes * 2.4f, math.saturate(WebAmount));

            float structure = math.saturate(body * mask * laneMask - Contrast);

            return radial * vertical * bulgeHole * armBias * structure;
        }

        // Fractal Brownian motion (4 octaves of simplex), ~[-1,1]. Building block for the domain warp above.
        private static float Fbm(float3 p)
        {
            float v = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < 4; o++)
            {
                v += amp * noise.snoise(p * freq);
                freq *= 2.02f;
                amp *= 0.5f;
            }
            return v;
        }

        // Curl of a 3-component noise potential (∇×F) via central differences → a divergence-free vector field.
        // Advecting coordinates along it produces incompressible, fluid-like filaments (no sources/sinks = no blobs).
        private static float3 CurlNoise(float3 p)
        {
            const float e = 0.35f;
            float3 dx = new float3(e, 0f, 0f), dy = new float3(0f, e, 0f), dz = new float3(0f, 0f, e);
            float3 o1 = new float3(31.4f, 11.7f, 47.2f);   // decorrelate the three potential components
            float3 o2 = new float3(-27.1f, 63.3f, -19.8f);

            // Potential P = (Px, Py, Pz); curl = (dPz/dy - dPy/dz, dPx/dz - dPz/dx, dPy/dx - dPx/dy).
            float dPz_dy = noise.snoise(p + o2 + dy) - noise.snoise(p + o2 - dy);
            float dPy_dz = noise.snoise(p + o1 + dz) - noise.snoise(p + o1 - dz);
            float dPx_dz = noise.snoise(p + dz) - noise.snoise(p - dz);
            float dPz_dx = noise.snoise(p + o2 + dx) - noise.snoise(p + o2 - dx);
            float dPy_dx = noise.snoise(p + o1 + dx) - noise.snoise(p + o1 - dx);
            float dPx_dy = noise.snoise(p + dy) - noise.snoise(p - dy);

            return new float3(dPz_dy - dPy_dz, dPx_dz - dPz_dx, dPy_dx - dPx_dy) / (2f * e);
        }

        private static float2 Gaussian2(ref Random rnd)
        {
            float u1 = math.max(rnd.NextFloat(), 1e-6f);
            float u2 = rnd.NextFloat();
            float mag = math.sqrt(-2.0f * math.log(u1));
            return new float2(mag * math.cos(2.0f * math.PI * u2), mag * math.sin(2.0f * math.PI * u2));
        }
    }
}
