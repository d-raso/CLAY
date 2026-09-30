using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace CLAY.GalaxyMap
{
    // Tunable knobs that shape a whole galaxy. Deterministic from WorldSeed.
    public struct GalaxyParameters : IComponentData
    {
        // Size
        public float DiskRadius;         // overall disk radius — bounds how far the galaxy extends
        public float DiskThickness;      // vertical half-thickness of the disk (as a fraction of DiskRadius)

        // Morphology
        public float BulgeToDiskRatio;   // 0..1 fraction of stars in the central bulge
        public float BulgeSize;          // bulge radius (world units)
        public float Ellipticity;        // 0..1 squashes the bulge
        public float BarStrength;         // 0..1 elongates the bulge into a bar
        public float Irregularity;        // 0..1 random scatter

        // Spiral arms
        public int ArmCount;
        public float PitchAngle;          // 'b' in the logarithmic spiral r = a·e^(b·theta)
        public float ArmWidth;            // radial thickness of each arm (world units)
        public float ArmDistinction;      // 0 = smooth featureless disk, 1 = razor-tight arms
        public float ArmTaper;            // -1 = skinny at centre, 0 = uniform width, +1 = skinny at the ends

        // Composition
        public float GasDustDensity;
        public float StarBirthRate;       // 0..1 → hotter young stars in the arms

        // Stellar populations (0 = old/red, 1 = young/blue) per region
        public float CorePop;             // bulge / core stars
        public float ArmPop;              // stars on the spiral arms (star-forming)
        public float DiskPop;             // inter-arm disk stars

        // Deformation
        public float TidalTailStrength;
        public float DiskWarpS;           // bends the outer disk up/down (radial², integral-sign warp)
        public float CollisionRingPhase;
        public float Elongation;          // 0..1 stretch the whole galaxy into an oval
        public float DustFloat;           // 0 = dust in the disc plane, 1 = dust drifts off-plane (noise)
        public float Ellipticalness;      // 0 = disky spiral, 1 = smooth spheroidal elliptical
        public int   AnomalyCount;        // 0..5 seed-typed anomalies (empty patches, over-densities, …)

        public uint WorldSeed;

        // A sensible default galaxy (Milky-Way-ish barred spiral).
        public static GalaxyParameters Default => new GalaxyParameters
        {
            DiskRadius = 60f,
            DiskThickness = 0.03f,
            BulgeToDiskRatio = 0.25f,
            BulgeSize = 8f,
            Ellipticity = 0.2f,
            BarStrength = 0.35f,
            Irregularity = 0.12f,
            ArmCount = 2,
            PitchAngle = 0.25f,
            ArmWidth = 4f,
            ArmDistinction = 0.75f,
            ArmTaper = 0f,
            GasDustDensity = 0.5f,
            StarBirthRate = 0.5f,
            CorePop = 0.1f,
            ArmPop = 0.75f,
            DiskPop = 0.35f,
            TidalTailStrength = 0f,
            DiskWarpS = 0f,
            CollisionRingPhase = 0f,
            Elongation = 0f,
            DustFloat = 0.15f,
            Ellipticalness = 0f,
            AnomalyCount = 0,
            WorldSeed = 12345u,
        };
    }

    // Plain per-star result of the generation job (position + physical props).
    public struct GalaxyStar
    {
        public float3 Position;
        public float Temperature;   // Kelvin — drives the blackbody colour
        public float Age;           // Gyr
        public float Luminosity;    // 0..1 power-law draw (many faint, few brilliant) → brightness & size
    }

    // Per-instance colour (blackbody, computed on the CPU) uploaded by Entities Graphics into the shader's
    // _StarColor. A float4 override is the same well-trodden path URP uses for base colour, so it's the most
    // reliable way to get per-instance data onto the GPU. RGB carries colour×brightness; A is unused.
    [MaterialProperty("_StarColor")]
    public struct StarColor : IComponentData
    {
        public float4 Value;
    }
}
