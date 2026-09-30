using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using CLAY.Galaxy;

namespace CLAY.Surface
{
    /// <summary>
    /// Rocks: pebbles, rocks and boulders scattered over the ground.
    /// • 10 procedural shapes: noise-displaced lumps, squashed/stretched, flat-bottomed, FLAT-SHADED so facets catch
    ///   the light, coloured from the planet's own chemistry palette (bedrock suites, dark basalt, pale weathered
    ///   stone, the world's accent mineral) with lighter weathered tops.
    /// • Three size classes on separate cell grids & draw distances (many tiny pebbles close by, a few big boulders
    ///   visible far away); sizes follow a power law within each class.
    /// • Density follows the landscape: mountains, crater fields and barren/desert/tundra ground are rocky; forest
    ///   floors are not. Scatter runs on a worker; rendering is GPU-instanced.
    /// </summary>
    public sealed class SurfaceRocks
    {
        struct Rock { public byte v; public float x, y, z, rx, ry, rz, s; }
        sealed class Cell { public Task<List<Rock>> task; public List<Rock> rocks; }

        // per size class: cell size, draw distance, density (per m² at rockiness 1), size range
        static readonly float[] CellM = { 12f, 32f, 128f };
        static readonly float[] Dist = { 60f, 260f, 700f };
        static readonly float[] Dens = { 1f / 6f, 1f / 70f, 1f / 2500f };
        static readonly Vector2[] Size = { new Vector2(0.06f, 0.35f), new Vector2(0.4f, 1.6f), new Vector2(2f, 7f) };

        const int Variants = 10;
        readonly SurfaceGeo geo;
        readonly ulong seed;
        readonly int layer;
        readonly Mesh[] meshes = new Mesh[Variants];
        readonly Material mat, ironMat, glassMat;
        // METEORITES: on dusty, bombarded worlds a share of the loose rocks are iron-nickel meteorites (polished metal,
        // bright facet glints) and impact glass (black tektites) — they sparkle while the regolith around them is matte.
        // Wet, thick-aired worlds bury and rust them away, so there they are rare.
        readonly float meteorFrac, ironShare;
        readonly Dictionary<long, Cell>[] cells = { new(), new(), new() };
        readonly List<Matrix4x4>[] batches = new List<Matrix4x4>[Variants];
        readonly List<Matrix4x4>[] ironBatches = new List<Matrix4x4>[Variants], glassBatches = new List<Matrix4x4>[Variants];
        readonly Matrix4x4[] buf = new Matrix4x4[1023];
        int running;
        public int Count { get { int n = 0; foreach (var d in cells) foreach (var c in d.Values) if (c.rocks != null) n += c.rocks.Count; return n; } }

        public SurfaceRocks(SurfaceGeo g, ulong planetSeed, int layer)
        {
            geo = g; seed = planetSeed; this.layer = layer;
            var sh = Shader.Find("CLAY/SurfaceTerrain");
            mat = new Material(sh) { enableInstancing = true };
            mat.SetFloat("_IsRock", 1f);
            mat.SetFloat("_Grain", 0.55f); mat.SetFloat("_GrainScale", 0.35f);
            var pal = geo.s.Palette;
            Color basalt = Color.Lerp(new Color(0.12f, 0.11f, 0.11f), pal.landLow, 0.3f);
            Color[] cols = { pal.landMid, pal.landHigh, pal.landLow, basalt, Color.Lerp(pal.landMid, pal.accent, 0.5f),
                             Color.Lerp(pal.landHigh, Color.white, 0.25f), pal.landMid * 0.8f, basalt, pal.landLow * 1.1f, pal.landMid };
            var r = new DetRng(DetRng.Hash(seed, 0x80C45UL));
            for (int i = 0; i < Variants; i++)
            {
                meshes[i] = BuildRock(ref r, cols[i], i);
                batches[i] = new List<Matrix4x4>(512); ironBatches[i] = new List<Matrix4x4>(64); glassBatches[i] = new List<Matrix4x4>(64);
            }

            var p = geo.planet;
            var theme = pal.theme;
            bool metalRich = theme == PlanetTexture.ChemTheme.Metallic || theme == PlanetTexture.ChemTheme.Ferrous || theme == PlanetTexture.ChemTheme.Cupric;
            bool air = geo.s.HasAtmo;
            float dusty = (air ? 0.35f : 1f) * (1f - Mathf.Clamp01(geo.s.WaterCov * 3f));   // regolith / dry desert keeps them
            float source = p.bombardment * 0.6f + geo.craters * 0.5f + geo.s.CraterAmp * 0.3f + (metalRich ? 0.45f : 0f);
            meteorFrac = Mathf.Clamp01(source * dusty) * 0.3f;
            ironShare = metalRich ? 0.85f : Mathf.Lerp(0.35f, 0.6f, geo.s.Mineral);
            ironMat = new Material(sh) { enableInstancing = true }; ironMat.SetFloat("_RockGlint", 1f); ironMat.SetFloat("_IsRock", 1f);
            glassMat = new Material(sh) { enableInstancing = true }; glassMat.SetFloat("_RockGlint", 2f); glassMat.SetFloat("_IsRock", 1f);
            if (meteorFrac > 0.01f) Debug.Log($"[Surface] meteorites: {meteorFrac * 100f:0}% of loose rocks ({ironShare * 100f:0}% iron, rest impact glass)");
        }

        static Mesh BuildRock(ref DetRng r, Color baseCol, int idx)
        {
            int lon = 11, lat = 7;
            float sx = r.Range(0.8f, 1.5f), sy = r.Range(0.45f, 0.95f), sz = r.Range(0.75f, 1.2f);
            float rough = r.Range(0.35f, 0.8f), freq = r.Range(1.2f, 2.4f);
            Vector3 off = new Vector3(r.Range(0f, 50f), r.Range(0f, 50f), r.Range(0f, 50f));
            var grid = new Vector3[(lon + 1) * (lat + 1)];
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j <= lon; j++)
                {
                    float phi = i / (float)lat * Mathf.PI, th = j / (float)lon * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    float n = PlanetTexture.SurfaceSampler.Noise(d * freq + off, 3);
                    float rr = 0.5f * (1f - rough * 0.5f + rough * n);
                    Vector3 p = Vector3.Scale(d * rr, new Vector3(sx, sy, sz));
                    if (p.y < -0.12f) p.y = -0.12f - (p.y + 0.12f) * 0.1f;   // flattened base that sits on the ground
                    grid[i * (lon + 1) + j] = p;
                }
            // flat-shaded: every triangle gets its own vertices + face normal + face colour
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var col = new List<Color>(); var t = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 fn = Vector3.Cross(b - a, c - a);
                if (fn.sqrMagnitude < 1e-12f) return;
                fn.Normalize();
                if (Vector3.Dot(fn, (a + b + c)) < 0f) { var tmp = b; b = c; c = tmp; fn = -fn; }   // outward
                float h = Mathf.Abs(Mathf.Sin((a.x * 12.9898f + a.y * 78.233f + a.z * 37.719f + idx) * 43758.5453f));
                float weather = Mathf.Clamp01(fn.y) * 0.25f;                                          // paler tops
                Color fc = baseCol * (0.8f + h * 0.35f) + new Color(weather, weather, weather) * 0.6f;
                fc.a = 0f;   // alpha = wetness in the terrain shader: rocks are dry
                int k = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                // softened facets: half face normal, half the rock's round normal — facets still read, but a single
                // triangle can no longer flash like a mirror or drop to black as the sun moves
                nrm.Add((fn * 0.45f + a.normalized * 0.55f).normalized);
                nrm.Add((fn * 0.45f + b.normalized * 0.55f).normalized);
                nrm.Add((fn * 0.45f + c.normalized * 0.55f).normalized);
                col.Add(fc); col.Add(fc); col.Add(fc);
                t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    Vector3 a = grid[i * (lon + 1) + j], b = grid[i * (lon + 1) + j + 1];
                    Vector3 c = grid[(i + 1) * (lon + 1) + j], d = grid[(i + 1) * (lon + 1) + j + 1];
                    Tri(a, c, b); Tri(b, c, d);
                }
            var m = new Mesh { name = $"Rock{idx}" };
            m.SetVertices(v); m.SetNormals(nrm); m.SetColors(col);
            var rw = new Vector4[v.Count]; for (int k = 0; k < rw.Length; k++) rw[k] = new Vector4(1f, 0f, 0f, 0f);   // palette slot 0 = Rocky
            m.SetUVs(0, rw);
            var zero = new Vector4[v.Count];
            m.SetUVs(1, zero); m.SetUVs(2, zero);   // no extra terrain weights, no river/lake surface (else drivers read garbage)
            m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        static long Key(long x, long z) => ((x + (1L << 30)) << 32) | (z + (1L << 30));

        public void Update(double camX, double camZ, bool terrainBusy)
        {
            int maxJobs = terrainBusy ? 0 : 2;
            for (int cls = 0; cls < 3; cls++)
            {
                float cs = CellM[cls], reach = Dist[cls] + cs;
                long c0x = (long)System.Math.Floor(camX / cs), c0z = (long)System.Math.Floor(camZ / cs);
                int rc = Mathf.CeilToInt(reach / cs);
                var keep = new HashSet<long>();
                var dict = cells[cls];
                for (long z = c0z - rc; z <= c0z + rc; z++)
                    for (long x = c0x - rc; x <= c0x + rc; x++)
                    {
                        double dx = (x + 0.5) * cs - camX, dz = (z + 0.5) * cs - camZ;
                        if (dx * dx + dz * dz > reach * reach) continue;
                        long k = Key(x, z); keep.Add(k);
                        if (!dict.ContainsKey(k) && running < maxJobs)
                        {
                            long cx = x, cz = z; int cl = cls;
                            dict[k] = new Cell { task = Task.Run(() => Scatter(cl, cx, cz)) };
                            running++;
                        }
                    }
                var drop = new List<long>();
                foreach (var kv in dict)
                {
                    var c = kv.Value;
                    if (c.task != null && c.task.IsCompleted)
                    {
                        running--;
                        if (c.task.IsFaulted) Debug.LogException(c.task.Exception); else c.rocks = c.task.Result;
                        c.task = null;
                    }
                    if (!keep.Contains(kv.Key) && c.task == null) drop.Add(kv.Key);
                }
                foreach (var k in drop) dict.Remove(k);
            }
        }

        List<Rock> Scatter(int cls, long cx, long cz)
        {
            var list = new List<Rock>();
            float cs = CellM[cls];
            double ox = cx * cs, oz = cz * cs;
            var r = new DetRng(DetRng.Hash(seed ^ (ulong)(cls * 7919), (ulong)Key(cx, cz)));
            var centre = geo.At(ox + cs * 0.5, oz + cs * 0.5);
            if (centre.underwater) return list;
            var lc = geo.ClimateAt(centre);
            bool bare = lc.biome == Biome.Barren || lc.biome == Biome.Desert || lc.biome == Biome.ColdDesert || lc.biome == Biome.Tundra;
            bool forest = lc.biome == Biome.TemperateForest || lc.biome == Biome.TemperateRainforest || lc.biome == Biome.TropicalRainforest
                       || lc.biome == Biome.TropicalSeasonal || lc.biome == Biome.Boreal;
            float rocky = Mathf.Clamp(0.25f + centre.mountain * 1.3f + centre.crater * 1.2f + geo.craters * 0.3f + (bare ? 0.55f : 0f)
                                      - (forest ? 0.2f : 0f) - centre.dune * 0.6f, 0.03f, 2.2f);
            rocky += geo.Boulderiness(centre) * (cls == 2 ? 1.2f : 3.5f);   // boulder fields: many more real boulders
            if (lc.biome == Biome.Ice && cls == 0) return list;
            float expect = Dens[cls] * cs * cs * rocky;
            int n = Mathf.FloorToInt(expect) + (r.Value < expect - Mathf.Floor(expect) ? 1 : 0);
            // clustering: rocks gather in fields (talus, ejecta, outcrops) rather than an even sprinkle
            for (int i = 0; i < n; i++)
            {
                float px = r.Range(0f, cs), pz = r.Range(0f, cs);
                float field = PlanetTexture.SurfaceSampler.Noise(new Vector3((float)((ox + px) / 90.0), cls * 3.1f, (float)((oz + pz) / 90.0)), 3);
                if (r.Value > Mathf.Clamp01(field * 1.8f - 0.25f)) continue;
                var sm = geo.At(ox + px, oz + pz);
                if (sm.underwater) continue;
                float u = r.Value;
                float s = Mathf.Lerp(Size[cls].x, Size[cls].y, u * u * u);   // power law: mostly small
                list.Add(new Rock
                {
                    v = (byte)r.RangeInt(0, Variants), x = px, y = sm.heightM - s * 0.22f, z = pz, s = s,
                    rx = r.Range(-12f, 12f), ry = r.Range(0f, 360f), rz = r.Range(-12f, 12f),
                });
            }
            return list;
        }

        public void Render(Camera cam, double ox, double oz)
        {
            for (int i = 0; i < Variants; i++) { batches[i].Clear(); ironBatches[i].Clear(); glassBatches[i].Clear(); }
            Vector3 cp = cam.transform.position;
            for (int cls = 0; cls < 3; cls++)
            {
                float cs = CellM[cls], d2 = Dist[cls] * Dist[cls];
                foreach (var kv in cells[cls])
                {
                    var c = kv.Value; if (c.rocks == null || c.rocks.Count == 0) continue;
                    long cx = ((kv.Key >> 32) & 0xFFFFFFFFL) - (1L << 30), cz = (kv.Key & 0xFFFFFFFFL) - (1L << 30);
                    float bx = (float)(cx * cs - ox), bz = (float)(cz * cs - oz);
                    foreach (var rk in c.rocks)
                    {
                        Vector3 p = new Vector3(bx + rk.x, rk.y, bz + rk.z);
                        if ((p - cp).sqrMagnitude > d2) continue;
                        var m4 = Matrix4x4.TRS(p, Quaternion.Euler(rk.rx, rk.ry, rk.rz), Vector3.one * rk.s);
                        // stable per-rock roll (from its placement): is it a meteorite, and which kind?
                        float roll = Frac(rk.x * 12.9898f + rk.z * 78.233f + rk.ry * 0.0371f);
                        if (cls < 2 && roll < meteorFrac)
                            (Frac(roll * 91.7f) < ironShare ? ironBatches : glassBatches)[rk.v].Add(m4);
                        else batches[rk.v].Add(m4);
                    }
                }
            }
            var rp = new RenderParams(mat)
            {
                layer = layer, camera = cam, receiveShadows = true, shadowCastingMode = ShadowCastingMode.On,
                worldBounds = new Bounds(cp, Vector3.one * 2000f),
            };
            Draw(rp, batches);
            if (meteorFrac > 0.01f)
            {
                var rpI = rp; rpI.material = ironMat; Draw(rpI, ironBatches);
                var rpG = rp; rpG.material = glassMat; Draw(rpG, glassBatches);
            }
        }

        void Draw(RenderParams rp, List<Matrix4x4>[] lists)
        {
            for (int i = 0; i < Variants; i++)
            {
                var list = lists[i];
                for (int start = 0; start < list.Count; start += buf.Length)
                {
                    int n = Mathf.Min(buf.Length, list.Count - start);
                    list.CopyTo(start, buf, 0, n);
                    Graphics.RenderMeshInstanced(rp, meshes[i], 0, buf, n);
                }
            }
        }
        static float Frac(float v) => v - Mathf.Floor(v);

        /// Re-scatter everything (the geography changed, e.g. rivers arrived). In-flight jobs finish unobserved.
        public void Invalidate() { foreach (var d in cells) d.Clear(); running = 0; }

        public void Dispose()
        {
            foreach (var m in meshes) if (m) Object.Destroy(m);
            if (mat) Object.Destroy(mat);
            if (ironMat) Object.Destroy(ironMat);
            if (glassMat) Object.Destroy(glassMat);
            foreach (var d in cells) d.Clear();
        }
    }
}
