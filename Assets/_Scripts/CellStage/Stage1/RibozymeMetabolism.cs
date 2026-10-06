using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Stage0;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage.Stage1
{
    /// <summary>
    /// RIBOZYME METABOLISM — energy sourcing and processing for S1 cells.
    /// (CellStage_SubStages §S1, energy sourcing table)
    ///
    /// Two-step model matching real biochemistry:
    ///   1. HARVEST:  raw molecules drawn from the environment. Rate depends on how well the
    ///                cell's metabolism gene matches the available source (biome-locked).
    ///   2. CONVERT:  raw → usable energy via ribozyme catalysis. Efficiency = ribozymeQuality.
    ///                The better your ribozymes, the less raw material you waste.
    ///
    /// Genome.metabolism is never chosen from a menu: offspring drift toward the source their parent actually
    /// lived on (Replicant tracks exposure; Divide may switch the child's metabolism to match).
    ///
    /// Szostak's constraint: the mineral surface that catalyses genome copying also catalyses ribozyme
    /// synthesis — copying and ribozyme-building in the same moment slow each other (Replicant applies it).
    /// </summary>
    public static class RibozymeMetabolism
    {
        // ── tuning ─────────────────────────────────────────────────────────────────────────
        // raw energy per second at full match + full source strength
        const float FermentationBase    = 0.25f;   // universal, low ceiling
        const float ProtonGradientBase  = 1.00f;   // high yield, proximity-gated
        const float PhototropyBase      = 0.60f;   // passive, light-gated
        const float MineralRedoxBase    = 0.70f;   // biome-gated, medium-high

        const float MinConversionEff    = 0.20f;   // worst ribozymes
        const float MaxConversionEff    = 0.90f;   // best ribozymes
        const float ConversionCapacity  = 0.8f;    // usable per second at quality 1 (×(0.3+q), × reaction rate)

        // ── Harvest ────────────────────────────────────────────────────────────────────────

        /// Raw energy harvested this tick from `source` at strength `sourceStrength` [0..1].
        public static float Harvest(Metabolism metabolism, EnergySourceType source,
                                    float sourceStrength, float ribozymeQuality, float dt)
        {
            float uptakeBonus = 1f + ribozymeQuality * 0.5f;   // better enzymes grab more substrate
            return BaseRate(source) * MetabolismMatch(metabolism, source) * sourceStrength * uptakeBonus * dt;
        }

        /// [0..1] how well `metabolism` exploits `source` (the wrong metabolism harvests at ~15 %).
        public static float MetabolismMatch(Metabolism metabolism, EnergySourceType source)
        {
            switch (source)
            {
                case EnergySourceType.OrganicFermentation:
                    return metabolism == Metabolism.Fermentation || metabolism == Metabolism.None ? 1f :
                           metabolism == Metabolism.Mixotroph ? 0.8f : 0.15f;
                case EnergySourceType.ProtonGradient:
                    return metabolism == Metabolism.Chemotrophy ? 1f :
                           metabolism == Metabolism.Mixotroph ? 0.6f : 0.15f;
                case EnergySourceType.PrimitivePhototrophy:
                    return metabolism == Metabolism.AnoxygenicPhoto || metabolism == Metabolism.OxygenicPhoto ? 1f :
                           metabolism == Metabolism.Mixotroph ? 0.7f : 0.15f;
                case EnergySourceType.MineralRedox:
                    return metabolism == Metabolism.Chemotrophy ? 0.9f :
                           metabolism == Metabolism.Fermentation ? 0.5f : 0.15f;
                default: return 0.15f;
            }
        }

        /// The metabolism a lineage drifts toward when it lives on `source`.
        public static Metabolism NativeMetabolism(EnergySourceType source) => source switch
        {
            EnergySourceType.ProtonGradient => Metabolism.Chemotrophy,
            EnergySourceType.MineralRedox => Metabolism.Chemotrophy,
            EnergySourceType.PrimitivePhototrophy => Metabolism.AnoxygenicPhoto,
            _ => Metabolism.Fermentation,
        };

        // ── Convert ────────────────────────────────────────────────────────────────────────

        public static float Efficiency(float ribozymeQuality) => Mathf.Lerp(MinConversionEff, MaxConversionEff, ribozymeQuality);

        /// Usable energy produced this tick from `rawAvailable` (the caller removes usable / Efficiency of raw).
        public static float Convert(float rawAvailable, float ribozymeQuality, float reactionRate, float dt)
        {
            float eff = Efficiency(ribozymeQuality);
            float capacity = ConversionCapacity * (0.3f + ribozymeQuality) * reactionRate * dt;
            return Mathf.Min(rawAvailable * eff, capacity);
        }

        // ── Source detection ───────────────────────────────────────────────────────────────

        /// All four sources' strengths [0..1] at a position (light, vents, seeps, organics).
        public static void SampleAll(Vector2 cellPos, TidePool pool, TidePoolBiomeGenome biome, float[] into)
        {
            float depth = pool.Depth(cellPos);
            float shallow = Mathf.Clamp01(1f - depth / 0.15f);
            into[(int)EnergySourceType.PrimitivePhototrophy] = pool.Daylight * shallow * (depth > 0.003f ? 1f : 0.3f);
            into[(int)EnergySourceType.ProtonGradient] = biome.ProtonGradientAt(cellPos);
            into[(int)EnergySourceType.MineralRedox] = biome.SeepAt(cellPos);
            into[(int)EnergySourceType.OrganicFermentation] = 0.35f + Mathf.Clamp01(depth * 3f) * 0.2f;   // dissolved organics everywhere, richer deeper
        }

        /// The dominant source FOR THIS METABOLISM (what it would actually earn most from) and its strength.
        public static (EnergySourceType type, float strength) SampleSource(Vector2 cellPos, TidePool pool, TidePoolBiomeGenome biome, Metabolism m, float[] scratch)
        {
            SampleAll(cellPos, pool, biome, scratch);
            int best = 0; float bestYield = -1f;
            for (int k = 0; k < 4; k++)
            {
                float y = BaseRate((EnergySourceType)k) * MetabolismMatch(m, (EnergySourceType)k) * scratch[k];
                if (y > bestYield) { bestYield = y; best = k; }
            }
            return ((EnergySourceType)best, scratch[best]);
        }

        /// The raw yield rate a metabolism would get at a position (AI chemotaxis compares these).
        public static float YieldAt(Vector2 pos, TidePool pool, TidePoolBiomeGenome biome, Metabolism m, float[] scratch)
        {
            var (t, s) = SampleSource(pos, pool, biome, m, scratch);
            return BaseRate(t) * MetabolismMatch(m, t) * s;
        }

        public static float BaseRate(EnergySourceType source) => source switch
        {
            EnergySourceType.OrganicFermentation => FermentationBase,
            EnergySourceType.ProtonGradient       => ProtonGradientBase,
            EnergySourceType.PrimitivePhototrophy => PhototropyBase,
            EnergySourceType.MineralRedox         => MineralRedoxBase,
            _ => FermentationBase,
        };
    }

    /// Biome-level data S1 (and S2) use to decide local energy availability: where the vent and the iron seep are
    /// and how strong. Generated deterministically from the planet context and the pool.
    public sealed class TidePoolBiomeGenome
    {
        public float ventProximityStrength;   // 0..1 peak proton gradient strength at vent centre
        public Vector2 ventCentre;            // world-space position of dominant vent/spring
        public float ventRadius;              // gradient falls off outside this radius
        public float ironSeepStrength;        // 0..1 mineral redox availability at the seep
        public Vector2 seepCentre;
        public float seepRadius;
        public float rnaStabilityMod;         // 1 = normal; >1 protects RNA (cold brine, clay); <1 degrades (vent, UV)

        /// Build from the planet context: vents scale with geothermal activity, seeps with reducing iron-rich water.
        public static TidePoolBiomeGenome Generate(CellStageContext ctx, TidePool pool, ref DetRng r)
        {
            var b = new TidePoolBiomeGenome
            {
                ventCentre = pool.RandomWetPoint(ref r, 0.12f),       // vents sit in the deeper parts
                ventProximityStrength = Mathf.Clamp01(0.3f + ctx.ventActivity * 0.7f),
                ventRadius = 6f + ctx.ventActivity * 10f,
                seepCentre = pool.RandomWetPoint(ref r, 0.04f),
                ironSeepStrength = Mathf.Clamp01((1f - ctx.redox) * (0.3f + ctx.minerals)),
                seepRadius = 8f + ctx.minerals * 8f,
                rnaStabilityMod = (ctx.tempC < 5f ? 1.3f : 1f) * (ctx.minerals > 0.65f ? 1.2f : 1f) * (ctx.ventActivity > 0.6f ? 0.8f : 1f),
            };
            return b;
        }

        /// [0..1] proton gradient strength at world position `pos`.
        public float ProtonGradientAt(Vector2 pos)
        {
            float dist = (pos - ventCentre).magnitude;
            return ventProximityStrength * Mathf.Clamp01(1f - dist / Mathf.Max(ventRadius, 0.1f));
        }

        /// [0..1] mineral-redox availability at `pos` (the iron seep's plume).
        public float SeepAt(Vector2 pos)
        {
            float dist = (pos - seepCentre).magnitude;
            return ironSeepStrength * Mathf.Clamp01(1f - dist / Mathf.Max(seepRadius, 0.1f));
        }
    }
}
