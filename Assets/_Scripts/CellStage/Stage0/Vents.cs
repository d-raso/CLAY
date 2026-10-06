using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// The pool's mineral vents (how many, how strong and which style come from the TidePoolBiome). Risk/reward
    /// places, never labelled:
    ///  • BLACK SMOKERS (vent field): dark sulfide spire clusters. The warm PLUME speeds chemistry and pumps out
    ///    fresh bases and clay; the hot CORE breaks chains fast. Thermal SURGES periodically widen the hot zone.
    ///  • CARBONATE TOWERS (alkaline hot spring, "Lost City"): tall white/cream spires with milky upwelling. Gentle —
    ///    no killing heat — and rich in fatty acids; their pH gradient is what holds membranes together nearby.
    /// The mounds are solid rock (things are pushed out of them).
    /// </summary>
    public sealed class Vents
    {
        public enum Style { BlackSmoker, CarbonateTower }

        public sealed class Vent
        {
            public Vector2 pos;
            public float strength;      // 0..1
            public float coreR, plumeR;
            public float seed;
        }

        public readonly List<Vent> list = new();
        public readonly Style style;
        readonly bool pulses;
        float surge;                    // 0..1, thermal surge (black smokers)

        public Vents(TidePool pool, CellStageContext ctx)
        {
            var b = pool.biome;
            style = b.ventStyle; pulses = b.thermalPulses;
            var r = new DetRng(DetRng.Hash(ctx.poolSeed, 0x7E47UL));
            for (int i = 0; i < b.ventCount; i++)
            {
                var p = pool.RandomWetPoint(ref r, 0.09f);           // vents sit in the deeper parts
                float s = Mathf.Clamp01(b.ventStrength + ctx.ventActivity * 0.3f + r.Range(-0.15f, 0.15f));
                float core = (style == Style.CarbonateTower ? 3.5f + s * 3f : 4.5f + s * 4.5f) * 1.5f;   // massive next to a cell
                list.Add(new Vent { pos = p, strength = s, coreR = core, plumeR = 24f + s * 22f, seed = r.Range(0f, 100f) });
            }
        }

        public void Tick(float t)
        {
            // thermal surges: every ~40 s the smokers flare for a few seconds
            surge = pulses ? Mathf.Pow(Mathf.Clamp01(Mathf.Sin(t * 0.155f) * 4f - 3f), 2f) : 0f;
        }

        /// Plume influence (0..1) and core heat (0..1) at a position. Carbonate towers aren't hot.
        public void At(Vector2 p, out float plume, out float core)
        {
            plume = 0f; core = 0f;
            foreach (var v in list)
            {
                float d = (p - v.pos).magnitude;
                plume = Mathf.Max(plume, v.strength * Mathf.Clamp01(1f - d / v.plumeR));
                if (style == Style.BlackSmoker)
                    core = Mathf.Max(core, v.strength * Mathf.Clamp01(1f - d / (v.coreR * (1.6f + surge * 1.4f))) * (1f + surge));
            }
        }

        /// The mineral mound is solid: anything inside its footprint is pushed back out to the edge.
        public Vector2 PushOut(Vector2 p, float radius)
        {
            foreach (var v in list)
            {
                float solid = v.coreR * 0.85f + radius;
                Vector2 d = p - v.pos; float m = d.magnitude;
                if (m < solid) p = v.pos + (m > 1e-4f ? d / m : Vector2.right) * solid;
            }
            return p;
        }

        /// The vents keep producing building blocks (towers: fatty acids especially).
        public void Emit(MoteField field, float dt, ref DetRng r)
        {
            foreach (var v in list)
            {
                if (r.Value > dt * 6f * v.strength) continue;
                var at = v.pos + r.InsideUnitCircle() * v.plumeR * 0.5f;
                MoteKind kind = style == Style.CarbonateTower
                    ? (r.Value < 0.5f ? MoteKind.Lipid : (MoteKind)r.RangeInt(0, 4))
                    : (r.Value < 0.25f ? MoteKind.Clay : (MoteKind)r.RangeInt(0, 4));
                field.motes.Add(new Mote { pos = at, kind = kind, alive = true, rot = r.Range(0f, 6.28f), spin = r.Range(-1f, 1f) });
            }
        }

        public void Render(QuadBatch under, QuadBatch over, float t, float day)
        {
            // the chimneys themselves are drawn by Features (CellFeature.shader); here: the heat glow on the floor
            // around them and what they breathe out — billowing mineral smoke (black smokers) or milky haze (towers)
            bool tower = style == Style.CarbonateTower;
            Color glowC = tower ? new Color(0.9f, 0.95f, 0.92f) : new Color(1f, 0.55f, 0.22f);
            foreach (var v in list)
            {
                float breathe = (0.85f + 0.15f * Mathf.Sin(t * 0.9f + v.seed)) * (1f + surge * 1.2f);
                under.Add(v.pos, v.plumeR * (1f + surge * 0.15f), 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, Alpha(glowC, (tower ? 0.03f : 0.045f) * v.strength * breathe));
                int puffs = tower ? 10 : Mathf.RoundToInt(16 * (1f + surge));
                for (int k = 0; k < puffs; k++)
                {
                    float life = Frac(k * 0.173f + t * (0.035f + 0.01f * (k % 3)) + v.seed * 0.01f);
                    float a = v.seed * 2f + k * 2.399f + life * 0.6f;
                    var p = v.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * v.coreR * (0.2f + life * 1.8f);
                    float size = v.coreR * (0.25f + life * 0.9f);
                    var col = tower ? new Color(0.95f, 0.97f, 0.96f, 0.07f * (1f - life)) : new Color(0.08f, 0.07f, 0.06f, 0.22f * (1f - life) * (1f + surge));
                    over.Add(p, size, a + t * 0.1f, 1f, 13, new Vector4(1f, 0f, Frac(k * 0.37f + v.seed), 0), Vector4.zero, col);   // ragged, billowing
                }
                // a few fine gas droplets rising off the mouths
                for (int k = 0; k < (tower ? 8 : 14); k++)
                {
                    float life = Frac(k * 0.137f + t * 0.15f + v.seed * 0.03f);
                    float a = v.seed + k * 2.399f;
                    var p = v.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * v.coreR * (0.1f + life * 0.9f);
                    over.Add(p, 0.12f + life * 0.12f, 0f, 1f, 10, new Vector4(1f, 0.1f, Frac(k * 0.31f), 0), Vector4.zero, new Color(0.9f, 0.95f, 1f, 0.35f * (1f - life)));
                }
            }
        }

        static Color Alpha(Color c, float a) { c.a = a; return c; }
        static float Frac(float x) => x - Mathf.Floor(x);
    }
}
