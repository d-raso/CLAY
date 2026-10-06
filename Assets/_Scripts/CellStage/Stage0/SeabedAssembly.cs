using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// HANDS-ON CHEMISTRY (CellStage_Decisions §10)
    ///
    /// When your bubble is stranded (or half-stranded) on a shore, scroll all the way in: the view settles on your
    /// cell and the mineral floor just below it, and the cell's REAL contents become touchable:
    ///  • drag any loose molecule inside the bubble;
    ///  • drop it on the end of a chain to extend it, or onto another loose base to start a new chain;
    ///  • click a bond to break a chain there; drag a base out of a chain to pull it off (a middle base splits it);
    ///  • drop bases on the mineral floor below and line them up — after a moment on the mineral they bond (minerals
    ///    really do catalyse RNA linking; clay best of all). A bonded row that IS a replicator lifts into the cell.
    ///
    /// Still discovery-only (no labels): chains flicker faintly when they match part of a working sequence —
    /// brighter the closer they are (hot / cold) — and glow fully when they are replicators.
    ///
    /// The floor type comes from the pool's chemistry: clay (wide, fast bonding), carbonate (average), basalt
    /// (cramped, slow), pyrite (one row, strict snap). Exit: scroll out, or Esc.
    /// </summary>
    public sealed class SeabedAssembly
    {
        public enum Surface { Clay, Carbonate, Basalt, Pyrite }

        // ── state ──────────────────────────────────────────────────────────────────────────
        public bool Active { get; private set; }
        public Surface surface;

        Protocell owner;
        readonly TidePool pool;
        readonly List<int>[] motifs;
        readonly CellStageWorld world;

        sealed class Placed { public int kind; public Vector2 pos; public float age; public float glow; }
        sealed class Loose { public int kind; public Vector2 off; public float seed; }   // a loose molecule inside the cell
        readonly List<Placed> placed = new();
        readonly List<Loose> loose = new();
        Placed dragging;                 // being moved by the mouse (null if none)
        Vector2 pressAt; bool pressMoved;
        float promoteTimer, lostShore;
        float exitTimer = -1f;
        DetRng r;

        // layout
        Vector2 anchor;
        float plateW, plateH, bondTime, snapRadius;
        int maxRows;
        const float Spacing = 0.2f;      // bonded bases on the mineral floor
        const float InSpacing = 0.15f;   // bases in a chain inside the cell
        const float BaseSize = 0.085f;
        const float InSize = 0.065f;
        const float Grab = 0.15f;

        // ── public API ─────────────────────────────────────────────────────────────────────
        public SeabedAssembly(Protocell owner, TidePool pool, List<int>[] motifs, CellStageWorld world)
        {
            this.owner = owner; this.pool = pool; this.motifs = motifs; this.world = world;
            var c = world.ctx;
            surface = pool.biome != null ? pool.biome.surface : c.minerals > 0.65f ? Surface.Clay
                    : c.ventActivity > 0.5f ? Surface.Basalt
                    : c.redox < 0.2f ? Surface.Pyrite
                    : Surface.Carbonate;
            r = new DetRng(DetRng.Hash(c.poolSeed, 0x5EABEDUL));
        }

        public void SetOwner(Protocell p) { if (Active) Exit(); owner = p; }

        /// Only when the bubble is stranded or half-stranded on a shore: sitting on dry ground, or in the thin film with
        /// dry rock near its rim. (A drying shoreline is where minerals catalyse linking — and where you're stuck.)
        public bool CanEnter
        {
            get
            {
                if (owner == null || owner.dead) return false;
                float d = pool.Depth(owner.pos);
                if (d <= 0.003f) return true;
                if (d > 0.06f) return false;
                float rr = owner.Radius * 1.7f;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI * 0.25f;
                    if (pool.Depth(owner.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr) <= 0.003f) return true;
                }
                return false;
            }
        }

        public void Tick(float dt)
        {
            if (!Active) return;
            if (owner == null || owner.dead) { Exit(); return; }
            float sc = Input.mouseScrollDelta.y;
            if (Input.GetKeyDown(KeyCode.Escape) || sc < -0.01f) { Exit(); return; }

            // the shore flooded (or you drifted off it): the hands-on view closes
            if (!CanEnter) { lostShore += dt; if (lostShore > 0.4f) { Exit(); return; } } else lostShore = 0f;
            UpdateCameraLerp(dt);
            if (exitTimer >= 0f) { exitTimer -= dt; if (exitTimer < 0f) Exit(); return; }
            SyncLoose();
            HandleMouse();
            foreach (var p in placed) p.age += dt;
            UpdatePlateGlow(dt);
            TryPromoteToInternalChain(dt);
        }

        public void Enter()
        {
            if (Active || !CanEnter) return;
            Active = true; exitTimer = -1f; promoteTimer = 0f;
            anchor = owner.pos;
            loose.Clear();
            switch (surface)
            {
                case Surface.Clay:      plateW = 2.4f; plateH = 0.9f; bondTime = 0.4f; snapRadius = 0.14f; maxRows = 3; break;
                case Surface.Basalt:    plateW = 1.5f; plateH = 0.6f; bondTime = 1.4f; snapRadius = 0.1f;  maxRows = 2; break;
                case Surface.Pyrite:    plateW = 2.2f; plateH = 0.3f; bondTime = 0.8f; snapRadius = 0.07f; maxRows = 1; break;
                default:                plateW = 2.0f; plateH = 0.8f; bondTime = 0.9f; snapRadius = 0.11f; maxRows = 3; break;
            }
        }

        /// Exit: every base on the floor or in hand goes back into the bubble (loose).
        public void Exit()
        {
            if (!Active) return;
            Active = false;
            if (owner != null && !owner.dead)
            {
                foreach (var p in placed) owner.free[p.kind]++;
                if (dragging != null) owner.free[dragging.kind]++;
            }
            placed.Clear(); dragging = null; loose.Clear();
        }

        // ── layout ─────────────────────────────────────────────────────────────────────────
        Vector2 Cell => owner.pos;
        float R => owner.Radius;
        Vector2 PlateCentre => anchor + new Vector2(0f, -(R + 0.25f + plateH * 0.5f));
        Rect PlateRect => new Rect(PlateCentre.x - plateW * 0.5f, PlateCentre.y - plateH * 0.5f, plateW, plateH);
        Vector2 MouseWorld() => world.cam.ScreenToWorldPoint(Input.mousePosition);

        /// Where base j of chain i sits inside the cell. Chains are rows stacked from the top of the bubble down; each
        /// row starts at the left edge of the membrane at that height (left-anchored, so pulling a base off the end
        /// never shifts the rest), and the spacing shrinks so even a long chain fits inside the chord.
        float RowY(int i) => R * 0.62f - i * Mathf.Min(0.2f, R * 0.22f);
        float RowSpacing(int i)
        {
            float y = RowY(i); float half = Mathf.Sqrt(Mathf.Max(R * R * 0.78f - y * y, 0.01f));
            int n = Mathf.Max(owner.chains[i].seq.Count + 1, 2);
            return Mathf.Min(InSpacing, half * 2f / n);
        }
        Vector2 ChainPos(int i, int j)
        {
            float y = RowY(i);
            float half = Mathf.Sqrt(Mathf.Max(R * R * 0.78f - y * y, 0.01f));
            return Cell + new Vector2(-half + RowSpacing(i) * (j + 0.5f), y);
        }
        float SnapR => 0.12f;                // forgiving: you don't have to land it exactly
        bool InCell(Vector2 p) => (p - Cell).sqrMagnitude < R * R * 0.92f;

        void UpdateCameraLerp(float dt)
        {
            var cam = world.cam;
            float top = R + 0.3f, bottom = R + 0.35f + plateH;
            float size = Mathf.Max(1.5f, (top + bottom) * 0.5f + 0.35f);
            Vector2 centre = anchor + new Vector2(0f, (top - bottom) * 0.5f);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, 1f - Mathf.Exp(-dt * 4f));
            cam.transform.position = Vector3.Lerp(cam.transform.position, new Vector3(centre.x, centre.y, -10f), 1f - Mathf.Exp(-dt * 4f));
        }

        /// Keep a stable on-screen position for each loose molecule the cell holds (positions persist while you work).
        void SyncLoose()
        {
            int[] have = new int[4];
            foreach (var l in loose) have[l.kind]++;
            for (int k = 0; k < 4; k++)
            {
                while (have[k] < owner.free[k])
                {
                    float a = r.Range(0f, Mathf.PI * 2f), d = Mathf.Sqrt(r.Value) * 0.75f;
                    var off = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f - 0.35f) * d * R;   // the lower part of the cell
                    loose.Add(new Loose { kind = k, off = off, seed = r.Range(0f, 100f) });
                    have[k]++;
                }
                for (int i = loose.Count - 1; i >= 0 && have[k] > owner.free[k]; i--)
                    if (loose[i].kind == k) { loose.RemoveAt(i); have[k]--; }
            }
        }

        // ── interaction ────────────────────────────────────────────────────────────────────
        void HandleMouse()
        {
            Vector2 m = MouseWorld();
            if (Input.GetMouseButtonDown(0) && dragging == null)
            {
                pressAt = m; pressMoved = false;
                dragging = PickUp(m);
            }
            if (Input.GetMouseButton(0) && (m - pressAt).sqrMagnitude > 0.03f * 0.03f) pressMoved = true;
            if (dragging != null)
            {
                // smooth, and pulled toward the spot it would snap into (you see where it will land before you let go)
                var target = m;
                if (ReplacePreview(m, out var rs)) target = Vector2.Lerp(m, rs, 0.75f);
                else if (SnapPreview(m, out var ss, out _)) target = Vector2.Lerp(m, ss, 0.75f);
                dragging.pos = Vector2.Lerp(dragging.pos, target, 1f - Mathf.Exp(-Time.deltaTime * 22f));
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (dragging != null) { Place(dragging, m); dragging = null; }
                else if (!pressMoved) TryBreakBondAt(m);     // a click on a bond breaks the chain there
            }
        }

        /// Pick up whatever is under the cursor: a base on the floor, a base in a chain (pulled off / splits the
        /// chain), or a loose molecule.
        Placed PickUp(Vector2 m)
        {
            for (int i = placed.Count - 1; i >= 0; i--)
                if ((placed[i].pos - m).magnitude < Grab) { var p = placed[i]; placed.RemoveAt(i); p.age = 0f; return p; }
            for (int i = 0; i < owner.chains.Count; i++)
            {
                var ch = owner.chains[i];
                for (int j = 0; j < ch.seq.Count; j++)
                    if ((ChainPos(i, j) - m).magnitude < Grab * 0.8f)
                    {
                        int kind = ch.seq[j];
                        RemoveFromChain(i, j);
                        return new Placed { kind = kind, pos = m };
                    }
            }
            for (int i = loose.Count - 1; i >= 0; i--)
                if ((Cell + loose[i].off - m).magnitude < Grab)
                {
                    int kind = loose[i].kind;
                    loose.RemoveAt(i);
                    owner.free[kind]--;
                    return new Placed { kind = kind, pos = m };
                }
            return null;
        }

        /// Drop: on the floor → placed; on a chain's end → extends it; onto a loose base → a new two-base chain;
        /// anywhere else in (or out of) the cell → loose again.
        void Place(Placed d, Vector2 m)
        {
            // dropped right on top of a base in a chain → replace it (the old one goes loose)
            for (int i = 0; i < owner.chains.Count; i++)
            {
                var ch = owner.chains[i];
                for (int j = 0; j < ch.seq.Count; j++)
                    if ((ChainPos(i, j) - m).magnitude < SnapR * 0.8f)
                    {
                        int old = ch.seq[j];
                        Edit(ch); ch.seq[j] = d.kind; Changed(ch);
                        owner.free[old]++;
                        loose.Add(new Loose { kind = old, off = Vector2.down * R * 0.35f + new Vector2(r.Range(-0.3f, 0.3f), 0f) * R, seed = r.Range(0f, 100f) });
                        return;
                    }
            }
            // …or on top of a base on the mineral floor → swap it there
            for (int i = 0; i < placed.Count; i++)
                if ((placed[i].pos - m).magnitude < SnapR * 0.8f)
                {
                    int old = placed[i].kind;
                    placed[i].kind = d.kind; placed[i].age = 0f;
                    owner.free[old]++;
                    loose.Add(new Loose { kind = old, off = Vector2.down * R * 0.35f, seed = r.Range(0f, 100f) });
                    return;
                }
            if (PlateRect.Contains(m)) { DropOnFloor(d); return; }
            // a chain end?
            for (int i = 0; i < owner.chains.Count; i++)
            {
                var ch = owner.chains[i];
                if (ch.seq.Count >= Protocell.MaxChain) continue;
                Vector2 left = ChainPos(i, 0) - new Vector2(RowSpacing(i), 0f);
                Vector2 right = ChainPos(i, ch.seq.Count - 1) + new Vector2(RowSpacing(i), 0f);
                if ((left - m).magnitude < SnapR) { Edit(ch); ch.seq.Insert(0, d.kind); Changed(ch); return; }
                if ((right - m).magnitude < SnapR) { Edit(ch); ch.seq.Add(d.kind); Changed(ch); return; }
            }
            // another loose base → a new chain of two
            for (int i = loose.Count - 1; i >= 0; i--)
                if ((Cell + loose[i].off - m).magnitude < SnapR)
                {
                    int other = loose[i].kind;
                    loose.RemoveAt(i); owner.free[other]--;
                    var nc = new Chain(); nc.seq.Add(other); nc.seq.Add(d.kind);
                    owner.chains.Add(nc); Changed(nc);
                    return;
                }
            // otherwise: loose in the cell (where you dropped it, if inside)
            owner.free[d.kind]++;
            loose.Add(new Loose { kind = d.kind, off = InCell(m) ? m - Cell : Vector2.down * R * 0.4f, seed = r.Range(0f, 100f) });
        }

        /// Where a base held at `m` would attach if dropped now (chain end or a loose partner), and what it bonds to.
        bool ReplacePreview(Vector2 m, out Vector2 spot)
        {
            spot = Vector2.zero;
            for (int i = 0; i < owner.chains.Count; i++)
                for (int j = 0; j < owner.chains[i].seq.Count; j++)
                    if ((ChainPos(i, j) - m).magnitude < SnapR * 0.8f) { spot = ChainPos(i, j); return true; }
            foreach (var p in placed) if ((p.pos - m).magnitude < SnapR * 0.8f) { spot = p.pos; return true; }
            return false;
        }

        bool SnapPreview(Vector2 m, out Vector2 spot, out Vector2 from)
        {
            spot = from = Vector2.zero;
            for (int i = 0; i < owner.chains.Count; i++)
            {
                var ch = owner.chains[i];
                if (ch.seq.Count >= Protocell.MaxChain) continue;
                float sp = RowSpacing(i);
                Vector2 first = ChainPos(i, 0), last = ChainPos(i, ch.seq.Count - 1);
                if ((first - new Vector2(sp, 0f) - m).magnitude < SnapR * 1.6f) { spot = first - new Vector2(sp, 0f); from = first; return true; }
                if ((last + new Vector2(sp, 0f) - m).magnitude < SnapR * 1.6f) { spot = last + new Vector2(sp, 0f); from = last; return true; }
            }
            foreach (var l in loose)
            {
                Vector2 lp = Cell + l.off;
                if ((lp - m).magnitude < SnapR * 1.6f) { spot = lp + new Vector2(InSpacing, 0f); from = lp; return true; }
            }
            return false;
        }

        /// Any edit releases a half-built copy (its bases go back to loose).
        void Edit(Chain ch)
        {
            foreach (int b in ch.copy) owner.free[b]++;
            ch.copy.Clear(); ch.copyTimer = 0f; ch.stalled = false;
        }

        void Changed(Chain ch)
        {
            Protocell.Classify(ch, motifs);
            if (owner.player) CellMusic.Instance?.Snap(ch.replicator);
            ch.sinceDock = 0f;
            if (owner.player && ch.seq.Count >= 3) world.progress.Reach(Milestone.FirstChain);
        }

        /// Pull base j off chain i: an end shortens it; a middle base splits it in two. Leftover singles go loose.
        void RemoveFromChain(int i, int j)
        {
            var ch = owner.chains[i];
            Edit(ch);
            var right = ch.seq.GetRange(j + 1, ch.seq.Count - j - 1);
            ch.seq.RemoveRange(j, ch.seq.Count - j);
            if (right.Count >= 2) { var nc = new Chain(); nc.seq.AddRange(right); owner.chains.Add(nc); Changed(nc); }
            else foreach (int b in right) owner.free[b]++;
            if (ch.seq.Count <= 1) { foreach (int b in ch.seq) owner.free[b]++; owner.chains.RemoveAt(i); }
            else Changed(ch);
        }

        /// A click on a bond (between two bases of a chain inside the cell) splits the chain there.
        void TryBreakBondAt(Vector2 m)
        {
            for (int i = 0; i < owner.chains.Count; i++)
            {
                var ch = owner.chains[i];
                for (int j = 0; j + 1 < ch.seq.Count; j++)
                {
                    Vector2 mid = (ChainPos(i, j) + ChainPos(i, j + 1)) * 0.5f;
                    if ((mid - m).magnitude > 0.05f) continue;
                    Edit(ch);
                    var right = ch.seq.GetRange(j + 1, ch.seq.Count - j - 1);
                    ch.seq.RemoveRange(j + 1, ch.seq.Count - j - 1);
                    if (right.Count >= 2) { var nc = new Chain(); nc.seq.AddRange(right); owner.chains.Add(nc); Changed(nc); }
                    else foreach (int b in right) owner.free[b]++;
                    if (ch.seq.Count <= 1) { foreach (int b in ch.seq) owner.free[b]++; owner.chains.RemoveAt(i); }
                    else Changed(ch);
                    return;
                }
            }
        }

        // ── the mineral floor ──────────────────────────────────────────────────────────────
        void DropOnFloor(Placed d)
        {
            // snap onto the free end of a row if close enough; otherwise start a new row on a row line
            Vector2 best = Vector2.zero; float bestD = snapRadius;
            bool snapped = false;
            foreach (var p in placed)
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 spot = p.pos + new Vector2(side * Spacing, 0f);
                    if (Occupied(spot) || !PlateRect.Contains(spot)) continue;
                    float dd = (spot - d.pos).magnitude;
                    if (dd < bestD) { bestD = dd; best = spot; snapped = true; }
                }
            if (snapped) d.pos = best;
            else
            {
                float rowStep = plateH / maxRows;
                int row = Mathf.Clamp(Mathf.FloorToInt((d.pos.y - PlateRect.yMin) / rowStep), 0, maxRows - 1);
                d.pos.y = PlateRect.yMin + rowStep * (row + 0.5f);
                if (Occupied(d.pos)) { owner.free[d.kind]++; return; }
            }
            d.age = 0f; d.glow = 0f;
            placed.Add(d);
        }

        bool Occupied(Vector2 spot)
        {
            foreach (var p in placed) if ((p.pos - spot).sqrMagnitude < (Spacing * 0.6f) * (Spacing * 0.6f)) return true;
            return false;
        }

        /// Rows of bonded bases on the floor (left to right), bonded once both neighbours sat bondTime on the mineral.
        List<List<Placed>> FloorChains()
        {
            var sorted = new List<Placed>(placed);
            sorted.Sort((a, b) => Mathf.Abs(a.pos.y - b.pos.y) > 0.02f ? a.pos.y.CompareTo(b.pos.y) : a.pos.x.CompareTo(b.pos.x));
            var res = new List<List<Placed>>();
            List<Placed> cur = null;
            foreach (var p in sorted)
            {
                bool joins = cur != null
                             && Mathf.Abs(cur[cur.Count - 1].pos.y - p.pos.y) < 0.02f
                             && Mathf.Abs(p.pos.x - cur[cur.Count - 1].pos.x - Spacing) < 0.03f
                             && p.age >= bondTime && cur[cur.Count - 1].age >= bondTime;
                if (joins) cur.Add(p);
                else { cur = new List<Placed> { p }; res.Add(cur); }
            }
            return res;
        }

        static List<int> Seq(List<Placed> chain) { var s = new List<int>(chain.Count); foreach (var p in chain) s.Add(p.kind); return s; }

        /// How close a sequence is to any replicator motif (longest in-order run, either strand sense), 1 = it IS one.
        float MotifMatch(List<int> seq)
        {
            float best = 0f;
            foreach (var m in motifs)
                for (int sense = 0; sense < 2; sense++)
                    for (int ms = 0; ms < m.Count; ms++)
                        for (int ss = 0; ss < seq.Count; ss++)
                        {
                            int n = 0;
                            while (ms + n < m.Count && ss + n < seq.Count
                                   && seq[ss + n] == (sense == 0 ? m[ms + n] : MoteChem.Complement(m[ms + n]))) n++;
                            float v = n / (float)m.Count;
                            if (n == m.Count && seq.Count != m.Count) v = 0.85f;     // the recipe plus extras: close, but it won't fold
                            best = Mathf.Max(best, v);
                        }
            return best;
        }

        static float HotCold(float match) => match >= 0.999f ? 1f : match >= 0.5f ? match * match * 0.45f : 0f;

        void UpdatePlateGlow(float dt)
        {
            foreach (var chain in FloorChains())
            {
                float target = chain.Count >= 2 ? HotCold(MotifMatch(Seq(chain))) : 0f;
                foreach (var p in chain) p.glow = Mathf.MoveTowards(p.glow, target, dt * 1.5f);
            }
        }

        /// A bonded floor row that IS a replicator, held glowing for a moment, lifts into the bubble as a living chain.
        void TryPromoteToInternalChain(float dt)
        {
            if (dragging != null) { promoteTimer = 0f; return; }
            List<Placed> rep = null;
            foreach (var chain in FloorChains())
                if (chain.Count >= 3 && chain[0].glow > 0.95f)
                {
                    var c = new Chain(); c.seq.AddRange(Seq(chain));
                    Protocell.Classify(c, motifs);
                    if (c.replicator) { rep = chain; break; }
                }
            if (rep == null) { promoteTimer = 0f; return; }
            promoteTimer += dt;
            if (promoteTimer < 1.2f) return;

            var ch = new Chain(); ch.seq.AddRange(Seq(rep));
            Protocell.Classify(ch, motifs);
            ch.glow = 1f; ch.sinceDock = 0f;
            owner.chains.Add(ch);
            foreach (var p in rep) placed.Remove(p);
            owner.absorbFlash = 1f;
            if (owner.player) world.progress.Reach(Milestone.FirstChain);
            promoteTimer = 0f;
        }

        // ── rendering ──────────────────────────────────────────────────────────────────────
        /// Draws the work area on the floor, the cell's contents (interactive layout), floor bases and the one in hand.
        public void Render(QuadBatch batch, Color[] baseColours, float time)
        {
            if (!Active) return;
            Color plateC = surface switch
            {
                Surface.Clay => new Color(0.62f, 0.55f, 0.46f, 1f),
                Surface.Basalt => new Color(0.3f, 0.3f, 0.33f, 1f),
                Surface.Pyrite => new Color(0.72f, 0.64f, 0.36f, 1f),
                _ => new Color(0.78f, 0.76f, 0.7f, 1f),
            };
            // a faint pool of mineral-tinted light on the floor marks where you can place
            float half = Mathf.Max(plateW, plateH * 1.6f) * 0.62f;
            var lightC = Color.Lerp(plateC, new Color(1f, 0.95f, 0.85f), 0.4f); lightC.a = 0.14f;
            batch.Add(PlateCentre, half, 0f, 1f, 7, new Vector4(1f, 0f, 0f, 0f), Vector4.zero, lightC);

            // inside the cell: chains as rows (bonds + bases), glowing hot/cold; loose molecules below them
            for (int i = 0; i < owner.chains.Count; i++)
            {
                var ch = owner.chains[i];
                float g = ch.replicator ? 0.75f + 0.25f * Mathf.Sin(time * 4f) : HotCold(MotifMatch(ch.seq));
                for (int j = 0; j < ch.seq.Count; j++)
                {
                    var p = ChainPos(i, j);
                    if (g > 0.05f)
                    {
                        float hs = ch.replicator ? RowSpacing(i) * 1.6f : RowSpacing(i) * 1.1f;
                        batch.Add(p, hs, 0f, 1f, 7, new Vector4(1f, 0f, 0f, 0f), Vector4.zero, new Color(1f, 0.85f, 0.4f, (ch.replicator ? 0.55f : 0.35f) * g));
                    }
                    float sp = RowSpacing(i), size = Mathf.Min(InSize, sp * 0.45f);
                    if (j > 0)
                    {
                        var q = ChainPos(i, j - 1);
                        batch.Add((p + q) * 0.5f, sp * 0.5f, 0f, 1f, 8, new Vector4(1f, g * 0.6f, 0f, 0f), Vector4.zero, new Color(0.92f, 0.94f, 0.88f, 0.85f));
                    }
                    var c = baseColours[ch.seq[j]]; c.a = 1f;
                    batch.Add(p, size, Mathf.PI * 0.5f, 1f, ch.seq[j], new Vector4(1f, g, 0f, 0f), Vector4.zero, c);
                }
            }
            foreach (var l in loose)
            {
                var p = Cell + l.off + new Vector2(Mathf.Sin(time * 0.7f + l.seed), Mathf.Cos(time * 0.6f + l.seed * 1.3f)) * 0.012f;
                var c = baseColours[l.kind]; c.a = 0.95f;
                batch.Add(p, InSize, l.seed + time * 0.2f, 1f, l.kind, new Vector4(0.95f, 0f, 0f, 0f), Vector4.zero, c);
            }

            // the mineral floor: bonds + placed bases
            foreach (var chain in FloorChains())
                for (int i = 1; i < chain.Count; i++)
                {
                    Vector2 a = chain[i - 1].pos, b = chain[i].pos;
                    float g = Mathf.Max(chain[i].glow, chain[i - 1].glow);
                    batch.Add((a + b) * 0.5f, (b - a).magnitude * 0.5f, 0f, 1f, 8, new Vector4(1f, g * 0.6f, 0f, 0f), Vector4.zero, new Color(0.92f, 0.94f, 0.88f, 0.85f));
                }
            foreach (var p in placed)
            {
                var c = baseColours[p.kind]; c.a = 1f;
                float pulse = p.glow > 0.95f ? 0.25f * (0.5f + 0.5f * Mathf.Sin(time * 5f)) : 0f;
                float settling = Mathf.Clamp01(p.age / Mathf.Max(bondTime, 0.01f));
                batch.Add(p.pos, BaseSize, Mathf.PI * 0.5f, 1f, p.kind, new Vector4(0.75f + 0.25f * settling, p.glow + pulse, 0f, 0f), Vector4.zero, c);
            }
            if (dragging != null && ReplacePreview(dragging.pos, out var rspot))
            {
                // replace preview: a pulsing ring around the base that would be swapped out
                var rc = baseColours[dragging.kind]; rc.a = 0.55f + 0.2f * Mathf.Sin(time * 9f);
                batch.Add(rspot, InSize * 1.9f, 0f, 1f, 6, new Vector4(1.4f, 0.6f, 0f, 0f), Vector4.zero, rc);
            }
            else if (dragging != null && SnapPreview(dragging.pos, out var spot, out var from))
            {
                // preview: a faint bond reaching to where the base would attach, and a ghost of it seated there
                var gc = baseColours[dragging.kind]; gc.a = 0.35f + 0.15f * Mathf.Sin(time * 8f);
                batch.Add((spot + from) * 0.5f, (spot - from).magnitude * 0.5f, Mathf.Atan2(spot.y - from.y, spot.x - from.x), 1f, 8, new Vector4(1f, 0.3f, 0f, 0f), Vector4.zero, new Color(0.95f, 0.97f, 0.9f, 0.45f));
                batch.Add(spot, InSize, Mathf.PI * 0.5f, 1f, dragging.kind, new Vector4(1.2f, 0.3f, 0f, 0f), Vector4.zero, gc);
            }
            if (dragging != null)
            {
                var c = baseColours[dragging.kind]; c.a = 0.9f;
                batch.Add(dragging.pos, BaseSize * 1.15f, Mathf.PI * 0.5f, 1f, dragging.kind, new Vector4(1.15f, 0f, 0f, 0f), Vector4.zero, c);
            }
        }
    }
}
