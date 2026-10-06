using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// Builds every visible virus body as a real faceted MESH each frame: a raised core joined to each node, so each
    /// facet is a flat triangle with its own 3D normal (lit by VirusMesh.shader). The node positions come from the
    /// body's physics (or its rest shape), so the silhouette, the spikes and the colours are the virus itself.
    /// </summary>
    public sealed class VirusMeshes
    {
        readonly Mesh mesh;
        readonly Material mat;
        readonly List<Vector3> v = new(), n = new();
        readonly List<Color> c = new();
        readonly List<Vector2> uv = new();
        readonly List<int> tri = new();
        public Material Material => mat;

        public VirusMeshes(Transform parent, int layer, int sortingOrder)
        {
            mesh = new Mesh { name = "VirusMeshes", indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic();
            mat = new Material(Shader.Find("CLAY/CellStage/VirusMesh"));
            var go = new GameObject("VirusMeshes") { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.sortingOrder = sortingOrder;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        public void Clear() { v.Clear(); n.Clear(); c.Clear(); uv.Clear(); tri.Clear(); lp.Clear(); }

        readonly List<Vector2> lp = new();   // body-space position (radius units) — the subunit layer sticks to the body

        /// One body: its HEAD (the nodes within ~1.5 radii, joined to a raised core) and its APPENDAGES (nodes pulled
        /// further out: fibres — and the longest, if long enough, a thicker tail), each a stalk ending in a knob.
        public void Add(VirusBody b, bool usePhysics, Vector2 at, float rot, float scale, System.Func<int, Color> colorOf, Color tint, float tintAmt, float alpha)
        {
            int m = b.parts.Count; if (m < 3) return;
            float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot), R = b.Radius;
            Vector2 W(Vector2 o) { var r = new Vector2(o.x * cs - o.y * sn, o.x * sn + o.y * cs) * scale; return at + r; }
            var cols = new Color[m]; Color avg = Color.black;
            for (int i = 0; i < m; i++) { cols[i] = Color.Lerp(colorOf(b.parts[i]), tint, tintAmt); avg += cols[i]; }
            avg /= m;
            Vector2 O(int i) => usePhysics ? b.pos[i] : b.Rest(i);
            var hull = new List<int>(); var app = new List<int>();
            for (int i = 0; i < m; i++) (b.polar[i].y <= 1.5f ? hull : app).Add(i);
            if (hull.Count < 3) { hull.Clear(); app.Clear(); for (int i = 0; i < m; i++) hull.Add(i); }
            int tail = -1; float longest = 1.8f;
            foreach (var k in app) if (b.polar[k].y > longest) { longest = b.polar[k].y; tail = k; }
            // the head: a raised core fanned to the hull nodes (with a bulge at each edge's middle)
            float coreH = R * 0.9f, rimH = R * 0.12f;
            for (int h = 0; h < hull.Count; h++)
            {
                int i = hull[h], j = hull[(h + 1) % hull.Count];
                Vector2 oi = O(i), oj = O(j), mid = (oi + oj) * 0.5f * 1.04f;
                var ci = Color.Lerp(cols[i], cols[i] * 0.8f, 0.5f); var cj = Color.Lerp(cols[j], cols[j] * 0.8f, 0.5f);   // node clusters a shade deeper
                var cm = Color.Lerp(cols[i], cols[j], 0.5f);
                Tri(at, W(oi), W(mid), Vector2.zero, oi, mid, coreH, rimH, rimH * 1.6f, avg, ci, cm, alpha, R, scale);
                Tri(at, W(mid), W(oj), Vector2.zero, mid, oj, coreH, rimH * 1.6f, rimH, avg, cm, cj, alpha, R, scale);
            }
            // appendages
            foreach (var k in app)
            {
                Vector2 tip = O(k), dir = tip.sqrMagnitude > 1e-6f ? tip.normalized : Vector2.right;
                Vector2 basePt = dir * R * 0.9f, perp = new Vector2(-dir.y, dir.x);
                float wdt = R * (k == tail ? 0.26f : 0.13f);
                var col = cols[k];
                const int segs = 4;
                for (int sgi = 0; sgi < segs; sgi++)
                {
                    float u0 = sgi / (float)segs, u1 = (sgi + 1) / (float)segs;
                    Vector2 a0 = Vector2.Lerp(basePt, tip, u0), a1 = Vector2.Lerp(basePt, tip, u1);
                    float w0 = wdt * (1f - 0.25f * u0), w1 = wdt * (1f - 0.25f * u1);
                    // a stalk: two halves tilted like a cylinder's sides; the centre line raised
                    Tri(at, W(a0 + perp * w0), W(a1 + perp * w1), W(a0), a0 + perp * w0, a1 + perp * w1, a0, rimH * 0.5f, rimH * 0.5f, rimH * 1.5f, col, col, col, alpha, R, scale, 0f, 0f, 0.6f);
                    Tri(at, W(a1 + perp * w1), W(a1), W(a0), a1 + perp * w1, a1, a0, rimH * 0.5f, rimH * 1.5f, rimH * 1.5f, col, col, col, alpha, R, scale, 0f, 0.6f, 0.6f);
                    Tri(at, W(a0 - perp * w0), W(a0), W(a1 - perp * w1), a0 - perp * w0, a0, a1 - perp * w1, rimH * 0.5f, rimH * 1.5f, rimH * 0.5f, col, col, col, alpha, R, scale, 0f, 0.6f, 0f);
                    Tri(at, W(a1 - perp * w1), W(a0), W(a1), a1 - perp * w1, a0, a1, rimH * 0.5f, rimH * 1.5f, rimH * 1.5f, col, col, col, alpha, R, scale, 0f, 0.6f, 0.6f);
                }
                // the knob (a tail ends in a broader baseplate)
                float kr = R * (k == tail ? 0.34f : 0.2f);
                for (int q = 0; q < 8; q++)
                {
                    float a0 = q * Mathf.PI / 4f, a1 = (q + 1) * Mathf.PI / 4f;
                    Vector2 p0 = tip + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * kr, p1 = tip + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * kr;
                    Tri(at, W(tip), W(p0), W(p1), tip, p0, p1, kr * 1.2f, rimH * 0.4f, rimH * 0.4f, col * 0.9f, col, col, alpha, R, scale, 1f, 0f, 0f);
                }
            }
        }

        /// A triangle: world positions (wa, wb, wd), body-space (oa, ob, od) and heights for its normal; uvx = 1 inside,
        /// 0 at an outer edge (the outline gets bumpy there).
        void Tri(Vector2 at, Vector2 wa, Vector2 wb, Vector2 oa_unused, Vector2 oa, Vector2 ob, float ha, float hb, float hd,
                 Color ca, Color cb, Color cd, float alpha, float R, float scale)
            => Tri(at, at, wa, wb, Vector2.zero, oa, ob, ha, hb, hd, ca, cb, cd, alpha, R, scale, 1f, 0f, 0f);
        void Tri(Vector2 at, Vector2 wa, Vector2 wb, Vector2 wd, Vector2 oa, Vector2 ob, Vector2 od, float ha, float hb, float hd,
                 Color ca, Color cb, Color cd, float alpha, float R, float scale, float ua = 1f, float ub = 0f, float ud = 0f)
        {
            var pa = new Vector3(oa.x, oa.y, ha); var pb = new Vector3(ob.x, ob.y, hb); var pd = new Vector3(od.x, od.y, hd);
            var nn = Vector3.Cross(pb - pa, pd - pa).normalized; if (nn.z < 0f) nn = -nn;
            int s0 = v.Count;
            v.Add(new Vector3(wa.x, wa.y, 0f)); v.Add(new Vector3(wb.x, wb.y, 0f)); v.Add(new Vector3(wd.x, wd.y, 0f));
            n.Add(nn); n.Add(nn); n.Add(nn);
            ca.a = alpha; cb.a = alpha; cd.a = alpha; c.Add(ca); c.Add(cb); c.Add(cd);
            uv.Add(new Vector2(ua, 0f)); uv.Add(new Vector2(ub, 0f)); uv.Add(new Vector2(ud, 0f));
            lp.Add(oa / R); lp.Add(ob / R); lp.Add(od / R);
            tri.Add(s0); tri.Add(s0 + 1); tri.Add(s0 + 2);
        }

        public void Upload(float day, Color water)
        {
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetUVs(1, lp);
            mesh.SetTriangles(tri, 0, false);
            mesh.bounds = new Bounds(new Vector3(TidePool.W * 0.5f, TidePool.H * 0.5f, 0f), new Vector3(TidePool.W + 40f, TidePool.H + 40f, 10f));
            mat.SetFloat("_Day", day); mat.SetColor("_WaterTint", water);
        }

        public void Dispose() { Object.Destroy(mat); Object.Destroy(mesh); }
    }
}
