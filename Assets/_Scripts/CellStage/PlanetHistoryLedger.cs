using System;
using UnityEngine;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage
{
    /// <summary>
    /// The planet's biochemical history, written by every player's and AI cell's metabolism
    /// (CellStage_Decisions §7). Oxygenic photosynthesis raises O₂; once reductants (dissolved iron, methane)
    /// are used up, O₂ accumulates and the Great Oxidation Event fires PLANET-WIDE. Other emergent catastrophes
    /// hang off the same ledger. Stub: totals and the GOE trigger; persistence/server sync comes with the MMO layer.
    /// </summary>
    [Serializable]
    public sealed class PlanetHistoryLedger
    {
        public float oxygen;          // 0..1 atmospheric O₂ (relative)
        public float reductantSink = 1f;  // dissolved iron + methane left to soak up O₂ (rusts out the seas)
        public float methane;
        public bool greatOxidation;
        public event Action OnGreatOxidation;

        // ── S2 / GoeTracker API ──────────────────────────────────────────────
        /// Cumulative O₂ emitted by oxygenic photosynthesisers (unit-less, planet scale).
        public float TotalOxygen { get; private set; }

        /// Current ambient O₂ level 0..1 (same as `oxygen`; alias for S2 code clarity).
        public float AmbientO2 => oxygen;

        /// True once the GOE threshold has fired; written once by GoeTracker.FireGOE().
        public bool GoeFired { get; private set; }

        /// Called each frame by Stage2.Prokaryote for every OxygenicPhoto cell.
        public void AddOxygen(float amount)
        {
            TotalOxygen += amount;
            if (reductantSink > 0f) reductantSink = Mathf.Max(0f, reductantSink - amount);
            else oxygen = Mathf.Clamp01(oxygen + amount * 1e-3f);
        }

        /// Called once by GoeTracker when TotalOxygen crosses its threshold.
        public void FireGOE()
        {
            if (GoeFired) return;
            GoeFired      = true;
            greatOxidation = true;
            OnGreatOxidation?.Invoke();
        }

        /// Record one tick of metabolism by a population of `biomass` running `m`.
        public void Record(Metabolism m, float biomass, float dt)
        {
            float rate = biomass * dt * 1e-5f;
            switch (m)
            {
                case Metabolism.OxygenicPhoto:
                    AddOxygen(rate * 1000f);  // delegate to S2 API (scales to TotalOxygen units)
                    break;
                case Metabolism.Chemotrophy: case Metabolism.Fermentation:
                    methane = Mathf.Clamp01(methane + rate * 0.3f); break;
                case Metabolism.Respiration:
                    oxygen = Mathf.Max(0f, oxygen - rate * 0.5f); break;
            }
            // GOE is now driven by GoeTracker via TotalOxygen threshold rather than this inline check,
            // but keep the legacy flag for any existing callers.
            if (!greatOxidation && oxygen > 0.1f) FireGOE();
        }
    }
}
