using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace CLAY.GalaxyMap
{
    // Places one star per index: bulge vs disk, logarithmic-spiral arms, then procedural deformations
    // (S-warp, tidal tail, collision ring), and a location-driven temperature. Burst-compiled, parallel.
    [BurstCompile]
    public struct GenerateGalaxyJob : IJobParallelFor
    {
        [ReadOnly] public GalaxyParameters Params;
        public NativeArray<GalaxyStar> StarOutput;

        public void Execute(int index)
        {
            // Deterministic per-star RNG (avoid seed 0, which Unity.Mathematics.Random forbids).
            var random = new Random(Params.WorldSeed + (uint)index * 747796405u + 1u);

            // Ellipticalness pushes more of the galaxy into the spheroidal bulge (→ a smooth elliptical).
            float bulgeChance = math.lerp(Params.BulgeToDiskRatio, 0.97f, math.saturate(Params.Ellipticalness));
            bool inBulge = random.NextFloat() < bulgeChance;
            float3 basePos;
            float barLen = Params.BarStrength * Params.DiskRadius * 0.35f;
            float thickY = Params.DiskThickness * Params.DiskRadius;
            float youngFrac;   // fraction of young (blue) stars for this star's region

            if (inBulge)
            {
                // Soft spheroidal core that REACHES the bar tips. When Ellipticalness is high it grows to fill the
                // galaxy (a spheroidal elliptical) and rounds up.
                float coreR = math.max(Params.BulgeSize, barLen) * 1.12f;
                coreR = math.lerp(coreR, Params.DiskRadius * 0.75f, math.saturate(Params.Ellipticalness));
                float rad = coreR * random.NextFloat() * math.sqrt(random.NextFloat());   // dense core, soft edge
                basePos = random.NextFloat3Direction() * rad;
                basePos.y *= math.lerp((1.0f - Params.Ellipticity) * 0.6f, 0.8f, math.saturate(Params.Ellipticalness));
                basePos.x *= math.lerp(1f, 1.25f, Params.BarStrength);   // slight bar elongation
                basePos.z *= math.lerp(1f, 0.4f, Params.BarStrength);    // thin the bar across → oval
                youngFrac = Params.CorePop;
            }
            else
            {
                int arms = math.max(1, Params.ArmCount);
                float pitch = math.max(Params.PitchAngle, 0.02f);
                int a = index % arms;
                float offset = a * (math.PI * 2f / arms);

                // Per-arm ROOT radius. Arms aligned with the bar (|cos| ≈ 1) spring from the bar ENDS (barLen);
                // arms across the bar root much closer to the CENTRE and thus emerge from within the bulge instead
                // of sprouting off the bar's short sides. Only engaged when a bar is actually present.
                float align = 0.5f + 0.5f * math.cos(2f * offset);       // 1 on the bar axis, 0 perpendicular
                float barW = math.saturate(Params.BarStrength * 2f);
                // Roots sit a bit INSIDE the bulge edge so the arm always overlaps the core (closing the small gap
                // that showed when arms were very thin or very crisp), never floating just outside it.
                float rootR = math.max(math.lerp(barLen * 0.28f, math.max(barLen * 0.85f, 1f), align * barW), 1f);

                // TIGHT arm: radius is a half-normal measured from the root — densest AT the root (so the arm is
                // anchored on the bulge/bar, no gap) and fading out with a Gaussian tail. No wide disk-fill, so
                // no hazy inner blob and no webbing between arms.
                float hh = math.max(Params.DiskRadius * 0.5f, 0.5f);
                float r = rootR + hh * math.abs(Gaussian2(ref random).x);
                float armAngle = math.log(r / rootR) / pitch;            // 0 at the root, winds outward
                float baseAngle = armAngle + offset;

                // Monotonic radial taper across the arm's span (+1 thin ends, −1 thin centre, 0 uniform).
                float rnorm = math.saturate(r / (Params.DiskRadius * 0.8f));
                float taperFac = Params.ArmTaper >= 0f
                    ? math.lerp(1f, 1f - Params.ArmTaper, rnorm)
                    : math.lerp(1f + Params.ArmTaper, 1f, rnorm);
                float halfW = Params.ArmWidth * math.max(taperFac, 0.05f);

                // Cross-arm Gaussian scatter. ArmDistinction tightens the arm (high) or lets it bleed into a
                // hazier disk (low) — a controlled widening, not a separate fuzzy population.
                // Renormalised looser: even at max ArmDistinction the arms never collapse to razor lines (min
                // spread 1.7, not 1.0), so they read as broad, soft arms rather than hard filaments.
                float spreadMul = math.lerp(3.0f, 1.7f, math.sqrt(math.saturate(Params.ArmDistinction)));
                // Root FLARE: near the root add width that scales with the root radius, so the arm base always
                // fans into the bulge (bridging the small gap that showed with thin or very crisp arms) and fades
                // to the tight arm body outward — independent of ArmWidth / ArmDistinction.
                float rootBridge = math.exp(-(r - rootR) / math.max(barLen * 0.3f, 0.5f));
                float sigma = halfW * 0.6f * spreadMul + rootBridge * rootR * 0.4f;
                float2 gStd = Gaussian2(ref random);
                float2 g = gStd * sigma;
                float sx = r * math.cos(baseAngle) + g.x;
                float sz = r * math.sin(baseAngle) + g.y;
                basePos = new float3(sx, random.NextFloat(-1f, 1f) * thickY, sz);

                // On-arm stars (small cross-arm offset) are young/blue; inter-arm stars trend old.
                float armness = math.exp(-0.5f * math.dot(gStd, gStd));
                youngFrac = math.lerp(Params.DiskPop, Params.ArmPop, armness);
            }

            // Overall random scatter + low-frequency simplex lumpiness (organic clumping, not uniform hash).
            basePos += random.NextFloat3(-1f, 1f) * (Params.Irregularity * Params.DiskRadius * 0.1f);
            float nseed = (Params.WorldSeed % 500u) * 0.13f;
            float3 nq = basePos * 0.03f + nseed;
            float3 nd = new float3(noise.snoise(nq), noise.snoise(nq + 11.3f), noise.snoise(nq - 7.1f));
            basePos += nd * (Params.Irregularity * Params.DiskRadius * 0.22f);

            float distanceToCenter = math.length(basePos);

            // Disk WARP: an integral-sign bend that grows with radius² (curves the outer disk up on one side and
            // down on the other), with a per-galaxy phase — actually bends the disc rather than slanting it.
            float warpPhase = (Params.WorldSeed % 628u) * 0.01f;
            float rN = distanceToCenter / math.max(Params.DiskRadius, 1f);
            basePos.y += Params.DiskWarpS * Params.DiskRadius * 0.6f * rN * rN
                       * math.sin(math.atan2(basePos.z, basePos.x) - warpPhase);

            // Tidal tail: drag the outer edge toward an attractor.
            float tidalStart = Params.DiskRadius * 0.4f;
            if (Params.TidalTailStrength > 0f && distanceToCenter > tidalStart)
            {
                float3 tidalVector = new float3(1f, 0.2f, 0.5f);
                basePos += tidalVector * ((distanceToCenter - tidalStart) * Params.TidalTailStrength);
            }

            // ANOMALIES: up to 5 seed-typed disturbances. Type depends on the anomaly's own RNG → empty voids or
            // scattered/disrupted zones (broken-looking patches). Cheap per-star sphere tests.
            // ANOMALIES perturb the star DISTRIBUTION (no empty voids — those read as ugly holes). Each is a seeded
            // patch that either compresses stars into an over-dense knot, shears them into a tidal stream, or swirls
            // them — weird local structure, never a gap. Placement is never at the core.
            for (int k = 0; k < Params.AnomalyCount; k++)
            {
                var ar = new Random(Params.WorldSeed * 7u + (uint)k * 99131u + 3u);
                float atype = ar.NextFloat();
                float aang = ar.NextFloat() * (math.PI * 2f);
                float arad = math.lerp(0.22f, 0.85f, ar.NextFloat()) * Params.DiskRadius;   // never at the core
                float3 ac = new float3(arad * math.cos(aang), (ar.NextFloat() - 0.5f) * thickY * 2f, arad * math.sin(aang));
                float arr = Params.DiskRadius * math.lerp(0.1f, 0.28f, ar.NextFloat());
                float sang = ar.NextFloat() * (math.PI * 2f);       // stream / swirl orientation

                float3 rel = basePos - ac;
                rel.y *= 2.5f;
                float dd = math.length(rel);
                if (dd >= arr) continue;
                float w = math.smoothstep(arr, 0f, dd);            // 1 at the centre → 0 at the edge (soft)
                float3 dir = math.normalizesafe(basePos - ac, new float3(1f, 0f, 0f));

                if (atype < 0.4f)
                {
                    basePos -= dir * (dd * 0.6f * w);              // KNOT: pull inward → dense over-population
                }
                else if (atype < 0.72f)
                {
                    float3 sdir = new float3(math.cos(sang), 0f, math.sin(sang));
                    basePos += sdir * (arr * 0.85f * w);           // STREAM: shear along a direction → tidal smear
                }
                else
                {
                    float rotA = w * 1.5f * (atype > 0.86f ? 1f : -1f);   // SWIRL: rotate the patch → pinwheel warp
                    float ca = math.cos(rotA), sa = math.sin(rotA);
                    float3 r2 = basePos - ac;
                    basePos = ac + new float3(ca * r2.x - sa * r2.z, r2.y, sa * r2.x + ca * r2.z);
                }
            }

            // ELONGATION: stretch the whole galaxy into an oval (applied last, as a pure visual transform).
            basePos.x *= 1f + Params.Elongation;
            basePos.z *= 1f - Params.Elongation * 0.35f;

            StarOutput[index] = new GalaxyStar
            {
                Position = basePos,
                Temperature = StarTemp(youngFrac, ref random),
                Age = random.NextFloat(0f, 10f),
                // Power-law luminosity: pow(u, 4) heavily skews toward 0 (most stars faint) with a thin tail of
                // brilliant stars. (Bootstrap still treats a negative luminosity as a hidden star, unused now.)
                Luminosity = math.pow(random.NextFloat(), 4f),
            };
        }

        // A 2D standard-normal sample (Box-Muller) — soft, round scatter instead of a hard uniform box.
        private static float2 Gaussian2(ref Random rnd)
        {
            float u1 = math.max(rnd.NextFloat(), 1e-6f);
            float u2 = rnd.NextFloat();
            float mag = math.sqrt(-2.0f * math.log(u1));
            return new float2(mag * math.cos(2.0f * math.PI * u2), mag * math.sin(2.0f * math.PI * u2));
        }

        // youngFrac = fraction of hot blue young stars in this region; the rest are old cool red stars.
        private static float StarTemp(float youngFrac, ref Random rnd)
        {
            if (rnd.NextFloat() < math.saturate(youngFrac))
                return math.lerp(6500f, 33000f, math.pow(rnd.NextFloat(), 2f));   // young O/B/A, bluer
            return math.lerp(2700f, 4600f, rnd.NextFloat());                       // old K/M, redder
        }
    }
}
