using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// COLLABORATIVE S0 → S1 TRANSITION: 3 FOUNDERS  (CellStage_Decisions §11)
    ///
    /// S0 does not graduate individually. The pool watches a shared counter of protocells that
    /// have achieved stable replication (genome.replicator.Count >= MinReplicatorLength &&
    /// replicatorGenerations >= MinGenerations). When that counter hits the threshold the
    /// pool "collapses": the 3 most genetically distinct surviving lineages become the S1
    /// founders, and every player in the pool respawns as a member of one of those 3.
    ///
    /// Cells that do not want to transition at the collapse moment are offered the virus path
    /// instead. This is never presented as a failure — it's an explicit choice.
    ///
    /// TidePool-level: Stage0Replication holds one FounderCollapse and checks ReadyToCollapse
    /// each tick; when true it calls Collapse() which returns the 3 founder genomes (plus the living
    /// cells that carry them, for the founder picker). CellStageWorld then transitions to Stage1Replicator.
    /// </summary>
    public sealed class FounderCollapse
    {
        // ── tuning ─────────────────────────────────────────────────────────────────────────
        const int MaxThreshold = 20;         // replicating protocells needed in a full pool
        const int MinThreshold = 4;          // …and in a thin one (a quiet single-player pool must still be able to collapse)
        const float ThresholdShare = 0.25f;  // threshold = 25 % of the living pool, clamped to [Min, Max]
        const int MinReplicatorLength = 3;   // chain must be at least this long to count
        const int MinGenerations = 2;        // must have passed the replicator through this many divisions
        public const int FounderCount = 3;   // number of distinct founding lineages

        // ── state ──────────────────────────────────────────────────────────────────────────
        int replicatingCount, threshold = MaxThreshold;
        bool collapsed;

        public bool ReadyToCollapse => !collapsed && replicatingCount >= threshold;
        public int ReplicatingCount => replicatingCount;
        public int Threshold => threshold;

        static bool Eligible(Protocell c) => !c.dead && c.ReplicatorCount > 0
                                             && c.genome.replicator.Count >= MinReplicatorLength
                                             && c.replicatorGenerations >= MinGenerations;

        // ── public API ─────────────────────────────────────────────────────────────────────

        /// Called each tick by Stage0Replication after the cell list update.
        public void Track(IReadOnlyList<Protocell> cells)
        {
            replicatingCount = 0; int alive = 0;
            foreach (var c in cells)
            {
                if (c.dead) continue;
                alive++;
                if (Eligible(c)) replicatingCount++;
            }
            threshold = Mathf.Clamp(Mathf.RoundToInt(alive * ThresholdShare), MinThreshold, MaxThreshold);
        }

        /// Select the 3 most genetically distinct surviving lineages. Greedy farthest-point sampling:
        /// seed with a representative of the most abundant replicating species, then repeatedly add the
        /// candidate whose minimum genome distance to the chosen set is largest. `carriers[i]` is the living
        /// cell representing founder i (null when a founder had to be synthesised by mutation).
        public List<Genome> Collapse(IReadOnlyList<Protocell> cells, ref DetRng r, out List<Protocell> carriers)
        {
            collapsed = true;
            carriers = new List<Protocell>();
            var founders = new List<Genome>();

            var candidates = new List<Protocell>();
            foreach (var c in cells) if (Eligible(c)) candidates.Add(c);
            if (candidates.Count == 0) return founders;

            // seed: the most abundant replicating species; its strongest replicator carrier represents it
            var bySpecies = new Dictionary<int, int>();
            foreach (var c in candidates) bySpecies[c.SpeciesId] = (bySpecies.TryGetValue(c.SpeciesId, out int n) ? n : 0) + 1;
            int topSpecies = candidates[0].SpeciesId, topCount = 0;
            foreach (var kv in bySpecies) if (kv.Value > topCount) { topCount = kv.Value; topSpecies = kv.Key; }
            Protocell seed = null;
            foreach (var c in candidates)
                if (c.SpeciesId == topSpecies && (seed == null || c.ReplicatorCount > seed.ReplicatorCount)) seed = c;
            carriers.Add(seed); candidates.Remove(seed);

            // farthest-point: maximise the minimum distance to everything already chosen
            while (carriers.Count < FounderCount && candidates.Count > 0)
            {
                Protocell best = null; float bestD = -1f;
                foreach (var c in candidates)
                {
                    float d = float.MaxValue;
                    foreach (var f in carriers) d = Mathf.Min(d, c.genome.Distance(f.genome));
                    if (d > bestD) { bestD = d; best = c; }
                }
                carriers.Add(best); candidates.Remove(best);
            }
            foreach (var c in carriers) founders.Add(c.genome.Clone());

            // a thin pool may have fewer than 3 distinct replicating lineages: fill with strong mutants of the last
            while (founders.Count < FounderCount)
            {
                founders.Add(founders[founders.Count - 1].Mutate(ref r, 2f));
                carriers.Add(null);
            }
            return founders;
        }

        /// The founder whose genome is closest to the player's (the lineage the player most resembles).
        public static int AssignPlayerToFounder(Genome playerGenome, List<Genome> founders)
        {
            int best = 0; float bestDist = float.MaxValue;
            for (int i = 0; i < founders.Count; i++)
            {
                float d = playerGenome.Distance(founders[i]);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}
