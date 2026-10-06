using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// Suspended matter: inedible specks drifting at many depths, chosen by the planet's chemistry. They give the water
    /// body and scale. Specks near the focal plane are crisp; ones floating ABOVE it pass over the view as big soft
    /// out-of-focus blobs (exactly what drifting dust looks like under a microscope); deep ones are faint and small.
    /// They live in a box around the camera and wrap, so the water is never empty wherever you go.
    /// </summary>
    public sealed class Suspended
    {
        public enum Matter { SilicaDebris, ClayFlocs, RustFlakes, SulfurGrains, TholinHaze, CarbonateFlocs }

        struct Speck { public Vector2 pos; public float depth, size, rot, spin, seed; }   // depth: 0 = floor … 1 = surface

        readonly List<Speck> specks = new();
        public readonly Matter matter;
        readonly Color colA, colB;
        readonly int shape;
        DetRng r;
        const float Box = 46f;          // half-size of the wrapping box around the camera

        public Suspended(CellStageContext ctx, TidePoolBiome biome = null)
        {
            r = new DetRng(DetRng.Hash(ctx.poolSeed, 0x5D5EUL));
            matter = biome != null ? biome.matter
                   : ctx.solvent == LiquidType.Methane ? Matter.TholinHaze
                   : ctx.ventActivity > 0.5f ? Matter.SulfurGrains
                   : ctx.redox < 0.3f && ctx.minerals > 0.45f ? Matter.RustFlakes
                   : ctx.minerals > 0.6f ? Matter.ClayFlocs
                   : Matter.SilicaDebris;
            switch (matter)
            {
                case Matter.CarbonateFlocs: colA = new Color(0.96f, 0.96f, 0.93f); colB = new Color(0.86f, 0.88f, 0.86f); shape = 7; break;
                case Matter.TholinHaze:   colA = new Color(0.95f, 0.6f, 0.25f); colB = new Color(0.8f, 0.45f, 0.2f); shape = 7; break;
                case Matter.SulfurGrains: colA = new Color(0.95f, 0.88f, 0.35f); colB = new Color(0.85f, 0.75f, 0.3f); shape = 6; break;
                case Matter.RustFlakes:   colA = new Color(0.72f, 0.36f, 0.18f); colB = new Color(0.55f, 0.28f, 0.16f); shape = 5; break;
                case Matter.ClayFlocs:    colA = new Color(0.8f, 0.74f, 0.64f); colB = new Color(0.66f, 0.6f, 0.52f); shape = 5; break;
                default:                  colA = new Color(0.85f, 0.88f, 0.86f); colB = new Color(0.7f, 0.72f, 0.7f); shape = 6; break;
            }
            int n = Mathf.RoundToInt((matter == Matter.TholinHaze ? 160 : 260) * (biome != null ? biome.suspendedMul : 1f));
            for (int i = 0; i < n; i++)
                specks.Add(new Speck { pos = r.InsideUnitCircle() * Box, depth = r.Value, size = r.Range(0.05f, 0.14f), rot = r.Range(0f, 6.28f), spin = r.Range(-0.6f, 0.6f), seed = r.Value });
        }

        public void Tick(float dt, Vector2 camPos, System.Func<Vector2, Vector2> flowAt)
        {
            for (int i = 0; i < specks.Count; i++)
            {
                var s = specks[i];
                Vector2 f = flowAt != null ? flowAt(s.pos) : Vector2.zero;
                // upper water moves with the current; the floor-hugging layer barely
                s.pos += f * (0.3f + 0.6f * s.depth) * dt + new Vector2(Mathf.Sin(Time.time * 0.3f + s.seed * 20f), Mathf.Cos(Time.time * 0.23f + s.seed * 17f)) * 0.05f * dt;
                s.depth = Mathf.Clamp01(s.depth - dt * 0.004f * (matter == Matter.TholinHaze ? 0.3f : 1f));   // slowly settling
                if (s.depth <= 0.001f) s.depth = 1f;                                                          // (recycled at the top)
                s.rot += s.spin * dt;
                // wrap around the camera
                Vector2 d = s.pos - camPos;
                if (d.x > Box) s.pos.x -= Box * 2f; else if (d.x < -Box) s.pos.x += Box * 2f;
                if (d.y > Box) s.pos.y -= Box * 2f; else if (d.y < -Box) s.pos.y += Box * 2f;
                specks[i] = s;
            }
        }

        /// `below` draws under the focus plane (beneath cells), `above` over everything (foreground dust).
        /// focusDepth: 0..1 depth of what's in focus (the player).
        public void Render(QuadBatch below, QuadBatch above, Rect view, float focusDepth, float day)
        {
            foreach (var s in specks)
            {
                if (!view.Contains(s.pos)) continue;
                float dz = s.depth - focusDepth;
                float blur = Mathf.Clamp01(Mathf.Abs(dz) * 2.2f);
                var c = Color.Lerp(colA, colB, s.seed);
                if (dz > 0.25f)
                {
                    // above the focal plane: large, soft, faint — drifting between you and the specimen
                    float k = (dz - 0.25f) / 0.75f;
                    c.a = 0.16f * (1f - k * 0.5f) * (matter == Matter.TholinHaze ? 1.3f : 1f);
                    above.Add(s.pos, s.size * (3f + k * 9f), s.rot, 1f, 7, new Vector4(1f, 0f, 1f, 0f), Vector4.zero, c);
                }
                else
                {
                    c.a = Mathf.Lerp(0.75f, 0.3f, blur) * (matter == Matter.TholinHaze ? 0.6f : 1f);
                    below.Add(s.pos, s.size * (1f + blur * 0.8f), s.rot, 1f, shape, new Vector4(0.85f, 0f, blur, 0f), Vector4.zero, c);
                }
            }
        }
    }
}
