using System;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Stage0;

namespace CLAY.CellStage.Stage2
{
    /// <summary>
    /// Drives the Great Oxidation Event (GOE) from PlanetHistoryLedger data.
    ///
    /// Science: Earth's GOE (~2.4 Ga) happened when oxygenic cyanobacteria overwhelmed
    /// the ocean's capacity to absorb O₂ via mineral oxidation. The atmosphere became
    /// permanently oxygenated, wiping out most anaerobic life while setting the stage
    /// for aerobic respiration and eukaryogenesis.
    ///
    /// Gameplay (CellStageDecisions §12, §7):
    ///   - PlanetHistoryLedger.totalOxygen accumulates from OxygenicPhoto cells.
    ///   - GoeTracker checks the ledger every frame. When threshold is crossed,
    ///     GoeFired fires ONCE and writes the ledger flag.
    ///   - The world becomes visibly different (rust staining, clarity shift).
    ///   - Aerobic respiration (Metabolism.Respiration) unlocks for all cells.
    ///   - Anaerobes without tolerance accumulate oxygenStress rapidly.
    ///
    /// GoeTracker does NOT show a UI message. The player reads the world change.
    /// </summary>
    public sealed class GoeTracker
    {
        // ── Threshold per planet type ─────────────────────────────────────────
        public const float DefaultThreshold = 150f;  // total O₂ units emitted to ledger

        /// Planet-dependent threshold: reducing, iron-rich water soaks up more O₂ (rust) before any accumulates;
        /// non-water solvents barely support oxygenic photosynthesis at all.
        public static float ThresholdFor(CellStageContext ctx)
        {
            float t = DefaultThreshold * (0.6f + (1f - ctx.redox) * 0.8f + ctx.minerals * 0.4f);
            if (ctx.solvent != CLAY.Galaxy.LiquidType.Water && ctx.solvent != CLAY.Galaxy.LiquidType.Brine) t *= 3f;
            return t;
        }

        // Post-GOE stress acceleration for intolerant anaerobes
        public const float PostGoeStressMultiplier = 3.5f;

        // ── State ─────────────────────────────────────────────────────────────
        public float threshold { get; private set; }
        public bool  goeFired => ledger != null && ledger.GoeFired;

        readonly PlanetHistoryLedger ledger;
        Stage2BiomeShift biomeShift;   // created when the GOE fires
        public Stage2BiomeShift BiomeShift => biomeShift;

        public event Action OnGoeFired;

        // ── Visual transition progress (0 = pre-GOE, 1 = fully oxidised) ─────
        public float oxidationProgress { get; private set; }

        public GoeTracker(PlanetHistoryLedger ledger, float? thresholdOverride = null)
        {
            this.ledger = ledger;
            threshold   = thresholdOverride ?? DefaultThreshold;
        }

        // ── Frame update ──────────────────────────────────────────────────────

        public void Tick(float dt, Stage2Mode mode)
        {
            if (!ledger.GoeFired)
            {
                if (ledger.TotalOxygen >= threshold)
                    FireGOE(mode);
                return;
            }

            // Smooth visual transition (rust staining, water clarity)
            oxidationProgress = Mathf.MoveTowards(oxidationProgress, 1f, dt * 0.01f);
        }

        void FireGOE(Stage2Mode mode)
        {
            ledger.FireGOE();          // writes the flag, prevents re-entry
            OnGoeFired?.Invoke();
            biomeShift = new Stage2BiomeShift();

            Debug.Log($"[CellStage] GOE fired at O₂={ledger.TotalOxygen:0.0} (threshold {threshold:0.0})");

            // Setback: O₂ producers that evolved oxygenTolerance < 0.3 self-poison
            // This is applied per-cell in Prokaryote.TickOxygenStress via prog.setbacks.oxygenSelfHarm
        }

        // ── Aerobic unlock check ──────────────────────────────────────────────

        /// Returns true if the given cell may now switch to aerobic respiration.
        public bool AerobicUnlocked(Prokaryote cell) =>
            ledger.GoeFired && cell.genome.oxygenTolerance >= 0.5f;

        // ── Visual colour hint (for renderer / post-processing) ───────────────

        /// Water tint lerps from clear (0) to rust-red (1) as the GOE unfolds.
        public Color WaterTint =>
            Color.Lerp(new Color(0.05f, 0.15f, 0.25f),
                       new Color(0.22f, 0.08f, 0.04f),
                       oxidationProgress);

        /// Particle-system oxygen-bubble density multiplier.
        public float OxyBubbleDensity => oxidationProgress * 2.5f;
    }

    /// Post-GOE appearance of the pool: dissolved iron rusts out (red-brown staining on the rock, reddened
    /// shallows), and the water clears as the iron precipitates. Applied to the pool overlay material.
    public sealed class Stage2BiomeShift
    {
        Color baseShallow, baseRock, baseCrust; bool captured;

        public void Apply(Material poolMat, float oxidationProgress)
        {
            if (poolMat == null) return;
            if (!captured)
            {
                baseShallow = poolMat.GetColor("_WaterShallow"); baseRock = poolMat.GetColor("_Rock"); baseCrust = poolMat.GetColor("_Crust");
                captured = true;
            }
            float k = Mathf.SmoothStep(0f, 1f, oxidationProgress);
            poolMat.SetColor("_Rock", Color.Lerp(baseRock, new Color(0.55f, 0.28f, 0.16f), k * 0.6f));          // banded-iron staining
            poolMat.SetColor("_Crust", Color.Lerp(baseCrust, new Color(0.78f, 0.5f, 0.32f), k * 0.5f));
            poolMat.SetColor("_WaterShallow", Color.Lerp(baseShallow, new Color(0.42f, 0.6f, 0.62f), k * 0.5f)); // clearer once the iron drops out
        }
    }
}
