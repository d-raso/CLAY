using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// The prebiotic soup's molecules. Never named in game — players learn them by shape and behaviour:
    ///  • four BASES (0↔1, 2↔3 pair up — their shapes fit like a notch and a tab),
    ///  • LIPIDS (fatty acids: tadpoles that join a membrane and grow it),
    ///  • CLAY grains (mineral flakes that speed up chain linking — real: montmorillonite),
    ///  • OTHER organics (sugars/amino acids: no use yet, but they still crowd the bubble — osmotic load).
    /// </summary>
    public enum MoteKind { BaseA = 0, BaseB = 1, BaseC = 2, BaseD = 3, Lipid = 4, Clay = 5, Organic = 6 }

    public struct Mote
    {
        public Vector2 pos;
        public MoteKind kind;
        public float rot, spin, age;
        public Vector2 drift;          // smoothed Brownian wander (a correlated random walk, not per-frame jitter)
        public bool alive, stranded;   // stranded = left on dry ground by the ebbing tide
    }

    public static class MoteChem
    {
        public const int KindCount = 7;
        public static bool IsBase(MoteKind k) => k <= MoteKind.BaseD;
        public static int Complement(int b) => b ^ 1;   // A↔B, C↔D
        public static float OsmoticWeight(MoteKind k) => k == MoteKind.Lipid ? 0f : k == MoteKind.Clay ? 0.6f : 1f;

        /// Colours per solvent (alien soups look alien). Index = MoteKind.
        public static Color Color(MoteKind k, CLAY.Galaxy.LiquidType solvent)
        {
            // partners share a family colour: round pair = warm (amber / coral), triangle pair = cool (teal / blue)
            Color[] water = { new(1f, 0.68f, 0.22f), new(0.95f, 0.36f, 0.28f), new(0.25f, 0.88f, 0.78f), new(0.3f, 0.52f, 1f), new(0.95f, 0.92f, 0.8f), new(0.62f, 0.55f, 0.48f), new(0.75f, 0.6f, 0.9f) };
            Color[] cold = { new(1f, 0.78f, 0.45f), new(0.95f, 0.5f, 0.6f), new(0.55f, 0.95f, 0.85f), new(0.6f, 0.65f, 1f), new(0.85f, 0.95f, 1f), new(0.5f, 0.55f, 0.6f), new(0.95f, 0.85f, 0.6f) };
            var pal = solvent == CLAY.Galaxy.LiquidType.Methane || solvent == CLAY.Galaxy.LiquidType.Ammonia ? cold : water;
            return pal[(int)k];
        }
    }

    /// <summary>
    /// Free molecules in the pool: spawned in kind-specific patches (so WHERE you swim decides WHAT you take in),
    /// drifting by Brownian motion, draining toward hollows as the water shrinks (concentrating the soup in the
    /// puddles) and stranded on dry ground until the tide returns.
    /// </summary>
    public sealed class MoteField
    {
        public readonly List<Mote> motes = new();
        readonly TidePool pool;
        readonly CellStageContext ctx;
        DetRng r;
        readonly Vector2[] patchOffset = new Vector2[MoteChem.KindCount];
        readonly float[] abundance = new float[MoteChem.KindCount];
        public int targetCount;

        public MoteField(TidePool p, CellStageContext c)
        {
            pool = p; ctx = c; r = new DetRng(DetRng.Hash(c.poolSeed, 0x30E7UL));
            for (int k = 0; k < MoteChem.KindCount; k++) patchOffset[k] = new Vector2(r.Range(0f, 500f), r.Range(0f, 500f));
            // the soup's recipe comes from the planet: reducing, organic-rich worlds make more bases; minerals → clay
            float rich = c.organicRichness;
            for (int k = 0; k < 4; k++) abundance[k] = 0.8f + rich * 0.6f + r.Range(-0.25f, 0.25f);
            abundance[(int)MoteKind.Lipid] = 1.1f + rich * 0.5f;
            abundance[(int)MoteKind.Clay] = 0.25f + c.minerals * 0.5f;
            abundance[(int)MoteKind.Organic] = 0.6f + rich * 0.5f;
            // the biome's monomer profile and density (e.g. springs rich in fatty acids, clay shelves densest)
            for (int k = 0; k < MoteChem.KindCount; k++) abundance[k] *= p.biome.kindMul[k];
            targetCount = Mathf.RoundToInt(Mathf.Lerp(1500f, 3000f, rich) * p.biome.densityMul);
            for (int i = 0; i < targetCount; i++) Spawn();
        }

        MoteKind PickKind(Vector2 p)
        {
            float tot = 0f; var w = new float[MoteChem.KindCount];
            for (int k = 0; k < MoteChem.KindCount; k++)
            {
                // each kind clumps in its own drifting patches
                float patch = TidePool.Fbm(p.x * 0.05f + patchOffset[k].x + pool.time * 0.004f, p.y * 0.05f + patchOffset[k].y, 3);
                w[k] = abundance[k] * Mathf.Pow(Mathf.Clamp01(patch * 1.8f - 0.45f), 2f) + 0.02f;
                tot += w[k];
            }
            float pick = r.Value * tot;
            for (int k = 0; k < MoteChem.KindCount; k++) { pick -= w[k]; if (pick <= 0f) return (MoteKind)k; }
            return MoteKind.Organic;
        }

        public void Spawn()
        {
            var p = pool.RandomWetPoint(ref r, 0.01f);
            motes.Add(new Mote { pos = p, kind = PickKind(p), rot = r.Range(0f, 6.283f), spin = r.Range(-1f, 1f), alive = true });
        }

        public void Tick(float dt, System.Func<Vector2, Vector2> flowAt = null)
        {
            float rate = ctx.ReactionRate;
            float brown = 0.35f * Mathf.Sqrt(Mathf.Max(rate, 0.1f));
            float relax = 1f - Mathf.Exp(-dt * 1.5f);
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                var m = motes[i];
                if (!m.alive) { motes[i] = motes[motes.Count - 1]; motes.RemoveAt(motes.Count - 1); continue; }
                m.age += dt;
                float depth = pool.Depth(m.pos);
                m.stranded = depth <= 0.003f;
                // on the shore molecules don't last: stranded ones dry and crumble, those in the thin ebbing film
                // break down fast (so a stranded bubble isn't buried by everything draining into its hollow)
                if (m.stranded ? r.Value < dt * 0.35f : depth < 0.03f && r.Value < dt * 0.18f) { m.alive = false; motes[i] = m; continue; }
                if (!m.stranded)
                {
                    m.drift = Vector2.Lerp(m.drift, new Vector2(r.Range(-1f, 1f), r.Range(-1f, 1f)) * brown * 2f, relax);
                    m.pos += m.drift * dt;
                    if (flowAt != null) m.pos += flowAt(m.pos) * (0.6f * dt);                      // small things ride the currents fully
                    if (depth < 0.05f) m.pos += pool.Downhill(m.pos) * (0.05f - depth) * 60f * dt;   // drains with the ebbing film
                    m.rot += m.spin * dt;
                }
                m.pos.x = Mathf.Clamp(m.pos.x, 0.5f, TidePool.W - 0.5f); m.pos.y = Mathf.Clamp(m.pos.y, 0.5f, TidePool.H - 0.5f);
                motes[i] = m;
            }
            // the soup is replenished (lightning, vents, meteorites, run-off) — slowly
            int deficit = targetCount - motes.Count;
            for (int k = 0; k < Mathf.Min(deficit, 8); k++) if (r.Value < dt * 20f * rate) Spawn();
        }
    }
}
