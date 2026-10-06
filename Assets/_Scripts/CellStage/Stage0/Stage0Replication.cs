using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// STAGE 0: REPLICATION (S0 Protocell → S1 Replicator). Guide a lipid bubble through a tide pool's soup, take in
    /// the right molecules without bursting, ride out the wet–dry cycle, and end up carrying a chain that copies
    /// itself — then pass it on through divisions (heredity). Nothing is explained: the body shows everything.
    ///
    /// Controls: WASD / arrows, or hold the left mouse button to swim toward the cursor. On death: click one of
    /// your living relatives (or press 1–8) to continue as it.
    ///  • Scroll all the way in while in shallow water → the seabed: drag bases onto the mineral and build chains.
    ///  • Hold X to shed your membrane and become a viroid (also offered — V — after repeated deaths). As a viroid:
    ///    drift, get taken up by a feeding protocell, copy yourself from its bases; Space bursts it (lyse),
    ///    F goes dormant / wakes. Dormant inside a host at the founder collapse = you join its lineage.
    ///  • The pool graduates TOGETHER (CellStage_Decisions §11): when enough protocells carry heritable
    ///    replicators, the 3 most distinct lineages glow as founders — click one (or 1–3) to join it in S1.
    /// </summary>
    public sealed class Stage0Replication : IStageMode, IStageGUI
    {
        CellStageWorld world;
        CellStageProgress prog;
        TidePool pool;
        MoteField field;
        List<int>[] motifs;
        readonly List<Protocell> cells = new();
        Protocell player;
        int playerSpecies;
        DetRng r;
        float firstArea;
        float spawnTimer;

        // S0 additions (CellStage_Decisions §10, §11)
        FounderCollapse founderCollapse;    // watches replicating-cell count; fires the 3-founder S0→S1 collapse
        SeabedAssembly seabedAssembly;      // zoom-in manual monomer placement on the seabed (player only)

        // respawn picker
        bool picking; float pickTimer; List<ILivingBody> options = new();
        int consecutiveDeaths;          // deaths without a division in between (offers the viroid path)
        List<int> lastStrand;           // the strand a dead player could carry on as a viroid
        Vector2 lastDeathPos;

        // viroids (the player's, plus AI copies scattered by lysis)
        readonly List<S0Viroid> viroids = new();
        S0Viroid playerViroid;
        float shedHold;                 // seconds X has been held (shedding the membrane)

        // founder collapse (the pool graduates together)
        bool collapsing; float collapseTimer;
        List<Genome> founders; List<Protocell> founderCarriers;

        // rendering
        Transform root;
        GameObject soupGo; Material soupMat, blobMat, moteMat, causMat;
        QuadBatch blobs, motesFree, motesIn, shadows, seabed, foreground;
        Vents vents;
        Suspended suspended;
        Stage1.TidePoolBiomeGenome biome;
        CellMusic music;
        Features features; Material featMat; QuadBatch featFloor, featOver, featShadow;
        bool momentReached, choicePending; int lifeCount, momentThreshold;
        float camSize = 7f, zoom = 1f;
        Vector2 camPos;

        // spatial hash of free motes
        readonly Dictionary<long, List<int>> grid = new();
        const float Cell = 3f;

        public void Begin(CellStageWorld w, Transform parent)
        {
            world = w; prog = w.progress;
            r = new DetRng(DetRng.Hash(w.ctx.poolSeed, 0x57A6E0UL));
            pool = new TidePool(w.ctx);
            field = new MoteField(pool, w.ctx);
            motifs = Protocell.Motifs(w.ctx.planetSeed);
            Pathways.Motifs = Protocell.Motifs(w.ctx.planetSeed);   // a separate copy: the M test toggle must not change gene recipes
            vents = new Vents(pool, w.ctx);
            features = new Features(pool, vents, w.ctx);
            Pathways.PlanetSeed = w.ctx.planetSeed;
            SetupMusic(parent);
            var br = new DetRng(DetRng.Hash(w.ctx.poolSeed, 0xA1B2C3D4UL));
            biome = Stage1.TidePoolBiomeGenome.Generate(w.ctx, pool, ref br);
            suspended = new Suspended(w.ctx, pool.biome);
            S0Viroid.DecayMul = pool.biome.rnaDecayMul;
            Debug.Log($"[CellStage] biome: {pool.biome.displayName}");
            if (TidePoolBiome.Forced >= 0) w.Banner(pool.biome.displayName.ToUpperInvariant(), pool.biome.tagline);   // DEV (F10): which biome
            else w.Banner("WELCOME TO THE TIDE POOL", "collect and build molecules to discover what they can become");

            root = new GameObject("Stage0").transform; root.SetParent(parent, false);
            BuildVisuals();

            // the player's first bubble, plus a scatter of other protocells that formed in the soup on their own
            var g = new Genome { hue = r.Range(0f, 1f), saturation = 0.6f };
            player = new Protocell(g, pool.RandomWetPoint(ref r, 0.06f), 1.6f) { player = true, wanderSeed = r.Range(0f, 100f) };
            firstArea = player.area;
            playerSpecies = w.species.Found(g, "player-lineage").id;
            w.species.Born(player, playerSpecies);
            cells.Add(player);
            for (int i = 0; i < 45; i++) SpawnWildProtocell();
            camPos = player.pos;

            // init S0 additions
            founderCollapse = new FounderCollapse();
            seabedAssembly = new SeabedAssembly(player, pool, motifs, w);
        }

        void SpawnWildProtocell()
        {
            var g = new Genome { hue = r.Range(0f, 1f), saturation = r.Range(0.2f, 0.6f), membraneToughness = r.Range(0.3f, 0.7f),
                                 uptake = r.Range(0.3f, 0.7f), lipidAffinity = r.Range(0.3f, 0.7f), wobble = r.Range(0.1f, 0.6f) };
            var c = new Protocell(g, pool.RandomWetPoint(ref r, 0.03f), r.Range(1.0f, 2.0f)) { wanderSeed = r.Range(0f, 100f) };
            world.species.Born(c, world.species.Found(g).id);
            cells.Add(c);
        }

        void BuildVisuals()
        {
            soupMat = new Material(Shader.Find("CLAY/CellStage/Soup"));
            soupMat.SetTexture("_Height", pool.heightTex);
            soupMat.SetVector("_ArenaSize", new Vector4(TidePool.W, TidePool.H, 0, 0));
            ApplySolventLook(soupMat, world.ctx.solvent);
            ApplyPlanetLook(soupMat, world.ctx);
            pool.biome.ApplyLook(soupMat);
            soupGo = new GameObject("Soup") { layer = CellStageWorld.CellLayer };
            soupGo.transform.SetParent(root, false);
            var m = new Mesh { name = "SoupQuad" };
            m.vertices = new[] { new Vector3(0, 0, 1), new Vector3(TidePool.W, 0, 1), new Vector3(TidePool.W, TidePool.H, 1), new Vector3(0, TidePool.H, 1) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            soupGo.AddComponent<MeshFilter>().sharedMesh = m;
            var smr = soupGo.AddComponent<MeshRenderer>();
            smr.sharedMaterial = soupMat; smr.sortingOrder = -60;  // the water itself: above the environment backdrop (−1000…−150), below its floating particles (−55)
            // crisp caustic filaments (additive) on a second quad over the same area
            causMat = new Material(Shader.Find("CLAY/CellStage/Caustics"));
            causMat.SetTexture("_Height", pool.heightTex);
            causMat.SetVector("_ArenaSize", new Vector4(TidePool.W, TidePool.H, 0, 0));

            moteMat = new Material(Shader.Find("CLAY/CellStage/Mote"));
            blobMat = new Material(Shader.Find("CLAY/CellStage/Membrane"));
            featMat = new Material(Shader.Find("CLAY/CellStage/Feature"));
            featMat.SetTexture("_Height", pool.heightTex);
            featMat.SetVector("_ArenaSize", new Vector4(TidePool.W, TidePool.H, 0, 0));
            featMat.SetColor("_WaterShallow", soupMat.GetColor("_WaterShallow")); featMat.SetColor("_WaterDeep", soupMat.GetColor("_WaterDeep"));
            featShadow = new QuadBatch("FeatureShadows", moteMat, root, CellStageWorld.CellLayer, 59);
            featFloor = new QuadBatch("FeaturesFloor", featMat, root, CellStageWorld.CellLayer, 60);
            featOver = new QuadBatch("FeaturesOverhead", featMat, root, CellStageWorld.CellLayer, 69);
            shadows = new QuadBatch("Shadows", moteMat, root, CellStageWorld.CellLayer, 61);
            motesFree = new QuadBatch("MotesFree", moteMat, root, CellStageWorld.CellLayer, 62);
            blobs = new QuadBatch("Protocells", blobMat, root, CellStageWorld.CellLayer, 63);
            motesIn = new QuadBatch("MotesInside", moteMat, root, CellStageWorld.CellLayer, 64);
            editorQ = new QuadBatch("GeneEditor", moteMat, root, CellStageWorld.CellLayer, 67);
            vmOut = new VirusMeshes(root, CellStageWorld.CellLayer, 65);
            vmIn = new VirusMeshes(root, CellStageWorld.CellLayer, 68);
            bursts = new BurstShells(root, CellStageWorld.CellLayer, 63);
            seabed = new QuadBatch("Seabed", moteMat, root, CellStageWorld.CellLayer, 66);
            foreground = new QuadBatch("ForegroundDust", moteMat, root, CellStageWorld.CellLayer, 70);
        }

        /// The pool's ground and waterline in the planet's own colours (the same chemistry palette the orbital map and
        /// surface terrain use): low rock = the planet's low ground, high rock = its highlands, mineral accent, salt rime,
        /// and a waterline glow tinted by the liquid.
        internal static void ApplyPlanetLook(Material m, CellStageContext ctx)
        {
            Color shallow = m.GetColor("_WaterShallow");
            m.SetColor("_Glow", Color.Lerp(shallow, Color.white, 0.45f) * 1.1f);
            if (ctx.planet == null) return;                        // sandbox: keep the neutral defaults
            var cp = PlanetTexture.Chem(ctx.planet, ctx.planetSeed);
            m.SetColor("_Rock", cp.landLow * 0.85f);
            m.SetColor("_RockHigh", Color.Lerp(cp.landMid, cp.landHigh, 0.5f));
            m.SetColor("_Accent", cp.accentAmt > 0.05f ? cp.accent : Color.Lerp(cp.landMid, cp.landLow, 0.5f));
            m.SetColor("_Crust", Color.Lerp(cp.ice, Color.white, 0.4f));
            if (cp.hasLiquid)
            {
                var sh = Color.Lerp(shallow, cp.oceanShallow, 0.5f);
                m.SetColor("_WaterShallow", sh);
                m.SetColor("_WaterDeep", Color.Lerp(m.GetColor("_WaterDeep"), cp.oceanDeep, 0.5f));
                m.SetColor("_Glow", Color.Lerp(sh, Color.white, 0.45f) * 1.1f);
            }
        }

        internal static void ApplySolventLook(Material m, LiquidType s)
        {
            switch (s)
            {
                case LiquidType.Methane:
                    m.SetColor("_WaterShallow", new Color(0.55f, 0.42f, 0.22f)); m.SetColor("_WaterDeep", new Color(0.12f, 0.08f, 0.04f));
                    m.SetColor("_Rock", new Color(0.55f, 0.5f, 0.45f)); m.SetColor("_Crust", new Color(0.75f, 0.62f, 0.4f)); break;
                case LiquidType.Ammonia:
                    m.SetColor("_WaterShallow", new Color(0.5f, 0.55f, 0.65f)); m.SetColor("_WaterDeep", new Color(0.1f, 0.12f, 0.2f));
                    m.SetColor("_Rock", new Color(0.6f, 0.6f, 0.62f)); m.SetColor("_Crust", new Color(0.9f, 0.9f, 0.95f)); break;
                case LiquidType.Brine:
                    m.SetColor("_WaterShallow", new Color(0.45f, 0.62f, 0.5f)); m.SetColor("_WaterDeep", new Color(0.08f, 0.18f, 0.16f));
                    m.SetColor("_Crust", new Color(0.95f, 0.93f, 0.88f)); break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────
        public void Tick(float dt)
        {
            // dev: F4 toggles the Stage 0 caustic layer (to compare it with the environment's own caustics)
            if (Input.GetKeyDown(KeyCode.F4))
            {
                float on = soupMat.GetFloat("_CausticsOn") > 0.5f ? 0f : 1f;
                soupMat.SetFloat("_CausticsOn", on); Debug.Log($"[CellStage] caustics {(on > 0.5f ? "ON" : "OFF")}");
            }
            // dev: M swaps the planet's replicator recipes for a test recipe (both = AAA, so its mirror BBB works too)
            if (Input.GetKeyDown(KeyCode.M)) ToggleTestMotifs();
            pool.Tick(dt);
            field.Tick(dt, Flow);
            vents.Tick(pool.time);
            vents.Emit(field, dt, ref r);
            suspended.Tick(dt, camPos, Flow);
            BuildGrid();
            float rate = world.ctx.ReactionRate * pool.biome.rateMul;

            world.captureEscape = seabedAssembly.Active || editing;
            if (player != null) player.sheltered = seabedAssembly.Active;
            seabedAssembly.Tick(dt);
            bool controlling = !picking && !collapsing && !seabedAssembly.Active;
            if (controlling && player != null && !player.dead) PlayerControl(dt);
            TickViroids(dt, controlling);
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.player && !picking) continue;
                if (playerViroid != null && playerViroid.host == c && playerViroid.HijackActive(pool.time)) Steer(c, ReadInput(c), dt);   // hijack spurt: you steer your host
                else AIControl(c, dt);
            }
            Collide();
            TickFragments(dt);
            TickSparks(dt);
            TickEnergyMotes(dt);
            features.Tick(dt, Flow);
            FeedMusic();

            for (int i = cells.Count - 1; i >= 0; i--)
            {
                var c = cells[i];
                if (c.dead) { c.burstFlash -= dt * 0.6f; if (c.burstFlash <= 0f) cells.RemoveAt(i); continue; }   // ~1.7 s to crumble
                c.contact = Mathf.Max(0f, c.contact - dt * 0.5f);
                Absorb(c, dt);
                // vents: the warm plume speeds every reaction; the hot core breaks chains (counts as extra UV-like damage)
                vents.At(c.pos, out float plume, out float core);
                // alkaline spring: the pH gradient around the towers holds membranes together; stray far and it doesn't
                c.phStress = pool.biome.phGradient ? Mathf.MoveTowards(c.phStress, Mathf.Clamp01(1f - plume * 2.5f), dt * 0.3f) : 0f;
                if (c.player && seabedAssembly.Active) { UpdateGenomeReplicator(c); continue; }   // hands-on: chemistry waits for you
                c.lyse = Mathf.Max(0f, c.lyse - dt * 0.3f);
                if (c.life != null)
                {
                    // ALIVE: the replicator is now a genome; molecules are not this cell's business any more.
                    if (c.life.morph < 1f)
                    {
                        float g = 1f + dt * 0.33f;                                      // ≈ 4× area over the ~4 s morph
                        c.area *= g; c.birthArea *= g;
                    }
                    c.life.morph = Mathf.Min(1f, c.life.morph + dt / 4.2f);
                    float dep = pool.Depth(c.pos);
                    if (dep <= 0.003f) c.moisture -= dt * 0.03f * (1.2f - c.genome.desiccationResist); else c.moisture = Mathf.Min(1f, c.moisture + dt * 0.25f);
                    c.area = Mathf.Max(0.05f, c.area + c.life.Tick(c, dt, pool, biome, rate, ref r));
                    if (c.player && devStable) { gathered = SlotCost; c.life.stability = 1f; c.life.membraneStress = 0f; c.life.starvation = 0f; c.life.energy.raw = Mathf.Max(c.life.energy.raw, 2f); c.moisture = 1f; for (int bk = 0; bk < 4; bk++) c.free[bk] = Mathf.Max(c.free[bk], 4); }
                    if (c.life.stability <= 0f) { Kill(c, "ran out of energy"); continue; }
                    if (c.life.lostGene >= 0)
                    {
                        fragments.Add(new GeneFragment { pos = c.pos + Random.insideUnitCircle * c.Radius, p = (Pathway)c.life.lostGene, life = 30f, seed = r.Range(0f, 100f) });
                        c.life.lostGene = -1;
                    }
                    if (c.life.Collapsed) { Kill(c, "membrane collapse"); continue; }
                    if (c.area < c.birthArea * 0.3f) { Kill(c, "starvation"); continue; }
                    if (c.moisture <= 0f) { Kill(c, "dried"); continue; }
                    if (c.player && c.genome.ring) PlayerFission(c, dt); else TryDivide(c, dt);
                    continue;
                }
                c.Chemistry(dt, pool.Depth(c.pos), pool.UV(c.pos) + core * 0.8f, rate * (1f + plume * 1.5f), motifs, ref r, c.player ? prog : DummyProgress);
                UpdateGenomeReplicator(c);
                if (c.life != null)
                {
                    c.area = Mathf.Max(0.05f, c.area + c.life.Tick(c, dt, pool, biome, rate, ref r));
                    if (c.life.Collapsed) { Kill(c, "membrane collapse"); continue; }
                    if (c.area < c.birthArea * 0.3f) { Kill(c, "starvation"); continue; }
                }
                if (c.Pressure > c.BurstThreshold) Kill(c, "burst");
                else if (c.moisture <= 0f) Kill(c, "dried");
                else TryDivide(c, dt);
            }
            if (player != null && !player.dead) PlayerMilestones();

            // the soup keeps making bubbles on its own while the pool is thinly populated
            spawnTimer -= dt;
            if (spawnTimer <= 0f) { spawnTimer = 4f; if (cells.Count < 60 && !momentReached) SpawnWildProtocell(); }

            if (picking) PickerTick(dt);

            // the pool graduates together: enough heritable replicators → 3 founders (CellStage_Decisions §11)
            // the pool's moment: once enough cells live (S1), everyone still in S0 chooses — life or virus
            CountLife();
            if (!momentReached)
            {
                // which of the planet's recipes has anyone in the pool assembled?
                foreach (var c in cells)
                {
                    if (c.dead || (c.life != null && !c.player)) continue;
                    foreach (var ch in c.chains)
                    {
                        if (!ch.replicator) continue;
                        int mi = Protocell.MotifIndex(ch, motifs);
                        if (mi >= 0 && c.player && !testMotifs) playerFound[mi] = true;   // remembered into S1
                        if (mi >= 0 && !motifFound[mi])
                        {
                            motifFound[mi] = true;
                            if (c.player) world.Banner($"{FoundCount()} / {motifs.Length}", "");
                        }
                    }
                }
                if (FoundCount() >= motifs.Length) BeginMoment();
            }
            // LUCA: the player's living lineage integrates → S2
            if (player != null && !player.dead && player.life != null && player.genome.Dna && player.genome.ribosome)
            {
                prog.founderGenomes = PickFounders();
                prog.playerSeedGenome = player.genome;
                prog.playerFounder = -1;
                prog.Reach(Milestone.Heredity);      // → S2 (CellStageWorld swaps the mode)
                return;
            }

            Render(dt);
        }

        static readonly CellStageProgress DummyProgress = new();

        // ── S1 in the shared pool + the life-or-virus moment ─────────────────────────────────
        List<int> revealedRecipe;
        float dive;
        readonly float[] srcScratch = new float[Pathways.Count], srcScratch4 = new float[4];
        // horizontal gene transfer: DNA spilled by dead living cells drifts until something takes it up
        sealed class GeneFragment { public Vector2 pos; public Pathway p; public float life; public float seed; }
        readonly List<GeneFragment> fragments = new();
        bool genomePanel; int pendingOffer = -1; Vector2 panelScroll;
        bool lastWasAlive;
        readonly bool[] motifFound = new bool[8], playerFound = new bool[8];
        int FoundCount() { int n = 0; for (int i = 0; i < motifs.Length; i++) if (motifFound[i]) n++; return n; }
        bool lineageSecured;        // the player's living lineage has divided: death never sends it back to S0
        Genome lastGenome;
        int dragGene = -1; bool dragOut;
        float virusTimer = 40f;          // the player's last body was a living (S1) cell                 // seconds left of the transformation close-up   // the recipe the player's own replicator matched — revealed once they're alive

        /// The planet recipe a chain matches exactly (as itself or its mirror strand), or null.
        List<int> MatchedRecipe(Protocell c)
        {
            foreach (var ch in c.chains)
            {
                if (!ch.replicator) continue;
                foreach (var m in motifs)
                {
                    if (ch.seq.Count != m.Count) continue;
                    bool same = true, mirror = true;
                    for (int k = 0; k < m.Count; k++) { if (ch.seq[k] != m[k]) same = false; if (ch.seq[k] != MoteChem.Complement(m[k])) mirror = false; }
                    if (same || mirror) return new List<int>(m);
                }
            }
            return c.genome.replicator.Count > 0 ? new List<int>(c.genome.replicator) : null;
        }

        void Graduate(Protocell c)
        {
            if (c.life != null || c.dead) return;
            if (c.player && revealedRecipe == null) revealedRecipe = MatchedRecipe(c);
            c.life = new S1Life(c.genome);
            if (!c.player) c.genome.geneSlots = 3;
            if (!c.player)
            {
                S1Life.Sources(c.pos, pool, biome, srcScratch4, srcScratch);
                int bestP = -1; float bestY = 0.12f;
                for (int k = 1; k < Pathways.Count; k++) { if (!Pathways.Available((Pathway)k, c.genome.ribosome)) continue; float y = Pathways.Get((Pathway)k).baseRate * srcScratch[k]; if (y > bestY) { bestY = y; bestP = k; } }
                if (bestP > 0 && r.Value < 0.75f) c.genome.AddGene((Pathway)bestP, 0.8f);
                else if (r.Value < 0.2f) c.genome.AddGene(Pathway.Rhodopsin, 0.5f);
                if (r.Value < 0.15f) c.genome.AddGene(Pathway.Lysis, 0.3f);
            }
            if (c.genome.metabolism == Metabolism.None) c.genome.metabolism = Metabolism.Fermentation;
            c.absorbFlash = 1f;
            if (c.player)
            {
                c.burstFlash = 0f; dive = 6.5f; music?.Event(CellMusic.Cue.Life);                        // the camera dives in to watch it become a cell
                prog.Reach(Milestone.StableReplicator);   // the player is now S1 (same pool, same mode)
                Debug.Log("[CellStage] your lineage is alive (S1)");
            }
        }

        void CountLife()
        {
            int alive = 0; lifeCount = 0;
            foreach (var c in cells) { if (c.dead) continue; alive++; if (c.life != null) lifeCount++; }
            momentThreshold = Mathf.Clamp(Mathf.RoundToInt(alive * 0.25f), 4, 20);
        }

        void BeginMoment()
        {
            momentReached = true;
            Debug.Log($"[CellStage] the pool's moment: {lifeCount} living cells — everyone else chooses life or virus");
            if (!(player != null && !player.dead && player.life != null)) world.Banner("THE TIDE HAS TURNED", "life has woken in the pool — live, or take what lives");
            // AI cells still in S0 make their own choice
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                var c = cells[i];
                if (c.dead || c.player || c.life != null) continue;
                if (r.Value < 0.9f) Graduate(c);
                else
                {
                    var strand = StrandOf(c);
                    var at = c.pos;
                    Kill(c, "shed");
                    viroids.Add(new S0Viroid(at, strand, false));
                }
            }
            // the player is asked — unless they already live
            choicePending = !(player != null && !player.dead && player.life != null);
        }

        void ChooseLife()
        {
            choicePending = false;
            if (playerViroid != null)
            {
                // a strand choosing life: it wraps itself in a new living bubble
                var at = playerViroid.pos; var strand = playerViroid.strand;
                playerViroid.alive = false; viroids.Remove(playerViroid); playerViroid = null;
                var g = new Genome { hue = r.Range(0f, 1f), saturation = 0.6f, metabolism = Metabolism.Fermentation };
                g.replicator.AddRange(strand);
                var c = new Protocell(g, at, 1.6f) { wanderSeed = r.Range(0f, 100f) };
                var ch = new Chain(); ch.seq.AddRange(strand); Protocell.Classify(ch, motifs); c.chains.Add(ch);
                playerSpecies = world.species.Found(g, "player-lineage").id;
                world.species.Born(c, playerSpecies);
                cells.Add(c); Possess(c);
                Graduate(c);
                return;
            }
            if (player == null || player.dead)
            {
                // between lives: continue as a living relative, or a new living bubble
                foreach (var c in cells) if (!c.dead && c.life != null && c.SpeciesId == playerSpecies) { Possess(c); return; }
                foreach (var c in cells) if (!c.dead && c.life != null) { Possess(c); return; }
                return;
            }
            Graduate(player);
        }

        void ChooseVirus()
        {
            choicePending = false;
            if (playerViroid != null) return;                         // already a strand
            if (player != null && !player.dead) { Shed(); return; }
            BecomeViroid(lastDeathPos, lastStrand);
        }

        /// The three most distinct living lineages (for S2 seeding).
        List<Genome> PickFounders()
        {
            var living = new List<Protocell>();
            foreach (var c in cells) if (!c.dead && c.life != null) living.Add(c);
            var res = new List<Genome>();
            if (living.Count == 0) { res.Add(player.genome.Clone()); return res; }
            res.Add(player.genome.Clone());
            while (res.Count < 3 && living.Count > 0)
            {
                Protocell best = null; float bestD = -1f;
                foreach (var c in living)
                {
                    float d = float.MaxValue;
                    foreach (var f in res) d = Mathf.Min(d, c.genome.Distance(f));
                    if (d > bestD) { bestD = d; best = c; }
                }
                res.Add(best.genome.Clone()); living.Remove(best);
            }
            return res;
        }

        /// The one on-screen choice in the stage: "Become life?"
        public void StageGUI()
        {
            if (playerViroid != null && playerViroid.body != null) DrawVirusBars();
            if (playerViroid != null && !choicePending)
            {
                // the way out of the virus path: wrap your strand in a membrane and live
                if (GUI.Button(new Rect(Screen.width - 170, Screen.height - 50, 160, 36), "Become life")) { ChooseLife(); return; }
            }
            if (player != null && !player.dead && player.life != null && player.life.morph >= 1f) DrawBars();
            if (editing && player != null && !player.dead && player.life != null) EditorGUI(); else guiRects.Clear();
            if (genomePanel && player != null && !player.dead && player.life != null) GenomePanel();
            if (player != null && !player.dead && player.life != null && player.life.morph >= 1f)
            {
                var st = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, richText = true };
                float need = player.birthArea * 1.8f;
                // no numbers: readiness shows on the cell itself (it pulses when it can divide); only the key to press
                string msg = fission switch
                {
                    0 => player.genome.ring && FissionReady(player) && !shownSpaceHint ? "SPACE" : "",
                    _ => "",
                };
                if (msg.Length > 0 && !editing) GUI.Label(new Rect(0, Screen.height - 64, Screen.width, 30), $"<b>{msg}</b>", st);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (player != null && !player.dead && player.life != null)
            {
                if (GUI.Button(new Rect(Screen.width - 170, 10, 160, 26), devStable ? "DEV: stability LOCKED" : "DEV: lock stability")) devStable = !devStable;
                if (GUI.Button(new Rect(Screen.width - 170, 40, 160, 26), "DEV: ready to divide"))
                {
                    player.area = Mathf.Max(player.area, player.birthArea * 1.85f);
                    player.life.copyStore = FissionCopyCost(player) + 0.5f;
                }
                if (GUI.Button(new Rect(Screen.width - 170, 70, 160, 26), player.genome.ring ? "DEV: ring ✓" : "DEV: give ring")) player.genome.ring = true;
                if (GUI.Button(new Rect(Screen.width - 170, 100, 160, 26), player.genome.ribosome ? "DEV: ribosome ✓" : "DEV: give ribosome")) { player.genome.ribosome = true; player.genome.Express(); }
            }
            // DEV: see the viruses — spawn a few nearby, or become one
            if (GUI.Button(new Rect(10, Screen.height - 76, 150, 26), "DEV: spawn viruses"))
            {
                Vector2 at = player != null ? player.pos : playerViroid != null ? playerViroid.pos : camPos;
                for (int k = 0; k < 6; k++)
                {
                    var strand = new List<int>(); int len = 3 + r.RangeInt(0, 4); for (int q = 0; q < len; q++) strand.Add(r.RangeInt(0, 4));
                    viroids.Add(new S0Viroid(at + r.InsideUnitCircle().normalized * r.Range(2.5f, 5f), strand, false));
                }
            }
            if (playerViroid == null && player != null && !player.dead && GUI.Button(new Rect(10, Screen.height - 46, 150, 26), "DEV: become virus")) Shed();
            if (player != null && !player.dead && player.life == null &&
                GUI.Button(new Rect(Screen.width - 150, 10, 140, 28), "DEV: become S1"))
            {
                // give the cell a working replicator (the first recipe) and graduate it
                if (player.ReplicatorCount == 0)
                {
                    var ch = new Chain(); ch.seq.AddRange(motifs[0]); Protocell.Classify(ch, motifs); ch.glow = 1f;
                    player.chains.Add(ch); playerFound[0] = true;
                    UpdateGenomeReplicator(player);
                }
                player.replicatorGenerations = Mathf.Max(player.replicatorGenerations, 2);
                Graduate(player);
                DevSpawnLife(16);
            }
#endif
            if (!choicePending) return;
            float w = 360f, h = 150f;
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.38f, w, h);
            GUI.Box(rect, GUIContent.none);
            var title = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true };
            GUI.Label(new Rect(rect.x, rect.y + 14f, w, 36f), "<b>Become life?</b>", title);
            if (GUI.Button(new Rect(rect.x + 24f, rect.y + 72f, w * 0.5f - 36f, 52f), "Yes")) ChooseLife();
            if (GUI.Button(new Rect(rect.x + w * 0.5f + 12f, rect.y + 72f, w * 0.5f - 36f, 52f), "No (virus)")) ChooseVirus();
        }

        /// TAB: the genome. Pathway genes, what each earns here, what it costs, and the priority (expression) slider —
        /// the player regulates genes; they never pick new ones from a menu.
        void GenomePanel()
        {
            var g = player.genome; var L = player.life;
            var area = new Rect(10, 60, 380, Screen.height - 140);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 10, area.y + 8, area.width - 20, area.height - 16));
            var h = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            var small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, richText = true };
            GUILayout.Label($"<b>GENOME</b>  {g.genes.Count}/{g.geneSlots} genes   ·   upkeep {L.upkeepRate * 60f:0.0}/min", h);
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            Pathway? remove = null;
            foreach (var p in g.genes)
            {
                var info = Pathways.Get(p);
                GUILayout.Space(6);
                GUILayout.Label($"<b>{info.name}</b>  <color=#aaa>({info.source})</color>", h);
                GUILayout.Label($"source here {Bar(L.here[(int)p])}   earning {L.yield[(int)p] * 60f:0.0}/min   share {S1Life.Share(g, p) * 100f:0}%", small);
                GUILayout.BeginHorizontal();
                GUILayout.Label("priority", small, GUILayout.Width(52));
                g.priority[(int)p] = GUILayout.HorizontalSlider(g.priority[(int)p], 0f, 1f);
                if (g.genes.Count > 1 && GUILayout.Button("silence", GUILayout.Width(60))) remove = p;
                GUILayout.EndHorizontal();
                GUILayout.Label($"<color=#bbb>{info.science}</color>", small);
            }
            if (remove.HasValue) g.RemoveGene(remove.Value);
            if (pendingOffer >= 0)
            {
                GUILayout.Space(10);
                GUILayout.Label($"<b>Free DNA:</b> {Pathways.Get((Pathway)pendingOffer).name} — no room. Replace:", h);
                Pathway? swap = null;
                foreach (var p in g.genes) if (GUILayout.Button(Pathways.Get(p).name)) swap = p;
                if (swap.HasValue) { g.RemoveGene(swap.Value); g.AddGene((Pathway)pendingOffer, 0.5f); pendingOffer = -1; }
                if (GUILayout.Button("digest it instead")) pendingOffer = -1;
            }
            // what would pay HERE that you can't use: the hint that sends you toward vents, seeps, light
            GUILayout.Space(10);
            GUILayout.Label("<b>Untapped here</b>", h);
            for (int k = 1; k < Pathways.Count; k++)
            {
                var p = (Pathway)k;
                if (g.Has(p) || L.here[k] < 0.08f) continue;
                GUILayout.Label($"{Pathways.Get(p).source} {Bar(L.here[k])} — cells with <i>{Pathways.Get(p).name}</i> could live on this", small);
            }
            GUILayout.Label("<color=#999>New genes come from: a copied gene drifting at division (likelier where its source is strong) · DNA spilled by dead living cells · digesting cells (Lysis).</color>", small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // two unlabeled bars, bottom centre — meaning comes from behaviour, not words:
        //   STABILITY: drops (and turns amber → red, then trembles) when you're stressed or starving; a small notch marks
        //   where it must be to dive into your cell, and the bar brightens past it.
        //   MUTATIONS: segments; one empties each time you keep a mutant, and they refill with a flash when you divide.
        float stabShown = 1f, mutFlash; int mutShown = -1;
        float Stability(Protocell c) => c.life.stability;
        static Texture2D iconStab, iconCplx;
        /// Two tiny wordless icons: a membrane ring (stability) and three linked molecules (complexity).
        static void MakeIcons()
        {
            if (iconStab != null) return;
            const int N = 32;
            iconStab = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            iconCplx = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) / N * 2f - Vector2.one;
                    // a cell: soft ring + faint inside + a little nucleus-like dot
                    float r = p.magnitude;
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.72f) / 0.12f);
                    float fill = r < 0.72f ? 0.25f : 0f;
                    float dot = Mathf.Clamp01(1f - (p - new Vector2(-0.15f, 0.12f)).magnitude / 0.2f);
                    float a1 = Mathf.Clamp01(ring + fill + dot);
                    iconStab.SetPixel(x, y, new Color(1f, 1f, 1f, a1));
                    // complexity: three linked molecules
                    Vector2 m1 = new Vector2(-0.5f, -0.35f), m2 = new Vector2(0.5f, -0.3f), m3 = new Vector2(0.0f, 0.5f);
                    float c1 = Mathf.Max(Mathf.Clamp01(1f - (p - m1).magnitude / 0.28f), Mathf.Max(Mathf.Clamp01(1f - (p - m2).magnitude / 0.24f), Mathf.Clamp01(1f - (p - m3).magnitude / 0.26f)));
                    float l1 = SegI(p, m1, m3), l2 = SegI(p, m2, m3), l3 = SegI(p, m1, m2);
                    float a2 = Mathf.Clamp01(c1 * 1.6f + Mathf.Max(l1, Mathf.Max(l2, l3)) * 0.8f);
                    iconCplx.SetPixel(x, y, new Color(1f, 1f, 1f, a2));
                }
            iconStab.Apply(); iconCplx.Apply();
        }
        static float SegI(Vector2 p, Vector2 a, Vector2 b) { var pa = p - a; var ba = b - a; float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba)); return Mathf.Clamp01(1f - (pa - ba * h).magnitude / 0.07f); }

        void DrawBars()
        {
            MakeIcons();
            var tex = Texture2D.whiteTexture; var prev = GUI.color;
            float w = 220f, h = 6f, x = (Screen.width - w) * 0.5f, y = Screen.height - 34f;
            // stability
            stabShown = Mathf.Lerp(stabShown, Stability(player), 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f));
            bool ready = Equilibrium(player);
            float shake = stabShown < 0.3f ? Mathf.Sin(Time.unscaledTime * 40f) * (0.3f - stabShown) * 6f : 0f;
            var col = stabShown > 0.5f ? Color.Lerp(new Color(0.95f, 0.75f, 0.3f), new Color(0.45f, 0.9f, 0.85f), (stabShown - 0.5f) * 2f)
                                       : Color.Lerp(new Color(0.95f, 0.3f, 0.25f), new Color(0.95f, 0.75f, 0.3f), stabShown * 2f);
            GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(new Rect(x - 1 + shake, y - 1, w + 2, h + 2), tex);
            col = Color.Lerp(col, Color.white, stabFlash * 0.6f);
            GUI.color = new Color(col.r, col.g, col.b, ready ? 0.95f : 0.7f); GUI.DrawTexture(new Rect(x + shake, y - stabFlash * 2f, w * stabShown, h + stabFlash * 4f), tex);
            GUI.color = new Color(1f, 1f, 1f, ready ? 0.9f : 0.4f); GUI.DrawTexture(new Rect(x + w * 0.5f - 1 + shake, y - 3, 2, h + 6), tex);   // the notch
            // mutations
            int max = 8, have = Mathf.Clamp(Mathf.FloorToInt(ComplexityFill), 0, max);
            if (mutShown >= 0 && have > mutShown) mutFlash = 1f;
            mutShown = have;
            mutFlash = Mathf.Max(0f, mutFlash - Time.unscaledDeltaTime * 1.5f);
            float sw = (w - (max - 1) * 3f) / max, y2 = y + 12f;
            float cf = ComplexityFill;
            for (int i = 0; i < max; i++)
            {
                float part = Mathf.Clamp01(cf - i);                                   // segments fill smoothly as you gather
                GUI.color = new Color(1f, 1f, 1f, 0.12f); GUI.DrawTexture(new Rect(x + i * (sw + 3f), y2, sw, 4f), tex);
                if (part > 0f)
                {
                    GUI.color = new Color(0.92f, 0.9f, 1f, 0.85f) + new Color(0.1f, 0.1f, 0.1f, 0f) * mutFlash * 3f;
                    GUI.DrawTexture(new Rect(x + i * (sw + 3f), y2, sw * part, 4f), tex);
                }
            }
            // the icons: a little cell for stability, linked molecules for complexity
            GUI.color = new Color(col.r, col.g, col.b, 0.9f); GUI.DrawTexture(new Rect(x - 22f + shake, y - 6f, 16f, 16f), iconStab);
            GUI.color = new Color(0.92f, 0.9f, 1f, 0.85f); GUI.DrawTexture(new Rect(x - 22f, y2 - 6f, 16f, 16f), iconCplx);
            GUI.color = prev;
        }

        void DrawVirusBars()
        {
            MakeIcons();
            var v = playerViroid; var tex = Texture2D.whiteTexture; var prev = GUI.color;
            float w = 220f, x = (Screen.width - w) * 0.5f, y = Screen.height - 34f;
            float integ = Mathf.Clamp01(v.rnaIntegrity);
            var col = Color.Lerp(new Color(0.95f, 0.3f, 0.25f), new Color(0.8f, 0.6f, 1f), integ);
            float cohv = v.interior ? Coherence : 1f;
            if (!v.interior)
            {
                // outside: the particle slowly decays (a thin bar; a well-built virus lasts much longer)
                GUI.color = new Color(0f, 0f, 0f, 0.3f); GUI.DrawTexture(new Rect(x - 1, y + 1, w + 2, 4), tex);
                GUI.color = new Color(0.8f, 0.7f, 1f, 0.7f); GUI.DrawTexture(new Rect(x, y + 2, w * Mathf.Clamp01(v.rnaIntegrity), 2), tex);
            }
            col = Color.Lerp(new Color(0.95f, 0.35f, 0.3f), new Color(0.85f, 0.75f, 1f), cohv);
            // the shell's soundness tints the icon (dull = weak, bright = sound)
            float shake = (1f - cohv) * Mathf.Sin(Time.unscaledTime * 37f) * 2.5f;
            GUI.color = col; GUI.DrawTexture(new Rect(x - 22f + shake, y + 17f, 16f, 16f), iconStab);
            {
                // progress: one segment per tier; the current one fills as you eat in patterns
                float yy = y + 22f, sw = (w - (MaxTier - 1) * 3f) / MaxTier;
                for (int i = 0; i < MaxTier; i++)
                {
                    float part = Mathf.Clamp01(vProgress - i);
                    GUI.color = new Color(1f, 1f, 1f, 0.12f); GUI.DrawTexture(new Rect(x + i * (sw + 3f), yy, sw, 5f), tex);
                    if (part > 0f) { GUI.color = new Color(0.92f, 0.82f, 1f, 0.9f); GUI.DrawTexture(new Rect(x + i * (sw + 3f), yy, sw * part, 5f), tex); }
                }
                GUI.color = new Color(0.92f, 0.88f, 1f, 0.85f); GUI.DrawTexture(new Rect(x - 22f, yy - 5f, 16f, 16f), iconCplx);
                // your current pattern: its compounds as dots (bright = held consistently; a pulse on each fitting bite)
                for (int i = 0; i < patKinds.Count; i++)
                {
                    var pc = PartColor(patKinds[i]); pc.a = 0.35f + 0.65f * patConsistency;
                    GUI.color = pc; GUI.DrawTexture(new Rect(x + w + 10f + i * 11f, yy - 2f, 8f, 8f), tex);
                }
            }
            if (v.interior)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(new Rect(x - 1, y + 11, w + 2, 6), tex);
                GUI.color = new Color(1f, 0.45f, 0.6f, 0.9f); GUI.DrawTexture(new Rect(x, y + 12, w * Mathf.Clamp01(hijack), 4), tex);
                GUI.color = new Color(1f, 0.45f, 0.6f, 0.9f); GUI.DrawTexture(new Rect(x - 22f, y + 7f, 16f, 16f), iconStab);
            }
            GUI.color = prev;
        }

        static string Bar(float v) { int n = Mathf.RoundToInt(Mathf.Clamp01(v) * 8f); return "<color=#9cf>" + new string('|', n) + "</color><color=#444>" + new string('|', 8 - n) + "</color>"; }

        /// DEV: populate the pool with living cells to test S1 — a third by the vent, a third by the seep, the rest anywhere;
        /// each graduates normally, so it takes the pathway that pays where it is.
        void DevSpawnLife(int n)
        {
            for (int k = 0; k < n; k++)
            {
                var g = new Genome { hue = r.Range(0f, 1f), saturation = r.Range(0.4f, 0.8f), motility = r.Range(0.2f, 0.6f) };
                Vector2 pos = pool.RandomWetPoint(ref r, 0.04f);
                if (k % 3 == 0) pos = biome.ventCentre + Random.insideUnitCircle * biome.ventRadius * 0.6f;
                else if (k % 3 == 1) pos = biome.seepCentre + Random.insideUnitCircle * biome.seepRadius * 0.6f;
                if (pool.Depth(pos) <= 0.01f) pos = pool.RandomWetPoint(ref r, 0.04f);
                var c = new Protocell(g, pos, r.Range(1.4f, 2.2f)) { wanderSeed = r.Range(0f, 100f) };
                var ch = new Chain(); ch.seq.AddRange(motifs[k % motifs.Length]); Protocell.Classify(ch, motifs); ch.glow = 1f;
                c.chains.Add(ch); UpdateGenomeReplicator(c);
                c.replicatorGenerations = 2;
                world.species.Born(c, world.species.Found(g, "dev-life").id);
                cells.Add(c);
                Graduate(c);
                c.life.energy.raw = 2f;
            }
            Debug.Log($"[CellStage] DEV: spawned {n} living cells");
        }

        List<int>[] realMotifs; bool testMotifs;
        /// Swap the recipes IN PLACE (the seabed view and every cell share this array), then re-check every chain.
        void ToggleTestMotifs()
        {
            if (realMotifs == null) { realMotifs = new List<int>[motifs.Length]; for (int i = 0; i < motifs.Length; i++) realMotifs[i] = new List<int>(motifs[i]); }
            testMotifs = !testMotifs;
            for (int i = 0; i < motifs.Length; i++)
                motifs[i] = testMotifs ? new List<int> { 0, 0, 0 } : new List<int>(realMotifs[i]);
            foreach (var c in cells) foreach (var ch in c.chains) Protocell.Classify(ch, motifs);
            Debug.Log($"[CellStage] test recipes {(testMotifs ? "ON: AAA (and its mirror BBB)" : "OFF: the planet's own recipes")}");
        }   // AI cells' chemistry milestones go nowhere

        /// WASD / arrows, or the left mouse button pulling toward the cursor, relative to `from`.
        Vector2 ReadInput(Protocell from) => ReadInput(from.pos, from.Radius);
        Vector2 ReadInput(Vector2 from, float deadzone)
        {
            Vector2 dir = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dir.y += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dir.y -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dir.x += 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dir.x -= 1;
            var mp = Input.mousePosition;
            bool mouseIn = Application.isFocused && mp.x >= 0 && mp.y >= 0 && mp.x <= Screen.width && mp.y <= Screen.height;
            if (dir == Vector2.zero && mouseIn && dragGene < 0 && !editing && !(genomePanel && mp.x < 400f))
            {
                Vector2 mw = world.cam.ScreenToWorldPoint(mp);
                Vector2 to = mw - from; float m = to.magnitude;
                float dz = Mathf.Max(deadzone, 0.3f) * 0.6f;
                if (m > dz) dir = to / m * Mathf.Clamp01((m - dz) / (dz * 5f));
            }
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            return dir;
        }

        void PlayerControl(float dt)
        {
            if (editing) { Steer(player, Vector2.zero, dt); player.life.editing = true; EditorInput(); GeneDragTick(); return; }
            if (player.life != null) player.life.editing = false;
            Steer(player, fission >= 2 ? Vector2.zero : ReadInput(player) * (fission == 1 ? 0.5f : 1f), dt);   // pulling the genome apart: you can't swim
            if (Input.GetKeyDown(KeyCode.F2) && player.life != null) genomePanel = !genomePanel;   // DEV: the numbers behind the genome
            if (player.life != null) GeneDrag();
            float sc = Input.mouseScrollDelta.y;
            if (sc > 0.01f && zoom <= 0.501f && player.life == null && seabedAssembly.CanEnter) { seabedAssembly.Enter(); return; }
            if (sc > 0.01f && zoom <= 0.501f && player.life != null && Equilibrium(player)) { editing = true; dragGene = -1; return; }   // into the cell   // past the closest zoom → down to the seabed
            if (Mathf.Abs(sc) > 0.01f) zoom = Mathf.Clamp(zoom * (1f - sc * 0.1f), 0.5f, 3f);
            // hold X: shed the membrane and go on as a naked strand (the viroid path)
            if (Input.GetKey(KeyCode.X)) { shedHold += dt; if (shedHold > 1.5f) { shedHold = 0f; Shed(); } }
            else shedHold = Mathf.Max(0f, shedHold - dt * 2f);
        }

        /// Low-Reynolds swimming: velocity follows intent almost instantly, no coasting. Dry ground pins you.
        void Steer(Protocell c, Vector2 dir, float dt)
        {
            float depth = pool.Depth(c.pos);
            float medium = depth <= 0.003f ? 0.25f : depth < 0.04f ? 0.7f : 1f;   // stranded: a slow crawl, not a stop
            float speed = (1.6f + c.genome.motility * 2.2f) / (1f + c.Radius * 0.25f) * medium * (c.life == null ? 1.7f : c.player ? 1.15f : 0.8f);
            Vector2 target = dir * speed;
            // pushing against the shore while wet: the membrane is squeezed (shear) — enough of it splits a big bubble
            if (depth > 0.003f && dir.sqrMagnitude > 0.25f && pool.Depth(c.pos + dir.normalized * c.Radius * 1.1f) <= 0.003f)
                c.shear += dt;
            else c.shear = Mathf.Max(0f, c.shear - dt * 2f);
            if (depth < 0.04f && depth > 0.003f) target += pool.Downhill(c.pos) * (0.04f - depth) * 40f;   // the ebbing film drags you to the hollows
            if (depth > 0.003f) target += Flow(c.pos) * (FlowCoupling / (1f + c.Radius * 0.4f));          // the pool's currents carry bubbles (big ones less)
            c.vel = Vector2.Lerp(c.vel, target, 1f - Mathf.Exp(-dt * 10f));
            c.pos += c.vel * dt;
            c.pos.x = Mathf.Clamp(c.pos.x, 1f, TidePool.W - 1f); c.pos.y = Mathf.Clamp(c.pos.y, 1f, TidePool.H - 1f);
            c.pos = vents.PushOut(c.pos, c.Radius);
            c.pos = features.PushOut(c.pos, c.Radius);                     // vent mounds are solid rock
        }

        void SetupMusic(Transform parent)
        {
            var go = new GameObject("CellMusic");
            go.transform.SetParent(parent, false);
            go.AddComponent<AudioSource>();
            music = go.AddComponent<CellMusic>();
            if (Object.FindFirstObjectByType<AudioListener>() == null && world.cam != null) world.cam.gameObject.AddComponent<AudioListener>();
        }

        /// The music listens to the pool: biome, light, tide, the current around you, food, company, danger.
        void FeedMusic()
        {
            if (music == null) return;
            music.biome = (int)pool.biome.kind;
            Pathways.LightEra = false;
            Pathways.ProteinEraDefault = player != null && player.genome.ribosome;
            if (player != null && !player.dead && player.life != null && !player.genome.ribosome && RibosomeReady(player.genome)) { player.genome.ribosome = true; player.genome.Express(); }
            if (player != null && !player.dead && player.genome.ribosome && !ribosomeShown) { ribosomeShown = true; world.Banner("RIBOSOME", ""); music.Event(CellMusic.Cue.Gene); }
            music.themeSeed = (int)(world.ctx.planetSeed & 0x7fffffff);
            music.day = pool.Daylight;
            music.tideRate = Mathf.Cos(pool.time * 2f * Mathf.PI / pool.tidePeriod + 1.2f);
            Vector2 at = player != null && !player.dead ? player.pos : camPos;
            music.flow = Flow(at).magnitude;
            int food = 0;
            foreach (var m in field.motes) if (m.alive && m.kind == MoteKind.Organic && (m.pos - at).sqrMagnitude < 144f) food++;
            music.food = Mathf.Clamp01(food / 10f);
            int near = 0; float danger = 0f;
            foreach (var c in cells)
            {
                if (c.dead || c.player) continue;
                float d2 = (c.pos - at).sqrMagnitude;
                if (d2 < 225f) near++;
                if (player != null && c.life != null && d2 < 100f && (player.life == null || CanDigest(c, player))) danger = Mathf.Max(danger, 1f - Mathf.Sqrt(d2) / 10f);
            }
            if (player != null && player.life != null) danger = Mathf.Max(danger, player.life.starvation * 0.6f);
            music.crowd = Mathf.Clamp01(near / 8f);
            music.danger = danger;
        }

        static float BayU(int k, int slots) => 0.08f + k / (float)Mathf.Max(slots, 1) * 0.8f;

        /// A point on the genome loop (u in 0..1): an irregular folded path, never a polygon.
        Vector2 GenomePoint(Protocell c, float u, float t, float fr, Vector2 center)
        {
            float sd = c.wanderSeed; u *= 6.2831853f;
            var off = new Vector2(Mathf.Sin(u + sd) * 0.55f + Mathf.Sin(u * 2.618f + sd * 1.7f + t * 0.11f) * 0.3f + Mathf.Sin(u * 4.236f + sd * 0.3f) * 0.15f,
                                  Mathf.Cos(u + sd * 0.8f) * 0.5f + Mathf.Cos(u * 3.14f + sd * 2.1f - t * 0.09f) * 0.32f + Mathf.Sin(u * 5.1f + sd) * 0.12f);
            return center + off * fr;
        }

        /// Grab a gene knot: drag it away from the loop to turn it up (bigger knot, more of the cell's effort), toward it
        /// to turn it down; drag it out through the membrane and let go to cut it out (it drifts off as a free scrap).
        void GeneDrag()
        {
            var c = player; var g = c.genome; float rad = c.Radius, t = pool.time;
            float ms = Mathf.Clamp(rad * 0.1f, 0.12f, 0.22f);
            Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
            if (Input.GetMouseButtonDown(0))
            {
                for (int k = 0; k < g.bays.Count; k++)
                {
                    if (g.bays[k] == null) continue;
                    var bp = GenomePoint(c, BayU(k, g.geneSlots), t, rad * 0.26f, c.pos);
                    if ((mw - bp).magnitude < ms * 2.5f) { dragGene = k; dragOut = false; break; }
                }
            }
            if (dragGene < 0) return;
            if (dragGene >= g.bays.Count || g.bays[dragGene] == null) { dragGene = -1; return; }
            int mp = Pathways.Match(g.bays[dragGene], out int merr);
            var basePos = GenomePoint(c, BayU(dragGene, g.geneSlots), t, rad * 0.26f, c.pos);
            dragOut = (mw - c.pos).magnitude > rad * 1.05f;
            if (!dragOut && merr <= 1) g.priority[mp] = Mathf.Clamp(0.15f + (mw - basePos).magnitude / (rad * 0.45f), 0.05f, 1f);
            if (!Input.GetMouseButton(0))
            {
                if (dragOut)
                {
                    g.bays[dragGene] = null; g.Express();
                    if (merr <= 1) fragments.Add(new GeneFragment { pos = c.pos + (mw - c.pos).normalized * rad * 1.4f, p = (Pathway)mp, life = 45f, seed = r.Range(0f, 100f) });
                }
                dragGene = -1; dragOut = false;
            }
        }

        // ── THE GENE EDITOR: zoom all the way into your cell while it is in equilibrium (stable, fed, not dividing).
        // Each bay holds 4 bases. Click a base to change it (left = next, right = previous); click an empty bay to
        // start one; shift-click a bay to clear it. Each change costs energy. A bay that spells a real recipe glows in
        // its gene's colour; one base off glows dimly (it works, leaky); anything else is RED — a misfolded protein that
        // costs upkeep and strains the membrane. Scroll out (or lose your balance) to leave.
        bool editing;
        bool devStable, shownSpaceHint, ribosomeShown;
        List<int> lastViroidStrand;
        // light glints · H₂ fizz · sulfide wisps · iron flecks (food is the organic particles)
        struct EMote { public Vector2 pos, vel; public int type; public float life, seed; }
        readonly List<EMote> emotes = new();
        static readonly Color[] EColor = { new Color(1f, 0.95f, 0.7f), new Color(0.75f, 0.95f, 1f), new Color(0.95f, 0.9f, 0.45f), new Color(0.75f, 0.4f, 0.2f), new Color(0.95f, 0.78f, 0.82f) };
        float absorbSoundT;

        static float TypeYield(Protocell c, int type) => type switch
        {
            0 => c.life.yield[(int)Pathway.Rhodopsin] + c.life.yield[(int)Pathway.AnoxygenicPhoto],
            1 => c.life.yield[(int)Pathway.Hydrogenotrophy],
            2 => c.life.yield[(int)Pathway.SulfurOxidation],
            _ => 0.05f + c.life.yield[(int)Pathway.Hydrogenotrophy] + c.life.yield[(int)Pathway.IronOxidation],
        };
        static bool Uses(Genome g, int type) => type switch
        {
            0 => Pathways.LightEra && (g.Has(Pathway.Rhodopsin) || g.Has(Pathway.AnoxygenicPhoto)),
            1 => g.Has(Pathway.Hydrogenotrophy),
            2 => g.Has(Pathway.SulfurOxidation),
            _ => true,                                    // FeS grains: every living cell takes them up as catalysts
        };

        void TickEnergyMotes(float dt)
        {
            if (player == null || player.dead || player.life == null) { emotes.Clear(); return; }
            Rect view = ViewRect(2f);
            // spawn where each source is present (sampled at random spots in view)
            for (int n = 0; n < 6; n++)
            {
                var p = new Vector2(r.Range(view.xMin, view.xMax), r.Range(view.yMin, view.yMax));
                if (pool.Depth(p) <= 0.005f) continue;
                S1Life.Sources(p, pool, biome, srcScratch4, srcScratch);
                float light = srcScratch[(int)Pathway.Rhodopsin], h2 = srcScratch[(int)Pathway.Hydrogenotrophy], fe = srcScratch[(int)Pathway.IronOxidation];
                float[] w = { Pathways.LightEra ? light * 0.6f : 0f, h2, h2 * (pool.biome.ventStyle == Vents.Style.BlackSmoker ? 0.8f : 0.2f), Mathf.Max(fe, h2 * 0.7f), 0.15f + Mathf.Max(h2, fe) * 0.8f };   // FeS grains near vents and seeps; amino acids (everywhere, richest there)
                for (int type = 0; type < 5; type++)
                    if (emotes.Count < 260 && r.Value < w[type] * dt * 10f)
                        emotes.Add(new EMote { pos = p, type = type, life = r.Range(6f, 12f), seed = r.Range(0f, 100f) });
            }
            // vent jets: each vent near the view sprays its stuff out of its mouths in pulses
            foreach (var vv in vents.list)
            {
                if (!view.Contains(vv.pos) && (vv.pos - (Vector2)world.cam.transform.position).magnitude > 40f) continue;
                float rate = 14f * vv.strength * (0.6f + 0.4f * Mathf.Sin(pool.time * 1.3f + vv.seed));
                if (r.Value < rate * dt && emotes.Count < 320)
                {
                    float a = vv.seed * 3f + r.RangeInt(0, 6) * 2.399f;
                    var mouth = vv.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * vv.coreR * r.Range(0.1f, 0.5f);
                    var dir = (mouth - vv.pos).normalized + r.InsideUnitCircle() * 0.5f;
                    int type = r.Value < 0.4f ? 1 : r.Value < 0.55f ? 4 : r.Value < 0.8f ? 3 : 2;   // H₂ fizz · amino acids · FeS grains · sulfide
                    emotes.Add(new EMote { pos = mouth, vel = dir.normalized * r.Range(3f, 6f), type = type, life = r.Range(5f, 10f), seed = r.Range(0f, 100f) });
                }
            }
            var c = player; float rad = c.Radius;
            for (int i = emotes.Count - 1; i >= 0; i--)
            {
                var e = emotes[i];
                e.life -= dt;
                var drift = Flow(e.pos) * 0.5f + new Vector2(Mathf.Sin(pool.time * 0.7f + e.seed), Mathf.Cos(pool.time * 0.6f + e.seed * 1.3f)) * (e.type == 1 ? 0.5f : 0.2f);
                Vector2 to = c.pos - e.pos; float d = to.magnitude;
                if (Uses(c.genome, e.type) && d < rad * 3f) drift += to / Mathf.Max(d, 0.01f) * (3f - d / rad) * (0.4f + 6f * TypeYield(c, e.type));   // a better gene pulls harder
                e.vel = Vector2.Lerp(e.vel, drift, 1f - Mathf.Exp(-dt * 3f));
                e.pos += e.vel * dt;
                if (d < rad * 0.9f && Uses(c.genome, e.type))
                {
                    if (e.type == 3) c.life.fes = Mathf.Min(c.life.fes + 1f, 10f);
                    if (e.type == 4) c.life.amino = Mathf.Min(c.life.amino + 1f, 20f);
                    Gather(0.05f);
                    // taken in: the same cool ripple through the membrane for every source
                    c.absorbFlash = Mathf.Max(c.absorbFlash, 0.35f);
                    if (pool.time - absorbSoundT > 0.25f) { absorbSoundT = pool.time; music?.Absorb(true); }
                    emotes.RemoveAt(i); continue;
                }
                if (e.life <= 0f || !view.Contains(e.pos)) { emotes.RemoveAt(i); continue; }
                emotes[i] = e;
            }
        }

        void DrawEnergyMotes(float t)
        {
            foreach (var e in emotes)
            {
                float fade = Mathf.Clamp01(e.life) * Mathf.Clamp01((12f - e.life) * 2f);
                var col = EColor[e.type];
                switch (e.type)
                {
                    case 0: col.a = 0.5f * fade * (0.5f + 0.5f * Mathf.Sin(t * 9f + e.seed * 5f)); motesFree.Add(e.pos, 0.14f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, col); break;   // twinkling glint
                    case 1: col.a = 0.6f * fade; motesFree.Add(e.pos, 0.09f, 0f, 1f, 10, new Vector4(1f, 0.3f, Frac(e.seed), 0), Vector4.zero, col); break;                            // fizz bubble
                    case 4:
                        col.a = 0.8f * fade; motesFree.Add(e.pos, 0.11f, e.seed, 1f, 12, new Vector4(1f, 0f, Frac(e.seed * 0.3f), 0), Vector4.zero, col);
                        break;          // amino acid
                    case 2: col.a = 0.18f * fade; motesFree.Add(e.pos, 0.45f, e.seed, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, col); break;                                         // sulfide wisp
                    default:
                        col.a = 0.8f * fade; motesFree.Add(e.pos, 0.1f, e.seed, 1f, 13, new Vector4(1f, 0f, Frac(e.seed * 0.7f), 0), Vector4.zero, col);
                        break;                     // iron–sulfur cluster
                }
            }
        }

        const float EditCost = 0.12f;
        bool Equilibrium(Protocell c) => c.life != null && c.life.morph >= 1f && c.life.stability > 0.5f && fission == 0;

        // Zoom all the way in while in equilibrium. Each gene is a KNOT of 4 bases on the genome ring (its structure now
        // visible). Click a knot: it offers 3 MUTANTS (each one base different) budding beside it. Hover a mutant to try
        // it in place — your cell immediately runs on it, so you SEE whether its protein works better or worse (energy
        // sparks streaming in, source particles being pulled in). Click a mutant to keep it (costs energy); click away to
        // keep the original. Click an empty bay to DUPLICATE a gene into it (then mutate the copy toward something new).
        // Right-click a knot to cut it out. Everything else in the cell keeps living around the editor.
        QuadBatch editorQ;
        VirusMeshes vmOut, vmIn;                      // virus bodies as faceted meshes (outside / inside a cell)
        float virusComplexity;                        // compounds gathered as a virus → how many nodes your mesh can have
        readonly int[] virusStock = new int[9];       // compounds you're carrying to build with
        bool virusEditing; int vDragNode = -1, vDragStock = -1;
        // ── THE PATTERN GAME (Flow): you become what you eat. A repeating pattern in what you take in — consistent, and
        // the more distinct compounds in it the better, kept going for many cycles — fills your progress; each tier
        // transforms your body. Eating at random barely helps.
        readonly List<int> eaten = new();
        float vProgress; int vTier, patPeriod, patIters; float patConsistency, patQuality;
        readonly List<int> patKinds = new();
        const int MaxTier = 6;
        /// How well you hold together: from how patterned your recent eating has been (a broken pattern still counts for
        /// some; random eating for little). It doesn't fade with time — not eating costs nothing.
        float Coherence => eaten.Count < 4 ? 0.45f : patConsistency * (0.55f + 0.45f * Mathf.Clamp01(new HashSet<int>(patKinds).Count / 3f));
        /// Incoherent viruses tremble, and may unravel: a chance each second, raised by a living host's defences.
        bool Unravels(S0Viroid v, float dt, float hostDefense)
        {
            float coh = Coherence, risk = (1f - coh) * (1f - coh);
            if (r.Value < dt * risk * 3f) v.body?.Impact(r.InsideUnitCircle().normalized, risk * 2.5f);   // the tremble: you can see it coming
            float p = 0.02f * risk * (hostDefense > 0f ? 0.6f + hostDefense * 0.6f : 1f);
            if (r.Value < p * dt) { v.alive = false; v.causeOfDeath = "unravelled"; return true; }
            return false;
        }

        void AnalyzePattern()
        {
            int n = eaten.Count; patPeriod = 0; patConsistency = 0f; patIters = 0; patKinds.Clear(); patQuality = 0f;
            float best = 0f;
            for (int P = 1; P <= 6; P++)
            {
                if (n < P * 2) break;
                int W = Mathf.Min(n, 20), match = 0, tot = 0;
                for (int i = n - W + P; i < n; i++) { if (i - P < 0) continue; tot++; if (eaten[i] == eaten[i - P]) match++; }
                if (tot == 0) continue;
                float cons = match / (float)tot;
                var d = new HashSet<int>(); for (int i = n - P; i < n; i++) d.Add(eaten[i]);
                float score = cons * cons * d.Count;
                if (score > best + 0.05f) { best = score; patPeriod = P; patConsistency = cons; }
            }
            if (patPeriod == 0) return;
            int streak = 0; for (int i = n - 1; i - patPeriod >= 0 && eaten[i] == eaten[i - patPeriod]; i--) streak++;
            patIters = streak / patPeriod;
            for (int i = n - patPeriod; i < n; i++) patKinds.Add(eaten[i]);
            var dk = new HashSet<int>(patKinds);
            patQuality = patConsistency * Mathf.Clamp01(dk.Count / 3f);
        }

        void VirusEat(int kind)
        {
            eaten.Add(kind); if (eaten.Count > 30) eaten.RemoveAt(0);
            int n = eaten.Count;
            bool fits = patPeriod > 0 && n > patPeriod && eaten[n - 1] == eaten[n - 1 - patPeriod];
            AnalyzePattern();
            float gain;
            if (fits && patConsistency > 0.55f)
            {
                int distinct = new HashSet<int>(patKinds).Count;
                gain = 0.035f + 0.03f * distinct * (1f + Mathf.Min(patIters, 8) * 0.15f);   // complex, sustained patterns grow you fast
                music?.Snap(true);
            }
            else { gain = 0.008f; music?.Absorb(false); }                                  // no pattern: barely anything
            vProgress = Mathf.Min(vProgress + gain, MaxTier + 0.999f);
            var b = playerViroid?.body;
            if (b != null) b.externalCohesion = Mathf.Clamp01(0.2f + vTier * 0.11f + patQuality * 0.2f);
            if (vTier < MaxTier && vProgress >= vTier + 1)
            {
                vTier++;
                RebuildVirusForm();
                stabFlash = 1f;
                music?.Event(CellMusic.Cue.Gene);
            }
        }

        void RebuildVirusForm()
        {
            if (playerViroid == null) return;
            var nb = VirusBody.FromTier(vTier, patKinds, StrandSeed(playerViroid.strand));
            nb.externalCohesion = Mathf.Clamp01(0.2f + vTier * 0.11f + patQuality * 0.2f);
            playerViroid.body = nb;
        }
        int NodeBudget => Mathf.Clamp(6 + Mathf.FloorToInt(virusComplexity / 3f), 3, VirusBody.MaxParts); BurstShells bursts; readonly System.Random burstRng = new System.Random(77); bool taughtClick, taughtHover;
        int editSel = -1, editHover = -1, editCount = 3; int[] editOrig; readonly int[][] editVar = new int[5][];
        // COMPLEXITY: what you've gathered (every compound taken in) + who's alive (each living relative in the pool).
        // Gathered complexity is spent to open a new gene slot; relatives fill the bar but aren't spent.
        float gathered;
        const float SlotCost = 8f;
        float ComplexityFill => Mathf.Clamp(gathered + Relatives() * 0.75f, 0f, SlotCost);
        void Gather(float amount) { gathered = Mathf.Min(gathered + amount, SlotCost); }
        int editsLeft => Mathf.FloorToInt(ComplexityFill);
        int Relatives() { int n = 0; foreach (var o in cells) if (!o.dead && o.life != null && !o.player && o.SpeciesId == playerSpecies) n++; return n; }
        // to write a base into a gene you need that base (nucleotide) in your cell
        Vector2 EditorBayPos(Protocell c, int k, out Vector2 dir)
        {
            float ang = Mathf.PI * 0.5f + k * 2f * Mathf.PI / Genome.RnaGeneSlots;
            dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            return c.pos + dir * c.Radius * 0.42f;
        }
        Vector2 EditorVariantPos(Protocell c, int k, int v)
        {
            var bp = EditorBayPos(c, k, out var dir); var tan = new Vector2(-dir.y, dir.x);
            return bp + dir * c.Radius * 0.3f + tan * (v - (editCount - 1) * 0.5f) * c.Radius * 0.2f;
        }

        void EndTrial(bool keep) { editSel = -1; editHover = -1; editOrig = null; }

        int geneFocus = -1, heldBase = -1; bool focusPaid; float stabFlash;
        readonly float[] stemW = new float[4];
        const float U = 0.085f;   // gene-level layout unit (× radius)

        Vector2 TrayPos(Protocell c, int b) { float u = c.Radius * U; return c.pos + new Vector2((b - 1.5f) * u * 1.6f, -u * 4.6f); }
        int selSlot = -1;
        public const int RiboDivisions = 5; public const float RiboThreshold = 0.6f;
        static bool RibosomeReady(Genome g)
        {
            if (g.s1Divisions < RiboDivisions) return false;
            var jobs = new System.Collections.Generic.HashSet<int>();
            foreach (var b in g.bays) { if (b == null) continue; int p = Pathways.Best(b, g.ribosome, out float e); if (e < RiboThreshold) return false; jobs.Add(p); }
            return jobs.Count >= 3;   // three different jobs, all working well
        }

        // screen rects owned by the editor's GUI (world clicks there are ignored)
        readonly System.Collections.Generic.List<Rect> guiRects = new();
        bool OverGui() { var m = Input.mousePosition; var sp = new Vector2(m.x, Screen.height - m.y); foreach (var r0 in guiRects) if (r0.Contains(sp)) return true; return false; }

        void AfterEdit(Protocell c, int[] gs, float before)
        {
            c.genome.Express();
            Pathways.Best(gs, c.genome.ribosome, out float after);
            float gain = after - before;
            if (gain > 0.005f)
            {
                c.life.membraneStress = Mathf.Max(0f, c.life.membraneStress - gain * 0.8f);
                c.life.stability = Mathf.Min(1f, c.life.stability + gain * 0.6f);
                c.life.energy.raw += gain * 1.5f;
                stabFlash = 1f;
            }
            music?.Snap(gain > 0.005f);
        }

        /// Keys: with a bead selected, 1–4 set its base.
        void SetBase(Protocell c, int b)
        {
            var g = c.genome; var gs = g.bays[geneFocus];
            var main = new List<int>(Pathways.Main(gs)); Pathways.Branches(gs, brTmp); Pathways.Bridges(gs, bgTmp);
            if (selSlot < 0 || selSlot >= main.Count || main[selSlot] == b) return;
            if (c.free[b] <= 0 || !Pay(c)) { music?.Absorb(false); return; }
            Pathways.Best(gs, g.ribosome, out float before);
            c.free[b]--; ToStock(c, main[selSlot]); main[selSlot] = b;
            g.bays[geneFocus] = Pathways.Encode(main, brTmp, bgTmp);
            AfterEdit(c, g.bays[geneFocus], before);
        }

        void EditorInput()
        {
            var c = player; var g = c.genome;
            stabFlash = Mathf.Max(0f, stabFlash - Time.deltaTime);
            if (c.life.stability < 0.3f || fission != 0) { geneFocus = -1; dragFrom = -1; editing = false; return; }
            bool back = Input.mouseScrollDelta.y < -0.01f || Input.GetKeyDown(KeyCode.Escape);
            if (back) { if (geneFocus >= 0) { geneFocus = -1; selSlot = -1; } else { editing = false; zoom = 0.6f; } return; }
            Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
            bool l = Input.GetMouseButtonDown(0) && !OverGui(), rgt = Input.GetMouseButtonDown(1) && !OverGui();
            if (geneFocus >= 0 && geneFocus < g.bays.Count && g.bays[geneFocus] != null)
            {
                var gs0 = g.bays[geneFocus];
                // keys: 1–4 put a base in the selected place; ←/→ move along the strand
                for (int b = 0; b < 4; b++) if (Input.GetKeyDown(KeyCode.Alpha1 + b)) SetBase(c, b);
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Tab)) selSlot = (Mathf.Max(selSlot, -1) + 1) % gs0.Length;
                if (Input.GetKeyDown(KeyCode.LeftArrow)) selSlot = (Mathf.Max(selSlot, 0) + gs0.Length - 1) % gs0.Length;
            }
            if (!l && !rgt) return;
            if (geneFocus < 0)
            {
                // ring level: click a gene to open it; click an empty bay to start one; right-click to cut one out
                float pick = c.Radius * 0.12f;
                if (g.geneSlots < g.SlotCap && (mw - EditorBayPos(c, g.geneSlots, out _)).magnitude < pick * 1.3f)
                {
                    // a locked bay: a FULL complexity bar opens it
                    if (l && ComplexityFill >= SlotCost - 0.01f) { gathered = 0f; g.geneSlots++; music?.Event(CellMusic.Cue.Gene); stabFlash = 1f; }
                    else music?.Absorb(false);
                    return;
                }
                for (int k = 0; k < g.geneSlots; k++)
                {
                    if ((mw - EditorBayPos(c, k, out _)).magnitude > pick * 1.3f) continue;
                    while (g.bays.Count <= k) g.bays.Add(null);
                    var seq = g.bays[k];
                    if (rgt && seq != null)
                    {
                        int mp = Pathways.Match(seq, out int err);
                        g.bays[k] = null; g.Express();
                        if (err <= 2) fragments.Add(new GeneFragment { pos = c.pos + Random.insideUnitCircle.normalized * c.Radius * 1.3f, p = (Pathway)mp, life = 45f, seed = r.Range(0f, 100f) });
                        return;
                    }
                    if (!l) return;
                    if (seq == null)
                    {
                        int[] src = null; foreach (var b in g.bays) if (b != null) { src = b; if (r.Value < 0.5f) break; }
                        g.bays[k] = src != null ? (int[])src.Clone() : Pathways.Blank();
                        g.Express();
                    }
                    geneFocus = k; selSlot = -1; focusPaid = false; taughtClick = true; beads.Clear();
                    return;
                }
                return;
            }
            if (geneFocus >= g.bays.Count || g.bays[geneFocus] == null) { geneFocus = -1; return; }
            var gs = g.bays[geneFocus];
            float reach = c.Radius * U * 0.8f;
            if (l)
            {
                // grab: any bead of the gene, or one of your free bases floating in the cell
                dragFrom = -1; pressPos = mw; dragMoved = false;
                float bd = reach;
                var mainG = Pathways.Main(gs); Pathways.Branches(gs, brTmp); Pathways.Bridges(gs, bgTmp);
                for (int i = 0; i < mainG.Length && i < beads.Count; i++) { float d = (mw - BeadWorld(c, i)).magnitude; if (d < bd) { bd = d; dragFrom = i; dragKind = mainG[i]; } }
                for (int j = 0; j < brTmp.Count && j < brBeads.Count; j++) { float d = (mw - BranchWorld(c, j)).magnitude; if (d < bd) { bd = d; dragFrom = BranchFrom + j; dragKind = brTmp[j].y; } }
                for (int j = 0; j < bgTmp.Count && j < bgBeads.Count; j++) { float d = (mw - BridgeWorld(c, j)).magnitude; if (d < bd) { bd = d; dragFrom = BridgeFrom + j; dragKind = bgTmp[j].z; } }
                if (dragFrom < 0)
                    for (int b = 0; b < 4; b++)
                        for (int q = 0; q < Mathf.Min(c.free[b], 8); q++)
                        {
                            float d = (mw - StockPos(c, b, q, pool.time)).magnitude;
                            if (d < bd) { bd = d; dragFrom = -2; dragKind = b; }
                        }
                if (dragFrom == -1) selSlot = -1;
            }
            if (rgt) { dragFrom = -1; selSlot = -1; }
        }

        int dragFrom = -1, dragKind; Vector2 pressPos; bool dragMoved;

        /// Your free bases, floating around the gene inside the cell.
        Vector2 StockPos(Protocell c, int b, int q, float t)
        {
            float a = b * 1.5708f + q * 0.62f + t * 0.04f + 0.3f * Mathf.Sin(t * 0.3f + q);
            float rr = c.Radius * (0.6f + 0.07f * Mathf.Sin(q * 1.3f + t * 0.25f + b));
            return c.pos + new Vector2(Mathf.Cos(a) * 1.25f, Mathf.Sin(a)) * rr;
        }
        Vector2 GeneCenter(Protocell c) => c.pos + new Vector2(0f, -c.Radius * U * 0.4f);

        bool Pay(Protocell c)
        {
            // an edit is paid from the cell's reserve (the stability bar) — a small sliver each
            if (c.life.stability < 0.08f) { music?.Absorb(false); return false; }
            c.life.stability -= 0.015f;
            return true;
        }
        void ToStock(Protocell c, int b) => c.free[b] = Mathf.Min(c.free[b] + 1, 12);

        // ── the gene as a physical chain: beads on springs that settle into the folded shape; branches hang off ──
        readonly List<Vector2> beads = new(), beadVel = new();       // the chain (local units of U·radius, from the gene centre)
        readonly List<Vector2> brBeads = new(), brVel = new();       // branch beads
        readonly List<Vector2Int> brTmp = new();                      // (parent, base) of each branch
        const int BranchFrom = 1000;                                  // dragFrom ≥ this = a branch bead
        Vector2 BeadWorld(Protocell c, int i) => GeneCenter(c) + beads[i] * (c.Radius * U);
        Vector2 BranchWorld(Protocell c, int j) => GeneCenter(c) + brBeads[j] * (c.Radius * U);

        /// Where each chain bead wants to sit, from how it folds.
        void FoldLayout(int[] s, List<Vector2> into)
        {
            into.Clear(); for (int i = 0; i < s.Length; i++) into.Add(Vector2.zero);
            var f = Pathways.Fold(s);
            if (f.pairs == 0)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    float a = Mathf.Lerp(Mathf.PI * 1.1f, -0.1f * Mathf.PI, s.Length == 1 ? 0.5f : i / (float)(s.Length - 1));
                    into[i] = new Vector2(Mathf.Cos(a) * 3.2f, Mathf.Sin(a) * 1.4f - 0.5f);
                }
                return;
            }
            float h = 1.15f;
            for (int k = 0; k < f.pairs; k++) { into[f.pi[k]] = new Vector2(-0.8f, -2.2f + k * h); into[f.pj[k]] = new Vector2(0.8f, -2.2f + k * h); }
            float topY = -2.2f + (f.pairs - 1) * h;
            int n = f.loopEnd - f.loopStart + 1;
            float rad = 0.6f + 0.3f * n;
            for (int k = 0; k < n; k++)
            {
                float a = Mathf.Lerp(Mathf.PI * 1.05f, -0.05f * Mathf.PI, n == 1 ? 0.5f : k / (float)(n - 1));
                into[f.loopStart + k] = new Vector2(Mathf.Cos(a) * Mathf.Max(rad, 0.9f), topY + 0.4f + Mathf.Sin(a) * rad);
            }
            if (f.bulge != 0)
            {
                int near = 0; for (int k = 0; k < f.pairs; k++) if ((f.bulge == 1 ? f.pi[k] : f.pj[k]) < f.bulgeIdx == (f.bulge == 1)) near = k;
                into[f.bulgeIdx] = new Vector2(f.bulge == 1 ? -2.0f : 2.0f, -2.2f + near * h + 0.55f * h);
            }
        }
        readonly List<Vector2> rest = new();
        const float PullOut = 2.8f;   // drag a bead this far from its place and let go → it comes out

        /// A branch's place: sticking out from its parent bead, away from the gene's middle (fanned if several).
        Vector2 BranchRest(int j)
        {
            int parent = brTmp[j].x; if (parent >= beads.Count) return Vector2.zero;
            int nth = 0; for (int k = 0; k < j; k++) if (brTmp[k].x == parent) nth++;
            var outward = beads[parent].sqrMagnitude > 1e-4f ? beads[parent].normalized : Vector2.up;
            float a = Mathf.Atan2(outward.y, outward.x) + (nth == 0 ? 0f : nth == 1 ? 0.7f : -0.7f);
            return beads[parent] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.15f;
        }

        // ── bridges: a base bonded to two beads ──
        readonly List<Vector2> bgBeads = new(), bgVel = new();
        readonly List<Vector3Int> bgTmp = new();
        const int BridgeFrom = 2000;
        Vector2 BridgeWorld(Protocell c, int j) => GeneCenter(c) + bgBeads[j] * (c.Radius * U);
        Vector2 BridgeRest(int j)
        {
            int a = bgTmp[j].x, b = bgTmp[j].y; if (a >= beads.Count || b >= beads.Count) return Vector2.zero;
            var mid = (beads[a] + beads[b]) * 0.5f; var d = beads[b] - beads[a];
            var perp = new Vector2(-d.y, d.x).normalized;
            if (Vector2.Dot(perp, mid) < 0f) perp = -perp;                    // the point of the triangle faces outward
            return mid + perp * Mathf.Max(0.9f, d.magnitude * 0.75f);
        }
        int Attachments() => brTmp.Count + bgTmp.Count;

        // drop rules (chain units). kinds: 0 nothing · 1 onto a chain bead (replace / swap) · 2 onto a branch bead ·
        // 3 extend the chain past an end (at = index) · 4 a branch on bead `at` · 5 a bridge between `at` and `at2` ·
        // 6 onto a bridge bead
        const float OnTop = 0.55f, Attach = 1.9f, Pair = 1.6f;
        int DropTarget(Vector2 m, int from, out int at) => DropTarget(m, from, out at, out _);
        int DropTarget(Vector2 m, int from, out int at, out int at2)
        {
            at = -1; at2 = -1;
            for (int j = 0; j < brBeads.Count; j++) if (from != BranchFrom + j && (m - brBeads[j]).magnitude < OnTop) { at = j; return 2; }
            for (int j = 0; j < bgBeads.Count; j++) if (from != BridgeFrom + j && (m - bgBeads[j]).magnitude < OnTop) { at = j; return 6; }
            int near = -1, second = -1; float bd = Attach, sd = Pair;
            for (int i = 0; i < beads.Count; i++) { if (i == from) continue; float d = (m - beads[i]).magnitude; if (d < bd) { bd = d; near = i; } }
            if (near < 0) return 0;
            if (bd < OnTop) { at = near; return 1; }
            if (from != -2) return 0;                                         // (gene beads only swap or come out)
            for (int i = 0; i < beads.Count; i++) { if (i == near) continue; float d = (m - beads[i]).magnitude; if (d < sd) { sd = d; second = i; } }
            bool roomForMore = Attachments() < Pathways.MaxBranches;
            // close to TWO beads (and nearer the first than a bead-length): a bridge — bonded to both; their own bond stays
            if (second >= 0 && bd < Pair && roomForMore && Mathf.Abs(near - second) <= 2 && near < 16 && second < 16) { at = Mathf.Min(near, second); at2 = Mathf.Max(near, second); return 5; }
            // off an end, pointing away from the chain: it extends the chain
            bool isEnd = near == 0 || near == beads.Count - 1;
            if (isEnd && beads.Count > 1 && beads.Count < Pathways.MaxLen)
            {
                var away = (beads[near] - beads[near == 0 ? 1 : near - 1]).normalized;
                if (Vector2.Dot(m - beads[near], away) > 0.2f) { at = near == 0 ? 0 : beads.Count; return 3; }
            }
            // otherwise: hanging off this one bead
            int onIt = 0; foreach (var b in brTmp) if (b.x == near) onIt++;
            if (onIt < 2 && roomForMore) { at = near; return 4; }
            return 0;
        }

        /// A strand of fixed thickness from a to b, drawn as short pieces (so stretching never fattens it).
        void Strand(Vector2 a, Vector2 b, float piece, int kind, Vector4 p1, Color col)
        {
            var d = b - a; float len = d.magnitude; if (len < 1e-4f) return;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / piece));
            float ang = Mathf.Atan2(d.y, d.x);
            for (int k = 0; k < n; k++)
            {
                var m = a + d * ((k + 0.5f) / n);
                editorQ.Add(m, len / n * 0.55f, ang, 1f, kind, p1, Vector4.zero, col);
            }
        }

        static void ShiftBridges(List<Vector3Int> bg, int from, int delta)
        {
            for (int j = bg.Count - 1; j >= 0; j--)
            {
                var b = bg[j];
                if (delta < 0 && (b.x == from || b.y == from)) { bg.RemoveAt(j); continue; }
                bg[j] = new Vector3Int(b.x >= from ? b.x + delta : b.x, b.y >= from ? b.y + delta : b.y, b.z);
            }
        }

        static void ShiftParents(List<Vector2Int> br, int from, int delta)
        {
            for (int j = br.Count - 1; j >= 0; j--)
            {
                var b = br[j];
                if (delta < 0 && b.x == from) { br.RemoveAt(j); continue; }
                if (b.x >= from) br[j] = new Vector2Int(b.x + delta, b.y);
            }
        }

        /// Every frame in the gene view: settle the chain and its branches, let the held bead pull its neighbours like
        /// a limb, and on release do what the drop means.
        void GeneDragTick()
        {
            if (geneFocus < 0 || player == null || player.genome.bays.Count <= geneFocus || player.genome.bays[geneFocus] == null) return;
            var c = player; var g = c.genome; var gs = g.bays[geneFocus];
            var mainArr = Pathways.Main(gs); Pathways.Branches(gs, brTmp); Pathways.Bridges(gs, bgTmp);
            float dt = Time.deltaTime, U1 = c.Radius * U;
            FoldLayout(mainArr, rest);
            while (beads.Count < mainArr.Length) { beads.Add(rest[beads.Count] + Random.insideUnitCircle * 0.3f); beadVel.Add(Vector2.zero); }
            while (beads.Count > mainArr.Length) { beads.RemoveAt(beads.Count - 1); beadVel.RemoveAt(beadVel.Count - 1); }
            while (brBeads.Count < brTmp.Count) { brBeads.Add(BranchRest(brBeads.Count)); brVel.Add(Vector2.zero); }
            while (brBeads.Count > brTmp.Count) { brBeads.RemoveAt(brBeads.Count - 1); brVel.RemoveAt(brVel.Count - 1); }
            while (bgBeads.Count < bgTmp.Count) { bgBeads.Add(BridgeRest(bgBeads.Count)); bgVel.Add(Vector2.zero); }
            while (bgBeads.Count > bgTmp.Count) { bgBeads.RemoveAt(bgBeads.Count - 1); bgVel.RemoveAt(bgVel.Count - 1); }
            Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2 mLocal = (mw - GeneCenter(c)) / U1;
            if (dragFrom != -1 && (mw - pressPos).magnitude > U1 * 0.3f) dragMoved = true;
            bool held(int i) => dragMoved && i == dragFrom;
            for (int i = 0; i < beads.Count; i++)
            {
                if (held(i)) { beads[i] = rest[i] + Vector2.ClampMagnitude(mLocal - rest[i], PullOut + 0.6f); beadVel[i] = Vector2.zero; continue; }
                var v = beadVel[i] + (rest[i] - beads[i]) * (40f * dt); v *= Mathf.Exp(-dt * 7f);
                beadVel[i] = v; beads[i] += v * dt;
            }
            for (int it = 0; it < 5; it++)
                for (int i = 0; i + 1 < beads.Count; i++)
                {
                    var d = beads[i + 1] - beads[i]; float len = d.magnitude;
                    if (len < 1e-4f || len < 1.15f) continue;
                    var corr = d * (1f - 1.15f / len) * 0.5f;
                    bool fa = held(i), fb = held(i + 1);
                    if (!fa) beads[i] += fb ? corr * 2f : corr;
                    if (!fb) beads[i + 1] -= fa ? corr * 2f : corr;
                }
            for (int j = 0; j < brBeads.Count; j++)
            {
                var r0 = BranchRest(j);
                if (held(BranchFrom + j)) { brBeads[j] = r0 + Vector2.ClampMagnitude(mLocal - r0, PullOut + 0.6f); brVel[j] = Vector2.zero; continue; }
                var v = brVel[j] + (r0 - brBeads[j]) * (40f * dt); v *= Mathf.Exp(-dt * 7f);
                brVel[j] = v; brBeads[j] += v * dt;
            }
            for (int j = 0; j < bgBeads.Count; j++)
            {
                var r0 = BridgeRest(j);
                if (held(BridgeFrom + j)) { bgBeads[j] = r0 + Vector2.ClampMagnitude(mLocal - r0, PullOut + 0.6f); bgVel[j] = Vector2.zero; continue; }
                var v = bgVel[j] + (r0 - bgBeads[j]) * (40f * dt); v *= Mathf.Exp(-dt * 7f);
                bgVel[j] = v; bgBeads[j] += v * dt;
            }
            if (dragFrom == -1 || Input.GetMouseButton(0)) return;

            // ── released ──
            int from = dragFrom; dragFrom = -1;
            if (!dragMoved) { selSlot = from >= 0 && from < BranchFrom ? from : -1; return; }   // a tap selects a chain bead (then 1–4)
            Pathways.Best(gs, g.ribosome, out float before);
            int kind = DropTarget(mLocal, from, out int at, out int at2);
            var main = new List<int>(mainArr); var br = new List<Vector2Int>(brTmp); var bg = new List<Vector3Int>(bgTmp);
            if (from == -2)
            {
                if (c.free[dragKind] <= 0) return;
                switch (kind)
                {
                    case 1: if (main[at] == dragKind || !Pay(c)) return; c.free[dragKind]--; ToStock(c, main[at]); main[at] = dragKind; break;
                    case 2: if (br[at].y == dragKind || !Pay(c)) return; c.free[dragKind]--; ToStock(c, br[at].y); br[at] = new Vector2Int(br[at].x, dragKind); break;
                    case 3: if (!Pay(c)) return; c.free[dragKind]--; main.Insert(at, dragKind); ShiftParents(br, at, 1); ShiftBridges(bg, at, 1); beads.Insert(at, mLocal); beadVel.Insert(at, Vector2.zero); break;
                    case 5: if (!Pay(c)) return; c.free[dragKind]--; bg.Add(new Vector3Int(at, at2, dragKind)); break;
                    case 6: if (bg[at].z == dragKind || !Pay(c)) return; c.free[dragKind]--; ToStock(c, bg[at].z); bg[at] = new Vector3Int(bg[at].x, bg[at].y, dragKind); break;
                    case 4: if (!Pay(c)) return; c.free[dragKind]--; br.Add(new Vector2Int(at, dragKind)); break;
                    default: return;
                }
            }
            else if (from >= BridgeFrom)
            {
                int j = from - BridgeFrom;
                if (kind == 1) { if (main[at] == bg[j].z || !Pay(c)) return; int tmp = main[at]; main[at] = bg[j].z; bg[j] = new Vector3Int(bg[j].x, bg[j].y, tmp); }
                else if ((bgBeads[j] - BridgeRest(j)).magnitude > PullOut) { if (!Pay(c)) return; ToStock(c, bg[j].z); bg.RemoveAt(j); bgBeads.RemoveAt(j); bgVel.RemoveAt(j); }
                else return;
            }
            else if (from >= BranchFrom)
            {
                int j = from - BranchFrom;
                if (kind == 1) { if (main[at] == br[j].y || !Pay(c)) return; int tmp = main[at]; main[at] = br[j].y; br[j] = new Vector2Int(br[j].x, tmp); }   // swap with a chain bead
                else if ((brBeads[j] - BranchRest(j)).magnitude > PullOut) { if (!Pay(c)) return; ToStock(c, br[j].y); br.RemoveAt(j); brBeads.RemoveAt(j); brVel.RemoveAt(j); }
                else return;
            }
            else
            {
                if (kind == 1) { if (main[at] == main[from] || !Pay(c)) return; int tmp = main[at]; main[at] = main[from]; main[from] = tmp; }   // swap
                else if (kind == 2) { if (br[at].y == main[from] || !Pay(c)) return; int tmp = br[at].y; br[at] = new Vector2Int(br[at].x, main[from]); main[from] = tmp; }
                else if (kind == 6) { if (bg[at].z == main[from] || !Pay(c)) return; int tmp = bg[at].z; bg[at] = new Vector3Int(bg[at].x, bg[at].y, main[from]); main[from] = tmp; }
                else if ((beads[from] - rest[from]).magnitude > PullOut)
                {
                    // pulled away: it comes out of the chain (with its branches) — and the gene refolds, or falls apart
                    if (main.Count <= Pathways.MinLen || !Pay(c)) { music?.Absorb(false); return; }
                    ToStock(c, main[from]); foreach (var b in br) if (b.x == from) ToStock(c, b.y); foreach (var b in bg) if (b.x == from || b.y == from) ToStock(c, b.z);
                    main.RemoveAt(from); ShiftParents(br, from, -1); ShiftBridges(bg, from, -1); beads.RemoveAt(from); beadVel.RemoveAt(from);
                }
                else return;
            }
            g.bays[geneFocus] = Pathways.Encode(main, br, bg);
            if (selSlot >= main.Count) selSlot = -1;
            AfterEdit(c, g.bays[geneFocus], before);
        }

        void DrawKnot(Vector2 at, int[] seq, float size, float t, float seed, float alpha, float thick = 1f)
        {
            // the gene as its folded strand: its bases ARE its shape; a gene near its best form folds cleanly and
            // takes its job's colour, a poor one trembles, a misfolded one is a dull tangle
            int mp = Pathways.Match(seq, out int err);
            var col = err > 2 ? new Color(0.55f, 0.3f, 0.26f) : GeneColor(Pathways.Get((Pathway)mp).knot, err);
            col.a = alpha;
            float work = err <= 2 && player != null && player.life != null ? Mathf.Clamp01(player.life.yield[mp] * 3f) : 0f;
            editorQ.Add(at, size, seed * 0.3f, 1f, 15, new Vector4(thick, work, SeqCode(seq), err > 2 ? 0f : Quality(err)), Vector4.zero, col);
        }

        /// How useful a sequence is to this cell right now: its closeness to the best form of whatever it does,
        /// or — before the ribosome — its closeness to the peptide-maker / an adaptor.
        float SeqScore(Genome g, int[] seq)
        {
            Pathways.Match(seq, out int err, g.ribosome);
            float sc = err == 0 ? 1f : err == 1 ? 0.55f : err == 2 ? 0.2f : 0f;
            if (!g.ribosome)
            {
                sc = Mathf.Max(sc, Pathways.Efficiency(seq, Pathway.Ribosome));
            }
            return sc;
        }

        static float SeqCode(int[] s0) { var s = Pathways.Main(s0); int B(int i) => i < s.Length ? s[i] : 0; return (B(0) + B(1) * 4 + B(2) * 16 + B(3) * 64 + 0.5f) / 256f; }
        static float Quality(int err) => err == 0 ? 1f : err == 1 ? 0.7f : err == 2 ? 0.4f : 0f;
        static Color GeneColor(Color job, int err)
        {
            var pearl = new Color(0.86f, 0.86f, 0.82f);
            var c = Color.Lerp(pearl, job, err == 0 ? 0.75f : err == 1 ? 0.5f : 0.25f);   // the closer to its best form, the more it shows its job
            c.a = job.a; return c;
        }

        void DrawEditor(Protocell c, float t)
        {
            var g = c.genome; float rad = c.Radius;
            editorQ.Add(c.pos, rad * 1.05f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0.02f, 0.04f, 0.05f, geneFocus >= 0 ? 0.8f : 0.6f));
            DrawMemory(c, t);
            if (geneFocus >= 0 && geneFocus < g.bays.Count && g.bays[geneFocus] != null) { DrawGene(c, g.bays[geneFocus], t); return; }
            // ring level: the genome loop with its genes; click one to work on it
            Vector2 prev = Vector2.zero;
            for (int i = 0; i <= 56; i++)
            {
                float a = i / 56f * Mathf.PI * 2f;
                var p = c.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad * (0.42f + 0.012f * Mathf.Sin(a * 5f + t * 0.5f));
                if (i > 0) { Vector2 mid = (p + prev) * 0.5f, sg = p - prev; editorQ.Add(mid, sg.magnitude * 0.6f, Mathf.Atan2(sg.y, sg.x), 1f, 11, new Vector4(1f, 0.1f, i / 56f, 0), Vector4.zero, new Color(0.85f, 0.88f, 0.95f, 0.7f)); }
                prev = p;
            }
            float ks = rad * 0.13f;
            if (g.geneSlots < g.SlotCap)
            {
                // the next locked bay: dim, and it fills in as your complexity bar fills — full = click to open it
                var lp = EditorBayPos(c, g.geneSlots, out _);
                float fill = Mathf.Clamp01(ComplexityFill / SlotCost);
                editorQ.Add(lp, ks * 0.5f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0.6f, 0.62f, 0.66f, 0.1f + 0.12f * fill));
                if (fill >= 1f) editorQ.Add(lp, ks * (0.6f + 0.08f * Mathf.Sin(t * 4f)), 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 1f, 1f, 0.18f));
            }
            for (int k = 0; k < g.geneSlots; k++)
            {
                var bp = EditorBayPos(c, k, out _);
                var seq = k < g.bays.Count ? g.bays[k] : null;
                if (seq == null) { editorQ.Add(bp, ks * 0.45f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0.9f, 0.92f, 0.95f, 0.18f)); continue; }
                DrawKnot(bp, seq, ks, t, k * 1.3f, 1f, 1.7f);
                if (!taughtClick && k == 0) editorQ.Add(bp, ks * (0.9f + 0.15f * Mathf.Sin(t * 4f)), 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 1f, 1f, 0.12f + 0.08f * Mathf.Sin(t * 4f)));
            }
        }

        /// GENE LEVEL: the hairpin, big. Coloured dots are bases (the S0 bases you've collected). Paired bases across
        /// the stem draw together and bond; unpaired ones splay apart. The two loop bases form the pocket at the top —
        /// when the loop fits a molecule (and the stem holds), that molecule drifts in and sits there, working.
        /// MEMORY: the replicators you discovered in S0, as rows of coloured dots across the top of the view. Every gene's
        /// working loop is a pair of neighbouring bases from one of them. Recipes you never found show as faint blanks.
        void DrawMemory(Protocell c, float t)
        {
            float u = c.Radius * U;
            var real = Pathways.Motifs; if (real == null) return;
            for (int m = 0; m < real.Length; m++)
            {
                var row = real[m];
                float y = u * (geneFocus >= 0 ? 5.2f : 9f) - m * u * 0.75f;
                for (int j = 0; j < row.Count; j++)
                {
                    var p = c.pos + new Vector2((j - (row.Count - 1) * 0.5f) * u * 0.7f, y);
                    if (playerFound[m])
                    {
                        var col = MoteChem.Color((MoteKind)row[j], world.ctx.solvent); col.a = 0.85f;
                        editorQ.Add(p, u * 0.17f, 0f, 1f, 10, new Vector4(1f, 0.15f, Frac(j * 0.3f + m), 0), Vector4.zero, col);
                        if (j > 0) editorQ.Add(p - new Vector2(u * 0.35f, 0f), u * 0.18f, 0f, 1f, 8, new Vector4(0.8f, 0, 0, 0), Vector4.zero, new Color(1f, 1f, 1f, 0.25f));
                    }
                    else editorQ.Add(p, u * 0.12f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 1f, 1f, 0.1f));
                }
            }
        }

        void DrawGene(Protocell c, int[] full, float t)
        {
            float u = c.Radius * U;
            var s = Pathways.Main(full); Pathways.Branches(full, brTmp); Pathways.Bridges(full, bgTmp);
            if (beads.Count != s.Length || brBeads.Count != brTmp.Count || bgBeads.Count != bgTmp.Count) return;   // (settles next frame)
            var f = Pathways.Fold(s);
            Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
            bool dragging = dragFrom != -1 && dragMoved;
            var shadowOff = new Vector2(u * 0.18f, -u * 0.22f);
            var strandC = new Color(0.88f, 0.9f, 0.96f, 0.85f);
            // backbone
            for (int i = 0; i + 1 < s.Length; i++)
            {
                Vector2 a = BeadWorld(c, i), b = BeadWorld(c, i + 1);
                Strand(a + shadowOff, b + shadowOff, u * 0.9f, 8, new Vector4(0.5f, 0, 0, 0), new Color(0f, 0f, 0f, 0.22f));
                Strand(a, b, u * 0.9f, 11, new Vector4(1f, 0.15f, i * 0.13f, 0), strandC);
            }
            // branches: a short stalk from the parent
            for (int jb = 0; jb < brTmp.Count; jb++)
                if (brTmp[jb].x < s.Length) Strand(BeadWorld(c, brTmp[jb].x), BranchWorld(c, jb), u * 0.9f, 11, new Vector4(1f, 0.15f, jb * 0.2f, 0), strandC);
            // bridges: bonded to both of their beads (the beads' own bond stays — a triangle)
            for (int jb = 0; jb < bgTmp.Count; jb++)
                if (bgTmp[jb].x < s.Length && bgTmp[jb].y < s.Length)
                {
                    Strand(BeadWorld(c, bgTmp[jb].x), BridgeWorld(c, jb), u * 0.9f, 11, new Vector4(1f, 0.15f, jb * 0.3f, 0), strandC);
                    Strand(BeadWorld(c, bgTmp[jb].y), BridgeWorld(c, jb), u * 0.9f, 11, new Vector4(1f, 0.15f, jb * 0.3f + 0.1f, 0), strandC);
                }
            // rungs
            for (int k = 0; k < f.pairs; k++)
                Strand(BeadWorld(c, f.pi[k]), BeadWorld(c, f.pj[k]), u * 0.8f, 8, new Vector4(1f, 0.6f, 0, 0), new Color(1f, 1f, 1f, 0.45f));
            void Bead(Vector2 p, int b, float depth, bool sel, bool farOut, float seed)
            {
                var col = MoteChem.Color((MoteKind)b, world.ctx.solvent); col.a = 1f;
                if (sel) editorQ.Add(p, u * (0.7f + 0.06f * Mathf.Sin(t * 6f)), 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 1f, 1f, 0.3f));
                editorQ.Add(p + shadowOff, u * 0.42f * depth, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0f, 0f, 0f, 0.35f));
                editorQ.Add(p, u * 0.36f * depth, 0f, 1f, 10, new Vector4(1f, 0.25f, Frac(seed), 0), Vector4.zero, col);
                if (farOut) editorQ.Add(p, u * 0.6f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 0.4f, 0.3f, 0.25f));
            }
            for (int i = 0; i < s.Length; i++)
            {
                float depth = 1f;
                for (int k = 0; k < f.pairs; k++) if (f.pi[k] == i || f.pj[k] == i) depth = 0.9f + 0.18f * Mathf.Sin(k * 1.4f + (f.pi[k] == i ? 0f : Mathf.PI) + t * 0.6f);
                Bead(BeadWorld(c, i), s[i], depth, i == selSlot, dragging && i == dragFrom && (beads[i] - rest[i]).magnitude > PullOut, i * 0.31f);
            }
            for (int jb = 0; jb < brTmp.Count; jb++)
                Bead(BranchWorld(c, jb), brTmp[jb].y, 0.85f, false, dragging && dragFrom == BranchFrom + jb && (brBeads[jb] - BranchRest(jb)).magnitude > PullOut, jb * 0.53f);
            for (int jb = 0; jb < bgTmp.Count; jb++)
                Bead(BridgeWorld(c, jb), bgTmp[jb].z, 0.9f, false, dragging && dragFrom == BridgeFrom + jb && (bgBeads[jb] - BridgeRest(jb)).magnitude > PullOut, jb * 0.71f);
            // the ghost: what letting go here would do
            if (dragging)
            {
                Vector2 mLocal = (mw - GeneCenter(c)) / u;
                int kind = DropTarget(mLocal, dragFrom, out int at, out int at2);
                var from = dragFrom == -2 ? mw : dragFrom >= BridgeFrom ? BridgeWorld(c, dragFrom - BridgeFrom) : dragFrom >= BranchFrom ? BranchWorld(c, dragFrom - BranchFrom) : BeadWorld(c, dragFrom);
                var gcol = new Color(1f, 1f, 1f, 0.35f + 0.15f * Mathf.Sin(t * 8f));
                var link = new Vector4(0.6f, 0, 0, 0);
                if (kind == 1 || kind == 2 || kind == 6)
                {
                    var tp = kind == 1 ? BeadWorld(c, at) : kind == 2 ? BranchWorld(c, at) : BridgeWorld(c, at);   // replace / swap: a ring
                    editorQ.Add(tp, u * (0.7f + 0.05f * Mathf.Sin(t * 8f)), 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 0.85f, 0.5f, 0.35f));
                }
                else if (kind == 3) Strand(from, BeadWorld(c, at == 0 ? 0 : s.Length - 1), u * 0.5f, 8, link, gcol);   // extends the chain: one link to the end bead
                else if (kind == 5) { Strand(from, BeadWorld(c, at), u * 0.5f, 8, link, gcol); Strand(from, BeadWorld(c, at2), u * 0.5f, 8, link, gcol); }   // a bridge: links to BOTH (their bond stays)
                else if (kind == 4) Strand(from, BeadWorld(c, at), u * 0.5f, 8, link, gcol);   // a branch: ONE link
            }
            // your free bases, floating in the cell
            for (int b = 0; b < 4; b++)
            {
                var col = MoteChem.Color((MoteKind)b, world.ctx.solvent); col.a = 0.9f;
                int n = Mathf.Min(c.free[b], 8) - (dragging && dragFrom == -2 && dragKind == b ? 1 : 0);
                for (int q = 0; q < n; q++)
                {
                    var sp = StockPos(c, b, q, t);
                    editorQ.Add(sp + shadowOff * 0.6f, u * 0.28f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0f, 0f, 0f, 0.25f));
                    editorQ.Add(sp, u * 0.25f, 0f, 1f, 10, new Vector4(1f, 0.2f, Frac(q * 0.37f + b), 0), Vector4.zero, col);
                }
            }
            if (dragging && dragFrom == -2)
            {
                var col = MoteChem.Color((MoteKind)dragKind, world.ctx.solvent); col.a = 1f;
                editorQ.Add(mw + shadowOff, u * 0.4f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0f, 0f, 0f, 0.3f));
                editorQ.Add(mw, u * 0.36f, 0f, 1f, 10, new Vector4(1f, 0.4f, 0.5f, 0), Vector4.zero, col);
            }
            // the pocket and nearby jobs
            int mp = Pathways.Best(full, c.genome.ribosome, out float eff);
            Vector2 pocket = GeneCenter(c);
            if (f.pairs > 0)
            {
                Vector2 acc = Vector2.zero; int n = f.loopEnd - f.loopStart + 1;
                for (int k = f.loopStart; k <= f.loopEnd; k++) acc += BeadWorld(c, k);
                pocket = acc / Mathf.Max(n, 1) - new Vector2(0f, u * 0.3f);
            }
            var kc = Pathways.Get((Pathway)mp).knot;
            var drift = new Vector2(Mathf.Sin(t * 1.7f), Mathf.Cos(t * 1.3f)) * u * 1.6f * (1f - eff);
            kc.a = 0.35f + 0.6f * eff;
            editorQ.Add(pocket + drift, u * (0.3f + 0.2f * eff), t, 1f, 13, new Vector4(1f, eff, 0.4f, 0), Vector4.zero, kc);
            int slotN = 0;
            for (int k = 0; k < Pathways.Count; k++)
            {
                if (k == mp || !Pathways.Available((Pathway)k, c.genome.ribosome)) continue;
                float e = Pathways.Efficiency(full, (Pathway)k);
                var jc = Pathways.Get((Pathway)k).knot; jc.a = 0.08f + 0.6f * e * e;
                float a = Mathf.PI * (0.62f + 0.12f * slotN++);
                editorQ.Add(GeneCenter(c) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * c.Radius * 0.45f, u * (0.2f + 0.15f * e), t * 0.3f + k, 1f, 13, new Vector4(1f, e, 0.4f, 0), Vector4.zero, jc);
            }
        }

        /// The editor's on-screen controls: an efficiency meter under each gene (with the ribosome threshold marked),
        /// and in the gene view the structure buttons.
        void EditorGUI()
        {
            guiRects.Clear();
            var c = player; var g = c.genome; var cam = world.cam;
            var tex = Texture2D.whiteTexture; var prevC = GUI.color;
            void Meter(Vector2 world, float w, float eff)
            {
                var sp = cam.WorldToScreenPoint(world); float x = sp.x - w * 0.5f, y = Screen.height - sp.y;
                GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(new Rect(x - 1, y - 1, w + 2, 7), tex);
                GUI.color = Color.Lerp(new Color(0.9f, 0.4f, 0.3f), new Color(0.45f, 0.95f, 0.75f), eff); GUI.DrawTexture(new Rect(x, y, w * eff, 5), tex);
                GUI.color = new Color(1f, 1f, 1f, 0.8f); GUI.DrawTexture(new Rect(x + w * RiboThreshold - 1, y - 2, 2, 9), tex);   // the threshold
            }
            if (geneFocus < 0)
            {
                for (int k = 0; k < g.bays.Count; k++)
                {
                    if (g.bays[k] == null) continue;
                    Pathways.Best(g.bays[k], g.ribosome, out float e);
                    Meter(EditorBayPos(c, k, out _) - new Vector2(0f, c.Radius * 0.1f), 60f, e);
                }
            }
            else if (geneFocus < g.bays.Count && g.bays[geneFocus] != null)
            {
                var gs = g.bays[geneFocus];
                Pathways.Best(gs, g.ribosome, out float e);
                Meter(c.pos + new Vector2(0f, -c.Radius * U * 3.0f), 180f, e);
            }
            GUI.color = prevC;
        }

        Vector2 RibozymeOffset(Protocell c, int k, int q, float t)
        {
            float s1 = k * 5.1f + q * 2.399f + c.wanderSeed, s2 = k * 3.7f + q * 1.618f;
            return new Vector2(Mathf.Sin(t * 0.09f + s1) + 0.5f * Mathf.Sin(t * 0.05f + s2), Mathf.Cos(t * 0.08f + s2) + 0.5f * Mathf.Cos(t * 0.04f + s1)) * 0.42f;
        }
        static int BayOf(Genome g, Pathway p)
        {
            for (int i = 0; i < g.bays.Count; i++) if (g.bays[i] != null && Pathways.Match(g.bays[i], out int e, g.ribosome) == (int)p && e <= 2) return i;
            return -1;
        }
        float ProteinAngle(Protocell c, int k, int q, int n, float t) => q * 6.2831853f / n + k * 1.7f + c.wanderSeed + t * 0.05f;

        struct Spark { public float ang, r0, prog, speed; public Color col; }
        readonly List<Spark> sparks = new();
        float[] sparkAcc = new float[Pathways.Count]; float gradSparkAcc;

        void TickSparks(float dt)
        {
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i]; s.prog += dt * s.speed;
                if (s.prog >= 1f) { sparks.RemoveAt(i); continue; }
                sparks[i] = s;
            }
            if (player == null || player.dead || player.life == null) return;
            var g = player.genome; float t = pool.time;
            gradSparkAcc += player.life.gradYield * dt * 14f;
            while (gradSparkAcc >= 1f && sparks.Count < 120) { gradSparkAcc -= 1f; sparks.Add(new Spark { ang = r.Range(0f, 6.283f), r0 = 0.95f, prog = 0f, speed = r.Range(1.1f, 1.6f), col = new Color(0.85f, 1f, 1f) }); }
            for (int k = 0; k < g.genes.Count; k++)
            {
                var gp = g.genes[k];
                sparkAcc[(int)gp] += player.life.yield[(int)gp] * dt * 14f;          // sparks per second ∝ what the gene earns
                while (sparkAcc[(int)gp] >= 1f && sparks.Count < 120)
                {
                    sparkAcc[(int)gp] -= 1f;
                    int nP = 2 + Mathf.RoundToInt(S1Life.Share(g, gp) * 10f);
                    float ang, r0 = 0.9f;
                    if (Pathways.Get(gp).protein) ang = ProteinAngle(player, k, r.RangeInt(0, nP), nP, t);
                    else { var o = RibozymeOffset(player, k, r.RangeInt(0, 1 + Mathf.RoundToInt(S1Life.Share(g, gp) * 4f)), t); ang = Mathf.Atan2(o.y, o.x); r0 = o.magnitude; }
                    var kc = Pathways.Get(gp).knot;
                    sparks.Add(new Spark { ang = ang, r0 = r0, prog = 0f, speed = r.Range(1.1f, 1.6f), col = Color.Lerp(Color.white, kc, 0.35f) });
                }
            }
        }

        static bool CanDigest(Protocell x, Protocell y) => x.genome.Has(Pathway.Lysis) && x.area > y.area * 1.5f;

        /// Free gene fragments drift; a living cell passing over one takes it up (transformation).
        void TickFragments(float dt)
        {
            for (int i = fragments.Count - 1; i >= 0; i--)
            {
                var f = fragments[i];
                f.life -= dt;
                f.pos += Flow(f.pos) * FlowCoupling * dt;
                if (f.life <= 0f) { fragments.RemoveAt(i); continue; }
                foreach (var c in cells)
                {
                    if (c.dead || c.life == null || (c.pos - f.pos).sqrMagnitude > c.Radius * c.Radius) continue;
                    if (c.genome.Has(f.p)) { fragments.RemoveAt(i); break; }       // digested as food
                    if (c.genome.HasFreeBay)
                    {
                        c.genome.AddGene(f.p, 0.4f);
                        if (c.player) Gather(0.5f);
                        c.absorbFlash = 1f;
                        if (c.player) music?.Event(CellMusic.Cue.Gene);
                    }
                    else { f.pos = c.pos + (f.pos - c.pos).normalized * c.Radius * 1.2f; continue; }   // no room: it bounces off
                    fragments.RemoveAt(i); break;
                }
            }
        }

        void AIControl(Protocell c, float dt)
        {
            if (c.dead) return;
            if (c.life != null && pool.Depth(c.pos) > 0.02f)
            {
                Protocell prey = null; float best = 18f * 18f;
                foreach (var o in cells)
                {
                    if (o.dead || o.life != null || o == c) continue;
                    float d2 = (o.pos - c.pos).sqrMagnitude;
                    if (d2 < best) { best = d2; prey = o; }
                }
                if (prey != null) { Steer(c, (prey.pos - c.pos).normalized, dt); return; }
            }
            Vector2 dir;
            float depth = pool.Depth(c.pos);
            if (depth < 0.02f || c.moisture < 0.7f)
            {
                // seek deeper water: sample a few directions, go where it's wettest
                dir = Vector2.zero; float best = depth;
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f + c.wanderSeed;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    float dd = pool.Depth(c.pos + d * 3f);
                    if (dd > best) { best = dd; dir = d; }
                }
            }
            else
            {
                float t = pool.time * 0.15f;
                float a = TidePool.Noise(c.wanderSeed + t, c.wanderSeed * 0.7f) * Mathf.PI * 4f;
                dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.6f;
                if (c.Pressure > c.BurstThreshold * 0.8f) dir *= 0.2f;   // sluggish when full
            }
            Steer(c, dir, dt);
        }

        void Collide()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var a = cells[i]; if (a.dead) continue;
                for (int j = i + 1; j < cells.Count; j++)
                {
                    var b = cells[j]; if (b.dead) continue;
                    Vector2 d = b.pos - a.pos; float rr = a.Radius + b.Radius, dist = d.magnitude;
                    if (dist < rr && dist > 1e-4f)
                    {
                        Vector2 push = d / dist * (rr - dist) * 0.5f;
                        float wa = b.area / (a.area + b.area);
                        a.pos -= push * wa * 2f; b.pos += push * (1f - wa) * 2f;
                        // sustained contact: the two bubbles' chemistry helps each other along (CellStage_Decisions — S0 is
                        // collaborative: touching another cell benefits you both, it doesn't hurt either)
                        a.contact += Time.deltaTime; b.contact += Time.deltaTime;
                        Protocell eater = a.life != null && b.life == null ? a : b.life != null && a.life == null ? b : null;
                        if (eater == null && a.life != null && b.life != null) eater = CanDigest(a, b) ? a : CanDigest(b, a) ? b : null;
                        if (eater != null)
                        {
                            var prey = eater == a ? b : a;
                            prey.lyse += Time.deltaTime / (eater.genome.Has(Pathway.Lysis) ? 0.8f : 1.5f) / (prey.player && prey.life != null ? 3f : 1f);
                            if (prey.lyse >= 1f)
                            {
                                eater.life.energy.raw += prey.area * 0.6f + prey.chains.Count * 0.15f;
                                eater.absorbFlash = 1f;
                                Kill(prey, "dissolved by a living cell");
                                continue;
                            }
                        }
                        if (a.contact > 1.2f && b.contact > 1.2f)
                        {
                            bool ha = NudgeTowardMotif(a, b), hb = NudgeTowardMotif(b, a);
                            if (ha) a.absorbFlash = Mathf.Max(a.absorbFlash, 0.45f);
                            if (hb) b.absorbFlash = Mathf.Max(b.absorbFlash, 0.45f);
                            a.contact = 0f; b.contact = 0f;
                        }
                    }
                }
            }
        }

        /// One step of drift toward a working recipe, helped by a partner cell. Picks the chain closest to a recipe
        /// (same length as it, or one longer/shorter) and fixes ONE thing — a wrong base, an extra end base, or a
        /// missing one — using a base from this cell, or one donated by the partner. It never completes a recipe:
        /// the last step is yours (or chance's). Returns true if something changed.
        bool NudgeTowardMotif(Protocell c, Protocell partner)
        {
            if (c.sheltered) return false;
            Chain bestC = null; List<int> bestM = null; bool bestComp = false; int bestErr = int.MaxValue;
            foreach (var ch in c.chains)
            {
                if (ch.replicator || ch.copy.Count > 0) continue;
                foreach (var m in motifs)
                    for (int sense = 0; sense < 2; sense++)
                    {
                        if (Mathf.Abs(ch.seq.Count - m.Count) > 1) continue;
                        int n = Mathf.Min(ch.seq.Count, m.Count), err = Mathf.Abs(ch.seq.Count - m.Count);
                        for (int k = 0; k < n; k++)
                        {
                            int want = sense == 0 ? m[k] : MoteChem.Complement(m[k]);
                            if (ch.seq[k] != want) err++;
                        }
                        if (err < bestErr) { bestErr = err; bestC = ch; bestM = m; bestComp = sense == 1; }
                    }
            }
            if (bestC == null || bestErr == 0 || (bestErr == 1 && (c.player || r.Value > 0.04f))) return false;          // nothing close — or one step from done (that one's yours)
            int Want(int k) => bestComp ? MoteChem.Complement(bestM[k]) : bestM[k];
            bool Take(int kind)
            {
                if (c.free[kind] > 0) { c.free[kind]--; return true; }
                if (partner.free[kind] > 0) { partner.free[kind]--; return true; }   // the partner donates
                return false;
            }
            var seq = bestC.seq;
            if (seq.Count > bestM.Count) { c.free[seq[seq.Count - 1]]++; seq.RemoveAt(seq.Count - 1); }          // trim an extra base
            else if (seq.Count < bestM.Count) { int k = Want(seq.Count); if (!Take(k)) return false; seq.Add(k); } // add the missing one
            else
            {
                for (int k = 0; k < seq.Count; k++)
                {
                    int want = Want(k);
                    if (seq[k] == want) continue;
                    if (!Take(want)) return false;
                    c.free[seq[k]]++; seq[k] = want;                     // swap one wrong base for the right one
                    break;
                }
            }
            Protocell.Classify(bestC, motifs);
            return true;
        }

        // ── molecules ──
        static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
        void BuildGrid()
        {
            foreach (var l in grid.Values) l.Clear();
            for (int i = 0; i < field.motes.Count; i++)
            {
                var m = field.motes[i]; if (!m.alive || m.stranded) continue;
                long k = Key((int)(m.pos.x / Cell), (int)(m.pos.y / Cell));
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(8);
                l.Add(i);
            }
        }

        void Absorb(Protocell c, float dt)
        {
            float rad = c.Radius;
            int x0 = (int)((c.pos.x - rad) / Cell), x1 = (int)((c.pos.x + rad) / Cell);
            int y0 = (int)((c.pos.y - rad) / Cell), y1 = (int)((c.pos.y + rad) / Cell);
            float p = 1f - Mathf.Exp(-dt * (0.8f + c.genome.uptake * 3f));
            if (pool.Depth(c.pos) < 0.03f) p *= 0.3f;                         // a stranded / beached bubble takes things in slowly
            for (int gy = y0; gy <= y1; gy++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    if (!grid.TryGetValue(Key(gx, gy), out var l)) continue;
                    foreach (int i in l)
                    {
                        var m = field.motes[i];
                        if (!m.alive || (m.pos - c.pos).sqrMagnitude > rad * rad) continue;
                        if (c.life != null && m.kind != MoteKind.Organic && m.kind != MoteKind.Lipid && ((int)m.kind > 3 || c.free[(int)m.kind] >= 8)) continue;   // alive: food, and bases to build genes with
                        if (r.Value > p) continue;
                        m.alive = false; field.motes[i] = m;
                        if (m.kind == MoteKind.Lipid && c.life != null) { c.genome.leak = Mathf.Min(0.95f, c.genome.leak + 0.012f); c.area += 0.05f; }   // simple fatty acids: a leakier membrane
                        else if (m.kind == MoteKind.Lipid) c.area += 0.16f * (0.4f + c.genome.lipidAffinity);
                        else c.free[(int)m.kind]++;
                        if (c.player)
                        {
                            prog.Reach(Milestone.FirstAbsorb);
                            // a sound for every pickup: bright if it's of use to you, a dull thud if it isn't
                            bool useful = c.life != null ? m.kind == MoteKind.Organic || (int)m.kind <= 3 : m.kind != MoteKind.Clay && m.kind != MoteKind.Organic;
                            music?.Absorb(useful);
                            Gather(c.life != null ? 0.06f : 0.03f);
                        }
                    }
                }
        }

        void UpdateGenomeReplicator(Protocell c)
        {
            Chain best = null;
            foreach (var ch in c.chains) if (ch.replicator && (best == null || ch.seq.Count > best.seq.Count)) best = ch;
            if (best != null && best.seq.Count != c.genome.replicator.Count) { c.genome.replicator.Clear(); c.genome.replicator.AddRange(best.seq); }
        }

        /// 0 → 1 as the membrane approaches double its birth area: the bubble visibly elongates toward a split.
        static float Stretch(Protocell c) => Mathf.Clamp01((c.area / Mathf.Max(c.birthArea, 0.01f) - 1.6f) / 0.4f);

        // ── BINARY FISSION (the player's S1 division, a struggle): bacteria don't do mitosis — they copy the genome,
        // pull the two copies to opposite poles, then constrict a ring (FtsZ) across the middle. Each step costs energy;
        // the copies keep jostling back together; close the ring on a copy and the division fails and hurts.
        int fission;                 // 0 growing · 1 copying · 3 constricting
        float fCopy, fxA, fxB, fFailFlash;
        static float FissionCopyCost(Protocell c) => 0.8f + 0.2f * c.genome.genes.Count;   // a longer genome costs more to copy
        bool FissionReady(Protocell c) => c.area >= c.birthArea * 1.8f && c.life.copyStore >= FissionCopyCost(c);
        bool CopyInMiddle(float x) => Mathf.Abs(x) < 0.22f;

        void PlayerFission(Protocell c, float dt)
        {
            var e = c.life.energy;
            fFailFlash = Mathf.Max(0f, fFailFlash - dt);
            switch (fission)
            {
                case 0:
                    c.pinch = Mathf.Max(0f, c.pinch - dt);
                    if (FissionReady(c) && Input.GetKeyDown(KeyCode.Space)) { shownSpaceHint = true; c.life.copyStore -= FissionCopyCost(c); fission = 1; fCopy = 0f; fxA = fxB = 0f; }
                    break;
                case 1:   // the copy is made along the genome
                    fCopy += dt / 2.5f;
                    if (fCopy >= 1f) fission = 3;
                    break;
                default:
                {
                    float t = pool.time, sd = c.wanderSeed;
                    fxA += (-0.6f - fxA) * 0.7f * dt + (Mathf.PerlinNoise(t * 0.8f, sd) - 0.5f) * 2.4f * dt;
                    fxB += (0.6f - fxB) * 0.7f * dt + (Mathf.PerlinNoise(t * 0.8f, sd + 7.3f) - 0.5f) * 2.4f * dt;
                    if (r.Value < dt * 0.3f) { if (r.Value < 0.5f) fxA += r.Range(0.4f, 0.8f); else fxB -= r.Range(0.4f, 0.8f); }   // a copy slips back
                    fxA = Mathf.Clamp(fxA, -0.85f, 0.85f); fxB = Mathf.Clamp(fxB, -0.85f, 0.85f);
                    if (Input.GetKey(KeyCode.Space) && e.usable + e.raw > 0.05f) { c.pinch += dt / 3.5f; e.raw = Mathf.Max(0f, e.raw - dt * 0.3f); }
                    else c.pinch = Mathf.Max(0f, c.pinch - dt * 0.12f);
                    if (c.pinch > 0.55f && (CopyInMiddle(fxA) || CopyInMiddle(fxB)))
                    {
                        // the ring closed on a genome: the split aborts, the membrane tears, a gene is damaged
                        c.life.membraneStress = Mathf.Min(1f, c.life.membraneStress + 0.35f);
                        c.area *= 0.85f; c.pinch = 0f; fission = 0; fFailFlash = 2.5f;
                        if (c.genome.genes.Count > 1 && r.Value < 0.5f) c.genome.RemoveGene(c.genome.genes[r.RangeInt(1, c.genome.genes.Count)]);
                        music?.Absorb(false);
                        break;
                    }
                    if (c.pinch >= 1f)
                    {
                        bool oneSide = Mathf.Sign(fxA) == Mathf.Sign(fxB);
                        fission = 0;
                        var d = FinishDivision(c);
                        if (oneSide && d != null) Kill(d, "no genome");   // both copies ended up in your half: the other is an empty bag
                    }
                    break;
                }
            }
        }

        void TryDivide(Protocell c, float dt)
        {
            // SHEAR: a stretched bubble forced against rock (or into dry ground) tears in two early (Szostak)
            if (c.shear > 0.6f && c.area >= c.birthArea * 1.5f && c.pinch < 0.5f) c.pinch = Mathf.Max(c.pinch, 0.55f);
            bool ready = (c.area >= c.birthArea * 2f && c.area > 1.4f) || c.pinch >= 0.55f;
            if (c.life != null) ready = c.area >= c.birthArea * 1.8f && c.life.copyStore >= FissionCopyCost(c);   // living: the genome copy must be paid for
            if (!ready) { c.pinch = Mathf.Max(0f, c.pinch - dt); return; }
            c.pinch += dt / 1.8f;
            if (c.pinch < 1f) return;
            if (c.life != null) c.life.copyStore = Mathf.Max(0f, c.life.copyStore - FissionCopyCost(c));
            FinishDivision(c);
        }

        Protocell FinishDivision(Protocell c)
        {
            bool living = c.life != null;
            bool clumsy = living && !c.genome.ring;
            var d = c.Divide(ref r, c.player ? prog : DummyProgress);
            if (living)
            {
                if (clumsy)
                {
                    // no ring: the membrane just tears in two — unequal halves, and the smaller may miss genes
                    float total = c.area + d.area, u = r.Range(0.35f, 0.65f);
                    c.area = c.birthArea = total * u; d.area = d.birthArea = total * (1f - u);
                    var small = u < 0.5f ? c : d;
                    for (int i = small.genome.genes.Count - 1; i >= 1; i--) if (r.Value < 0.3f) small.genome.RemoveGene(small.genome.genes[i]);
                    c.genome.passiveSplits++; d.genome.passiveSplits = c.genome.passiveSplits;
                    if (c.genome.ribosome && r.Value < 0.12f + 0.1f * c.genome.passiveSplits)
                    {
                        c.genome.ring = d.genome.ring = true;
                        // with a ring, the cell can hold a shape: the lineage takes its own form
                        int arche = r.RangeInt(0, 4);
                        c.genome.elong = arche == 1 || arche == 2 ? r.Range(0.4f, 0.9f) : r.Range(0f, 0.15f);
                        c.genome.bend = arche == 2 ? r.Range(0.25f, 0.5f) : 0f;
                        c.genome.lobes = arche == 3 ? r.Range(0.4f, 0.8f) : r.Range(0f, 0.15f);
                        d.genome.elong = c.genome.elong; d.genome.bend = c.genome.bend; d.genome.lobes = c.genome.lobes;
                        if (c.player) { world.Banner("THE RING", ""); music?.Event(CellMusic.Cue.Gene); }
                    }
                }
                // RNA → DNA: a reverse transcriptase (from a virus — or, rarely, its own) rewrites the genome over a few divisions
                if (!c.genome.rt && c.generation >= 8 && r.Value < 0.08f) c.genome.rt = true;
                if (c.genome.rt && !c.genome.Dna) { c.genome.dnaStage++; d.genome.rt = true; d.genome.dnaStage = c.genome.dnaStage; }
                if (c.genome.ring)
                {
                    // shape drifts a little every generation: lineages diverge into rods, curves, cocci, lobed forms
                    d.genome.elong = Mathf.Clamp01(d.genome.elong + r.Range(-0.06f, 0.06f));
                    d.genome.bend = Mathf.Clamp(d.genome.bend + r.Range(-0.05f, 0.05f), 0f, 0.6f);
                    d.genome.lobes = Mathf.Clamp01(d.genome.lobes + r.Range(-0.05f, 0.05f));
                }
                if (c.player && c.genome.Dna) world.Banner("DNA", "");
                if (c.player && !lineageSecured) { lineageSecured = true; world.Banner("LINEAGE", ""); }
            }
            if (living) { c.genome.s1Divisions++; d.genome.s1Divisions = c.genome.s1Divisions; }
            if (living) { c.genome.leak = Mathf.Max(0.15f, c.genome.leak - 0.04f); d.genome.leak = c.genome.leak; }   // newer membranes are tighter
            if (c.player) music?.Event(c.gainedGene >= 0 ? CellMusic.Cue.Gene : CellMusic.Cue.Divide);
            if (c.player && c.gainedGene >= 0) world.Banner("NEW GENE", "");
            d.replicatorGenerations = d.ReplicatorCount > 0 ? c.replicatorGenerations + 1 : 0;
            c.replicatorGenerations = c.ReplicatorCount > 0 ? c.replicatorGenerations + 1 : 0;
            world.species.Born(d, c.SpeciesId);
            cells.Add(d);
            if (c.player || d.player) consecutiveDeaths = 0;
            // a replicator carried through 2 divisions: this lineage is alive (S1) — individually, not all at once
            if (c.life == null && c.replicatorGenerations >= 2 && (c.player || momentReached)) Graduate(c);
            if (d.life == null && d.replicatorGenerations >= 2 && (d.player || momentReached)) Graduate(d);
            if (c.SpeciesId == playerSpecies || d.SpeciesId == playerSpecies)
            {
                prog.Reach(Milestone.FirstDivision);
                if (d.ReplicatorCount > 0 || c.ReplicatorCount > 0) prog.Reach(Milestone.InheritedReplicator);
            }
            return d;
        }

        void Kill(Protocell c, string cause)
        {
            c.dead = true; c.causeOfDeath = cause; c.burstFlash = 1f;
            world.species.Died(c);
            // spill the contents back into the soup (chains fall apart into bases)
            for (int k = 0; k < MoteChem.KindCount; k++)
                for (int n = 0; n < Mathf.Min(c.free[k], 25); n++)
                    field.motes.Add(new Mote { pos = c.pos + Random.insideUnitCircle * c.Radius, kind = (MoteKind)k, alive = true, rot = r.Range(0f, 6.28f), spin = r.Range(-1f, 1f) });
            if (c.life != null)
                foreach (var gp in c.genome.genes)
                    if (r.Value < 0.7f) fragments.Add(new GeneFragment { pos = c.pos + Random.insideUnitCircle * c.Radius * 0.6f, p = gp, life = 45f, seed = r.Range(0f, 100f) });
            int lipids = Mathf.Min(Mathf.RoundToInt(c.area * 2f), 20);
            for (int n = 0; n < lipids; n++)
                field.motes.Add(new Mote { pos = c.pos + Random.insideUnitCircle * c.Radius, kind = MoteKind.Lipid, alive = true, rot = r.Range(0f, 6.28f), spin = r.Range(-1f, 1f) });
            if (c == player)
            {
                consecutiveDeaths++;
                lastWasAlive = c.life != null; fission = 0; dragGene = -1; editing = false; geneFocus = -1;
                if (c.life != null) lastGenome = c.genome.Clone();
                music?.Event(CellMusic.Cue.Death);
                lastDeathPos = c.pos;
                lastStrand = StrandOf(c);
                BeginPicker();
            }
        }

        void PlayerMilestones()
        {
            if (player.area >= firstArea * 1.6f) prog.Reach(Milestone.MembraneGrowth);
            if (player.lowTideSurvival > 20f && pool.TidePhase > 0f) prog.Reach(Milestone.SurvivedDryPhase);
            // (StableReplicator — the S0 graduation — is now reached by the whole pool at the founder collapse)
            if (player.genome.copyFidelity >= 0.93f) prog.Reach(Milestone.CopyFidelity);
        }

        // ── death → continue as a living relative (CellStage_Decisions §3) ──
        void BeginPicker()
        {
            picking = true; pickTimer = 0f;
            options = world.species.RespawnOptions(playerSpecies);
        }

        void PickerTick(float dt)
        {
            pickTimer += dt;
            options.RemoveAll(o => !o.IsAlive);
            if (options.Count == 0)
            {
                if (consecutiveDeaths >= 2 && Input.GetKeyDown(KeyCode.V)) { BecomeViroid(lastDeathPos, lastStrand); return; }
                // no relatives left: the lineage is gone. After a pause, a new bubble forms from the soup.
                if (pickTimer > 3f)
                {
                    var g = new Genome { hue = r.Range(0f, 1f), saturation = 0.6f };
                    var c = new Protocell(g, pool.RandomWetPoint(ref r, 0.06f), 1.6f) { wanderSeed = r.Range(0f, 100f) };
                    if (lastWasAlive && lineageSecured && lastGenome != null)
                    {
                        // the lineage endures: a living descendant somewhere in the pool carries on
                        var lg = lastGenome.Clone();
                        var lc = new Protocell(lg, pool.RandomWetPoint(ref r, 0.06f), 6f) { wanderSeed = r.Range(0f, 100f) };
                        lc.life = new S1Life(lg) { morph = 1f };
                        lc.life.energy.raw = 1.5f;
                        lastWasAlive = false; fission = 0;
                        world.species.Born(lc, playerSpecies);
                        cells.Add(lc); Possess(lc);
                        return;
                    }
                    if (lastWasAlive && revealedRecipe != null)
                    {
                        // you were alive once: the chemistry is remembered. Reborn carrying the recipe, one division from life.
                        var ch = new Chain(); ch.seq.AddRange(revealedRecipe); Protocell.Classify(ch, motifs); ch.glow = 1f;
                        c.chains.Add(ch); UpdateGenomeReplicator(c);
                        c.replicatorGenerations = 1;
                        world.Banner("BACK TO THE SOUP", "your recipe survived — divide once to live again");
                    }
                    lastWasAlive = false;
                    fission = 0;
                    playerSpecies = world.species.Found(g, "player-lineage").id;
                    world.species.Born(c, playerSpecies);
                    cells.Add(c);
                    Possess(c);
                }
                return;
            }
            // after repeated failures, the naked-strand path is always open (never a game over)
            if (consecutiveDeaths >= 2 && Input.GetKeyDown(KeyCode.V)) { BecomeViroid(lastDeathPos, lastStrand); return; }
            int chosen = -1;
            for (int k = 0; k < Mathf.Min(options.Count, 8); k++) if (Input.GetKeyDown(KeyCode.Alpha1 + k)) chosen = k;
            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
                float bestD = float.MaxValue;
                for (int k = 0; k < options.Count; k++)
                {
                    var o = (Protocell)options[k];
                    float d = (o.pos - mw).magnitude - o.Radius;
                    if (d < bestD && d < 1.5f) { bestD = d; chosen = k; }
                }
            }
            if (chosen >= 0) Possess((Protocell)options[chosen]);
        }

        void Possess(Protocell c)
        {
            if (player != null) player.player = false;
            player = c; c.player = true; picking = false;
            if (playerViroid != null) { playerViroid.player = false; playerViroid = null; }
            seabedAssembly.SetOwner(c);
        }

        // ── the viroid path ──────────────────────────────────────────────────────────────────
        public const float FlowCoupling = 0.45f;   // how strongly the environment's currents move things here
        Vector2 Flow(Vector2 p)
        {
            var f = FlowFieldManager.Instance;
            if (f == null) return Vector2.zero;
            // only near the view: the field caches a grid around the camera; far lookups fall back to an expensive
            // analytic evaluation (and nobody sees those molecules drift anyway)
            Vector2 cp = world.cam.transform.position;
            if ((p - cp).sqrMagnitude > 45f * 45f) return Vector2.zero;
            var bm = pool.biome;
            Vector2 v = f.SampleFlowAtPosition(p) * bm.flowMul;
            float t = pool.time;
            // the tide: flood / ebb along the pool's axis, fastest when the level changes fastest, strongest in the shallows
            float tideRate = Mathf.Cos(t * 2f * Mathf.PI / pool.tidePeriod + 1.2f);
            float shallow = Mathf.Clamp01(1f - pool.Depth(p) / 0.12f) * 0.7f + 0.3f;
            v += bm.tideAxis * (tideRate * bm.tidalCurrent * 1.4f * shallow);
            // eddies: the curl of a slow noise field (divergence-free, so it swirls rather than piles things up)
            if (bm.turbulence > 0f)
            {
                const float s = 0.07f, e = 0.5f;
                float n(Vector2 q) => Mathf.PerlinNoise(q.x * s + t * 0.03f + 31.7f, q.y * s - t * 0.02f + 11.3f);
                v += new Vector2(n(p + new Vector2(0, e)) - n(p - new Vector2(0, e)), -(n(p + new Vector2(e, 0)) - n(p - new Vector2(e, 0)))) / (2f * e * s) * (bm.turbulence * 0.35f);
            }
            // vent convection: water heated at the vent rises and spins outward in a slow vortex
            if (bm.convection > 0f)
            {
                Vector2 d = p - biome.ventCentre; float dist = d.magnitude + 0.5f;
                float fall = Mathf.Exp(-dist / (biome.ventRadius * 0.8f + 2f));
                v += (new Vector2(-d.y, d.x) / dist * 0.9f + d / dist * 0.5f) * (bm.convection * fall * 1.5f) * (1f + 0.4f * Mathf.Sin(t * 0.5f));
            }
            return features.Deflect(p, v);
        }

        List<int> StrandOf(Protocell c)
        {
            if (c.genome.replicator.Count > 0) return new List<int>(c.genome.replicator);
            Chain best = null;
            foreach (var ch in c.chains) if (best == null || ch.seq.Count > best.seq.Count) best = ch;
            if (best != null && best.seq.Count >= 2) return new List<int>(best.seq);
            var s = new List<int>(); for (int i = 0; i < 3; i++) s.Add(r.RangeInt(0, 4));   // a scrap of RNA
            return s;
        }

        /// Hold X: the membrane dissolves; you go on as the strand you carried.
        void Shed()
        {
            if (player == null || player.dead) return;
            var strand = StrandOf(player);
            var at = player.pos;
            var me = player;
            seabedAssembly.SetOwner(null);
            me.player = false;
            player = null;               // first, so Kill() doesn't treat this as a death (no picker)
            Kill(me, "shed");            // spills the contents
            BecomeViroid(at, strand);
        }

        void BecomeViroid(Vector2 at, List<int> strand)
        {
            if (strand == null || strand.Count == 0) { strand = new List<int>(); for (int i = 0; i < 3; i++) strand.Add(r.RangeInt(0, 4)); }
            picking = false;
            if (player != null) { player.player = false; player = null; }
            playerViroid = new S0Viroid(at, strand, true);
            playerViroid.body = new VirusBody();
            vTier = Mathf.Max(0, vTier - 1); vProgress = vTier;   // dying costs you a tier
            playerViroid.body = VirusBody.FromTier(vTier, patKinds, StrandSeed(strand));
            playerViroid.body.externalCohesion = Mathf.Clamp01(0.2f + vTier * 0.11f);
            playerViroid.entryCheck = CanInvade;
            viroids.Add(playerViroid);
        }

        // ── THE VIRUS, played ──────────────────────────────────────────────────────────────────
        // Outside: pick up compounds to build your shell (cohesion = how sound it is), ride the currents, and force your
        // way into cells. A protocell lets you in easily; a LIVING cell only if your shell is strong enough for it.
        // Inside a living cell the cell becomes your world: steal its parts (bases, iron–sulfur, amino acids — even its
        // genes) to grow your shell, while a hijack meter fills; the cell fights back, wearing on your RNA. Fill the
        // meter and the cell is yours: it bursts with your copies, you gain, and you move on. Take enough of one
        // species and you partly — then fully — claim it.
        float hijack, pickT; Vector2 vLocal, lastVirusVel;
        readonly List<int> shedBuf = new();
        struct Pick { public Vector2 local; public int kind; public float seed; }   // kind: 0–3 base · 7 FeS · 8 amino · 9 gene
        readonly List<Pick> picks = new();
        readonly Dictionary<int, int> speciesHijacks = new(), speciesClaim = new();
        float claimTimer;

        float Defense(Protocell h)
        {
            if (h.life == null) return 0.3f;
            float d = 0.6f + h.area * 0.02f + (h.genome.ribosome ? 0.6f : 0f) + (h.genome.Dna ? 1f : 0f);
            foreach (var b in h.genome.bays) if (b != null) { Pathways.Best(b, h.genome.ribosome, out float e); d += e * 0.4f; }
            return d;
        }
        bool CanInvade(Protocell c)
        {
            if (c.life == null) return true;
            var b = playerViroid?.body; if (b == null) return false;
            bool ok = b.Strength * 2.2f + b.Spikiness * 0.4f + Mathf.Min(b.Fibers, 4) * 0.12f >= Defense(c) * 0.55f;   // a sound mesh, spikes, and fibres that grab                  // living cells need a sound shell
            if (!ok)
            {
                // bounced off: the shell takes the hit
                var dir = (playerViroid.pos - c.pos).normalized;
                playerViroid.pos = c.pos + dir * (c.Radius + b.Radius + 0.05f);
                playerViroid.vel = dir * 2f;
                b.Impact(-dir, 4f);
            }
            return ok;
        }

        void VirusPlay(float dt)
        {
            var v = playerViroid; var b = v.body; if (b == null) return;
            if (!v.insideHost)
            {
                float sc = Input.mouseScrollDelta.y;
                if (virusEditing) { VirusEditor(); return; }
                if (Mathf.Abs(sc) > 0.01f) zoom = Mathf.Clamp(zoom * (1f - sc * 0.1f), 0.5f, 3f);
                // (outside a cell a virus particle is inert: it takes nothing in, and slowly decays until it finds a host)
                // physics: the shell lags as you speed up and turn; parts too far gone break off
                var accel = (v.vel - lastVirusVel) / Mathf.Max(dt, 1e-3f); lastVirusVel = v.vel;
                shedBuf.Clear(); b.Tick(dt, accel, shedBuf);
                foreach (var k in shedBuf)
                    if (k <= 6) field.motes.Add(new Mote { pos = v.pos + r.InsideUnitCircle() * b.Radius, kind = (MoteKind)k, alive = true, rot = r.Range(0f, 6.28f), spin = r.Range(-1f, 1f) });
                if (shedBuf.Count > 0) music?.Absorb(false);
                // rocks and vents are hard: hitting them knocks the shell
                var pushed = features.PushOut(vents.PushOut(v.pos, b.Radius), b.Radius);
                if ((pushed - v.pos).sqrMagnitude > 1e-6f) { b.Impact((v.pos - pushed).normalized, 3f); v.pos = pushed; }
                return;
            }
            var h = v.host;
            if (h == null || h.dead || h.life == null) return;                  // (protocell hosts: the S0 copying game)
            if (!v.interior)
            {
                v.interior = true; hijack = 0f; vLocal = Vector2.zero; BuildPicks(h); world.Banner("INSIDE", "");
                eaten.Clear(); AnalyzePattern();                                         // a new generation: assembly starts over
                v.rnaIntegrity = 1f;                                                     // sheltered inside the cell
            }
            // swim inside the cell
            Vector2 aim = ReadInput(h.pos + vLocal * h.Radius, 0.1f);
            vLocalVel = aim * 0.9f;
            vLocal += aim * dt * 0.9f;
            if (vLocal.magnitude > 0.75f) vLocal = vLocal.normalized * 0.75f;
            v.pos = h.pos + vLocal * h.Radius;
            shedBuf.Clear(); b.Tick(dt, -aim * 3f, shedBuf);
            // steal what you touch
            for (int i = picks.Count - 1; i >= 0; i--)
            {
                var p = picks[i];
                p.local += new Vector2(Mathf.Sin(pool.time * 0.4f + p.seed), Mathf.Cos(pool.time * 0.37f + p.seed * 1.3f)) * dt * 0.05f;
                picks[i] = p;
                if ((p.local - vLocal).magnitude > 0.1f + b.Radius / h.Radius) continue;
                picks.RemoveAt(i);
                float worth = (0.5f + b.Strength) / Defense(h) * (b.HasTail ? 1.35f : 1f);
                if (p.kind <= 3) { if (h.free[p.kind] > 0) h.free[p.kind]--; VirusEat(p.kind); hijack += 0.06f * worth; }
                else if (p.kind == 7) { h.life.fes = Mathf.Max(0f, h.life.fes - 1f); VirusEat(7); hijack += 0.08f * worth; }
                else if (p.kind == 8) { h.life.amino = Mathf.Max(0f, h.life.amino - 1f); VirusEat(8); hijack += 0.08f * worth; }
                else
                {
                    // a gene: tearing it out cripples the cell and speeds the takeover
                    for (int k = 0; k < h.genome.bays.Count; k++) if (h.genome.bays[k] != null) { h.genome.bays[k] = null; h.genome.Express(); break; }
                    virusComplexity += 3f;
                    hijack += 0.22f * worth;
                }
                Gather(0.06f); music?.Snap(true);
            }
            float def = Defense(h);
            float inject = b.HasTail ? 1.35f : 1f;                              // a tail injects: faster takeovers
            hijack += dt * 0.006f * (0.3f + b.Strength) / def * inject;          // sitting still barely helps
            if (Unravels(v, dt, def)) return;                                        // the cell fights back: incoherent invaders come apart
            h.viroidLoad = Mathf.Max(h.viroidLoad, hijack * 0.8f);
            if (Input.GetKeyDown(KeyCode.Space)) { v.Exit(h.pos + vLocal.normalized * h.Radius * 1.4f, vLocal.normalized * 3f); return; }   // burst back out
            if (hijack >= 1f) Takeover(h);
        }

        Color PartColor(int k)
        {
            if (k <= 6) { var c0 = MoteChem.Color((MoteKind)k, world.ctx.solvent); c0.a = 1f; return c0; }
            return k == 7 ? new Color(0.38f, 0.33f, 0.28f) : new Color(0.95f, 0.75f, 0.8f);
        }

        /// The virus shell: parts as shaded beads, linked around a faceted ring, the RNA coiled inside. Sound shells
        /// look tight and bright; weak ones loose and dull; a hit flashes the shell.
        /// The virus: ONE organic body (a lumpy virion covered in protein knobs). Its size comes from how many parts
        /// it has gathered, its colour from what they are, how round and calm it looks from its cohesion; it stretches
        /// along its motion and flashes when hit. Inside a cell it's drawn larger, with a soft glow, so you can see
        /// yourself in there.
        void DrawVirusBody(S0Viroid v, Vector2 at, float t, QuadBatch q, float scale = 1f)
        {
            var b = v.body; int n = b.parts.Count;
            Color mix = new Color(0.82f, 0.78f, 0.9f);                           // protein: pale, faintly violet
            if (n > 0)
            {
                Color acc = Color.black; foreach (var k in b.parts) acc += PartColor(k);
                mix = Color.Lerp(mix, acc / n, 0.22f);                          // a hint of what it's built from
            }
            mix = Color.Lerp(mix, Color.white, b.hitFlash * 0.5f); mix.a = 1f;
            var vel = v.interior ? (Vector2)vLocalVel : v.vel;
            float speed = vel.magnitude, stretch = 1f + Mathf.Clamp(speed * 0.03f, 0f, 0.12f) + b.hitFlash * 0.1f;   // capsids are rigid: barely squash
            float ang = speed > 0.05f ? Mathf.Atan2(vel.y, vel.x) : 0f;
            float size = v.interior && v.host != null ? v.host.Radius * (0.12f + 0.08f * Mathf.Clamp01(n / (float)VirusBody.MaxParts)) : b.Radius * 1.9f * scale;   // inside: a small thing in a big cell
            q.Add(at + new Vector2(0.05f, -0.07f) * scale, size * 1.05f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0f, 0f, 0f, 0.3f));
            if (v.interior) q.Add(at, size * 1.8f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0.85f, 0.6f, 1f, 0.22f + 0.08f * Mathf.Sin(t * 3f)));   // you, in here
            q.Add(at, size * 1.25f, 0f, 1f, 18, new Vector4(stretch, b.Cohesion, StrandSeed(v.strand), Mathf.Clamp01(n / (float)VirusBody.MaxParts)), Vector4.zero, mix);
        }
        Vector2 vLocalVel;

        // ── THE VIRUS EDITOR: zoom all the way in on your virus. Drag a node to reshape the mesh (out = spikier, in =
        // more compact); drop a compound you carry onto a node to replace it, or onto the body to add a node (as many
        // as your complexity allows); pull a node far away to take it off. Scroll out / Esc to leave.
        Vector2 VStockPos(int k, int q, float t)
        {
            var b = playerViroid.body;
            float a = k * 0.698f + q * 0.31f + t * 0.05f;
            return playerViroid.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * b.Radius * (2.3f + 0.25f * Mathf.Sin(q * 1.7f + t * 0.3f));
        }
        void VirusEditor()
        {
            var v = playerViroid; var b = v.body;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.mouseScrollDelta.y < -0.01f) { virusEditing = false; zoom = 0.6f; vDragNode = vDragStock = -1; return; }
            Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
            var d = mw - v.pos;
            if (Input.GetMouseButtonDown(0))
            {
                vDragNode = -1; vDragStock = -1;
                float best = b.Radius * 0.35f;
                for (int i = 0; i < b.parts.Count; i++) { float dd = (mw - (v.pos + b.pos[i])).magnitude; if (dd < best) { best = dd; vDragNode = i; } }
                if (vDragNode < 0)
                    for (int k = 0; k < 9; k++) for (int q = 0; q < Mathf.Min(virusStock[k], 8); q++)
                    { float dd = (mw - VStockPos(k, q, pool.time)).magnitude; if (dd < best) { best = dd; vDragStock = k; } }
            }
            if (Input.GetMouseButton(0)) { if (vDragNode >= 0 && vDragNode < b.parts.Count) b.pos[vDragNode] = d; return; }   // the node follows your hand
            if (vDragNode >= 0 && vDragNode < b.parts.Count)
            {
                if (d.magnitude > b.Radius * 2.6f && b.parts.Count > 3) { virusStock[Mathf.Clamp(b.parts[vDragNode], 0, 8)]++; b.RemoveAt(vDragNode); music?.Absorb(false); }
                else { b.Reshape(vDragNode, Mathf.Atan2(d.y, d.x), d.magnitude / b.Radius); music?.Snap(true); }
            }
            else if (vDragStock >= 0 && virusStock[vDragStock] > 0)
            {
                int near = -1; float best = b.Radius * 0.35f;
                for (int i = 0; i < b.parts.Count; i++) { float dd = (mw - (v.pos + b.pos[i])).magnitude; if (dd < best) { best = dd; near = i; } }
                if (near >= 0) { virusStock[Mathf.Clamp(b.parts[near], 0, 8)]++; b.parts[near] = vDragStock; b.Recompute(); virusStock[vDragStock]--; music?.Snap(true); }
                else if (d.magnitude < b.Radius * 2.2f && b.parts.Count < NodeBudget) { b.Add(vDragStock, Mathf.Atan2(d.y, d.x), Mathf.Clamp(d.magnitude / b.Radius, 0.5f, 2f)); virusStock[vDragStock]--; music?.Snap(true); }
                else music?.Absorb(false);
            }
            vDragNode = vDragStock = -1;
        }
        void DrawVirusEditor(S0Viroid v, float t)
        {
            var b = v.body;
            for (int k = 0; k < 9; k++)
                for (int q = 0; q < Mathf.Min(virusStock[k], 8) - (vDragStock == k ? 1 : 0); q++)
                {
                    var col = PartColor(k); col.a = 0.95f;
                    editorQ.Add(VStockPos(k, q, t), b.Radius * 0.12f, 0f, 1f, 10, new Vector4(1f, 0.2f, Frac(q * 0.37f + k), 0), Vector4.zero, col);
                }
            Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
            if (vDragStock >= 0) { var col = PartColor(vDragStock); col.a = 1f; editorQ.Add(mw, b.Radius * 0.14f, 0f, 1f, 10, new Vector4(1f, 0.4f, 0.5f, 0), Vector4.zero, col); }
            if (vDragNode >= 0 && (mw - v.pos).magnitude > b.Radius * 2.6f) editorQ.Add(mw, b.Radius * 0.3f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 0.4f, 0.3f, 0.3f));   // let go here → it comes off
            // nodes: small beads at the mesh's corners so you can grab them
            for (int i = 0; i < b.parts.Count; i++) editorQ.Add(v.pos + b.pos[i], b.Radius * 0.07f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 1f, 1f, 0.5f));
        }
        /// A stable number for a virus lineage (its strand): decides its type and colour, so copies share a family look.
        static float StrandSeed(List<int> s)
        {
            uint h = 2166136261u; foreach (var b in s) { h ^= (uint)b + 1u; h *= 16777619u; }
            return (h % 10007u) / 10007f;
        }
        static Color VirusColor(float seed)
        {
            Color[] fam = { new(0.62f, 0.45f, 0.82f), new(0.35f, 0.7f, 0.68f), new(0.85f, 0.62f, 0.38f), new(0.75f, 0.42f, 0.55f), new(0.55f, 0.62f, 0.85f) };
            return fam[(int)(seed * 97f) % fam.Length];
        }

        void DrawPicks(Protocell h, float t)
        {
            foreach (var p in picks)
            {
                var at = h.pos + p.local * h.Radius;
                if (p.kind == 9)
                {
                    var kc = new Color(0.9f, 0.85f, 0.75f, 0.95f);
                    editorQ.Add(at, h.Radius * 0.12f, t * 0.2f + p.seed, 1f, 15, new Vector4(1.4f, 0.3f, Frac(p.seed), 0.8f), Vector4.zero, kc);   // one of its genes
                }
                else editorQ.Add(at, h.Radius * (p.kind >= 7 ? 0.045f : 0.04f), p.seed + t * 0.3f, 1f, p.kind == 7 ? 13 : p.kind == 8 ? 12 : 10, new Vector4(1f, 0.2f, Frac(p.seed), 0), Vector4.zero, PartColor(p.kind));
            }
        }

        void BuildPicks(Protocell h)
        {
            picks.Clear();
            void Put(int kind, int n) { for (int i = 0; i < n; i++) { var p = r.InsideUnitCircle() * 0.7f; picks.Add(new Pick { local = p, kind = kind, seed = r.Range(0f, 100f) }); } }
            for (int b = 0; b < 4; b++) Put(b, Mathf.Min(h.free[b], 4));
            Put(7, Mathf.Min(Mathf.CeilToInt(h.life.fes), 4));
            Put(8, Mathf.Min(Mathf.CeilToInt(h.life.amino), 4));
            int genes = 0; foreach (var g in h.genome.bays) if (g != null) genes++;
            Put(9, genes);
            if (picks.Count < 4) Put(r.RangeInt(0, 4), 4 - picks.Count);
        }

        void Takeover(Protocell h)
        {
            var v = playerViroid; var b = v.body;
            int species = h.SpeciesId;
            int copies = 2 + Mathf.RoundToInt(b.Strength * 4f);
            for (int i = 0; i < copies; i++) viroids.Add(new S0Viroid(h.pos + r.InsideUnitCircle() * h.Radius, new List<int>(v.strand), false) { vel = r.InsideUnitCircle() * 2.5f });
            var at = h.pos;
            v.Exit(at, r.InsideUnitCircle() * 2f);
            Kill(h, "hijacked");
            vProgress = Mathf.Min(vProgress + 0.5f, MaxTier + 0.999f);   // a takeover feeds you
            if (vTier < MaxTier && vProgress >= vTier + 1) { vTier++; RebuildVirusForm(); }
            v.rnaIntegrity = 1f;
            if (b != null) b.externalCohesion = Mathf.Clamp01(0.2f + vTier * 0.11f + Coherence * 0.25f);   // your copies are as sound as this assembly was
            Gather(2.5f);
            world.Banner("HIJACKED", "");
            music?.Event(CellMusic.Cue.Gene);
            // the species: how much of it is yours now?
            speciesHijacks.TryGetValue(species, out int got); got++; speciesHijacks[species] = got;
            int alive = 0; foreach (var c in cells) if (!c.dead && c.life != null && c.SpeciesId == species) alive++;
            float share = got / (float)(got + alive);
            speciesClaim.TryGetValue(species, out int lvl);
            int newLvl = got >= 6 && share >= 0.75f ? 2 : got >= 3 && share >= 0.35f ? 1 : 0;
            if (newLvl > lvl) { speciesClaim[species] = newLvl; world.Banner(newLvl == 2 ? "SPECIES CLAIMED" : "SPECIES INFECTED", ""); }
        }

        /// Claimed species keep working for you: their cells now and then shed your copies; a fully claimed species
        /// also feeds your complexity.
        void TickClaims(float dt)
        {
            if (speciesClaim.Count == 0 || playerViroid == null) return;
            claimTimer -= dt;
            if (claimTimer > 0f) return;
            claimTimer = 6f;
            foreach (var c in cells)
            {
                if (c.dead || c.life == null || !speciesClaim.TryGetValue(c.SpeciesId, out int lvl) || lvl == 0) continue;
                c.viroidLoad = Mathf.Max(c.viroidLoad, 0.3f * lvl);
                if (r.Value < 0.15f * lvl && viroids.Count < 60) viroids.Add(new S0Viroid(c.pos + r.InsideUnitCircle() * c.Radius, new List<int>(playerViroid.strand), false) { vel = r.InsideUnitCircle() * 2f });
                if (lvl == 2) Gather(0.05f);
            }
        }

        void TickViroids(float dt, bool controlling)
        {
            if (player != null && !player.dead && player.life != null && !player.genome.Dna)
            {
                virusTimer -= dt;
                int wild = 0; foreach (var v in viroids) if (v.alive && !v.player) wild++;
                if (virusTimer <= 0f && wild < 4)
                {
                    virusTimer = r.Range(35f, 70f);
                    var at = player.pos + r.InsideUnitCircle().normalized * r.Range(14f, 24f);
                    if (pool.Depth(at) > 0.02f)
                    {
                        var strand = new List<int>(); for (int i = 0; i < 4; i++) strand.Add(r.RangeInt(0, 4));
                        viroids.Add(new S0Viroid(at, strand, false));
                    }
                }
            }
            foreach (var v in viroids)
            {
                if (!v.alive || v.player || !v.insideHost || v.host == null || v.host.life == null) continue;
                v.digestTimer += dt;
                v.host.viroidLoad = Mathf.Max(v.host.viroidLoad, 0.4f);
                v.host.life.membraneStress = Mathf.Min(1f, v.host.life.membraneStress + dt * (v.host.player ? 0.008f : 0.02f));   // infection hurts
                if (v.digestTimer > 8f)
                {
                    // the cell fought it off; its enzyme for writing RNA into DNA sometimes stays behind
                    var h = v.host;
                    h.viroidLoad = 0f; v.alive = false;
                    if (!h.genome.rt && r.Value < 0.5f)
                    {
                        h.genome.rt = true; h.absorbFlash = 1f;
                        if (h.player) { world.Banner("A STRANGE GENE", ""); music?.Event(CellMusic.Cue.Gene); }
                    }
                }
            }
            // the player's share of the living pool currently infected (drives hijack spurts)
            int alive = 0, infected = 0;
            foreach (var c in cells) if (!c.dead) alive++;
            foreach (var v in viroids) if (v.alive && v.insideHost && !v.dormant) infected++;
            float share = alive > 0 ? infected / (float)alive : 0f;

            if (playerViroid != null && controlling) VirusPlay(dt);
            if (playerViroid != null && controlling && !playerViroid.interior)
            {
                playerViroid.ApplyPlayerInput(virusEditing ? Vector2.zero : ReadInput(playerViroid.pos, 0.2f));
                if (Input.GetKeyDown(KeyCode.F) && playerViroid.insideHost) { if (playerViroid.dormant) playerViroid.WakeUp(); else playerViroid.GoDormant(); }
                if (Input.GetKeyDown(KeyCode.Space) && playerViroid.insideHost && !playerViroid.interior && !playerViroid.dormant) LyseViroid(playerViroid);
            }
            TickClaims(dt);
            for (int i = viroids.Count - 1; i >= 0; i--)
            {
                var v = viroids[i];
                v.Tick(dt, pool.time, pool, cells, world.ctx.ReactionRate, share, Flow, ref r);
                if (!v.player && v.ReadyToLyse) LyseViroid(v);
                if (!v.alive)
                {
                    viroids.RemoveAt(i);
                    if (v == playerViroid) PlayerViroidDied();
                }
            }
        }

        void LyseViroid(S0Viroid v)
        {
            var fidelity = v.host != null ? v.host.genome.copyFidelity : 0.85f;
            var copies = v.Lyse(ref r, fidelity, out var host);
            if (host != null && !host.dead) Kill(host, "lysed");
            viroids.AddRange(copies);
        }

        /// The player's strand fell apart. If any of its copies are still out there, carry on as the nearest one;
        /// otherwise the soup forms a new bubble (or a living relative from before can be chosen).
        void PlayerViroidDied()
        {
            var at = playerViroid.pos;
            lastViroidStrand = new List<int>(playerViroid.strand);
            playerViroid = null;
            S0Viroid next = null; float bestD = float.MaxValue;
            foreach (var v in viroids) { float d = (v.pos - at).sqrMagnitude; if (v.alive && d < bestD) { bestD = d; next = v; } }
            if (next != null)
            {
                next.player = true; playerViroid = next;
                next.body = VirusBody.FromTier(vTier, patKinds, StrandSeed(next.strand)); next.body.externalCohesion = Mathf.Clamp01(0.2f + vTier * 0.11f + patQuality * 0.2f);
                next.entryCheck = CanInvade;
                return;
            }
            consecutiveDeaths++;
            lastDeathPos = at;
            BecomeViroid(pool.RandomWetPoint(ref r, 0.03f), lastViroidStrand);   // you are a virus: another one drifts in
        }

        // ── the founder collapse ─────────────────────────────────────────────────────────────
        void BeginCollapse()
        {
            founders = founderCollapse.Collapse(cells, ref r, out founderCarriers);
            if (founders.Count == 0) return;
            collapsing = true; collapseTimer = 0f;
            if (seabedAssembly.Active) seabedAssembly.Exit();
            picking = false;
            Debug.Log($"[CellStage] founder collapse: {founderCollapse.ReplicatingCount} heritable replicators → {founders.Count} founders");
        }

        void CollapseTick(float dt)
        {
            collapseTimer += dt;
            int chosen = -1;
            Genome seed = player != null && !player.dead ? player.genome : null;

            if (playerViroid != null)
            {
                // a dormant viroid inside a living host integrates into it and descends from that lineage
                if (playerViroid.insideHost && playerViroid.dormant) seed = playerViroid.Integrate() ?? seed;
                chosen = seed != null ? FounderCollapse.AssignPlayerToFounder(seed, founders) : 0;
            }
            else if (collapseTimer > 0.8f)
            {
                // the three founders glow; click one (or 1–3) to join it
                for (int k = 0; k < founders.Count; k++) if (Input.GetKeyDown(KeyCode.Alpha1 + k)) chosen = k;
                if (Input.GetMouseButtonDown(0))
                {
                    Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
                    float bestD = 1.5f;
                    for (int k = 0; k < founderCarriers.Count; k++)
                    {
                        var c = founderCarriers[k]; if (c == null || c.dead) continue;
                        float d = (c.pos - mw).magnitude - c.Radius;
                        if (d < bestD) { bestD = d; chosen = k; }
                    }
                }
                if (collapseTimer > 30f) chosen = seed != null ? FounderCollapse.AssignPlayerToFounder(seed, founders) : 0;   // no choice → the nearest
            }
            if (chosen < 0) return;

            prog.founderGenomes = founders;
            prog.playerFounder = chosen;
            prog.playerSeedGenome = seed;
            collapsing = false;
            prog.Reach(Milestone.StableReplicator);      // → S1 (CellStageWorld swaps the mode)
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────
        void Render(float dt)
        {
            float day = pool.Daylight;
            soupMat.SetFloat("_Tide", pool.Tide); featMat.SetFloat("_Tide", pool.Tide); featMat.SetFloat("_Day", day); soupMat.SetFloat("_WaveEnergy", pool.biome.waveEnergy * (0.75f + 0.35f * Mathf.Abs(Mathf.Cos(pool.time * 2f * Mathf.PI / pool.tidePeriod + 1.2f)) * Mathf.Min(1f, pool.biome.tidalCurrent))); soupMat.SetFloat("_Day", day);
            causMat.SetFloat("_Tide", pool.Tide); causMat.SetFloat("_Day", day);
            blobMat.SetFloat("_Day", day); moteMat.SetFloat("_Day", day);

            // camera: follow the player (bubble or strand); while choosing a relative or a founder, pull back to show them
            Vector2 focus = player != null ? player.pos : playerViroid != null ? playerViroid.pos : camPos;
            float size = (player != null ? 5.5f + player.Radius * 2.2f : playerViroid != null ? 3.5f : 8f) * zoom;
            if (playerViroid != null && playerViroid.interior && playerViroid.host != null) { focus = playerViroid.host.pos; size = playerViroid.host.Radius * 1.35f; }   // the cell is your world now
            if (playerViroid != null && virusEditing && playerViroid.body != null) { focus = playerViroid.pos; size = playerViroid.body.Radius * 3.2f; }
            if (dive > 0f && player != null)
            {
                dive -= dt;
                float u = 1f - Mathf.Clamp01(dive / 6.5f);                            // 0 → 1 over the dive
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 3f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.7f) / 0.3f)));
                size = Mathf.Lerp(size, player.Radius * 1.35f, k);                    // in fast, hold close while it changes, ease out
            }
            if (editing && player != null) size = player.Radius * (geneFocus >= 0 ? 0.9f : 1.25f);   // deeper: one gene fills the view
            if (picking || collapsing)
            {
                var b = new Bounds(focus, Vector3.zero);
                if (picking) foreach (var o in options) b.Encapsulate((Vector3)((Protocell)o).pos);
                if (collapsing) foreach (var c in founderCarriers) if (c != null && !c.dead) b.Encapsulate((Vector3)c.pos);
                focus = b.center; size = Mathf.Max(10f, Mathf.Max(b.extents.x / world.cam.aspect, b.extents.y) + 5f);
            }
            size = Mathf.Min(size, Mathf.Min(TidePool.H * 0.5f, TidePool.W * 0.5f / world.cam.aspect) * 0.97f);   // never past the world's edge
            if (!seabedAssembly.Active)        // the seabed view drives the camera itself
            {
                camPos = Vector2.Lerp(camPos, focus, 1f - Mathf.Exp(-dt * 5f));
                camSize = Mathf.Lerp(camSize, size, 1f - Mathf.Exp(-dt * 3f));
                float chh = camSize, chw = chh * world.cam.aspect;
                camPos = new Vector2(Mathf.Clamp(camPos.x, chw, TidePool.W - chw), Mathf.Clamp(camPos.y, chh, TidePool.H - chh));
                world.cam.orthographicSize = camSize;
                world.cam.transform.position = new Vector3(camPos.x, camPos.y, -10f);
            }
            else { camPos = world.cam.transform.position; camSize = world.cam.orthographicSize; }

            // free molecules (+ their soft shadows on the floor; floating ones cast offset, blurrier shadows)
            motesFree.Clear(); shadows.Clear(); foreground.Clear();
            var ctx = world.ctx;
            Vector2 sh = new Vector2(0.35f, -0.5f);   // light from the upper left
            Rect view = ViewRect(4f);
            // focal plane at the depth of whatever you're watching: things higher/lower in the water go soft
            Vector2 fp = player != null ? player.pos : playerViroid != null ? playerViroid.pos : camPos;
            float focusLift = Mathf.Clamp01(pool.Depth(fp) * 12f);
            foreach (var m in field.motes)
            {
                if (!m.alive || !view.Contains(m.pos)) continue;
                var col = MoteChem.Color(m.kind, ctx.solvent); col.a = m.stranded ? 0.55f : 0.95f;
                bool alive = player != null && player.life != null;
                if (alive && m.kind != MoteKind.Organic) continue;                    // not your concern any more
                if (alive)
                {
                    int fk = (int)(Frac(m.spin * 13.7f + 0.5f) * 4f) % 4;          // droplet · globule cluster · floc · membrane scrap
                    var fc = fk == 0 ? new Color(0.88f, 0.76f, 0.5f, 0.9f) : fk == 1 ? new Color(0.8f, 0.84f, 0.6f, 0.9f)
                           : fk == 2 ? new Color(0.62f, 0.5f, 0.38f, 0.85f) : new Color(0.9f, 0.7f, 0.62f, 0.8f);
                    float fs = (fk == 0 ? 0.34f : fk == 1 ? 0.42f : fk == 2 ? 0.5f : 0.46f) * (0.7f + 0.6f * Frac(m.spin * 3.37f + 0.11f));
                    float cj = Frac(m.spin * 5.71f + 0.7f) - 0.5f, cj2 = Frac(m.spin * 9.13f + 0.2f) - 0.5f;
                    fc = new Color(fc.r * (1f + cj * 0.22f), fc.g * (1f + cj2 * 0.18f), fc.b * (1f - cj * 0.25f), fc.a);
                    motesFree.Add(m.pos, fs, m.rot, 1f, fk == 0 ? 10 : 11 + fk, new Vector4(1f, 0.2f, Frac(m.spin * 7.1f + 0.3f), 0), Vector4.zero, fc);
                    continue;
                }
                float lift = m.stranded ? 0.05f : Mathf.Clamp01(pool.Depth(m.pos) * 12f);
                shadows.Add(m.pos + sh * (0.15f + lift * 0.6f), 0.3f + lift * 0.15f, 0f, 1f, 7, new Vector4(1f, 0, 0, 0), Vector4.zero, new Color(0, 0, 0, 0.35f * (1f - lift * 0.5f) * day));
                {
                    float hs = Frac(m.spin * 17.31f + 0.37f), hc = Frac(m.spin * 7.77f + 0.11f);
                    float sz = 0.27f * (0.72f + 0.6f * hs);                                  // sizes vary (a touch smaller)
                    col = new Color(col.r * (0.85f + 0.3f * hc), col.g * (0.85f + 0.3f * Frac(hc * 3.7f)), col.b * (0.85f + 0.3f * Frac(hc * 5.3f)), col.a);   // and shades
                    motesFree.Add(m.pos, sz, m.rot, 1f, (int)m.kind, new Vector4(m.stranded ? 0.65f : 1f, 0, Mathf.Clamp01(Mathf.Abs(lift - focusLift) * 1.4f), 0), Vector4.zero, col);
                }
            }
            featFloor.Clear(); featOver.Clear(); featShadow.Clear();
            features.Render(featFloor, featOver, featShadow, ViewRect(14f));
            featFloor.Upload(); featOver.Upload(); featShadow.Upload();
            vents.Render(shadows, motesFree, t0(), day);
            suspended.Render(motesFree, foreground, view, focusLift, day);
            DrawEnergyMotes(pool.time);
            // free DNA fragments: short pale coils drifting (gene transfer)
            foreach (var f in fragments)
            {
                var fcol = Pathways.Get(f.p).knot; fcol.a = 0.85f;
                fcol.a *= Mathf.Clamp01(f.life / 5f);
                Vector2 prev = f.pos;
                for (int k = 1; k <= 5; k++)
                {
                    float u = k / 5f;
                    var q = f.pos + new Vector2(u * 0.9f - 0.45f, Mathf.Sin(u * 7f + f.seed + pool.time * 0.7f) * 0.18f);
                    Vector2 mid = (q + prev) * 0.5f, seg = q - prev;
                    if (k > 1) motesFree.Add(mid, seg.magnitude * 0.6f, Mathf.Atan2(seg.y, seg.x), 1f, 11, new Vector4(1f, 0.3f, u, 0), Vector4.zero, fcol);
                    prev = q;
                }
            }
            motesFree.Upload();

            // bubbles + their insides
            blobs.Clear(); motesIn.Clear(); editorQ.Clear(); vmOut.Clear(); vmIn.Clear();
            float t = pool.time;
            bursts?.Tick(dt, Flow);
            foreach (var c in cells)
            {
                float rad = c.Radius;
                float strain = Mathf.Clamp01((c.Pressure - 0.65f) / Mathf.Max(c.BurstThreshold - 0.65f, 0.05f));
                // something replicating inside: an erratic, restless shimmer (cause unexplained)
                strain = Mathf.Max(strain, c.viroidLoad * (0.45f + 0.35f * Mathf.Abs(Mathf.Sin(pool.time * 7.3f + c.wanderSeed))));
                float glow = 0f; if (c.life == null) foreach (var ch in c.chains) glow = Mathf.Max(glow, ch.glow);   // living cells don't glow like replicators
                glow = Mathf.Clamp01(glow * (0.6f + 0.15f * c.ReplicatorCount));
                if (c.life != null)
                {
                    strain = Mathf.Max(strain, c.life.membraneStress * 1.2f);
                    if (c.genome.rt && !c.genome.Dna) glow = Mathf.Max(glow, 0.25f * (0.5f + 0.5f * Mathf.Sin(pool.time * 2f + c.wanderSeed)));   // DNA forming
                }
                c.absorbFlash = Mathf.Max(0f, c.absorbFlash - dt * 2f);
                glow = Mathf.Max(glow, c.absorbFlash * 0.6f);
                if (c.player && c.life != null && c.genome.ring && fission == 0 && FissionReady(c)) glow = Mathf.Max(glow, 0.35f + 0.35f * Mathf.Sin(pool.time * 4f));   // ripe: ready to divide
                bool option = (picking && options.Contains(c)) || (collapsing && founderCarriers.Contains(c));
                var tint = c.genome.Tint; tint.a = c.dead ? 0f : 1f;
                if (c.life != null)
                    foreach (var gp in c.genome.genes)
                    {
                        var pg = Pathways.Get(gp).pigment;
                        if (pg.a > 0f) tint = Color.Lerp(tint, new Color(pg.r, pg.g, pg.b, tint.a), pg.a * S1Life.Share(c.genome, gp) * c.life.morph);
                    }
                strain = Mathf.Max(strain, c.lyse);
                blobs.Add(c.pos, rad * 2.3f, c.life != null ? c.life.heading : 0f, 2.3f, c.life != null ? Mathf.SmoothStep(0f, 1f, c.life.morph) : 0f,
                          new Vector4(strain, glow, Mathf.Max(1f - c.moisture, c.life != null ? c.life.starvation : 0f), Mathf.Clamp01(Mathf.Max(c.pinch, Stretch(c) * 0.3f))),
                          new Vector4(c.wanderSeed, (c.life != null ? Mathf.Clamp01(0.15f + (1f - c.genome.leak) * 0.75f) : Mathf.Clamp01(c.genome.membraneThickness)), c.dead ? Mathf.Max(c.burstFlash, 0f) : 0f, option ? 1f : 0f),
                          c.dead ? new Color(tint.r, tint.g, tint.b, 1f) : tint, 0f, new Vector4(c.genome.elong * (c.life != null ? c.life.morph : 0f), c.genome.bend, c.genome.lobes, c.life != null ? 0.001f + Frac(c.SpeciesId * 0.6180339f + 0.137f) * 0.998f : 0f));
                if (c.dead) continue;
                float lift = Mathf.Clamp01(pool.Depth(c.pos) * 10f);
                shadows.Add(c.pos + sh * (rad * 0.3f + lift * 1.2f), rad * (1.05f + lift * 0.25f), 0f, 1f, 7, new Vector4(1f, 0, 0, 0), Vector4.zero, new Color(0, 0, 0, 0.4f * (1f - lift * 0.4f) * day));
                if (!view.Overlaps(new Rect(c.pos.x - rad, c.pos.y - rad, rad * 2f, rad * 2f))) continue;
                if (seabedAssembly.Active && c == player) continue;   // the hands-on view draws (and lets you touch) its contents
                DrawInside(c, rad, t);
            }
            DrawViroids(t);
            if (revealedRecipe != null) DrawRecipeBadge(t);
            blobs.Upload(); motesIn.Upload(); editorQ.Upload(); shadows.Upload();
            { var wc = soupMat.GetColor("_WaterShallow"); vmOut.Upload(pool.Daylight, wc); vmIn.Upload(pool.Daylight, wc); }
            foreground.Upload();

            seabed.Clear();
            if (seabedAssembly.Active)
            {
                var cols = new Color[4];
                for (int k = 0; k < 4; k++) cols[k] = MoteChem.Color((MoteKind)k, ctx.solvent);
                seabedAssembly.Render(seabed, cols, t);
            }
            seabed.Upload();
        }

        /// A viroid is a bare strand: a few bases on a backbone, tumbling. Faint in open water, fading as it
        /// degrades; inside a host it curls up near the middle, pulsing while it copies itself.
        void DrawViroids(float t)
        {
            var ctx = world.ctx;
            foreach (var v in viroids)
            {
                if (!v.alive) continue;
                float ms = v.insideHost ? 0.1f : 0.08f;
                float alpha = v.insideHost ? 0.95f : Mathf.Lerp(0.35f, 0.85f, v.rnaIntegrity);
                if (v.player && !v.insideHost) alpha = Mathf.Max(alpha, 0.7f);
                Vector2 c = v.insideHost && v.host != null ? v.host.pos + new Vector2(Mathf.Sin(v.spin * 0.3f), Mathf.Cos(v.spin * 0.37f)) * v.host.Radius * 0.25f : v.pos;
                float a0 = v.spin;
                if (v.player && v.body != null && (!v.insideHost || v.interior))
                {
                    var b = v.body;
                    Color mix = new Color(0.82f, 0.78f, 0.9f);
                    float scale = 1f;
                    if (v.interior && v.host != null)
                    {
                        scale = v.host.Radius * (0.1f + 0.07f * Mathf.Clamp01(b.parts.Count / (float)VirusBody.MaxParts)) / Mathf.Max(b.Radius * 1.2f, 1e-3f);
                        editorQ.Add(c, b.Radius * scale * 2.6f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0.85f, 0.6f, 1f, 0.22f + 0.08f * Mathf.Sin(t * 3f)));   // you, in here
                    }
                    (v.interior ? vmIn : vmOut).Add(b, true, c, 0f, scale, PartColor, Color.Lerp(mix, Color.white, b.hitFlash * 0.5f), 0.12f + b.hitFlash * 0.4f, 1f);
                    if (virusEditing && !v.interior) DrawVirusEditor(v, t);
                    if (v.interior) DrawPicks(v.host, t);
                    ms *= 0.55f;
                    if (v.interior) continue;
                }
                else if (!v.insideHost)
                {
                    // outside a host the strand travels inside a geometric protein shell (the capsid)
                    float cs = ms * (v.strand.Count * 0.9f + 2.2f);
                    float vs = StrandSeed(v.strand);
                    if (v.body == null) v.body = VirusBody.Wild(vs);
                    var fam = VirusColor(vs);
                    // each wild virus turns at its own pace and phase (its own seed + where it is)
                    float rot = vs * 6.2831853f + t * (0.15f + 0.35f * Frac(vs * 7.3f)) * (Frac(vs * 3.1f) > 0.5f ? 1f : -1f) + v.pos.x * 0.05f;
                    vmOut.Add(v.body, false, c, rot, cs * 0.7f / Mathf.Max(v.body.Radius * 1.2f, 1e-3f), k => Color.Lerp(PartColor(k), fam, 0.55f), fam, 0f, alpha);
                    continue;   // the wild virion hides its strand
                    ms *= 0.6f;
                }
                Vector2 prev = Vector2.zero;
                for (int i = 0; i < v.strand.Count; i++)
                {
                    float a = a0 + i * 0.5f;
                    var p = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * ((i - (v.strand.Count - 1) * 0.5f) * ms * 1.8f)
                              + new Vector2(-Mathf.Sin(a0), Mathf.Cos(a0)) * Mathf.Sin(a * 1.7f) * ms * 0.4f;
                    if (i > 0)
                    {
                        Vector2 mid = (p + prev) * 0.5f, seg = p - prev;
                        motesIn.Add(mid, seg.magnitude * 0.5f, Mathf.Atan2(seg.y, seg.x), 1f, 8, new Vector4(1f, 0f, 0f, 0f), Vector4.zero, new Color(0.9f, 0.9f, 0.85f, alpha * 0.8f));
                    }
                    prev = p;
                    var col = MoteChem.Color((MoteKind)v.strand[i], ctx.solvent); col.a = alpha;
                    float pulse = v.insideHost && !v.dormant ? 0.3f * (0.5f + 0.5f * Mathf.Sin(t * 6f + i)) : 0f;
                    motesIn.Add(p, ms, a0 + Mathf.PI * 0.5f, 1f, v.strand[i], new Vector4(v.dormant ? 0.6f : 1f, pulse, 0, 0), Vector4.zero, col);
                }
            }
        }

        void DrawInside(Protocell c, float rad, float t)
        {
            var ctx = world.ctx;
            float ms = Mathf.Clamp(rad * 0.1f, 0.12f, 0.22f);
            if (c.life != null)
            {
                // the genome: a loop of RNA at the heart of the cell (a twisted double strand once it is DNA), with its
                // gene BAYS along it — each a coloured knot when filled, a pale empty pocket when not
                int segs = 34;
                float fr = rad * 0.26f;
                var g = c.genome;
                float dnaK = g.dnaStage / (float)Genome.DnaStages;
                Color rc = Color.Lerp(new Color(0.82f, 0.86f, 0.92f, 0.7f), new Color(0.9f, 0.92f, 0.98f, 0.95f), dnaK);
                bool dividing = c.player && fission >= 1;
                int copies = dividing ? 2 : 1;
                var axis = new Vector2(Mathf.Cos(c.life.heading), Mathf.Sin(c.life.heading));   // the cell divides across its long axis
                if (c.player && editing) { DrawEditor(c, t); copies = 0; }
                for (int cp = 0; cp < copies; cp++)
                {
                    float fx = fission == 1 ? 0f : (cp == 0 ? fxA : fxB);
                    var gcen = c.pos + (dividing ? axis * fx * rad * 0.55f : Vector2.zero);
                    if (dividing && c.pinch > 0.3f && CopyInMiddle(fx)) rc = new Color(1f, 0.55f, 0.45f, 0.95f);   // in the ring's way
                    else rc = Color.Lerp(new Color(0.82f, 0.86f, 0.92f, 0.7f), new Color(0.9f, 0.92f, 0.98f, 0.95f), dnaK);
                    float frc = dividing ? rad * 0.2f : fr;
                    int segsN = fission == 1 && cp == 1 ? Mathf.RoundToInt(segs * fCopy) : segs;
                    for (int strandI = 0; strandI < (dnaK > 0f ? 2 : 1); strandI++)
                    {
                        Vector2 prev = Vector2.zero;
                        for (int i = 0; i <= segsN; i++)
                        {
                            float u = i / (float)segs;
                            var p = GenomePoint(c, u, t, frc, gcen);
                            if (strandI == 1)
                            {
                                // the second strand of DNA winds around the first
                                var tan = GenomePoint(c, u + 0.01f, t, frc, gcen) - p;
                                var nrm = new Vector2(-tan.y, tan.x).normalized;
                                p += nrm * Mathf.Sin(u * 90f) * rad * 0.025f * dnaK;
                            }
                            if (i > 0)
                            {
                                Vector2 mid = (p + prev) * 0.5f, seg = p - prev;
                                motesIn.Add(mid, seg.magnitude * 0.6f, Mathf.Atan2(seg.y, seg.x), 1f, 11, new Vector4(1f, 0.12f, u, 0), Vector4.zero, strandI == 1 ? new Color(rc.r, rc.g, rc.b, rc.a * dnaK) : rc);
                            }
                            prev = p;
                        }
                    }
                    if (cp == 1) continue;
                    // the bays
                    for (int k = 0; k < g.geneSlots; k++)
                    {
                        var bp = GenomePoint(c, BayU(k, g.geneSlots), t, frc, gcen);
                        var seqK = k < g.bays.Count ? g.bays[k] : null;
                        int mp = -1, merr = 9;
                        if (seqK != null) mp = Pathways.Match(seqK, out merr);
                        if (seqK != null && merr > 2)
                            motesIn.Add(bp, ms * 1.6f, t * 0.05f + k, 1f, 15, new Vector4(1f, 0f, SeqCode(seqK), 0f), Vector4.zero, new Color(0.55f, 0.3f, 0.26f, 0.9f));   // misfolded: a dull tangle
                        else if (seqK != null)
                        {
                            var gp = (Pathway)mp;
                            var kc = Pathways.Get(gp).knot; kc.a = merr == 0 ? 0.95f : 0.6f;
                            float pr = g.priority[(int)gp];
                            if (c.player && dragGene == k)
                            {
                                Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
                                bp = Vector2.Lerp(bp, mw, 0.6f);
                                if (dragOut) kc.a = 0.45f;
                            }
                            float work = c.life.yield[(int)gp] > 0.01f ? 0.5f + 0.5f * Mathf.Sin(t * 5f + k) : 0f;   // it pulses while it earns
                            motesIn.Add(bp, ms * (1.5f + pr * 1.3f), t * 0.05f + k, 1f, 15, new Vector4(1f, work, SeqCode(seqK), Quality(merr)), Vector4.zero, GeneColor(kc, merr));
                        }
                        else motesIn.Add(bp, ms * 1.3f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0.85f, 0.9f, 0.95f, 0.22f));   // an empty bay
                    }
                    if (g.rt && !g.Dna)   // the viral gene: a dark knot that doesn't belong
                        motesIn.Add(GenomePoint(c, 0.93f, t, frc, gcen), ms * 1.2f, t, 1f, 13, new Vector4(1f, 0.1f, 0.6f, 0), Vector4.zero, new Color(0.3f, 0.2f, 0.35f, 0.95f));
                }
                // PROTEINS: each gene builds its own machine in the membrane, and each looks different —
                //   light genes: purple / red antenna crescents · vent H₂: cyan pump pores · sulfur: yellow granule clusters
                //   iron: rust flocs · lysis: a green enzyme haze outside · fermentation: food held in vacuoles inside
                // more of a gene's share → more machines; they light up as they work (how well = how bright and how busy)
                if (c.life.morph > 0.5f)
                    for (int k = 0; k < g.genes.Count; k++)
                    {
                        var gp = g.genes[k];
                        var kc = Pathways.Get(gp).knot;
                        if (!Pathways.Get(gp).protein)
                        {
                            // an RNA catalyst: copies of its fold drifting in the cytoplasm, working where they are
                            int nR = 1 + Mathf.RoundToInt(S1Life.Share(g, gp) * 4f);
                            float onR = Mathf.Clamp01(c.life.yield[(int)gp] * 3f);
                            int bay = BayOf(g, gp);
                            var rs = bay >= 0 ? g.bays[bay] : Pathways.Recipe(gp);
                            Pathways.Match(rs, out int rErr, g.ribosome);
                            for (int q = 0; q < nR; q++)
                                motesIn.Add(c.pos + RibozymeOffset(c, k, q, t) * rad, ms * 1.1f, t * 0.15f + q * 2f, 1f, 15, new Vector4(1f, onR, SeqCode(rs), Quality(rErr)), Vector4.zero, GeneColor(kc, rErr));
                            continue;
                        }
                        int nP = 2 + Mathf.RoundToInt(S1Life.Share(g, gp) * 10f);
                        float on = Mathf.Clamp01(c.life.yield[(int)gp] * 3f);
                        for (int q = 0; q < nP; q++)
                        {
                            float a = ProteinAngle(c, k, q, nP, t);
                            var mp = c.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad * 0.92f;
                            kc.a = 0.35f + 0.6f * on * (0.7f + 0.3f * Mathf.Sin(t * 6f + q * 2.1f));
                            switch (gp)
                            {
                                case Pathway.Rhodopsin: case Pathway.AnoxygenicPhoto:
                                    motesIn.Add(mp, ms * 1.1f, a + Mathf.PI, 1f, 14, new Vector4(1f, on, Frac(q * 0.31f + 0.4f), 0), Vector4.zero, kc); break;
                                case Pathway.ProtonPump:
                                    motesIn.Add(mp, ms * 0.5f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(kc.r, kc.g, kc.b, kc.a));
                                    motesIn.Add(mp, ms * 0.22f, 0f, 1f, 10, new Vector4(1f, on, 0.2f, 0), Vector4.zero, new Color(0.9f, 1f, 1f, 0.5f + 0.5f * on)); break;
                                case Pathway.SulfurOxidation:
                                    motesIn.Add(c.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad * 0.75f, ms * 0.7f, a, 1f, 12, new Vector4(1f, on * 0.5f, Frac(q * 0.17f), 0), Vector4.zero, kc); break;
                                case Pathway.IronOxidation:
                                    motesIn.Add(mp, ms * 0.8f, a, 1f, 13, new Vector4(1f, on * 0.3f, Frac(q * 0.29f), 0), Vector4.zero, kc); break;
                                default:   // lysis: enzymes leaking outside
                                    motesIn.Add(c.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad * 1.15f, ms * 1.2f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(kc.r, kc.g, kc.b, 0.15f)); break;
                            }
                        }
                    }
                // (nucleotides are shown in the editor's tray, not here)
                for (int bk = 0; bk < (editing && c.player ? 0 : 0); bk++)
                    for (int qi = 0; qi < Mathf.Min(c.free[bk], 8); qi++)
                    {
                        float s1 = bk * 11.3f + qi * 2.71f + c.wanderSeed;
                        var off = new Vector2(Mathf.Sin(t * 0.12f + s1), Mathf.Cos(t * 0.1f + s1 * 1.3f)) * 0.68f;
                        var bc = new Color(0.82f, 0.84f, 0.8f, 0.85f);
                        motesIn.Add(c.pos + off * rad, ms * 0.32f, s1 + t * 0.2f, 1f, 16, new Vector4(1f, 0f, (bk + 0.5f) / 4f, 0), Vector4.zero, bc);
                    }
                // amino acids held (small pink knobs) — shown once the road to the ribosome has begun
                bool riboWork0 = c.life.peptides > 0.5f || c.life.pepRate > 0.1f;
                for (int qi = 0; qi < (riboWork0 ? Mathf.Min(Mathf.CeilToInt(c.life.amino), 12) : 0); qi++)
                {
                    float s1 = qi * 2.91f + c.wanderSeed * 1.1f;
                    var off = new Vector2(Mathf.Sin(t * 0.11f + s1), Mathf.Cos(t * 0.1f + s1 * 1.4f)) * 0.6f;
                    motesIn.Add(c.pos + off * rad, ms * 0.3f, s1, 1f, 12, new Vector4(1f, 0f, Frac(s1), 0), Vector4.zero, new Color(0.95f, 0.75f, 0.8f, 0.85f));
                }
                // peptides: chains of amino acids. Random → tangled and varied; with adaptors (a code) they fold into the
                // same tidy helix every time. Half of them line the membrane (that's how they steady it).
                int nPep = Mathf.Min(Mathf.RoundToInt(c.life.peptides), 14);
                for (int qi = 0; qi < nPep; qi++)
                {
                    float s1 = qi * 3.77f + c.wanderSeed * 0.9f;
                    Vector2 basePos = qi % 2 == 0
                        ? c.pos + new Vector2(Mathf.Cos(s1 + t * 0.03f), Mathf.Sin(s1 + t * 0.03f)) * rad * 0.82f
                        : c.pos + new Vector2(Mathf.Sin(t * 0.08f + s1), Mathf.Cos(t * 0.07f + s1 * 1.3f)) * rad * 0.55f;
                    int len = 3 + (int)(Frac(s1 * 1.7f) * 4f);
                    float coded = c.life.coded;
                    Vector2 prevP = basePos;
                    for (int j = 0; j < len; j++)
                    {
                        // tidy helix (coded) vs a random walk (uncoded)
                        float ja = coded * (j * 1.2f) + (1f - coded) * (Frac(Mathf.Sin(s1 * 13.1f + j * 7.3f) * 43758.5f) * 6.283f);
                        var pp = j == 0 ? basePos : prevP + new Vector2(Mathf.Cos(ja + s1), Mathf.Sin(ja + s1)) * rad * 0.045f;
                        if (j > 0) { Vector2 mid = (pp + prevP) * 0.5f, sg = pp - prevP; motesIn.Add(mid, sg.magnitude * 0.55f, Mathf.Atan2(sg.y, sg.x), 1f, 8, new Vector4(0.8f, 0, 0, 0), Vector4.zero, new Color(0.9f, 0.72f, 0.78f, 0.6f)); }
                        motesIn.Add(pp, ms * 0.22f, 0f, 1f, 10, new Vector4(1f, 0.1f, Frac(s1 + j * 0.3f), 0), Vector4.zero, new Color(0.96f, 0.76f, 0.82f, 0.9f));
                        prevP = pp;
                    }
                }
                // iron–sulfur grains you hold: small dark metallic flecks (the catalytic cores of CO₂ fixation)
                for (int qi = 0; qi < Mathf.Min(Mathf.CeilToInt(c.life.fes), 10); qi++)
                {
                    float s1 = qi * 3.31f + c.wanderSeed * 0.7f;
                    var off = new Vector2(Mathf.Sin(t * 0.07f + s1), Mathf.Cos(t * 0.06f + s1 * 1.9f)) * 0.5f;
                    motesIn.Add(c.pos + off * rad, ms * 0.45f, s1, 1f, 13, new Vector4(1f, 0.1f, Frac(s1), 0), Vector4.zero, new Color(0.35f, 0.3f, 0.26f, 0.95f));
                }
                // the vent's protons streaming in through a leaky membrane (bright specks crossing inward)
                if (c.player && c.life.gradYield > 0.01f)
                {
                    int np = Mathf.Clamp(Mathf.RoundToInt(c.life.gradYield * 30f), 1, 24);
                    for (int qi = 0; qi < np; qi++)
                    {
                        float ph = Frac(t * 0.8f + qi * 0.137f), a = qi * 2.399f + c.wanderSeed + Mathf.Floor(t * 0.8f + qi * 0.137f) * 1.3f;
                        var p = c.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad * Mathf.Lerp(1.15f, 0.8f, ph);
                        motesIn.Add(p, ms * 0.18f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0.85f, 1f, 1f, 0.8f * Mathf.Sin(ph * Mathf.PI)));
                    }
                }
                // energy sparks: every working gene sends little flashes from its machines to the heart of the cell
                if (c.player) foreach (var sp in sparks)
                {
                    var from = c.pos + new Vector2(Mathf.Cos(sp.ang), Mathf.Sin(sp.ang)) * rad * sp.r0;
                    var p = Vector2.Lerp(from, c.pos, sp.prog * sp.prog);
                    var sc = sp.col; sc.a = 0.9f * Mathf.Sin(sp.prog * Mathf.PI);
                    motesIn.Add(p, ms * 0.35f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, sc);
                }
                // raw feedstock: dull brown granules that pile up when the ribozymes can't keep up (a clogged cell)
                int rawN = 0;   // merged into the gold beads' glow
                for (int qi = 0; qi < rawN; qi++)
                {
                    float s1 = qi * 3.71f + c.wanderSeed * 1.3f, s2 = qi * 2.13f + c.wanderSeed;
                    var off = new Vector2(Mathf.Sin(t * 0.09f + s1) + Mathf.Sin(t * 0.05f + s2) * 0.6f, Mathf.Cos(t * 0.08f + s2) + Mathf.Cos(t * 0.04f + s1) * 0.6f) / 1.6f;
                    motesIn.Add(c.pos + off * rad * 0.7f, ms * 0.4f, 0f, 1f, 10, new Vector4(1f, 0f, Frac(s1), 0), Vector4.zero, new Color(0.45f, 0.36f, 0.26f, 0.7f));
                }
                // ribozymes: gold beads — more and brighter with quality, flaring while they convert
                int beads = 4 + Mathf.RoundToInt(c.life.ribozymeQuality * 12f);
                float riboWork = Mathf.Clamp01(c.life.converting * 2f);
                var gc = Color.Lerp(new Color(0.6f, 0.5f, 0.3f), new Color(1f, 0.88f, 0.5f), c.life.ribozymeQuality); gc.a = 0.55f + 0.4f * riboWork;
                for (int bi = 0; bi < beads; bi++)
                {
                    // each granule drifts on its own slow path through the cytoplasm
                    float s1 = bi * 2.399f + c.wanderSeed, s2 = bi * 1.618f + c.wanderSeed * 0.7f;
                    var off = new Vector2(Mathf.Sin(t * 0.13f + s1) + Mathf.Sin(t * 0.07f + s2 * 2f) * 0.5f, Mathf.Cos(t * 0.11f + s2) + Mathf.Cos(t * 0.05f + s1 * 1.5f) * 0.5f) / 1.5f;
                    var bp = c.pos + off * rad * 0.62f;
                    motesIn.Add(bp, ms * (0.45f + 0.25f * Frac(s1)), 0f, 1f, 10, new Vector4(1f, riboWork * (0.5f + 0.5f * Mathf.Sin(t * 7f + bi)), 0, 0), Vector4.zero, gc);
                }
                return;
            }
            // loose molecules jostle inside
            int n = 0;
            for (int k = 0; k < MoteChem.KindCount && n < 40; k++)
                for (int i = 0; i < c.free[k] && n < 40; i++, n++)
                {
                    float s = n * 12.9898f + c.wanderSeed * 7.1f;
                    float a = Frac(Mathf.Sin(s) * 43758.5f) * 6.283f + t * (0.3f + Frac(s * 0.37f) * 0.4f);
                    float d = Mathf.Sqrt(Frac(Mathf.Sin(s * 1.7f) * 24634.6f)) * rad * 0.78f;
                    var p = c.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    var col = MoteChem.Color((MoteKind)k, ctx.solvent); col.a = 0.9f;
                    motesIn.Add(p, ms, a * 2f, 1f, k, new Vector4(0.9f, 0, 0, 0), Vector4.zero, col);
                }
            // a living cell's ribozymes: golden catalyst beads whose glow IS their quality
            if (c.life != null)
            {
                int beads = Mathf.RoundToInt(c.life.ribozymeQuality * 6f);
                for (int bi = 0; bi < beads; bi++)
                {
                    float ba = c.wanderSeed * 3f + bi * 1.7f + t * 0.4f;
                    var bp = c.pos + new Vector2(Mathf.Cos(ba), Mathf.Sin(ba)) * rad * (0.5f + 0.12f * Mathf.Sin(t + bi));
                    motesIn.Add(bp, ms * 0.9f, ba, 1f, 6, new Vector4(1f, c.life.ribozymeQuality * 0.8f, 0, 0), Vector4.zero, new Color(0.95f, 0.85f, 0.55f, 0.9f));
                }
            }
            // chains: curled strings of bases; a forming copy rides alongside (complementary shapes interlock)
            int ci = 0;
            foreach (var ch in c.chains)
            {
                if (ci++ > 6) break;
                float baseA = c.wanderSeed + ci * 2.1f + t * 0.1f;
                // fit the string inside the membrane: shorter steps for long chains, start nearer the middle
                float step = Mathf.Min(ms * 1.9f, rad * 1.3f / Mathf.Max(ch.seq.Count, 1));
                Vector2 start = c.pos + new Vector2(Mathf.Cos(baseA), Mathf.Sin(baseA)) * rad * 0.25f;
                Vector2 prev = Vector2.zero;
                for (int i = 0; i < ch.seq.Count; i++)
                {
                    float a = baseA + i * 0.35f + Mathf.Sin(t * 0.8f + i) * 0.1f;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));      // each base's notch/tab faces the copy side
                    var p = start + Rot(dir, Mathf.PI * 0.5f) * (i * step);   // a gently curling string
                    Vector2 rel = p - c.pos; float lim = rad * 0.78f;
                    if (rel.sqrMagnitude > lim * lim) p = c.pos + rel.normalized * lim;   // never past the membrane
                    // the bond to the previous base in the chain
                    if (i > 0)
                    {
                        Vector2 mid = (p + prev) * 0.5f, seg = p - prev;
                        motesIn.Add(mid, seg.magnitude * 0.5f, Mathf.Atan2(seg.y, seg.x), 1f, 8, new Vector4(1f, ch.glow * 0.4f, 0, 0), Vector4.zero, new Color(0.9f, 0.92f, 0.85f, 0.8f));
                    }
                    prev = p;
                    var col = MoteChem.Color((MoteKind)ch.seq[i], ctx.solvent); col.a = 1f;
                    if (ch.glow > 0.05f)   // a replicator: a pulsing golden halo behind each base
                        motesIn.Add(p, ms * 2.4f, 0f, 1f, 7, new Vector4(1f, 0, 0, 0), Vector4.zero, new Color(1f, 0.85f, 0.4f, 0.5f * ch.glow * (0.75f + 0.25f * Mathf.Sin(t * 4f + i))));
                    motesIn.Add(p, ms, a, 1f, ch.seq[i], new Vector4(1f + ch.glow * 0.3f, ch.glow * 0.9f, 0, 0), Vector4.zero, col);
                    Vector2 dock = p + dir * ms * (ch.seq[i] < 2 ? 1.03f : 0.56f);   // centre distance at which the partner's tab seats in this notch   // centre distance at which the partner's tab seats in this notch (or vice versa)
                    if (i < ch.copy.Count)
                    {
                        // the newest partner slides in and CLICKS into place (a short flash on contact)
                        bool newest = i == ch.copy.Count - 1;
                        float k = newest ? Mathf.Clamp01(ch.sinceDock / 0.35f) : 1f;
                        float ease = 1f - (1f - k) * (1f - k);
                        var pc = dock + dir * ms * 2.2f * (1f - ease);
                        float flash = newest ? Mathf.Clamp01(1f - Mathf.Abs(ch.sinceDock - 0.35f) / 0.15f) : 0f;
                        var cc = MoteChem.Color((MoteKind)ch.copy[i], ctx.solvent); cc.a = 1f;
                        motesIn.Add(pc, ms, a + Mathf.PI, 1f, ch.copy[i], new Vector4(1f + flash * 0.6f, ch.glow * 0.5f + flash, 0, 0), Vector4.zero, cc);
                    }
                    else if (i == ch.copy.Count && ch.stalled)
                    {
                        // an empty socket: a ghost of the partner it's waiting for, blinking
                        float blink = 0.25f + 0.35f * (0.5f + 0.5f * Mathf.Sin(t * 6f));
                        var gc = MoteChem.Color((MoteKind)MoteChem.Complement(ch.seq[i]), ctx.solvent); gc.a = blink;
                        motesIn.Add(dock, ms, a + Mathf.PI, 1f, MoteChem.Complement(ch.seq[i]), new Vector4(1.3f, 0, 0, 0), Vector4.zero, gc);
                    }
                }
            }
        }

        /// The revealed recipe: a small glowing chain of icons in the top-left corner of the view.
        void DrawRecipeBadge(float t)
        {
            var cam = world.cam;
            float unit = cam.orthographicSize * 0.055f;
            Vector2 at = cam.ScreenToWorldPoint(new Vector3(Screen.width * 0.04f + 30f, Screen.height - Screen.height * 0.07f - 20f, 10f));
            int n = revealedRecipe.Count;
            float step = unit * 2.4f;
            Vector2 mid = at + new Vector2(step * (n - 1) * 0.5f, 0f);
            foreground.Add(mid, step * (n * 0.5f + 0.7f), 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0f, 0f, 0f, 0.35f));
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 2.2f);
            for (int i = 0; i < n; i++)
            {
                var p = at + new Vector2(step * i, 0f);
                if (i > 0) foreground.Add(p - new Vector2(step * 0.5f, 0f), step * 0.5f, 0f, 1f, 8, new Vector4(1f, 0.4f, 0, 0), Vector4.zero, new Color(0.95f, 0.95f, 0.9f, 0.9f));
                foreground.Add(p, unit * 2.2f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 0.85f, 0.4f, 0.45f * pulse));
                var col = MoteChem.Color((MoteKind)revealedRecipe[i], world.ctx.solvent); col.a = 1f;
                foreground.Add(p, unit, Mathf.PI * 0.5f, 1f, revealedRecipe[i], new Vector4(1.1f, 0.5f * pulse, 0, 0), Vector4.zero, col);
            }
        }

        Rect ViewRect(float pad)
        {
            float hh = world.cam.orthographicSize + pad, hw = hh * world.cam.aspect;
            var c = world.cam.transform.position;
            return new Rect(c.x - hw, c.y - hh, hw * 2f, hh * 2f);
        }

        static Vector2 Rot(Vector2 v, float a) { float c = Mathf.Cos(a), s = Mathf.Sin(a); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }
        static float Frac(float x) => x - Mathf.Floor(x);
        float t0() => pool.time;

        public void End()
        {
            if (root) Object.Destroy(root.gameObject);

            vmOut?.Dispose(); vmIn?.Dispose();
            if (music) Object.Destroy(music.gameObject); bursts?.Clear(); if (soupMat) Object.Destroy(soupMat); if (featMat) Object.Destroy(featMat); if (causMat) Object.Destroy(causMat); if (blobMat) Object.Destroy(blobMat); if (moteMat) Object.Destroy(moteMat);
            if (pool != null && pool.heightTex) Object.Destroy(pool.heightTex);
        }

        public void DebugGUI()
        {
            if (music != null) GUILayout.Label($"music: {music.NowPlaying}");
            if (editing) GUILayout.Label($"editor: gene {geneFocus} · holding {dragFrom} (moved {dragMoved}) · selected {selSlot} · complexity {ComplexityFill:0.0} (gathered {gathered:0.0} + {Relatives()} relatives) · beads {beads.Count}");
            GUILayout.Label($"tide {pool.Tide:0.00} ({pool.TidePhase:+0.0;-0.0}) · day {pool.Daylight:0.00} · motes {field.motes.Count} · cells {cells.Count}");
            string mot = ""; foreach (var m in motifs) { foreach (int b in m) mot += "ABCD"[b]; mot += " "; }
            GUILayout.Label($"planet motifs: {mot}{(testMotifs ? " (TEST — M to restore)" : "  · M = test recipe AAA")}");
            if (player != null)
            {
                string free = ""; for (int k = 0; k < MoteChem.KindCount; k++) free += $"{(MoteKind)k}:{player.free[k]} ";
                GUILayout.Label($"player: area {player.area:0.00} · pressure {player.Pressure:0.00}/{player.BurstThreshold:0.00} · moist {player.moisture:0.00} · depth {pool.Depth(player.pos):0.000}");
                GUILayout.Label(free);
                GUILayout.Label($"chains {player.chains.Count} · replicators {player.ReplicatorCount} · repGen {player.replicatorGenerations} · fidelity {player.genome.copyFidelity:0.000} · gen {player.generation}");
                foreach (var ch in player.chains) { string s = ""; foreach (int b in ch.seq) s += "ABCD"[b]; GUILayout.Label($"  {s}{(ch.replicator ? " ★" : "")} copy {ch.copy.Count}/{ch.seq.Count}"); }
            }
            string ms = ""; foreach (Milestone m in System.Enum.GetValues(typeof(Milestone))) if (prog.Has(m)) ms += m + " ";
            GUILayout.Label($"milestones: {ms}");
            if (picking) GUILayout.Label($"PICKING: {options.Count} living relatives · deaths in a row {consecutiveDeaths}{(consecutiveDeaths >= 2 ? " (V = viroid)" : "")}");
            GUILayout.Label($"recipes found {FoundCount()}/{motifs.Length} · living cells (S1): {lifeCount}/{momentThreshold}{(momentReached ? " — the moment has passed (life or virus)" : "")}");
            if (player != null && player.life != null)
                GUILayout.Label($"you are ALIVE (S1): source {player.life.lastSource} · ribozyme {player.life.ribozymeQuality:0.00} · stress {player.life.membraneStress:0.00} · raw {player.life.energy.raw:0.00} · making {player.life.converting:0.00}/s · copy store {player.life.copyStore:0.00}/{FissionCopyCost(player):0.00} · activity {player.life.activity:0.00} · leak {player.genome.leak:0.00} · gradient {player.life.gradYield:0.00}/s · bleed {player.life.bleed:0.000}/s · FeS {player.life.fes:0.0} · amino {player.life.amino:0.0} · peptides {player.life.peptides:0.0} · peptide-maker {player.life.pepRate:0.00} · adaptors {player.life.adapt:0.00} · coded {player.life.coded:0.00} · ribosome {player.genome.ribosome} · genes {player.genome.genes.Count}/{player.genome.geneSlots} · ring {player.genome.ring} · RT {player.genome.rt} · DNA {player.genome.dnaStage}/{Genome.DnaStages} · lineage {lineageSecured}");
            GUILayout.Label($"BIOME: {pool.biome.displayName} · rate ×{pool.biome.rateMul:0.0} · UV ×{pool.biome.uvMul:0.0} · tide ×{pool.biome.tideMul:0.0}");
            GUILayout.Label($"vents {vents.list.Count} · suspended {suspended.matter} · F6 cycles illumination");
            GUILayout.Label($"seabed: {seabedAssembly.surface}{(seabedAssembly.Active ? " (ACTIVE)" : seabedAssembly.CanEnter ? " (reachable: scroll in)" : "")}");
            if (playerViroid != null)
                GUILayout.Label($"VIROID: integrity {playerViroid.rnaIntegrity:0.00} · {(playerViroid.insideHost ? (playerViroid.dormant ? "dormant in host" : $"copying {playerViroid.copiesBuilt}/{S0Viroid.CopiesToFill}") : "drifting")} · viroids in pool {viroids.Count}");
        }
    }
}
