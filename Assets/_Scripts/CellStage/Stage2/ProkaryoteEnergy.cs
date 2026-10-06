using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Stage0;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage.Stage2
{
    /// <summary>
    /// Energy budget for one prokaryote in S2.
    ///
    /// Harvest() → raw (local source strength × metabolism) and Allocate() → upkeep first; whatever is left is
    /// SURPLUS (grows the cell toward division and its size toward the S3 gate); any shortfall accumulates as
    /// DEFICIT (starvation — fatal past a limit, repaid slowly from surplus).
    ///
    /// S2 adds aerobic respiration as the highest-yield source — strictly post-GOE (needs O₂ in the water).
    /// </summary>
    public sealed class ProkaryoteEnergy
    {
        // ── Public state ────────────────────────────────────────────────────
        public float raw;          // harvested this tick (pre-allocation)
        public float usable;       // after enzyme conversion (+ biofilm sharing)
        public float surplus;      // this tick's usable − upkeep (≥ 0)
        public float surplusRate;  // smoothed surplus per second (growth speed, visuals)
        public float deficit;      // accumulated starvation (energy units)

        public float upkeepSpend;

        // ── Base yields: raw energy per second at full source strength ──────
        const float YieldFermentation   = 0.30f;   // dissolved organics, everywhere, low
        const float YieldChemotrophy    = 1.20f;   // vents / iron seeps
        const float YieldAnoxPhoto      = 0.60f;   // light, no O₂ produced
        const float YieldOxyPhoto       = 0.75f;   // light, splits water → O₂ (feeds the GOE)
        const float YieldAerobicResp    = 1.60f;   // O₂ × organics — post-GOE only
        const float UpkeepPerArea       = 0.04f;
        const float EnzymeConversion    = 0.65f;
        public const float DeficitLimit = 2.5f;    // starvation past this is fatal

        /// Harvest from the local environment (all inputs 0..1).
        public void Harvest(Metabolism metabolism, float ambientO2, float lightLevel, float mineralRedox, float organicConcentration, float dt)
        {
            float y = metabolism switch
            {
                Metabolism.Fermentation    => YieldFermentation * organicConcentration,
                Metabolism.None            => YieldFermentation * organicConcentration * 0.7f,
                Metabolism.Chemotrophy     => YieldChemotrophy * mineralRedox,
                Metabolism.AnoxygenicPhoto => YieldAnoxPhoto * lightLevel,
                Metabolism.OxygenicPhoto   => YieldOxyPhoto * lightLevel,
                Metabolism.Respiration     => YieldAerobicResp * ambientO2 * (0.4f + organicConcentration),
                Metabolism.Mixotroph       => (YieldFermentation * organicConcentration + YieldAnoxPhoto * lightLevel) * 0.6f,
                _                          => 0f,
            };
            raw += y * dt;
        }

        /// Convert raw → usable; pay upkeep first; the rest is surplus; shortfalls accumulate as deficit.
        public void Allocate(float dt, float area, float enzymeQuality)
        {
            usable += raw * Mathf.Lerp(0.3f, 1f, enzymeQuality) * EnzymeConversion;
            raw = 0f;
            float upkeep = UpkeepPerArea * area * dt;
            if (usable >= upkeep)
            {
                upkeepSpend = upkeep;
                surplus = usable - upkeep;
                float repay = Mathf.Min(deficit, surplus * 0.5f);     // half the surplus repays old starvation first
                deficit -= repay; surplus -= repay;
            }
            else
            {
                upkeepSpend = usable;
                deficit += upkeep - usable;
                surplus = 0f;
            }
            usable = 0f;
            surplusRate = Mathf.Lerp(surplusRate, surplus / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 0.7f));
        }

        /// Split energy 50/50 on division.
        public void Split(ProkaryoteEnergy child)
        {
            child.surplusRate = surplusRate;
            deficit *= 0.5f; child.deficit = deficit;
        }
    }
}
