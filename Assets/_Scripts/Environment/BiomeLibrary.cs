using UnityEngine;

/// <summary>
/// Defines the palette of biomes and blends between them based on climate + position.
/// This is pure data + math — no MonoBehaviour. PlanetaryEnvironment owns it and
/// pushes the blended result to global shader properties each tick.
///
/// THREE TIERS OF CONTROL (as designed):
///   BROAD  — climate temperature (from latitude + planet base) selects which biomes
///            are even plausible. Cold poles favor brine/silt; warm equator favors
///            shallows/bloom/springs/seep.
///   MEDIUM — per-biome Perlin "presence" fields in world space decide which of the
///            plausible biomes you're actually standing in, with smooth blends.
///   FINE   — left to the shaders (per-pixel noise).
/// </summary>
public static class BiomeLibrary
{
    public struct Profile
    {
        public Color deep, mid, shallow;   // 3-stop background palette (dark→light)
        public float murkiness;            // 0 clear .. 1 turbid (fog/contrast loss)
        public float causticStrength;      // how strongly light focuses into caustics
        public Color causticColor;
        public Color particleColor;        // tint for fine drifting particles
        public float particleDensity;      // 0..2 multiplier
        public Color organicColor;         // tint for organic-matter drifters
        public float organicDensity;       // 0..2 multiplier
        public float preferredTemp;        // climate match center (°C)
        public float tempTolerance;        // gaussian width of climate match
    }

    // ── Biome definitions ────────────────────────────────────────────────────
    public static readonly Profile[] Biomes =
    {
        // Sunlit Shallows — bright, clear, alive
        new Profile {
            deep    = C(0.04f,0.18f,0.20f), mid = C(0.10f,0.42f,0.40f), shallow = C(0.35f,0.78f,0.66f),
            murkiness = 0.12f, causticStrength = 1.0f, causticColor = C(0.95f,1.0f,0.85f),
            particleColor = C(0.85f,0.96f,0.90f), particleDensity = 1.2f,
            organicColor  = C(0.55f,0.85f,0.55f), organicDensity  = 1.0f,
            preferredTemp = 24f, tempTolerance = 14f,
        },
        // Algal Bloom — vivid green-gold, dense life, soft hazy light
        new Profile {
            deep    = C(0.06f,0.16f,0.05f), mid = C(0.20f,0.40f,0.10f), shallow = C(0.55f,0.72f,0.22f),
            murkiness = 0.42f, causticStrength = 0.45f, causticColor = C(0.85f,0.95f,0.55f),
            particleColor = C(0.70f,0.90f,0.40f), particleDensity = 1.8f,
            organicColor  = C(0.50f,0.80f,0.30f), organicDensity  = 1.7f,
            preferredTemp = 20f, tempTolerance = 16f,
        },
        // Mineral Springs — pale turquoise, mineral haze, bubble streams
        new Profile {
            deep    = C(0.05f,0.20f,0.24f), mid = C(0.18f,0.50f,0.55f), shallow = C(0.55f,0.85f,0.88f),
            murkiness = 0.18f, causticStrength = 0.7f, causticColor = C(0.80f,0.98f,1.0f),
            particleColor = C(0.80f,0.95f,1.0f), particleDensity = 1.0f,
            organicColor  = C(0.65f,0.88f,0.92f), organicDensity  = 0.6f,
            preferredTemp = 30f, tempTolerance = 18f,
        },
        // Iron Seep — rusty red-orange, hot, mineral motes
        new Profile {
            deep    = C(0.16f,0.06f,0.03f), mid = C(0.42f,0.18f,0.08f), shallow = C(0.75f,0.40f,0.18f),
            murkiness = 0.50f, causticStrength = 0.25f, causticColor = C(1.0f,0.7f,0.4f),
            particleColor = C(0.95f,0.6f,0.35f), particleDensity = 1.3f,
            organicColor  = C(0.85f,0.45f,0.25f), organicDensity  = 0.9f,
            preferredTemp = 55f, tempTolerance = 22f,
        },
        // Murky Silt Flats — olive-brown, turbid, detritus
        new Profile {
            deep    = C(0.10f,0.10f,0.06f), mid = C(0.28f,0.26f,0.14f), shallow = C(0.50f,0.46f,0.28f),
            murkiness = 0.70f, causticStrength = 0.18f, causticColor = C(0.80f,0.75f,0.55f),
            particleColor = C(0.65f,0.60f,0.45f), particleDensity = 1.4f,
            organicColor  = C(0.55f,0.48f,0.32f), organicDensity  = 1.6f,
            preferredTemp = 12f, tempTolerance = 30f,
        },
        // Cold Brine — desaturated steel-blue, dark, sparse
        new Profile {
            deep    = C(0.04f,0.07f,0.12f), mid = C(0.12f,0.20f,0.32f), shallow = C(0.32f,0.44f,0.58f),
            murkiness = 0.55f, causticStrength = 0.30f, causticColor = C(0.70f,0.82f,1.0f),
            particleColor = C(0.60f,0.72f,0.90f), particleDensity = 0.5f,
            organicColor  = C(0.45f,0.58f,0.78f), organicDensity  = 0.4f,
            preferredTemp = -2f, tempTolerance = 16f,
        },
    };

    static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

    /// <summary>
    /// Blend all biomes at a world position given the local climate temperature.
    /// weight_i = climateMatch_i(temp) * presenceBlob_i(pos).
    /// </summary>
    public static Profile Blend(Vector2 worldPos, float climateTemp, float biomeScale)
    {
        float totalW = 0f;
        Profile acc = default;

        for (int i = 0; i < Biomes.Length; i++)
        {
            var b = Biomes[i];

            // BROAD: climate gate — gaussian falloff around the biome's preferred temp
            float dT = (climateTemp - b.preferredTemp) / b.tempTolerance;
            float climate = Mathf.Exp(-dT * dT);

            // MEDIUM: spatial presence — each biome owns a moving Perlin blob field
            float seed = i * 37.13f;
            float blob = Mathf.PerlinNoise(worldPos.x * biomeScale + seed,
                                           worldPos.y * biomeScale + seed * 1.7f);
            // Sharpen so one biome tends to dominate a region rather than grey mush
            blob = Mathf.Pow(Mathf.Clamp01(blob), 2.0f);

            float w = climate * blob + 0.0001f; // epsilon keeps blend defined everywhere
            totalW += w;

            acc.deep            += b.deep            * w;
            acc.mid             += b.mid             * w;
            acc.shallow         += b.shallow         * w;
            acc.murkiness       += b.murkiness       * w;
            acc.causticStrength += b.causticStrength * w;
            acc.causticColor    += b.causticColor    * w;
            acc.particleColor   += b.particleColor   * w;
            acc.particleDensity += b.particleDensity * w;
            acc.organicColor    += b.organicColor    * w;
            acc.organicDensity  += b.organicDensity  * w;
        }

        float inv = 1f / totalW;
        acc.deep            *= inv;  acc.mid           *= inv;  acc.shallow        *= inv;
        acc.causticColor    *= inv;  acc.particleColor *= inv;  acc.organicColor   *= inv;
        acc.murkiness       *= inv;  acc.causticStrength *= inv;
        acc.particleDensity *= inv;  acc.organicDensity  *= inv;
        return acc;
    }
}
