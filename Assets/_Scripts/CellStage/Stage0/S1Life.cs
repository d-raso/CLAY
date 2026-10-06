using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Genetics;
using CLAY.CellStage.Stage1;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// S1 LIFE inside the shared pool. A protocell that has carried a replicator through 2 divisions graduates on its
    /// own (cells in one pool can be in S0 and S1 at the same time).
    ///
    /// ENERGY (CellStage_Metabolism.md): the cell carries metabolic PATHWAY genes (Genetics/Pathways.cs). Each pathway
    /// harvests from its own source — organics, light, H₂ at vents, sulfide, dissolved iron — in proportion to the
    /// share of the protein budget its priority gets, and every gene costs upkeep whether it earns or not. Ribozymes
    /// then turn raw energy into usable energy; usable energy pays membrane upkeep first, then ribozymes / growth.
    ///
    /// EVOLUTION: the lineage records how much of each source it has lived in (exposure). At division a gene's copy can
    /// drift into a neighbouring pathway — far more often when the lineage lives where that pathway would pay.
    /// </summary>
    public sealed class S1Life
    {
        public readonly EnergyBudget energy = new();
        public float ribozymeQuality;
        public float membraneStress;
        public float starvation;
        public float fes;
        public float amino, peptides;       // amino acids held; peptides made (they coat the membrane and steady RNA)
        public float pepRate, adapt, coded;  // how well the peptide-maker works · adaptor strength · how un-random the peptides are                   // iron–sulfur grains held (the CO₂-fixing ribozyme's catalytic cores)
        public float gradYield, bleed;      // energy / s from the vent's proton gradient; loss through a leaky membrane away from it
        public float stability = 1f;        // 0 … 1: drains steadily (faster when moving), refilled by energy made; 0 = death
        public float copyStore;
        public bool editing;                // the player is working on its genes (set by the mode)             // usable energy set aside to copy the genome (fills once the cell is grown)
        public float converting;            // usable energy made per second (how hard the ribozymes are working)
        public float activity;              // 0 resting … 1 swimming hard: resting builds enzymes, moving grows
        Vector2 lastPos; bool hasLast;
        public float heading;               // swimming direction (rods point where they go)
        public EnergySourceType lastSource;
        public Pathway dominant;
        public float morph;                 // 0 → 1 over the first seconds alive: the bubble visibly becomes a cell
        public readonly float[] here = new float[Pathways.Count];      // source strength at the cell, per pathway (0..1)
        public readonly float[] yield = new float[Pathways.Count];     // raw energy / s each owned pathway earns now
        public float upkeepRate;                                       // gene upkeep / s
        readonly float[] exposure = new float[Pathways.Count];
        readonly float[] idle = new float[Pathways.Count];
        public int lostGene = -1;            // set when a gene frays away (the mode spills it and shows it)
        float lived;
        readonly float[] scratch = new float[4];

        public S1Life(Genome g)
        {
            ribozymeQuality = g.copyFidelity * 0.5f;
            if (g.bays.Count == 0)
            {
                // a new lineage: one gene slot, and a poor fermentation gene to start with
                g.geneSlots = 1;
                g.bays.Add(Pathways.Weak(Pathway.Fermentation)); g.priority[(int)Pathway.Fermentation] = 1f; g.Express();
            }
        }

        public S1Life Inherit(ref DetRng r)
        {
            var c = new S1Life(new Genome()) { ribozymeQuality = Mathf.Clamp01(ribozymeQuality + r.Range(-0.04f, 0.03f)), lived = lived * 0.5f, fes = fes * 0.5f, stability = stability };
            fes *= 0.5f;
            energy.Split(c.energy);
            copyStore = 0f;
            for (int k = 0; k < exposure.Length; k++) { c.exposure[k] = exposure[k] * 0.5f; exposure[k] *= 0.5f; }
            lived *= 0.5f;
            return c;
        }

        /// Source strength (0..1) each pathway would draw on at this spot.
        public static void Sources(Vector2 pos, TidePool pool, TidePoolBiomeGenome biome, float[] scratch, float[] into)
        {
            RibozymeMetabolism.SampleAll(pos, pool, biome, scratch);
            float light = scratch[(int)EnergySourceType.PrimitivePhototrophy];
            float vent = scratch[(int)EnergySourceType.ProtonGradient];
            float seep = scratch[(int)EnergySourceType.MineralRedox];
            float org = scratch[(int)EnergySourceType.OrganicFermentation];
            into[(int)Pathway.Fermentation] = org * 0.5f;
            if (!Pathways.LightEra) light = 0f;                                  // no light-harvesting before S2
            into[(int)Pathway.Rhodopsin] = light;
            into[(int)Pathway.AnoxygenicPhoto] = light * Mathf.Clamp01(0.15f + Mathf.Max(vent, seep) * 1.2f);   // light AND an electron donor
            into[(int)Pathway.ProtonPump] = vent; into[(int)Pathway.Ribosome] = 0f;
            into[(int)Pathway.Hydrogenotrophy] = vent;
            into[(int)Pathway.SulfurOxidation] = vent * 0.85f;
            into[(int)Pathway.IronOxidation] = seep;
            into[(int)Pathway.Lysis] = 0f;
        }

        /// Share of the protein budget a pathway gets (its priority over the sum of priorities).
        public static float Share(Genome g, Pathway p)
        {
            if (!g.Has(p)) return 0f;
            float sum = 0f; foreach (var q in g.genes) sum += g.priority[(int)q];
            return sum > 1e-4f ? g.priority[(int)p] / sum : 1f / g.genes.Count;
        }

        /// One tick of living metabolism. Returns membrane area grown (negative = shrinking) this tick.
        public float Tick(Protocell c, float dt, TidePool pool, TidePoolBiomeGenome biome, float reactionRate, ref DetRng r)
        {
            var g = c.genome;
            Sources(c.pos, pool, biome, scratch, here);
            for (int k = 0; k < Pathways.Count; k++) { exposure[k] += here[k] * dt; yield[k] = 0f; }
            lived += dt;

            // a gene with nothing to work on slowly unravels (unused genes are lost — they cost upkeep);
            // an RNA genome also breaks under UV
            if (g.genes.Count > 1)
            {
                for (int i = g.genes.Count - 1; i >= 0; i--)
                {
                    var p = g.genes[i];
                    if (p == Pathway.Lysis || p == Pathway.Fermentation || p == Pathway.Ribosome || p == Pathway.Adaptor || p == Pathway.ProtonPump) continue;
                    idle[(int)p] = here[(int)p] < 0.03f ? idle[(int)p] + dt : Mathf.Max(0f, idle[(int)p] - dt * 2f);
                    if (idle[(int)p] > 150f) { g.RemoveGene(p); idle[(int)p] = 0f; lostGene = (int)p; break; }
                }
                if (!g.Dna && r.Value < pool.UV(c.pos) * dt * 0.004f * (1f - Mathf.Clamp01(peptides / 25f)))   // peptides steady the RNA
                {
                    var p = g.genes[r.RangeInt(1, g.genes.Count)];
                    g.RemoveGene(p); lostGene = (int)p;
                }
            }
            float raw = 0f, best = -1f;
            float uptakeBonus = 1f + ribozymeQuality * 0.5f;
            upkeepRate = 0f;
            foreach (var p in g.genes)
            {
                var info = Pathways.Get(p);
                float y = info.baseRate * here[(int)p] * Share(g, p) * uptakeBonus * Mathf.Max(g.eff[(int)p], 0.01f) * (1f + 0.5f * Mathf.Max(0, g.copies[(int)p] - 1));
                if (p == Pathway.Hydrogenotrophy) y *= 0.25f + 0.75f * Mathf.Clamp01(fes / 6f);   // only as good as its FeS cores
                if (g.Has(Pathway.ProtonPump)) y *= 1f + 0.3f * g.eff[(int)Pathway.ProtonPump];     // chemiosmotic coupling
                yield[(int)p] = y;
                raw += y * dt;
                stability += y * g.eff[(int)p] * 0.12f * dt;                     // a good gene doesn't just earn — it steadies you
                upkeepRate += info.upkeep;
                if (y > best) { best = y; dominant = p; }
            }
            // food taken in is fermented — faster the more of the budget goes to fermentation
            int org = (int)MoteKind.Organic;
            float fShare = Share(g, Pathway.Fermentation);
            if (fShare > 0f && c.free[org] > 0 && r.Value < dt * (0.6f + fShare * 2.4f))
            {
                c.free[org]--;
                float bite = 0.3f * (0.4f + fShare);
                raw += bite; yield[(int)Pathway.Fermentation] += bite / Mathf.Max(dt, 1e-3f) * 0.05f;
                stability += 0.05f * (0.2f + 1.6f * g.eff[(int)Pathway.Fermentation] * g.eff[(int)Pathway.Fermentation]);   // a meal, digested well, is a real relief
            }
            // the vent's natural proton gradient, harvested straight through a leaky membrane; away from it the same
            // leaks bleed energy. A proton pump (protein era) makes the cell its own gradient and plugs the bleed.
            bool pump = g.Has(Pathway.ProtonPump);
            float ventHere = here[(int)Pathway.Hydrogenotrophy];
            float pe = pump ? g.eff[(int)Pathway.ProtonPump] * (1f + 0.5f * Mathf.Max(0, g.copies[(int)Pathway.ProtonPump] - 1)) : 0f;
            gradYield = ventHere * g.leak * (0.12f + 1.1f * pe);                 // without the coupler the gradient barely helps
            bleed = 0.02f * g.leak * (1f - ventHere) * (1f - 0.7f * Mathf.Clamp01(pe));
            yield[(int)Pathway.ProtonPump] = gradYield;
            stability += gradYield * Mathf.Clamp01(pe) * 0.12f * dt;
            raw += gradYield * dt;
            upkeepRate += bleed;
            if (gradYield > best) { best = gradYield; dominant = Pathway.Hydrogenotrophy; }
            fes = Mathf.Max(0f, fes - dt * 0.01f);                                // grains wear out slowly

            // ── toward the ribosome ──
            // amino acids: taken from the water, and made by your own CO₂ fixation
            amino = Mathf.Min(amino + yield[(int)Pathway.Hydrogenotrophy] * dt * 1.2f, 20f);
            pepRate = PeptideRate(g);
            adapt = AdaptorScore(g);
            if (pepRate > 0f && amino > 0f)
            {
                float made = Mathf.Min(amino, pepRate * dt * 1.5f);
                amino -= made; peptides = Mathf.Min(peptides + made * 0.5f, 20f);
            }
            peptides = Mathf.Max(0f, peptides - peptides * dt * 0.015f);           // turnover
            membraneStress = Mathf.Max(0f, membraneStress - peptides * 0.0012f * dt);   // peptides coat the membrane
            coded = Mathf.Clamp01(adapt) * Mathf.Clamp01(pepRate * 2f);

            upkeepRate += 0.012f * g.misfolds;                                    // misfolded proteins are made and wasted
            membraneStress = Mathf.Clamp01(membraneStress + g.misfolds * 0.004f * dt);
            float carry = energy.raw + raw - upkeepRate * dt;
            float unpaidGenes = carry < 0f ? -carry : 0f;
            energy.raw = Mathf.Clamp(carry, 0f, 4f + c.area);
            lastSource = dominant switch
            {
                Pathway.Rhodopsin or Pathway.AnoxygenicPhoto => EnergySourceType.PrimitivePhototrophy,
                Pathway.Hydrogenotrophy or Pathway.SulfurOxidation => EnergySourceType.ProtonGradient,
                Pathway.IronOxidation => EnergySourceType.MineralRedox,
                _ => EnergySourceType.OrganicFermentation,
            };
            g.metabolism = Pathways.ToMetabolism(dominant);

            // copying the genome competes with metabolism: conversion slows while the cell divides
            float usable = RibozymeMetabolism.Convert(energy.raw, ribozymeQuality, reactionRate * (c.pinch > 0.05f ? 0.5f : 1f), dt);
            converting = Mathf.Lerp(converting, usable / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 2f));
            {
                float drainS = 0.006f * (1f + 0.8f * activity) * (editing ? 0.4f : 1f) + upkeepRate * 0.3f + g.misfolds * 0.004f;
                float gainS = usable / Mathf.Max(dt, 1e-4f) * 0.05f;
                stability = Mathf.Clamp01(stability + (gainS - drainS) * dt);
            }
            energy.raw -= usable / RibozymeMetabolism.Efficiency(ribozymeQuality);
            energy.usable += usable;
            energy.Allocate(dt, g, c.area);
            // where the surplus goes is behaviour, not a menu: resting builds better enzymes, swimming and eating grows
            if (hasLast) activity = Mathf.MoveTowards(activity, Mathf.Clamp01((c.pos - lastPos).magnitude / Mathf.Max(dt, 1e-4f) / 1.5f), dt * 0.5f);
            if (hasLast && (c.pos - lastPos).sqrMagnitude > 1e-6f)
            {
                var dv = c.pos - lastPos;
                heading = Mathf.LerpAngle(heading * Mathf.Rad2Deg, Mathf.Atan2(dv.y, dv.x) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 2f)) * Mathf.Deg2Rad;
            }
            lastPos = c.pos; hasLast = true;
            float bias = Mathf.Lerp(g.investBias, Mathf.Lerp(0.8f, 0.15f, activity), 0.7f);
            float surplus = energy.ribozymeSpend + energy.copySpend;
            energy.ribozymeSpend = surplus * bias; energy.copySpend = surplus - energy.ribozymeSpend;

            // upkeep shortfall → stress (shimmer) and shrinking (wrinkles)
            float need = EnergyBudget.UpkeepPerArea * c.area * dt + upkeepRate * dt;
            float shortfall = need > 0f ? Mathf.Clamp01((energy.deficit + unpaidGenes) / need) : 0f;
            float calm = editing ? 0.25f : Mathf.Lerp(0.45f, 1f, activity);           // still cells (and cells tending their genes) strain far less
            shortfall *= calm;
            float tough = 0.6f + g.membraneToughness * 0.8f;
            membraneStress = Mathf.Clamp01(membraneStress + (shortfall * 0.015f / tough - (1f - shortfall) * (editing ? 0.08f : 0.04f)) * dt);
            starvation = Mathf.MoveTowards(starvation, shortfall, dt * 0.5f);
            float shrink = energy.deficit * 0.5f;

            float ceiling = 0.5f + g.copyFidelity * 0.5f;
            ribozymeQuality = Mathf.Min(ribozymeQuality + energy.ribozymeSpend * 0.05f, ceiling);
            // until grown, surplus builds membrane; once grown, it is saved up to copy the genome
            float grow = 0f;
            if (c.area >= c.birthArea * 1.8f) copyStore = Mathf.Min(copyStore + energy.copySpend, 4f);
            else grow = energy.copySpend * EnergyBudget.GrowthPerEnergy;
            energy.ribozymeSpend = energy.copySpend = energy.upkeepSpend = 0f;

            return grow - shrink;
        }

        /// At division: a copy of one of the genome's genes may drift into a neighbouring pathway (duplication +
        /// divergence) — rarely by chance, often when the lineage has been living where that pathway pays. The genome
        /// can also lengthen (one more gene slot). Returns the pathway gained, or -1.
        public int Evolve(Genome g, ref DetRng r)
        {
            if (g.Dna && r.Value < 0.25f && g.geneSlots < g.SlotCap) g.geneSlots++;   // (RNA genomes gain slots only by complexity)
            g.geneSlots = Mathf.Min(g.geneSlots, g.SlotCap);
            if (!g.HasFreeBay) return -1;
            float t = Mathf.Max(lived, 20f);
            for (int i = 0; i < g.genes.Count; i++)
            {
                foreach (var nb in Pathways.Neighbours(g.genes[i]))
                {
                    if (g.Has(nb)) continue;
                    float avg = Mathf.Clamp01(exposure[(int)nb] / t);
                    if (r.Value < 0.02f + 0.3f * avg * avg + 0.15f * avg) { g.AddGene(nb, 0.4f); return (int)nb; }
                }
            }
            return -1;
        }

        /// The peptide-maker's activity, graded by how close any (otherwise unused) gene is to its fold — any random
        /// sequence links a few amino acids now and then; the closer, the more. A thioester ribozyme does it very weakly.
        public static float PeptideRate(Genome g)
        {
            float best = g.Has(Pathway.Fermentation) ? 0.03f : 0f;
            var target = Pathways.Recipe(Pathway.Ribosome);
            foreach (var b in g.bays)
            {
                if (b == null) continue;
                best = Mathf.Max(best, Pathways.Efficiency(b, Pathway.Ribosome));
            }
            return best;
        }
        /// Adaptors add up (a code needs several): each close one makes the peptides less random.
        public static float AdaptorScore(Genome g)
        {
            float sum = 0f; var target = Pathways.Recipe(Pathway.Adaptor);
            foreach (var b in g.bays)
            {
                if (b == null) continue;
                sum += Pathways.Efficiency(b, Pathway.Adaptor);
            }
            return sum;
        }

        public Metabolism DriftedMetabolism(Metabolism current, ref DetRng r) => Pathways.ToMetabolism(dominant);

        public bool Collapsed => membraneStress >= 1f;
    }
}
