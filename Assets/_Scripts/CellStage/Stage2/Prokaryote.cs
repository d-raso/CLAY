using System;
using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Stage0;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage.Stage2
{
    /// <summary>
    /// One living prokaryotic cell in S2. Analogous to Stage1.Replicant.
    ///
    /// Key differences vs S1:
    ///   • Owns a Metabolism that drifts via HGT and the niche it lives in (no explicit menu choice).
    ///   • Commits to a DOMAIN (bacteria / archaea) under environmental pressure: extreme habitats (vents, hot,
    ///     cold, brine) push toward archaea — tougher, and maturing faster toward S3; mild water toward bacteria —
    ///     faster and more versatile.
    ///   • Tracks oxygenTolerance and suffers when O₂ arrives without it.
    ///   • Emits O₂ to PlanetHistoryLedger when metabolising oxygenically.
    ///   • Has a biofilm slot — non-null while inside a BiofilmPatch.
    ///   • S3 gate fields: domainMaturity, cellSizeProgress, hasProtoEngulf.
    /// </summary>
    public sealed class Prokaryote : ILivingBody
    {
        // ── ILivingBody ──────────────────────────────────────────────────────
        public Vector2 pos  { get; set; }
        public Vector2 vel  { get; set; }
        public float   area { get; set; }
        public float   birthArea;
        public bool    dead { get; private set; }
        public bool    player;
        public string  causeOfDeath;
        Genome   ILivingBody.Genome             => genome;
        Vector2  ILivingBody.Position           => pos;
        bool     ILivingBody.IsAlive            => !dead;
        bool     ILivingBody.IsPlayerControlled => player;
        public int SpeciesId { get; set; }

        // ── Identity ─────────────────────────────────────────────────────────
        public Genome genome;
        public int    generation;
        public float  burstFlash;   // visual; decays each frame
        public float  wanderSeed;   // deterministic wander offset (set at birth)
        public float  enzymeQuality = 0.6f;   // carried over from S1 ribozymes; drifts on division

        public float Radius => Mathf.Sqrt(area / Mathf.PI);

        // ── Energy ──────────────────────────────────────────────────────────
        public ProkaryoteEnergy energy = new();

        // ── Environment (written by Stage2Mode each tick) ────────────────────
        public float envExtreme;              // 0..1 how extreme the local habitat is (archaea pressure)
        public Metabolism bestLocalMetabolism = Metabolism.Fermentation;   // what would earn most right here

        // ── Metabolism / chemistry ───────────────────────────────────────────
        public float oxygenStress;
        public float aerobicAdaptationProgress;

        // ── Social ───────────────────────────────────────────────────────────
        public BiofilmPatch biofilm;
        public float contactTimer;
        public float hgtProximityBias;
        public float hgtFlash;                // brief visual pulse when genes were exchanged

        // ── S3 gate (all hidden from player) ─────────────────────────────────
        public float domainMaturity;
        public float domainLean;              // −1 bacteria … +1 archaea (commits at ±1)
        public float cellSizeProgress;
        public bool hasProtoEngulf;
        public float dimpleFlash;
        public event Action OnProtoEngulfMutation;

        // ── Division ─────────────────────────────────────────────────────────
        public float divisionTimer;
        public const float DivisionInterval = 12f;    // minimum seconds between divisions (half this at most)
        public bool ReadyToDivide => area >= birthArea * 1.8f && divisionTimer >= DivisionInterval * 0.5f;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public void Tick(float dt, float ambientO2, float lightLevel, float mineralRedox, float organicConcentration,
                         ref DetRng r, PlanetHistoryLedger ledger, CellStageProgress prog)
        {
            if (dead) return;

            // 1. Harvest + allocate (upkeep first; surplus grows the cell)
            energy.Harvest(genome.metabolism, ambientO2, lightLevel, mineralRedox, organicConcentration, dt);
            energy.Allocate(dt, area, enzymeQuality);
            area += energy.surplus * 0.6f;

            // 2. O₂ stress
            TickOxygenStress(ambientO2, dt, prog);

            // 3. Oxygenic photosynthesis writes the planet's history
            if (genome.metabolism == Metabolism.OxygenicPhoto)
                ledger.AddOxygen(area * dt * 0.02f * lightLevel);

            // 4. Domain: commit under environmental pressure, then mature (archaea faster)
            TickDomain(dt);

            // 5. Size toward the S3 gate: slowly with time, faster with surplus
            cellSizeProgress = Mathf.Clamp01(cellSizeProgress + dt * (0.0008f + energy.surplusRate * 0.01f));

            // 6. Proto-engulf: a rare one-time mutation in mature, large cells (per-second chance)
            if (!hasProtoEngulf && domainMaturity > 0.7f && cellSizeProgress > 0.6f && r.NextFloat() < 0.004f * dt)
            {
                hasProtoEngulf = true;
                dimpleFlash = 1f;
                OnProtoEngulfMutation?.Invoke();
                genome.parts["ProtoEngulfDimple"] = 0.1f;   // a nascent part (Genome.parts: name → development 0..1)
            }

            // 7. Post-GOE aerobic adaptation
            if (ledger.GoeFired && genome.metabolism != Metabolism.Respiration)
                aerobicAdaptationProgress = Mathf.Clamp01(aerobicAdaptationProgress + dt * 0.003f);

            // 8. Timers, visuals
            divisionTimer += dt;
            hgtFlash = Mathf.Max(0f, hgtFlash - dt * 1.5f);
            dimpleFlash = Mathf.Max(0f, dimpleFlash - dt * 0.4f);

            // 9. Starvation
            if (energy.deficit > ProkaryoteEnergy.DeficitLimit) Kill("starvation");
        }

        void TickOxygenStress(float ambientO2, float dt, CellStageProgress prog)
        {
            bool tolerant = genome.oxygenTolerance > 0.4f || genome.metabolism == Metabolism.Respiration;
            if (tolerant) { oxygenStress = Mathf.MoveTowards(oxygenStress, 0f, dt * 0.05f); return; }
            float dose = ambientO2 * dt * 0.03f * (genome.metabolism == Metabolism.OxygenicPhoto ? 2f : 1f);   // producers sit in their own O₂
            if (biofilm != null) dose = biofilm.ReducedOxygenStress(dose);
            oxygenStress = Mathf.Clamp01(oxygenStress + dose);
            // O₂ self-harm (CellStage_Decisions §6 / §12): switched on by the GOE (prog.oxygenSelfHarm > 0)
            if (oxygenStress > 0.8f && prog.oxygenSelfHarm > 0f)
            {
                area = Mathf.Max(0.05f, area - dt * 0.03f * prog.oxygenSelfHarm * 2f);
                if (area <= birthArea * 0.3f) Kill("oxygen poisoning");
            }
        }

        void TickDomain(float dt)
        {
            if (genome.domain == Domain.Undecided)
            {
                // extremes (vents, heat, cold, brine) lean archaea; mild water leans bacteria
                domainLean += (envExtreme - 0.4f) * 0.03f * dt;
                if (domainLean >= 1f) genome.domain = Domain.Archaea;
                else if (domainLean <= -1f) genome.domain = Domain.Bacteria;
                return;
            }
            float rate = genome.domain == Domain.Archaea ? 0.004f : 0.0025f;   // Asgard-like archaea head toward S3 faster
            domainMaturity = Mathf.Clamp01(domainMaturity + rate * dt);
        }

        /// Divide: the child inherits a mutated genome; its metabolism may drift toward the local niche, oxygen
        /// tolerance drifts (pushed upward once O₂ is around), and post-GOE tolerant lineages can turn to respiration.
        public Prokaryote Divide(ref DetRng r, CellStageProgress prog, float ambientO2, bool goeFired)
        {
            divisionTimer = 0f;
            float childArea = area * 0.5f;
            area = childArea; birthArea = childArea;

            var g = genome.Mutate(ref r, 1f, prog.mutationMeltdown);
            if (g.metabolism != bestLocalMetabolism && r.NextFloat() < 0.2f) g.metabolism = bestLocalMetabolism;
            if (g.metabolism == Metabolism.AnoxygenicPhoto && r.NextFloat() < 0.06f) g.metabolism = Metabolism.OxygenicPhoto;   // water-splitting appears
            g.oxygenTolerance = Mathf.Clamp01(g.oxygenTolerance + (ambientO2 > 0.01f ? r.Range(-0.02f, 0.06f) : r.Range(-0.03f, 0.03f)));
            if (goeFired && g.oxygenTolerance >= 0.5f && g.metabolism != Metabolism.Respiration && r.NextFloat() < 0.2f) g.metabolism = Metabolism.Respiration;

            var child = new Prokaryote
            {
                pos        = pos + r.InsideUnitCircle().normalized * Radius * 0.6f,
                vel        = -vel * 0.3f,
                area       = childArea,
                birthArea  = childArea,
                genome     = g,
                generation = generation + 1,
                wanderSeed = r.NextFloat() * 100f,
                enzymeQuality = Mathf.Clamp01(enzymeQuality + r.Range(-0.03f, 0.04f)),
                domainLean = domainLean,
                domainMaturity = domainMaturity * 0.9f,          // maturity is mostly heritable
                cellSizeProgress = cellSizeProgress * 0.85f,
            };
            energy.Split(child.energy);
            generation++;
            return child;
        }

        public void Kill(string cause)
        {
            if (dead) return;
            dead = true;
            causeOfDeath = cause;
            burstFlash = 1f;
            biofilm?.Remove(this);
        }
    }
}
