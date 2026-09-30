using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using CLAY.Galaxy;

namespace CLAY.Surface
{
    /// <summary>
    /// 3D grass near the camera. Clumps of curved, tapered blades (3 mesh variants + a light far LOD), streamed in
    /// 8 m cells within ~60 m on worker threads. Placement follows the SAME terrain-type weights the ground shader
    /// uses: dense on Grassy ground, short and sparse on Mossy, none underwater or on steep slopes. Each clump takes
    /// the ground's own colour at its spot (so alien pigments carry through) and is drawn GPU-instanced with a
    /// per-instance tint. Wind comes from the planet's climate.
    /// </summary>
    public sealed class SurfaceGrass
    {
        const float CellM = 8f, Radius = 60f, NearLod = 22f;
        const float Density = 5f;                    // clumps per m² at full Grassy weight
        const int Variants = 3, Coarse = 4;          // coarse Ground samples per cell side (+1)

        struct Clump { public Vector3 p; public float rot, s, hs, rnd; public Color tint; public byte v; }
        sealed class Cell { public Task<List<Clump>> task; public List<Clump> clumps; }

        readonly SurfaceGeo geo;
        readonly ulong seed;
        readonly int layer, slotGrass, slotMoss;
        public readonly bool enabled;
        readonly Mesh[] near = new Mesh[Variants];
        Mesh far;
        readonly Material mat;
        readonly Dictionary<long, Cell> cells = new();
        readonly List<Matrix4x4>[] bNear = new List<Matrix4x4>[Variants];
        readonly List<Vector4>[] tNear = new List<Vector4>[Variants];
        readonly List<Matrix4x4> bFar = new(4096); readonly List<Vector4> tFar = new(4096);
        readonly Matrix4x4[] mBuf = new Matrix4x4[1023]; readonly Vector4[] tBuf = new Vector4[1023];
        readonly MaterialPropertyBlock mpb = new();
        int running;
        public int Count { get; private set; }

        public SurfaceGrass(SurfaceGeo g, ulong planetSeed, int layer)
        {
            geo = g; seed = planetSeed; this.layer = layer;
            slotGrass = geo.SlotOf(SurfaceGeo.TerrainType.Grassy);
            slotMoss = geo.SlotOf(SurfaceGeo.TerrainType.Mossy);
            enabled = slotGrass >= 0 || slotMoss >= 0;
            var sh = Shader.Find("CLAY/SurfaceGrass");
            if (!enabled || sh == null) { enabled = false; return; }
            mat = new Material(sh) { enableInstancing = true };
            var r = new DetRng(DetRng.Hash(seed, 0x6A55UL));
            for (int i = 0; i < Variants; i++) { near[i] = BuildClump(ref r, 11 + i * 2, 3); bNear[i] = new List<Matrix4x4>(2048); tNear[i] = new List<Vector4>(2048); }
            far = BuildClump(ref r, 6, 1);
            // wind from the planet's climate (direction fixed per world, strength from wind load)
            float wa = r.Range(0f, Mathf.PI * 2f);
            float strength = Mathf.Clamp(0.25f + geo.climate.windLoad * 0.12f, 0.15f, 1.4f);
            Shader.SetGlobalVector("_GrassWind", new Vector4(Mathf.Cos(wa), Mathf.Sin(wa), strength, 1.2f + strength));
            Debug.Log($"[Surface] grass: on (slots grass {slotGrass}, moss {slotMoss}), wind ×{strength:0.00}");
        }

        // A clump of curved, tapered blades. uv.y = height along the blade; colour.r = per-blade tone.
        static Mesh BuildClump(ref DetRng r, int blades, int segs)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int b = 0; b < blades; b++)
            {
                float ang = r.Range(0f, Mathf.PI * 2f), rad = r.Range(0f, 0.12f);
                Vector3 basePos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                float face = r.Range(0f, Mathf.PI * 2f);
                Vector3 wdir = new Vector3(Mathf.Cos(face), 0f, Mathf.Sin(face));             // blade width direction
                Vector3 lean = Vector3.Cross(Vector3.up, wdir) * r.Range(-1f, 1f) + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 0.6f;
                float h = r.Range(0.28f, 0.7f), w = r.Range(0.016f, 0.028f);
                float tone = r.Range(0f, 1f);
                int b0 = v.Count;
                for (int k = 0; k <= segs; k++)
                {
                    float tt = k / (float)segs;
                    Vector3 ctr = basePos + Vector3.up * (h * tt) + lean * (h * 0.4f * tt * tt);
                    Vector3 tangent = (Vector3.up * h + lean * (h * 0.8f * tt)).normalized;
                    Vector3 nrm = Vector3.Cross(wdir, tangent).normalized;
                    float hw = w * Mathf.Pow(1f - tt, 0.8f) * 0.5f;
                    if (k < segs)
                    {
                        v.Add(ctr - wdir * hw); v.Add(ctr + wdir * hw);
                        n.Add(nrm); n.Add(nrm); c.Add(new Color(tone, 0, 0)); c.Add(new Color(tone, 0, 0));
                        uv.Add(new Vector2(0, tt)); uv.Add(new Vector2(1, tt));
                    }
                    else { v.Add(ctr); n.Add(nrm); c.Add(new Color(tone, 0, 0)); uv.Add(new Vector2(0.5f, 1f)); }
                }
                for (int k = 0; k < segs - 1; k++)
                {
                    int a = b0 + k * 2;
                    t.Add(a); t.Add(a + 2); t.Add(a + 1); t.Add(a + 1); t.Add(a + 2); t.Add(a + 3);
                }
                int last = b0 + (segs - 1) * 2, tip = b0 + segs * 2;
                t.Add(last); t.Add(tip); t.Add(last + 1);
            }
            var m = new Mesh { name = "GrassClump" };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.bounds = new Bounds(new Vector3(0, 0.4f, 0), new Vector3(1.6f, 1.2f, 1.6f));
            return m;
        }

        static long Key(long cx, long cz) => ((cx + (1L << 30)) << 32) | (cz + (1L << 30));

        public void Update(double camX, double camZ, bool busy)
        {
            if (!enabled) return;
            long ccx = (long)System.Math.Floor(camX / CellM), ccz = (long)System.Math.Floor(camZ / CellM);
            int R = Mathf.CeilToInt(Radius / CellM) + 1;
            // start missing cells, nearest first (a few at a time; terrain has priority)
            int maxJobs = busy ? 1 : 3;
            for (int ring = 0; ring <= R && running < maxJobs; ring++)
                for (int dz = -ring; dz <= ring && running < maxJobs; dz++)
                    for (int dx = -ring; dx <= ring && running < maxJobs; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                        long cx = ccx + dx, cz = ccz + dz, k = Key(cx, cz);
                        if (cells.ContainsKey(k)) continue;
                        double x0 = cx * CellM, z0 = cz * CellM;
                        if (System.Math.Sqrt(System.Math.Pow(x0 + CellM * 0.5 - camX, 2) + System.Math.Pow(z0 + CellM * 0.5 - camZ, 2)) > Radius + CellM) continue;
                        var cell = new Cell();
                        cell.task = Task.Run(() => Scatter(cx, cz));
                        cells[k] = cell; running++;
                    }
            var drop = new List<long>();
            foreach (var kv in cells)
            {
                var c = kv.Value;
                if (c.task != null && c.task.IsCompleted)
                {
                    running--;
                    c.clumps = c.task.IsFaulted ? new List<Clump>() : c.task.Result;
                    if (c.task.IsFaulted) Debug.LogException(c.task.Exception.GetBaseException());
                    c.task = null;
                }
                long cx = ((kv.Key >> 32) & 0xFFFFFFFFL) - (1L << 30), cz = (kv.Key & 0xFFFFFFFFL) - (1L << 30);
                if (c.task == null && (Mathf.Abs(cx - ccx) > R + 1 || Mathf.Abs(cz - ccz) > R + 1)) drop.Add(kv.Key);
            }
            foreach (var k in drop) cells.Remove(k);
        }

        List<Clump> Scatter(long cx, long cz)
        {
            var list = new List<Clump>();
            double x0 = cx * CellM, z0 = cz * CellM;
            // coarse grid of ground evaluations: grass weight, moss weight, colour, slope
            int C = Coarse + 1; float st = CellM / Coarse;
            var gw = new float[C * C]; var mw = new float[C * C]; var col = new Color[C * C];
            bool any = false;
            for (int j = 0; j < C; j++)
                for (int i = 0; i < C; i++)
                {
                    double x = x0 + i * st, z = z0 + j * st;
                    var sm = geo.At(x, z);
                    int k = j * C + i;
                    if (sm.underwater) continue;
                    float hx = geo.At(x + 1.5, z).heightM - sm.heightM, hz = geo.At(x, z + 1.5).heightM - sm.heightM;
                    Vector3 nrm = new Vector3(-hx / 1.5f, 1f, -hz / 1.5f).normalized;
                    float slope = Mathf.Clamp01((1f - nrm.y) * 3.2f);
                    col[k] = geo.Ground(sm, geo.ClimateAt(sm), slope, geo.Orbital(sm, slope), out Vector4 a, out Vector4 b);
                    float W(int slot) => slot < 0 ? 0f : slot < 4 ? a[slot] : b[slot - 4];
                    float steepCut = 1f - Mathf.Clamp01((slope - 0.35f) / 0.25f);
                    gw[k] = W(slotGrass) * steepCut; mw[k] = W(slotMoss) * steepCut;
                    if (gw[k] > 0.03f || mw[k] > 0.03f) any = true;
                }
            if (!any) return list;

            var r = new DetRng(DetRng.Hash(seed ^ 0x9A55UL, (ulong)Key(cx, cz)));
            int n = Mathf.RoundToInt(Density * CellM * CellM);
            for (int q = 0; q < n; q++)
            {
                float px = r.Range(0f, CellM), pz = r.Range(0f, CellM);
                float fu = px / st, fv = pz / st;
                int i0 = Mathf.Min((int)fu, Coarse - 1), j0 = Mathf.Min((int)fv, Coarse - 1);
                float tu = fu - i0, tv = fv - j0;
                float Bil(float[] f) => Mathf.Lerp(Mathf.Lerp(f[j0 * C + i0], f[j0 * C + i0 + 1], tu), Mathf.Lerp(f[(j0 + 1) * C + i0], f[(j0 + 1) * C + i0 + 1], tu), tv);
                float g = Bil(gw), m = Bil(mw);
                // clumping: grass grows in patches, not an even lawn
                float patch = PlanetTexture.SurfaceSampler.Noise(new Vector3((float)((x0 + px) / 6.0), 5.1f, (float)((z0 + pz) / 6.0)), 2);
                float keep = Mathf.Max(g, m * 0.45f) * Mathf.Clamp01(patch * 1.6f - 0.15f);
                if (r.Value > keep) continue;
                Color c0 = Color.Lerp(col[j0 * C + i0], col[j0 * C + i0 + 1], tu), c1 = Color.Lerp(col[(j0 + 1) * C + i0], col[(j0 + 1) * C + i0 + 1], tu);
                Color tint = Color.Lerp(c0, c1, tv);
                var sm = geo.At(x0 + px, z0 + pz);
                if (sm.underwater) continue;
                bool mossy = m > g;
                list.Add(new Clump
                {
                    p = new Vector3(px, sm.heightM - 0.02f, pz), rot = r.Range(0f, 360f),
                    s = r.Range(0.75f, 1.3f) * (mossy ? 0.9f : 1f), hs = mossy ? r.Range(0.18f, 0.32f) : r.Range(0.7f, 1.25f),
                    rnd = r.Value, tint = tint * r.Range(0.88f, 1.1f), v = (byte)r.RangeInt(0, Variants),
                });
            }
            return list;
        }

        public void Render(Camera cam, double ox, double oz)
        {
            if (!enabled) return;
            for (int i = 0; i < Variants; i++) { bNear[i].Clear(); tNear[i].Clear(); }
            bFar.Clear(); tFar.Clear();
            Vector3 cp = cam.transform.position;
            int count = 0;
            foreach (var kv in cells)
            {
                var c = kv.Value; if (c.clumps == null || c.clumps.Count == 0) continue;
                long cx = ((kv.Key >> 32) & 0xFFFFFFFFL) - (1L << 30), cz = (kv.Key & 0xFFFFFFFFL) - (1L << 30);
                float bx = (float)(cx * CellM - ox), bz = (float)(cz * CellM - oz);
                float cdx = bx + CellM * 0.5f - cp.x, cdz = bz + CellM * 0.5f - cp.z;
                if (cdx * cdx + cdz * cdz > (Radius + CellM) * (Radius + CellM)) continue;
                foreach (var cl in c.clumps)
                {
                    Vector3 p = new Vector3(bx + cl.p.x, cl.p.y, bz + cl.p.z);
                    float dx = p.x - cp.x, dz = p.z - cp.z, d2 = dx * dx + dz * dz;
                    if (d2 > Radius * Radius) continue;
                    float d = Mathf.Sqrt(d2);
                    // thin out with distance (the far half keeps fewer, lighter clumps)
                    if (d > NearLod && cl.rnd > Mathf.Lerp(1f, 0.45f, (d - NearLod) / (Radius - NearLod))) continue;
                    var m4 = Matrix4x4.TRS(p, Quaternion.Euler(0f, cl.rot, 0f), new Vector3(cl.s, cl.s * cl.hs, cl.s));
                    var tv = new Vector4(cl.tint.r, cl.tint.g, cl.tint.b, 1f);
                    if (d < NearLod) { bNear[cl.v].Add(m4); tNear[cl.v].Add(tv); } else { bFar.Add(m4); tFar.Add(tv); }
                    count++;
                }
            }
            Count = count;
            var rp = new RenderParams(mat)
            {
                layer = layer, camera = cam, receiveShadows = true, shadowCastingMode = ShadowCastingMode.Off,
                worldBounds = new Bounds(cp, Vector3.one * (Radius * 2.5f)), matProps = mpb,
            };
            for (int i = 0; i < Variants; i++) Draw(rp, near[i], bNear[i], tNear[i]);
            Draw(rp, far, bFar, tFar);
        }

        void Draw(RenderParams rp, Mesh mesh, List<Matrix4x4> ms, List<Vector4> ts)
        {
            for (int start = 0; start < ms.Count; start += mBuf.Length)
            {
                int n = Mathf.Min(mBuf.Length, ms.Count - start);
                ms.CopyTo(start, mBuf, 0, n); ts.CopyTo(start, tBuf, 0, n);
                mpb.SetVectorArray("_Tint", tBuf);
                Graphics.RenderMeshInstanced(rp, mesh, 0, mBuf, n);
            }
        }

        /// Re-scatter everything (the geography changed, e.g. rivers arrived). In-flight jobs finish unobserved.
        public void Invalidate() { cells.Clear(); running = 0; }

        public void Dispose()
        {
            foreach (var m in near) if (m) Object.Destroy(m);
            if (far) Object.Destroy(far);
            if (mat) Object.Destroy(mat);
            cells.Clear();
        }
    }
}
