using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace CLAY.GalaxyMap
{
    // Emission / reflection nebulae as DISCRETE regions (not a uniform gas layer). A limited number of region
    // centres are seeded in the spiral arms (where hot young stars form); each region gets several scattered
    // "puffs" so it reads as one irregular glowing cloud. Emission regions are H-alpha pink with O III teal cores;
    // a fraction are blue reflection nebulae. Burst-compiled, one puff per index.
    [BurstCompile]
    public struct GenerateNebulaJob : IJobParallelFor
    {
        [ReadOnly] public GalaxyParameters Params;
        public int PuffsPerRegion;
        public float RegionRadius;      // base scatter radius of puffs around a region centre (world units)
        public float ReflectionFrac;    // 0..1 fraction of regions that are blue reflection nebulae
        public float SizeJitter;        // 0..1 how widely region sizes vary (few big complexes vs many small)
        public float ArmSpread;         // lateral scatter of regions off the arm centre (× ArmWidth)
        public float RadialMin;         // inner radius fraction of the disk where nebulae start
        public float RadialMax;         // outer radius fraction where they end
        public float Evaporation;       // 0 = none; higher = exposed gas is eroded → pillars point at the cluster

        public NativeArray<GalaxyStar> Output;   // Position; Temperature=hue(<0 reflection / 0..1 emission); Luminosity=brightness; Age=sizeFactor

        public void Execute(int index)
        {
            int region = index / math.max(PuffsPerRegion, 1);
            var rr = new Random(Params.WorldSeed * 2u + (uint)region * 2654435761u + 7u);
            var pr = new Random(Params.WorldSeed * 3u + (uint)index * 747796405u + 13u);

            int arms = math.max(1, Params.ArmCount);
            float pitch = math.max(Params.PitchAngle, 0.02f);
            float barLen = Params.BarStrength * Params.DiskRadius * 0.35f;

            // --- Region centre: sit in an arm; the arm and radius are picked randomly (not tied to region index)
            //     so regions aren't evenly distributed one-per-arm. ---
            int a = (int)(rr.NextFloat() * arms) % arms;
            float offset = a * (math.PI * 2f / arms);
            float align = 0.5f + 0.5f * math.cos(2f * offset);
            float barW = math.saturate(Params.BarStrength * 2f);
            float rootR = math.max(math.lerp(barLen * 0.3f, math.max(barLen * 0.85f, 1f), align * barW), 1f);

            float rFrac = math.lerp(RadialMin, RadialMax, rr.NextFloat() * rr.NextFloat());   // concentrate inward
            float cr = rootR + rFrac * Params.DiskRadius;
            // Keep nebulae OUT of the bright bulge/core — a central dark absorption mass over the core looks wrong.
            cr = math.max(cr, Params.BulgeSize * 1.6f + Params.DiskRadius * 0.06f);
            float armAngle = math.log(cr / rootR) / pitch;
            float baseAngle = armAngle + offset;
            float lateral = Gauss(ref rr) * Params.ArmWidth * ArmSpread;
            float2 dir = new float2(math.cos(baseAngle), math.sin(baseAngle));
            float2 perp = new float2(-dir.y, dir.x);
            float2 c2 = dir * cr + perp * lateral;
            float thickY = Params.DiskThickness * Params.DiskRadius;
            float3 center = new float3(c2.x, (rr.NextFloat() - 0.5f) * 2f * thickY, c2.y);

            // --- Per-region variation: a skewed size (most small, a few big complexes) and a varied brightness. ---
            float sizeMul = math.lerp(0.4f, 1f + SizeJitter * 4f, rr.NextFloat() * rr.NextFloat());
            float regionBright = math.lerp(0.35f, 1f, rr.NextFloat()) / math.max(1f, sizeMul * 0.6f);   // big complexes dimmer per-puff

            // --- Region type & colour ---
            bool reflection = rr.NextFloat() < ReflectionFrac;
            float hue = reflection ? -1f : rr.NextFloat();

            // --- Puff: scatter around the centre, flattened into the disc plane; brightness fades outward. ---
            float3 g = new float3(Gauss(ref pr), Gauss(ref pr) * 0.35f, Gauss(ref pr));
            float3 pos = center + g * RegionRadius * sizeMul;
            float d = math.length(g);
            float bright = math.exp(-d * d * 0.7f) * regionBright * math.lerp(0.5f, 1f, pr.NextFloat());

            // PHOTOEVAPORATION: the O/B cluster sits at the region centre. March from this puff toward the centre
            // and sample a gas-density field; if a dense knot SHADOWS the puff it survives, if it's directly
            // exposed to the stars it's eroded — carving pillars whose heads point at the cluster.
            if (Evaporation > 0f)
            {
                float3 toStar = center - pos;
                float distStar = math.length(toStar);
                float3 sdir = math.normalizesafe(toStar, new float3(1f, 0f, 0f));
                float ns = (Params.WorldSeed % 811u) * 0.07f;
                float shield = 0f;
                for (int s = 1; s <= 4; s++)
                {
                    float3 sp = pos + sdir * (distStar * (s / 5f));
                    shield = math.max(shield, NebDensity(sp * 0.12f + ns));   // densest knot along the ray
                }
                float exposed = math.saturate(1f - shield * 1.8f);           // 1 = no shielding gas → evaporate
                bright *= math.lerp(1f, 0.06f, math.saturate(exposed * Evaporation));
            }

            // Follow the galaxy's warp + oval elongation so nebulae sit in the disc shape.
            float warpPhase = (Params.WorldSeed % 628u) * 0.01f;
            float rr2 = math.length(new float2(pos.x, pos.z));
            float rN = rr2 / math.max(Params.DiskRadius, 1f);
            pos.y += Params.DiskWarpS * Params.DiskRadius * 0.6f * rN * rN * math.sin(math.atan2(pos.z, pos.x) - warpPhase);
            pos.x *= 1f + Params.Elongation;
            pos.z *= 1f - Params.Elongation * 0.35f;

            Output[index] = new GalaxyStar
            {
                Position = pos,
                Temperature = hue,
                Luminosity = bright,
                Age = sizeMul * math.lerp(0.6f, 1.4f, pr.NextFloat()),   // per-puff size factor
            };
        }

        private static float Gauss(ref Random rnd)
        {
            float u1 = math.max(rnd.NextFloat(), 1e-6f);
            float u2 = rnd.NextFloat();
            return math.sqrt(-2f * math.log(u1)) * math.cos(2f * math.PI * u2);
        }

        // Gas-density field (0..1) used for the photoevaporation shadow test — 3-octave simplex.
        private static float NebDensity(float3 p)
        {
            float v = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < 3; o++) { v += amp * noise.snoise(p * freq); freq *= 2.1f; amp *= 0.5f; }
            return math.saturate(v * 0.6f + 0.5f);
        }
    }
}
