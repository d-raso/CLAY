using System;
using System.Collections.Generic;
using UnityEngine;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage
{
    /// The cell stage's sub-stages (CellStage_SubStages.md). The designer's "Stage 0: Replication" spans S0+S1.
    public enum SubStage
    {
        S0_Protocell,       // lipid bubble in the soup
        S1_Replicator,      // carries a self-copying chain (heredity)
        S2_Prokaryote,      // "Little Engines": metabolism, bacteria/archaea, gene transfer by bumping
        S3_Endosymbiosis,   // proto-eukaryote: primitive phagocytosis → endosymbiosis climax → nucleus formation gate
        S4_Eukaryote,       // agar.io free-for-all
        S5_Colony,          // multicellularity → graduate to the creature stage
        Virus,              // the semi-living alternative path (CellStage_Decisions §5)
        Graduated,
    }

    /// Every milestone in the cell stage. Graduation milestones advance the sub-stage; the others are the
    /// "supporting" milestones whose absence brings SETBACKS (CellStage_Decisions §6). Never shown as a list.
    public enum Milestone
    {
        // S0
        FirstAbsorb, MembraneGrowth, SurvivedDryPhase, FirstChain,
        StableReplicator,           // ★ graduation S0 → S1
        // S1
        FirstDivision, InheritedReplicator, CopyFidelity,
        Heredity,                   // ★ graduation S1 → S2 (lineage of N divisions carrying the replicator)
        // S2
        EnergySource, EnergyStore, GeneTransfer, OxygenTolerance, Biofilm,
        StableMetabolism,           // ★ graduation S2 → S3
        // S3
        Cytoskeleton, FirstEngulf,
        Endosymbiont,               // supporting — symbiont retained (Phase B); sets branch
        NucleusFormed,              // ★ graduation S3 → S4 (genome complexity → nuclear envelope)
        // S4
        Predation, SexualReproduction, PopulationThreshold,
        ColonyAdhesion,             // ★ graduation S4 → S5 (division adhesion + apex + pop threshold)
        // S5
        Adhesion, CellTypes, SingleCellBottleneck,
        MulticellularBody,          // ★ graduation → creature stage
    }

    /// A consequence of reaching a graduation milestone out of order. Slows, never blocks.
    public sealed class Setback
    {
        public string id;
        public Milestone[] missing;      // the supporting milestones whose absence triggers it
        public Milestone trigger;        // reached this without them
        public Action<CellStageProgress> apply;
        public bool active;
    }

    /// <summary>
    /// The lineage's progress through the cell stage: reached milestones, current sub-stage, active setbacks.
    /// Stage modes report milestones here; this decides graduation and setbacks. Purely data + rules (no UI).
    /// </summary>
    public sealed class CellStageProgress
    {
        public SubStage stage = SubStage.S0_Protocell;
        readonly HashSet<Milestone> reached = new();
        public readonly List<Setback> setbacks = new();
        public event Action<Milestone> OnMilestone;
        public event Action<SubStage, SubStage> OnStageChanged;
        /// Fired by HGTContact each time a gene transfer event completes (donor genome, recipient genome).
        public event Action<Genetics.Genome, Genetics.Genome> OnHGTEvent;
        public void RaiseHGT(Genetics.Genome donor, Genetics.Genome recipient) => OnHGTEvent?.Invoke(donor, recipient);

        // passed from FounderCollapse to Stage1Replicator on the S0→S1 transition
        public List<Genome> founderGenomes;
        public int playerFounder = -1;          // which founder lineage the player chose to join (−1 = nearest to playerSeedGenome)
        public Genome playerSeedGenome;         // the player's genome at the collapse (or the host they integrated into)

        // setback effects the stage modes read (multipliers; 1 = no effect)
        public float mutationMeltdown = 1f;   // big genome without nucleus → more broken offspring
        public float oxygenSelfHarm = 0f;     // O₂ producer without tolerance
        public float energyCap = 1f;          // mitochondria without membrane surface
        public float colonyCheaters = 0f;     // multicellular without bottleneck

        public CellStageProgress() { DefineSetbacks(); }

        public bool Has(Milestone m) => reached.Contains(m);

        public void Reach(Milestone m)
        {
            if (!reached.Add(m)) return;
            Debug.Log($"[CellStage] milestone {m}");
            OnMilestone?.Invoke(m);
            foreach (var s in setbacks)
                if (!s.active && s.trigger == m && Array.Exists(s.missing, x => !reached.Contains(x)))
                { s.active = true; s.apply(this); Debug.Log($"[CellStage] setback {s.id}"); }
            var next = GraduationTarget(m);
            if (next.HasValue) SetStage(next.Value);
        }

        public void SetStage(SubStage s)
        {
            if (s == stage) return;
            var old = stage; stage = s;
            OnStageChanged?.Invoke(old, s);
        }

        static SubStage? GraduationTarget(Milestone m) => m switch
        {
            Milestone.StableReplicator => SubStage.S1_Replicator,
            Milestone.Heredity => SubStage.S2_Prokaryote,
            Milestone.StableMetabolism => SubStage.S3_Endosymbiosis,
            Milestone.Endosymbiont => SubStage.S4_Eukaryote,
            Milestone.PopulationThreshold => SubStage.S5_Colony,
            Milestone.MulticellularBody => SubStage.Graduated,
            _ => null,
        };

        void DefineSetbacks()
        {
            setbacks.Add(new Setback { id = "mutation-meltdown", trigger = Milestone.PopulationThreshold, missing = new[] { Milestone.NucleusFormed },
                apply = p => p.mutationMeltdown = 3f });
            setbacks.Add(new Setback { id = "oxygen-self-poisoning", trigger = Milestone.StableMetabolism, missing = new[] { Milestone.OxygenTolerance },
                apply = p => p.oxygenSelfHarm = 0.5f });
            setbacks.Add(new Setback { id = "energy-capped", trigger = Milestone.Endosymbiont, missing = new[] { Milestone.Cytoskeleton },
                apply = p => p.energyCap = 0.6f });
            setbacks.Add(new Setback { id = "colony-cheaters", trigger = Milestone.MulticellularBody, missing = new[] { Milestone.SingleCellBottleneck },
                apply = p => p.colonyCheaters = 0.3f });
            setbacks.Add(new Setback { id = "fragile-heredity", trigger = Milestone.Heredity, missing = new[] { Milestone.CopyFidelity },
                apply = p => p.mutationMeltdown = Mathf.Max(p.mutationMeltdown, 1.8f) });
        }
    }
}
