using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Stage0;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage.Stage1
{
    /// <summary>
    /// The energy allocation system for S1 cells (CellStage_SubStages §S1 "Allocate").
    ///
    /// Each tick, part of the usable energy pool is spent, in priority order:
    ///   1. Membrane upkeep  — paid FIRST (proportional to membrane area); any shortfall is the deficit that
    ///                         stresses and shrinks the membrane.
    ///   2. The surplus is split by the genome's investBias (a mutation-driven function gene):
    ///        • ribozyme synthesis — slowly raises ribozymeQuality (better catalysts = better conversion)
    ///        • genome copying     — grows the membrane and funds the next division
    ///
    /// The player never sets sliders. They influence the economy INDIRECTLY: rich energy sources ease
    /// everything; the gene pool's investBias drifts with which variants survive; and the body shows it all
    /// (shimmer = upkeep starved, wrinkles = shrinking, glow = ribozyme quality, stretching = about to divide).
    /// </summary>
    public sealed class EnergyBudget
    {
        // ── constants (energy units per second unless noted) ───────────────────────────────
        public const float UpkeepPerArea = 0.025f;   // membrane maintenance, per unit of membrane area
        public const float CopyCost = 0.6f;          // energy that funds one division's genome copy
        public const float GrowthPerEnergy = 0.8f;   // membrane area grown per unit of copy energy
        const float SpendRate = 2f;                  // fraction of the usable pool spent per second

        // ── pools ─────────────────────────────────────────────────────────────────────────
        public float raw;         // harvested but unprocessed energy (feedstock for ribozymes)
        public float usable;      // processed energy ready to spend
        public float deficit;     // this tick's unpaid upkeep (energy units)

        // ── this-tick spend buckets (written by Allocate, consumed by Replicant) ──────────
        public float upkeepSpend;
        public float ribozymeSpend;
        public float copySpend;

        /// Distribute this tick's spending: upkeep first, the surplus by the genome's investBias.
        public void Allocate(float dt, Genome genome, float area)
        {
            float spend = usable * Mathf.Clamp01(SpendRate * dt);
            float need = UpkeepPerArea * area * dt;
            float upkeep = Mathf.Min(spend, need);
            float surplus = spend - upkeep;
            upkeepSpend = upkeep;
            deficit = need - upkeep;
            ribozymeSpend = surplus * genome.investBias;
            copySpend = surplus * (1f - genome.investBias);
            usable -= spend;
        }

        /// Split energy between parent and child on division.
        public void Split(EnergyBudget child)
        {
            child.raw    = raw    * 0.5f; raw    *= 0.5f;
            child.usable = usable * 0.5f; usable *= 0.5f;
            child.deficit = 0f;
        }
    }

    /// Which energy source is available at a given pool location.
    /// Derived from TidePoolBiomeGenome + cell position each tick.
    public enum EnergySourceType
    {
        OrganicFermentation,  // leftover S0-era organics — universal fallback
        ProtonGradient,       // vent / alkaline spring chemiosmosis — near-vent only
        PrimitivePhototrophy, // sunlit biomes — light-dependent
        MineralRedox,         // pyrite/iron seep — biome-specific
    }
}
