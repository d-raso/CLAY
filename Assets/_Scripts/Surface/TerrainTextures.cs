using System.Threading.Tasks;
using UnityEngine;

namespace CLAY.Surface
{
    /// <summary>
    /// Synthesises a tileable PBR texture set for every terrain type (SurfaceGeo.TerrainType), once per session, on
    /// worker threads — the ground's equivalent of photo-scanned materials, built from how each surface forms:
    /// individually STACKED objects (pebbles, scree, clods, moss cushions) with irregular outlines, power-law sizes
    /// and per-object tone; HIERARCHICAL crack networks with warped, varying-width edges and curled, tilted plates;
    /// strongly WARPED, amplitude-modulated ripples and sastrugi (never clean stripes); meteorite regmaglypts; ropy
    /// pāhoehoe folds; a power-law crater population for regolith.
    ///
    /// Output: two Texture2DArrays (one slice per type), bound globally for SurfaceTerrain.shader:
    ///   _TerrA = normal.x, normal.z (0..1), height (0..1), ambient occlusion
    ///   _TerrB = tone/2 (albedo multiplier), roughness, emission mask, metal mask
    /// plus _TerrScale[type] = metres covered by one tile.
    /// </summary>
    public static class TerrainTextures
    {
        public const int N = 512;
        public static readonly float[] Scale =
        {
            4f, 4f, 0.9f, 2.4f, 8f, 1.3f, 12f, 3.2f, 2.5f, 7f, 15f, 9f, 1.4f, 4f, 7f, 8f, 3f, 10f, 2f, 3f, 2.6f,
        };

        public static bool Ready { get; private set; }
        static Task<Color32[][]> job;
        static Texture2DArray texA, texB;

        /// Start the bake (no-op if running / done). Call Poll() every frame until Ready.
        public static void Ensure()
        {
            if (Ready) { Bind(); return; }
            if (job != null) return;
            int count = SurfaceGeo.TypeCount;
            job = Task.Run(() =>
            {
                var outp = new Color32[count * 2][];
                Parallel.For(0, count, t =>
                {
                    var c = new Canvas(N, Scale[t]);
                    Generate(t, c);
                    c.Finish(out outp[t * 2], out outp[t * 2 + 1]);
                });
                return outp;
            });
            Shader.SetGlobalFloat("_TerrReady", 0f);
        }

        public static void Poll()
        {
            if (Ready || job == null || !job.IsCompleted) return;
            if (job.IsFaulted) { Debug.LogException(job.Exception.GetBaseException()); job = null; return; }
            var data = job.Result; job = null;
            int count = SurfaceGeo.TypeCount;
            texA = new Texture2DArray(N, N, count, TextureFormat.RGBA32, true, true) { name = "TerrainA", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            texB = new Texture2DArray(N, N, count, TextureFormat.RGBA32, true, true) { name = "TerrainB", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            for (int t = 0; t < count; t++) { texA.SetPixels32(data[t * 2], t); texB.SetPixels32(data[t * 2 + 1], t); }
            texA.Apply(true, true); texB.Apply(true, true);
            Ready = true;
            Bind();
            Debug.Log("[Terrain] texture sets ready");
        }

        static void Bind()
        {
            Shader.SetGlobalTexture("_TerrA", texA);
            Shader.SetGlobalTexture("_TerrB", texB);
            var sc = new float[32]; for (int i = 0; i < Scale.Length; i++) sc[i] = Scale[i];
            Shader.SetGlobalFloatArray("_TerrScale", sc);
            Shader.SetGlobalFloat("_TerrReady", 1f);
        }

        // ── canvas ───────────────────────────────────────────────────────────────────────────────────────
        sealed class Canvas
        {
            public readonly int n; public readonly float metres;
            public readonly float[] H, T, R, E, M;
            public Canvas(int n, float metres)
            {
                this.n = n; this.metres = metres;
                H = new float[n * n]; T = new float[n * n]; R = new float[n * n]; E = new float[n * n]; M = new float[n * n];
                for (int i = 0; i < T.Length; i++) { T[i] = 1f; R[i] = 0.85f; }
            }
            public int Idx(int x, int y) => ((y % n + n) % n) * n + ((x % n + n) % n);

            public void Finish(out Color32[] a, out Color32[] b)
            {
                a = new Color32[n * n]; b = new Color32[n * n];
                float hmin = float.MaxValue, hmax = float.MinValue;
                for (int i = 0; i < H.Length; i++) { hmin = Mathf.Min(hmin, H[i]); hmax = Mathf.Max(hmax, H[i]); }
                // normalise tone to a mean of 1: the ORBITAL colour is the average brightness of the ground, whichever
                // terrain type sits on it (otherwise grass came out darker and frost lighter than the map)
                double tsum = 0; for (int i = 0; i < T.Length; i++) tsum += T[i];
                float tmean = (float)(tsum / T.Length);
                if (tmean > 1e-3f) for (int i = 0; i < T.Length; i++) T[i] /= tmean;
                float texel = metres / n;
                // cavity AO: how far below its neighbourhood each texel sits (box average, radius ~6 texels)
                var avg = BoxBlur(H, n, 6);
                float aoK = 1f / Mathf.Max((hmax - hmin) * 0.35f, 1e-5f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int i = y * n + x;
                        float dx = (H[Idx(x + 1, y)] - H[Idx(x - 1, y)]) / (2f * texel);
                        float dz = (H[Idx(x, y + 1)] - H[Idx(x, y - 1)]) / (2f * texel);
                        var nn = new Vector3(-dx, 1f, -dz).normalized;
                        float ao = Mathf.Clamp01(1f - Mathf.Max(0f, avg[i] - H[i]) * aoK);
                        float hn = (H[i] - hmin) / Mathf.Max(hmax - hmin, 1e-6f);
                        a[i] = new Color32(B(nn.x * 0.5f + 0.5f), B(nn.z * 0.5f + 0.5f), B(hn), B(ao));
                        b[i] = new Color32(B(T[i] * 0.5f), B(R[i]), B(E[i]), B(M[i]));
                    }
            }
            static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
            static float[] BoxBlur(float[] src, int n, int r)
            {
                var tmp = new float[src.Length]; var dst = new float[src.Length]; float k = 1f / (2 * r + 1);
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    { float s = 0; for (int d = -r; d <= r; d++) s += src[y * n + ((x + d) % n + n) % n]; tmp[y * n + x] = s * k; }
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    { float s = 0; for (int d = -r; d <= r; d++) s += tmp[((y + d) % n + n) % n * n + x]; dst[y * n + x] = s * k; }
                return dst;
            }
        }

        // ── tileable noise (all periods are integers → seamless) ─────────────────────────────────────────
        static uint Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 144665);
                h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16);
            }
        }
        static float Rnd(int x, int y, int s) => (Hash(x, y, s) & 0xFFFFFF) / 16777216f;
        static int Wrap(int v, int p) => (v % p + p) % p;
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// Periodic gradient noise, periods px × py cells over the unit tile; ~[-1, 1].
        static float PN(float u, float v, int px, int py, int s)
        {
            float x = u * px, y = v * py;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float G(int ix, int iy, float ox, float oy)
            {
                float a = Rnd(Wrap(ix, px), Wrap(iy, py), s) * 6.2831853f;
                return Mathf.Cos(a) * ox + Mathf.Sin(a) * oy;
            }
            float n00 = G(x0, y0, fx, fy), n10 = G(x0 + 1, y0, fx - 1, fy), n01 = G(x0, y0 + 1, fx, fy - 1), n11 = G(x0 + 1, y0 + 1, fx - 1, fy - 1);
            float ux = Fade(fx), uy = Fade(fy);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, ux), Mathf.Lerp(n01, n11, ux), uy) * 1.41f;
        }
        static float Fbm(float u, float v, int p, int oct, int s, float gain = 0.5f)
        {
            float sum = 0, a = 1, norm = 0;
            for (int o = 0; o < oct; o++) { sum += a * PN(u, v, p, p, s + o * 101); norm += a; a *= gain; p *= 2; }
            return sum / norm;
        }
        static float Fbm2(float u, float v, int px, int py, int oct, int s)
        {
            float sum = 0, a = 1, norm = 0;
            for (int o = 0; o < oct; o++) { sum += a * PN(u, v, px, py, s + o * 101); norm += a; a *= 0.5f; px *= 2; py *= 2; }
            return sum / norm;
        }
        static float Ridged(float u, float v, int p, int oct, int s)
        {
            float sum = 0, a = 1, norm = 0;
            for (int o = 0; o < oct; o++) { float r = 1f - Mathf.Abs(PN(u, v, p, p, s + o * 101)); sum += a * r * r; norm += a; a *= 0.5f; p *= 2; }
            return sum / norm;
        }
        /// Periodic Worley: f1, f2 (cell units), the nearest cell's random id, and the vector to its feature point.
        static void Worley(float u, float v, int p, int s, out float f1, out float f2, out float id, out Vector2 toC)
        {
            float x = u * p, y = v * p; int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            f1 = f2 = 9f; id = 0; toC = Vector2.zero;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int cx = xi + i, cy = yi + j, wx = Wrap(cx, p), wy = Wrap(cy, p);
                    float px2 = cx + 0.1f + 0.8f * Rnd(wx, wy, s), py2 = cy + 0.1f + 0.8f * Rnd(wx, wy, s + 7);
                    float ddx = px2 - x, ddy = py2 - y, d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                    if (d < f1) { f2 = f1; f1 = d; id = Rnd(wx, wy, s + 13); toC = new Vector2(ddx, ddy); } else if (d < f2) f2 = d;
                }
        }
        static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        delegate void Pix(int i, float u, float v);
        static void ForEach(Canvas c, Pix f)
        {
            int n = c.n;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) f(y * n + x, (x + 0.5f) / n, (y + 0.5f) / n);
        }

        // Stamp one object (pebble / fragment / cushion) with an irregular outline, composited by HEIGHT (the taller
        // surface wins — stacking). e = superellipse exponent (2 round, ~1.2 angular), tilt = planar facet.
        static void Stamp(Canvas c, System.Random r, float cu, float cv, float rad, float elong, float ang, float height,
                          float bury, float tone, float rough, float e, float tilt, float toneNoise = 0.08f, float metal = 0f)
        {
            int n = c.n;
            float rx = rad, ry = rad * elong;
            int R = Mathf.CeilToInt(Mathf.Max(rx, ry) * 1.25f * n) + 1;
            int cx = Mathf.RoundToInt(cu * n), cy = Mathf.RoundToInt(cv * n);
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            // outline wobble: a few random harmonics
            float a1 = (float)r.NextDouble() * 0.12f, p1 = (float)r.NextDouble() * 6.28f, a2 = (float)r.NextDouble() * 0.08f, p2 = (float)r.NextDouble() * 6.28f;
            float a3 = (float)r.NextDouble() * 0.05f, p3 = (float)r.NextDouble() * 6.28f;
            float tx = ((float)r.NextDouble() - 0.5f) * tilt, ty = ((float)r.NextDouble() - 0.5f) * tilt;
            int seed = r.Next();
            for (int dy = -R; dy <= R; dy++)
                for (int dx = -R; dx <= R; dx++)
                {
                    float lx = (dx * ca + dy * sa) / n, ly = (-dx * sa + dy * ca) / n;
                    float th = Mathf.Atan2(ly, lx);
                    float wob = 1f + a1 * Mathf.Cos(2 * th + p1) + a2 * Mathf.Cos(3 * th + p2) + a3 * Mathf.Cos(5 * th + p3);
                    float qx = Mathf.Abs(lx / (rx * wob)), qy = Mathf.Abs(ly / (ry * wob));
                    float d = Mathf.Pow(Mathf.Pow(qx, e) + Mathf.Pow(qy, e), 1f / e);
                    if (d >= 1f) continue;
                    float prof = Mathf.Pow(1f - d * d, 0.55f);                                   // rounded shoulders
                    float h = prof * height - bury + (lx * tx + ly * ty) * height * 3f;
                    int i = c.Idx(cx + dx, cy + dy);
                    float hs = c.H[i];
                    if (h <= hs) continue;
                    c.H[i] = h;
                    float tn = 1f + (Rnd(dx + seed, dy, 3) - 0.5f) * toneNoise;
                    c.T[i] = tone * tn * (0.92f + 0.12f * prof);                                  // tops catch more light
                    c.R[i] = rough; c.M[i] = metal;
                }
        }
        static float PowerLaw(System.Random r, float min, float max, float k = 2.2f)
        {
            float u = (float)r.NextDouble();
            return min * Mathf.Pow(max / min, Mathf.Pow(u, k));                               // mostly small, few big
        }
        static float LogNormal(System.Random r, float sigma) => Mathf.Exp(((float)r.NextDouble() + (float)r.NextDouble() + (float)r.NextDouble() - 1.5f) * sigma);

        // ── generators ───────────────────────────────────────────────────────────────────────────────────
        static void Generate(int t, Canvas c)
        {
            var r = new System.Random(7919 * (t + 1));
            float m = c.metres;
            switch ((SurfaceGeo.TerrainType)t)
            {
                case SurfaceGeo.TerrainType.Rocky:
                case SurfaceGeo.TerrainType.Bouldery:  RockBase(c, 11, m, 1f); break;
                case SurfaceGeo.TerrainType.Gravel:    Gravel(c, r, m); break;
                case SurfaceGeo.TerrainType.Sandy:     Sand(c, 6, 0.0045f * m, 0.55f, 21); break;
                case SurfaceGeo.TerrainType.Dunes:     Sand(c, 7, 0.004f * m, 0.3f, 23); break;
                case SurfaceGeo.TerrainType.FineSoil:  Soil(c, r, m); break;
                case SurfaceGeo.TerrainType.DriedLake: Polygons(c, m, 5, 0, 0.004f, 0.02f, 0.004f, 0.6f, 31, 0.55f); break;
                case SurfaceGeo.TerrainType.Cracked:   Polygons(c, m, 3, 9, 0.024f, 0.08f, 0.02f, 0.85f, 33, 0.3f); break;
                case SurfaceGeo.TerrainType.MetallicRock: Metal(c, r, m); break;
                case SurfaceGeo.TerrainType.Molten:    Molten(c, m); break;
                case SurfaceGeo.TerrainType.IceSheet:  Ice(c, m); break;
                case SurfaceGeo.TerrainType.Snow:      Snow(c, m); break;
                case SurfaceGeo.TerrainType.Grassy:    Grass(c, r, m); break;
                case SurfaceGeo.TerrainType.Regolith:  Regolith(c, r, m); break;
                case SurfaceGeo.TerrainType.BasaltFlow: Basalt(c, m); break;
                case SurfaceGeo.TerrainType.SaltFlat:  Salt(c, m); break;
                case SurfaceGeo.TerrainType.Sulfur:    Sulfur(c, m); break;
                case SurfaceGeo.TerrainType.PatternedGround: Patterned(c, r, m); break;
                case SurfaceGeo.TerrainType.Scree:     Scree(c, r, m); break;
                case SurfaceGeo.TerrainType.Frost:     RockBase(c, 41, m, 0.7f); Frost(c); break;
                case SurfaceGeo.TerrainType.Mossy:     Moss(c, r, m); break;
            }
        }

        // Jointed, weathered bedrock: warped ridged relief, sparse irregular fractures, mineral mottling.
        static void RockBase(Canvas c, int s, float m, float amp)
        {
            ForEach(c, (i, u, v) =>
            {
                float wu = u + Fbm(u, v, 2, 3, s) * 0.08f, wv = v + Fbm(u, v, 2, 3, s + 5) * 0.08f;
                float rid = Ridged(wu, wv, 3, 6, s + 9);
                Worley(wu + Fbm(u, v, 4, 3, s + 21) * 0.04f, wv + Fbm(u, v, 4, 3, s + 23) * 0.04f, 3, s + 31, out float f1, out float f2, out float id, out _);
                float mask = SS(-0.1f, 0.3f, Fbm(u, v, 2, 3, s + 41));                    // only some joints open
                float w = 0.02f + 0.03f * (Fbm(u, v, 8, 2, s + 43) * 0.5f + 0.5f);
                float crack = (1f - SS(0f, w, f2 - f1)) * mask;
                float fine = Fbm(u, v, 32, 4, s + 51);
                c.H[i] = (rid * 0.45f + fine * 0.04f - crack * 0.12f + (id - 0.5f) * 0.06f) * m * 0.06f * amp;
                c.T[i] = (0.82f + rid * 0.22f + Fbm(u, v, 10, 4, s + 61) * 0.1f + (id - 0.5f) * 0.08f) * (1f - crack * 0.45f)
                       * (1f + SS(0.55f, 0.75f, Fbm(u, v, 24, 2, s + 71)) * 0.12f);          // mineral speckle
                c.R[i] = 0.7f - rid * 0.15f + crack * 0.2f;
            });
        }

        // Desert pavement: a bed of fine grit with hundreds of stacked, partly buried, varnished pebbles.
        static void Gravel(Canvas c, System.Random r, float m)
        {
            ForEach(c, (i, u, v) => { c.H[i] = Fbm(u, v, 16, 4, 3) * 0.004f; c.T[i] = 0.88f + Fbm(u, v, 64, 3, 5) * 0.1f; c.R[i] = 0.95f; });
            for (int k = 0; k < 1400; k++)
            {
                float rad = PowerLaw(r, 0.006f, 0.045f);
                float tone = Mathf.Clamp(LogNormal(r, 0.35f), 0.55f, 1.6f);
                Stamp(c, r, (float)r.NextDouble(), (float)r.NextDouble(), rad, Mathf.Lerp(0.55f, 1f, (float)r.NextDouble()),
                      (float)r.NextDouble() * 6.28f, rad * m * 0.55f, rad * m * 0.15f, tone, Mathf.Lerp(0.35f, 0.8f, (float)r.NextDouble()), 2.2f, 0.25f);
            }
        }

        // Aeolian sand: smooth, gently warped, amplitude-modulated asymmetric ripples with the odd Y-junction defect,
        // on soft grain-scale mottling (no hard speckle).
        static void Sand(Canvas c, int waves, float amp, float warpK, int s)
        {
            ForEach(c, (i, u, v) =>
            {
                float warp = Fbm(u, v, 1, 3, s) * 0.5f * warpK + Fbm(u, v, 3, 3, s + 3) * 0.06f * warpK;
                float ph = (v + warp) * waves;
                ph -= Mathf.Floor(ph);
                float prof = ph < 0.7f ? ph / 0.7f : (1f - ph) / 0.3f;                      // gentle stoss, steep lee
                prof = prof * prof * (3f - 2f * prof);
                float env = SS(-0.45f, 0.35f, Fbm(u, v, 5, 3, s + 7));                         // ripple fields come and go
                float mott = Fbm(u, v, 48, 3, s + 9);
                c.H[i] = prof * amp * (0.35f + 0.65f * env) + Fbm(u, v, 4, 3, s + 11) * amp * 0.8f;
                c.T[i] = 0.96f + prof * 0.05f * env + mott * 0.04f;
                c.R[i] = 0.95f;
            });
        }

        // Fine soil: crumbly clods and crumbs, darker damp hollows.
        static void Soil(Canvas c, System.Random r, float m)
        {
            ForEach(c, (i, u, v) => { c.H[i] = Fbm(u, v, 4, 5, 51) * 0.01f * m; c.T[i] = 0.82f + Fbm(u, v, 8, 4, 53) * 0.15f; c.R[i] = 0.93f; });
            for (int k = 0; k < 1100; k++)   // soft crumbs, low and mostly buried (pebbles belong to Gravel)
            {
                float rad = PowerLaw(r, 0.004f, 0.022f, 1.8f);
                Stamp(c, r, (float)r.NextDouble(), (float)r.NextDouble(), rad, Mathf.Lerp(0.6f, 1f, (float)r.NextDouble()),
                      (float)r.NextDouble() * 6.28f, rad * m * 0.14f, rad * m * 0.06f, Mathf.Lerp(0.82f, 1.05f, (float)r.NextDouble()), 0.95f, 1.8f, 0.3f, 0.15f);
            }
        }

        // Crack networks: primary polygons (warped, curved edges), optional secondary subdivision, varying crack width,
        // plates curled up at their edges and tilted individually, fine surface texture. Dried lake = broad shallow
        // polygons on smooth hardpan; cracked = deep hierarchical desiccation cracks.
        static void Polygons(Canvas c, float m, int p1, int p2, float w1, float depth, float curl, float rough, int s, float smoothTone)
        {
            ForEach(c, (i, u, v) =>
            {
                float wu = u + Fbm(u, v, 3, 3, s) * 0.05f, wv = v + Fbm(u, v, 3, 3, s + 1) * 0.05f;
                Worley(wu, wv, p1, s + 5, out float f1, out float f2, out float id, out Vector2 toC);
                float wv1 = w1 * (0.55f + 0.9f * (Fbm(u, v, 12, 2, s + 9) * 0.5f + 0.5f));
                float e1 = f2 - f1;
                float crack = 1f - SS(0f, wv1, e1);
                float e2 = 9f, crack2 = 0f;
                if (p2 > 0)
                {
                    Worley(wu + Fbm(u, v, 9, 2, s + 11) * 0.02f, wv + Fbm(u, v, 9, 2, s + 13) * 0.02f, p2, s + 17, out float g1, out float g2, out _, out _);
                    e2 = g2 - g1; crack2 = (1f - SS(0f, wv1 * 0.45f * p1 / p2 * 3f, e2)) * SS(0.2f, 0.5f, Fbm(u, v, 4, 2, s + 19) + 0.3f);
                }
                float crack3 = 0f;
                if (p2 > 0)                                                                      // fine crazing inside the plates
                {
                    int p3 = p2 * 2 + 3;
                    Worley(wu + Fbm(u, v, 14, 2, s + 35) * 0.012f, wv + Fbm(u, v, 14, 2, s + 36) * 0.012f, p3, s + 37, out float k1, out float k2, out _, out _);
                    crack3 = (1f - SS(0f, wv1 * p3 / p1 * 0.3f, k2 - k1)) * SS(-0.1f, 0.45f, Fbm(u, v, 6, 2, s + 39)) * 0.55f;
                }
                float edge = Mathf.Min(e1, e2 * p1 / Mathf.Max(p2, 1));
                float lift = curl * (1f - SS(0f, 0.25f, edge));                                   // curled plate margins
                float tiltPlate = (toC.x * (id - 0.5f) + toC.y * (Rnd((int)(id * 1000), 3, s) - 0.5f)) * curl * 0.6f;
                float cr = Mathf.Max(crack, Mathf.Max(crack2 * 0.8f, crack3));
                c.H[i] = (lift + tiltPlate + Fbm(u, v, 40, 3, s + 23) * 0.002f - cr * depth) * m;
                c.T[i] = (1f + (id - 0.5f) * 0.08f + Fbm(u, v, 6, 3, s + 29) * 0.06f) * (1f - cr * 0.55f)
                       * (1f + SS(0.4f, 0.8f, Fbm(u, v, 3, 3, s + 31)) * (1f - smoothTone) * 0.1f);
                c.R[i] = rough + cr * 0.1f;
            });
        }

        // Iron-nickel meteorite: thumbprint regmaglypts on a smooth metal skin, with rust blooms.
        static void Metal(Canvas c, System.Random r, float m)
        {
            ForEach(c, (i, u, v) => { c.H[i] = Fbm(u, v, 3, 4, 71) * 0.01f * m; c.T[i] = 1f; c.R[i] = 0.3f; c.M[i] = 1f; });
            // regmaglypts: overlapping shallow dimples (min-composite)
            int n = c.n;
            for (int k = 0; k < 260; k++)
            {
                float rad = PowerLaw(r, 0.02f, 0.09f, 1.3f), cu = (float)r.NextDouble(), cv = (float)r.NextDouble();
                int R = Mathf.CeilToInt(rad * n), cx = (int)(cu * n), cy = (int)(cv * n);
                for (int dy = -R; dy <= R; dy++) for (int dx = -R; dx <= R; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / (rad * n); if (d >= 1f) continue;
                    int i = c.Idx(cx + dx, cy + dy);
                    float dip = -(1f - d * d) * rad * m * 0.12f;
                    c.H[i] = Mathf.Min(c.H[i], c.H[i] * 0.3f + dip);
                }
            }
            ForEach(c, (i, u, v) =>
            {
                float rust = SS(0.2f, 0.6f, Fbm(u, v, 7, 5, 77) + Fbm(u, v, 16, 3, 79) * 0.25f);   // small patches only: big ones read as a repeat
                c.M[i] = 1f - rust;
                c.R[i] = Mathf.Lerp(0.28f, 0.85f, rust) + Fbm(u, v, 24, 2, 81) * 0.05f;
                c.T[i] = Mathf.Lerp(1f + Fbm(u, v, 8, 3, 83) * 0.06f, 0.7f + Fbm(u, v, 12, 3, 85) * 0.2f, rust);
            });
        }

        // Live lava: crust broken on THREE scales (big warped plates, secondary fractures, fine crazing), crack widths
        // varying along their length; pahoehoe ropes are flow-aligned wrinkles, not rings. Seams glow.
        static void Molten(Canvas c, float m)
        {
            ForEach(c, (i, u, v) =>
            {
                float wu = u + Fbm(u, v, 2, 4, 91) * 0.12f, wv = v + Fbm(u, v, 2, 4, 93) * 0.12f;
                Worley(wu, wv, 3, 95, out float f1, out float f2, out float id, out _);
                Worley(wu + Fbm(u, v, 6, 3, 96) * 0.03f, wv + Fbm(u, v, 6, 3, 98) * 0.03f, 8, 97, out float g1, out float g2, out _, out _);
                Worley(wu, wv, 21, 99, out float k1, out float k2, out _, out _);
                float wv1 = 0.05f * (0.4f + 1.2f * (Fbm(u, v, 5, 2, 100) * 0.5f + 0.5f));
                float s1 = 1f - SS(0f, wv1, f2 - f1);
                float s2 = (1f - SS(0f, wv1 * 0.5f, g2 - g1)) * SS(-0.2f, 0.3f, Fbm(u, v, 3, 2, 102));
                float s3 = (1f - SS(0f, 0.04f, k2 - k1)) * SS(0f, 0.4f, Fbm(u, v, 5, 2, 104)) * 0.6f;
                float seam = Mathf.Max(s1, Mathf.Max(s2 * 0.85f, s3));
                float flow = v + Fbm(u, v, 2, 3, 106) * 0.2f;
                float rope = Mathf.Sin(flow * 90f + Fbm(u, v, 10, 2, 108) * 3f) * 0.5f + 0.5f;
                rope *= SS(-0.1f, 0.4f, Fbm(u, v, 3, 2, 110)) * (1f - seam);
                c.H[i] = ((1f - seam) * (0.04f + (id - 0.5f) * 0.02f) + rope * 0.006f + Fbm(u, v, 24, 3, 112) * 0.005f - seam * 0.035f) * m;
                c.E[i] = Mathf.Pow(Mathf.Max(s1, s2 * 0.7f), 1.4f) + s3 * 0.15f;
                c.T[i] = 0.85f + rope * 0.1f + Fbm(u, v, 12, 3, 114) * 0.1f;
                c.R[i] = 0.4f + (1f - rope) * 0.15f;
            });
        }

        // Glacial ice: glassy, gently scalloped, thin irregular fractures, trapped bubbles.
        static void Ice(Canvas c, float m)
        {
            ForEach(c, (i, u, v) =>
            {
                Worley(u, v, 9, 111, out float f1, out _, out _, out _);
                float scallop = f1 * f1 * 0.004f;
                float wu = u + Fbm(u, v, 3, 3, 113) * 0.04f, wv = v + Fbm(u, v, 3, 3, 115) * 0.04f;
                Worley(wu, wv, 3, 117, out float g1, out float g2, out _, out _);
                float frac = (1f - SS(0f, 0.012f, g2 - g1)) * SS(-0.2f, 0.3f, Fbm(u, v, 2, 2, 119));
                float bub = Rnd((int)(u * 2048), (int)(v * 2048), 121) > 0.992f ? 1f : 0f;
                c.H[i] = (scallop + Fbm(u, v, 2, 4, 123) * 0.01f - frac * 0.006f) * m;
                c.T[i] = 1f + frac * 0.35f + bub * 0.3f + Fbm(u, v, 4, 3, 125) * 0.05f;
                c.R[i] = 0.12f + frac * 0.4f;
            });
        }

        // Snow: broad, soft wind-drift dunes with gently curving sastrugi (not straight lines), fine sparkle is in the shader.
        static void Snow(Canvas c, float m)
        {
            ForEach(c, (i, u, v) =>
            {
                float wu = u + Fbm(u, v, 1, 3, 131) * 0.25f, wv = v + Fbm(u, v, 2, 3, 132) * 0.12f;
                float sast = 1f - Mathf.Abs(Fbm2(wu, wv, 2, 6, 3, 133));
                sast = Mathf.Pow(sast, 4f) * SS(-0.3f, 0.4f, Fbm(u, v, 2, 2, 135));
                float drift = Fbm(u, v, 1, 4, 137);
                c.H[i] = (drift * 0.03f + sast * 0.01f + Fbm(u, v, 12, 3, 138) * 0.0015f) * m;
                c.T[i] = 1f - (1f - sast) * 0.03f + Fbm(u, v, 24, 2, 139) * 0.015f;
                c.R[i] = 0.8f;
            });
        }

        // Grass: dense, overlapping tapered blades leaning mostly one way (wind), in clumps with shadowed soil gaps;
        // per-blade tone (fresh / dry / shaded) so it reads as a sward, not stars.
        static void Grass(Canvas c, System.Random r, float m)
        {
            ForEach(c, (i, u, v) => { c.H[i] = Fbm(u, v, 4, 3, 141) * 0.004f * m; c.T[i] = 0.42f + Fbm(u, v, 8, 3, 143) * 0.08f; c.R[i] = 0.95f; });
            int n = c.n;
            float lean = 0.9f;
            for (int k = 0; k < 2600; k++)                                                   // clumps
            {
                float cu = (float)r.NextDouble(), cv = (float)r.NextDouble();
                if (Fbm(cu, cv, 3, 3, 145) < -0.35f) continue;                                // bare patches
                int blades = 10 + r.Next(18);
                float clumpR = Mathf.Lerp(0.004f, 0.018f, (float)r.NextDouble());
                float clumpLean = lean + ((float)r.NextDouble() - 0.5f) * 1.2f;
                for (int b = 0; b < blades; b++)
                {
                    float bu = cu + ((float)r.NextDouble() - 0.5f) * clumpR, bv = cv + ((float)r.NextDouble() - 0.5f) * clumpR;
                    float a = clumpLean + ((float)r.NextDouble() - 0.5f) * 0.9f;
                    float l = Mathf.Lerp(0.02f, 0.07f, (float)r.NextDouble());
                    float tone = (float)r.NextDouble() < 0.12f ? Mathf.Lerp(1.3f, 1.6f, (float)r.NextDouble())   // sun-dried blade
                                                              : Mathf.Lerp(0.75f, 1.15f, (float)r.NextDouble());
                    int steps = Mathf.CeilToInt(l * n * 1.4f);
                    for (int st = 0; st <= steps; st++)
                    {
                        float t = st / (float)steps;
                        float bend = t * t * 0.25f;                                              // tips droop
                        int x = Mathf.RoundToInt((bu + Mathf.Cos(a + bend) * l * t) * n), y = Mathf.RoundToInt((bv + Mathf.Sin(a + bend) * l * t) * n);
                        float h = (0.4f + 0.6f * t) * l * m * 0.25f;                              // blades rise toward the tip
                        for (int w = 0; w <= (t < 0.5f ? 1 : 0); w++)                             // tapered: 2 px at the base, 1 at the tip
                        {
                            int i = c.Idx(x + w, y);
                            if (h > c.H[i]) { c.H[i] = h; c.T[i] = tone * (0.75f + 0.35f * t); c.R[i] = 0.8f; }
                        }
                    }
                }
            }
        }

        // Regolith: impact-gardened dust — a power-law population of soft craters with rims, and scattered clasts.
        static void Regolith(Canvas c, System.Random r, float m)
        {
            ForEach(c, (i, u, v) => { c.H[i] = Fbm(u, v, 4, 5, 151) * 0.006f * m; c.T[i] = 0.95f + Fbm(u, v, 32, 3, 153) * 0.05f; c.R[i] = 1f; });
            int n = c.n;
            for (int k = 0; k < 900; k++)
            {
                float rad = PowerLaw(r, 0.005f, 0.06f, 2.2f), cu = (float)r.NextDouble(), cv = (float)r.NextDouble();
                float depth = rad * m * Mathf.Lerp(0.08f, 0.2f, (float)r.NextDouble()) * (float)r.NextDouble() + rad * m * 0.03f;
                int R = Mathf.CeilToInt(rad * 1.6f * n), cx = (int)(cu * n), cy = (int)(cv * n);
                bool fresh = r.NextDouble() < 0.15;
                for (int dy = -R; dy <= R; dy++) for (int dx = -R; dx <= R; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / (rad * n); if (d > 1.6f) continue;
                    int i = c.Idx(cx + dx, cy + dy);
                    float bowl = d < 1f ? -(1f - d * d) : 0f;
                    float rim = Mathf.Exp(-12f * (d - 1f) * (d - 1f)) * 0.3f;
                    c.H[i] += (bowl + rim) * depth;
                    if (fresh) c.T[i] *= 1f + rim * 0.5f + (d < 1.5f ? 0.08f : 0f);        // fresh ejecta is brighter
                }
            }
            for (int k = 0; k < 120; k++)
            {
                float rad = PowerLaw(r, 0.004f, 0.02f);
                Stamp(c, r, (float)r.NextDouble(), (float)r.NextDouble(), rad, 0.8f, (float)r.NextDouble() * 6.28f, rad * m * 0.5f, 0f, 0.8f, 0.9f, 1.4f, 0.5f);
            }
        }

        // Basalt flow (cooled): overlapping pāhoehoe TOES — elongated domed lobes stacked down-flow, each with its own
        // cross-flow ropy wrinkles and a glassy skin — and rubbly ʻaʻā clinker fields of angular fragments. No glow.
        static void Basalt(Canvas c, float m)
        {
            var r = new System.Random(161);
            ForEach(c, (i, u, v) => { c.H[i] = Fbm(u, v, 3, 4, 163) * 0.01f * m; c.T[i] = 0.8f; c.R[i] = 0.6f; });
            for (int k = 0; k < 170; k++)                                                   // toes, flowing roughly +v
            {
                float rad = PowerLaw(r, 0.04f, 0.14f, 1.4f);
                float ang = 1.5708f + ((float)r.NextDouble() - 0.5f) * 1.1f;
                Stamp(c, r, (float)r.NextDouble(), (float)r.NextDouble(), rad, Mathf.Lerp(0.45f, 0.7f, (float)r.NextDouble()), ang,
                      rad * m * 0.28f, rad * m * 0.04f, Mathf.Lerp(0.85f, 1.1f, (float)r.NextDouble()), 0.35f, 2.2f, 0.15f, 0.05f);
            }
            ForEach(c, (i, u, v) =>
            {
                float wr = Fbm(u, v, 4, 2, 165) * 0.08f;
                float rope = Mathf.Sin((v + wr) * 140f) * 0.5f + 0.5f;                            // cross-flow wrinkles on the toes
                c.H[i] += rope * rope * 0.0025f * m * SS(0.3f, 0.7f, c.R[i] < 0.5f ? 1f : 0f);
                c.T[i] *= 0.95f + rope * 0.08f;
                float aa = SS(0.15f, 0.45f, Fbm(u, v, 2, 3, 169));                             // clinker fields
                if (aa > 0.01f)
                {
                    float clink = Ridged(u, v, 18, 4, 171);
                    Worley(u, v, 30, 173, out float f1, out float f2, out float id, out _);
                    float block = (1f - SS(0f, 0.12f, f2 - f1));
                    c.H[i] = Mathf.Lerp(c.H[i], (clink * 0.03f + (id - 0.5f) * 0.012f - block * 0.01f) * m, aa);
                    c.T[i] = Mathf.Lerp(c.T[i], (0.7f + clink * 0.35f) * (1f - block * 0.4f), aa);
                    c.R[i] = Mathf.Lerp(c.R[i], 0.92f, aa);
                }
            });
        }

        // Salt flat: pressure ridges thrust up along polygon edges, crystalline crust.
        static void Salt(Canvas c, float m)
        {
            ForEach(c, (i, u, v) =>
            {
                float wu = u + Fbm(u, v, 3, 3, 181) * 0.03f, wv = v + Fbm(u, v, 3, 3, 183) * 0.03f;
                Worley(wu, wv, 5, 185, out float f1, out float f2, out float id, out _);
                float w = 0.05f + 0.05f * (Fbm(u, v, 10, 2, 187) * 0.5f + 0.5f);
                float ridge = 1f - SS(0f, w, f2 - f1);
                float crest = ridge * (0.7f + 0.3f * (Fbm(u, v, 40, 2, 189) * 0.5f + 0.5f));
                c.H[i] = (crest * 0.015f + Fbm(u, v, 30, 3, 191) * 0.0015f) * m;
                c.T[i] = 1f + crest * 0.08f + (id - 0.5f) * 0.04f + Fbm(u, v, 6, 3, 193) * 0.05f;
                c.R[i] = 0.55f - crest * 0.2f;
            });
        }

        // Sulfur: crystalline crust of small flat facets with blistered patches.
        static void Sulfur(Canvas c, float m)
        {
            ForEach(c, (i, u, v) =>
            {
                Worley(u, v, 22, 201, out float f1, out float f2, out float id, out Vector2 toC);
                float facet = (toC.x * (id - 0.5f) + toC.y * (Rnd((int)(id * 997), 1, 203) - 0.5f)) * 0.6f;
                float blister = SS(0.2f, 0.6f, Fbm(u, v, 4, 4, 205));
                c.H[i] = (facet * 0.004f + blister * 0.012f + Fbm(u, v, 2, 3, 207) * 0.01f) * m;
                c.T[i] = 0.9f + (id - 0.5f) * 0.25f + blister * 0.1f;
                c.R[i] = 0.45f + (1f - SS(0f, 0.08f, f2 - f1)) * 0.3f;
            });
        }

        // Patterned ground: sorted-stone polygons — stones piled along the borders, fine domed centres.
        static void Patterned(Canvas c, System.Random r, float m)
        {
            var border = new float[c.n * c.n];
            ForEach(c, (i, u, v) =>
            {
                float wu = u + Fbm(u, v, 3, 3, 211) * 0.04f, wv = v + Fbm(u, v, 3, 3, 213) * 0.04f;
                Worley(wu, wv, 4, 215, out float f1, out float f2, out _, out _);
                border[i] = 1f - SS(0.02f, 0.18f, f2 - f1);
                c.H[i] = ((1f - border[i]) * 0.006f + Fbm(u, v, 16, 3, 217) * 0.002f) * m;
                c.T[i] = 0.8f + Fbm(u, v, 12, 3, 219) * 0.1f; c.R[i] = 0.95f;
            });
            int n = c.n;
            for (int k = 0; k < 3200; k++)
            {
                float cu = (float)r.NextDouble(), cv = (float)r.NextDouble();
                if (border[c.Idx((int)(cu * n), (int)(cv * n))] < (float)r.NextDouble()) continue;
                float rad = PowerLaw(r, 0.004f, 0.018f);
                Stamp(c, r, cu, cv, rad, Mathf.Lerp(0.6f, 1f, (float)r.NextDouble()), (float)r.NextDouble() * 6.28f,
                      rad * m * 0.5f, rad * m * 0.1f, Mathf.Clamp(LogNormal(r, 0.3f), 0.6f, 1.5f), 0.75f, 2f, 0.3f);
            }
        }

        // Scree: stacked angular fragments with flat facets.
        static void Scree(Canvas c, System.Random r, float m)
        {
            ForEach(c, (i, u, v) => { c.H[i] = Fbm(u, v, 4, 3, 221) * 0.01f * m; c.T[i] = 0.7f; c.R[i] = 0.9f; });
            for (int k = 0; k < 1300; k++)
            {
                float rad = PowerLaw(r, 0.01f, 0.07f, 1.8f);
                Stamp(c, r, (float)r.NextDouble(), (float)r.NextDouble(), rad, Mathf.Lerp(0.45f, 1f, (float)r.NextDouble()),
                      (float)r.NextDouble() * 6.28f, rad * m * 0.35f, 0f, Mathf.Clamp(LogNormal(r, 0.25f), 0.65f, 1.4f),
                      Mathf.Lerp(0.55f, 0.85f, (float)r.NextDouble()), 1.15f, 1.2f, 0.12f);
            }
        }

        // Frost: hoarfrost gathering in the hollows of the rock beneath.
        static void Frost(Canvas c)
        {
            var avg = new float[c.H.Length];
            ForEach(c, (i, u, v) => avg[i] = c.H[i]);
            float hmin = float.MaxValue, hmax = float.MinValue;
            foreach (var h in c.H) { hmin = Mathf.Min(hmin, h); hmax = Mathf.Max(hmax, h); }
            ForEach(c, (i, u, v) =>
            {
                float low = 1f - (c.H[i] - hmin) / Mathf.Max(hmax - hmin, 1e-6f);
                float frost = SS(0.35f, 0.7f, low + Fbm(u, v, 16, 3, 231) * 0.25f);
                float crystal = Rnd((int)(u * 2048), (int)(v * 2048), 233) > 0.97f ? 0.25f : 0f;
                c.T[i] = Mathf.Lerp(c.T[i], 1.5f + crystal, frost);
                c.R[i] = Mathf.Lerp(c.R[i], 0.55f, frost);
            });
        }

        // Moss: soft rounded cushions with a fine fuzzy surface.
        static void Moss(Canvas c, System.Random r, float m)
        {
            ForEach(c, (i, u, v) => { c.H[i] = Fbm(u, v, 4, 3, 241) * 0.005f * m; c.T[i] = 0.6f; c.R[i] = 0.98f; });
            for (int k = 0; k < 1100; k++)
            {
                float rad = PowerLaw(r, 0.015f, 0.09f, 1.5f);
                Stamp(c, r, (float)r.NextDouble(), (float)r.NextDouble(), rad, Mathf.Lerp(0.7f, 1f, (float)r.NextDouble()),
                      (float)r.NextDouble() * 6.28f, rad * m * 0.6f, rad * m * 0.05f, Mathf.Lerp(0.8f, 1.25f, (float)r.NextDouble()), 0.98f, 2f, 0.1f, 0.3f);
            }
            ForEach(c, (i, u, v) => { float f = Fbm(u, v, 96, 2, 243); c.H[i] += f * 0.0015f * m; c.T[i] *= 0.9f + f * 0.2f; });
        }
    }
}
