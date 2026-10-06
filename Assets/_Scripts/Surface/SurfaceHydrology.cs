using System.Collections.Generic;
using UnityEngine;

namespace CLAY.Surface
{
    /// <summary>
    /// Rivers and lakes that actually drain. Built once per landing on a worker thread from a coarse grid (~1,000 km
    /// square, 3.2 km cells) of the real surface height around the landing site:
    ///  1. PRIORITY-FLOOD (Barnes et al. 2014) from the sinks (sea, map edge): every depression fills to its spill
    ///     height, and each cell's RECEIVER is the neighbour it was flooded from — a drainage tree that always reaches
    ///     the sea, straight through the lakes.
    ///  2. Filled cells well above the ground are LAKES, at their spill level.
    ///  3. FLOW ACCUMULATION of rainfall (weighted by the planet's moisture map) down the tree; where it passes a
    ///     threshold a RIVER forms, wider and deeper downstream (hydraulic geometry: width ∝ √discharge).
    ///  4. The river network is kept as segments in a spatial hash; SurfaceGeo.At asks for the nearest one and carves
    ///     a meandering channel, banks and a broad valley — with water in it on worlds that have liquid, dry arroyos on
    ///     dry worlds with air.
    /// </summary>
    public sealed class SurfaceHydrology
    {
        const int N = 320;
        const float Cell = 3200f;
        const float BucketM = 6000f;

        readonly float[] lakeLevel;                 // NaN = no lake in this coarse cell
        readonly float[] fillF, fillDepth;          // filled (spill) surface and how far above the ground it sits
        readonly List<Seg> segs = new();
        readonly Dictionary<long, List<int>> buckets = new();
        public readonly bool wet;                   // liquid in the channels (else dry arroyos)
        public int RiverCount => segs.Count;
        public int LakeCells { get; private set; }

        struct Seg { public float ax, az, bx, bz, la, lb, width, depth; }

        SurfaceHydrology(bool wet) { this.wet = wet; lakeLevel = new float[N * N]; fillF = new float[N * N]; fillDepth = new float[N * N]; }

        static float Origin => -N * 0.5f * Cell;

        public static SurfaceHydrology Build(SurfaceGeo geo, bool wet)
        {
            var hy = new SurfaceHydrology(wet);
            int n = N * N;
            var H = new float[n]; var rain = new float[n];
            float o = Origin;
            System.Threading.Tasks.Parallel.For(0, N, j =>                 // ~100k height samples: use every core
            {
                for (int i = 0; i < N; i++)
                {
                    double x = o + (i + 0.5) * Cell, z = o + (j + 0.5) * Cell;
                    var sm = geo.At(x, z);
                    int k = j * N + i;
                    H[k] = sm.heightM;
                    rain[k] = 0.15f + geo.s.Moisture(sm.dir);
                }
            });
            double rainSum = 0; for (int k = 0; k < n; k++) rainSum += rain[k];
            // Smooth the coarse heights (3×3 box, twice) before routing: at 3.2 km the rolling hills alias into a field
            // of false pits, and every false pit would otherwise fill into a 'lake' — flooding whole continents.
            // Sea cells keep their height so coastlines stay put.
            var tmp = new float[n];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        int k = j * N + i;
                        if (geo.hasSea && H[k] < 0f) { tmp[k] = H[k]; continue; }
                        float sum = 0f; int c = 0;
                        for (int dj = -1; dj <= 1; dj++)
                            for (int di = -1; di <= 1; di++)
                            {
                                int ni = i + di, nj = j + dj;
                                if (ni < 0 || nj < 0 || ni >= N || nj >= N) continue;
                                sum += H[nj * N + ni]; c++;
                            }
                        tmp[k] = sum / c;
                    }
                System.Array.Copy(tmp, H, n);
            }
            float avgRain = (float)(rainSum / n);

            // 1. priority-flood with a tiny epsilon so filled flats still drain
            var F = new float[n]; var rcv = new int[n]; var closed = new bool[n];
            var order = new int[n]; int oc = 0;
            var heap = new MinHeap(n);
            for (int k = 0; k < n; k++) { rcv[k] = -1; F[k] = H[k]; }
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    bool edge = i == 0 || j == 0 || i == N - 1 || j == N - 1;
                    bool sea = geo.hasSea && H[k] < 0f;
                    if (edge || sea) { closed[k] = true; heap.Push(k, F[k]); }
                }
            while (heap.Count > 0)
            {
                int c = heap.Pop(); order[oc++] = c;
                int ci = c % N, cj = c / N;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int ni = ci + di, nj = cj + dj;
                        if (ni < 0 || nj < 0 || ni >= N || nj >= N) continue;
                        int nk = nj * N + ni;
                        if (closed[nk]) continue;
                        closed[nk] = true;
                        F[nk] = Mathf.Max(H[nk], F[c] + 0.01f);
                        rcv[nk] = c;
                        heap.Push(nk, F[nk]);
                    }
            }

            // 2. lakes: only REAL basins — deep enough (≥ 20 m of fill), spanning several cells, and in a climate wet
            // enough to fill them (dry basins stay dry: playas). Everything else just drains through.
            var comp = new int[n]; for (int k = 0; k < n; k++) comp[k] = -1;
            var stack = new Stack<int>(); var members = new List<int>();
            var basins = new List<(float vol, int[] mem)>();
            for (int k0 = 0; k0 < n; k0++)
            {
                hy.fillF[k0] = F[k0];
                hy.lakeLevel[k0] = float.NaN;
                if (comp[k0] >= 0 || F[k0] - H[k0] < 20f || (geo.hasSea && H[k0] < 0f)) continue;
                members.Clear(); stack.Push(k0); comp[k0] = k0;
                float rainIn = 0f;
                while (stack.Count > 0)
                {
                    int c = stack.Pop(); members.Add(c); rainIn += rain[c];
                    int ci = c % N, cj = c / N;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int ni = ci + di, nj = cj + dj;
                            if (ni < 0 || nj < 0 || ni >= N || nj >= N) continue;
                            int nk = nj * N + ni;
                            if (comp[nk] >= 0 || F[nk] - H[nk] < 20f || (geo.hasSea && H[nk] < 0f)) continue;
                            comp[nk] = k0; stack.Push(nk);
                        }
                }
                bool real = members.Count >= 3 && rainIn / members.Count > avgRain * 0.8f;
                if (real)
                {
                    float vol = 0f; foreach (int m in members) vol += F[m] - H[m];
                    basins.Add((vol, members.ToArray()));
                }
            }
            // biggest basins first, until lakes cover ~3% of the land (Earth: ~2–4%); smaller ones just drain through
            basins.Sort((x, y) => y.vol.CompareTo(x.vol));
            int landCells = 0; for (int k = 0; k < n; k++) if (!(geo.hasSea && H[k] < 0f)) landCells++;
            int budget = Mathf.Max(3, Mathf.RoundToInt(landCells * 0.03f));
            foreach (var (vol, mem) in basins)
            {
                if (hy.LakeCells + mem.Length > budget) continue;
                foreach (int m in mem) { hy.lakeLevel[m] = F[m]; hy.LakeCells++; }
            }
            for (int k = 0; k < n; k++)
                hy.fillDepth[k] = float.IsNaN(hy.lakeLevel[k]) ? 0f : Mathf.Max(F[k] - H[k], 0f);

            // 3. flow accumulation, from the highest cells down
            var acc = new float[n];
            for (int q = oc - 1; q >= 0; q--)
            {
                int k = order[q];
                acc[k] += rain[k];
                if (rcv[k] >= 0) acc[rcv[k]] += acc[k];
            }

            // 4. river segments
            float thresh = avgRain * 28f;
            for (int k = 0; k < n; k++)
            {
                if (acc[k] < thresh || rcv[k] < 0) continue;
                int r = rcv[k];
                if (!float.IsNaN(hy.lakeLevel[k]) && !float.IsNaN(hy.lakeLevel[r])) continue;   // inside a lake
                if (geo.hasSea && H[k] < 0f) continue;
                float cells = acc[k] / avgRain;
                float width = Mathf.Clamp(3.2f * Mathf.Sqrt(cells), 6f, 420f);
                var sgm = new Seg
                {
                    ax = o + (k % N + 0.5f) * Cell, az = o + (k / N + 0.5f) * Cell,
                    bx = o + (r % N + 0.5f) * Cell, bz = o + (r / N + 0.5f) * Cell,
                    la = F[k], lb = F[r], width = width, depth = 1.2f + width * 0.035f,
                };
                int idx = hy.segs.Count;
                hy.segs.Add(sgm);
                // register in every bucket the segment's padded box touches
                float pad = width * 4f + 1500f;
                long bx0 = (long)System.Math.Floor((Mathf.Min(sgm.ax, sgm.bx) - pad) / BucketM), bx1 = (long)System.Math.Floor((Mathf.Max(sgm.ax, sgm.bx) + pad) / BucketM);
                long bz0 = (long)System.Math.Floor((Mathf.Min(sgm.az, sgm.bz) - pad) / BucketM), bz1 = (long)System.Math.Floor((Mathf.Max(sgm.az, sgm.bz) + pad) / BucketM);
                for (long bz = bz0; bz <= bz1; bz++)
                    for (long bx = bx0; bx <= bx1; bx++)
                    {
                        long key = (bx << 32) ^ (bz & 0xFFFFFFFFL);
                        if (!hy.buckets.TryGetValue(key, out var l)) hy.buckets[key] = l = new List<int>(4);
                        l.Add(idx);
                    }
            }
            return hy;
        }

        /// Carve rivers / fill lakes at a surface point. Called from SurfaceGeo.At (any thread).
        public void Apply(ref SurfaceGeo.Sample o, double x, double z)
        {
            float fx = (float)x, fz = (float)z;
            // LAKES: bilinear spill surface + bilinear basin depth. Water only where the basin is real (filled well
            // above the coarse ground) AND the actual ground dips below the spill level — so shorelines follow the
            // terrain's contours instead of the 3.2 km grid.
            float gx = (fx - Origin) / Cell - 0.5f, gz = (fz - Origin) / Cell - 0.5f;
            int i0 = Mathf.FloorToInt(gx), j0 = Mathf.FloorToInt(gz);
            if (i0 >= 0 && j0 >= 0 && i0 < N - 1 && j0 < N - 1)
            {
                float tx = gx - i0, tz = gz - j0;
                float Bil(float[] a) => Mathf.Lerp(Mathf.Lerp(a[j0 * N + i0], a[j0 * N + i0 + 1], tx), Mathf.Lerp(a[(j0 + 1) * N + i0], a[(j0 + 1) * N + i0 + 1], tx), tz);
                float basin = Bil(fillDepth);
                if (basin > 12f)
                {
                    float L = Bil(fillF);
                    if (o.heightM < L)
                    {
                        o.water = wet ? 1f : 0f;
                        if (wet) { o.heightM = L - 0.15f; o.flowX = 0; o.flowZ = 0; }
                        else o.heightM = Mathf.Lerp(o.heightM, L - 2f, 0.6f);                   // dry lake: a flat playa floor
                        o.valley = Mathf.Max(o.valley, 0.6f);
                        return;
                    }
                }
            }
            // RIVERS: nearest segment, measured from a meander-warped point (so channels wind, not zigzag)
            long key = ((long)System.Math.Floor(x / BucketM) << 32) ^ ((long)System.Math.Floor(z / BucketM) & 0xFFFFFFFFL);
            if (!buckets.TryGetValue(key, out var list)) return;
            float best = float.MaxValue; int bi = -1; float bt = 0;
            float wx = 0, wz = 0;
            foreach (int si in list)
            {
                var s = segs[si];
                // meanders: a two-scale warp large enough (≈ half a grid cell) to bend the 3.2 km D8 segments into
                // winding channels instead of straight 45°/90° runs
                float mw = Mathf.Clamp(s.width * 6f + 900f, 900f, 2400f);
                wx = ((Noise(fx / 2600f, fz / 2600f, 3.1f) - 0.5f) * 1.6f + (Noise(fx / 700f, fz / 700f, 5.3f) - 0.5f) * 0.5f) * mw;
                wz = ((Noise(fx / 2600f, fz / 2600f, 7.7f) - 0.5f) * 1.6f + (Noise(fx / 700f, fz / 700f, 9.9f) - 0.5f) * 0.5f) * mw;
                float px = fx + wx, pz = fz + wz;
                float dx = s.bx - s.ax, dz = s.bz - s.az;
                float t = Mathf.Clamp01(((px - s.ax) * dx + (pz - s.az) * dz) / Mathf.Max(dx * dx + dz * dz, 1f));
                float qx = s.ax + dx * t - px, qz = s.az + dz * t - pz;
                float d = Mathf.Sqrt(qx * qx + qz * qz) - s.width * 0.5f;
                if (d < best) { best = d; bi = si; bt = t; }
            }
            if (bi < 0) return;
            var sg = segs[bi];
            float level = Mathf.Lerp(sg.la, sg.lb, bt);
            float hOrig = o.heightM;
            // the network's level comes from a coarse, smoothed grid; where the real ground here is LOWER, the river
            // runs at the ground (cut into it), never perched above it
            level = Mathf.Min(level, hOrig);
            float valleyW = sg.width * 6f + 300f;
            if (best > valleyW) return;
            // broad valley → banks → channel
            float valleyFloor = level + Mathf.Max(best, 0f) * 0.06f + Mathf.Max(best - sg.width * 2f, 0f) * 0.12f;
            float carve = 1f - Mathf.Clamp01(best / valleyW);
            o.heightM = Mathf.Lerp(o.heightM, Mathf.Min(o.heightM, valleyFloor), carve * carve * (3f - 2f * carve));
            o.valley = Mathf.Max(o.valley, carve * 0.8f);
            if (best < 0f)
            {
                float across = Mathf.Clamp01(-best / (sg.width * 0.5f));                     // 0 at the bank → 1 mid-channel
                if (wet)
                {
                    // the water surface: at the network's level where the channel is cut into the ground, but never
                    // hovering above the land — where the ground falls away, the water follows it down
                    o.heightM = Mathf.Min(level, hOrig) - 0.4f - sg.depth * 0.08f * across;   // surface just below the banks
                    o.water = 1f;
                    float dx = sg.bx - sg.ax, dz = sg.bz - sg.az, l = Mathf.Sqrt(dx * dx + dz * dz) + 1e-3f;
                    o.flowX = dx / l; o.flowZ = dz / l;
                }
                else o.heightM = Mathf.Min(o.heightM, level - sg.depth * Mathf.Sqrt(across));   // dry channel bed
            }
            else if (best < sg.width * 0.6f)
                o.heightM = Mathf.Min(o.heightM, level + best * 0.9f);                       // steep banks
        }

        static float Noise(float x, float z, float seed) =>
            CLAY.Galaxy.PlanetTexture.SurfaceSampler.Noise(new Vector3(x + seed, seed * 0.7f, z - seed), 2);

        // binary min-heap of cell indices keyed by height
        sealed class MinHeap
        {
            readonly int[] items; readonly float[] keys; public int Count;
            public MinHeap(int cap) { items = new int[cap + 1]; keys = new float[cap + 1]; }
            public void Push(int item, float key)
            {
                int i = Count++;
                items[i] = item; keys[i] = key;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (keys[p] <= keys[i]) break;
                    (items[p], items[i]) = (items[i], items[p]); (keys[p], keys[i]) = (keys[i], keys[p]);
                    i = p;
                }
            }
            public int Pop()
            {
                int top = items[0];
                Count--;
                items[0] = items[Count]; keys[0] = keys[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < Count && keys[l] < keys[m]) m = l;
                    if (r < Count && keys[r] < keys[m]) m = r;
                    if (m == i) break;
                    (items[m], items[i]) = (items[i], items[m]); (keys[m], keys[i]) = (keys[i], keys[m]);
                    i = m;
                }
                return top;
            }
        }
    }
}
