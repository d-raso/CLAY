using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage.Stage0
{
    /// A polymer inside a protocell: a sequence of bases, plus an in-progress complementary copy.
    public sealed class Chain
    {
        public List<int> seq = new();
        public List<int> copy = new();     // complementary strand being assembled on this template
        public float copyTimer;
        public bool replicator;            // folds into a self-copier (contains a planet motif)
        public float glow;
        public float sinceDock = 9f;       // seconds since the last partner clicked into place (snap animation)
        public bool stalled;               // copying is waiting for a partner that isn't inside the bubble
    }

    /// <summary>
    /// A lipid bubble and the chemistry inside it (Stage 0: S0 Protocell → S1 Replicator). All state is shown on
    /// the body, never as numbers: size = membrane area, strain shimmer = osmotic pressure, wrinkles = drying,
    /// glow = a working replicator, pinching = about to divide.
    ///
    /// The rules players discover:
    ///  • taking in loose molecules raises internal pressure; linking them into chains relieves it (polymers are
    ///    osmotically cheap) — and lipids grow the membrane so it can hold more. Overfill → burst.
    ///  • chains link fastest while drying out (the low-tide film) and with clay inside — but drying too long kills.
    ///  • any chain slowly templates a complementary copy from matching bases inside; a few planet-specific motifs
    ///    fold into REPLICATORS that copy themselves fast and glow. Copies can mutate (copy fidelity).
    ///  • UV in shallow daylight water breaks chains; deep water shields but is dilute.
    ///  • a big enough bubble pinches in two, sharing its chains between the daughters.
    /// </summary>
    public sealed class Protocell : ILivingBody
    {
        public const int MaxChain = 12;
        public Vector2 pos, vel;
        public float area;                 // membrane area (radius = √(area/π))
        public float birthArea;
        public readonly int[] free = new int[MoteChem.KindCount];   // loose molecules inside (lipids join the membrane instead)
        public readonly List<Chain> chains = new();
        public float moisture = 1f;        // 1 wet … 0 dried out
        public float pinch;                // 0..1 division in progress
        public float shear;                // seconds being squeezed against rock (shear division)
        public float viroidLoad;           // 0..1 a viroid replicating inside (membrane shimmers, behaves erratically)
        public float hijackUntil;          // pool time until which an infecting viroid player is steering this cell
        public float absorbFlash;
        public bool sheltered;
        public float phStress;
        public int gainedGene = -1, daughterGained = -1;   // pathways gained at the last division (−1 none)
        public float lyse;                 // 0..1 a living cell is dissolving this protocell's membrane (S1 predation)             // 0..1 away from an alkaline spring's gradient, membranes destabilise
        public S1Life life;                // non-null once this cell has graduated to S1 (a living metabolism)
        public float contact;              // seconds touching another bubble (sustained contact → shared chemistry)             // true while the player is assembling on the seabed (no drying meanwhile)          // a brief flash when something slips in through the membrane
        public float burstFlash;
        public bool dead;
        public bool player;
        public Genome genome;
        public int generation;
        public int replicatorGenerations;  // consecutive ancestors (incl. this cell) that carried a replicator
        public float wanderSeed;
        public float lowTideSurvival;      // time spent concentrating in a drying film
        public string causeOfDeath;

        // ILivingBody
        public Genome Genome => genome;
        public Vector2 Position => pos;
        public bool IsAlive => !dead;
        public bool IsPlayerControlled => player;
        public int SpeciesId { get; set; }

        public float Radius => Mathf.Sqrt(area / Mathf.PI);
        public float Capacity => area * 3.0f;                     // roomier: more can go in before the pressure builds
        public float Load
        {
            get
            {
                float l = 0f;
                for (int k = 0; k < MoteChem.KindCount; k++) l += free[k] * MoteChem.OsmoticWeight((MoteKind)k);
                foreach (var c in chains) l += (c.seq.Count + c.copy.Count) * 0.22f;   // polymers: osmotically cheap
                return l;
            }
        }
        public float Pressure => Load / Mathf.Max(Capacity, 0.01f);
        public float BurstThreshold => 1.3f + genome.membraneToughness * 0.5f - phStress * 0.35f;
        public int ReplicatorCount { get { int n = 0; foreach (var c in chains) if (c.replicator) n++; return n; } }

        public Protocell(Genome g, Vector2 p, float a)
        {
            genome = g; pos = p; area = a; birthArea = a;
        }

        /// One tick of internal chemistry. `depth` = water above it; `uv` = UV dose rate; `rate` = planet reaction rate.
        public void Chemistry(float dt, float depth, float uv, float rate, List<int>[] motifs, ref DetRng r, CellStageProgress prog)
        {
            // ── drying: in a thin film the soup inside concentrates (fast linking); on bare ground the cell dries out ──
            bool film = depth > 0f && depth < 0.04f;
            if (depth <= 0.003f && !sheltered) moisture -= dt * 0.035f * (1.2f - genome.desiccationResist);   // ~30 s on bare rock
            else moisture = Mathf.Min(1f, moisture + dt * 0.25f);
            if (film || depth <= 0.003f) lowTideSurvival += dt;

            int bases = free[0] + free[1] + free[2] + free[3];
            float linkRate = rate * (0.06f + (film ? 0.9f : 0f) + (depth <= 0.003f ? 1.4f : 0f)) * (1f + free[(int)MoteKind.Clay] * 0.35f);

            // ── polymerise: link loose bases into chains (or extend a chain) ──
            if (bases >= 2 && r.Value < linkRate * dt * Mathf.Min(bases, 8))
            {
                int b = TakeRandomBase(ref r);
                if (b >= 0)
                {
                    Chain target = null;
                    foreach (var c in chains) if (!c.replicator && c.seq.Count < MaxChain && c.copy.Count == 0 && r.Value < 0.6f) { target = c; break; }   // a folded replicator doesn't take on extra bases
                    if (target == null)
                    {
                        int b2 = TakeRandomBase(ref r);
                        if (b2 >= 0) { target = new Chain(); target.seq.Add(b2); chains.Add(target); }
                    }
                    if (target != null) { target.seq.Add(b); Classify(target, motifs); if (target.seq.Count >= 3) prog.Reach(Milestone.FirstChain); }
                    else free[b]++;
                }
            }

            // ── template copying: matching bases pair onto a chain one by one; replicators are catalytic ──
            for (int ci = chains.Count - 1; ci >= 0; ci--)
            {
                var c = chains[ci];
                c.glow = Mathf.MoveTowards(c.glow, c.replicator ? 1f : 0f, dt * 0.6f);
                if (c.seq.Count < 3) continue;
                float copyRate = rate * (c.replicator ? 2.4f : 0.08f) * (film ? 1.5f : 1f);
                c.copyTimer += dt * copyRate;
                c.sinceDock += dt;
                while (c.copyTimer >= 1f && c.copy.Count < c.seq.Count)
                {
                    c.copyTimer -= 1f;
                    int want = MoteChem.Complement(c.seq[c.copy.Count]);
                    int got = want;
                    if (r.Value > genome.copyFidelity) got = AnyAvailableBase(ref r, want);   // mispairing = mutation
                    if (got < 0 || free[got] <= 0) { c.copyTimer = 0f; c.stalled = true; break; }   // stalls until the partner turns up
                    free[got]--; c.copy.Add(got); c.sinceDock = 0f; c.stalled = false;
                }
                if (c.copy.Count >= c.seq.Count && c.seq.Count > 0)
                {
                    var child = new Chain(); child.seq.AddRange(c.copy); c.copy.Clear();
                    Classify(child, motifs); chains.Add(child);
                }
            }

            // ── decay: hydrolysis in water, UV breakage in shallow daylight ──
            float breakRate = 0.0002f * rate + uv * 0.25f * (1.1f - genome.membraneToughness);   // near-zero background: chains break from UV / vent heat
            for (int ci = chains.Count - 1; ci >= 0; ci--)
            {
                var c = chains[ci];
                if (c.seq.Count > 1 && r.Value < breakRate * (c.replicator ? 0.6f : 1f) * dt * c.seq.Count)
                {
                    int cut = r.RangeInt(1, c.seq.Count);
                    var tail = new Chain(); tail.seq.AddRange(c.seq.GetRange(cut, c.seq.Count - cut));
                    c.seq.RemoveRange(cut, c.seq.Count - cut);
                    foreach (int b in c.copy) free[b]++; c.copy.Clear();
                    Classify(c, motifs);
                    if (tail.seq.Count >= 2) { Classify(tail, motifs); chains.Add(tail); } else foreach (int b in tail.seq) free[b]++;
                }
                if (c.seq.Count <= 1) { foreach (int b in c.seq) free[b]++; foreach (int b in c.copy) free[b]++; chains.RemoveAt(ci); }
            }
        }

        int TakeRandomBase(ref DetRng r)
        {
            int tot = free[0] + free[1] + free[2] + free[3]; if (tot <= 0) return -1;
            int pick = r.RangeInt(0, tot);
            for (int k = 0; k < 4; k++) { if (pick < free[k]) { free[k]--; return k; } pick -= free[k]; }
            return -1;
        }

        int AnyAvailableBase(ref DetRng r, int not)
        {
            int start = r.RangeInt(0, 4);
            for (int i = 0; i < 4; i++) { int k = (start + i) & 3; if (k != not && free[k] > 0) return k; }
            return free[not] > 0 ? not : -1;
        }

        /// A chain is a replicator only if it IS one of the planet's motifs — exactly that sequence, or exactly its
        /// complement (the mirror strand). Extra bases spoil the fold: AAA works, AAAC does not.
        /// Which recipe (index) this chain is, or −1.
        public static int MotifIndex(Chain c, List<int>[] motifs)
        {
            for (int i = 0; i < motifs.Length; i++)
                if (c.seq.Count == motifs[i].Count && (Contains(c.seq, motifs[i], false) || Contains(c.seq, motifs[i], true))) return i;
            return -1;
        }

        public static void Classify(Chain c, List<int>[] motifs)
        {
            c.replicator = false;
            foreach (var m in motifs)
                if (c.seq.Count == m.Count && (Contains(c.seq, m, false) || Contains(c.seq, m, true))) { c.replicator = true; return; }
        }

        static bool Contains(List<int> s, List<int> m, bool complement)
        {
            for (int i = 0; i + m.Count <= s.Count; i++)
            {
                bool ok = true;
                for (int j = 0; j < m.Count && ok; j++) ok = s[i + j] == (complement ? MoteChem.Complement(m[j]) : m[j]);
                if (ok) return true;
            }
            return false;
        }

        /// The planet's replicator motifs: what chemistry folds into a self-copier HERE. Planet-wide, not per pool.
        public static List<int>[] Motifs(ulong planetSeed)
        {
            var r = new DetRng(DetRng.Hash(planetSeed, 0x2EB1CA7EUL));
            var res = new List<int>[3];                       // three recipes per planet
            for (int i = 0; i < res.Length; i++)
            {
                res[i] = new List<int>();
                int len = i < 2 ? 3 : 4;
                for (int k = 0; k < len; k++) res[i].Add(r.RangeInt(0, 4));
            }
            return res;
        }

        /// Split into two: membrane area halves, chains and loose molecules are shared out at random.
        public Protocell Divide(ref DetRng r, CellStageProgress prog)
        {
            var g2 = genome.Mutate(ref r, 1f, prog.mutationMeltdown);
            // binary fission makes two NEW cells: either may carry a drifted gene copy
            if (life != null) { gainedGene = life.Evolve(genome, ref r); daughterGained = life.Evolve(g2, ref r); }
            if (life != null) g2.metabolism = life.DriftedMetabolism(g2.metabolism, ref r);   // daughters adapt to their niche
            var d = new Protocell(g2, pos + Random.insideUnitCircle.normalized * Radius * 0.6f, area * 0.5f)
            {
                generation = generation + 1, wanderSeed = r.Range(0f, 100f), moisture = moisture,
            };
            if (life != null) d.life = life.Inherit(ref r);
            area *= 0.5f; birthArea = area; pinch = 0f; shear = 0f;
            for (int k = 0; k < MoteChem.KindCount; k++) { int give = 0; for (int i = 0; i < free[k]; i++) if (r.Value < 0.5f) give++; free[k] -= give; d.free[k] += give; }
            for (int i = chains.Count - 1; i >= 0; i--) if (r.Value < 0.5f) { d.chains.Add(chains[i]); chains.RemoveAt(i); }
            // heredity: a working replicator goes to BOTH daughters — if one side ended up without, it gets a copy
            Chain Rep(List<Chain> list) { foreach (var ch in list) if (ch.replicator) return ch; return null; }
            var mine = Rep(chains); var theirs = Rep(d.chains);
            if (mine != null && theirs == null) { var cp = new Chain(); cp.seq.AddRange(mine.seq); cp.replicator = true; cp.glow = 1f; d.chains.Add(cp); }
            else if (theirs != null && mine == null) { var cp = new Chain(); cp.seq.AddRange(theirs.seq); cp.replicator = true; cp.glow = 1f; chains.Add(cp); }
            generation++;
            return d;
        }
    }
}
