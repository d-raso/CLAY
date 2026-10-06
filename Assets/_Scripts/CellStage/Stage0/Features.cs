using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// The pool's STRUCTURES: what makes one biome's floor look nothing like another's. Scattered per biome from the
    /// pool seed — boulders, basalt column clusters, chemical gardens (hollow mineral tubes that grow where vent fluid
    /// meets seawater, a candidate cradle of life), carbonate lace spires, mud volcanoes, sinter rimstone terraces,
    /// pebble beds, sand ripple fields, sulfur crystal crusts, trapped gas bubbles — plus things floating overhead
    /// (pumice rafts, foam) that drift with the water and cast shadows. Solid ones are obstacles; water flows around
    /// them. Drawn by CellFeature.shader.
    /// </summary>
    public sealed class Features
    {
        public enum Kind { Boulder, Basalt, ChemGarden, LaceSpire, MudVolcano, Rimstone, Pebbles, Ripples, Pumice, Foam, Sulfur, Bubbles, Smoker, Spire }

        public sealed class Feature
        {
            public Kind kind; public Vector2 pos; public float size, rot, seed;
            public bool solid, overhead;
            public Color a, b;
            public Vector2 vel;
        }

        public readonly List<Feature> list = new();
        readonly Vents vents;

        public Features(TidePool pool, Vents vents, CellStageContext ctx)
        {
            this.vents = vents;
            var r = new DetRng(DetRng.Hash(ctx.poolSeed, 0xFEA7UL));
            var bm = pool.biome;
            Color rock = bm.rock, hi = bm.rockHigh, acc = bm.accent, crust = bm.crust;

            void Place(Kind k, int n, float sMin, float sMax, bool solid, Color a, Color b, float minDepth = 0.0f, bool overhead = false, Vector2? near = null, float nearR = 0f)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = Vector2.zero; bool ok = false;
                    for (int tries = 0; tries < 20 && !ok; tries++)
                    {
                        p = near.HasValue ? near.Value + r.InsideUnitCircle() * nearR
                                          : new Vector2(r.Range(6f, TidePool.W - 6f), r.Range(6f, TidePool.H - 6f));
                        if (overhead) { ok = true; break; }
                        float h = pool.Height(p);
                        ok = pool.Depth(p) >= minDepth && h < 0.95f;
                        foreach (var v in vents.list) if ((p - v.pos).magnitude < v.coreR * 1.6f + sMax) ok = false;
                    }
                    if (!ok) continue;
                    float jitter = r.Range(0.85f, 1.15f);
                    list.Add(new Feature
                    {
                        kind = k, pos = p, size = r.Range(sMin, sMax), rot = r.Range(0f, 6.283f), seed = r.Range(0f, 50f),
                        solid = solid, overhead = overhead, a = a * jitter, b = b * jitter,
                    });
                }
            }

            // every pool: some loose rock — darker and redder than the floor so it reads as rock sitting on it
            Color stone = Color.Lerp(rock * 0.7f, new Color(0.35f, 0.28f, 0.24f), 0.3f), stoneHi = Color.Lerp(hi, rock, 0.3f);
            Place(Kind.Boulder, Mathf.RoundToInt(6 * bm.rockiness), 1.8f, 5f, true, stone, stoneHi);
            Vector2? ventPos = vents.list.Count > 0 ? vents.list[0].pos : null;
            switch (bm.kind)
            {
                case TidePoolBiome.Kind.HydrothermalVentField:
                    Place(Kind.Basalt, 6, 4f, 9f, true, new Color(0.17f, 0.16f, 0.16f), new Color(0.32f, 0.3f, 0.28f));
                    Place(Kind.ChemGarden, 7, 2.5f, 4.5f, true, new Color(0.62f, 0.3f, 0.12f), new Color(0.15f, 0.12f, 0.1f), 0f, false, ventPos, 30f);
                    Place(Kind.Sulfur, 6, 1.8f, 3.2f, false, new Color(0.92f, 0.82f, 0.25f), new Color(1f, 0.96f, 0.6f), 0f, false, ventPos, 26f);
                    break;
                case TidePoolBiome.Kind.AlkalineHotSpring:
                    Place(Kind.Spire, 9, 2.5f, 5.5f, true, new Color(0.9f, 0.89f, 0.84f), new Color(0.7f, 0.74f, 0.68f));   // small white carbonate towers
                    Place(Kind.ChemGarden, 4, 2f, 3.5f, true, new Color(0.88f, 0.9f, 0.84f), new Color(0.5f, 0.66f, 0.56f));
                    break;
                case TidePoolBiome.Kind.ClayMineralShelf:
                    Place(Kind.MudVolcano, 6, 3f, 6f, true, new Color(0.5f, 0.38f, 0.26f), new Color(0.3f, 0.22f, 0.15f));
                    Place(Kind.Pebbles, 3, 4f, 7f, false, stone, stoneHi);
                    break;
                default:   // sunlit tidal flat
                    Place(Kind.Pebbles, 8, 3f, 7f, false, stone, Color.Lerp(stoneHi, acc, 0.5f));
                    Place(Kind.Boulder, 5, 2f, 6f, true, stone, stoneHi);
                    break;
            }
        }

        /// Solid structures are rock: push a body of `radius` out of them.
        public Vector2 PushOut(Vector2 p, float radius)
        {
            foreach (var f in list)
            {
                if (!f.solid) continue;
                float solid = f.size * 0.72f + radius;
                Vector2 d = p - f.pos; float m = d.sqrMagnitude;
                if (m >= solid * solid) continue;
                m = Mathf.Sqrt(m);
                p = f.pos + (m > 1e-4f ? d / m : Vector2.right) * solid;
            }
            return p;
        }

        /// Water flows AROUND solid structures: remove the part of the current heading into one (and speed it up along
        /// the side a little — the wake).
        public Vector2 Deflect(Vector2 p, Vector2 v)
        {
            foreach (var f in list)
            {
                if (!f.solid) continue;
                Vector2 d = p - f.pos;
                float reach = f.size * 1.3f;
                float m2 = d.sqrMagnitude;
                if (m2 > reach * reach) continue;
                float m = Mathf.Sqrt(m2) + 1e-3f;
                Vector2 nrm = d / m;
                float into = Vector2.Dot(v, nrm);
                float k = Mathf.Clamp01((reach - m) / (reach - f.size * 0.72f));
                if (into < 0f) v -= nrm * into * k;
                v *= 1f + 0.25f * k;
            }
            return v;
        }

        /// Overhead rafts drift with the surface currents (wrapping around the pool).
        public void Tick(float dt, System.Func<Vector2, Vector2> flowAt)
        {
            foreach (var f in list)
            {
                if (!f.overhead) continue;
                f.vel = Vector2.Lerp(f.vel, flowAt(f.pos) * 0.8f + new Vector2(0.15f, 0.05f), 1f - Mathf.Exp(-dt));
                f.pos += f.vel * dt;
                f.rot += dt * 0.01f;
                if (f.pos.x < -10f) f.pos.x += TidePool.W + 20f; if (f.pos.x > TidePool.W + 10f) f.pos.x -= TidePool.W + 20f;
                if (f.pos.y < -10f) f.pos.y += TidePool.H + 20f; if (f.pos.y > TidePool.H + 10f) f.pos.y -= TidePool.H + 20f;
            }
        }

        public void Render(QuadBatch floor, QuadBatch overhead, QuadBatch shadows, Rect view)
        {
            bool tower = vents.style == Vents.Style.CarbonateTower;
            foreach (var v in vents.list)
            {
                float size = v.coreR * 1.25f;
                shadows.Add(v.pos + new Vector2(0.4f, -0.5f) * v.coreR * 0.3f, size * 1.1f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0f, 0f, 0f, 0.45f));
                var a = tower ? new Color(0.86f, 0.84f, 0.78f) : new Color(0.16f, 0.13f, 0.11f);
                var b = tower ? new Color(0.62f, 0.66f, 0.6f) : new Color(0.55f, 0.36f, 0.16f);
                floor.Add(v.pos, size, v.seed, 1f, tower ? 13 : 12, new Vector4(v.seed * 0.37f, 0f, 0.02f, 1f), new Vector4(b.r, b.g, b.b, v.strength), a);
            }
            foreach (var f in list)
            {
                float ext = f.size * 1.1f;
                if (f.pos.x + ext < view.xMin || f.pos.x - ext > view.xMax || f.pos.y + ext < view.yMin || f.pos.y - ext > view.yMax) continue;
                var p1 = new Vector4(f.seed, f.overhead ? 1f : 0f, f.overhead ? 0.06f : 0.0f, f.overhead ? 0.75f : 1f);
                var p2 = new Vector4(f.b.r, f.b.g, f.b.b, 0f);
                if (f.overhead)
                {
                    shadows.Add(f.pos + new Vector2(1.6f, -2.2f), f.size * 1.2f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0f, 0f, 0f, f.kind == Kind.Foam ? 0.12f : 0.28f));
                    overhead.Add(f.pos, f.size, f.rot, 1f, (int)f.kind, p1, p2, f.a);
                }
                else
                {
                    if (f.solid) shadows.Add(f.pos + new Vector2(0.35f, -0.45f) * f.size * 0.3f, f.size * 0.95f, 0f, 1f, 7, new Vector4(1, 0, 0, 0), Vector4.zero, new Color(0f, 0f, 0f, 0.35f));
                    floor.Add(f.pos, f.size, f.rot, 1f, (int)f.kind, p1, p2, f.a);
                }
            }
        }
    }
}
