using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.GalaxyMap
{
    // The galaxy-side context of one picked star: everything we can read from its place/colour/brightness in the
    // rendered galaxy, used to INFER what kind of system it should generate.
    public struct GalaxyStarContext
    {
        public int index;            // star index (→ deterministic seed)
        public uint galaxySeed;
        public Vector3 worldPos;
        public float temperatureK;   // blackbody temperature (the star's colour)
        public float luminosity01;   // 0..1 brightness rank in the galaxy render
        public float ageGyr;         // render-layer age hint
        public float radiusFrac;     // 0 = galactic centre … 1 = disk edge
        public float armProximity;   // 0..1 how strongly on a spiral arm (star-forming)
        public float nebulaProximity;// 0..1 how close to a volumetric nebula (1 = inside one)
        public Color color;
    }

    // The distilled result of inference — a deterministic seed plus the generation biases, and a human summary.
    public struct StarInferenceResult
    {
        public ulong seed;
        public SystemGenParams genParams;
        public float metallicity;      // [Fe/H] the system will use
        public string summary;         // multi-line human-readable "why"
    }

    // Turns a star's galaxy context into concrete system-generation inputs. Pure/deterministic: the same star in
    // the same galaxy always infers the same system.
    public static class StarInference
    {
        public static StarInferenceResult Infer(in GalaxyStarContext c)
        {
            ulong seed = DetRng.Hash(c.galaxySeed, (ulong)(uint)c.index);
            var roll = new DetRng(seed ^ 0x9E3779B97F4A7C15UL);   // side-channel rolls that don't disturb the system seed

            // COLOUR → spectral class. The star's rendered temperature IS its class (blue/hot = O/B/A, red = M).
            var desig = Astrophysics.Designation(Mathf.Clamp(c.temperatureK, 2400f, 52000f));
            SpectralClass cls = desig.cls;
            float hotFrac = Mathf.Clamp01((c.temperatureK - 6000f) / 20000f);   // 0 cool … 1 very hot/massive

            // METALLICITY: real radial gradient — metal-rich toward the centre and on the arms, metal-poor in the
            // outskirts/halo. Drives planet richness & habitability.
            float feh = Mathf.Lerp(0.35f, -0.55f, Mathf.Clamp01(c.radiusFrac))
                      + 0.12f * c.armProximity
                      + roll.Range(-0.08f, 0.08f);
            feh = Mathf.Clamp(feh, -1f, 0.6f);

            // NEAR A NEBULA / ON AN ARM → young & volatile (hot, flaring, active). Bright blue stars are young too.
            float youth = Mathf.Clamp01(0.7f * c.nebulaProximity + 0.35f * c.armProximity + 0.25f * hotFrac);

            // METAL-RICH → more terrestrials & higher habitability; metal-poor → fewer.
            float habBoost = Mathf.Clamp(1f + feh * 0.9f, 0.5f, 1.7f);

            // MULTIPLICITY: massive/hot stars are far more often in multiple systems.
            float multiChance = Mathf.Clamp01(0.12f + 0.5f * hotFrac + 0.15f * Mathf.Clamp01(c.luminosity01 - 0.7f));
            int starCount = 1;
            if (roll.Value < multiChance) starCount = 2;
            if (starCount == 2 && roll.Value < multiChance * 0.4f) starCount = 3;

            var p = SystemGenParams.Default;
            p.forceClass = cls;
            p.starCount = starCount;
            p.metallicityOverride = feh;
            p.youthBias = youth;
            p.habitabilityBoost = habBoost;

            string massyNote = hotFrac > 0.6f
                ? "very bright/hot → massive, short-lived giant: few terrestrial worlds"
                : (cls == SpectralClass.M ? "cool red dwarf: long-lived, tight HZ, flare-limited" : "sun-like range");
            string summary =
                $"Seed {seed}\n" +
                $"Colour {c.temperatureK:0} K → {cls} ({massyNote})\n" +
                $"Position r={c.radiusFrac:0.00} of disk, arm={c.armProximity:0.00}, nebula={c.nebulaProximity:0.00}\n" +
                $"[Fe/H] {feh:0.00} → habitability ×{habBoost:0.00}\n" +
                $"Youth/volatility {youth:0.00}   |   {starCount}-star system";

            return new StarInferenceResult { seed = seed, genParams = p, metallicity = feh, summary = summary };
        }
    }
}
