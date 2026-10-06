using UnityEngine;

namespace CLAY.CellStage.Genetics
{
    /// <summary>
    /// THE CELL'S CATALYSTS — what its genes do (CellStage_Metabolism.md).
    ///
    /// S1 is the RNA world: there are no proteins yet. A gene is a strand of RNA that FOLDS, and the fold itself is the
    /// catalyst (a ribozyme). Energy at this stage is mostly chemistry and place:
    ///   · the natural PROTON GRADIENT at alkaline vents, harvested through a leaky membrane (Genome.leak — not a gene);
    ///   · H₂ + CO₂ chemistry on iron–sulfur mineral grains the cell swallows (FeS cofactors) — the CO₂-fixing ribozyme;
    ///   · breaking down organic matter — the thioester ribozyme.
    /// One special ribozyme is THE RIBOSOME: when a lineage evolves it, RNA starts building PROTEINS, and protein genes
    /// unlock — a membrane proton PUMP (tight membrane, free to leave the vents), sulfur and iron oxidation, lysis.
    /// Light comes later still (S2): rhodopsin, then anoxygenic photosynthesis.
    ///
    /// Each gene is a 4-base sequence; each pathway has a best sequence fixed per planet. Exact = full power, one base
    /// off ≈ half, two off ≈ a fifth, further = misfolded (a useless, costly strand).
    /// </summary>
    public enum Pathway { Fermentation, Rhodopsin, AnoxygenicPhoto, Hydrogenotrophy, SulfurOxidation, IronOxidation, Lysis, ProtonPump, Ribosome, Adaptor }

    public static class Pathways
    {
        public const int Count = 10;

        public struct Info
        {
            public string name, source, science;
            public float baseRate;      // raw energy / s at full priority and full source strength
            public float upkeep;        // energy / s just to keep the gene and what it makes
            public Color pigment;       // how the pathway colours the cell (a = strength)
            public Color knot;          // the gene's colour (as a fold, and as a free RNA scrap)
            public bool protein;        // needs the ribosome
        }

        static readonly Info[] info =
        {
            new Info { knot = new Color(0.92f, 0.72f, 0.38f), name = "Thioester ribozyme", source = "organic matter", baseRate = 0.3f, upkeep = 0.004f,
                science = "Breaks organic molecules down, storing the energy as thioesters — a simple chemical energy currency older than ATP." },
            new Info { knot = new Color(0.72f, 0.36f, 0.82f), name = "Rhodopsin pump", source = "light", baseRate = 0.35f, upkeep = 0.006f, pigment = new Color(0.62f, 0.22f, 0.5f, 0.55f), protein = true,
                science = "A light-driven proton pump. Comes later (S2)." },
            new Info { knot = new Color(0.86f, 0.3f, 0.36f), name = "Anoxygenic photosynthesis", source = "light + sulfide/iron", baseRate = 0.85f, upkeep = 0.014f, pigment = new Color(0.55f, 0.18f, 0.2f, 0.6f), protein = true,
                science = "Bacteriochlorophyll photosynthesis without oxygen. Comes later (S2)." },
            new Info { knot = new Color(0.36f, 0.82f, 0.9f), name = "CO₂-fixing ribozyme", source = "H₂ + CO₂ on iron–sulfur grains", baseRate = 1.0f, upkeep = 0.01f,
                science = "Turns H₂ and CO₂ into small organic molecules, using iron–sulfur mineral grains as its catalytic heart (our enzymes still carry FeS clusters). Vents only, and only as good as the FeS you hold." },
            new Info { knot = new Color(0.95f, 0.9f, 0.4f), name = "Sulfur oxidation", source = "sulfide (vent plumes)", baseRate = 0.8f, upkeep = 0.009f, pigment = new Color(0.85f, 0.8f, 0.45f, 0.35f), protein = true,
                science = "A protein that oxidises H₂S from black smokers; stores sulfur granules." },
            new Info { knot = new Color(0.78f, 0.4f, 0.18f), name = "Iron oxidation", source = "dissolved iron (seeps)", baseRate = 0.6f, upkeep = 0.008f, pigment = new Color(0.65f, 0.32f, 0.12f, 0.5f), protein = true,
                science = "A protein that oxidises Fe²⁺ to rust. Poor yield, steady supply." },
            new Info { knot = new Color(0.45f, 0.85f, 0.45f), name = "Lysis enzymes", source = "other cells", baseRate = 0f, upkeep = 0.007f, protein = true,
                science = "Secreted proteins that dissolve other membranes: digest protocells faster, and smaller living cells at all." },
            new Info { knot = new Color(0.55f, 0.95f, 0.85f), name = "Proton coupler", source = "the vent's proton gradient", baseRate = 0f, upkeep = 0.008f,
                science = "Couples the vent's natural proton gradient (alkaline vent fluid against acidic water) to making energy. The better it's built, the more of the gradient you capture, and the less you bleed away from it." },
            new Info { knot = new Color(1f, 0.95f, 0.8f), name = "Peptide-maker (proto-ribosome)", source = "amino acids", baseRate = 0f, upkeep = 0.006f,
                science = "An RNA that links amino acids into short peptides. The closer its fold to the right one, the faster it works. Even random peptides help: they coat the membrane and steady your RNA." },
            new Info { knot = new Color(0.95f, 0.7f, 0.8f), name = "Adaptor (proto-tRNA)", source = "amino acids", baseRate = 0f, upkeep = 0.004f,
                science = "A small RNA that pairs particular bases with particular amino acids. With adaptors the peptides stop being random — RNA starts to specify protein: the genetic code." },
        };

        public static Info Get(Pathway p) => info[(int)p];

        /// What a genome can express: RNA-world catalysts always; proteins once it has a ribosome; light not before S2.
        public static bool LightEra;
        public static bool Available(Pathway p, bool ribosome)
        {
            if (p == Pathway.Rhodopsin || p == Pathway.AnoxygenicPhoto) return LightEra && ribosome;
            if (p == Pathway.Ribosome || p == Pathway.Adaptor) return false;   // (retired: the ribosome comes from three good energy genes)
            return !info[(int)p].protein || ribosome;
        }
        public static bool ProteinEraDefault;   // set per frame from the player's genome (for matching with no genome at hand)

        // ── GENES as FOLDING CHAINS ─────────────────────────────────────────────────────────────────────────────────
        // A gene is just a chain of bases (2–12). Its SHAPE is not set by hand: it FOLDS by itself — the two ends zip
        // together wherever their bases pair (A↔B, C↔D) into a STEM, one unpaired base may kink it as a BULGE, and
        // whatever is left in the middle hangs as the LOOP (the pocket). If the ends don't pair, it's a floppy strand
        // that barely works. Change a base, pull one out, add one — and the gene refolds.
        // Each job has an OPTIMAL ENGINE (seeded per planet): a stem of 2–4 rungs, a loop of 2–4, a bulge or not, and
        // loop bases from the S0 recipes. Efficiency (0–1) = how close the folded gene comes to it.
        public const int MinLen = 2, MaxLen = 12;
        // side BRANCHES: a base hanging off one base of the chain. Stored after the chain as BranchTag + parent·4 + base.
        // A branch on a LOOP base reaches into the pocket (it counts as part of the loop); elsewhere it's just shape.
        public const int BranchTag = 1000, BridgeTag = 2000, MaxBranches = 6;
        public static int[] Main(int[] s)
        {
            int n = 0; foreach (var v in s) if (v < BranchTag) n++;   // (the chain only)
            if (n == s.Length) return s;
            var m = new int[n]; int k = 0; foreach (var v in s) if (v < BranchTag) m[k++] = v; return m;
        }
        public static void Branches(int[] s, System.Collections.Generic.List<Vector2Int> into)
        {
            into.Clear(); foreach (var v in s) if (v >= BranchTag && v < BridgeTag) into.Add(new Vector2Int((v - BranchTag) / 4, (v - BranchTag) % 4));
        }
        /// Bridges: (parent A, parent B, base) — a base bonded to two beads (a triangle with their own bond).
        public static void Bridges(int[] s, System.Collections.Generic.List<Vector3Int> into)
        {
            into.Clear(); foreach (var v in s) if (v >= BridgeTag) { int x = (v - BridgeTag) / 4; into.Add(new Vector3Int(x / 16, x % 16, (v - BridgeTag) % 4)); }
        }
        public static int[] Encode(System.Collections.Generic.List<int> main, System.Collections.Generic.List<Vector2Int> br, System.Collections.Generic.List<Vector3Int> bg = null)
        {
            int nb = bg != null ? bg.Count : 0;
            var o = new int[main.Count + br.Count + nb];
            for (int i = 0; i < main.Count; i++) o[i] = main[i];
            for (int j = 0; j < br.Count; j++) o[main.Count + j] = BranchTag + br[j].x * 4 + br[j].y;
            for (int j = 0; j < nb; j++) o[main.Count + br.Count + j] = BridgeTag + (bg[j].x * 16 + bg[j].y) * 4 + bg[j].z;
            return o;
        }
        public static ulong PlanetSeed;
        public static System.Collections.Generic.List<int>[] Motifs; static object builtFor; static ulong builtSeed = ulong.MaxValue;
        struct Target { public int stem, loop, bulge; public int[] loopBases; }
        static Target[] targets;
        static readonly Pathway[] MotifOrder = { Pathway.Fermentation, Pathway.Hydrogenotrophy, Pathway.ProtonPump, Pathway.Ribosome, Pathway.Adaptor, Pathway.SulfurOxidation, Pathway.IronOxidation, Pathway.Lysis };
        public static int Comp(int b) => b ^ 1;

        static void Build()
        {
            if (targets != null && builtSeed == PlanetSeed && builtFor == Motifs) return;
            builtSeed = PlanetSeed; builtFor = Motifs;
            var r = new CLAY.Galaxy.DetRng(CLAY.Galaxy.DetRng.Hash(PlanetSeed, 0x6E7E5UL));
            targets = new Target[Count];
            var frags = new System.Collections.Generic.List<int[]>();
            if (Motifs != null)
                for (int len = 3; len >= 2; len--)
                    foreach (var m in Motifs)
                        for (int st = 0; m != null && st + len <= m.Count; st++)
                        {
                            var f = m.GetRange(st, len).ToArray();
                            bool dup = false; foreach (var e in frags) if (e.Length == f.Length && System.Linq.Enumerable.SequenceEqual(e, f)) dup = true;
                            if (!dup) frags.Add(f);
                        }
            int fi = 0;
            var order = new System.Collections.Generic.List<Pathway>(MotifOrder);
            for (int k = 0; k < Count; k++) if (!order.Contains((Pathway)k)) order.Add((Pathway)k);
            foreach (var p in order)
            {
                int[] lb;
                if (fi < frags.Count) lb = frags[fi++];
                else { lb = new int[2 + r.RangeInt(0, 2)]; for (int q = 0; q < lb.Length; q++) lb[q] = r.RangeInt(0, 4); }
                targets[(int)p] = new Target { stem = 2 + r.RangeInt(0, 3), loop = lb.Length, bulge = r.RangeInt(0, 3), loopBases = lb };
            }
        }
        public static int[] Loop(Pathway p) { Build(); return targets[(int)p].loopBases; }

        /// How a chain folds: stem pairs (i, j), an optional bulge, and the loop between.
        public struct Folded { public int pairs, bulge, bulgeIdx, loopStart, loopEnd; public int[] pi, pj; }
        public static Folded Fold(int[] s)
        {
            s = Main(s);
            var f = new Folded { pi = new int[6], pj = new int[6], bulgeIdx = -1 };
            int n = s.Length, i = 0, j = n - 1, pairsAtBulge = -1;
            while (j - i >= 3 && f.pairs < 6)
            {
                if (Comp(s[i]) == s[j]) { f.pi[f.pairs] = i; f.pj[f.pairs] = j; f.pairs++; i++; j--; continue; }
                if (f.pairs > 0 && f.bulge == 0 && j - i >= 4 && Comp(s[i + 1]) == s[j]) { f.bulge = 1; f.bulgeIdx = i; pairsAtBulge = f.pairs; i++; continue; }
                if (f.pairs > 0 && f.bulge == 0 && j - i >= 4 && Comp(s[i]) == s[j - 1]) { f.bulge = 2; f.bulgeIdx = j; pairsAtBulge = f.pairs; j--; continue; }
                break;
            }
            if (f.bulge != 0 && f.pairs == pairsAtBulge)
            {
                // no rung after the bulge: it was just the start of the loop
                if (f.bulge == 1) i = f.bulgeIdx; else j = f.bulgeIdx;
                f.bulge = 0; f.bulgeIdx = -1;
            }
            if (f.pairs == 0) { f.loopStart = 0; f.loopEnd = n - 1; } else { f.loopStart = i; f.loopEnd = j; }
            return f;
        }

        static int[] Build(int stem, int bulge, int[] loop, CLAY.Galaxy.DetRng r)
        {
            var L = new int[stem]; for (int k = 0; k < stem; k++) L[k] = r.RangeInt(0, 4);
            int bb = r.RangeInt(0, 4);
            var left = new System.Collections.Generic.List<int> { L[0] };
            if (bulge == 1) left.Add(bb == Comp(L[1]) ? (bb + 2) % 4 : bb);
            for (int k = 1; k < stem; k++) left.Add(L[k]);
            var rightRev = new System.Collections.Generic.List<int> { Comp(L[0]) };
            if (bulge == 2) rightRev.Add(bb == L[1] ? (bb + 2) % 4 : bb);
            for (int k = 1; k < stem; k++) rightRev.Add(Comp(L[k]));
            rightRev.Reverse();
            var all = new System.Collections.Generic.List<int>(left); all.AddRange(loop); all.AddRange(rightRev);
            return all.ToArray();
        }
        /// A working example of a job's gene.
        public static int[] Recipe(Pathway p)
        {
            Build(); var t = targets[(int)p];
            return Build(t.stem, t.bulge, t.loopBases, new CLAY.Galaxy.DetRng(CLAY.Galaxy.DetRng.Hash(PlanetSeed, 0x57E3UL + (ulong)p)));
        }
        /// A poor first version: the stem a rung off, the bulge wrong, the loop's bases wrong.
        public static int[] Weak(Pathway p)
        {
            Build(); var t = targets[(int)p];
            var loop = new int[t.loop]; for (int i = 0; i < t.loop; i++) loop[i] = (t.loopBases[i] + 1 + i) % 4;
            return Build(t.stem == 2 ? 3 : t.stem - 1, t.bulge == 0 ? 1 : 0, loop, new CLAY.Galaxy.DetRng(CLAY.Galaxy.DetRng.Hash(PlanetSeed, 0x3EA4UL + (ulong)p)));
        }
        /// A short floppy strand.
        public static int[] Blank() => new[] { 0, 2, 2, 0 };

        /// How well a gene does a job: 0 … 1.
        public static float Efficiency(int[] s, Pathway p)
        {
            Build(); var t = targets[(int)p];
            if (s == null) return 0f;
            var full = s; s = Main(s);
            if (s.Length < MinLen) return 0f;
            var f = Fold(s);
            // the pocket: the loop's bases, plus any branches reaching in from them
            var loopSeq = new System.Collections.Generic.List<int>();
            for (int k = f.loopStart; k <= f.loopEnd; k++)
            {
                loopSeq.Add(s[k]);
                if (f.pairs > 0)
                    foreach (var v in full)
                    {
                        if (v >= BranchTag && v < BridgeTag && (v - BranchTag) / 4 == k) loopSeq.Add((v - BranchTag) % 4);
                        if (v >= BridgeTag) { int x = (v - BridgeTag) / 4; if (x / 16 == k) loopSeq.Add((v - BridgeTag) % 4); }   // (counted once, at its first parent)
                    }
            }
            int loopN = loopSeq.Count;
            float braces = 0f;
            if (f.pairs > 0)
                foreach (var v in full)
                    if (v >= BridgeTag)
                    {
                        int x = (v - BridgeTag) / 4, a = x / 16, b = x % 16;
                        bool aStem = false, bStem = false;
                        for (int q = 0; q < f.pairs; q++) { if (f.pi[q] == a || f.pj[q] == a) aStem = true; if (f.pi[q] == b || f.pj[q] == b) bStem = true; }
                        if (aStem && bStem) braces += 0.5f;                              // a bridge across the stem braces it
                    }
            float effPairs = f.pairs + Mathf.Min(braces, 1f);
            float stem = f.pairs == 0 ? 0f : Mathf.Clamp01(1f - Mathf.Abs(effPairs - t.stem) * 0.35f);
            float loopSize = Mathf.Clamp01(1f - Mathf.Abs(loopN - t.loop) * 0.35f);
            float bul = f.bulge == t.bulge ? 1f : 0f;
            int hit = 0; for (int k = 0; k < Mathf.Min(loopN, t.loop); k++) if (loopSeq[k] == t.loopBases[k]) hit++;
            float lb = hit / (float)t.loop;
            float e = stem * 0.3f + loopSize * 0.15f + bul * 0.1f + lb * 0.45f;
            if (f.pairs == 0) e *= 0.35f;                                        // a floppy strand barely works
            return e * e * (3f - 2f * e);
        }

        /// What the gene does (the job its folded pocket fits best) — and its efficiency at it.
        public static int Best(int[] seq, bool ribosome, out float eff)
        {
            int best = 0; eff = -1f;
            for (int k = 0; k < Count; k++)
            {
                if (!Available((Pathway)k, ribosome)) continue;
                float e = Efficiency(seq, (Pathway)k); if (e > eff) { eff = e; best = k; }
            }
            return best;
        }
        public static int Match(int[] seq, out int err) => Match(seq, out err, ProteinEraDefault);
        /// Legacy: the job, and a coarse "error" bucket (0 superb · 1 good · 2 weak · 3+ misfolded).
        public static int Match(int[] seq, out int err, bool ribosome)
        {
            int p = Best(seq, ribosome, out float e);
            err = e >= 0.9f ? 0 : e >= 0.6f ? 1 : e >= 0.3f ? 2 : 3;
            return p;
        }

        /// Duplication-divergence neighbours: a gene's copy can drift into these (if available).
        public static Pathway[] Neighbours(Pathway p) => p switch
        {
            Pathway.Fermentation => new[] { Pathway.Hydrogenotrophy, Pathway.Ribosome, Pathway.Lysis },
            Pathway.Ribosome => new[] { Pathway.Adaptor },
            Pathway.Adaptor => new[] { Pathway.Ribosome },
            Pathway.Hydrogenotrophy => new[] { Pathway.ProtonPump, Pathway.SulfurOxidation, Pathway.Fermentation },
            Pathway.SulfurOxidation => new[] { Pathway.IronOxidation, Pathway.AnoxygenicPhoto },
            Pathway.IronOxidation => new[] { Pathway.SulfurOxidation },
            Pathway.ProtonPump => new[] { Pathway.Rhodopsin },
            Pathway.Lysis => new[] { Pathway.Fermentation },
            Pathway.Rhodopsin => new[] { Pathway.AnoxygenicPhoto },
            Pathway.AnoxygenicPhoto => new[] { Pathway.Rhodopsin },
            _ => new Pathway[0],
        };

        public static Metabolism ToMetabolism(Pathway p) => p switch
        {
            Pathway.Rhodopsin or Pathway.AnoxygenicPhoto => Metabolism.AnoxygenicPhoto,
            Pathway.Hydrogenotrophy or Pathway.SulfurOxidation or Pathway.IronOxidation or Pathway.ProtonPump => Metabolism.Chemotrophy,
            _ => Metabolism.Fermentation,
        };
    }
}
