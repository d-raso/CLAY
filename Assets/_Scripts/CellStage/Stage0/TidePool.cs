using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// The pool basin and its rhythms. A baked heightfield (bowl + rock outcrops + hollows) under a TIDE that rises
    /// and falls: at low tide the water shrinks into the hollows, which are the crevices that let a protocell ride
    /// out the dry phase. A day/night cycle drives UV, which deep water shields. All local, all deterministic
    /// from the pool seed. World units ≈ micrometres in spirit; the arena is W × H.
    /// </summary>
    public sealed class TidePool
    {
        public const float W = 192f, H = 120f;
        const int TW = 512, TH = 320;

        readonly float[] h = new float[TW * TH];
        public readonly Texture2D heightTex;
        readonly CellStageContext ctx;
        public readonly TidePoolBiome biome;   // this pool's biome (drives basin, tide, UV, colours, formations, soup)
        public float time;
        public readonly float tidePeriod, dayPeriod;
        readonly float tideMid, tideAmp;

        public TidePool(CellStageContext c)
        {
            biome = TidePoolBiome.For(c);
            ctx = c;
            var r = new DetRng(c.poolSeed);
            float ox = r.Range(0f, 100f), oy = r.Range(0f, 100f);
            for (int j = 0; j < TH; j++)
                for (int i = 0; i < TW; i++)
                {
                    float x = (i + 0.5f) / TW * W, y = (j + 0.5f) / TH * H;
                    // bowl: low in the middle, rising to a rocky rim
                    float ex = (x / W - 0.5f) * 2f, ey = (y / H - 0.5f) * 2f;
                    float bowl = Mathf.Pow(Mathf.Clamp01(Mathf.Sqrt(ex * ex * 0.9f + ey * ey)), 2.2f) * 0.75f * biome.bowlDepth;
                    float n = Fbm(x * 0.06f + ox, y * 0.06f + oy, 4);
                    float rocks = Mathf.Pow(Mathf.Clamp01(Fbm(x * 0.11f + oy, y * 0.11f + ox, 3) * 1.6f - 0.6f), 1.5f) * 0.45f * biome.rockiness;   // outcrops
                    float hollows = -Mathf.Pow(Mathf.Clamp01(Fbm(x * 0.15f + 31f, y * 0.15f + 17f, 3) * 1.7f - 0.85f), 1.2f) * 0.25f;
                    h[j * TW + i] = Mathf.Clamp01(0.18f + bowl + (n - 0.5f) * 0.3f + rocks + hollows);
                }
            heightTex = new Texture2D(TW, TH, TextureFormat.RFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "TidePoolHeight" };
            heightTex.SetPixelData(h, 0); heightTex.Apply(false, false);

            tidePeriod = Mathf.Lerp(150f, 70f, c.tidalRange);             // strong tides cycle faster
            dayPeriod = 200f;
            tideMid = 0.5f; tideAmp = Mathf.Lerp(0.08f, 0.22f, c.tidalRange) * biome.tideMul;
        }

        public void Tick(float dt) { time += dt; }

        /// Water level in height units (the pool is wet wherever Height < Tide).
        public float Tide => tideMid + tideAmp * Mathf.Sin(time * 2f * Mathf.PI / tidePeriod + 1.2f);
        /// −1 = deepest ebb … +1 = highest flood.
        public float TidePhase => Mathf.Sin(time * 2f * Mathf.PI / tidePeriod + 1.2f);
        public float Daylight => Mathf.Clamp01(Mathf.Sin(time * 2f * Mathf.PI / dayPeriod) * 1.4f + 0.3f);

        public float Height(Vector2 p)
        {
            float fx = Mathf.Clamp(p.x / W * TW - 0.5f, 0f, TW - 1.001f), fy = Mathf.Clamp(p.y / H * TH - 0.5f, 0f, TH - 1.001f);
            int i = (int)fx, j = (int)fy; float tx = fx - i, ty = fy - j;
            float a = h[j * TW + i], b = h[j * TW + i + 1], c = h[(j + 1) * TW + i], d = h[(j + 1) * TW + i + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// Water depth (height units) at p; ≤ 0 means dry ground.
        public float Depth(Vector2 p) => Tide - Height(p);
        public bool Wet(Vector2 p) => Depth(p) > 0.003f;

        /// Downhill direction (motes and stranded cells drain into hollows as the water shrinks).
        public Vector2 Downhill(Vector2 p)
        {
            const float e = 0.6f;
            float gx = Height(p + new Vector2(e, 0)) - Height(p - new Vector2(e, 0));
            float gy = Height(p + new Vector2(0, e)) - Height(p - new Vector2(0, e));
            return -new Vector2(gx, gy) / (2f * e);
        }

        /// UV dose rate at p: daylight × planet UV, shielded by water depth.
        public float UV(Vector2 p) => ctx.uv * biome.uvMul * Daylight * Mathf.Exp(-Mathf.Max(Depth(p), 0f) * 14f);

        public Vector2 RandomWetPoint(ref DetRng r, float minDepth = 0.02f)
        {
            for (int k = 0; k < 200; k++)
            {
                var p = new Vector2(r.Range(2f, W - 2f), r.Range(2f, H - 2f));
                if (Depth(p) > minDepth) return p;
            }
            return new Vector2(W * 0.5f, H * 0.5f);
        }

        // ── noise ──
        static float Hash(int x, int y) { unchecked { uint n = (uint)(x * 374761393 + y * 668265263); n = (n ^ (n >> 13)) * 1274126177u; return ((n ^ (n >> 16)) & 0xFFFFFF) / 16777216f; } }
        public static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx), Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx), fy);
        }
        public static float Fbm(float x, float y, int oct)
        {
            float s = 0f, a = 0.5f, n = 0f;
            for (int k = 0; k < oct; k++) { s += a * Noise(x, y); n += a; x = x * 2.03f + 7.1f; y = y * 2.03f + 3.7f; a *= 0.5f; }
            return s / n;
        }
    }
}
