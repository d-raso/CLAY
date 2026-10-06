using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Stage0;
using CLAY.CellStage.Genetics;
using CLAY.CellStage.Stage1;   // TidePoolBiomeGenome, RibozymeMetabolism.SampleAll

namespace CLAY.CellStage.Stage2
{
    /// <summary>
    /// S2 "Little Engines" — the prokaryote sub-stage (CellStage_Decisions §12).
    ///
    /// The same pool as S0/S1 (heightfield, tide, day), now with real metabolic niches: sunlit shallows (light),
    /// the vent's warm plume and the iron seep (chemistry), dissolved organics everywhere, and — after the
    /// Great Oxidation Event — oxygen. Cells never pick a metabolism or a domain: lineages drift toward what
    /// earns where they live, swap genes by sustained contact, settle into biofilms, and commit to bacteria or
    /// archaea under the pressure of their habitat.
    ///
    /// Tick: pool → environment per cell → Prokaryote.Tick → HGT → biofilms → GOE → movement → division →
    ///       S3 gate → render. Nothing is announced: the GOE shows as rusting rock, clearing water and rising
    ///       oxygen bubbles; the S3 unlock shows as a dimple forming on the membrane.
    ///
    /// Controls as before (WASD / mouse). Hold Shift near another cell to press against it (more gene swapping).
    /// </summary>
    public sealed class Stage2Mode : IStageMode
    {
        // ── References ───────────────────────────────────────────────────────
        CellStageWorld        world;
        CellStageProgress     prog;
        TidePool              pool;
        TidePoolBiomeGenome   biome;

        readonly List<Prokaryote>   cells   = new();
        readonly List<BiofilmPatch> biofilms = new();

        Prokaryote player;
        int        playerSpecies;
        DetRng     r;
        const int  MaxCells = 110;
        readonly float[] scratch = new float[4];
        float      biofilmTimer;

        GoeTracker goe;

        // Respawn picker
        bool              picking;
        float             pickTimer;
        List<ILivingBody> options = new();

        // Rendering
        Transform root;
        Material soupMat, causMat, blobMat, moteMat;
        QuadBatch haze, shadows, blobs, insides;
        float camSize = 7f, zoom = 1f;
        Vector2 camPos;

        // ── IStageMode ────────────────────────────────────────────────────────

        public void Begin(CellStageWorld w, Transform parent)
        {
            world = w;
            prog  = w.progress;
            r     = new DetRng(w.ctx.poolSeed ^ 0x5AA53F00UL);
            pool  = new TidePool(w.ctx);
            var br = new DetRng(DetRng.Hash(w.ctx.poolSeed, 0xA1B2C3D4UL));   // the same vent & seep as S1
            biome = TidePoolBiomeGenome.Generate(w.ctx, pool, ref br);

            goe = new GoeTracker(w.ledger, GoeTracker.ThresholdFor(w.ctx));
            goe.OnGoeFired += HandleGoeFired;

            root = new GameObject("Stage2").transform; root.SetParent(parent, false);
            BuildVisuals();
            SeedFromFounders();
        }

        public void Tick(float dt)
        {
            world.captureEscape = false;
            pool.Tick(dt);
            float ambientO2 = world.ledger.AmbientO2;

            if (!picking && player != null && !player.dead) PlayerControl(dt);

            // cells: read the local environment, live, maybe divide
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                var c = cells[i];
                if (c.dead) continue;
                if (!c.player || picking) AIChemotaxis(c, dt);
                Environment(c.pos, out float light, out float redox, out float organics, out float extreme);
                c.envExtreme = extreme;
                c.bestLocalMetabolism = BestMetabolism(light, redox, organics, ambientO2, c.genome.oxygenTolerance);
                c.Tick(dt, ambientO2, light, redox, organics, ref r, world.ledger, prog);
                if (c.dead) { OnCellDied(c); continue; }
                c.contactTimer = Mathf.Max(0f, c.contactTimer - dt * 0.3f);   // contact has to be sustained
                TryDivide(c, ambientO2);
            }
            Collide();

            // gene transfer by sustained contact; biofilms (formation throttled)
            HGTContact.ScanContacts(cells, dt, ref r, prog);
            foreach (var bf in biofilms) bf.Tick(cells, dt);
            biofilmTimer -= dt;
            if (biofilmTimer <= 0f) { biofilmTimer = 1f; TryFormBiofilms(); }
            biofilms.RemoveAll(bf => bf.members.Count == 0);
            if (player != null && player.biofilm != null) prog.Reach(Milestone.Biofilm);

            goe.Tick(dt, this);

            // clear dead cells after their burst flash
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                var c = cells[i];
                if (c.dead) { c.burstFlash -= dt * 1.5f; if (c.burstFlash <= 0f) cells.RemoveAt(i); }
            }

            PlayerMilestones();
            CheckS3Gate();
            if (picking) PickerTick(dt);
            Render(dt);
        }

        public void End()
        {
            if (root) Object.Destroy(root.gameObject);
            if (soupMat) Object.Destroy(soupMat); if (causMat) Object.Destroy(causMat);
            if (blobMat) Object.Destroy(blobMat); if (moteMat) Object.Destroy(moteMat);
            if (pool != null && pool.heightTex) Object.Destroy(pool.heightTex);
            cells.Clear();
            biofilms.Clear();
        }

        public void DebugGUI()
        {
            GUILayout.Label($"S2 · cells={cells.Count} biofilms={biofilms.Count}" +
                            $" O₂={world.ledger.TotalOxygen:0.0}/{goe.threshold:0.0}" +
                            $" GOE={world.ledger.GoeFired} ({goe.oxidationProgress:0.00})");
            int[] byMet = new int[7]; int bac = 0, arc = 0;
            foreach (var c in cells) if (!c.dead) { byMet[(int)c.genome.metabolism]++; if (c.genome.domain == Domain.Bacteria) bac++; else if (c.genome.domain == Domain.Archaea) arc++; }
            GUILayout.Label($"  ferm {byMet[1]} chemo {byMet[2]} anox {byMet[3]} oxy {byMet[4]} resp {byMet[5]} · bacteria {bac} archaea {arc}");
            if (player != null && !player.dead)
            {
                GUILayout.Label($"  player: {player.genome.metabolism} · {player.genome.domain} (lean {player.domainLean:+0.00;-0.00}) · oTol {player.genome.oxygenTolerance:0.00}");
                GUILayout.Label($"  maturity {player.domainMaturity:0.00}/0.85 · size {player.cellSizeProgress:0.00}/0.75 · dimple {player.hasProtoEngulf} · surplus {player.energy.surplusRate:0.000}/s · deficit {player.energy.deficit:0.00}");
            }
            if (picking) GUILayout.Label($"PICKING: {options.Count} living relatives");
        }

        // ── Environment ──────────────────────────────────────────────────────

        /// Local resources (0..1) and how extreme the habitat is (archaea pressure).
        void Environment(Vector2 p, out float light, out float redox, out float organics, out float extreme)
        {
            RibozymeMetabolism.SampleAll(p, pool, biome, scratch);
            light = scratch[(int)EnergySourceType.PrimitivePhototrophy];
            redox = Mathf.Max(scratch[(int)EnergySourceType.ProtonGradient], scratch[(int)EnergySourceType.MineralRedox]);
            organics = scratch[(int)EnergySourceType.OrganicFermentation];
            var ctx = world.ctx;
            float thermal = Mathf.Clamp01((ctx.tempC - 40f) / 40f) + Mathf.Clamp01((5f - ctx.tempC) / 25f);
            extreme = Mathf.Clamp01(scratch[(int)EnergySourceType.ProtonGradient] * 1.2f + thermal
                                    + (ctx.solvent == LiquidType.Brine ? 0.35f : 0f) + pool.UV(p) * 0.3f);
        }

        /// Which metabolism would earn most here (offspring drift toward it).
        static Metabolism BestMetabolism(float light, float redox, float organics, float o2, float tolerance)
        {
            Metabolism best = Metabolism.Fermentation; float y = 0.30f * organics;
            if (1.2f * redox > y) { y = 1.2f * redox; best = Metabolism.Chemotrophy; }
            if (0.6f * light > y) { y = 0.6f * light; best = Metabolism.AnoxygenicPhoto; }
            if (tolerance >= 0.5f && 1.6f * o2 * (0.4f + organics) > y) best = Metabolism.Respiration;
            return best;
        }

        // ── Seeding ──────────────────────────────────────────────────────────

        void SeedFromFounders()
        {
            // the lineages that came through S1 (founders, as they stood); the player continues the one they played
            var founders = prog.founderGenomes;
            if (founders == null || founders.Count == 0) founders = new List<Genome> { new Genome { metabolism = Metabolism.Fermentation } };
            var playerG = prog.playerSeedGenome;
            for (int f = 0; f < founders.Count; f++)
            {
                int sid = world.species.Found(founders[f]).id;
                Vector2 home = pool.RandomWetPoint(ref r, 0.04f);
                for (int k = 0; k < 14; k++)
                {
                    var c = NewCell(founders[f].Mutate(ref r, 0.4f), home + r.InsideUnitCircle() * 6f);
                    world.species.Born(c, sid);
                    cells.Add(c);
                }
            }
            int fi = prog.playerFounder >= 0 && prog.playerFounder < founders.Count ? prog.playerFounder
                   : playerG != null ? FounderCollapse.AssignPlayerToFounder(playerG, founders) : 0;
            var pc = NewCell(founders[fi].Clone(), pool.RandomWetPoint(ref r, 0.05f));
            pc.player = true; player = pc;
            playerSpecies = world.species.Found(pc.genome, "player-lineage").id;
            world.species.Born(pc, playerSpecies);
            cells.Add(pc);
            camPos = pc.pos;
        }

        Prokaryote NewCell(Genome g, Vector2 at)
        {
            if (g.metabolism == Metabolism.None) g.metabolism = Metabolism.Fermentation;
            at.x = Mathf.Clamp(at.x, 2f, TidePool.W - 2f); at.y = Mathf.Clamp(at.y, 2f, TidePool.H - 2f);
            if (pool.Depth(at) <= 0.01f) at = pool.RandomWetPoint(ref r, 0.03f);
            return new Prokaryote { pos = at, area = 1f, birthArea = 1f, genome = g, wanderSeed = r.NextFloat() * 100f };
        }

        // ── Division ─────────────────────────────────────────────────────────

        void TryDivide(Prokaryote c, float ambientO2)
        {
            if (!c.ReadyToDivide) return;
            int alive = 0; foreach (var x in cells) if (!x.dead) alive++;
            if (alive >= MaxCells && !c.player) return;
            var child = c.Divide(ref r, c.player ? prog : DummyProgress, ambientO2, world.ledger.GoeFired);
            world.species.Born(child, c.SpeciesId);
            cells.Add(child);
        }

        // ── Biofilm formation ─────────────────────────────────────────────────

        void TryFormBiofilms()
        {
            foreach (var anchor in cells)
            {
                if (anchor.dead || anchor.biofilm != null) continue;
                var patch = BiofilmPatch.TryForm(cells, anchor.pos);
                if (patch != null) biofilms.Add(patch);
            }
        }

        // ── Milestones / gate ─────────────────────────────────────────────────

        void PlayerMilestones()
        {
            if (player == null || player.dead) return;
            if (player.energy.surplusRate > 0.02f) prog.Reach(Milestone.EnergySource);
            if (player.energy.deficit < 0.05f && player.energy.surplusRate > 0.05f && player.area > player.birthArea * 1.3f) prog.Reach(Milestone.EnergyStore);
            if (player.genome.oxygenTolerance >= 0.4f) prog.Reach(Milestone.OxygenTolerance);
        }

        void CheckS3Gate()
        {
            if (player == null || player.dead) return;
            // three body conditions (CellStage_Decisions §12): domain maturity, cell size, the proto-engulf dimple
            if (player.domainMaturity >= 0.85f && player.cellSizeProgress >= 0.75f && player.hasProtoEngulf)
                prog.Reach(Milestone.StableMetabolism);   // S2 graduation milestone → S3
        }

        // ── Death / respawn ───────────────────────────────────────────────────

        void OnCellDied(Prokaryote c)
        {
            world.species.Died(c);
            if (c.player) BeginPicker();
        }

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
                if (pickTimer > 3f)
                {
                    // the lineage is gone: continue from the most successful lineage still alive
                    Genome g = null; int best = 0;
                    foreach (var kv in world.species.All) if (kv.Value.living.Count > best) { best = kv.Value.living.Count; g = kv.Value.founder; }
                    var c = NewCell(g != null ? g.Mutate(ref r, 1f) : new Genome { metabolism = Metabolism.Fermentation }, pool.RandomWetPoint(ref r, 0.05f));
                    playerSpecies = world.species.Found(c.genome, "player-lineage").id;
                    world.species.Born(c, playerSpecies);
                    cells.Add(c); Possess(c);
                }
                return;
            }
            int chosen = -1;
            for (int k = 0; k < Mathf.Min(options.Count, 8); k++) if (Input.GetKeyDown(KeyCode.Alpha1 + k)) chosen = k;
            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
                float bestD = 1.5f;
                for (int k = 0; k < options.Count; k++)
                {
                    var o = (Prokaryote)options[k];
                    float d = (o.pos - mw).magnitude - o.Radius;
                    if (d < bestD) { bestD = d; chosen = k; }
                }
            }
            if (chosen >= 0) Possess((Prokaryote)options[chosen]);
        }

        void Possess(Prokaryote c)
        {
            if (player != null) player.player = false;
            player = c; c.player = true; picking = false;
        }

        // ── Movement ──────────────────────────────────────────────────────────

        void PlayerControl(float dt)
        {
            Vector2 dir = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dir.y += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dir.y -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dir.x += 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dir.x -= 1;
            if (Input.GetMouseButton(0))
            {
                Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
                Vector2 to = mw - player.pos;
                if (to.magnitude > player.Radius * 0.3f) dir = to;
            }
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            Steer(player, dir, dt);
            // hold Shift: press up against neighbours (sustained contact → more gene swapping)
            player.hgtProximityBias = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 1f : 0f;
            float sc = Input.mouseScrollDelta.y;
            if (Mathf.Abs(sc) > 0.01f) zoom = Mathf.Clamp(zoom * (1f - sc * 0.1f), 0.5f, 3f);
        }

        /// Low-Reynolds swimming; biofilm members are held by the matrix; dry ground pins.
        void Steer(Prokaryote c, Vector2 dir, float dt)
        {
            float depth = pool.Depth(c.pos);
            float medium = depth <= 0.003f ? 0.04f : depth < 0.04f ? 0.45f : 1f;
            float speed = (1.3f + c.genome.motility * 2.2f) * (c.genome.domain == Domain.Bacteria ? 1.15f : 1f) / (1f + c.Radius * 0.25f) * medium;
            if (c.biofilm != null) speed *= BiofilmPatch.MobilityPenalty;
            Vector2 target = dir * speed;
            if (depth < 0.04f && depth > 0.003f) target += pool.Downhill(c.pos) * (0.04f - depth) * 40f;
            var f = FlowFieldManager.Instance;
            if (f != null && depth > 0.003f && c.biofilm == null) target += f.SampleFlowAtPosition(c.pos) * (Stage0Replication.FlowCoupling / (1f + c.Radius * 0.4f));
            c.vel = Vector2.Lerp(c.vel, target, 1f - Mathf.Exp(-dt * 10f));
            var p = c.pos + c.vel * dt;
            p.x = Mathf.Clamp(p.x, 1f, TidePool.W - 1f); p.y = Mathf.Clamp(p.y, 1f, TidePool.H - 1f);
            c.pos = p;
        }

        /// AI: move toward where its metabolism earns most; leave drying ground; wander otherwise; biofilm members
        /// hold station near the film's centre.
        void AIChemotaxis(Prokaryote c, float dt)
        {
            float depth = pool.Depth(c.pos);
            Vector2 dir = Vector2.zero;
            if (c.biofilm != null) dir = (c.biofilm.centre - c.pos) * 0.3f;
            else if (depth < 0.02f)
            {
                float best = depth;
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
                float here = Yield(c, c.pos);
                float bestY = here; Vector2 bestD = Vector2.zero;
                float phase = c.wanderSeed + pool.time * 0.05f;
                for (int k = 0; k < 5; k++)
                {
                    float a = phase + k * Mathf.PI * 0.4f;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    float y = Yield(c, c.pos + d * 5f);
                    if (y > bestY * 1.05f) { bestY = y; bestD = d; }
                }
                float t = pool.time * 0.15f;
                float wa = TidePool.Noise(c.wanderSeed + t, c.wanderSeed * 0.7f) * Mathf.PI * 4f;
                dir = bestD * 0.8f + new Vector2(Mathf.Cos(wa), Mathf.Sin(wa)) * 0.35f;
            }
            Steer(c, dir, dt);
        }

        float Yield(Prokaryote c, Vector2 at)
        {
            Environment(at, out float light, out float redox, out float organics, out _);
            float o2 = world.ledger.AmbientO2;
            return c.genome.metabolism switch
            {
                Metabolism.Chemotrophy => 1.2f * redox,
                Metabolism.AnoxygenicPhoto => 0.6f * light,
                Metabolism.OxygenicPhoto => 0.75f * light,
                Metabolism.Respiration => 1.6f * o2 * (0.4f + organics),
                Metabolism.Mixotroph => (0.3f * organics + 0.6f * light) * 0.6f,
                _ => 0.3f * organics,
            };
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
                    }
                }
            }
        }

        // ── GOE callback ─────────────────────────────────────────────────────

        void HandleGoeFired()
        {
            Debug.Log("[CellStage] GOE: world chemistry shifting — rust staining, anaerobe die-off begins.");
            // O₂ is now in the water: intolerant cells start to suffer (read by Prokaryote as prog.oxygenSelfHarm > 0)
            prog.oxygenSelfHarm = Mathf.Max(prog.oxygenSelfHarm, 0.5f);
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        void BuildVisuals()
        {
            soupMat = new Material(Shader.Find("CLAY/CellStage/Soup"));
            soupMat.SetTexture("_Height", pool.heightTex);
            soupMat.SetVector("_ArenaSize", new Vector4(TidePool.W, TidePool.H, 0, 0));
            Stage0Replication.ApplySolventLook(soupMat, world.ctx.solvent);
            Stage0Replication.ApplyPlanetLook(soupMat, world.ctx);
            pool.biome.ApplyLook(soupMat);
            causMat = new Material(Shader.Find("CLAY/CellStage/Caustics"));
            causMat.SetTexture("_Height", pool.heightTex);
            causMat.SetVector("_ArenaSize", new Vector4(TidePool.W, TidePool.H, 0, 0));
            var m = new Mesh { name = "PoolQuad" };
            m.vertices = new[] { new Vector3(0, 0, 1), new Vector3(TidePool.W, 0, 1), new Vector3(TidePool.W, TidePool.H, 1), new Vector3(0, TidePool.H, 1) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            Quad("Soup", soupMat, m, -60);   // the physical water (caustics are part of it)
            moteMat = new Material(Shader.Find("CLAY/CellStage/Mote"));
            blobMat = new Material(Shader.Find("CLAY/CellStage/Membrane"));
            haze = new QuadBatch("EnergyHaze", moteMat, root, CellStageWorld.CellLayer, 61);
            shadows = new QuadBatch("Shadows", moteMat, root, CellStageWorld.CellLayer, 62);
            blobs = new QuadBatch("Prokaryotes", blobMat, root, CellStageWorld.CellLayer, 63);
            insides = new QuadBatch("Insides", moteMat, root, CellStageWorld.CellLayer, 64);
        }

        void Quad(string name, Material mat, Mesh m, int order)
        {
            var go = new GameObject(name) { layer = CellStageWorld.CellLayer };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.sortingOrder = order;
        }

        static Color MetabolismColour(Metabolism m) => m switch
        {
            Metabolism.Fermentation => new Color(0.75f, 0.6f, 0.4f),
            Metabolism.Chemotrophy => new Color(1f, 0.55f, 0.2f),
            Metabolism.AnoxygenicPhoto => new Color(0.75f, 0.35f, 0.65f),   // purple bacteriochlorophyll
            Metabolism.OxygenicPhoto => new Color(0.35f, 0.85f, 0.4f),      // chlorophyll green
            Metabolism.Respiration => new Color(0.4f, 0.7f, 1f),
            Metabolism.Mixotroph => new Color(0.4f, 0.85f, 0.8f),
            _ => new Color(0.8f, 0.8f, 0.75f),
        };

        void Render(float dt)
        {
            float day = pool.Daylight, t = pool.time;
            soupMat.SetFloat("_Tide", pool.Tide); soupMat.SetFloat("_Day", day);
            causMat.SetFloat("_Tide", pool.Tide); causMat.SetFloat("_Day", day);
            blobMat.SetFloat("_Day", day); moteMat.SetFloat("_Day", day);
            goe.BiomeShift?.Apply(soupMat, goe.oxidationProgress);

            Vector2 focus = player != null ? player.pos : camPos;
            float size = (player != null ? 5f + player.Radius * 2.2f : 8f) * zoom;
            if (picking)
            {
                var b = new Bounds(focus, Vector3.zero);
                foreach (var o in options) b.Encapsulate((Vector3)((Prokaryote)o).pos);
                focus = b.center; size = Mathf.Max(10f, Mathf.Max(b.extents.x / world.cam.aspect, b.extents.y) + 5f);
            }
            camPos = Vector2.Lerp(camPos, focus, 1f - Mathf.Exp(-dt * 5f));
            camSize = Mathf.Lerp(camSize, size, 1f - Mathf.Exp(-dt * 3f));
            world.cam.orthographicSize = camSize;
            world.cam.transform.position = new Vector3(camPos.x, camPos.y, -10f);
            Rect view = ViewRect(4f);

            // niches: vent plume, iron seep, biofilm matrices; after the GOE, oxygen bubbles rising everywhere in view
            haze.Clear();
            float pulse = 0.85f + 0.15f * Mathf.Sin(t * 1.3f);
            haze.Add(biome.ventCentre, biome.ventRadius, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(1f, 0.55f, 0.2f, 0.22f * biome.ventProximityStrength * pulse));
            haze.Add(biome.seepCentre, biome.seepRadius, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0.75f, 0.32f, 0.15f, 0.2f * biome.ironSeepStrength));
            foreach (var bf in biofilms)
                if (bf.Established) haze.Add(bf.centre, bf.BoundingRadius() * 1.25f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0.85f, 0.88f, 0.7f, 0.18f + 0.25f * bf.matrixDensity));
            int bubbles = Mathf.RoundToInt(goe.OxyBubbleDensity * 24f);
            for (int i = 0; i < bubbles; i++)
            {
                float ph = Frac(i * 0.173f + t * 0.1f);
                var p = new Vector2(view.xMin + Frac(i * 0.618f) * view.width + Mathf.Sin(t + i) * 0.4f, view.yMin + ph * view.height);
                if (pool.Depth(p) <= 0.003f) continue;
                haze.Add(p, 0.07f + 0.05f * Frac(i * 0.37f), 0f, 1f, 6, new Vector4(1.3f, 0.2f, 0, 0), Vector4.zero, new Color(0.9f, 0.97f, 1f, 0.45f * (1f - ph)));
            }
            haze.Upload();

            shadows.Clear(); blobs.Clear(); insides.Clear();
            Vector2 sh = new Vector2(0.35f, -0.5f);
            foreach (var c in cells)
            {
                float rad = c.Radius;
                if (!view.Overlaps(new Rect(c.pos.x - rad * 2.3f, c.pos.y - rad * 2.3f, rad * 4.6f, rad * 4.6f))) continue;
                float starving = Mathf.Clamp01(c.energy.deficit / ProkaryoteEnergy.DeficitLimit);
                float glow = Mathf.Clamp01(c.energy.surplusRate * 4f) * 0.5f + c.hgtFlash * 0.5f + c.dimpleFlash * 0.8f;
                float stretch = Mathf.Clamp01((c.area / Mathf.Max(c.birthArea, 0.01f) - 1.4f) / 0.4f);
                bool option = picking && options.Contains(c);
                var tint = c.genome.Tint; tint.a = c.dead ? 0f : 1f;
                // archaea: thicker, sturdier membrane; bacteria: thinner and smoother
                float thick = c.genome.membraneThickness + (c.genome.domain == Domain.Archaea ? 0.4f : c.genome.domain == Domain.Bacteria ? -0.15f : 0f);
                blobs.Add(c.pos, rad * 2.3f, 0f, 2.3f, 0f,
                          new Vector4(Mathf.Clamp01(c.oxygenStress * 1.2f), Mathf.Clamp01(glow), starving, stretch * 0.3f),
                          new Vector4(c.wanderSeed, Mathf.Clamp01(thick), c.dead ? Mathf.Max(c.burstFlash, 0f) : 0f, option ? 1f : 0f),
                          c.dead ? new Color(tint.r, tint.g, tint.b, 1f) : tint);
                if (c.dead) continue;
                float lift = Mathf.Clamp01(pool.Depth(c.pos) * 10f);
                shadows.Add(c.pos + sh * (rad * 0.3f + lift * 1.2f), rad * (1.05f + lift * 0.25f), 0f, 1f, 7, new Vector4(1f, 0, 0, 0), Vector4.zero, new Color(0, 0, 0, 0.4f * (1f - lift * 0.4f) * day));
                DrawInside(c, rad, t);
            }
            shadows.Upload(); blobs.Upload(); insides.Upload();
        }

        /// Inside: metabolism machinery as coloured granules (what the cell lives on shows as its colour), and — once
        /// it has happened — the proto-engulf dimple: a small cup pressed into the membrane.
        void DrawInside(Prokaryote c, float rad, float t)
        {
            float ms = Mathf.Clamp(rad * 0.12f, 0.08f, 0.18f);
            var mc = MetabolismColour(c.genome.metabolism); mc.a = 0.9f;
            int n = 5 + Mathf.RoundToInt(Mathf.Clamp01(c.energy.surplusRate * 6f) * 5f);
            for (int i = 0; i < n; i++)
            {
                float a = c.wanderSeed * 3f + i * 2.399f + t * 0.3f;
                float d = rad * (0.2f + 0.45f * Frac(i * 0.61f + c.wanderSeed));
                var p = c.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                insides.Add(p, ms, a, 1f, 6, new Vector4(1f, c.genome.metabolism == Metabolism.OxygenicPhoto ? 0.25f * day(t) : 0f, 0, 0), Vector4.zero, mc);
            }
            if (c.hasProtoEngulf)
            {
                float a = c.wanderSeed + t * 0.05f;
                var dirv = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var col = c.genome.Tint * 0.8f; col.a = 0.95f;
                insides.Add(c.pos + dirv * rad * 0.85f, rad * 0.22f, a + Mathf.PI, 1f, 0, new Vector4(0.9f + c.dimpleFlash, c.dimpleFlash, 0, 0), Vector4.zero, col);
            }
        }

        float day(float _) => pool.Daylight;
        static float Frac(float x) => x - Mathf.Floor(x);

        Rect ViewRect(float pad)
        {
            float hh = world.cam.orthographicSize + pad, hw = hh * world.cam.aspect;
            var c = world.cam.transform.position;
            return new Rect(c.x - hw, c.y - hh, hw * 2f, hh * 2f);
        }

        static readonly CellStageProgress DummyProgress = new();
    }
}
