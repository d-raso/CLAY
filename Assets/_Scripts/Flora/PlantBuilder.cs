using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.Flora
{
    // Builds a plant from a genome as three meshes: WOOD (stems/bark/spines), FOLIAGE (leaves/pads/cactus bodies),
    // and FLOWER (flowers/caps). Each growth habit has its own generator. Per-vertex ALPHA carries the paint-job
    // pattern (base→accent blend); the CLAY/Flora shader reads it.
    public static class PlantBuilder
    {
        public struct Built { public Mesh wood, foliage, flower; }

        /// Level-of-detail knob (1 = full, e.g. the lab; ~0.4 mid-distance; ~0.15 far). Scales limb budget,
        /// tube/organ tessellation and leaf counts. Set before Build(); main-thread only.
        public static float Detail = 1f;
        static int Res(int n, int min) => Mathf.Max(min, Mathf.RoundToInt(n * Mathf.Lerp(0.35f, 1f, Detail)));

        /// HLSL-style smoothstep(edge0, edge1, x) → 0..1 (NOT Unity's Mathf.SmoothStep, which interpolates a→b).
        static float SS(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        class MB
        {
            public List<Vector3> v = new(); public List<Vector3> n = new(); public List<Color> c = new(); public List<int> t = new();
            public List<Vector4> uv = new();   // xy = (around, along), z = local circumference (for world-consistent grain)
            public Color tint = Color.white;   // per-organ RGB tint written into vertex colour (per-leaf coloration)
            public int Count => v.Count;
            public void Vert(Vector3 p, Vector3 nrm, float accentA, Vector2 tex = default, float circ = 1f)
            {
                // Guard against NaN/Inf from any degenerate normalize upstream — bad verts make Unity reject the mesh.
                // Collapse a bad vertex onto the PREVIOUS one (→ zero-area, invisible) rather than onto the world
                // origin, which used to draw a long spike from the organ down to the tree base.
                if (!Finite(p)) p = v.Count > 0 ? v[v.Count - 1] : Vector3.zero;
                if (!Finite(nrm) || nrm.sqrMagnitude < 1e-10f) nrm = Vector3.up; else nrm = nrm.normalized;
                v.Add(p); n.Add(nrm); c.Add(new Color(tint.r, tint.g, tint.b, accentA)); uv.Add(new Vector4(tex.x, tex.y, circ, 0f));
            }
            static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                                             || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
            public void Tri(int a, int b, int d) { t.Add(a); t.Add(b); t.Add(d); }
            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                if (v.Count == 0)
                {
                    m.SetVertices(new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.zero });
                    m.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up });
                    m.SetColors(new List<Color> { Color.white, Color.white, Color.white });
                    m.SetUVs(0, new List<Vector4> { Vector4.zero, Vector4.zero, Vector4.zero });
                    m.SetTriangles(new[] { 0, 0, 0 }, 0);
                    return m;
                }
                m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
                return m;
            }
        }

        // Per-vertex ambient occlusion baked into uv.w (1 = open sky, lower = sheltered): each vertex looks along its
        // normal and sums nearby geometry in front of it (a sparse sample of the plant's own vertices), weighted by
        // distance. Canopy interiors, leaf undersides, trunk crotches and the base darken — and unlike screen-space AO it
        // holds at any distance and costs nothing at runtime.
        static void BakeAO(params MB[] parts)
        {
            var pts = new List<Vector3>();
            foreach (var mb in parts) pts.AddRange(mb.v);
            if (pts.Count == 0) return;
            Vector3 lo = pts[0], hi = pts[0];
            foreach (var q in pts) { lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q); }
            float size = Mathf.Max((hi - lo).magnitude, 0.05f);
            float R = size * 0.22f, R2 = R * R;
            // a deterministic sparse sample of occluders
            int want = Mathf.Min(420, pts.Count), step = Mathf.Max(1, pts.Count / want);
            var occ = new List<Vector3>(want + 1);
            for (int i = 0; i < pts.Count; i += step) occ.Add(pts[i]);
            float norm = 1f / Mathf.Max(occ.Count * 0.045f, 1f);
            foreach (var mb in parts)
                for (int i = 0; i < mb.v.Count; i++)
                {
                    Vector3 p = mb.v[i], n = mb.n[i];
                    float o = 0f;
                    for (int k = 0; k < occ.Count; k++)
                    {
                        Vector3 d = occ[k] - p;
                        float d2 = d.sqrMagnitude;
                        if (d2 < 1e-6f || d2 > R2) continue;
                        float dn = Vector3.Dot(d, n);
                        if (dn <= 0f) continue;
                        float dist = Mathf.Sqrt(d2);
                        o += (1f - dist / R) * (dn / dist);                 // closer and more head-on = more occluding
                    }
                    float ground = Mathf.Clamp01(1f - (p.y - lo.y) / Mathf.Max(size * 0.15f, 0.02f)) * 0.25f;   // near the soil
                    float ao = Mathf.Clamp01(1f - Mathf.Clamp01(o * norm) * 0.75f - ground);
                    var u = mb.uv[i]; u.w = Mathf.Max(ao, 0.05f); mb.uv[i] = u;
                }
        }

        public static Built Build(PlantGenome g)
        {
            var wood = new MB(); var foliage = new MB(); var flower = new MB();
            var r = new DetRng((ulong)(uint)g.seed ^ 0x8EEDBEEFUL);

            switch (g.archetype)
            {
                case PlantArchetype.Tree:            BuildBranching(g, ref r, wood, foliage, flower, tree: true); break;
                case PlantArchetype.Shrub:           BuildBranching(g, ref r, wood, foliage, flower, tree: false); break;
                case PlantArchetype.Vine:            BuildVine(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.FloweringHerb:   BuildHerb(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Fern:            BuildRosetteFronds(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.GrassClump:      BuildGrass(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Cactus:          BuildCactus(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.SucculentRosette:BuildSucculent(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Globe:           BuildGlobe(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.MatAlgae:        BuildMat(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Mushroom:        BuildMushroom(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Tendril:         BuildTendril(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.OrbCluster:      BuildOrbCluster(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Tube:            BuildTube(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Ribbon:          BuildRibbon(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Umbrella:        BuildUmbrella(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Crystal:         BuildCrystal(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Spire:           BuildSpire(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.Segmented:       BuildSegmented(g, ref r, wood, foliage, flower); break;
                case PlantArchetype.LivingStone:     BuildLivingStone(g, ref r, wood, foliage, flower); break;
            }

            // Nothing may pass through the ground: anything that droops, dangles or bulges below y=0 comes to rest ON it.
            GroundClamp(wood); GroundClamp(foliage); GroundClamp(flower);
            BakeAO(wood, foliage, flower);
            return new Built { wood = wood.Mesh("Wood"), foliage = foliage.Mesh("Foliage"), flower = flower.Mesh("Flower") };
        }

        static void GroundClamp(MB mb)
        {
            for (int i = 0; i < mb.v.Count; i++)
            {
                Vector3 p = mb.v[i];
                if (p.y < 0f) { p.y = 0f; mb.v[i] = p; }
            }
        }

        // ── archetype generators ─────────────────────────────────────────────────────────────────

        static int _limbBudget;

        static void BuildBranching(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower, bool tree)
        {
            _limbBudget = Mathf.Max(24, Mathf.RoundToInt(400 * Detail));   // hard cap on total limbs so vert count stays bounded
            int depthMax = Mathf.Clamp(g.branchDepth + 1, 3, 6);   // ensure real sub-branching (not bare sticks)
            float baseR = Mathf.Max(g.heightM * (0.025f + 0.03f * g.trunkTaper), 0.03f);
            float trunkLen = Mathf.Max(g.heightM * (tree ? 0.65f : 0.45f), 0.15f);
            int stems = tree ? 1 : Mathf.Clamp(g.stemCount, 2, 5);
            // STILT ROOTS: lift the trunk base off the ground and carry it down on a ring of arching prop roots.
            float lift = g.stiltRoots > 0.01f ? g.heightM * 0.22f * g.stiltRoots : 0f;
            Vector3 origin = Vector3.up * lift;
            if (lift > 0f) AddStiltRoots(g, ref r, wood, origin, baseR, lift);
            for (int i = 0; i < stems; i++)
            {
                Vector3 d = stems == 1 ? Vector3.up : (Quaternion.Euler(r.Range(5f, 20f), 360f * i / stems, 0f) * Vector3.up).normalized;
                GrowLimb(g, ref r, wood, foliage, flower, origin, d, baseR, trunkLen, 0, depthMax, Vector3.zero);
            }
        }

        // Prop roots: each leaves the trunk a little above its base, arches out and down (quadratic Bézier) and
        // flares where it meets the ground; some fork near the bottom. They start INSIDE the trunk so they're joined.
        static void AddStiltRoots(PlantGenome g, ref DetRng r, MB wood, Vector3 trunkBase, float baseR, float lift)
        {
            int n = 4 + Mathf.RoundToInt(g.stiltRoots * 8f);
            for (int i = 0; i < n; i++)
            {
                float az = (360f * i / n + r.Range(-18f, 18f)) * Mathf.Deg2Rad;
                Vector3 outD = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                Vector3 a = trunkBase + Vector3.up * (baseR * r.Range(0.3f, 2.5f)) + outD * baseR * 0.3f;
                Vector3 c = trunkBase + outD * (lift * r.Range(0.55f, 1.15f)); c.y = -baseR * 0.3f;
                Vector3 ctrl = a + outD * (lift * r.Range(0.35f, 0.8f)) + Vector3.up * (lift * r.Range(0.05f, 0.3f));
                float rootR = baseR * r.Range(0.28f, 0.45f);
                var pts = new List<Vector3>(); var radii = new List<float>();
                int m = 10;
                for (int k = 0; k <= m; k++)
                {
                    float t = k / (float)m, it = 1f - t;
                    Vector3 p = it * it * a + 2f * it * t * ctrl + t * t * c;
                    p += NoiseOffset(p, i * 7.1f, rootR * 0.35f) * t;                        // gnarled
                    pts.Add(p); radii.Add(rootR * Mathf.Lerp(1.2f, 0.75f, t) * (t > 0.85f ? 1.6f : 1f));   // flare at the ground
                }
                AddTube(wood, pts, radii, 8, 0.04f);
                if (r.Value < 0.4f)   // fork: a smaller root splits off near the ground
                {
                    Vector3 fs = pts[m - 3];
                    Vector3 fe = fs + (Quaternion.AngleAxis(r.Range(-50f, 50f), Vector3.up) * outD) * lift * 0.35f; fe.y = -baseR * 0.2f;
                    var fp = new List<Vector3> { fs, Vector3.Lerp(fs, fe, 0.5f) + Vector3.up * lift * 0.05f, fe };
                    var fr = new List<float> { rootR * 0.7f, rootR * 0.55f, rootR * 0.8f };
                    AddTube(wood, fp, fr, 6, 0.04f);
                }
            }
        }

        // A LIMB is one continuous tapering tube. Side branches SPROUT from points along it, starting on the
        // centreline and emerging TANGENTIALLY — they begin aligned with the parent and curve outward over the
        // first segments (`emerge` = the parent's direction), so the branch flows out of the trunk as a smooth
        // grazing junction instead of a tube butted in perpendicular. Recurses as its own continuous limbs.
        static void GrowLimb(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower, Vector3 start, Vector3 dir, float radius, float length, int depth, int depthMax, Vector3 emerge, float socket = 0f)
        {
            if (_limbBudget-- <= 0) return;
            bool split = g.trunkSplits && depth < depthMax - 1;   // this limb forks at its end instead of tapering
            int segs = depth == 0 ? 18 : depth == 1 && depthMax > 2 ? 12 : Mathf.Clamp(9 - depth, 4, 9);   // trunk/boughs get many rings so bends stay round, not blocky; more rings along each limb → smoother curves + cleaner junctions
            float seglen = length / segs;
            float endRad = split ? radius * 0.6f : radius * (0.05f + 0.12f * g.trunkTaper);
            var pts = new List<Vector3>(segs + 1); var radii = new List<float>(segs + 1);
            Vector3 cur = start;
            Vector3 target = dir.normalized;
            // Emerge TANGENTIALLY: start growing aligned with the parent, then curve to the intended direction over
            // the first couple segments, so the branch flows out of the trunk instead of butting into it.
            bool tangent = emerge.sqrMagnitude > 1e-6f;
            Vector3 d = tangent ? Vector3.Slerp(emerge.normalized, target, 0.32f).normalized : target;
            Vector3 bendAxis = Perp(target) * (r.Value < 0.5f ? -1f : 1f);
            float phyl = r.Range(0f, 360f);
            bool leafy = depth >= depthMax - 1;   // only the outermost twigs carry leaves (keeps the canopy from becoming a solid mass)
            int children = depth >= depthMax - 1 ? 0
                         : split ? Mathf.RoundToInt(g.branchDensity * 2f)
                         : (depth == 0 ? 4 + Mathf.RoundToInt(g.branchDensity * 3f) : 2 + Mathf.RoundToInt(g.branchDensity * 2f));
            // Big membranes are huge, so the plant carries only a few of them — drastically thin the branching.
            if (g.leaf == LeafShape.Membrane && children > 0) children = Mathf.Max(1, Mathf.RoundToInt(children * 0.35f));

            // ── crown silhouette (branch style): tilt, vertical bias, and whether limbs come in whorled rings ──
            float styleAng = 1f; Vector3 styleBias = Vector3.up * 0.15f; bool whorled = false; int whorls = 4;
            switch (g.branchStyle)
            {
                case BranchStyle.Excurrent: styleAng = 1.45f; styleBias = Vector3.down * (0.15f + depth * 0.12f); whorled = true; whorls = 4; break;   // conical conifer, near-horizontal drooping whorls
                case BranchStyle.Columnar:  styleAng = 0.35f; styleBias = Vector3.up * 0.65f; break;                                                   // steep upswept, narrow
                case BranchStyle.Weeping:   styleAng = 1.15f; styleBias = Vector3.down * (0.12f + depth * 0.28f); break;                               // arch then hang
                case BranchStyle.Whorled:   styleAng = 1.2f;  styleBias = Vector3.down * 0.05f; whorled = true; whorls = 5; break;                     // distinct rings
                case BranchStyle.Sparse:    styleAng = 1.0f;  styleBias = Vector3.up * 0.1f; if (children > 0) children = Mathf.Max(1, children - 2); break;   // open, few limbs (must NOT force ≥1 at the terminal depth or recursion never ends → stack overflow)
                default:                    styleAng = 1.0f;  styleBias = Vector3.up * 0.15f; break;                                                   // Spreading (oak)
            }

            for (int s = 0; s <= segs; s++)
            {
                float t = s / (float)segs;
                float rr = Mathf.Lerp(radius, endRad, t);
                // A gentle basal swell (not a puffy collar) — the tangential emergence does the real blending.
                if (s == 0) rr *= depth == 0 ? 1.25f : 1.3f;
                else if (s == 1 && depth > 0) rr *= 1.1f;
                // SOCKET: flare the first two rings out to ~the parent's radius at the attachment point so the branch
                // base fills the trunk cross-section and cannot float free of it. Tapers back within one segment.
                if (socket > 0f)
                {
                    if (s == 0) rr = Mathf.Max(rr, socket * 0.95f);
                    else if (s == 1) rr = Mathf.Max(rr, socket * 0.55f);
                }
                // Bulbous morphology: a pot-belly swell in the lower-mid of thicker limbs (baobab / bottle tree).
                if (g.trunkBulge > 0.01f)
                {
                    float bulge = Mathf.Exp(-((t - 0.28f) / 0.3f) * ((t - 0.28f) / 0.3f));
                    rr *= 1f + g.trunkBulge * 1.7f * bulge * Mathf.Max(0f, 1f - depth * 0.35f);
                }
                // Pinch the tip to a near-point (leader mode) — UNLESS this tip carries a terminal organ, which needs
                // girth to seat against (a pinched point makes the organ look like it's floating).
                bool organTip = g.terminalOrgan != TerminalOrgan.None && depth >= depthMax - 1;
                if (!split && s == segs) rr *= organTip ? 0.85f : 0.35f;
                pts.Add(cur); radii.Add(rr);

                // Sprout side branches scheduled for this node (spread along the upper part of the limb).
                for (int c = 0; c < children; c++)
                {
                    float lo = depth == 0 ? Mathf.Max(0.45f, g.bareTrunkFrac) : 0.25f;   // bareTrunkFrac clears the lower bole → palm / umbrella crown on a naked stem
                    int node = whorled
                        ? Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(lo, 0.92f, ((c % whorls) + 0.5f) / whorls) * segs), 1, segs)
                        : Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(lo, 0.95f, (c + 0.5f) / children) * segs), 1, segs);
                    if (node != s) continue;
                    phyl += 137.5f;
                    float ang = g.branchAngle * styleAng * (1.1f - depth * 0.04f) * r.Range(0.85f, 1.2f);
                    Vector3 sd = BranchDir(d, ang, phyl, ref r);
                    sd = Vector3.Normalize(sd + styleBias);
                    // Start the child EMBEDDED at the trunk centreline and pass `socket = rr` so its base ring flares out
                    // to the parent radius — the junction is filled from the inside, so branches never float free
                    // (works regardless of exit angle or trunk bulge). Tangential emergence still blends the direction.
                    float childR = rr * r.Range(0.5f, 0.62f);
                    // Recess the base slightly DOWN into the already-built parent so the flared socket ring is buried
                    // inside the trunk wall — otherwise the open (uncapped) start ring shows as a flat cut face.
                    Vector3 childStart = cur - d * (rr * 0.4f);
                    GrowLimb(g, ref r, wood, foliage, flower, childStart, sd, childR, length * r.Range(0.45f, 0.62f), depth + 1, depthMax, d, rr);
                }

                // Phyllotactic leaves along the upper limb — golden-angle spread, seated on the twig SURFACE (not the
                // centreline) so they don't spear through the wood, and kept small relative to the twig.
                if (leafy && s >= 2 && g.leaf != LeafShape.None && g.leaf != LeafShape.Membrane)
                {
                    phyl += 137.5f;
                    Vector3 outdir = (Quaternion.AngleAxis(phyl, d) * Perp(d)).normalized;
                    Vector3 lpos = cur + outdir * rr * 1.05f;
                    PlaceLeaf(foliage, g, ref r, lpos, Vector3.Normalize(outdir * 0.85f + d * 0.3f + Vector3.down * 0.2f), Mathf.Min(g.leafSize, 1.8f) * 0.3f, Mathf.Repeat(phyl / 360f, 1f));
                }

                if (s < segs)
                {
                    cur += d * seglen;
                    if (tangent && s < 2) d = Vector3.Slerp(d, target, 0.55f).normalized;   // curve out of the parent
                    float bend = (0.6f + r.Value) * (9f / segs) * (depth == 0 ? 0.35f : 1f);   // trunk stays fairly upright
                    d = (Quaternion.AngleAxis(bend, bendAxis) * d).normalized;
                    d = Vector3.Normalize(d + Vector3.down * (depth == 0 ? 0.002f : 0.02f + depth * 0.012f)
                                            + Vector3.down * (g.pendant * 0.18f) * (depth == 0 ? 0.1f : 1f));   // pendant: limbs sag/hang
                }
            }

            AddTube(wood, pts, radii, Mathf.Max(28 - depth * 4, 10), 0.025f);   // rounder trunk, fewer sides on twigs

            if (split)
            {
                // Dichotomous fork: the limb ends by splitting into 2–3 roughly-equal limbs (recurses).
                int forks = r.RangeInt(2, 4);
                float fphyl = r.Range(0f, 360f);
                for (int k = 0; k < forks; k++)
                {
                    fphyl += 360f / forks + r.Range(-25f, 25f);
                    float ang = g.branchAngle * r.Range(0.55f, 0.95f);
                    Vector3 fd = Vector3.Normalize(BranchDir(d, ang, fphyl, ref r) + Vector3.up * 0.28f);
                    GrowLimb(g, ref r, wood, foliage, flower, cur, fd, endRad * r.Range(0.72f, 0.94f), length * r.Range(0.62f, 0.85f), depth + 1, depthMax, d, endRad);
                }
            }
            else
            {
                if (g.leaf != LeafShape.None)
                    ScatterLeaves(g, ref r, foliage, cur, d, g.leaf == LeafShape.Membrane ? 1 : Mathf.RoundToInt(g.leafDensity * 2.5f) + 1);
                if (g.hasFlowers && r.Value < g.flowerAmount) AddFlower(flower, g, ref r, cur, d);
                // Big TERMINAL ORGAN at the outermost tips (giant flower / orb / pitcher / membrane / berry / bulb / plume).
                if (g.terminalOrgan != TerminalOrgan.None && depth >= depthMax - 1)
                    AddTerminalOrgan(g, ref r, wood, foliage, flower, cur, d);
            }
            if (g.spininess > 0.45f) AddSpines(wood, cur, endRad * 2f, Mathf.RoundToInt(g.spininess * 4f) + 1, r);
        }

        // A continuous smooth tube through a polyline (shared rings, parallel-transported frame → no seams/facets).
        // `wobble` adds a little per-vertex radius irregularity so trunks/stems read as bark, not machined pipe.
        static void AddTube(MB mb, List<Vector3> pts, List<float> radii, int sides, float wobble = 0f)
        {
            int n = pts.Count; if (n < 2) return;
            sides = Res(sides, 4);
            int cols = sides + 1;   // DUPLICATE seam column: the last vertex sits on top of the first but with U=1,
                                    // so the wrapping quad no longer crams the whole texture into one face (the seam scrunch).
            int b0 = mb.Count;
            Vector3 right = Perp((pts[1] - pts[0]).normalized);
            float vlen = 0f;   // cumulative length along the tube → texture V coordinate
            for (int i = 0; i < n; i++)
            {
                if (i > 0) vlen += Vector3.Distance(pts[i], pts[i - 1]);
                Vector3 dir = i == 0 ? (pts[1] - pts[0]) : i == n - 1 ? (pts[n - 1] - pts[n - 2]) : (pts[i + 1] - pts[i - 1]);
                dir = dir.normalized;
                right = right - dir * Vector3.Dot(right, dir);
                if (right.sqrMagnitude < 1e-6f) right = Perp(dir);
                right.Normalize();
                Vector3 fwd = Vector3.Cross(dir, right);
                float circ = 6.2831853f * Mathf.Max(radii[i], 0.01f);
                for (int k = 0; k <= sides; k++)   // 0..sides inclusive; k==sides duplicates k==0's position with U=1
                {
                    float a = 2f * Mathf.PI * k / sides;
                    Vector3 off = right * Mathf.Cos(a) + fwd * Mathf.Sin(a);
                    float rr = radii[i] * (1f + wobble * (Hash01(i * 131 + (k % sides) * 17) - 0.5f) * 2f);
                    mb.Vert(pts[i] + off * rr, off, 0f, new Vector2(k / (float)sides, vlen), circ);
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = b0 + i * cols + k, b = a + 1;
                    int c = a + cols, dd = b + cols;
                    mb.Tri(a, b, c); mb.Tri(b, dd, c);   // outward-facing winding, no %wrap → seamless
                }

            // End caps so a tube is never an open pipe (palm crowns showing a hollow top, cut branch/twig ends).
            // Winding is AUTO-ORIENTED from the actual geometry so the cap always faces outward (a wrong guess would
            // be back-face-culled → still looks hollow).
            AddTubeCap(mb, pts[n - 1], (pts[n - 1] - pts[n - 2]).normalized, b0 + (n - 1) * cols, sides);
            AddTubeCap(mb, pts[0], (pts[0] - pts[1]).normalized, b0, sides);
        }

        // Close one end of a tube with a fan from a centre vertex, choosing the winding that faces `axis` (outward).
        static void AddTubeCap(MB mb, Vector3 center, Vector3 axis, int ring0, int sides)
        {
            int c = mb.Count;
            mb.Vert(center, axis, 0f);
            // Test the first triangle's winding against the outward axis and pick the order that faces outward.
            Vector3 p0 = mb.v[ring0 + 0], p1 = mb.v[ring0 + 1];
            bool forward = Vector3.Dot(Vector3.Cross(p0 - center, p1 - center), axis) >= 0f;
            for (int k = 0; k < sides; k++)
            {
                int a = ring0 + k, b = ring0 + k + 1;
                if (forward) mb.Tri(c, a, b); else mb.Tri(c, b, a);
            }
        }

        // VINE: a surface crawler. It hugs whatever it touches — rocks, terrain, other plants' colliders — by probing
        // with raycasts each step: it follows the surface tangent, climbs walls it runs into (phototropic up-bias),
        // drapes over edges, and creeps along the ground (never below it) when there's nothing to climb. A few side
        // runners branch off. Leaves face away from the surface they grow on.
        static void BuildVine(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            float rad = Mathf.Clamp(g.heightM * 0.012f, 0.025f, 0.12f);
            float totalLen = Mathf.Clamp(g.heightM * 2.2f, 3f, 40f);
            int runners = 1 + Mathf.RoundToInt(g.branchDensity * 3f);
            for (int k = 0; k < runners; k++)
            {
                float a = r.Range(-35f, 35f) + (k == 0 ? 0f : r.Range(-90f, 90f));
                Vector3 start = new Vector3(-0.5f, 0f, r.Range(-0.3f, 0.3f));
                Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * Vector3.right;   // toward the host by default
                float len = totalLen * (k == 0 ? 1f : r.Range(0.4f, 0.75f));
                CrawlVine(g, ref r, wood, foliage, flower, start, Vector3.up, dir, rad * (k == 0 ? 1f : 0.75f), len);
            }
        }

        static void CrawlVine(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower, Vector3 p, Vector3 n, Vector3 dir,
                              float rad, float length)
        {
            float step = Mathf.Max(rad * 3.5f, 0.08f);
            int segs = Mathf.Clamp(Mathf.RoundToInt(length / step), 8, 220);
            float climb = 0.35f + (1f - g.lean) * 0.6f;   // upright genomes climb eagerly, sprawling ones creep
            var pts = new List<Vector3>(); var radii = new List<float>();
            p.y = Mathf.Max(p.y, rad);
            dir = Vector3.ProjectOnPlane(dir, n).normalized;
            float wanderPhase = r.Range(0f, 10f);
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                pts.Add(p); radii.Add(rad * Mathf.Lerp(1f, 0.35f, t));

                if (i % 2 == 0 && g.leaf != LeafShape.None)
                    ScatterLeaves(g, ref r, foliage, p, Vector3.Normalize(n + dir * 0.3f), 1);   // leaves face off the surface
                if (g.hasFlowers && r.Value < g.flowerAmount * 0.12f) AddFlower(flower, g, ref r, p + n * rad, n);

                // steer: wander in the surface plane + phototropic pull upward
                float wob = Mathf.Sin(i * 0.35f + wanderPhase) * 0.5f + r.Range(-0.25f, 0.25f);
                Vector3 side = Vector3.Cross(n, dir);
                Vector3 want = dir + side * wob + Vector3.up * climb * 0.5f;
                dir = Vector3.ProjectOnPlane(want, n).normalized;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.ProjectOnPlane(Vector3.up, n).normalized;

                Vector3 target = p + dir * step;
                Vector3 np, nn;
                if (Physics.Raycast(p, dir, out RaycastHit wall, step * 1.3f))
                {   // ran into something: turn onto its face and climb it
                    nn = wall.normal; np = wall.point + nn * rad;
                    dir = Vector3.ProjectOnPlane(dir + Vector3.up * 1.5f, nn).normalized;
                }
                else if (Physics.Raycast(target + n * step * 1.5f, -n, out RaycastHit hug, step * 3.5f))
                {   // stay glued to the current surface as it curves
                    nn = hug.normal; np = hug.point + nn * rad;
                }
                else if (Physics.Raycast(target, -n, out RaycastHit wrap, step * 4f) ||
                         Physics.Raycast(target - n * rad * 2f, -dir, out wrap, step * 2f))
                {   // wrapped over an edge: follow round the corner
                    nn = wrap.normal; np = wrap.point + nn * rad;
                }
                else
                {   // nothing underneath: droop under gravity (drapes off overhangs)
                    np = target + Vector3.down * step * 0.6f; nn = Vector3.Lerp(n, Vector3.up, 0.3f).normalized;
                }
                // the ground is a surface too — never sink below it
                if (np.y < rad) { np.y = rad; nn = Vector3.up; }
                n = nn.sqrMagnitude > 1e-6f ? nn.normalized : Vector3.up;
                dir = Vector3.ProjectOnPlane(dir, n).normalized;
                if (dir.sqrMagnitude < 1e-6f) dir = Perp(n);
                p = np;
            }
            AddTube(wood, pts, radii, 8, 0.03f);
        }

        static void BuildHerb(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            int stems = Mathf.Clamp(g.stemCount + 2, 3, 8);
            float h = Mathf.Clamp(g.heightM, 0.3f, 3f);
            for (int i = 0; i < stems; i++)
            {
                Vector3 d = (Quaternion.Euler(r.Range(4f, 16f), 360f * i / stems, 0f) * Vector3.up).normalized;
                Vector3 p = Vector3.zero; float rad = h * 0.02f;
                int segs = 4;
                for (int s = 0; s < segs; s++)
                {
                    Vector3 end = p + d * (h / segs);
                    AddSegment(wood, p, end, rad, rad * 0.8f, d, 5, 0f, r);
                    if (r.Value < 0.6f) ScatterLeaves(g, ref r, foliage, p, PerpUp(d), 2);
                    p = end; d = (Quaternion.AngleAxis(r.Range(-10f, 10f), Perp(d)) * d).normalized; rad *= 0.8f;
                }
                AddFlower(flower, g, ref r, p, d);
            }
        }


        static void BuildRosetteFronds(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            int fronds = 6 + Mathf.RoundToInt(g.leafDensity * 6f);
            float L = Mathf.Clamp(g.heightM, 0.5f, 6f);
            for (int i = 0; i < fronds; i++)
            {
                float az = 360f * i / fronds + r.Range(-12f, 12f);
                Vector3 dir = (Quaternion.AngleAxis(az, Vector3.up) * Quaternion.AngleAxis(r.Range(25f, 60f), Vector3.right) * Vector3.up).normalized;
                Vector3 baseTip = AddLeafBase(wood, Vector3.zero, dir, L * 0.03f, L * 0.12f);
                AddFrond(foliage, g, ref r, baseTip, dir, L * g.leafSize, true);
            }
        }

        static void BuildGrass(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            int blades = 20 + Mathf.RoundToInt(g.leafDensity * 30f);
            float L = Mathf.Clamp(g.heightM, 0.2f, 4f);
            for (int i = 0; i < blades; i++)
            {
                Vector3 baseP = new Vector3(r.Range(-0.3f, 0.3f), 0f, r.Range(-0.3f, 0.3f));
                Vector3 dir = (Vector3.up + new Vector3(r.Range(-0.5f, 0.5f), 0f, r.Range(-0.5f, 0.5f))).normalized;
                AddBlade(foliage, g, baseP, dir, 0.03f * g.leafSize * (0.7f + r.Value), L * (0.6f + 0.6f * r.Value), r.Range(0.2f, 0.6f));
            }
            if (g.hasFlowers) for (int i = 0; i < 3; i++) AddFlower(flower, g, ref r, new Vector3(r.Range(-0.2f, 0.2f), L * 0.9f, r.Range(-0.2f, 0.2f)), Vector3.up);
        }

        static void BuildCactus(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            // Five real cactus body plans, all built from continuous, gently-bending, ribbed, ROUND-TOPPED bodies
            // with areoles (the felted cushions spines grow from) on the rib crests:
            // 0 columnar with arms that sweep out and curve up (saguaro) · 1 barrel · 2 organ-pipe clump ·
            // 3 jointed chains of segments (cholla) · 4 sprawling serpent stems.
            float h = Mathf.Clamp(g.heightM, 0.4f, 10f) * 0.75f;
            float rad = Mathf.Max(h * 0.13f * g.bodyGirth, 0.08f);
            int ribs = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(6f, 22f, r.Value) * (0.6f + g.ribbing * 0.6f)), 5, 26);
            float ribDepth = 0.05f + g.ribbing * 0.2f;
            int form = r.RangeInt(0, 5);
            var tips = new List<(Vector3 p, Vector3 d)>();
            switch (form)
            {
                case 1: // barrel: squat, fat, slightly leaning
                {
                    float bh = h * 0.45f, br = rad * 2.1f;
                    var path = Arc(Vector3.zero, Vector3.up, new Vector3(r.Range(-0.1f, 0.1f), 0f, r.Range(-0.1f, 0.1f)), bh, 8);
                    var radii = Profile(path.Count, br, t => 0.82f + 0.3f * Mathf.Sin(t * Mathf.PI * 0.9f));
                    tips.Add(AddRibbedBody(g, ref r, foliage, wood, path, radii, ribs + 6, ribDepth * 1.3f));
                    break;
                }
                case 2: // organ-pipe: many columns from one base
                {
                    int n = 4 + r.RangeInt(0, 6);
                    for (int i = 0; i < n; i++)
                    {
                        float az = i * 137.5f * Mathf.Deg2Rad;
                        Vector3 b = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)) * rad * (i == 0 ? 0f : 0.9f + 0.5f * Mathf.Sqrt(i));
                        Vector3 bend = new Vector3(b.x, 0f, b.z).normalized * r.Range(0.03f, 0.12f) + new Vector3(r.Range(-0.04f, 0.04f), 0f, r.Range(-0.04f, 0.04f));
                        var path = Arc(b, Vector3.up, bend, h * r.Range(0.55f, 1f), 12);
                        tips.Add(AddRibbedBody(g, ref r, foliage, wood, path, Profile(path.Count, rad * 0.7f, t => 1f), ribs, ribDepth));
                    }
                    break;
                }
                case 3: // cholla: chains of ovoid joints branching at the joints
                {
                    var stack = new List<(Vector3 p, Vector3 d, int depth)> { (Vector3.zero, Vector3.up, 0) };
                    int budget = 18;
                    while (stack.Count > 0 && budget-- > 0)
                    {
                        var (p0, d0, dep) = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                        float segL = h * r.Range(0.18f, 0.3f) * (dep == 0 ? 1.4f : 1f);
                        var path = Arc(p0 - d0 * rad * 0.25f, d0, new Vector3(r.Range(-0.15f, 0.15f), 0.05f, r.Range(-0.15f, 0.15f)), segL, 8);
                        var t = AddRibbedBody(g, ref r, foliage, wood, path, Profile(path.Count, rad * 0.6f, u => 0.55f + 0.5f * Mathf.Sin(u * Mathf.PI)), Mathf.Max(5, ribs / 2), ribDepth * 0.6f);
                        if (dep < 3)
                            for (int c = 0, nc = r.RangeInt(1, 4); c < nc; c++)
                                stack.Add((t.p, Vector3.Normalize(t.d + new Vector3(r.Range(-0.9f, 0.9f), r.Range(0f, 0.3f), r.Range(-0.9f, 0.9f))), dep + 1));
                        else tips.Add(t);
                    }
                    break;
                }
                case 4: // serpent: stems that sprawl along the ground and lift their tips
                {
                    int n = 2 + r.RangeInt(0, 4);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 d0 = Quaternion.AngleAxis(360f * i / n + r.Range(-25f, 25f), Vector3.up) * new Vector3(1f, 0.08f, 0f);
                        var path = new List<Vector3>(); Vector3 p = Vector3.up * rad * 0.5f, d = d0.normalized;
                        float L = h * r.Range(1f, 1.8f); int m = 18;
                        for (int k = 0; k <= m; k++)
                        {
                            path.Add(p); float t = k / (float)m;
                            p += d * (L / m);
                            d = Vector3.Normalize(Quaternion.AngleAxis(r.Range(-18f, 18f), Vector3.up) * d + Vector3.up * (t > 0.7f ? 0.25f : -0.02f));
                            if (p.y < rad * 0.5f) p.y = rad * 0.5f;
                        }
                        tips.Add(AddRibbedBody(g, ref r, foliage, wood, path, Profile(path.Count, rad * 0.55f, t => 1f - 0.2f * t), ribs, ribDepth));
                    }
                    break;
                }
                default: // saguaro: trunk + arms sweeping out and curving upward (one smooth curve, not an elbow)
                {
                    var trunk = Arc(Vector3.zero, Vector3.up, new Vector3(r.Range(-0.05f, 0.05f), 0f, r.Range(-0.05f, 0.05f)), h, 16);
                    tips.Add(AddRibbedBody(g, ref r, foliage, wood, trunk, Profile(trunk.Count, rad, t => 1f + 0.08f * Mathf.Sin(t * 5f)), ribs, ribDepth));
                    int arms = r.Value < 0.75f ? r.RangeInt(1, 5) : 0;
                    for (int i = 0; i < arms; i++)
                    {
                        float frac = r.Range(0.3f, 0.7f);
                        int idx = Mathf.RoundToInt(frac * (trunk.Count - 1));
                        Vector3 outD = Quaternion.AngleAxis(360f * i / arms + r.Range(-30f, 30f), Vector3.up) * Vector3.right;
                        Vector3 a = trunk[idx] - outD * rad * 0.3f;                          // starts inside the trunk
                        Vector3 ctrl = trunk[idx] + outD * rad * r.Range(2.2f, 3.5f) - Vector3.up * rad * 0.2f;
                        Vector3 end = ctrl + Vector3.up * h * frac * r.Range(0.6f, 1f);
                        var arm = new List<Vector3>();
                        for (int k = 0; k <= 16; k++)
                        {
                            float t = k / 16f, it = 1f - t;
                            arm.Add(it * it * a + 2f * it * t * ctrl + t * t * end);          // one smooth J-curve
                        }
                        tips.Add(AddRibbedBody(g, ref r, foliage, wood, arm, Profile(arm.Count, rad * 0.72f, t => 1f), ribs, ribDepth));
                    }
                    break;
                }
            }
            if (g.hasFlowers)
                foreach (var t in tips)
                    if (r.Value < 0.4f + g.flowerAmount * 0.6f) AddFlower(flower, g, ref r, t.p, t.d);
        }

        // A path that starts at `p`, heads along `d`, and drifts by `bend` per step (gentle organic curvature).
        static List<Vector3> Arc(Vector3 p, Vector3 d, Vector3 bend, float len, int steps)
        {
            var pts = new List<Vector3>(); d = d.normalized;
            for (int k = 0; k <= steps; k++) { pts.Add(p); p += d * (len / steps); d = Vector3.Normalize(d + bend * (1f / steps) * 4f); }
            return pts;
        }
        static List<float> Profile(int n, float r0, System.Func<float, float> f)
        {
            var l = new List<float>(n);
            for (int i = 0; i < n; i++) l.Add(r0 * f(i / (float)Mathf.Max(n - 1, 1)));
            return l;
        }

        // A continuous succulent body along a path: ribbed cross-section (crests & furrows), gentle wobble, a ROUNDED
        // dome top (the path is extended by a hemisphere), recomputed normals so ribs shade properly, and areoles —
        // pale felted cushions on each rib crest, each sprouting a cluster of spines. Returns the crown point + dir.
        static (Vector3 p, Vector3 d) AddRibbedBody(PlantGenome g, ref DetRng r, MB body, MB spines, List<Vector3> path, List<float> radii, int ribs, float ribDepth)
        {
            // extend with a hemispherical dome so the top is rounded, not a cut pipe
            Vector3 tipDir = (path[path.Count - 1] - path[path.Count - 2]).normalized;
            float topR = radii[radii.Count - 1];
            Vector3 top0 = path[path.Count - 1];
            var P = new List<Vector3>(path); var R = new List<float>(radii);
            for (int k = 1; k <= 5; k++)
            {
                float a = k / 5f * Mathf.PI * 0.5f;
                P.Add(top0 + tipDir * (Mathf.Sin(a) * topR * 0.85f)); R.Add(Mathf.Max(Mathf.Cos(a) * topR, topR * 0.02f));
            }
            int n = P.Count, sides = Mathf.Max(ribs * 4, 20), cols = sides + 1;
            var grid = new Vector3[cols, n];
            Vector3 right = Perp((P[1] - P[0]).normalized);
            float seed = r.Range(0f, 50f), vlen = 0f; var vs = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i > 0) vlen += Vector3.Distance(P[i], P[i - 1]);
                vs[i] = vlen;
                Vector3 dir = (i == 0 ? P[1] - P[0] : i == n - 1 ? P[n - 1] - P[n - 2] : P[i + 1] - P[i - 1]).normalized;
                right = (right - dir * Vector3.Dot(right, dir)); if (right.sqrMagnitude < 1e-6f) right = Perp(dir); right.Normalize();
                Vector3 fwd = Vector3.Cross(dir, right);
                for (int k = 0; k <= sides; k++)
                {
                    float th = 2f * Mathf.PI * k / sides;
                    float rib = 1f - ribDepth * (0.5f - 0.5f * Mathf.Cos(ribs * th));            // crest at th = 2πj/ribs
                    Vector3 off = right * Mathf.Cos(th) + fwd * Mathf.Sin(th);
                    Vector3 pp = P[i] + off * (R[i] * rib);
                    pp += NoiseOffset(pp, seed, R[i] * 0.03f);
                    grid[k, i] = pp;
                }
            }
            int b0 = body.Count; body.tint = Color.white;
            for (int i = 0; i < n; i++)
                for (int k = 0; k <= sides; k++)
                {
                    Vector3 du = grid[Mathf.Min(k + 1, sides), i] - grid[Mathf.Max(k - 1, 0), i];
                    Vector3 dv = grid[k, Mathf.Min(i + 1, n - 1)] - grid[k, Mathf.Max(i - 1, 0)];
                    Vector3 nn = Vector3.Cross(du, dv);
                    if (nn.sqrMagnitude < 1e-10f) nn = grid[k, i] - P[i];
                    if (Vector3.Dot(nn, grid[k, i] - P[i]) < 0f) nn = -nn;                        // always outward
                    float rib = 0.5f - 0.5f * Mathf.Cos(ribs * 2f * Mathf.PI * k / sides);
                    body.Vert(grid[k, i], nn, rib * 0.35f, new Vector2(k / (float)sides, vs[i]), 6.2831853f * Mathf.Max(R[i], 0.01f));
                }
            for (int i = 0; i < n - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = b0 + i * cols + k, b = a + 1, c = a + cols, dd = b + cols;
                    body.Tri(a, b, c); body.Tri(b, dd, c);
                }

            // areoles + spines along each crest
            if (g.spininess > 0.05f)
            {
                float spacing = Mathf.Max(R[0] * 0.55f, 0.02f), acc = 0f;
                for (int i = 1; i < n - 2; i++)
                {
                    acc += Vector3.Distance(P[i], P[i - 1]);
                    if (acc < spacing) continue; acc = 0f;
                    for (int j = 0; j < ribs; j++)
                    {
                        int k = Mathf.RoundToInt(j * sides / (float)ribs + (i % 2) * 0.5f * sides / ribs) % sides;
                        Vector3 bp = grid[k, i], outN = (grid[k, i] - P[i]).normalized;
                        spines.tint = new Color(1.6f, 1.5f, 1.35f);                       // pale felt cushion
                        AddSphere(spines, bp, R[i] * 0.06f, 0f, PaintJob.Solid, 0.7f, 5, 3);
                        spines.tint = Color.white;
                        int ns = 1 + Mathf.RoundToInt(g.spininess * 6f);
                        for (int s = 0; s < ns; s++)
                        {
                            Vector3 sd = Vector3.Normalize(outN + new Vector3(r.Range(-0.7f, 0.7f), r.Range(-0.5f, 0.7f), r.Range(-0.7f, 0.7f)) * 0.8f);
                            Vector3 tip = bp + sd * R[i] * r.Range(0.25f, 0.6f) * (0.5f + g.spininess);
                            Vector3 sw = PerpUp(sd) * R[i] * 0.012f;
                            int q = spines.Count;
                            spines.Vert(bp - sw, sd, 0f); spines.Vert(bp + sw, sd, 0f); spines.Vert(tip, sd, 0f);
                            spines.Tri(q, q + 1, q + 2); spines.Tri(q, q + 2, q + 1);
                        }
                    }
                }
            }
            return (P[n - 1], tipDir);
        }

        static void BuildSucculent(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            int rings = 3;
            int leaves = 8 + Mathf.RoundToInt(g.leafDensity * 6f);
            float L = Mathf.Clamp(g.heightM, 0.2f, 3f);
            for (int ring = 0; ring < rings; ring++)
            {
                float elev = 20f + ring * 22f;
                float scale = 1f - ring * 0.22f;
                int cnt = Mathf.Max(4, leaves - ring * 3);
                for (int i = 0; i < cnt; i++)
                {
                    float az = 360f * i / cnt + ring * 25f;
                    Vector3 dir = (Quaternion.AngleAxis(az, Vector3.up) * Quaternion.AngleAxis(elev, Vector3.right) * Vector3.up).normalized;
                    AddThickLeaf(foliage, g, Vector3.up * ring * L * 0.08f, dir, L * scale * g.leafSize, 0.14f * g.bodyGirth * scale);
                }
            }
            if (g.hasFlowers) AddFlower(flower, g, ref r, Vector3.up * L * 0.4f, Vector3.up);
        }

        static void BuildGlobe(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            float rad = Mathf.Max(Mathf.Clamp(g.heightM, 0.2f, 3f) * 0.5f * g.bodyGirth, 0.15f);
            float squash = Mathf.Lerp(1f, 0.7f, g.ribbing);
            // Sit the body slightly INTO the ground (its bottom is then clamped flat), so it never hovers.
            Vector3 c = Vector3.up * (rad * squash * 0.85f);
            AddOrganSphere(foliage, g, ref r, c, rad, squash);
            // Points ON the squashed ellipsoid, pulled slightly inward so organs root into the body (relief can dent it).
            Vector3 Surf(Vector3 u) => c + Vector3.Scale(u, new Vector3(rad, rad * squash, rad)) * 0.9f;
            Vector3 SurfN(Vector3 u) => Vector3.Normalize(new Vector3(u.x / rad, u.y / (rad * squash), u.z / rad));
            if (g.spininess > 0.1f)
            {
                int ns = Mathf.RoundToInt(40 * g.spininess);
                for (int i = 0; i < ns; i++)
                {
                    Vector3 u = new Vector3(r.Range(-1f, 1f), r.Range(-0.2f, 1f), r.Range(-1f, 1f)).normalized;
                    Vector3 bp = Surf(u), sd = SurfN(u), sw = PerpUp(sd) * rad * 0.02f;
                    int q = wood.Count;
                    wood.Vert(bp - sw, sd, 0f); wood.Vert(bp + sw, sd, 0f); wood.Vert(bp + sd * rad * r.Range(0.25f, 0.45f), sd, 0f);
                    wood.Tri(q, q + 1, q + 2); wood.Tri(q, q + 2, q + 1);
                }
            }
            if (g.hasFlowers) for (int i = 0; i < 1 + Mathf.RoundToInt(g.flowerAmount * 3); i++)
            {
                Vector3 u = new Vector3(r.Range(-0.7f, 0.7f), r.Range(0.4f, 1f), r.Range(-0.7f, 0.7f)).normalized;
                AddFlower(flower, g, ref r, Surf(u), SurfN(u));
            }
            if (g.leaf != LeafShape.None)   // any leaves sprout from the body surface, never from thin air
                for (int i = 0; i < 3 + Mathf.RoundToInt(g.leafDensity * 4f); i++)
                {
                    Vector3 u = new Vector3(r.Range(-1f, 1f), r.Range(0.1f, 1f), r.Range(-1f, 1f)).normalized;
                    PlaceLeaf(foliage, g, ref r, Surf(u), SurfN(u), Mathf.Min(g.leafSize, 1.8f) * 0.35f, r.Range(0f, 1f));
                }
        }

        static void BuildMat(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            // A COLONY, not scattered balls: a few founding cushions whose offspring crowd around them, overlapping into
            // a continuous lumpy mound — biggest and tallest at each centre, thinning to small flat lobes at the edge —
            // with flat lobed thallus sheets creeping outward along the ground.
            int colonies = r.RangeInt(2, 5);
            var centres = new Vector3[colonies];
            for (int k = 0; k < colonies; k++) centres[k] = new Vector3(r.Range(-1.3f, 1.3f), 0f, r.Range(-1.3f, 1.3f));
            int blobs = 22 + Mathf.RoundToInt(g.leafDensity * 26f);
            for (int i = 0; i < blobs; i++)
            {
                Vector3 cc = centres[r.RangeInt(0, colonies)];
                float spread = r.Range(0f, 1f); spread *= spread;                     // most crowd near the centre
                float ang = r.Range(0f, Mathf.PI * 2f);
                Vector3 pos = cc + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (spread * 1.1f);
                float rad = Mathf.Lerp(0.42f, 0.08f, spread) * r.Range(0.7f, 1.2f) * g.bodyGirth;
                float sq = Mathf.Lerp(0.6f, 0.3f, spread) * r.Range(0.8f, 1.15f);
                AddOrganSphere(foliage, g, ref r, pos + Vector3.up * rad * sq * 0.45f, rad, sq, 16, 10);
                if (spread > 0.45f && r.Value < 0.5f)                                 // thallus lobes creeping outward
                {
                    Vector3 outD = (pos - cc).normalized + Vector3.up * 0.25f;
                    AddDisc(foliage, g, pos + Vector3.up * 0.01f, outD.normalized, rad * r.Range(1.6f, 2.6f), r.Range(0f, 1f), true);
                }
                if (g.leaf == LeafShape.Blade) for (int b = 0; b < 3; b++)
                    AddBlade(foliage, g, pos, (Vector3.up + new Vector3(r.Range(-0.6f, 0.6f), 0, r.Range(-0.6f, 0.6f))).normalized, 0.03f, r.Range(0.4f, 1f) * g.leafSize, 0.5f);
            }
        }

        static void BuildMushroom(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            float h = Mathf.Clamp(g.heightM, 0.2f, 3f);
            float rad = h * 0.08f * g.bodyGirth;
            // Stalk that swells at the base (many fungi do) rather than a straight peg.
            var pts = new List<Vector3>(); var radii = new List<float>();
            int sn = 5;
            for (int s = 0; s <= sn; s++)
            {
                float t = s / (float)sn;
                pts.Add(Vector3.up * (t * h));
                radii.Add(rad * (0.8f + 0.6f * Mathf.Exp(-t * 4f)) * (1f + 0.05f * Mathf.Sin(t * 8f)));
            }
            AddTube(wood, pts, radii, 9, 0.03f);
            Vector3 capBase = Vector3.up * h;
            float capR = h * 0.4f * g.bodyGirth;
            float capH = capR * r.Range(0.35f, 0.9f);   // dome height varies (button ↔ parasol)
            float droop = r.Range(0.15f, 0.7f);          // how far the rim curls down
            Vector3 capTop = capBase - Vector3.up * (capH * 0.9f);    // CapY(0)==capH → cap centre sits just over the stalk top
            AddOrganicCap(flower, g, ref r, capTop, capR, capH, droop);

            // Gills: radial sheets whose TOP edge follows the cap's underside, hanging a little below it (deepest mid-
            // span, tapering at the stalk and the rim) — tucked under the cap instead of poking out past the rim.
            int gills = 26 + Mathf.RoundToInt(g.bodyGirth * 18f);
            flower.tint = new Color(0.62f, 0.56f, 0.55f);
            float thick = capH * 0.08f;
            for (int i = 0; i < gills; i++)
            {
                float a = 2f * Mathf.PI * i / gills + r.Range(-0.02f, 0.02f);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 nrm = Vector3.Cross(d, Vector3.up);
                bool shortGill = (i % 2) == 1;                         // alternate full & partial gills (real lamellae)
                float u0 = shortGill ? 0.55f : 0.14f, u1 = 0.94f;
                int q = flower.Count, n = 6;
                for (int k = 0; k <= n; k++)
                {
                    float u = Mathf.Lerp(u0, u1, k / (float)n);
                    Vector3 topP = capTop + d * (u * capR) + Vector3.up * (CapY(u, capH, droop) - thick);
                    float drop = capR * 0.13f * Mathf.Sin(Mathf.InverseLerp(u0, u1, u) * Mathf.PI);
                    flower.Vert(topP, nrm, 0.2f);
                    flower.Vert(topP + Vector3.down * drop, nrm, 0.6f);
                }
                for (int k = 0; k < n; k++)
                {
                    int t0 = q + k * 2, t1 = t0 + 2;
                    flower.Tri(t0, t0 + 1, t1); flower.Tri(t1, t0 + 1, t1 + 1);
                    flower.Tri(t0, t1, t0 + 1); flower.Tri(t1, t1 + 1, t0 + 1);   // two-sided
                }
            }
            flower.tint = Color.white;
        }

        static void BuildTendril(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            float h = Mathf.Clamp(g.heightM, 0.5f, 10f);
            float stalkH = h * 0.35f;
            AddSegment(wood, Vector3.zero, Vector3.up * stalkH, h * 0.03f, h * 0.024f, Vector3.up, 6, 0f, r);
            Vector3 head = Vector3.up * stalkH;
            float br = h * 0.12f * g.bodyGirth;
            AddOrganSphere(foliage, g, ref r, head + Vector3.up * br, br, 0.85f);   // medusa head bulb: relief + pattern
            int tendrils = 8 + Mathf.RoundToInt(g.leafDensity * 10f);
            for (int i = 0; i < tendrils; i++)
            {
                Vector3 d = (Vector3.down + new Vector3(r.Range(-1f, 1f), 0f, r.Range(-1f, 1f)) * 0.6f).normalized;
                Vector3 p = head + Vector3.up * br * 0.6f; float rad = h * 0.018f; float seglen = h * 0.6f / 6f;
                for (int s = 0; s < 6; s++)
                {
                    Vector3 end = p + d * seglen;
                    AddSegment(foliage, p, end, rad, rad * 0.85f, d, 4, 0f, r);
                    p = end; rad *= 0.85f;
                    d = Vector3.Normalize(Vector3.Lerp(d, Vector3.down, 0.3f) + new Vector3(r.Range(-0.3f, 0.3f), 0f, r.Range(-0.3f, 0.3f)));
                }
                if (g.hasFlowers && r.Value < g.flowerAmount * 0.4f) AddFlower(flower, g, ref r, p, d);
            }
        }

        static void BuildOrbCluster(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            float h = Mathf.Clamp(g.heightM, 0.4f, 8f);
            int nodes = 3 + Mathf.RoundToInt(g.branchDensity * 4f);
            Vector3 p = Vector3.zero, d = Vector3.up; float rad = h * 0.025f;
            for (int nnode = 0; nnode < nodes; nnode++)
            {
                Vector3 end = p + d * (h / nodes);
                AddSegment(wood, p, end, rad, rad * 0.9f, d, 5, 0f, r);
                int orbs = 3 + Mathf.RoundToInt(g.leafDensity * 4f);
                for (int j = 0; j < orbs; j++)
                {
                    // Pendant lets the orb clusters dangle below the node (grape-bunch look).
                    Vector3 sprayDir = new Vector3(r.Range(-1f, 1f), r.Range(-0.6f, 0.3f) - g.pendant * 1.2f, r.Range(-1f, 1f)).normalized;
                    float orr = h * 0.05f * g.bodyGirth * r.Range(0.7f, 1.3f);
                    float stalkL = orr * r.Range(0.3f, 0.9f) + g.pendant * h * 0.08f;
                    // pedicel: a thin drooping stalk from the node into the orb (it ends INSIDE the orb → attached)
                    var sp = new List<Vector3>(); var sr = new List<float>();
                    Vector3 sd = sprayDir, q = end;
                    for (int k = 0; k <= 4; k++)
                    {
                        sp.Add(q); sr.Add(Mathf.Lerp(rad * 0.45f, rad * 0.25f, k / 4f));
                        q += sd * ((stalkL + orr * 0.5f) / 4f); sd = Vector3.Normalize(sd + Vector3.down * 0.15f * g.pendant);
                    }
                    AddTube(wood, sp, sr, 5, 0.02f);
                    AddOrganSphere(foliage, g, ref r, sp[4] + sd * (orr * 0.6f), orr, r.Range(0.85f, 1.2f));   // stalk tip is buried inside
                }
                p = end; d = (Quaternion.AngleAxis(r.Range(-12f, 12f), Perp(d)) * d).normalized; rad *= 0.86f;
            }
            if (g.hasFlowers) AddFlower(flower, g, ref r, p, Vector3.up);
        }

        static void BuildTube(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            // A colony of open funnels. Each is an organic pitcher body (belly, curve, relief, peristome, dark throat),
            // placed on a phyllotactic spiral whose spacing exceeds their footprint so they never interpenetrate;
            // outer ones lean outward like a real clump.
            int tubes = 3 + Mathf.RoundToInt(g.leafDensity * 4f);
            float H = Mathf.Clamp(g.heightM, 0.3f, 4f);
            float unit = H * 0.42f * Mathf.Clamp(g.bodyGirth, 0.6f, 1.6f);
            bool fringe = r.Value < 0.5f;
            for (int i = 0; i < tubes; i++)
            {
                float s = unit * (i == 0 ? 1f : r.Range(0.55f, 1f));
                float ring = unit * 0.62f * Mathf.Sqrt(i);                        // Vogel spiral: area-even packing
                float az = i * 137.508f * Mathf.Deg2Rad;
                Vector3 outr = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                Vector3 baseP = outr * ring;
                Vector3 dir = Vector3.Normalize(Vector3.up + outr * (0.12f + 0.22f * Mathf.Sqrt(i) / Mathf.Sqrt(tubes)));
                AddPitcher(foliage, g, ref r, baseP, dir, s, fringe);
                if (g.hasFlowers && r.Value < g.flowerAmount * 0.5f) AddFlower(flower, g, ref r, baseP + dir * s * 1.6f, dir);
            }
        }

        static void BuildRibbon(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            int ribbons = 4 + Mathf.RoundToInt(g.leafDensity * 6f);
            float L = Mathf.Clamp(g.heightM, 0.5f, 12f);
            for (int i = 0; i < ribbons; i++)
            {
                Vector3 baseP = new Vector3(r.Range(-0.4f, 0.4f), 0f, r.Range(-0.4f, 0.4f));
                Vector3 dir = (Vector3.up + new Vector3(r.Range(-0.3f, 0.3f), 0f, r.Range(-0.3f, 0.3f))).normalized;
                AddBlade(foliage, g, baseP, dir, L * 0.12f * g.leafSize * g.bodyGirth, L * r.Range(0.7f, 1.1f), r.Range(0.15f, 0.5f));
            }
        }

        static void BuildUmbrella(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            float h = Mathf.Clamp(g.heightM, 1f, 40f) * r.Range(0.6f, 1.4f);
            float rad = h * r.Range(0.015f, 0.035f) * Mathf.Clamp(g.bodyGirth, 0.6f, 1.6f);

            // ── Stalk: one of several body plans ──
            int stem = r.RangeInt(0, 5);   // 0 straight · 1 swooping curve · 2 bulbous (bottle) · 3 corkscrew · 4 whip
            var pts = new List<Vector3>(); var radii = new List<float>();
            Vector3 bendAxis = (Quaternion.AngleAxis(r.Range(0f, 360f), Vector3.up) * Vector3.right);
            Vector3 p = Vector3.zero, d = Vector3.up; int sn = 14;
            float bulgeAt = r.Range(0.15f, 0.45f), helixR = rad * r.Range(2f, 5f), helixTurns = r.Range(1.5f, 3.5f);
            for (int s = 0; s <= sn; s++)
            {
                float t = s / (float)sn;
                float rr = rad * (1f + 0.8f * Mathf.Exp(-t * 7f));                         // flared foot
                if (stem == 2) rr *= 1f + 2.2f * Mathf.Exp(-((t - bulgeAt) / 0.2f) * ((t - bulgeAt) / 0.2f));
                if (stem == 4) rr *= Mathf.Lerp(1.4f, 0.45f, t);
                else rr *= Mathf.Lerp(1f, 0.8f, t);
                Vector3 pp = p;
                if (stem == 3) pp += (Quaternion.AngleAxis(t * helixTurns * 360f, Vector3.up) * Vector3.right) * helixR * Mathf.Sin(t * Mathf.PI);
                pts.Add(pp); radii.Add(rr);
                if (s < sn)
                {
                    float bend = stem == 1 ? r.Range(2f, 7f) : r.Range(-2f, 3f);
                    p += d * (h / sn); d = (Quaternion.AngleAxis(bend, bendAxis) * d).normalized;
                }
            }
            Vector3 top = pts[sn];
            // collar where the cap meets the stalk (the "annulus"), so the joint is a flare, not a pin
            radii[sn] = Mathf.Max(radii[sn], rad) * 2.4f;
            radii[sn - 1] *= 1.5f;
            AddTube(wood, pts, radii, 12, 0.03f);

            // ── Cap: one of several shapes. The cap SURFACE centre is placed exactly at the stalk top. ──
            int shape = r.RangeInt(0, 5);   // 0 dome · 1 flat plate · 2 tall bell · 3 inverted cup · 4 drooping parasol
            float R = h * r.Range(0.22f, 0.6f) * g.bodyGirth;
            float H, droop;
            switch (shape)
            {
                case 1:  H = R * r.Range(0.03f, 0.08f); droop = 0f; break;
                case 2:  H = R * r.Range(0.5f, 0.85f); droop = r.Range(0.9f, 1.5f); break;
                case 3:  H = -R * r.Range(0.15f, 0.35f); droop = 0f; break;
                case 4:  H = R * r.Range(0.15f, 0.3f); droop = r.Range(1.2f, 2.2f); break;
                default: H = R * r.Range(0.2f, 0.4f); droop = r.Range(0.2f, 0.6f); break;
            }
            Vector3 capOrigin = top - Vector3.up * H;            // CapY(0) == H → surface centre lands on `top`
            AddOrganicCap(foliage, g, ref r, capOrigin, R, H, droop);

            // Tiered (pagoda) variants: smaller caps further down the stalk.
            int tiers = r.Value < 0.25f ? r.RangeInt(1, 3) : 0;
            for (int k = 0; k < tiers; k++)
            {
                float f = 1f - (k + 1) * r.Range(0.18f, 0.28f);
                int idx = Mathf.Clamp(Mathf.RoundToInt(f * sn), 2, sn - 2);
                float tr = R * (0.7f - 0.2f * k), th = Mathf.Abs(H) * 0.5f + tr * 0.1f;
                AddOrganicCap(foliage, g, ref r, pts[idx] - Vector3.up * th, tr, th, droop * 0.7f);
            }

            // Ribs follow the cap UNDERSIDE (only on caps with some curvature); they start inside the collar.
            if (shape != 1 && shape != 3)
            {
                int spokes = 6 + r.RangeInt(0, 8);
                for (int i = 0; i < spokes; i++)
                {
                    Vector3 rd = Quaternion.AngleAxis(360f * i / spokes + r.Range(-6f, 6f), Vector3.up) * Vector3.right;
                    var rp = new List<Vector3>(); var rw = new List<float>();
                    for (int k = 0; k <= 7; k++)
                    {
                        float u = Mathf.Lerp(0.02f, 0.93f, k / 7f);
                        rp.Add(capOrigin + rd * (u * R) + Vector3.up * (CapY(u, H, droop) - Mathf.Abs(H) * 0.06f - rad * 0.3f));
                        rw.Add(Mathf.Lerp(rad * 0.55f, rad * 0.12f, k / 7f));
                    }
                    AddTube(wood, rp, rw, 5, 0.02f);
                }
            }
            if (g.hasFlowers) AddFlower(flower, g, ref r, top + Vector3.up * Mathf.Max(0f, rad), Vector3.up);
        }

        static void BuildCrystal(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            // Druse-like clusters: a lumpy mineral-ish base (organ sphere, squashed) sprouting crystals whose sizes follow
            // a power law (a few big, many small), splayed like a real geode cluster, each an irregular prism.
            int clusters = 1 + Mathf.RoundToInt(g.branchDensity * 3f);
            float H = Mathf.Clamp(g.heightM, 0.3f, 6f);
            for (int cI = 0; cI < clusters; cI++)
            {
                Vector3 basePos = (cI == 0 ? Vector3.zero : new Vector3(r.Range(-1f, 1f), 0f, r.Range(-1f, 1f)).normalized * H * r.Range(0.3f, 0.6f));
                float cs = cI == 0 ? 1f : r.Range(0.4f, 0.75f);
                AddOrganSphere(wood, g, ref r, basePos, H * 0.16f * cs, 0.45f);   // matrix rock the crystals grow from
                int spikes = 5 + Mathf.RoundToInt(g.leafDensity * 8f);
                Vector3 lean = new Vector3(r.Range(-0.35f, 0.35f), 0f, r.Range(-0.35f, 0.35f));
                for (int i = 0; i < spikes; i++)
                {
                    float big = Mathf.Pow(r.Value, 2.2f);                                        // power-law sizes
                    Vector3 dir = Vector3.Normalize(Vector3.up + lean + new Vector3(r.Range(-0.9f, 0.9f), r.Range(-0.1f, 0.4f), r.Range(-0.9f, 0.9f)) * (1.1f - big * 0.6f));
                    Vector3 sp = basePos + new Vector3(r.Range(-1f, 1f), 0f, r.Range(-1f, 1f)) * H * 0.1f * cs;
                    float len = H * cs * Mathf.Lerp(0.18f, 1f, 1f - big) * r.Range(0.7f, 1.1f);
                    AddCrystal(foliage, g, ref r, sp, dir, len, len * r.Range(0.09f, 0.17f) * g.bodyGirth);
                }
            }
            if (g.hasFlowers) AddFlower(flower, g, ref r, Vector3.up * H * 0.5f, Vector3.up);
        }

        // One natural crystal: an irregular 5–7 sided prism (uneven face widths, slight taper, off-axis twist) with a
        // lopsided pyramidal termination whose apex is off-centre. Flat-shaded per face so facets glint individually;
        // each crystal gets a slight tint shift, and colour deepens toward the tip.
        static void AddCrystal(MB mb, PlantGenome g, ref DetRng r, Vector3 basePos, Vector3 dir, float len, float width)
        {
            dir = dir.normalized;
            Vector3 a = PerpUp(dir), b = Vector3.Cross(dir, a);
            int sides = r.RangeInt(5, 8);
            float bodyLen = len * r.Range(0.55f, 0.8f), twist = r.Range(0f, 360f), taper = r.Range(0.75f, 1f);
            Vector3 apex = basePos + dir * len + (a * r.Range(-0.25f, 0.25f) + b * r.Range(-0.25f, 0.25f)) * width;
            var r0 = new Vector3[sides]; var r1 = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float ang = (twist + 360f * k / sides + r.Range(-14f, 14f)) * Mathf.Deg2Rad;
                Vector3 off = a * Mathf.Cos(ang) + b * Mathf.Sin(ang);
                float rw = width * r.Range(0.72f, 1.22f);
                r0[k] = basePos - dir * (width * 0.4f) + off * rw;                                   // buried a little in the matrix
                r1[k] = basePos + dir * (bodyLen * r.Range(0.9f, 1.1f)) + off * rw * taper;           // uneven shoulder
            }
            float v = 0.85f + 0.3f * r.Value;
            mb.tint = new Color(v, v * r.Range(0.95f, 1.05f), v * r.Range(0.95f, 1.08f));
            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides;
                Vector3 n1 = Vector3.Normalize(Vector3.Cross(r1[k] - r0[k], r0[k1] - r0[k]));
                int q = mb.Count;
                mb.Vert(r0[k], n1, 0.1f); mb.Vert(r0[k1], n1, 0.1f); mb.Vert(r1[k1], n1, 0.6f); mb.Vert(r1[k], n1, 0.6f);
                mb.Tri(q, q + 1, q + 2); mb.Tri(q, q + 2, q + 3);
                Vector3 n2 = Vector3.Normalize(Vector3.Cross(apex - r1[k], r1[k1] - r1[k]));
                q = mb.Count;
                mb.Vert(r1[k], n2, 0.6f); mb.Vert(r1[k1], n2, 0.6f); mb.Vert(apex, n2, 1f);
                mb.Tri(q, q + 1, q + 2);
            }
            mb.tint = Color.white;
        }

        // Height of an organic cap/canopy surface at radial fraction u (0 centre → 1 rim): a dome whose rim droops.
        static float CapY(float u, float H, float droop) => H * (1f - u * u) - droop * H * u * u * u;

        // An organic cap/parasol: dense radial grid following CapY, with a scalloped, noise-wobbled rim, gentle surface
        // noise, recomputed normals and the genome's organ PATTERN (spots, bands, marbling…) baked into alpha.
        static void AddOrganicCap(MB mb, PlantGenome g, ref DetRng r, Vector3 top, float R, float H, float droop)
        {
            int lon = 28, lat = 10;
            float scallop = r.Range(0f, 1f), lobes = r.RangeInt(5, 13), seed = r.Range(0f, 40f);
            var grid = new Vector3[lon + 1, lat + 1]; var al = new float[lon + 1, lat + 1];
            for (int i = 0; i <= lat; i++)
            {
                float u = i / (float)lat;
                for (int j = 0; j <= lon; j++)
                {
                    float th = j / (float)lon * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                    float rimW = 1f + u * u * (scallop * 0.09f * Mathf.Sin(th * lobes) + (VN(Mathf.Cos(th) * 3f + seed, Mathf.Sin(th) * 3f, seed) - 0.5f) * 0.12f);
                    Vector3 sp = new Vector3(d.x * u * 2.2f, u, d.z * u * 2.2f) + Vector3.one * seed;
                    float bump = (FBM3(sp * 1.5f, 3) - 0.5f) * H * 0.18f * g.organRelief;
                    grid[j, i] = top + d * (u * R * rimW) + Vector3.up * (CapY(u, H, droop) + bump);
                    al[j, i] = OrganAlpha(g.organColor, sp, j / (float)lon, u);
                }
            }
            int b0 = mb.Count; mb.tint = Color.white;
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j <= lon; j++)
                {
                    Vector3 du = grid[Mathf.Min(j + 1, lon), i] - grid[Mathf.Max(j - 1, 0), i];
                    Vector3 dv = grid[j, Mathf.Min(i + 1, lat)] - grid[j, Mathf.Max(i - 1, 0)];
                    Vector3 n = Vector3.Cross(du, dv);
                    if (n.sqrMagnitude < 1e-9f || i == 0) n = Vector3.up;
                    if (Vector3.Dot(n, Vector3.up) < 0f && i < lat / 2) n = -n;   // keep the top facing up
                    mb.Vert(grid[j, i], n, al[j, i], new Vector2(j / (float)lon, i / (float)lat));
                }
            int st = lon + 1;
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = b0 + i * st + j, b = a + 1, c = a + st, dd = c + 1;
                    mb.Tri(a, c, b); mb.Tri(b, c, dd);
                }
        }

        // ── shared geometry helpers ──────────────────────────────────────────────────────────────

        // A ribbed tapered column (cactus/palm body) → foliage; ribs modulate radius.
        static void BuildColumn(PlantGenome g, ref DetRng r, MB foliage, MB wood, Vector3 p, Vector3 dir, float rad, float len, float ribbing)
        {
            int segs = Mathf.Max(3, Mathf.RoundToInt(len / Mathf.Max(rad, 0.1f)));
            Vector3 cur = p;
            for (int s = 0; s < segs; s++)
            {
                float t0 = s / (float)segs, t1 = (s + 1) / (float)segs;
                Vector3 a = p + dir * len * t0, b = p + dir * len * t1;
                float ra = rad * Mathf.Sin(Mathf.Clamp01(0.15f + t0) * Mathf.PI * 0.55f + 0.3f);   // slight barrel
                float rb = rad * Mathf.Sin(Mathf.Clamp01(0.15f + t1) * Mathf.PI * 0.55f + 0.3f);
                AddSegment(foliage, a, b, Mathf.Max(ra, rad * 0.3f), Mathf.Max(rb, rad * 0.3f), dir, 12, ribbing, r);
                cur = b;
            }
            if (g.spininess > 0.1f) AddSpines(wood, p + dir * len * 0.5f, rad, Mathf.RoundToInt(30 * g.spininess * len), r, dir, len);
        }

        static void AddSegment(MB mb, Vector3 a, Vector3 b, float ra, float rb, Vector3 dir, int sides, float ribAmt, DetRng r)
        {
            Vector3 up = Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, dir));
            Vector3 fwd = Vector3.Cross(dir, right);
            int b0 = mb.Count;
            for (int k = 0; k < sides; k++)
            {
                float ang = 2f * Mathf.PI * k / sides;
                float rib = 1f - ribAmt * 0.35f * (0.5f + 0.5f * Mathf.Cos(ang * sides));   // fluting
                Vector3 off = (right * Mathf.Cos(ang) + fwd * Mathf.Sin(ang));
                mb.Vert(a + off * ra * rib, off, 0f);
                mb.Vert(b + off * rb * rib, off, ribAmt > 0.01f ? 0.15f : 0f);
            }
            for (int k = 0; k < sides; k++)
            {
                int i0 = b0 + k * 2, i1 = b0 + ((k + 1) % sides) * 2;
                mb.Tri(i0, i1, i0 + 1); mb.Tri(i1, i1 + 1, i0 + 1);   // outward-facing winding
            }
        }

        static void ScatterLeaves(PlantGenome g, ref DetRng r, MB foliage, Vector3 pos, Vector3 dir, int count)
        {
            if (g.leaf == LeafShape.None) return;
            count = Mathf.Max(1, Mathf.RoundToInt(count * Mathf.Lerp(0.3f, 1f, Detail)));
            for (int k = 0; k < count; k++)
            {
                // Fan the leaves outward and gently downward (droop) rather than bursting in every direction.
                Vector3 outward = Vector3.Normalize(dir + new Vector3(r.Range(-1f, 1f), r.Range(-0.5f, 0.3f), r.Range(-1f, 1f)) * 0.9f);
                Vector3 ld = Vector3.Normalize(outward + Vector3.down * (0.35f + g.pendant * 1.3f));   // pendant: leaves hang
                float sz = Mathf.Min(g.leafSize, 1.8f) * r.Range(0.28f, 0.42f);   // capped + small relative to the twig
                // Keep the leaf base ON the twig — jitter is a tiny fraction of the LEAF size, not the plant's, so
                // leaves stay attached instead of floating off in a starburst.
                Vector3 lpos = pos + outward * sz * r.Range(0f, 0.12f);
                PlaceLeaf(foliage, g, ref r, lpos, ld, sz, r.Range(0f, 1f));
            }
        }

        // Dispatch to the right foliage-organ builder for this genome's leaf morphology. Deliberately covers weird,
        // non-Earth forms: flat discs, fans, funnels, bladder-orbs, dangling filaments, and stem-hugging algal sheaths.
        static void PlaceLeaf(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float size, float rand)
        {
            if (g.leaf == LeafShape.None) return;
            // Reject a degenerate placement outright so no organ can spawn NaN verts (which collapse to the base).
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) return;
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            mb.tint = LeafTint(g, rand);   // per-leaf coloration → vertex RGB. Deterministic from `rand` so it does NOT
                                           // consume the shared RNG (otherwise changing colour-variation reshuffled the whole plant).
            switch (g.leaf)
            {
                case LeafShape.None: return;
                case LeafShape.Frond:    AddFrond(mb, g, ref r, pos, dir, size * 1.2f, true); break;
                case LeafShape.Blade:    AddBlade(mb, g, pos, dir, 0.06f * size, size * 0.95f, 0.5f); break;
                case LeafShape.Strap:    AddBlade(mb, g, pos, dir, 0.13f * size, size * 1.3f, 0.4f); break;       // seagrass ribbon (shorter)
                case LeafShape.Filament: AddBlade(mb, g, pos, Vector3.Normalize(dir + Vector3.down), 0.025f * size, size * 1.5f, 0.85f); break;  // dangling string (shorter)
                case LeafShape.Disc:     AddDisc(mb, g, pos, dir, size, rand, false); break;
                case LeafShape.Pad:      AddDisc(mb, g, pos, dir, size * 1.1f, rand, true); break;                // thick fleshy pad
                case LeafShape.Fan:      AddFan(mb, g, pos, dir, size, rand); break;
                case LeafShape.Cup:      AddFunnel(mb, g, pos, Vector3.Normalize(dir + Vector3.up * 0.6f), size, rand); break;
                case LeafShape.Trumpet:  AddFunnel(mb, g, pos, dir, size * 1.15f, rand); break;
                case LeafShape.Orb:      AddOrbs(mb, g, ref r, pos, dir, size); break;
                case LeafShape.Sheath:   AddSheath(mb, g, ref r, pos, dir, size); break;
                case LeafShape.Needle:   AddNeedleTuft(mb, g, ref r, pos, dir, size, rand); break;   // short needles in a small cluster
                case LeafShape.Kelp:     AddKelp(mb, g, pos, dir, size, rand); break;               // long wavy blade
                case LeafShape.Antler:   AddAntler(mb, g, pos, dir, size); break;                   // flat forked staghorn
                case LeafShape.Membrane: AddMembrane(mb, g, pos, dir, Mathf.Max(g.leafSize, 1f) * 1.6f, rand); break;   // big sail (uncapped size)
                default:                 AddLeaf(mb, g, pos, dir, size * 0.9f, rand); break;   // Ovate/Lanceolate/Palmate/Cordate/Needle/Scale/Bilobe/Reniform
            }
            mb.tint = Color.white;   // reset so non-leaf geometry sharing this buffer isn't tinted
        }

        // Fancy per-leaf coloration: each leaf gets its own multiplicative tint of the base albedo, derived
        // deterministically from `seed` (0..1) so it never disturbs the growth RNG. `leafColorVar` widens the spread;
        // a fraction of leaves flush strongly toward the accent colour (variegation / autumn-fleck look).
        static Color LeafTint(PlantGenome g, float seed)
        {
            float var = Mathf.Clamp01(g.leafColorVar);
            if (var < 0.001f) return Color.white;
            int h0 = (int)(seed * 104729f) * 374761393 + 668265263;
            float a = Hash01(h0), b = Hash01(h0 + 17), c = Hash01(h0 + 91), k = Hash01(h0 + 133);
            float val = 1f + (a - 0.5f) * 0.55f * var;      // ±~0.28 brightness
            float hue = (b - 0.5f) * 0.14f * var;           // warm/cool tilt
            Color t = new Color(val * (1f + hue), val, val * (1f - hue * 0.7f), 1f);
            if (c < var * 0.22f)                            // strong accent flush on some leaves
            {
                Color.RGBToHSV(g.accent, out float ah, out float asat, out float _);
                Color acc = Color.HSVToRGB(ah, Mathf.Clamp01(asat * 1.1f), 1f);
                float m = 0.5f + 0.4f * k;
                t = new Color(Mathf.Lerp(t.r, acc.r * 1.3f, m), Mathf.Lerp(t.g, acc.g * 1.3f, m), Mathf.Lerp(t.b, acc.b * 1.3f, m), 1f);
            }
            return t;
        }

        // A shaped, veined leaf (left/centre/right columns along a midrib) with a paint-job accent alpha.
        static void AddLeaf(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float size, float perLeafRand)
        {
            dir = dir.normalized;
            Vector3 side = PerpUp(dir);
            Vector3 nrm = Vector3.Normalize(Vector3.Cross(side, dir));
            Vector3 nL = Vector3.Normalize(nrm * 0.9f - side * 0.35f);   // fold normals (V cross-section)
            Vector3 nR = Vector3.Normalize(nrm * 0.9f + side * 0.35f);
            Vector3 droop = Vector3.ProjectOnPlane(Vector3.down, dir);
            droop = droop.sqrMagnitude > 1e-5f ? droop.normalized : Vector3.zero;   // leaves hang under gravity
            int segN = 13;   // more segments → smooth, organic outline
            float jit = g.leafJitter * size * 0.06f;   // per-vertex imperfection
            int b0 = mb.Count;
            for (int s = 0; s <= segN; s++)
            {
                float t = s / (float)segN;
                float w = LeafWidth(g.leaf, t) * size * 0.62f * (1f + g.leafRuffle * 0.25f * Mathf.Sin(t * 9f));   // wider (less spiky)
                // arch the leaf along its length, raise the midrib (folded blade), and let the tip droop under gravity
                Vector3 c = pos + dir * (t * size) + nrm * (0.18f * size * Mathf.Sin(t * Mathf.PI)) + droop * (0.22f * size * t * t);
                float aL = AccentA(g.paint, t, 1f, perLeafRand);
                float aC = AccentA(g.paint, t, 0f, perLeafRand);
                // Cross-section is a shallow keel: raised midrib, sides cupped slightly down → catches light with depth.
                float cup = 0.28f * w;
                Vector3 pL = c - side * w - nrm * cup, pC = c + nrm * (w * 0.6f), pR = c + side * w - nrm * cup;
                // uv.xy = (across 0..1, along 0..1) so the shader can draw a midrib + lateral veins on the blade.
                mb.Vert(pL + NoiseOffset(pL, perLeafRand * 97f, jit), nL, aL, new Vector2(0f, t));
                mb.Vert(pC + NoiseOffset(pC, perLeafRand * 97f, jit * 0.5f), nrm, aC, new Vector2(0.5f, t));
                mb.Vert(pR + NoiseOffset(pR, perLeafRand * 97f, jit), nR, aL, new Vector2(1f, t));
            }
            for (int s = 0; s < segN; s++)
            {
                int r0 = b0 + s * 3, r1 = b0 + (s + 1) * 3;
                mb.Tri(r0, r1, r0 + 1); mb.Tri(r0 + 1, r1, r1 + 1);       // left half
                mb.Tri(r0 + 1, r1 + 1, r0 + 2); mb.Tri(r0 + 2, r1 + 1, r1 + 2); // right half
            }
        }

        // A fat 3D succulent leaf (a pointed thick spindle).
        static void AddThickLeaf(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float len, float thick)
        {
            dir = dir.normalized;
            Vector3 s0 = PerpUp(dir), s1 = Vector3.Cross(dir, s0);
            int rings = 3, sides = 5, b0 = mb.Count;
            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings;
                float w = thick * Mathf.Sin(Mathf.Clamp01(t + 0.05f) * Mathf.PI * 0.9f);
                Vector3 c = pos + dir * (t * len);
                for (int k = 0; k < sides; k++)
                {
                    float ang = 2f * Mathf.PI * k / sides;
                    Vector3 off = (s0 * Mathf.Cos(ang) + s1 * Mathf.Sin(ang));
                    mb.Vert(c + off * w, off, AccentA(g.paint, t, 1f, 0.5f));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int k = 0; k < sides; k++)
                {
                    int i0 = b0 + i * sides + k, i1 = b0 + i * sides + (k + 1) % sides;
                    int j0 = i0 + sides, j1 = i1 + sides;
                    mb.Tri(i0, j0, i1); mb.Tri(i1, j0, j1);
                }
        }

        static void AddBlade(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float width, float length, float curve)
        {
            dir = dir.normalized;
            Vector3 side = PerpUp(dir);
            Vector3 bend = Vector3.Cross(side, Vector3.up).normalized;
            int segN = 5, b0 = mb.Count;
            for (int s = 0; s <= segN; s++)
            {
                float t = s / (float)segN;
                Vector3 c = pos + dir * (t * length) + bend * (curve * length * t * t);
                float w = width * (1f - t * 0.8f);
                Vector3 nrm = Vector3.Cross(side, dir);
                mb.Vert(c - side * w, nrm, AccentA(g.paint, t, 1f, 0.4f));
                mb.Vert(c + side * w, nrm, AccentA(g.paint, t, 1f, 0.4f));
            }
            for (int s = 0; s < segN; s++)
            {
                int r0 = b0 + s * 2, r1 = b0 + (s + 1) * 2;
                mb.Tri(r0, r1, r0 + 1); mb.Tri(r0 + 1, r1, r1 + 1);
            }
        }

        // A flat round leaf on a short petiole (lily-pad / pennywort). `thick` bulges it into a fleshy pad.
        static void AddDisc(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float size, float rand, bool thick)
        {
            dir = dir.normalized;
            Vector3 s0 = PerpUp(dir), s1 = Vector3.Cross(dir, s0);
            Vector3 c = pos + dir * size * 0.45f;                 // blade sits at the end of a petiole
            AddBlade(mb, g, pos, dir, size * 0.035f, size * 0.47f, 0.04f);   // the petiole itself (no floating gap)
            float rad = size * 0.5f;
            int seg = 22, b0 = mb.Count;
            mb.Vert(c + dir * (thick ? rad * 0.25f : 0f), dir, AccentA(g.paint, 0f, 0f, rand));   // centre
            for (int i = 0; i <= seg; i++)
            {
                float a = 2f * Mathf.PI * i / seg;
                float rr = rad * (1f + g.leafRuffle * 0.18f * Mathf.Sin(a * 6f) + (Hash01(i * 7 + (int)(rand * 999f)) - 0.5f) * g.leafJitter * 0.28f);
                Vector3 off = s0 * Mathf.Cos(a) + s1 * Mathf.Sin(a);
                Vector3 p = c + off * rr + dir * (g.leafJitter * rad * 0.14f * Mathf.Sin(a * 3f + rand * 6f));   // warp the plane out of flat
                p += NoiseOffset(p, rand * 61f, g.leafJitter * rad * 0.05f);
                mb.Vert(p, dir, AccentA(g.paint, 1f, Mathf.Abs(Mathf.Cos(a)), rand));
            }
            for (int i = 0; i < seg; i++) mb.Tri(b0, b0 + 1 + i, b0 + 2 + i);   // two-sided material → winding is unimportant
        }

        // A ginkgo/kelp fan: a wedge that flares from the stalk to a wide, ruffled far edge.
        static void AddFan(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float size, float rand)
        {
            dir = dir.normalized;
            Vector3 side = PerpUp(dir);
            int seg = 16, b0 = mb.Count;
            float span = 1.1f;   // radians of the fan wedge
            mb.Vert(pos, dir, AccentA(g.paint, 0f, 1f, rand));   // stalk apex
            for (int i = 0; i <= seg; i++)
            {
                float f = (i / (float)seg - 0.5f) * span;
                float edge = size * (1f + g.leafRuffle * 0.22f * Mathf.Sin(i * 2.3f));
                Vector3 outr = Vector3.Normalize(dir * Mathf.Cos(f) + side * Mathf.Sin(f));
                mb.Vert(pos + outr * edge, dir, AccentA(g.paint, 1f, Mathf.Abs(f) / (span * 0.5f), rand));
            }
            for (int i = 0; i < seg; i++) mb.Tri(b0, b0 + 1 + i, b0 + 2 + i);
        }

        // An open funnel that catches light (cup faces up, trumpet flares outward). A cone of `rings` open at the rim.
        static void AddFunnel(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float size, float rand)
        {
            dir = dir.normalized;
            Vector3 s0 = PerpUp(dir), s1 = Vector3.Cross(dir, s0);
            int rings = 4, sides = 16, b0 = mb.Count;
            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings;
                float rad = Mathf.Lerp(size * 0.06f, size * 0.5f, t * t);   // narrow base → wide rim
                Vector3 c = pos + dir * (t * size * 0.9f);
                for (int k = 0; k <= sides; k++)
                {
                    float a = 2f * Mathf.PI * k / sides;
                    float rr = rad * (1f + g.leafRuffle * 0.15f * Mathf.Sin(a * 5f) * t);
                    Vector3 off = s0 * Mathf.Cos(a) + s1 * Mathf.Sin(a);
                    Vector3 nrm = Vector3.Normalize(off + dir * 0.5f);
                    Vector3 p = c + off * rr;
                    p += NoiseOffset(p, rand * 43f, g.leafJitter * size * 0.04f * t);
                    mb.Vert(p, nrm, AccentA(g.paint, t, Mathf.Abs(Mathf.Cos(a)), rand));
                }
            }
            int stride = sides + 1;
            for (int i = 0; i < rings; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = b0 + i * stride + k, b = a + 1, cc = a + stride, dd = b + stride;
                    mb.Tri(a, cc, b); mb.Tri(b, cc, dd);
                }
        }

        // Clusters of small photosynthetic bladder-spheres on short stalks (grape-like / air-plant orbs).
        static void AddOrbs(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float size)
        {
            dir = dir.normalized;
            Vector3 sideA = Perp(dir), sideB = Vector3.Cross(dir, sideA);
            int n = r.RangeInt(3, 6);
            // A chain of bladders, each overlapping the one before; the first is seated on the twig itself, so the
            // cluster is physically connected (the old version scattered them in the air around the node).
            Vector3 prevC = pos; float prevR = 0f;
            for (int i = 0; i < n; i++)
            {
                float rr = size * r.Range(0.13f, 0.26f);
                Vector3 jd = Vector3.Normalize(dir + sideA * r.Range(-0.7f, 0.7f) + sideB * r.Range(-0.7f, 0.7f) + Vector3.down * r.Range(0f, 0.5f));
                Vector3 c = prevC + jd * ((prevR + rr) * 0.72f);
                AddOrganSphere(mb, g, ref r, c, rr, r.Range(0.68f, 1.32f));
                prevC = c; prevR = rr;
            }
        }

        // An algal sheath: many tiny overlapping scales/discs that coat the stem near a node (a shaggy green fur).
        static void AddSheath(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float size)
        {
            int n = 6 + Mathf.RoundToInt(g.leafDensity * 4f);
            for (int i = 0; i < n; i++)
            {
                Vector3 outr = (Quaternion.AngleAxis(360f * i / n + r.Range(-15f, 15f), dir) * Perp(dir)).normalized;
                Vector3 scaleDir = Vector3.Normalize(outr + dir * r.Range(-0.3f, 0.3f));
                AddDisc(mb, g, pos + outr * size * 0.05f, scaleDir, size * r.Range(0.28f, 0.42f), r.Range(0f, 1f), true);
            }
        }

        // A big volumetric photosynthetic MEMBRANE — a subdivided, curved, distorted sail rooted at `pos`. Several
        // morphologies (broad sail / kidney / lobed / elongate), rippled and warped so no two are alike. High vertex
        // count + computed normals so it reads as a soft organic sheet, not a card. Uses the leaf shader's veins/
        // mottle/per-leaf colour, so it carries interesting patterns.
        static void AddMembrane(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float size, float seed)
        {
            dir = dir.normalized;
            Vector3 side = PerpUp(dir);
            Vector3 up = Vector3.Cross(side, dir);   // sheet's face normal reference
            int nu = 18, nv = 14;                    // dense grid
            int morph = (int)(Hash01((int)(seed * 7919f) + 3) * 4f);
            float ruf = 0.15f + g.leafRuffle * 0.45f;             // calmer edge — billow, don't shred
            float jit = size * (0.004f + g.leafJitter * 0.014f);  // very gentle imperfection
            var grid = new Vector3[nu + 1, nv + 1];
            for (int j = 0; j <= nv; j++)
            {
                float v = j / (float)nv;             // base→tip
                // Width envelope = morphology. A short base "petiole" then a BROAD sheet held wide most of the length
                // (so it reads as a membrane/sail, not a pointy leaf). `hold` keeps it full through the middle.
                float hold = SS(0f, 0.18f, v) * SS(1f, 0.7f, v);
                float env;
                switch (morph)
                {
                    // (multipliers re-tuned now that `hold` really spans 0..1 — it was stuck near ~0.13 by a SmoothStep bug)
                    case 1:  env = hold * 0.62f; break;                                                    // kidney: very broad rounded sheet
                    case 2:  env = hold * (0.42f + 0.08f * Mathf.Cos(v * Mathf.PI * 3f)); break;           // lobed frill sheet
                    case 3:  env = SS(0f, 0.25f, v) * SS(1f, 0.5f, v) * 0.3f; break;                      // elongate sail
                    default: env = hold * 0.5f; break;                                                     // broad sail
                }
                for (int iu = 0; iu <= nu; iu++)
                {
                    float u = iu / (float)nu;
                    float x = u - 0.5f;
                    float w = env * size * (1f + 0.06f * ruf * Mathf.Sin(v * 4f + x * 3f));   // margin barely wavers → smooth outline
                    // broad, gentle billow across the sheet → volume without shredding
                    float lift = 0.15f * size * Mathf.Sin(v * Mathf.PI)
                               + 0.08f * size * ruf * Mathf.Sin(x * 2.5f + v * 2.5f);
                    Vector3 p = pos + dir * (v * size * 1.4f) + side * (x * 2f * w) + up * lift;
                    p += NoiseOffset(p, seed, jit);
                    grid[iu, j] = p;
                }
            }
            int b0 = mb.Count;
            for (int j = 0; j <= nv; j++)
                for (int iu = 0; iu <= nu; iu++)
                {
                    // per-vertex normal from grid neighbours → smooth curved shading
                    Vector3 du = grid[Mathf.Min(iu + 1, nu), j] - grid[Mathf.Max(iu - 1, 0), j];
                    Vector3 dv = grid[iu, Mathf.Min(j + 1, nv)] - grid[iu, Mathf.Max(j - 1, 0)];
                    Vector3 nrm = Vector3.Cross(dv, du);
                    if (nrm.sqrMagnitude < 1e-8f) nrm = up;
                    float u = iu / (float)nu, vv = j / (float)nv;
                    mb.Vert(grid[iu, j], nrm, AccentA(g.paint, vv, Mathf.Abs(u - 0.5f) * 2f, seed), new Vector2(u, vv));
                }
            int stride = nu + 1;
            for (int j = 0; j < nv; j++)
                for (int iu = 0; iu < nu; iu++)
                {
                    int a = b0 + j * stride + iu, b = a + 1, cc = a + stride, dd = cc + 1;
                    mb.Tri(a, cc, b); mb.Tri(b, cc, dd);
                }
        }

        // A conifer fascicle: a small cluster of short, thin needles fanning from the twig (all rooted at `pos`).
        static void AddNeedleTuft(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float size, float rand)
        {
            dir = dir.normalized;
            Vector3 side = Perp(dir);
            int n = r.RangeInt(3, 7);
            float len = size * 0.6f;
            for (int i = 0; i < n; i++)
            {
                Vector3 nd = Vector3.Normalize(dir + (Quaternion.AngleAxis(360f * i / n + r.Range(-20f, 20f), dir) * side) * r.Range(0.12f, 0.5f));
                AddBlade(mb, g, pos, nd, 0.022f * size, len * r.Range(0.8f, 1.1f), 0.12f);
            }
        }

        // A long wavy broad blade (kelp / seaweed): rooted at pos, ripples side-to-side and droops toward the tip.
        static void AddKelp(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float size, float rand)
        {
            dir = dir.normalized;
            Vector3 side = PerpUp(dir);
            Vector3 nrm = Vector3.Cross(side, dir);
            int segN = 16; float len = size * 2.2f, wid = size * 0.55f;
            int b0 = mb.Count;
            for (int s = 0; s <= segN; s++)
            {
                float t = s / (float)segN;
                float w = wid * (0.35f + 0.65f * Mathf.Sin(t * Mathf.PI)) * (1f + 0.2f * g.leafRuffle * Mathf.Sin(t * 9f));
                Vector3 wave = side * (0.14f * len * Mathf.Sin(t * 5.5f + rand * 6f));   // undulating ribbon
                Vector3 c = pos + dir * (t * len) + wave + Vector3.down * (0.12f * len * t * t);
                mb.Vert(c - side * w, nrm, AccentA(g.paint, t, 1f, rand), new Vector2(0f, t));
                mb.Vert(c + side * w, nrm, AccentA(g.paint, t, 1f, rand), new Vector2(1f, t));
            }
            for (int s = 0; s < segN; s++)
            {
                int r0 = b0 + s * 2, r1 = b0 + (s + 1) * 2;
                mb.Tri(r0, r1, r0 + 1); mb.Tri(r0 + 1, r1, r1 + 1);
            }
        }

        // A flat forked staghorn blade (recursively forks into a few flat lobes).
        static void AddAntler(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float size)
        {
            dir = dir.normalized;
            Vector3 side = PerpUp(dir);
            AddBlade(mb, g, pos, dir, 0.06f * size, size * 0.55f, 0.1f);
            Vector3 mid = pos + dir * (size * 0.5f);
            Vector3 l = Vector3.Normalize(dir + side * 0.85f), rt = Vector3.Normalize(dir - side * 0.85f);
            AddBlade(mb, g, mid, l, 0.05f * size, size * 0.5f, 0.15f);
            AddBlade(mb, g, mid, rt, 0.05f * size, size * 0.5f, 0.15f);
            // second-order forks at each lobe tip
            Vector3 lt = mid + l * (size * 0.45f), rtt = mid + rt * (size * 0.45f);
            AddBlade(mb, g, lt, Vector3.Normalize(l + side * 0.7f), 0.035f * size, size * 0.34f, 0.2f);
            AddBlade(mb, g, lt, Vector3.Normalize(l - side * 0.4f), 0.035f * size, size * 0.34f, 0.2f);
            AddBlade(mb, g, rtt, Vector3.Normalize(rt - side * 0.7f), 0.035f * size, size * 0.34f, 0.2f);
            AddBlade(mb, g, rtt, Vector3.Normalize(rt + side * 0.4f), 0.035f * size, size * 0.34f, 0.2f);
        }

        // A carnivore pitcher: an OPEN-mouthed cup with a dark throat you can see into, a rolled accent PERISTOME rim,
        // organ relief + two-tone pattern on the body, and a varied fringe of teeth. Every one is unique.
        static void AddPitcher(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float size, bool fringe = true)
        {
            dir = dir.normalized;
            Vector3 s0 = PerpUp(dir), s1 = Vector3.Cross(dir, s0);
            int rings = 11, sides = 18;
            float H = size * r.Range(1.3f, 2.1f);
            float bellyPos = r.Range(0.34f, 0.6f), bellyAmt = r.Range(0.15f, 0.3f), bellyWid = r.Range(0.22f, 0.34f);
            float neck = r.Range(0.55f, 0.9f), baseR = size * r.Range(0.05f, 0.08f);
            Vector3 bendAxis = (Quaternion.AngleAxis(r.Range(0f, 360f), dir) * s0).normalized;
            float curveAmt = r.Range(-0.32f, 0.32f) * size, ovality = r.Range(0f, 0.2f);
            float relief = size * 0.045f * Mathf.Clamp01(g.organRelief);   // gentle ripples only — a hollow cup should not crumple
            float jit = size * (0.006f + g.leafJitter * 0.02f), noiseSeed = r.Range(0f, 40f);

            float Rad(float t)
            {
                float bulge = Mathf.Exp(-((t - bellyPos) / bellyWid) * ((t - bellyPos) / bellyWid));
                return (baseR + size * bellyAmt * bulge) * Mathf.Lerp(1f, neck, SS(bellyPos, 1f, t));
            }
            Vector3 Ctr(float t) => pos + dir * (t * H) + bendAxis * (curveAmt * t * t);
            Vector3 Off(float a) => s0 * (Mathf.Cos(a) * (1f + ovality)) + s1 * (Mathf.Sin(a) * (1f - ovality));

            // ── outer body (relief + pattern, recomputed normals) ──
            mb.tint = Color.white;
            var grid = new Vector3[sides + 1, rings + 1];
            var alpha = new float[sides + 1, rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings; Vector3 c = Ctr(t); float rad = Rad(t);
                for (int k = 0; k <= sides; k++)
                {
                    float a = 2f * Mathf.PI * k / sides; Vector3 off = Off(a);
                    Vector3 sp = off * 2.4f + dir * (t * 3f) + Vector3.one * noiseSeed;
                    float disp = OrganDisp(g.organShape, sp, k / (float)sides, t, noiseSeed) * relief * SS(0f, 0.3f, t);
                    Vector3 p = c + off * rad + off * disp;
                    p += NoiseOffset(p, noiseSeed, jit * (0.4f + t));
                    grid[k, i] = p; alpha[k, i] = OrganAlpha(g.organColor, sp, k / (float)sides, t);
                }
            }
            int b0 = mb.Count;
            for (int i = 0; i <= rings; i++)
                for (int k = 0; k <= sides; k++)
                {
                    Vector3 du = grid[Mathf.Min(k + 1, sides), i] - grid[Mathf.Max(k - 1, 0), i];
                    Vector3 dv = grid[k, Mathf.Min(i + 1, rings)] - grid[k, Mathf.Max(i - 1, 0)];
                    Vector3 nrm = Vector3.Cross(du, dv); if (nrm.sqrMagnitude < 1e-9f) nrm = (grid[k, i] - Ctr(i / (float)rings));
                    mb.Vert(grid[k, i], nrm, alpha[k, i], new Vector2(k / (float)sides, i / (float)rings));
                }
            int stride = sides + 1;
            for (int i = 0; i < rings; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = b0 + i * stride + k, b = a + 1, cc = a + stride, dd = b + stride;
                    mb.Tri(a, cc, b); mb.Tri(b, cc, dd);
                }
            int cBot = mb.Count; mb.Vert(pos, -dir, 0f);
            for (int k = 0; k < sides; k++) mb.Tri(cBot, b0 + k, b0 + k + 1);

            Vector3 mouth = Ctr(1f); float rimR = Rad(1f);

            // ── peristome: a rolled lip, ALL accent-coloured (alpha=1), flaring out then rolling down into the throat ──
            int lipOuter = mb.Count;
            for (int k = 0; k <= sides; k++) { Vector3 off = Off(2f * Mathf.PI * k / sides); mb.Vert(mouth + off * (rimR * 1.3f) + dir * (size * 0.05f), Vector3.Normalize(off + dir), 1f, new Vector2(k / (float)sides, 1f)); }
            int lipInner = mb.Count;
            for (int k = 0; k <= sides; k++) { Vector3 off = Off(2f * Mathf.PI * k / sides); mb.Vert(mouth + off * (rimR * 0.82f) - dir * (size * 0.04f), Vector3.Normalize(dir - off), 1f, new Vector2(k / (float)sides, 1f)); }
            for (int k = 0; k < sides; k++)
            {
                int a = lipOuter + k, b = a + 1, c = lipInner + k, d = c + 1;
                mb.Tri(a, c, b); mb.Tri(b, c, d);          // top of the lip
                mb.Tri(a, b, c); mb.Tri(b, d, c);          // underside (two-sided rim so it reads from any angle)
            }

            // ── dark throat descending inside from the inner lip (the visible open mouth/trap) ──
            mb.tint = new Color(0.10f, 0.09f, 0.10f);
            int rings2 = 4, throat0 = mb.Count;
            for (int i = 0; i <= rings2; i++)
            {
                float tt = i / (float)rings2;
                float trad = Mathf.Lerp(rimR * 0.82f, rimR * 0.26f, tt);
                Vector3 tc = mouth - dir * (tt * H * 0.5f) + bendAxis * (curveAmt * 0.1f * tt);
                for (int k = 0; k <= sides; k++) { Vector3 off = Off(2f * Mathf.PI * k / sides); mb.Vert(tc + off * trad, -off, 1f, new Vector2(k / (float)sides, tt)); }
            }
            for (int i = 0; i < rings2; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = throat0 + i * stride + k, b = a + 1, cc = a + stride, dd = b + stride;
                    mb.Tri(a, b, cc); mb.Tri(b, dd, cc);   // inward-facing (we look down into it)
                }
            int tBot = mb.Count; mb.Vert(mouth - dir * (H * 0.5f), dir, 1f);
            for (int k = 0; k < sides; k++) mb.Tri(tBot, throat0 + rings2 * stride + k + 1, throat0 + rings2 * stride + k);
            mb.tint = Color.white;

            // ── fringe: varied teeth/cilia curving inward from the outer lip (accent-tinted, uneven) ──
            int fr = fringe ? r.RangeInt(14, 24) : 0;
            for (int k = 0; k < fr; k++)
            {
                float a = 2f * Mathf.PI * k / fr + r.Range(-0.1f, 0.1f);
                Vector3 off = Off(a);
                Vector3 basePt = mouth + off * (rimR * 1.28f) + dir * (size * 0.05f);
                float len = size * r.Range(0.16f, 0.44f);
                Vector3 curl = s0 * r.Range(-0.3f, 0.3f) + s1 * r.Range(-0.3f, 0.3f);
                Vector3 tip = basePt + Vector3.Normalize(off * r.Range(0.15f, 0.5f) + dir * r.Range(0.6f, 1.2f) + curl) * len;
                Vector3 t2 = Vector3.Cross(dir, off).normalized * (size * r.Range(0.012f, 0.02f));
                int q = mb.Count;
                mb.Vert(basePt - t2, dir, 0.7f); mb.Vert(basePt + t2, dir, 0.7f); mb.Vert(tip, dir, 0.4f);
                mb.Tri(q, q + 1, q + 2);
            }
        }

        // A fleshy ORGAN: an ellipsoid whose surface is displaced by the genome's OrganShape (smooth → brain folds →
        // lobes → warts → ridges → veins) with recomputed normals for real relief, and a two-tone OrganColor pattern
        // baked into vertex alpha (blends the material's base ↔ accent). Reused by orbs, berries, bulbs, orb-clusters.
        static void AddOrganSphere(MB mb, PlantGenome g, ref DetRng r, Vector3 center, float rad, float squash, int lon = 22, int lat = 16)
        {
            lon = Res(lon, 6); lat = Res(lat, 4);
            float relief = rad * 0.32f * Mathf.Clamp01(g.organRelief);
            float seed = r.Range(0f, 40f);
            mb.tint = Color.white;
            var grid = new Vector3[lon + 1, lat + 1];
            var alpha = new float[lon + 1, lat + 1];
            // A living body is never a ball: a few soft LOBES, a lopsided bulge, gravity SAG that widens the lower half,
            // a flattened SEAT where it rests, and fine skin relief — applied to every organ before its genome relief.
            Vector3 skew = new Vector3(Mathf.Sin(seed * 1.7f), Mathf.Sin(seed * 2.3f) * 0.4f, Mathf.Cos(seed * 1.1f)).normalized;
            float lobeAmt = r.Range(0.18f, 0.34f), skewAmt = r.Range(0.06f, 0.16f), sag = r.Range(0.08f, 0.2f);
            for (int i = 0; i <= lat; i++)
            {
                float vv = i / (float)lat, phi = vv * Mathf.PI;
                for (int j = 0; j <= lon; j++)
                {
                    float uu = j / (float)lon, th = uu * 2f * Mathf.PI;
                    Vector3 n0 = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    float lobe = (FBM3(n0 * 1.15f + Vector3.one * (seed + 7f), 2) - 0.5f) * 2f * lobeAmt;
                    float bulge = Vector3.Dot(n0, skew) * skewAmt;
                    float skin = (FBM3(n0 * 9f + Vector3.one * (seed + 3f), 2) - 0.5f) * 0.05f;
                    float k = 1f + lobe + bulge + skin;
                    float below = Mathf.Max(0f, -n0.y);
                    float widen = 1f + sag * below;                                   // heavy tissue slumps outward
                    Vector3 basePos = center + new Vector3(n0.x * rad * k * widen, n0.y * rad * squash * k, n0.z * rad * k * widen);
                    float seat = center.y - rad * squash * 0.72f;                      // flattened contact patch
                    if (basePos.y < seat) basePos.y = seat - (seat - basePos.y) * 0.25f;
                    Vector3 sp = n0 * 2.4f + Vector3.one * seed;
                    grid[j, i] = basePos + n0 * (OrganDisp(g.organShape, sp, uu, vv, seed) * relief);
                    alpha[j, i] = OrganAlpha(g.organColor, sp, uu, vv);
                }
            }
            int b0 = mb.Count;
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j <= lon; j++)
                {
                    Vector3 du = grid[Mathf.Min(j + 1, lon), i] - grid[Mathf.Max(j - 1, 0), i];
                    Vector3 dv = grid[j, Mathf.Min(i + 1, lat)] - grid[j, Mathf.Max(i - 1, 0)];
                    Vector3 nrm = Vector3.Cross(dv, du);
                    if (nrm.sqrMagnitude < 1e-9f) nrm = (grid[j, i] - center);
                    mb.Vert(grid[j, i], nrm, alpha[j, i], new Vector2(j / (float)lon, i / (float)lat));
                }
            int stride = lon + 1;
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = b0 + i * stride + j, b = a + 1, cc = a + stride, dd = cc + 1;
                    mb.Tri(a, b, cc); mb.Tri(b, dd, cc);
                }
        }

        static float OrganDisp(OrganShape s, Vector3 sp, float u, float v, float seed)
        {
            switch (s)
            {
                case OrganShape.Brain:                                   // domain-warped ridged folds (gyri/sulci)
                    Vector3 warp = new Vector3(FBM3(sp + new Vector3(11f, 0f, 0f)), FBM3(sp + new Vector3(0f, 17f, 0f)), FBM3(sp + new Vector3(0f, 0f, 23f)));
                    float n = FBM3(sp * 1.6f + warp * 1.8f, 4);
                    return (0.5f - Mathf.Abs(n - 0.5f)) * 2f - 0.4f;     // sharp ridges
                case OrganShape.Lobed:                                   // a few big soft lobes
                    return (FBM3(sp * 0.8f, 2) - 0.5f) * 1.6f;
                case OrganShape.Warty:                                   // scattered bumps
                    return SS(0.55f, 0.85f, FBM3(sp * 3.2f, 3)) * 1.2f - 0.2f;
                case OrganShape.Ridged:                                  // meridian ridges
                    return Mathf.Cos(u * 6.2831853f * 7f + (FBM3(sp) - 0.5f) * 3f) * 0.5f;
                case OrganShape.Veined:                                  // raised thin network
                    float nv = FBM3(sp * 2.2f, 4);
                    return (1f - SS(0.0f, 0.06f, Mathf.Abs(nv - 0.5f))) * 0.9f;
                default: return 0f;                                       // Smooth
            }
        }

        static float OrganAlpha(OrganColor c, Vector3 sp, float u, float v)
        {
            switch (c)
            {
                case OrganColor.Varied:   return SS(0.3f, 0.7f, FBM3(sp * 1.8f, 3));
                case OrganColor.Banded:   return 0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 7f + (FBM3(sp) - 0.5f) * 2f);
                case OrganColor.Blotched: return SS(0.52f, 0.66f, FBM3(sp * 2.2f, 3));
                case OrganColor.Marbled:  Vector3 w = new Vector3(FBM3(sp + Vector3.one * 5f), FBM3(sp + Vector3.one * 9f), FBM3(sp + Vector3.one * 13f));
                                          return SS(0.4f, 0.6f, FBM3(sp * 1.4f + w * 2.5f, 4));
                case OrganColor.Speckled: return FBM3(sp * 7f, 2) > 0.72f ? 1f : 0f;
                default: return 0f;       // Solid
            }
        }

        // ── TERMINAL ORGANS: a big payload at a branch tip, independent of leaf shape. ──────────────────────────────
        static void AddTerminalOrgan(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower, Vector3 pos, Vector3 dir)
        {
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            float sz = 0.35f * g.terminalSize * Mathf.Clamp(g.heightM * 0.16f, 0.35f, 2.2f);   // scales with the plant

            // PENDANT: hang the organ from a drooping peduncle so it dangles below the twig.
            Vector3 opos = pos, odir = dir;
            if (g.pendant > 0.05f)
            {
                float L = (0.4f + g.pendant * 1.6f) * Mathf.Clamp(g.heightM * 0.16f, 0.35f, 2.5f);
                int N = 6; var pts = new List<Vector3>(); var radii = new List<float>();
                Vector3 pcur = pos, pd = Vector3.Normalize(dir + Vector3.down * (0.6f + g.pendant));
                float pr = Mathf.Max(sz * 0.12f, 0.02f);
                for (int s = 0; s <= N; s++)
                {
                    pts.Add(pcur); radii.Add(Mathf.Lerp(pr, pr * 0.6f, s / (float)N));
                    pcur += pd * (L / N); pd = Vector3.Normalize(pd + Vector3.down * 0.35f);
                }
                AddTube(wood, pts, radii, 6, 0.03f);
                opos = pcur; odir = Vector3.down;
            }

            switch (g.terminalOrgan)
            {
                case TerminalOrgan.Bloom:    AddBloom(flower, g, ref r, opos, odir, sz * 2.2f); break;
                case TerminalOrgan.Orb:      { float os = sz * r.Range(0.7f, 1.3f); AddOrganSphere(foliage, g, ref r, opos + odir * (os * 0.55f), os, r.Range(0.8f, 1.3f)); } break;   // embed + size variance
                case TerminalOrgan.Pitcher:  AddPitcher(foliage, g, ref r, opos, g.pendant > 0.05f ? odir : Vector3.Normalize(dir * 0.35f + Vector3.up), sz * 1.7f); break;
                case TerminalOrgan.Membrane: AddMembrane(foliage, g, opos, odir, sz * 2.6f, r.Range(0f, 1f)); break;
                case TerminalOrgan.Berry:    AddBerryCluster(flower, g, ref r, opos, odir, sz * 1.5f); break;
                case TerminalOrgan.Bulb:     AddOrganSphere(flower, g, ref r, opos + odir * (sz * 0.6f), sz * 1.15f, r.Range(0.7f, 1.1f)); break;   // embed
                case TerminalOrgan.Plume:    AddNeedleTuft(foliage, g, ref r, opos, odir, sz * 2.6f, r.Range(0f, 1f)); break;
                case TerminalOrgan.Cone:     AddCone(flower, g, ref r, opos - odir * (sz * 0.1f), odir, sz * 1.6f); break;
                case TerminalOrgan.Anemone:  AddAnemone(flower, g, ref r, opos, odir, sz * 1.3f); break;
            }
        }

        // A big multi-layered radial flower (chrysanthemum / aster): a domed disc ringed by tiers of ray petals that
        // lie progressively flatter toward the outside.
        static void AddBloom(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float size)
        {
            dir = dir.normalized;
            mb.tint = Color.white;
            Vector3 s0 = PerpUp(dir);
            Vector3 center = pos + dir * (size * 0.15f);
            AddSphere(mb, center, size * 0.3f, 0.25f, g.paint, 0.55f, 12, 7);        // disc floret dome
            int layers = 4 + r.RangeInt(0, 3);
            for (int L = 0; L < layers; L++)
            {
                float lt = L / (float)Mathf.Max(layers - 1, 1);
                float ring = size * (0.28f + 0.32f * lt);
                float petalLen = size * (0.9f - 0.45f * lt) * r.Range(0.9f, 1.1f);
                float tilt = Mathf.Lerp(18f, 78f, lt);                                // outer petals flatten out
                int petals = 9 + L * 3 + r.RangeInt(0, 3);
                float phase = r.Range(0f, 360f);
                for (int i = 0; i < petals; i++)
                {
                    float a = 360f * i / petals + phase;
                    Vector3 outr = (Quaternion.AngleAxis(a, dir) * s0).normalized;
                    Vector3 tang = Vector3.Cross(dir, outr);
                    Vector3 pbase = center + outr * (ring * 0.5f);
                    AddPetal(mb, g, pbase, dir, outr, tang, petalLen, r.Range(0.12f, 0.22f), tilt, r.Range(5f, 30f), r.Range(0f, 1f));   // slender cupped ray petal
                }
            }
        }

        // A dense spike/cluster of round berries, each with a dark eye-spot (image 42's white eyeball berries).
        static void AddBerryCluster(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float size)
        {
            dir = dir.normalized;
            Vector3 s0 = PerpUp(dir), s1 = Vector3.Cross(dir, s0);
            int n = 12 + r.RangeInt(0, 16);
            float spike = size * 2.2f;
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n;
                float a = t * 137.5f * Mathf.Deg2Rad * n * 0.5f;   // spiral around the spike
                float rad = size * 0.45f * (1f - 0.5f * t);
                Vector3 jit = new Vector3(r.Range(-1f, 1f), r.Range(-1f, 1f), r.Range(-1f, 1f)) * (size * 0.08f);
                Vector3 c = pos + dir * (t * spike) + (s0 * Mathf.Cos(a) + s1 * Mathf.Sin(a)) * rad + jit;
                float br = size * r.Range(0.16f, 0.26f);
                AddOrganSphere(mb, g, ref r, c, br, r.Range(0.9f, 1.1f));   // relief + pattern per the genome
                // dark eye-spot: a raised iris seated proud on the outward face + a small bright highlight
                Vector3 outN = Vector3.Normalize(Vector3.ProjectOnPlane(c - pos, dir) + (c - pos) * 0.3f);
                if (outN.sqrMagnitude < 1e-6f) outN = (c - pos).normalized;
                mb.tint = new Color(0.09f, 0.07f, 0.08f);
                AddSphere(mb, c + outN * br * 0.72f, br * 0.5f, 0f, PaintJob.Solid, 1f, 8, 6);
                mb.tint = new Color(0.95f, 0.95f, 0.98f);
                AddSphere(mb, c + outN * br * 1.02f, br * 0.16f, 0f, PaintJob.Solid, 1f, 5, 4);   // glint
                mb.tint = Color.white;
            }
            mb.tint = Color.white;
        }

        // CONE: an egg-shaped core shingled with a Fibonacci spiral of cupped scales that flare at the base and
        // hug the axis toward the tip (pinecone / cycad cone / the "egg" organs in alien concept art).
        static void AddCone(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float size)
        {
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            Vector3 s0 = PerpUp(dir);
            float L = size * r.Range(1.6f, 2.4f), R = size * r.Range(0.42f, 0.7f), widest = r.Range(0.3f, 0.5f);
            float Prof(float t) => R * Mathf.Pow(Mathf.Sin(Mathf.Clamp01(t * 0.97f + 0.02f) * Mathf.PI), 0.75f)
                                     * (t < widest ? 1f : Mathf.Lerp(1f, 0.55f, (t - widest) / (1f - widest)));
            var pts = new List<Vector3>(); var radii = new List<float>();
            for (int k = 0; k <= 10; k++) { float t = k / 10f; pts.Add(pos + dir * (t * L)); radii.Add(Mathf.Max(Prof(t) * 0.72f, size * 0.02f)); }
            mb.tint = new Color(0.7f, 0.7f, 0.7f);
            AddTube(mb, pts, radii, 12, 0.02f);                       // darker core peeks between the scales
            mb.tint = Color.white;
            int n = 55 + r.RangeInt(0, 60);
            for (int i = 0; i < n; i++)
            {
                float t = Mathf.Lerp(0.04f, 0.96f, (i + 0.5f) / n);
                float az = i * 137.508f;
                Vector3 outr = (Quaternion.AngleAxis(az, dir) * s0).normalized;
                Vector3 tang = Vector3.Cross(dir, outr).normalized;
                Vector3 b = pos + dir * (t * L) + outr * (Prof(t) * 0.6f);
                float len = R * (0.35f + 0.45f * Mathf.Sin(t * Mathf.PI)) * r.Range(0.85f, 1.15f);
                AddPetal(mb, g, b, dir, outr, tang, len, r.Range(0.42f, 0.6f), Mathf.Lerp(62f, 22f, t), r.Range(-25f, -5f), r.Range(0f, 1f));
            }
        }

        // ANEMONE: a relief-textured head ringed by glandular tentacles that curl in spirals and droop, each tipped
        // with a glistening bead (sundew / sea-anemone / the tentacle-crowned trees in alien concept art).
        static void AddAnemone(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float size)
        {
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            Vector3 s0 = PerpUp(dir);
            Vector3 head = pos + dir * (size * 0.45f);
            AddOrganSphere(mb, g, ref r, head, size * 0.5f, r.Range(0.75f, 1.2f));
            int n = 8 + r.RangeInt(0, 12);
            float curlSign = r.Value < 0.5f ? -1f : 1f;
            for (int i = 0; i < n; i++)
            {
                float az = 360f * i / n + r.Range(-8f, 8f);
                Vector3 outr = (Quaternion.AngleAxis(az, dir) * s0).normalized;
                Vector3 tang = Vector3.Cross(dir, outr).normalized;
                Vector3 p = head + outr * (size * 0.4f) - dir * (size * r.Range(0.05f, 0.3f));
                Vector3 d = Vector3.Normalize(outr + dir * r.Range(-0.4f, 0.4f));
                float Lt = size * r.Range(1.2f, 3f), curl = r.Range(6f, 24f) * curlSign;
                int segs = 14; float step = Lt / segs, tr = size * r.Range(0.05f, 0.09f);
                var pts = new List<Vector3>(); var radii = new List<float>();
                for (int s = 0; s <= segs; s++)
                {
                    float t = s / (float)segs;
                    pts.Add(p); radii.Add(Mathf.Lerp(tr, tr * 0.22f, t));
                    p += d * step;
                    d = (Quaternion.AngleAxis(curl * (0.4f + t), tang) * d).normalized;   // tightening spiral
                    d = Vector3.Normalize(d + Vector3.down * (0.05f + g.pendant * 0.12f));
                }
                AddTube(mb, pts, radii, 6, 0.03f);
                mb.tint = new Color(1.35f, 1.3f, 1.3f);                   // glistening droplet
                AddSphere(mb, pts[segs], tr * 1.3f, 0f, PaintJob.Solid, 1f, 7, 5);
                mb.tint = Color.white;
            }
        }

        // SPIRE: a spiky succulent rosette at the ground, from which a tall, slightly leaning column rises, studded
        // in a spiral with florets (textured organ beads, small flowers) and bristly bracts — alpine giants like
        // Puya raimondii, silversword and giant lobelia, which pour everything into one towering inflorescence.
        static void BuildSpire(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            float H = Mathf.Clamp(g.heightM, 1f, 12f) * r.Range(0.8f, 1.3f);
            float rosR = H * r.Range(0.18f, 0.3f);
            for (int ring = 0; ring < 3; ring++)
            {
                int cnt = 12 + r.RangeInt(0, 8) - ring * 3;
                for (int i = 0; i < cnt; i++)
                {
                    float az = 360f * i / cnt + ring * 17f + r.Range(-6f, 6f);
                    float elev = 15f + ring * 22f + r.Range(-5f, 5f);
                    Vector3 d = (Quaternion.AngleAxis(az, Vector3.up) * Quaternion.AngleAxis(90f - elev, Vector3.right) * Vector3.up).normalized;
                    AddThickLeaf(foliage, g, Vector3.up * (ring * H * 0.02f), d, rosR * (1f - ring * 0.2f) * r.Range(0.85f, 1.15f), rosR * 0.09f * g.bodyGirth);
                }
            }
            // the column
            Vector3 lean = new Vector3(r.Range(-0.12f, 0.12f), 1f, r.Range(-0.12f, 0.12f)).normalized;
            float colR = H * r.Range(0.05f, 0.09f) * g.bodyGirth;
            int m = 16; var pts = new List<Vector3>(); var radii = new List<float>();
            Vector3 p = Vector3.zero, d0 = lean;
            for (int k = 0; k <= m; k++)
            {
                float t = k / (float)m;
                pts.Add(p); radii.Add(colR * Mathf.Lerp(1.15f, 0.25f, Mathf.Pow(t, 1.4f)));
                p += d0 * (H / m); d0 = Vector3.Normalize(d0 + new Vector3(r.Range(-0.03f, 0.03f), 0f, r.Range(-0.03f, 0.03f)));
            }
            AddTube(wood, pts, radii, 12, 0.03f);
            // florets on a Fibonacci spiral up the column
            int nf = 70 + r.RangeInt(0, 90);
            int kind = r.RangeInt(0, 3);   // 0 organ beads · 1 small flowers · 2 bristly bracts + beads
            for (int i = 0; i < nf; i++)
            {
                float t = Mathf.Lerp(0.18f, 0.98f, (i + 0.5f) / nf);
                float fk = t * m; int k0 = Mathf.Min(Mathf.FloorToInt(fk), m - 1); float fr = fk - k0;
                Vector3 axisP = Vector3.Lerp(pts[k0], pts[k0 + 1], fr);
                float cr = Mathf.Lerp(radii[k0], radii[k0 + 1], fr);
                Vector3 outr = (Quaternion.AngleAxis(i * 137.508f, Vector3.up) * Vector3.right).normalized;
                Vector3 surf = axisP + outr * (cr * 0.85f);
                if (kind == 1 && r.Value < 0.7f)
                    AddFlower(flower, g, ref r, surf, Vector3.Normalize(outr + Vector3.up * 0.4f));
                else
                    AddOrganSphere(flower, g, ref r, surf + outr * (cr * 0.25f), cr * r.Range(0.35f, 0.6f), r.Range(0.8f, 1.1f), 10, 7);
                if (kind == 2 && r.Value < 0.5f)
                    AddBlade(foliage, g, surf, Vector3.Normalize(outr + Vector3.up * r.Range(0.2f, 0.8f)), cr * 0.08f, cr * r.Range(1.5f, 3f), 0.2f);
            }
        }

        // SEGMENTED: clumps of jointed, hollow-looking stalks (horsetail / Equisetum). Each node swells and wears a
        // dark sheath collar and a whorl of fine needle-branches that shorten toward the top; fertile stalks end in
        // a small spore cone.
        static void BuildSegmented(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            float H = Mathf.Clamp(g.heightM, 0.4f, 6f);
            int stems = 3 + Mathf.RoundToInt(g.branchDensity * 6f);
            for (int st = 0; st < stems; st++)
            {
                Vector3 basep = new Vector3(r.Range(-1f, 1f), 0f, r.Range(-1f, 1f)) * H * 0.12f;
                Vector3 d = Vector3.Normalize(Vector3.up + new Vector3(r.Range(-0.25f, 0.25f), 0f, r.Range(-0.25f, 0.25f)));
                float h = H * r.Range(0.6f, 1.1f), rad = H * 0.013f * g.bodyGirth * r.Range(0.8f, 1.2f);
                int segs = 6 + r.RangeInt(0, 7); float segLen = h / segs;
                bool fertile = r.Value < 0.35f, whorls = !fertile || r.Value < 0.4f;
                var pts = new List<Vector3>(); var radii = new List<float>();
                Vector3 p = basep;
                for (int s = 0; s <= segs; s++)
                {
                    float t = s / (float)segs;
                    float nodeR = rad * Mathf.Lerp(1f, 0.6f, t);
                    pts.Add(p); radii.Add(nodeR * 1.18f);                                              // swollen node
                    if (s < segs) { pts.Add(p + d * segLen * 0.5f); radii.Add(nodeR); }             // internode
                    if (s > 0 && s < segs)
                    {
                        // sheath collar (dark, slightly flared)
                        wood.tint = new Color(0.35f, 0.3f, 0.28f);
                        AddTube(wood, new List<Vector3> { p - d * nodeR * 0.4f, p + d * nodeR * 2.2f },
                                      new List<float> { nodeR * 1.3f, nodeR * 1.45f }, 10, 0.05f);
                        wood.tint = Color.white;
                        if (whorls)
                        {
                            int nw = 8 + r.RangeInt(0, 9);
                            float wl = H * 0.2f * (1f - t) * r.Range(0.7f, 1.1f) + rad * 2f;
                            for (int w = 0; w < nw; w++)
                            {
                                Vector3 outr = (Quaternion.AngleAxis(360f * w / nw + s * 13f, d) * PerpUp(d)).normalized;
                                AddBlade(foliage, g, p + outr * nodeR, Vector3.Normalize(outr + d * 0.35f), rad * 0.35f, wl, 0.35f);
                            }
                        }
                    }
                    if (s < segs) { p += d * segLen; d = Vector3.Normalize(d + new Vector3(r.Range(-0.04f, 0.04f), 0f, r.Range(-0.04f, 0.04f))); }
                }
                AddTube(wood, pts, radii, 10, 0.02f);
                if (fertile) AddCone(flower, g, ref r, p - d * rad, d, rad * 3.2f);
            }
        }

        // LIVING STONE: lithops-like pairs of fleshy half-domes split by a fissure, sitting flush with (partly in)
        // the ground so they read as pebbles; some push a single daisy-like flower out of the fissure.
        static void BuildLivingStone(PlantGenome g, ref DetRng r, MB wood, MB foliage, MB flower)
        {
            float H = Mathf.Clamp(g.heightM, 0.05f, 1f);
            int count = 1 + Mathf.RoundToInt(g.leafDensity * 4f);
            for (int i = 0; i < count; i++)
            {
                float rad = H * 0.5f * g.bodyGirth * r.Range(0.6f, 1.2f);
                float ring = i == 0 ? 0f : H * (0.9f + 0.5f * Mathf.Sqrt(i));
                float ang = i * 137.508f * Mathf.Deg2Rad;
                Vector3 c = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ring;
                Vector3 axis = Quaternion.AngleAxis(r.Range(0f, 180f), Vector3.up) * Vector3.right;
                float gap = rad * r.Range(0.45f, 0.6f);
                AddOrganSphere(foliage, g, ref r, c + axis * gap + Vector3.up * rad * 0.2f, rad * 0.62f, r.Range(0.7f, 0.95f), 18, 12);
                AddOrganSphere(foliage, g, ref r, c - axis * gap + Vector3.up * rad * 0.2f, rad * 0.62f, r.Range(0.7f, 0.95f), 18, 12);
                if (g.hasFlowers || r.Value < 0.35f)
                    AddFlower(flower, g, ref r, c + Vector3.up * rad * 0.55f, Vector3.up);
            }
        }

        // A reusable LEAF/FROND BASE: a short tapering woody socket (petiole boot) rooted at `pos` and growing along
        // `dir`, so a leaf/frond plugs organically into the stem instead of poking out of a bare tube. Returns the tip
        // position where the leaf itself should begin. Add it to the WOOD mesh; recycle for palms, ferns, big leaves…
        static Vector3 AddLeafBase(MB wood, Vector3 pos, Vector3 dir, float baseR, float length, int sides = 7)
        {
            dir = dir.normalized;
            var pts = new List<Vector3> { pos - dir * (length * 0.2f), pos + dir * (length * 0.5f), pos + dir * length };
            var radii = new List<float> { baseR * 1.2f, baseR * 0.7f, baseR * 0.42f };
            AddTube(wood, pts, radii, sides, 0.02f);
            return pos + dir * length;
        }

        // A compound frond: a rachis with leaflets down each side.
        static void AddFrond(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir, float length, bool droop)
        {
            // 1) Trace the RACHIS (the frond's central stalk): it arches down under gravity and, with frondCurl,
            //    rolls its tip into a tightening spiral (circinate vernation: a fiddlehead).
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            Vector3 side = PerpUp(dir);
            int N = 28;
            float curl = Mathf.Clamp01(g.frondCurl), curlStart = 1f - 0.45f * curl;
            var pts = new List<Vector3>(); var tans = new List<Vector3>(); var radii = new List<float>();
            Vector3 p = pos, d = dir;
            float rachisR = Mathf.Max(length * 0.012f, 0.004f);
            for (int k = 0; k <= N; k++)
            {
                float t = k / (float)N;
                pts.Add(p); tans.Add(d); radii.Add(rachisR * Mathf.Lerp(1f, 0.3f, t));
                float u = curl > 0.01f && t > curlStart ? (t - curlStart) / (1f - curlStart) : 0f;
                float step = length / N * Mathf.Lerp(1f, 0.45f, u);                       // spiral tightens
                p += d * step;
                if (droop) d = Vector3.Normalize(d + Vector3.down * 0.05f);                // arch under gravity
                if (u > 0f) d = (Quaternion.AngleAxis(-38f * curl * (0.5f + u), side) * d).normalized;   // coil
            }
            AddTube(mb, pts, radii, 6, 0.02f);

            // 2) Leaflets (pinnae) rooted ON the rachis, in opposite pairs, angled toward the tip; none in the coil.
            int count = Mathf.Max(4, Mathf.RoundToInt((8f + length * 3f) * g.frondPinnae));
            float zone = curlStart * 0.96f;
            for (int j = 0; j < count; j++)
            {
                float tz = (j + 0.5f) / count, t = tz * zone;
                float fk = t * N; int k0 = Mathf.Min(Mathf.FloorToInt(fk), N - 1); float fr = fk - k0;
                Vector3 at = Vector3.Lerp(pts[k0], pts[k0 + 1], fr);
                Vector3 tg = Vector3.Normalize(Vector3.Lerp(tans[k0], tans[k0 + 1], fr));
                Vector3 s = Vector3.ProjectOnPlane(side, tg); s = s.sqrMagnitude > 1e-6f ? s.normalized : PerpUp(tg);
                float plen = length * 0.3f * Mathf.Sin(Mathf.Lerp(0.12f, 1f, tz) * Mathf.PI) * g.leafSize + length * 0.03f;
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 pd = Vector3.Normalize(s * sg + tg * 0.45f + Vector3.down * (droop ? 0.15f : 0f));
                    AddPinna(mb, g, ref r, at, pd, tg, plen, rachisR, tz);
                }
            }
        }

        // One pinna. Once-divided: a single shaped leaflet. Twice-divided: its own mini-rachis bearing pinnules
        // (a lacy, bipinnate frond) — every piece starts on the stalk it grows from, so nothing floats.
        static void AddPinna(MB mb, PlantGenome g, ref DetRng r, Vector3 basePos, Vector3 pd, Vector3 rachisDir, float len, float rachisR, float along)
        {
            float wf = 0.09f * g.frondWidth;
            if (g.frondDivision < 0.5f)
            {
                AddSmallLeaflet(mb, g, basePos, pd, len, along, wf * 1.6f);
                return;
            }
            var mp = new List<Vector3>(); var mr = new List<float>();
            for (int k = 0; k <= 4; k++) { mp.Add(basePos + pd * (len * k / 4f) + Vector3.down * (len * 0.06f * k * k / 16f)); mr.Add(rachisR * Mathf.Lerp(0.45f, 0.15f, k / 4f)); }
            AddTube(mb, mp, mr, 4, 0.02f);
            Vector3 n = Vector3.Cross(rachisDir, pd); n = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
            Vector3 ps = Vector3.Cross(n, pd).normalized;
            int cnt = Mathf.Clamp(Mathf.RoundToInt(len * 30f * g.frondPinnae * (0.5f + g.frondDivision)), 4, 16);
            for (int q = 0; q < cnt; q++)
            {
                float u = (q + 0.5f) / cnt;
                Vector3 pp = basePos + pd * (len * u) + Vector3.down * (len * 0.06f * u * u);
                float pl = len * 0.34f * Mathf.Sin(Mathf.Lerp(0.2f, 1f, u) * Mathf.PI) + len * 0.03f;
                AddSmallLeaflet(mb, g, pp, Vector3.Normalize(ps + pd * 0.55f), pl, along, wf * 1.3f);
                AddSmallLeaflet(mb, g, pp, Vector3.Normalize(-ps + pd * 0.55f), pl, along, wf * 1.3f);
            }
        }

        // A slender tapering pinna (frond leaflet). Base → mid → point, gently drooping, so a frond reads as feathery
        // rather than a comb of triangles.
        static void AddSmallLeaflet(MB mb, PlantGenome g, Vector3 pos, Vector3 dir, float len, float along, float widthF = 0.09f)
        {
            dir = dir.normalized;
            Vector3 side = PerpUp(dir);
            Vector3 nrm = Vector3.Cross(side, dir);
            Vector3 droop = Vector3.ProjectOnPlane(Vector3.down, dir);
            droop = droop.sqrMagnitude > 1e-5f ? droop.normalized : Vector3.zero;
            float w = len * widthF;
            int b0 = mb.Count;
            mb.Vert(pos - side * w, nrm, AccentA(g.paint, 0f, 1f, along));
            mb.Vert(pos + side * w, nrm, AccentA(g.paint, 0f, 1f, along));
            Vector3 mid = pos + dir * (len * 0.6f) + droop * (len * 0.06f);
            mb.Vert(mid - side * (w * 0.55f), nrm, AccentA(g.paint, 0.6f, 1f, along));
            mb.Vert(mid + side * (w * 0.55f), nrm, AccentA(g.paint, 0.6f, 1f, along));
            mb.Vert(pos + dir * len + droop * (len * 0.12f), nrm, AccentA(g.paint, 1f, 0f, along));   // point
            mb.Tri(b0, b0 + 2, b0 + 1); mb.Tri(b0 + 1, b0 + 2, b0 + 3);
            mb.Tri(b0 + 2, b0 + 4, b0 + 3);
        }

        static void AddFlower(MB mb, PlantGenome g, ref DetRng r, Vector3 pos, Vector3 dir)
        {
            // A real flower: a short receptacle, one or two whorls of CUPPED, shaped petals (narrow claw → broad
            // rounded blade → soft tip, curling outward), a domed centre disc and a ring of stamens. Petal shape,
            // cup depth, curl and whorl count vary per flower so a plant's flowers aren't identical stamps.
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            int petals = Mathf.Clamp(g.petalCount, 3, 12);
            float size = 0.25f * g.flowerSize * Mathf.Max(g.leafSize, 0.4f);
            Vector3 s0 = PerpUp(dir), s1 = Vector3.Cross(dir, s0);
            float cup = r.Range(20f, 70f);            // how upright the petals start (cup ↔ open star)
            float curl = r.Range(-15f, 35f);          // how much they curl back toward the tip
            float broad = r.Range(0.25f, 0.5f);       // petal width relative to length
            int whorls = r.Value < 0.45f ? 2 : 1;
            float phase = r.Range(0f, 360f);
            mb.tint = Color.white;
            for (int w = 0; w < whorls; w++)
            {
                float wl = size * (w == 0 ? 1f : 0.72f);
                float wcup = cup + (w == 0 ? 0f : -18f);
                for (int i = 0; i < petals; i++)
                {
                    float az = phase + 360f * (i + (w == 0 ? 0f : 0.5f)) / petals + r.Range(-6f, 6f);
                    Vector3 outr = (Quaternion.AngleAxis(az, dir) * s0).normalized;
                    Vector3 tang = Vector3.Cross(dir, outr).normalized;
                    AddPetal(mb, g, pos + dir * (size * 0.05f * w), dir, outr, tang, wl * r.Range(0.9f, 1.1f), broad, wcup, curl, r.Range(0f, 1f));
                }
            }
            // centre: a domed disc (accent) + a ring of thin stamens with bright tips
            AddSphere(mb, pos + dir * (size * 0.08f), size * 0.16f, 0.2f, PaintJob.Solid, 0.55f, 10, 6);
            int stamens = 6 + petals;
            for (int i = 0; i < stamens; i++)
            {
                float az = 360f * i / stamens + r.Range(-8f, 8f);
                Vector3 outr = (Quaternion.AngleAxis(az, dir) * s0).normalized;
                Vector3 sd = Vector3.Normalize(dir * 1.6f + outr * r.Range(0.3f, 0.7f));
                Vector3 b = pos + dir * (size * 0.1f) + outr * (size * 0.1f);
                Vector3 tip = b + sd * (size * r.Range(0.3f, 0.45f));
                Vector3 wv = Vector3.Cross(sd, outr).normalized * (size * 0.012f);
                int q = mb.Count;
                mb.Vert(b - wv, sd, 0.3f); mb.Vert(b + wv, sd, 0.3f); mb.Vert(tip, sd, 1f);
                mb.Tri(q, q + 1, q + 2); mb.Tri(q, q + 2, q + 1);
            }
        }

        // One cupped petal: rows of (left, mid, right) along its length. Starts tilted `cupDeg` from the flower axis,
        // bends further out by `curlDeg` toward the tip, and is dished across its width (a real petal, not a card).
        static void AddPetal(MB mb, PlantGenome g, Vector3 basePos, Vector3 axis, Vector3 outr, Vector3 tang, float len,
                             float broad, float cupDeg, float curlDeg, float rand)
        {
            int segN = 10, b0 = mb.Count;
            Vector3 p = basePos;
            float ruffle = 0.08f + g.leafRuffle * 0.22f, jit = len * (0.012f + g.leafJitter * 0.03f);
            float wavePh = rand * 6.28f, lopside = (rand - 0.5f) * 0.3f;
            for (int s = 0; s <= segN; s++)
            {
                float t = s / (float)segN;
                // petal outline: narrow claw at the base, broad rounded blade, soft tip — slightly lopsided
                float wdt = len * broad * Mathf.Sin(Mathf.Pow(t, 0.7f) * Mathf.PI) * (t < 0.15f ? Mathf.Lerp(0.35f, 1f, t / 0.15f) : 1f);
                float ang = cupDeg + curlDeg * t * t;
                Vector3 d = Vector3.Normalize(Vector3.Lerp(axis, outr, Mathf.Clamp01(ang / 90f)));   // tilt from axis toward outward
                Vector3 nrm = Vector3.Cross(tang, d).normalized;
                float dish = wdt * 0.35f;                                                       // edges lift → cupped
                float waveL = Mathf.Sin(t * 11f + wavePh) * ruffle * wdt * t;                   // ruffled margins,
                float waveR = Mathf.Sin(t * 11f + wavePh + 1.9f) * ruffle * wdt * t;            // out of phase per side
                float crease = -wdt * 0.12f;                                                    // midrib sits in a groove
                float a = AccentA(g.paint, t, 0f, rand);
                Vector3 pl = p - tang * (wdt * (1f + lopside)) + nrm * (dish + waveL);
                Vector3 pc = p + nrm * crease;
                Vector3 pr = p + tang * (wdt * (1f - lopside)) + nrm * (dish + waveR);
                mb.Vert(pl + NoiseOffset(pl, rand * 53f, jit), nrm, a, new Vector2(0f, t));
                mb.Vert(pc, nrm, AccentA(g.paint, t, 1f, rand) * 0.6f + (1f - t) * 0.4f, new Vector2(0.5f, t));
                mb.Vert(pr + NoiseOffset(pr, rand * 53f, jit), nrm, a, new Vector2(1f, t));
                p += d * (len / segN);
            }
            for (int s = 0; s < segN; s++)
            {
                int r0 = b0 + s * 3, r1 = r0 + 3;
                mb.Tri(r0, r1, r0 + 1); mb.Tri(r0 + 1, r1, r1 + 1);
                mb.Tri(r0 + 1, r1 + 1, r0 + 2); mb.Tri(r0 + 2, r1 + 1, r1 + 2);
            }
        }

        static void AddSphere(MB mb, Vector3 center, float rad, float ribbing, PaintJob paint, float squash = 1f, int lon = 12, int lat = 8)
        {
            int b0 = mb.Count;
            for (int i = 0; i <= lat; i++)
            {
                float v = i / (float)lat, phi = v * Mathf.PI;
                for (int j = 0; j <= lon; j++)
                {
                    float u = j / (float)lon, th = u * 2f * Mathf.PI;
                    float rib = 1f - ribbing * 0.18f * (0.5f + 0.5f * Mathf.Cos(th * lon));
                    Vector3 nrm = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi) * squash, Mathf.Sin(phi) * Mathf.Sin(th)).normalized;
                    // slight organic irregularity (small spheres too: no perfect geometric balls)
                    rib *= 1f + (FBM3(nrm * 1.4f + center * 7.3f, 2) - 0.5f) * 0.22f;
                    mb.Vert(center + Vector3.Scale(nrm, new Vector3(rad, rad * squash, rad)) * rib, nrm, AccentA(paint, v, Mathf.Abs(Mathf.Cos(th)), u));
                }
            }
            int stride = lon + 1;
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = b0 + i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                    mb.Tri(a, b, c); mb.Tri(b, d, c);   // outward-facing winding
                }
        }

        static void AddCap(MB mb, Vector3 baseTop, float rad, float height, PaintJob paint)
        {
            int lon = 14, lat = 5, b0 = mb.Count;
            for (int i = 0; i <= lat; i++)
            {
                float v = i / (float)lat, phi = v * Mathf.PI * 0.5f;   // hemisphere dome
                for (int j = 0; j <= lon; j++)
                {
                    float th = j / (float)lon * 2f * Mathf.PI;
                    Vector3 nrm = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    Vector3 pos = baseTop + new Vector3(nrm.x * rad, nrm.y * height, nrm.z * rad);
                    mb.Vert(pos, nrm, AccentA(paint, 1f - v, 0f, j / (float)lon));
                }
            }
            int stride = lon + 1;
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = b0 + i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                    mb.Tri(a, b, c); mb.Tri(b, d, c);   // outward-facing winding
                }
        }

        static void AddSpines(MB mb, Vector3 center, float rad, int count, DetRng r, Vector3 axis = default, float len = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = new Vector3(r.Range(-1f, 1f), r.Range(-1f, 1f), r.Range(-1f, 1f)).normalized;
                Vector3 baseP = center + dir * rad * (len > 0f ? 0.9f : 1f) + (len > 0f ? axis * r.Range(-len * 0.5f, len * 0.5f) : Vector3.zero);
                Vector3 tip = baseP + dir * rad * r.Range(0.25f, 0.5f);
                Vector3 s = PerpUp(dir) * rad * 0.02f;
                int b0 = mb.Count;
                mb.Vert(baseP - s, dir, 0f); mb.Vert(baseP + s, dir, 0f); mb.Vert(tip, dir, 0f);
                mb.Tri(b0, b0 + 1, b0 + 2);
            }
        }

        // ── small maths helpers ──
        static float LeafWidth(LeafShape shape, float t) => shape switch
        {
            LeafShape.Lanceolate => Mathf.Pow(Mathf.Sin(t * Mathf.PI), 1.6f),
            LeafShape.Cordate => Mathf.Sin(Mathf.Clamp01(t * 0.7f + 0.3f) * Mathf.PI),
            LeafShape.Palmate => Mathf.Sin(Mathf.Pow(t, 0.85f) * Mathf.PI) * (0.72f + 0.28f * Mathf.Cos(t * Mathf.PI * 3f)),   // 3 rounded lobes, widest low
            LeafShape.Needle => 0.12f * (1f - t * 0.6f),
            LeafShape.Blade => 1f - t * 0.7f,
            LeafShape.Pad => Mathf.Sin(t * Mathf.PI),
            LeafShape.Scale => 0.4f * Mathf.Sin(t * Mathf.PI),
            LeafShape.Reniform => Mathf.Sin(Mathf.Clamp01(t * 0.55f + 0.2f) * Mathf.PI) * 1.15f,        // broad kidney
            LeafShape.Bilobe => Mathf.Sin(t * Mathf.PI) * (t > 0.82f ? Mathf.InverseLerp(1f, 0.82f, t) : 1f), // notched tip
            LeafShape.Strap => 1f - t * 0.55f,
            LeafShape.Tongue => Mathf.Sin(Mathf.Clamp01(t * 0.45f + 0.35f) * Mathf.PI) * 0.95f,        // broad, blunt-tipped
            LeafShape.Spatulate => Mathf.Pow(Mathf.Sin(Mathf.Clamp01(t * 0.75f + 0.12f) * Mathf.PI), 1.3f) * (t > 0.9f ? Mathf.InverseLerp(1f, 0.9f, t) : 1f), // widest near tip
            _ => Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.7f),   // Ovate
        };

        static float AccentA(PaintJob paint, float along, float side, float rand) => paint switch
        {
            PaintJob.TipTinted => SS(0f, 1f, Mathf.InverseLerp(0.5f, 1f, along)),
            PaintJob.Veined => 1f - SS(0f, 0.25f, side),
            PaintJob.Margin => SS(0.6f, 1f, side),
            PaintJob.Variegated => rand > 0.6f ? 1f : 0f,
            PaintJob.Spotted => (Mathf.Repeat(rand * 13.13f + along * 7f, 1f) > 0.82f) ? 1f : 0f,
            PaintJob.GradientVertical => Mathf.Clamp01(along),
            _ => 0f,
        };

        static float Hash01(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
        }

        // ── 3D value noise (for organ relief) ──
        static float VN(float x, float y, float z)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf), w = zf * zf * (3f - 2f * zf);
            float c000 = Hash01((xi * 73856093) ^ (yi * 19349663) ^ (zi * 83492791));
            float c100 = Hash01(((xi + 1) * 73856093) ^ (yi * 19349663) ^ (zi * 83492791));
            float c010 = Hash01((xi * 73856093) ^ ((yi + 1) * 19349663) ^ (zi * 83492791));
            float c110 = Hash01(((xi + 1) * 73856093) ^ ((yi + 1) * 19349663) ^ (zi * 83492791));
            float c001 = Hash01((xi * 73856093) ^ (yi * 19349663) ^ ((zi + 1) * 83492791));
            float c101 = Hash01(((xi + 1) * 73856093) ^ (yi * 19349663) ^ ((zi + 1) * 83492791));
            float c011 = Hash01((xi * 73856093) ^ ((yi + 1) * 19349663) ^ ((zi + 1) * 83492791));
            float c111 = Hash01(((xi + 1) * 73856093) ^ ((yi + 1) * 19349663) ^ ((zi + 1) * 83492791));
            float x00 = Mathf.Lerp(c000, c100, u), x10 = Mathf.Lerp(c010, c110, u);
            float x01 = Mathf.Lerp(c001, c101, u), x11 = Mathf.Lerp(c011, c111, u);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
        }
        static float FBM3(Vector3 p, int oct = 4)
        {
            float s = 0f, a = 0.5f;
            for (int i = 0; i < oct; i++) { s += a * VN(p.x, p.y, p.z); p *= 2.02f; a *= 0.5f; }
            return s;
        }

        // Small deterministic per-vertex displacement so organs never look machined — the imperfection of life.
        static Vector3 NoiseOffset(Vector3 p, float seed, float amp)
        {
            if (amp <= 0f) return Vector3.zero;
            int hx = Mathf.RoundToInt(p.x * 53.1f + seed * 13.7f);
            int hy = Mathf.RoundToInt(p.y * 47.3f + seed * 21.1f);
            int hz = Mathf.RoundToInt(p.z * 59.7f + seed * 31.3f);
            return new Vector3(Hash01(hx * 3 + 1) - 0.5f, Hash01(hy * 3 + 7) - 0.5f, Hash01(hz * 3 + 13) - 0.5f) * (2f * amp);
        }

        static Vector3 Perp(Vector3 d)
        {
            Vector3 up = Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Normalize(Vector3.Cross(up, d));
        }
        static Vector3 PerpUp(Vector3 d) => Perp(d);
        static Vector3 BranchDir(Vector3 d, float angle, float az, ref DetRng r)
        {
            // Tilt d away from its axis by `angle`, around a perpendicular axis that is itself SPUN around d by
            // `az` — so successive children fan out all the way around, not all to one side. (The old version
            // rotated d around its own axis for `az`, which is a no-op, so every branch tilted the same way.)
            d = d.normalized;
            Vector3 axis = (Quaternion.AngleAxis(az, d) * Perp(d)).normalized;
            return (Quaternion.AngleAxis(angle * r.Range(0.8f, 1.15f), axis) * d).normalized;
        }
    }
}
