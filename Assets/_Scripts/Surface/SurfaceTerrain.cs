using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using CLAY.Galaxy;

namespace CLAY.Surface
{
    /// <summary>
    /// Streaming quadtree terrain over a ~1049 km square centred on the landing point. Nodes split while the camera
    /// is closer than SplitK × their size, down to 64 m leaves (≈2 m vertex spacing), so detail concentrates where
    /// you are and the horizon stays cheap. Each tile is a 32×32 grid built on a worker thread (height, normals,
    /// biome colour), with skirts hanging from its edges to hide seams between neighbours at different detail.
    /// Old tiles are only removed once every new tile covering their area is ready — no holes while streaming.
    /// Tile world coordinates are doubles; Unity positions are relative to a floating origin.
    /// </summary>
    public sealed class SurfaceTerrain
    {
        const int Res = 32;
        public const double RootSize = 64.0 * (1 << 14);   // 1,048,576 m
        const int MaxLevel = 14;                            // leaf = 64 m
        const float SplitK = 1.9f;

        readonly SurfaceGeo geo;
        readonly Transform parent;
        readonly Material mat;
        readonly int layer;
        readonly int maxJobs;

        sealed class Chunk
        {
            public int level; public long ix, iz; public double x0, z0, size;
            public GameObject go; public Mesh mesh;
            public Task<Data> task; public bool ready, queued; public int ver, taskVer;
        }
        struct Data { public Vector3[] v, n; public Color32[] c; public Vector4[] w, w2, w3; public int[] t; }

        readonly Dictionary<long, Chunk> chunks = new();
        readonly HashSet<long> desired = new();
        readonly List<Chunk> building = new();
        int running;

        /// Bump to rebuild every tile (keeping the old ones visible until replaced) — e.g. when rivers arrive.
        public int Version;
        public int ChunkCount => chunks.Count;
        public int PendingCount => running;
        public bool AnyReady { get; private set; }
        public int ReadyCount { get; private set; }
        public int Failures { get; private set; }
        public string LastError { get; private set; }

        /// Build one tile synchronously on the main thread (used at landing): surfaces any error with a real
        /// stack trace instead of an AggregateException from a worker, and gives an immediate first tile.
        public void BuildNow(double camX, double camZ)
        {
            int level = MaxLevel - 2;
            double size = RootSize / (1L << level);
            long ix = (long)System.Math.Floor((camX + RootSize * 0.5) / size), iz = (long)System.Math.Floor((camZ + RootSize * 0.5) / size);
            var c = MakeChunk(Key(level, ix, iz));
            Upload(c, Build(geo, c.x0, c.z0, c.size));
            chunks[Key(level, ix, iz)] = c;
        }

        public SurfaceTerrain(SurfaceGeo g, Transform parent, Material mat, int layer)
        {
            geo = g; this.parent = parent; this.mat = mat; this.layer = layer;
            maxJobs = Mathf.Max(2, System.Environment.ProcessorCount - 2);
        }

        static long Key(int level, long ix, long iz) => ((long)level << 42) | ((ix + (1L << 20)) << 21) | (iz + (1L << 20));

        public void Update(double camX, double camY, double camZ, double ox, double oz)
        {
            desired.Clear();
            Collect(0, 0, 0, camX, camY, camZ);

            // queue missing tiles (nearest first) and start jobs
            var todo = new List<Chunk>();
            foreach (var k in desired)
                if (!chunks.TryGetValue(k, out var c)) { c = MakeChunk(k); chunks[k] = c; todo.Add(c); }
                else if (!c.queued && (!c.ready || c.ver != Version)) todo.Add(c);
            todo.Sort((a, b) => DistTo(a, camX, camZ).CompareTo(DistTo(b, camX, camZ)));
            foreach (var c in todo)
            {
                if (running >= maxJobs) break;
                if (c.queued) continue;
                double x0 = c.x0, z0 = c.z0, size = c.size; var g = geo;
                c.task = Task.Run(() => Build(g, x0, z0, size)); c.taskVer = Version;
                c.queued = true; running++; building.Add(c);
            }

            // finish jobs (a few mesh uploads per frame)
            int uploads = 0;
            for (int i = building.Count - 1; i >= 0 && uploads < 6; i--)
            {
                var c = building[i];
                if (!c.task.IsCompleted) continue;
                building.RemoveAt(i); running--;
                if (c.task.IsFaulted)
                {
                    var ex = c.task.Exception?.GetBaseException();
                    Failures++; LastError = ex?.ToString();
                    if (Failures <= 3) Debug.LogException(ex);
                    c.queued = false; continue;
                }
                // dropped meanwhile — or dropped AND re-requested as a new Chunk object: uploading this stale one would
                // create an orphan GameObject that is never repositioned or retired (the tile "glued" to the camera,
                // floating plains over the sea)
                if (!chunks.TryGetValue(Key(c.level, c.ix, c.iz), out var live) || !ReferenceEquals(live, c)) continue;
                try { Upload(c, c.task.Result); uploads++; }
                catch (System.Exception ex) { Failures++; LastError = ex.ToString(); if (Failures <= 3) Debug.LogException(ex); }
            }

            // retire tiles no longer wanted, but only once their area is fully covered by ready tiles
            var retire = new List<long>();
            foreach (var kv in chunks)
            {
                if (desired.Contains(kv.Key)) continue;
                var old = kv.Value;
                if (old.queued && !old.ready) { retire.Add(kv.Key); continue; }   // never shown: drop freely
                bool covered = true;
                foreach (var dk in desired)
                {
                    var d = chunks[dk];
                    if (!d.ready && Overlaps(old, d)) { covered = false; break; }
                }
                if (covered) retire.Add(kv.Key);
            }
            foreach (var k in retire)
            {
                var c = chunks[k];
                if (c.go) Object.Destroy(c.go);
                if (c.mesh) Object.Destroy(c.mesh);
                chunks.Remove(k);
            }

            // Reveal a wanted tile only when nothing it replaces is still on screen. Otherwise an old coarse tile and
            // its finer replacements draw at once, slicing through each other (the "hollow mountain" slabs).
            var leftovers = new List<Chunk>();
            foreach (var kv in chunks) if (!desired.Contains(kv.Key) && kv.Value.go) leftovers.Add(kv.Value);
            foreach (var dk in desired)
            {
                var d = chunks[dk];
                if (!d.go) continue;
                bool blocked = false;
                foreach (var o in leftovers) if (Overlaps(o, d)) { blocked = true; break; }
                if (d.go.activeSelf == blocked) d.go.SetActive(!blocked);
            }

            // place visible tiles relative to the floating origin
            foreach (var c in chunks.Values)
                if (c.go) c.go.transform.localPosition = new Vector3((float)(c.x0 + c.size * 0.5 - ox), 0f, (float)(c.z0 + c.size * 0.5 - oz));
        }

        void Collect(int level, long ix, long iz, double camX, double camY, double camZ)
        {
            double size = RootSize / (1L << level);
            double x0 = -RootSize * 0.5 + ix * size, z0 = -RootSize * 0.5 + iz * size;
            double dx = System.Math.Max(0, System.Math.Max(x0 - camX, camX - (x0 + size)));
            double dz = System.Math.Max(0, System.Math.Max(z0 - camZ, camZ - (z0 + size)));
            double dist = System.Math.Sqrt(dx * dx + dz * dz + camY * camY);
            if (level < MaxLevel && dist < size * SplitK)
            {
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                        Collect(level + 1, ix * 2 + i, iz * 2 + j, camX, camY, camZ);
            }
            else desired.Add(Key(level, ix, iz));
        }

        Chunk MakeChunk(long key)
        {
            int level = (int)(key >> 42);
            long ix = ((key >> 21) & ((1L << 21) - 1)) - (1L << 20);
            long iz = (key & ((1L << 21) - 1)) - (1L << 20);
            double size = RootSize / (1L << level);
            return new Chunk { level = level, ix = ix, iz = iz, size = size, x0 = -RootSize * 0.5 + ix * size, z0 = -RootSize * 0.5 + iz * size };
        }

        static double DistTo(Chunk c, double x, double z)
        {
            double cx = c.x0 + c.size * 0.5 - x, cz = c.z0 + c.size * 0.5 - z;
            return System.Math.Sqrt(cx * cx + cz * cz) - c.size * 0.5;
        }

        static bool Overlaps(Chunk a, Chunk b) =>
            a.x0 < b.x0 + b.size && b.x0 < a.x0 + a.size && a.z0 < b.z0 + b.size && b.z0 < a.z0 + a.size;

        // ── tile construction (worker thread) ──
        static Data Build(SurfaceGeo geo, double x0, double z0, double size)
        {
            int n = Res, w = n + 3;                 // one-sample border on every side for normals
            double step = size / n;
            var sm = new SurfaceGeo.Sample[w * w];
            for (int j = 0; j < w; j++)
                for (int i = 0; i < w; i++)
                    sm[j * w + i] = geo.At(x0 + (i - 1) * step, z0 + (j - 1) * step);

            int vc = (n + 1) * (n + 1), skirt = 4 * (n + 1);
            var v = new Vector3[vc + skirt]; var nm = new Vector3[vc + skirt]; var col = new Color32[vc + skirt]; var mw = new Vector4[vc + skirt]; var mw2 = new Vector4[vc + skirt]; var mw3 = new Vector4[vc + skirt];
            float half = (float)(size * 0.5), st = (float)step;

            // the orbital albedo is the costliest term and varies slowly: evaluate on a coarse grid, interpolate
            const int CS = 4; int cn = n / CS;
            var orb = new Color[(cn + 1) * (cn + 1)];
            for (int j = 0; j <= cn; j++)
                for (int i = 0; i <= cn; i++)
                    orb[j * (cn + 1) + i] = geo.Orbital(sm[(j * CS + 1) * w + (i * CS + 1)], 0.2f);
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    var s = sm[(j + 1) * w + (i + 1)];
                    float hx = sm[(j + 1) * w + (i + 2)].heightM - sm[(j + 1) * w + i].heightM;
                    float hz = sm[(j + 2) * w + (i + 1)].heightM - sm[j * w + (i + 1)].heightM;
                    Vector3 nrm = new Vector3(-hx / (2f * st), 1f, -hz / (2f * st)).normalized;
                    float slope = Mathf.Clamp01((1f - nrm.y) * 3.2f);
                    var lc = geo.ClimateAt(s);
                    int idx = j * (n + 1) + i;
                    v[idx] = new Vector3(i * st - half, s.heightM, j * st - half);
                    nm[idx] = nrm;
                    int ci = Mathf.Min(i / CS, cn - 1), cj = Mathf.Min(j / CS, cn - 1);
                    float fu = (i - ci * CS) / (float)CS, fv = (j - cj * CS) / (float)CS;
                    Color o0 = Color.Lerp(orb[cj * (cn + 1) + ci], orb[cj * (cn + 1) + ci + 1], fu);
                    Color o1 = Color.Lerp(orb[(cj + 1) * (cn + 1) + ci], orb[(cj + 1) * (cn + 1) + ci + 1], fu);
                    col[idx] = geo.Ground(s, lc, slope, Color.Lerp(o0, o1, fv), out mw[idx], out mw2[idx]);
                    mw3[idx] = new Vector4(s.water, s.flowX, s.flowZ, 0f);   // river / lake surface for the shader
                }
            // skirts: copies of the border hanging straight down, hiding cracks against coarser neighbours
            float drop = Mathf.Clamp((float)size * 0.006f, 2f, 150f);   // just enough to hide cracks — tall skirts read as walls
            int si = vc;
            var border = new List<int>(skirt);
            for (int i = 0; i <= n; i++) border.Add(i);                              // south edge (j=0)
            for (int i = 0; i <= n; i++) border.Add(n * (n + 1) + i);                // north edge
            for (int j = 0; j <= n; j++) border.Add(j * (n + 1));                    // west edge
            for (int j = 0; j <= n; j++) border.Add(j * (n + 1) + n);                // east edge
            foreach (int b in border) { v[si] = v[b] - new Vector3(0f, drop, 0f); nm[si] = nm[b]; col[si] = col[b]; mw[si] = mw[b]; mw2[si] = mw2[b]; mw3[si] = mw3[b]; si++; }

            var t = new List<int>(n * n * 6 + skirt * 12);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + (n + 1), d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
            for (int e = 0; e < 4; e++)
                for (int k = 0; k < n; k++)
                {
                    int top0 = border[e * (n + 1) + k], top1 = border[e * (n + 1) + k + 1];
                    int bot0 = vc + e * (n + 1) + k, bot1 = bot0 + 1;
                    t.Add(top0); t.Add(bot0); t.Add(top1); t.Add(top1); t.Add(bot0); t.Add(bot1);
                    t.Add(top0); t.Add(top1); t.Add(bot0); t.Add(top1); t.Add(bot1); t.Add(bot0);   // both faces
                }
            return new Data { v = v, n = nm, c = col, w = mw, w2 = mw2, w3 = mw3, t = t.ToArray() };
        }

        void Upload(Chunk c, Data d)
        {
            var m = new Mesh { name = $"Terrain L{c.level} {c.ix},{c.iz}", indexFormat = IndexFormat.UInt16 };
            m.SetVertices(d.v); m.SetNormals(d.n); m.SetColors(d.c); m.SetUVs(0, d.w); m.SetUVs(1, d.w2); m.SetUVs(2, d.w3); m.SetTriangles(d.t, 0);
            m.RecalculateBounds();
            var b = m.bounds; b.Expand(new Vector3(0f, 60000f, 0f)); m.bounds = b;   // curvature lowers far tiles in the shader
            c.ver = c.taskVer; c.queued = false;
            if (c.go)   // rebuilt in place (new geography): swap the mesh, no gap on screen
            {
                if (c.mesh) Object.Destroy(c.mesh);
                c.go.GetComponent<MeshFilter>().sharedMesh = m; c.mesh = m; c.ready = true;
                return;
            }
            var go = new GameObject(m.name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = c.size <= 4096 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            c.go = go; c.mesh = m; c.ready = true; AnyReady = true; ReadyCount++;
        }

        public void Dispose()
        {
            foreach (var c in chunks.Values) { if (c.go) Object.Destroy(c.go); if (c.mesh) Object.Destroy(c.mesh); }
            chunks.Clear();
        }
    }
}
