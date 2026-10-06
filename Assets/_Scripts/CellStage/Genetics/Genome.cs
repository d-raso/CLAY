using System;
using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.CellStage.Genetics
{
    /// <summary>
    /// A cell's heritable identity (CellStage_Decisions §2): FUNCTION genes change only by mutation / behaviour
    /// pressure / gene transfer; APPEARANCE genes are the player's creative choice in the editor. The replicator
    /// chain of Stage 0 is the earliest genome — later stages add genes to the same object.
    /// </summary>
    [Serializable]
    public sealed class Genome
    {
        // ── Stage 0/1: the replicator itself (a sequence of monomer kinds) ──
        public List<int> replicator = new();
        public float copyFidelity = 0.85f;     // chance each monomer copies correctly

        // ── function (mutation-driven) ──
        public float membraneToughness = 0.5f; // burst threshold / UV resistance
        public float lipidAffinity = 0.5f;     // how well the membrane takes up fatty acids
        public float uptake = 0.5f;            // monomer absorption rate
        public float motility = 0.3f;          // speed (before flagella exist this is just drift/twitch)
        public float desiccationResist = 0.3f;
        public Metabolism metabolism = Metabolism.None;
        public Domain domain = Domain.Undecided;
        public float oxygenTolerance;          // 0..1
        public float investBias = 0.5f;        // S1+: share of spare energy put into better ribozymes (the rest → copying / growth)
        // S1+: metabolic pathway genes (Pathways.cs) and their expression priorities (player-regulated)
        public readonly List<Pathway> genes = new();
        public readonly float[] priority = new float[Pathways.Count];
        public int geneSlots = 3;               // (a new living lineage starts with 1; complexity buys more)
        public const int MaxGeneSlots = 6;
        public const int RnaGeneSlots = 5;      // an RNA genome breaks if it gets longer than this
        public bool ring;
        public bool ribosome;                   // RNA can build proteins: protein genes unlock
        public float leak = 0.85f;              // membrane leakiness: harvests a vent's natural proton gradient, bleeds away from it                       // the division ring (FtsZ): controlled, equal fission
        public bool rt;                         // a reverse-transcriptase gene (from a virus): RNA → DNA begins
        public int dnaStage;                    // 0..DnaStages: divisions spent converting the genome
        public const int DnaStages = 3;
        public bool Dna => dnaStage >= DnaStages;
        public int passiveSplits;
        public int s1Divisions;                 // divisions since coming alive (the ribosome needs time as well as good genes)
        public float elong, bend, lobes;        // body shape (after the ring): rod length, curve (vibrio), lobing               // clumsy divisions so far (the ring becomes likelier)
        public int SlotCap => Dna ? MaxGeneSlots : RnaGeneSlots;
        public bool Has(Pathway p) => genes.Contains(p);
        // the gene bays: each holds a 4-base sequence (null = empty). genes/eff/misfolds are READ from them (Express).
        public readonly List<int[]> bays = new();
        public readonly float[] eff = new float[Pathways.Count];   // 1 exact recipe, 0.5 one base off ("leaky")
        public int misfolds;
        public readonly int[] copies = new int[Pathways.Count];   // working copies of each job                                       // bays whose sequence makes nothing (a misfolded protein)
        public bool HasFreeBay { get { if (bays.Count < geneSlots) return true; foreach (var b in bays) if (b == null) return true; return false; } }
        public void AddGene(Pathway p, float prio)
        {
            if (genes.Contains(p) || !HasFreeBay || !Pathways.Available(p, ribosome)) return;
            var seq = (int[])Pathways.Recipe(p).Clone();
            if (p != Pathway.Fermentation) { var f = Pathways.Fold(seq); seq[f.loopStart] = (seq[f.loopStart] + 1) % 4; }   // genes picked up or drifted in work, but not perfectly
            int at = bays.IndexOf(null);
            if (at >= 0) bays[at] = seq; else bays.Add(seq);
            priority[(int)p] = prio;
            Express();
        }
        public void RemoveGene(Pathway p)
        {
            for (int i = 0; i < bays.Count; i++)
                if (bays[i] != null && Pathways.Match(bays[i], out int e, ribosome) == (int)p && e <= 2) bays[i] = null;
            priority[(int)p] = 0f;
            Express();
        }
        public void Express()
        {
            genes.Clear(); misfolds = 0; Array.Clear(eff, 0, eff.Length); Array.Clear(copies, 0, copies.Length);
            foreach (var b in bays)
            {
                if (b == null) continue;
                int p = Pathways.Best(b, ribosome, out float e);
                if (e >= 0.2f)
                {
                    if (!genes.Contains((Pathway)p)) genes.Add((Pathway)p);
                    eff[p] = Mathf.Max(eff[p], e); copies[p]++;
                    if (priority[p] <= 0f) priority[p] = 0.5f;
                }
                else misfolds++;
            }
        }
        public readonly Dictionary<string, float> parts = new();   // developed parts → development 0..1 (S2+)
        public readonly List<string> symbionts = new();             // kept organelles (S3+)

        // ── appearance (player-chosen) ──
        public float hue = 0.55f, saturation = 0.5f;
        public float wobble = 0.3f;            // membrane shape irregularity
        public float membraneThickness = 0.4f;
        public int pattern;                    // interior pattern style

        public Genome Clone()
        {
            var copy = new Genome
            {
                replicator = new List<int>(replicator), copyFidelity = copyFidelity, membraneToughness = membraneToughness, lipidAffinity = lipidAffinity,
                uptake = uptake, motility = motility, desiccationResist = desiccationResist, metabolism = metabolism, domain = domain,
                oxygenTolerance = oxygenTolerance, investBias = investBias, hue = hue, saturation = saturation, wobble = wobble, membraneThickness = membraneThickness, pattern = pattern,
            };
            foreach (var kv in parts) copy.parts[kv.Key] = kv.Value;
            copy.genes.AddRange(genes); Array.Copy(priority, copy.priority, priority.Length); copy.geneSlots = geneSlots;
            foreach (var b in bays) copy.bays.Add(b == null ? null : (int[])b.Clone());
            Array.Copy(eff, copy.eff, eff.Length); copy.misfolds = misfolds;
            copy.s1Divisions = s1Divisions; copy.ring = ring; copy.ribosome = ribosome; copy.leak = leak; copy.rt = rt; copy.elong = elong; copy.bend = bend; copy.lobes = lobes; copy.dnaStage = dnaStage; copy.passiveSplits = passiveSplits;
            copy.symbionts.AddRange(symbionts);
            return copy;
        }

        /// Small random changes to FUNCTION genes (and appearance drift the player hasn't pinned).
        /// `meltdown` > 1 is the no-nucleus setback: bigger, more often harmful changes.
        public Genome Mutate(ref DetRng r, float rate = 1f, float meltdown = 1f)
        {
            var g = Clone();
            float k = rate * meltdown;
            if (r.Value < 0.5f * rate) g.membraneToughness = Mathf.Clamp01(g.membraneToughness + r.Range(-0.08f, 0.08f) * k);
            if (r.Value < 0.5f * rate) g.lipidAffinity = Mathf.Clamp01(g.lipidAffinity + r.Range(-0.08f, 0.08f) * k);
            if (r.Value < 0.5f * rate) g.uptake = Mathf.Clamp01(g.uptake + r.Range(-0.08f, 0.08f) * k);
            if (r.Value < 0.4f * rate) g.motility = Mathf.Clamp01(g.motility + r.Range(-0.06f, 0.06f) * k);
            if (r.Value < 0.4f * rate) g.desiccationResist = Mathf.Clamp01(g.desiccationResist + r.Range(-0.08f, 0.08f) * k);
            if (r.Value < 0.3f * rate) g.copyFidelity = Mathf.Clamp(g.copyFidelity + r.Range(-0.03f, 0.04f) * rate, 0.5f, 0.995f);
            if (r.Value < 0.4f * rate) g.investBias = Mathf.Clamp(g.investBias + r.Range(-0.08f, 0.08f) * k, 0.05f, 0.95f);
            if (r.Value < 0.3f) g.hue = Mathf.Repeat(g.hue + r.Range(-0.03f, 0.03f), 1f);
            if (r.Value < 0.3f) g.wobble = Mathf.Clamp01(g.wobble + r.Range(-0.05f, 0.05f));
            return g;
        }

        /// How different two genomes are (0 = identical) — species membership & the respawn picker.
        public float Distance(Genome o)
        {
            float d = Mathf.Abs(membraneToughness - o.membraneToughness) + Mathf.Abs(lipidAffinity - o.lipidAffinity)
                    + Mathf.Abs(uptake - o.uptake) + Mathf.Abs(motility - o.motility) + Mathf.Abs(desiccationResist - o.desiccationResist)
                    + Mathf.Abs(copyFidelity - o.copyFidelity) + Mathf.Abs(investBias - o.investBias) * 0.5f;
            if (metabolism != o.metabolism) d += 0.5f;
            foreach (var p in genes) if (!o.genes.Contains(p)) d += 0.3f;
            d += Mathf.Abs(elong - o.elong) + Mathf.Abs(bend - o.bend) + Mathf.Abs(lobes - o.lobes);
            if (domain != o.domain) d += 0.5f;
            return d;
        }

        public Color Tint => Color.HSVToRGB(hue, Mathf.Lerp(0.15f, 0.8f, saturation), 0.9f);
    }

    public enum Metabolism { None, Fermentation, Chemotrophy, AnoxygenicPhoto, OxygenicPhoto, Respiration, Mixotroph }
    public enum Domain { Undecided, Bacteria, Archaea, Eukaryote, Virus }
}
