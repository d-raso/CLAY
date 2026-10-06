using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Genetics;
using CLAY.CellStage.Stage0;

namespace CLAY.CellStage.Stage1
{
    /// <summary>
    /// An S1 cell: a protocell that has encapsulated a self-replicating polymer and is now
    /// developing a true metabolism: an energy budget (EnergyBudget), ribozymes (ribozymeQuality) and a
    /// metabolism gene that drifts toward whatever the lineage actually lives on.
    ///
    /// All state is shown on the body, never as numbers:
    ///  • membrane shimmer intensity   = membrane stress (upkeep deficit)
    ///  • interior glow brightness     = ribozyme quality / metabolic output
    ///  • wrinkling / shrinkage        = energy starvation
    ///  • stretching toward a split    = copy progress
    ///  • offspring visible changes    = mutations (colour shift, pattern, glow level)
    ///  • structural membrane markers  = CRISPR-like immunity after viroid infection
    /// </summary>
    public sealed class Replicant : ILivingBody
    {
        // ── physics (same as Protocell) ───────────────────────────────────────────────────
        public Vector2 pos, vel;
        public float area;
        public float birthArea;
        public bool dead;
        public bool player;
        public string causeOfDeath;
        public float burstFlash;
        public float wanderSeed;

        // ── genetics ──────────────────────────────────────────────────────────────────────
        public Genome genome;
        public int generation;

        // ── S1-specific ───────────────────────────────────────────────────────────────────
        public EnergyBudget energy;
        public float ribozymeQuality;        // 0..1 — improves with ribozyme synthesis investment; mutates on copy
        public float membraneStress;         // 0..1 — rises when upkeep underfunded; collapses membrane at 1
        public float starvation;             // 0..1 — how hard the cell is shrinking (wrinkles)
        public float copyProgress;           // 0..1+ — genome copy funded; divides at 1 once big enough
        public float copyRate;               // smoothed copy energy per second (fast copying = sloppy copies)
        public float lucaProgress;           // 0..1 — how close to the LUCA gate (all three conditions met simultaneously)
        public bool immunized;               // true once a viroid infection was cleared (CRISPR-like marker)
        public float immunityStrength;       // 0..1 — resistance to reinfection
        public EnergySourceType lastSource;
        readonly float[] exposure = new float[4];   // seconds spent harvesting each source (metabolism drift)

        // ── ILivingBody ───────────────────────────────────────────────────────────────────
        public Genome Genome => genome;
        public Vector2 Position => pos;
        public bool IsAlive => !dead;
        public bool IsPlayerControlled => player;
        public int SpeciesId { get; set; }

        public float Radius => Mathf.Sqrt(area / Mathf.PI);
        public bool ReadyToDivide => copyProgress >= 1f && area >= birthArea * 1.8f;

        public Replicant(Genome g, Vector2 p, float a)
        {
            genome = g; pos = p; area = a; birthArea = a;
            energy = new EnergyBudget();
            ribozymeQuality = g.copyFidelity * 0.5f;   // starting proxy: a careful copier has decent catalysts
        }

        /// One tick of S1 biochemistry. `source` / `sourceStrength` = what this cell can best harvest here.
        public void Tick(float dt, EnergySourceType source, float sourceStrength,
                         float reactionRate, ref DetRng r, CellStageProgress prog)
        {
            if (dead) return;
            lastSource = source;

            // 1. Gather
            float rawGain = RibozymeMetabolism.Harvest(genome.metabolism, source, sourceStrength, ribozymeQuality, dt);
            energy.raw = Mathf.Min(energy.raw + rawGain, 4f + area);
            if (rawGain > 0f) exposure[(int)source] += dt;

            // 2. Process: raw → usable via ribozymes (worse ribozymes waste more raw per usable)
            float usable = RibozymeMetabolism.Convert(energy.raw, ribozymeQuality, reactionRate, dt);
            energy.raw -= usable / RibozymeMetabolism.Efficiency(ribozymeQuality);
            energy.usable += usable;

            // 3. Allocate: upkeep first, surplus by investBias
            energy.Allocate(dt, genome, area);

            // Szostak: copying and ribozyme-building share the same mineral catalysis — doing both at once slows both
            if (energy.copySpend > 0f && energy.ribozymeSpend > 0f) { energy.copySpend *= 0.8f; energy.ribozymeSpend *= 0.8f; }

            // 4. Apply
            ApplyMembraneUpkeep(dt);
            ApplyRibozymeSynthesis(dt);
            ApplyGenomeCopy(dt);

            // 5. LUCA progress
            UpdateLucaProgress(dt, prog);

            // 6. Collapse / starvation
            if (membraneStress >= 1f) Kill("membrane collapse");
            else if (area < birthArea * 0.35f) Kill("starvation");
        }

        // ── private helpers ───────────────────────────────────────────────────────────────

        void ApplyMembraneUpkeep(float dt)
        {
            // unpaid upkeep stresses the membrane (shimmer) and shrinks it (wrinkles); a tough membrane copes better
            float need = EnergyBudget.UpkeepPerArea * area * dt;
            float shortfall = need > 0f ? energy.deficit / need : 0f;               // 0 = fully paid … 1 = nothing paid
            float toughness = 0.6f + genome.membraneToughness * 0.8f;
            membraneStress = Mathf.Clamp01(membraneStress + (shortfall * 0.015f / toughness - (1f - shortfall) * 0.04f) * dt);   // ~1–2 min of total starvation to collapse
            starvation = Mathf.MoveTowards(starvation, shortfall, dt * 0.5f);
            area = Mathf.Max(0.05f, area - energy.deficit * 0.5f);
            energy.upkeepSpend = 0f;
        }

        void ApplyRibozymeSynthesis(float dt)
        {
            if (energy.ribozymeSpend <= 0f) return;
            float ceiling = 0.5f + genome.copyFidelity * 0.5f;                     // the genome limits how good ribozymes get
            ribozymeQuality = Mathf.Min(ribozymeQuality + energy.ribozymeSpend * 0.05f, ceiling);
            energy.ribozymeSpend = 0f;
        }

        void ApplyGenomeCopy(float dt)
        {
            copyRate = Mathf.Lerp(copyRate, energy.copySpend / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt));
            if (energy.copySpend <= 0f) return;
            // copy energy both grows the membrane and advances the genome copy; division happens in the mode
            area += energy.copySpend * EnergyBudget.GrowthPerEnergy;
            copyProgress += energy.copySpend / EnergyBudget.CopyCost;
            energy.copySpend = 0f;
        }

        void UpdateLucaProgress(float dt, CellStageProgress prog)
        {
            bool stableMemb = membraneStress < 0.15f;
            bool reliableMetab = ribozymeQuality > 0.65f && genome.metabolism != Metabolism.None;
            bool workingRibozyme = ribozymeQuality > 0.5f;

            float target = (stableMemb ? 0.34f : 0f) + (reliableMetab ? 0.33f : 0f) + (workingRibozyme ? 0.33f : 0f);
            lucaProgress = Mathf.MoveTowards(lucaProgress, target, dt * 0.05f);

            if (lucaProgress >= 0.99f && player)
                prog.Reach(Milestone.Heredity);   // LUCA integration = S1 graduation gate
        }

        public void Kill(string cause)
        {
            dead = true; causeOfDeath = cause; burstFlash = 1f;
        }

        /// Divide into two S1 cells. Offspring inherit a mutated genome and half the energy/area.
        /// Copying fast (high copyRate) makes sloppier copies; the child's metabolism may drift toward the source the
        /// parent actually lived on.
        public Replicant Divide(ref DetRng r, CellStageProgress prog)
        {
            float sloppy = 1f + Mathf.Clamp01(copyRate / 0.05f - 1f);                // > 1 when copying faster than the careful pace
            var g2 = genome.Mutate(ref r, sloppy, prog.mutationMeltdown);
            // metabolism drift: the lineage adapts to its niche (never a menu choice)
            int dom = 0; float total = 0f;
            for (int k = 0; k < 4; k++) { total += exposure[k]; if (exposure[k] > exposure[dom]) dom = k; }
            var native = RibozymeMetabolism.NativeMetabolism((EnergySourceType)dom);
            if (total > 5f && g2.metabolism != native && exposure[dom] / total > 0.5f && r.Value < 0.35f)
                g2.metabolism = native;
            else if (g2.metabolism == Metabolism.None && r.Value < 0.3f)
                g2.metabolism = Metabolism.Fermentation;

            float rq2 = Mathf.Clamp01(ribozymeQuality + r.Range(-0.05f, 0.04f) * sloppy);
            var child = new Replicant(g2, pos + r.InsideUnitCircle().normalized * Radius * 0.6f, area * 0.5f)
            {
                generation = generation + 1,
                wanderSeed = r.Range(0f, 100f),
                ribozymeQuality = rq2,
            };
            area *= 0.5f; birthArea = area;
            copyProgress = 0f;
            energy.Split(child.energy);
            for (int k = 0; k < 4; k++) exposure[k] *= 0.5f;
            generation++;
            return child;
        }

        /// Called when a viroid has been cleared (or lysed from). Builds CRISPR-like immunity, which also leaves a
        /// visible structural marker on the membrane (thicker, patterned — drawn by Stage1Replicator).
        public void GainImmunity(ref DetRng r)
        {
            immunized = true;
            immunityStrength = Mathf.Min(1f, immunityStrength + r.Range(0.1f, 0.3f));
        }
    }
}
