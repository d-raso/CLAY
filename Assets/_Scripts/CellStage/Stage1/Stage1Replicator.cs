using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Genetics;
using CLAY.CellStage.Stage0;

namespace CLAY.CellStage.Stage1
{
    /// <summary>
    /// STAGE 1: REPLICATOR (RNA world → LUCA).
    ///
    /// Receives 3 founder genomes from the S0 collapse (FounderCollapse) and seeds a new
    /// population from them. The player's cell is in the lineage they chose at the collapse.
    ///
    /// Goal: evolve a stable membrane + working ribozyme metabolism + integrated genome
    ///       — all simultaneously — to reach the LUCA gate and graduate to S2.
    ///
    /// Loop: Gather (go where your metabolism earns) → Process (ribozymes) → Allocate (upkeep first, then
    ///        ribozymes vs copying by the investBias gene) → Copy (fast copying = sloppy copies) → Select
    ///        (offspring drift toward the source they lived on; die → continue as a living relative).
    ///
    /// Energy lives in the pool, not in particles: sunlit shallows feed light-users, the vent's warm plume feeds
    /// chemotrophs, the rusty iron seep feeds mineral users, and dissolved organics feed fermenters everywhere.
    ///
    /// Discovery-only: no meters, no labels. Everything shown on the body:
    ///   • shimmer    = membrane stress (upkeep underfunded)
    ///   • glow       = ribozyme quality / metabolic output
    ///   • wrinkle    = starvation
    ///   • stretching = a copy nearly funded (about to divide)
    ///   • offspring  = mutations (hue/pattern/glow shifts)
    ///   • markers    = CRISPR-like immunity after viroid clearing (thicker, patterned membrane)
    ///   • LUCA pulse = all three conditions met (integration event)
    /// </summary>
    public sealed class Stage1Replicator : IStageMode
    {
        // ── context ───────────────────────────────────────────────────────────────────────
        CellStageWorld world;
        CellStageProgress prog;
        TidePool pool;
        TidePoolBiomeGenome biome;

        // ── population ────────────────────────────────────────────────────────────────────
        readonly List<Replicant> cells = new();
        Replicant player;
        int playerSpecies;
        DetRng r;
        const int MaxCells = 90;
        readonly float[] scratch = new float[4];

        // ── respawn picker (same as S0) ───────────────────────────────────────────────────
        bool picking; float pickTimer;
        List<ILivingBody> options = new();

        // ── rendering ─────────────────────────────────────────────────────────────────────
        Transform root;
        Material soupMat, causMat, blobMat, moteMat;
        QuadBatch haze, shadows, blobs, insides;
        float camSize = 7f, zoom = 1f;
        Vector2 camPos;

        // ── IStageMode ────────────────────────────────────────────────────────────────────

        public void Begin(CellStageWorld w, Transform parent)
        {
            world = w; prog = w.progress;
            r = new DetRng(DetRng.Hash(w.ctx.poolSeed, 0xA1B2C3D4UL));
            pool = new TidePool(w.ctx);
            biome = TidePoolBiomeGenome.Generate(w.ctx, pool, ref r);

            root = new GameObject("Stage1").transform; root.SetParent(parent, false);
            BuildVisuals();

            // receive founder genomes from the S0 collapse
            var founders = w.progress.founderGenomes;   // set by Stage0 FounderCollapse
            if (founders == null || founders.Count == 0)
            {
                // fallback for a dev entry straight into S1
                founders = new List<Genome> { new Genome { metabolism = Metabolism.Fermentation, copyFidelity = 0.85f } };
            }

            // seed the population: 15 of each founder lineage
            for (int f = 0; f < founders.Count; f++)
            {
                int sid = world.species.Found(founders[f]).id;
                for (int i = 0; i < 15; i++)
                {
                    var g = founders[f].Mutate(ref r, 0.3f);
                    var c = new Replicant(g, pool.RandomWetPoint(ref r, 0.05f), 1.6f) { wanderSeed = r.Range(0f, 100f) };
                    world.species.Born(c, sid);
                    cells.Add(c);
                }
            }

            // the player joins the founder lineage they picked at the S0 collapse, else the one nearest their S0 genome
            int fi = w.progress.playerFounder;
            if (fi < 0 || fi >= founders.Count)
                fi = w.progress.playerSeedGenome != null ? FounderCollapse.AssignPlayerToFounder(w.progress.playerSeedGenome, founders) : 0;
            var pg = founders[fi].Clone();
            player = new Replicant(pg, pool.RandomWetPoint(ref r, 0.06f), 1.6f) { player = true, wanderSeed = r.Range(0f, 100f) };
            playerSpecies = world.species.Found(pg, "player-lineage").id;
            world.species.Born(player, playerSpecies);
            cells.Add(player);
            camPos = player.pos;
        }

        public void Tick(float dt)
        {
            world.captureEscape = false;
            pool.Tick(dt);

            if (!picking && player != null && !player.dead) PlayerControl(dt);

            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.dead) continue;
                if (!c.player || picking) AIChemotaxis(c, dt);
                var (source, strength) = RibozymeMetabolism.SampleSource(c.pos, pool, biome, c.genome.metabolism, scratch);
                c.Tick(dt, source, strength, world.ctx.ReactionRate, ref r, c.player ? prog : DummyProgress);
                if (c.dead) { world.species.Died(c); if (c == player) BeginPicker(); continue; }
                TryDivide(c);
            }
            Collide();

            // remove dead cells after their burst flash
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                var c = cells[i];
                if (c.dead) { c.burstFlash -= dt * 1.5f; if (c.burstFlash <= 0f) cells.RemoveAt(i); }
            }

            // LUCA gate: Replicant reaches Milestone.Heredity on integration; CellStageProgress maps that to S2 and
            // CellStageWorld swaps the mode automatically — nothing to do here.
            // (S0 protocells don't coexist with S1 in the current build, so lipid dissolution of S0 bubbles is moot.)

            if (picking) PickerTick(dt);
            Render(dt);
        }

        public void End()
        {
            if (root) Object.Destroy(root.gameObject);
            if (soupMat) Object.Destroy(soupMat); if (causMat) Object.Destroy(causMat);
            if (blobMat) Object.Destroy(blobMat); if (moteMat) Object.Destroy(moteMat);
            if (pool != null && pool.heightTex) Object.Destroy(pool.heightTex);
        }

        public void DebugGUI()
        {
            GUILayout.Label($"Stage 1 · cells {cells.Count} · tide {pool.Tide:0.00} · day {pool.Daylight:0.00}");
            GUILayout.Label($"vent {biome.ventProximityStrength:0.00} r{biome.ventRadius:0} · seep {biome.ironSeepStrength:0.00} r{biome.seepRadius:0}");
            if (player != null && !player.dead)
            {
                GUILayout.Label($"source {player.lastSource} · energy raw {player.energy.raw:0.00} usable {player.energy.usable:0.00}");
                GUILayout.Label($"ribozyme Q {player.ribozymeQuality:0.000} · memStress {player.membraneStress:0.000} · starving {player.starvation:0.00}");
                GUILayout.Label($"area {player.area:0.00}/{player.birthArea:0.00} · copy {player.copyProgress:0.00} · investBias {player.genome.investBias:0.00}");
                GUILayout.Label($"metabolism {player.genome.metabolism} · LUCA {player.lucaProgress:0.00} · gen {player.generation}");
            }
            if (picking) GUILayout.Label($"PICKING: {options.Count} living relatives");
        }

        // ── movement ──────────────────────────────────────────────────────────────────────

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
            float sc = Input.mouseScrollDelta.y;
            if (Mathf.Abs(sc) > 0.01f) zoom = Mathf.Clamp(zoom * (1f - sc * 0.1f), 0.5f, 3f);
        }

        /// Low-Reynolds movement: follows intent near-instantly, no coasting. Dry ground pins you.
        void Steer(Replicant c, Vector2 dir, float dt)
        {
            float depth = pool.Depth(c.pos);
            float medium = depth <= 0.003f ? 0.04f : depth < 0.04f ? 0.45f : 1f;
            float speed = (1.2f + c.genome.motility * 2f) / (1f + c.Radius * 0.25f) * medium;
            Vector2 target = dir * speed;
            if (depth < 0.04f && depth > 0.003f) target += pool.Downhill(c.pos) * (0.04f - depth) * 40f;
            var f = FlowFieldManager.Instance;
            if (f != null && depth > 0.003f) target += f.SampleFlowAtPosition(c.pos) * (Stage0Replication.FlowCoupling / (1f + c.Radius * 0.4f));
            c.vel = Vector2.Lerp(c.vel, target, 1f - Mathf.Exp(-dt * 10f));
            c.pos += c.vel * dt;
            c.pos.x = Mathf.Clamp(c.pos.x, 1f, TidePool.W - 1f);
            c.pos.y = Mathf.Clamp(c.pos.y, 1f, TidePool.H - 1f);
        }

        /// AI: drift toward wherever this cell's metabolism earns most (sample a ring of directions), get out of
        /// drying ground, and otherwise wander.
        void AIChemotaxis(Replicant c, float dt)
        {
            float depth = pool.Depth(c.pos);
            Vector2 dir = Vector2.zero;
            if (depth < 0.02f)
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
                float here = RibozymeMetabolism.YieldAt(c.pos, pool, biome, c.genome.metabolism, scratch);
                float bestY = here; Vector2 bestD = Vector2.zero;
                float phase = c.wanderSeed + pool.time * 0.05f;
                for (int k = 0; k < 5; k++)
                {
                    float a = phase + k * Mathf.PI * 0.4f;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    float y = RibozymeMetabolism.YieldAt(c.pos + d * 5f, pool, biome, c.genome.metabolism, scratch);
                    if (y > bestY * 1.05f) { bestY = y; bestD = d; }
                }
                float t = pool.time * 0.15f;
                float wa = TidePool.Noise(c.wanderSeed + t, c.wanderSeed * 0.7f) * Mathf.PI * 4f;
                dir = bestD * 0.8f + new Vector2(Mathf.Cos(wa), Mathf.Sin(wa)) * 0.35f;
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
                    }
                }
            }
        }

        // ── division ──────────────────────────────────────────────────────────────────────

        void TryDivide(Replicant c)
        {
            if (!c.ReadyToDivide) return;
            int alive = 0; foreach (var x in cells) if (!x.dead) alive++;
            if (alive >= MaxCells && !c.player) return;           // a crowded pool: AI cells wait (the player never does)
            var child = c.Divide(ref r, c.player ? prog : DummyProgress);
            world.species.Born(child, c.SpeciesId);
            cells.Add(child);
            if (c.SpeciesId == playerSpecies || child.SpeciesId == playerSpecies)
            {
                prog.Reach(Milestone.FirstDivision);
                if (child.ribozymeQuality > 0.5f) prog.Reach(Milestone.InheritedReplicator);
                if (child.genome.copyFidelity >= 0.93f || c.genome.copyFidelity >= 0.93f) prog.Reach(Milestone.CopyFidelity);
            }
        }

        // ── respawn picker ────────────────────────────────────────────────────────────────

        void BeginPicker()
        {
            picking = true; pickTimer = 0f;
            options = world.species.RespawnOptions(playerSpecies);
        }

        void PickerTick(float dt)
        {
            pickTimer += dt;
            options.RemoveAll(o => !o.IsAlive);
            if (options.Count == 0) { if (pickTimer > 3f) SpawnFreshCell(); return; }
            int chosen = -1;
            for (int k = 0; k < Mathf.Min(options.Count, 8); k++) if (Input.GetKeyDown(KeyCode.Alpha1 + k)) chosen = k;
            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mw = world.cam.ScreenToWorldPoint(Input.mousePosition);
                float bestD = 1.5f;
                for (int k = 0; k < options.Count; k++)
                {
                    var o = (Replicant)options[k];
                    float d = (o.pos - mw).magnitude - o.Radius;
                    if (d < bestD) { bestD = d; chosen = k; }
                }
            }
            if (chosen >= 0) Possess((Replicant)options[chosen]);
        }

        void SpawnFreshCell()
        {
            // the whole lineage is gone: a new cell rises from the most successful lineage still alive (or a fresh one)
            Genome g = null;
            int bestCount = 0;
            foreach (var kv in world.species.All) if (kv.Value.living.Count > bestCount) { bestCount = kv.Value.living.Count; g = kv.Value.founder; }
            g = g != null ? g.Mutate(ref r, 1f) : new Genome { metabolism = Metabolism.Fermentation };
            var c = new Replicant(g, pool.RandomWetPoint(ref r, 0.06f), 1.6f) { wanderSeed = r.Range(0f, 100f) };
            playerSpecies = world.species.Found(g, "player-lineage").id;
            world.species.Born(c, playerSpecies);
            cells.Add(c); Possess(c);
        }

        void Possess(Replicant c)
        {
            if (player != null) player.player = false;
            player = c; c.player = true; picking = false;
        }

        // ── rendering ─────────────────────────────────────────────────────────────────────

        void BuildVisuals()
        {
            // the same pool as S0: rock/shallows overlay + caustics over the environment's water
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
            blobs = new QuadBatch("Replicants", blobMat, root, CellStageWorld.CellLayer, 63);
            insides = new QuadBatch("Insides", moteMat, root, CellStageWorld.CellLayer, 64);
        }

        void Quad(string name, Material mat, Mesh m, int order)
        {
            var go = new GameObject(name) { layer = CellStageWorld.CellLayer };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.sortingOrder = order;
        }

        void Render(float dt)
        {
            float day = pool.Daylight, t = pool.time;
            soupMat.SetFloat("_Tide", pool.Tide); soupMat.SetFloat("_Day", day);
            causMat.SetFloat("_Tide", pool.Tide); causMat.SetFloat("_Day", day);
            blobMat.SetFloat("_Day", day); moteMat.SetFloat("_Day", day);

            // camera: follow the player; while choosing a relative, pull back to show the family
            Vector2 focus = player != null ? player.pos : camPos;
            float size = (player != null ? 5.5f + player.Radius * 2.2f : 8f) * zoom;
            if (picking)
            {
                var b = new Bounds(focus, Vector3.zero);
                foreach (var o in options) b.Encapsulate((Vector3)((Replicant)o).pos);
                focus = b.center; size = Mathf.Max(10f, Mathf.Max(b.extents.x / world.cam.aspect, b.extents.y) + 5f);
            }
            camPos = Vector2.Lerp(camPos, focus, 1f - Mathf.Exp(-dt * 5f));
            camSize = Mathf.Lerp(camSize, size, 1f - Mathf.Exp(-dt * 3f));
            world.cam.orthographicSize = camSize;
            world.cam.transform.position = new Vector3(camPos.x, camPos.y, -10f);

            // energy landscape: the vent's warm shimmering plume and the iron seep's rusty bloom (soft glows), with a
            // stream of tiny bubbles rising from the vent
            haze.Clear();
            float pulse = 0.85f + 0.15f * Mathf.Sin(t * 1.3f);
            haze.Add(biome.ventCentre, biome.ventRadius, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, Glow(new Color(1f, 0.55f, 0.2f), 0.22f * biome.ventProximityStrength * pulse));
            haze.Add(biome.seepCentre, biome.seepRadius, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, Glow(new Color(0.75f, 0.32f, 0.15f), 0.2f * biome.ironSeepStrength));
            for (int i = 0; i < 18; i++)
            {
                float ph = Frac(i * 0.137f + t * 0.12f);
                float ang = i * 2.399f;
                var p = biome.ventCentre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * biome.ventRadius * 0.25f * Frac(i * 0.61f) + new Vector2(Mathf.Sin(t + i) * 0.3f, ph * 3f);
                haze.Add(p, 0.08f, 0f, 1f, 6, new Vector4(1.2f, 0.3f, 0, 0), Vector4.zero, new Color(1f, 0.95f, 0.85f, (1f - ph) * 0.5f * biome.ventProximityStrength));
            }
            haze.Upload();

            shadows.Clear(); blobs.Clear(); insides.Clear();
            Vector2 sh = new Vector2(0.35f, -0.5f);
            Rect view = ViewRect(4f);
            foreach (var c in cells)
            {
                float rad = c.Radius;
                if (!view.Overlaps(new Rect(c.pos.x - rad * 2.3f, c.pos.y - rad * 2.3f, rad * 4.6f, rad * 4.6f))) continue;
                float glow = Mathf.Clamp01((c.ribozymeQuality - 0.3f) / 0.5f) * 0.7f;
                glow = Mathf.Max(glow, c.lucaProgress * (0.6f + 0.4f * Mathf.Sin(t * 3f + c.wanderSeed)));   // the LUCA pulse
                float stretch = Mathf.Clamp01(c.copyProgress) * Mathf.Clamp01((c.area / Mathf.Max(c.birthArea, 0.01f) - 1.4f) / 0.4f);
                bool option = picking && options.Contains(c);
                var tint = c.genome.Tint; tint.a = c.dead ? 0f : 1f;
                blobs.Add(c.pos, rad * 2.3f, 0f, 2.3f, 0f,
                          new Vector4(Mathf.Clamp01(c.membraneStress * 1.3f), glow, c.starvation, stretch * 0.3f),
                          new Vector4(c.wanderSeed, c.genome.membraneThickness + (c.immunized ? 0.35f * c.immunityStrength : 0f), c.dead ? Mathf.Max(c.burstFlash, 0f) : 0f, option ? 1f : 0f),
                          c.dead ? new Color(tint.r, tint.g, tint.b, 1f) : tint);
                if (c.dead) continue;
                float lift = Mathf.Clamp01(pool.Depth(c.pos) * 10f);
                shadows.Add(c.pos + sh * (rad * 0.3f + lift * 1.2f), rad * (1.05f + lift * 0.25f), 0f, 1f, 7, new Vector4(1f, 0, 0, 0), Vector4.zero, new Color(0, 0, 0, 0.4f * (1f - lift * 0.4f) * day));
                DrawInside(c, rad, t);
            }
            shadows.Upload(); blobs.Upload(); insides.Upload();
        }

        /// Inside: the genome strand (bases on a backbone) and a few ribozyme beads whose glow IS the catalyst quality.
        void DrawInside(Replicant c, float rad, float t)
        {
            var ctx = world.ctx;
            float ms = Mathf.Clamp(rad * 0.1f, 0.12f, 0.22f);
            var seq = c.genome.replicator;
            float baseA = c.wanderSeed + t * 0.1f;
            Vector2 start = c.pos + new Vector2(Mathf.Cos(baseA), Mathf.Sin(baseA)) * rad * 0.2f;
            Vector2 prev = Vector2.zero;
            int n = Mathf.Min(seq.Count, 12);
            for (int i = 0; i < n; i++)
            {
                float a = baseA + i * 0.35f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var p = start + new Vector2(-dir.y, dir.x) * (i * ms * 1.9f) - new Vector2(-dir.y, dir.x) * (n * ms * 0.9f);
                if (i > 0) { Vector2 mid = (p + prev) * 0.5f, seg = p - prev; insides.Add(mid, seg.magnitude * 0.5f, Mathf.Atan2(seg.y, seg.x), 1f, 8, new Vector4(1f, 0f, 0, 0), Vector4.zero, new Color(0.9f, 0.92f, 0.85f, 0.8f)); }
                prev = p;
                var col = MoteChem.Color((MoteKind)seq[i], ctx.solvent); col.a = 1f;
                insides.Add(p, ms, a, 1f, seq[i], new Vector4(1f, 0f, 0, 0), Vector4.zero, col);
            }
            int beads = Mathf.RoundToInt(c.ribozymeQuality * 6f);
            for (int i = 0; i < beads; i++)
            {
                float a = c.wanderSeed * 3f + i * 1.7f + t * 0.4f;
                var p = c.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad * (0.45f + 0.15f * Mathf.Sin(t + i));
                insides.Add(p, ms * 0.9f, a, 1f, 6, new Vector4(1f, c.ribozymeQuality * 0.8f, 0, 0), Vector4.zero, new Color(0.95f, 0.85f, 0.55f, 0.9f));
            }
        }

        static Color Glow(Color c, float a) { c.a = a; return c; }
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
