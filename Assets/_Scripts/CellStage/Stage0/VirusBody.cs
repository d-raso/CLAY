using System.Collections.Generic;
using UnityEngine;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// A VIRUS as a MESH of nodes. Each node is a compound (bases, lipid, clay, organic, iron–sulfur, amino acid) and
    /// has a place on the body: an angle and a reach from the core. The body is the faceted surface stretched between
    /// the nodes and a raised core — so its silhouette, its spikes and its colours all come from what it's built of and
    /// how the nodes are arranged. The player reshapes their own mesh in the virus editor.
    ///
    /// COHESION (how sound): balanced complementary bases, variety, a little mineral and lipid; and SYMMETRY — an even,
    /// balanced shape holds together better. SPIKINESS (how far nodes reach out, unevenly) helps force a way into
    /// cells, at a cost to cohesion. STRENGTH = cohesion × size.
    ///
    /// Physics: nodes spring toward their places, lag when the body accelerates, squash on hits, and snap off when
    /// pulled too far (weak meshes shed easily).
    /// </summary>
    public sealed class VirusBody
    {
        public const int MaxParts = 22;
        public readonly List<int> parts = new();          // compound kind per node (MoteKind 0..6; 7 iron–sulfur; 8 amino acid)
        public readonly List<Vector2> polar = new();      // each node's place: (angle, reach ×Radius)
        public readonly List<Vector2> pos = new(), vel = new();
        float cohesion; public float externalCohesion = -1f;   // ≥ 0: set by how well the virus eats (the pattern game)
        public float Cohesion { get => externalCohesion >= 0f ? externalCohesion : cohesion; private set => cohesion = value; }
        public float Symmetry { get; private set; } = 1f;
        public float Spikiness { get; private set; }
        /// Appendages: nodes reaching past 1.5 radii are FIBRES (they grab cells — easier to get in); the longest, if it
        /// reaches 1.8+, is a TAIL (it injects — faster takeovers). Both cost a little cohesion (they stick out).
        public int Fibers { get; private set; }
        public bool HasTail { get; private set; }
        public float Strength => Cohesion * Mathf.Clamp01(0.25f + parts.Count / 14f);
        public float Radius => 0.22f + 0.02f * parts.Count;   // world units
        public float hitFlash;

        /// Add a node at `angle` (or between the widest gap if NaN), reaching `reach`.
        public void Add(int kind, float angle = float.NaN, float reach = 1f)
        {
            if (parts.Count >= MaxParts) return;
            if (float.IsNaN(angle)) angle = WidestGap();
            int at = 0; while (at < polar.Count && polar[at].x < angle) at++;
            parts.Insert(at, kind); polar.Insert(at, new Vector2(Mathf.Repeat(angle, Mathf.PI * 2f), reach));
            var p0 = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius * reach * 0.5f;
            pos.Insert(at, p0); vel.Insert(at, Vector2.zero);
            Recompute();
        }
        // (kept for older callers: a node placed toward a world offset)
        public void Add(int kind, Vector2 worldOffset) => Add(kind, worldOffset.sqrMagnitude > 1e-6f ? Mathf.Atan2(worldOffset.y, worldOffset.x) : float.NaN);

        public void RemoveAt(int i)
        {
            parts.RemoveAt(i); polar.RemoveAt(i); pos.RemoveAt(i); vel.RemoveAt(i);
            Recompute();
        }

        /// Reshape: move node `i` to a new angle and reach (keeps the ring ordered by angle).
        public void Reshape(int i, float angle, float reach)
        {
            int k = parts[i]; var p = pos[i]; var v = vel[i];
            parts.RemoveAt(i); polar.RemoveAt(i); pos.RemoveAt(i); vel.RemoveAt(i);
            angle = Mathf.Repeat(angle, Mathf.PI * 2f);
            int at = 0; while (at < polar.Count && polar[at].x < angle) at++;
            parts.Insert(at, k); polar.Insert(at, new Vector2(angle, Mathf.Clamp(reach, 0.45f, 2.0f))); pos.Insert(at, p); vel.Insert(at, v);
            Recompute();
        }

        float WidestGap()
        {
            if (polar.Count == 0) return 0f;
            float best = 0f, at = 0f;
            for (int i = 0; i < polar.Count; i++)
            {
                float a = polar[i].x, b = i + 1 < polar.Count ? polar[i + 1].x : polar[0].x + Mathf.PI * 2f;
                if (b - a > best) { best = b - a; at = a + (b - a) * 0.5f; }
            }
            return Mathf.Repeat(at, Mathf.PI * 2f);
        }

        public void Recompute()
        {
            var n = new int[9]; foreach (var k in parts) n[Mathf.Clamp(k, 0, 8)]++;
            int bases = n[0] + n[1] + n[2] + n[3];
            if (parts.Count == 0) { Cohesion = 0f; return; }
            float pairAB = (n[0] + n[1]) > 0 ? 1f - Mathf.Abs(n[0] - n[1]) / (float)(n[0] + n[1]) : 0f;
            float pairCD = (n[2] + n[3]) > 0 ? 1f - Mathf.Abs(n[2] - n[3]) / (float)(n[2] + n[3]) : 0f;
            float balance = bases > 0 ? ((n[0] + n[1]) * pairAB + (n[2] + n[3]) * pairCD) / bases : 0f;
            int kinds = 0; for (int k = 0; k < 9; k++) if (n[k] > 0) kinds++;
            float variety = Mathf.Clamp01(kinds / 5f);
            float mineral = (n[5] + n[7]) / (float)parts.Count, lipid = n[4] / (float)parts.Count, filler = (n[6] + n[8]) / (float)parts.Count;
            float stiff = 1f - Mathf.Abs(mineral - 0.2f) * 2.5f, seal = 1f - Mathf.Abs(lipid - 0.15f) * 3f;
            // shape: how even the angles and reaches are (symmetry), and how far and unevenly nodes reach (spikiness)
            float meanR = 0f; foreach (var p in polar) meanR += p.y; meanR /= polar.Count;
            float varR = 0f, gapVar = 0f; float ideal = Mathf.PI * 2f / polar.Count;
            for (int i = 0; i < polar.Count; i++)
            {
                varR += (polar[i].y - meanR) * (polar[i].y - meanR);
                float b = i + 1 < polar.Count ? polar[i + 1].x : polar[0].x + Mathf.PI * 2f;
                gapVar += Mathf.Abs((b - polar[i].x) - ideal) / ideal;
            }
            varR /= polar.Count; gapVar /= polar.Count;
            Symmetry = Mathf.Clamp01(1f - gapVar * 0.8f - Mathf.Sqrt(varR) * 0.6f);
            Spikiness = Mathf.Clamp01((meanR - 1f) * 1.2f + Mathf.Sqrt(varR) * 1.2f);
            Fibers = 0; HasTail = false;
            foreach (var pp in polar) { if (pp.y >= 1.8f) HasTail = true; else if (pp.y > 1.5f) Fibers++; }
            float c = balance * 0.4f + variety * 0.15f + Mathf.Clamp01(stiff) * 0.12f + Mathf.Clamp01(seal) * 0.08f + (1f - Mathf.Clamp01(filler * 2f)) * 0.05f + Symmetry * 0.2f;
            c *= 1f - Spikiness * 0.3f;
            Cohesion = Mathf.Clamp01(bases >= 2 ? c : c * 0.5f);
        }

        /// Where node `i` wants to sit (body space, world units).
        public Vector2 Rest(int i)
        {
            var p = polar[i];
            return new Vector2(Mathf.Cos(p.x), Mathf.Sin(p.x)) * Radius * p.y;
        }

        /// The player's body at a TIER of the pattern game: the better you've eaten, the more complex the form.
        /// Nodes are coloured by the compounds of your pattern, in order — you become what you eat.
        public static VirusBody FromTier(int tier, List<int> pattern, float seed)
        {
            var b = new VirusBody();
            var kinds = pattern != null && pattern.Count > 0 ? pattern : new List<int> { 0, 1, 2, 3 };
            var r = new System.Random((int)(seed * 9973f) + tier * 31);
            int head = tier switch { 0 => 3, 1 => 5, 2 => 6, 3 => 8, 4 => 8, 5 => 10, _ => 12 };
            int fibres = tier switch { 2 => 2, 3 => 4, 5 => 4, 6 => 6, _ => 0 };
            bool tail = tier >= 4;
            int k = 0;
            for (int i = 0; i < head; i++)
            {
                float a = i * Mathf.PI * 2f / head + (tier == 0 ? (float)(r.NextDouble() - 0.5) * 0.9f : 0f);
                float reach = tier == 0 ? 0.75f + (float)r.NextDouble() * 0.5f : 1f;
                b.parts.Add(kinds[k++ % kinds.Count]); b.polar.Add(new Vector2(Mathf.Repeat(a, Mathf.PI * 2f), reach));
            }
            for (int i = 0; i < fibres; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / fibres + 0.3f;
                b.parts.Add(kinds[k++ % kinds.Count]); b.polar.Add(new Vector2(Mathf.Repeat(a, Mathf.PI * 2f), 1.6f));
            }
            if (tail) { b.parts.Add(kinds[k++ % kinds.Count]); b.polar.Add(new Vector2(Mathf.PI * 1.5f, 2f)); }
            // keep the ring ordered by angle
            var idx = new List<int>(); for (int i = 0; i < b.parts.Count; i++) idx.Add(i);
            idx.Sort((x, y) => b.polar[x].x.CompareTo(b.polar[y].x));
            var p2 = new List<int>(); var pol2 = new List<Vector2>();
            foreach (var i in idx) { p2.Add(b.parts[i]); pol2.Add(b.polar[i]); }
            b.parts.Clear(); b.parts.AddRange(p2); b.polar.Clear(); b.polar.AddRange(pol2);
            for (int i = 0; i < b.parts.Count; i++) { b.pos.Add(Vector2.zero); b.vel.Add(Vector2.zero); }
            b.Recompute();
            for (int i = 0; i < b.parts.Count; i++) b.pos[i] = b.Rest(i);
            return b;
        }

        /// A wild virus's body, generated from its lineage seed: symmetric, with its own number of nodes and spikes.
        public static VirusBody Wild(float seed)
        {
            var b = new VirusBody();
            var r = new System.Random((int)(seed * 100000f) + 17);
            int n = 5 + r.Next(9), sym = 2 + r.Next(3);
            float spike = (float)r.NextDouble();
            var kinds = new int[sym]; for (int k = 0; k < sym; k++) kinds[k] = r.Next(0, 9);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float reach = 1f + (i % sym == 0 ? spike * 0.8f : 0f);
                if (i == 0 && r.NextDouble() < 0.35) reach = 2f;            // some lineages grow a tail
                b.parts.Add(kinds[i % sym]); b.polar.Add(new Vector2(a, reach));
                b.pos.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * reach * 0.3f); b.vel.Add(Vector2.zero);
            }
            b.Recompute();
            for (int i = 0; i < n; i++) b.pos[i] = b.Rest(i);
            return b;
        }

        /// Physics. `bodyAccel` = how the whole body accelerated (nodes lag behind it). Nodes that break off → `shed`.
        public void Tick(float dt, Vector2 bodyAccel, List<int> shed)
        {
            hitFlash = Mathf.Max(0f, hitFlash - dt * 2f);
            float k = 30f + 70f * Cohesion, damp = 6f;
            for (int i = 0; i < parts.Count; i++)
            {
                var v = vel[i] + ((Rest(i) - pos[i]) * k - bodyAccel * 0.6f) * dt;
                v *= Mathf.Exp(-dt * damp);
                vel[i] = v; pos[i] += v * dt;
            }
            float tol = Radius * (0.45f + 0.9f * Cohesion);
            for (int i = parts.Count - 1; i >= 0; i--)
                if (externalCohesion < 0f && (pos[i] - Rest(i)).magnitude > tol && parts.Count > 3)
                {
                    shed.Add(parts[i]); parts.RemoveAt(i); polar.RemoveAt(i); pos.RemoveAt(i); vel.RemoveAt(i); hitFlash = 1f;
                }
            if (shed.Count > 0) Recompute();
        }

        /// Something hit the mesh from `dir` (unit, world) with `force`: nodes on that side are knocked in.
        public void Impact(Vector2 dir, float force)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                float facing = Vector2.Dot(pos[i].normalized, dir);
                if (facing > 0.2f) vel[i] -= dir * force * facing * (1.4f - Cohesion);
            }
            hitFlash = Mathf.Max(hitFlash, 0.6f);
        }
    }
}
