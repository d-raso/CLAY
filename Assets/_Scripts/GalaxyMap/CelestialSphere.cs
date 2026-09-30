using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.GalaxyMap
{
    // A live, data-driven celestial sphere: the surrounding stars rendered as camera-following POINT GEOMETRY (not a
    // baked image), so the sky is crisp at any zoom and telescope-ready. Directions come from the real star catalog
    // (relative to the observer) plus a deterministic synthetic background for density; both are persistent (same
    // seed → same sky), so a civilization could catalog and re-find them.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CelestialSphere : MonoBehaviour
    {
        public Camera cam;
        public float radius = 2000f;
        public float pointSize = 2f;
        public float gain = 1f;
        public float magLimit = 0f;      // raise to show only bright stars (naked eye); lower for a "bigger aperture"

        Material _mat;
        Mesh _mesh;

        // Build the sky as seen from `observer`, using the galaxy's retained star catalog + synthetic background.
        public void Build(Vector3 observer, GalaxyBootstrap galaxy, ulong seed,
                          int backgroundStars, float starGain, float bgGain,
                          int densityBoost, float densityJitter, int bulgeStars, float bulgeGain,
                          float dustExtinction)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            EnsureBBLut();
            var P = galaxy != null ? galaxy.CurrentParams() : default;
            float dustMarch = (galaxy != null ? galaxy.DiskRadiusValue : 60f) * 1.3f;   // sightline length through the disk
            var mf = GetComponent<MeshFilter>();
            var mr = GetComponent<MeshRenderer>();
            if (_mat == null)
            {
                var sh = Shader.Find("Clay/CelestialStar");
                _mat = new Material(sh) { name = "CelestialStarMat" };
                mr.sharedMaterial = _mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            int n = galaxy != null ? galaxy.StarCountLive : 0;
            int total = n * (1 + Mathf.Max(0, densityBoost)) + Mathf.Max(0, backgroundStars) + Mathf.Max(0, bulgeStars);

            var verts = new List<Vector3>(total * 4);
            var uvs = new List<Vector2>(total * 4);
            var cols = new List<Color>(total * 4);
            var tris = new List<int>(total * 6);
            var rng = new DetRng(seed == 0UL ? 1UL : seed);

            // The core direction (galaxy centre at world origin). Stars pointing toward it get EXTRA density copies,
            // so the star field simply grows denser toward the galactic centre and thins out with no boundary — a
            // real bulge as a density gradient, NOT a pasted circle or a glow disc.
            Vector3 coreDir = observer.sqrMagnitude > 1e-4f ? (-observer).normalized : Vector3.forward;

            // Real catalog stars + faint jittered copies (density boost). Copies FOLLOW the real galaxy, so the band
            // and the central bulge both emerge from the true star distribution. Points stay small (never orbs).
            for (int i = 0; i < n; i++)
            {
                var s = galaxy.GetStar(i);
                if (s.Luminosity < 0f) continue;
                Vector3 rel = galaxy.GetStarWorld(i) - observer;
                float d2 = rel.sqrMagnitude;
                if (d2 < 1e-4f) continue;
                float dist = Mathf.Sqrt(d2);
                Vector3 dir = rel / dist;
                float inten = Mathf.Max(s.Luminosity, 0.0006f) / Mathf.Max(d2, 0.25f) * starGain;
                inten = inten / (1f + inten * 0.35f);        // soft-compress the brightest → no blown orbs
                inten *= DustAtten(observer, dir, Mathf.Min(dist, dustMarch), P, dustExtinction);  // dust lanes dim it
                Color bc = BB(s.Temperature);
                Vector3 crgb = new Vector3(bc.r, bc.g, bc.b);
                AddPoint(verts, uvs, cols, tris, dir, crgb * inten);

                // Density-boost copies only for stars bright enough that the copies will actually be seen — most of a
                // power-law population is near-invisible, so skipping their copies is a huge build-time/vert win.
                if (inten < 0.01f) continue;
                float align = Vector3.Dot(dir, coreDir);
                float coreW = Mathf.SmoothStep(0f, 1f, align);        // 0 away from core → 1 straight at it
                int extra = Mathf.RoundToInt(bulgeGain * 8f * coreW);
                int copies = densityBoost + extra;
                for (int k = 0; k < copies; k++)
                {
                    Vector3 jd = (dir + new Vector3(rng.Value - 0.5f, rng.Value - 0.5f, rng.Value - 0.5f) * (2f * densityJitter)).normalized;
                    AddPoint(verts, uvs, cols, tris, jd, crgb * (inten * (0.12f + 0.35f * rng.Value)));
                }
            }

            // NUCLEUS / BULGE (per astronomy: the integrated light of billions of unresolved stars → a bright,
            // TIGHT, BLOOMING concentration toward the core, only ~10–20° across from mid-disk, with resolved star
            // density climbing steeply into it). Tight angular size = atan(bulge/coreDist) (a compact nucleus, not a
            // wash), a small BRIGHT central glow so it blooms, dense bright stars on top, all dimmed with distance
            // (`distDim`) — fainter but still dense/bright when far. Smooth radial falloff → no hard edge.
            if (bulgeStars > 0 && galaxy != null)
            {
                float bulge = Mathf.Max(galaxy.BulgeSizeValue, galaxy.DiskRadiusValue * 0.08f);
                float coreDist = Mathf.Max(observer.magnitude, bulge * 0.5f);
                float distDim = Mathf.Clamp(bulge * 3f / coreDist, 0.2f, 1.3f);   // brighter near, fainter far (floored)
                float ang = Mathf.Clamp(Mathf.Atan(bulge * 1.2f / coreDist), 0.02f, 0.32f);   // tight, concentrated
                Vector3 upv = Mathf.Abs(coreDir.y) < 0.9f ? Vector3.up : Vector3.right;
                Vector3 tx = Vector3.Normalize(Vector3.Cross(upv, coreDir));
                Vector3 ty = Vector3.Cross(coreDir, tx);
                // Dense bright stars, steeply concentrated toward the centre. NO glow disc — bloom acting on this
                // tight dense cluster of bright points is what makes the core glow (a nucleus of stars, not a blob).
                for (int i = 0; i < bulgeStars; i++)
                {
                    float rr = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-4f, 1f - rng.Value))) * 0.45f * ang;
                    float a = rng.Value * 2f * Mathf.PI;
                    Vector3 dir = (coreDir * Mathf.Cos(rr) + (tx * Mathf.Cos(a) + ty * Mathf.Sin(a)) * Mathf.Sin(rr)).normalized;
                    float falloff = Mathf.Exp(-(rr / ang) * (rr / ang) * 1.3f);
                    float b = bulgeGain * distDim * (0.9f + 1.3f * Mathf.Pow(rng.Value, 1.5f)) * falloff;   // bright → blooms
                    b *= DustAtten(observer, dir, dustMarch, P, dustExtinction);   // dust lanes silhouette the core
                    Color sc = BB(Mathf.Lerp(3800f, 7500f, rng.Value));
                    AddPoint(verts, uvs, cols, tris, dir, new Vector3(sc.r, sc.g, sc.b) * b);
                }
            }

            // Faint uniform fill so the anti-galaxy sky isn't pure black.
            for (int i = 0; i < backgroundStars; i++)
            {
                float z = rng.Value * 2f - 1f, az = rng.Value * 2f * Mathf.PI;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                Vector3 dir = new Vector3(r * Mathf.Cos(az), z, r * Mathf.Sin(az));
                float b = Mathf.Pow(rng.Value, 3.2f) * bgGain * DustAtten(observer, dir, dustMarch, P, dustExtinction);
                Color bc = BB(Mathf.Lerp(2800f, 13000f, Mathf.Pow(rng.Value, 2f)));
                AddPoint(verts, uvs, cols, tris, dir, new Vector3(bc.r * b, bc.g * b, bc.b * b));
            }

            if (_mesh == null) { _mesh = new Mesh { name = "CelestialSphere" }; _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetUVs(0, uvs);
            _mesh.SetColors(cols);
            _mesh.SetTriangles(tris, 0);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);   // never frustum-cull the whole sky
            mf.sharedMesh = _mesh;

            transform.localScale = Vector3.one * radius;
            _ = sw;
        }

        // Blackbody colour lookup table (BlackbodyColor's pow/log is far too slow to call per star).
        static Color[] _bbLut;
        static void EnsureBBLut()
        {
            if (_bbLut != null) return;
            _bbLut = new Color[128];
            for (int i = 0; i < 128; i++) _bbLut[i] = Astrophysics.BlackbodyColor(Mathf.Lerp(1500f, 40000f, i / 127f));
        }
        static Color BB(float tempK)
        {
            int idx = Mathf.Clamp((int)((tempK - 1500f) / (38500f) * 127f), 0, 127);
            return _bbLut[idx];
        }

        // Fraction of a star's light that survives interstellar DUST along the sightline — marches a disk dust field
        // (exponential radial × sech² plane × spiral-arm compression × ridged-noise lanes) and applies Beer-Lambert
        // extinction. Stars seen through the dusty disk/arms are dimmed → the dark rift and lanes across the band.
        static float DustAtten(Vector3 observer, Vector3 dir, float maxDist, in GalaxyParameters P, float ext)
        {
            if (ext <= 0f || P.DiskRadius <= 0f) return 1f;
            const int steps = 5;
            float step = maxDist / steps, tau = 0f;
            for (int i = 0; i < steps; i++)
            {
                Vector3 p = observer + dir * (step * (i + 0.5f));
                tau += DustDensity(p, P) * step;
            }
            return Mathf.Exp(-tau * ext * Mathf.Max(P.GasDustDensity, 0.15f));
        }

        static float DustDensity(Vector3 p, in GalaxyParameters P)
        {
            float rr = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            float radial = Mathf.Exp(-rr / Mathf.Max(P.DiskRadius * 0.9f, 1f));
            float hz = Mathf.Max(P.DiskThickness * P.DiskRadius * 0.45f, 0.05f);
            float sech = 1f / (float)System.Math.Cosh(p.y / hz);
            float vertical = sech * sech;
            float x = Mathf.Clamp01((rr - P.BulgeSize * 0.4f) / Mathf.Max(P.BulgeSize * 1.1f, 1e-3f));
            float hole = x * x * (3f - 2f * x);                       // edge-based smoothstep bulge hole
            int arms = Mathf.Max(1, P.ArmCount);
            float pitch = Mathf.Max(P.PitchAngle, 0.02f);
            float refR = Mathf.Max(P.BarStrength * P.DiskRadius * 0.35f, 1f);
            float ang = Mathf.Atan2(p.z, p.x);
            float armPhase = arms * (ang - Mathf.Log(Mathf.Max(rr, 1e-3f) / refR) / pitch);
            float sc = (Mathf.Sin(armPhase) + 1f) * 0.5f;
            float armBias = 0.35f + 1.3f * sc * sc;                   // dust piles up on the arms
            float lane = 1f - Mathf.Abs(noise.snoise(new float3(p.x, p.y, p.z) * 0.06f));
            lane *= lane;                                             // ridged → filamentary dark lanes
            return radial * vertical * hole * armBias * (0.35f + 0.9f * lane);
        }

        // Adds one star point. Size (in color.a) stays small — a star is a pinpoint; only the very brightest grow a
        // little, and never into a glowing orb.
        static void AddPoint(List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> t, Vector3 dir, Vector3 rgb)
        {
            float lum = Mathf.Max(rgb.x, Mathf.Max(rgb.y, rgb.z));
            float size = Mathf.Clamp(0.7f + 0.45f * Mathf.Sqrt(lum), 0.7f, 1.6f);
            var col = new Color(rgb.x, rgb.y, rgb.z, size);
            int b = v.Count;
            v.Add(dir); v.Add(dir); v.Add(dir); v.Add(dir);
            uv.Add(new Vector2(-1, -1)); uv.Add(new Vector2(1, -1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(-1, 1));
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        }

        // A soft additive glow disc (for the diffuse bulge): a real angular quad (corners are sky directions) with a
        // ≈0 size flag so the shader's pixel-billboard offset is negligible; the shader's round falloff makes it a
        // smooth glow that fades to nothing at the rim (no hard ball edge).
        static void AddGlowQuad(List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> t,
                                Vector3 cd, Vector3 tx, Vector3 ty, float s, Vector3 rgb)
        {
            Vector3 d00 = (cd - tx * s - ty * s).normalized;
            Vector3 d10 = (cd + tx * s - ty * s).normalized;
            Vector3 d11 = (cd + tx * s + ty * s).normalized;
            Vector3 d01 = (cd - tx * s + ty * s).normalized;
            var col = new Color(rgb.x, rgb.y, rgb.z, 0.001f);   // a≈0 → no pixel offset; use the geometry size
            int b = v.Count;
            v.Add(d00); v.Add(d10); v.Add(d11); v.Add(d01);
            uv.Add(new Vector2(-1, -1)); uv.Add(new Vector2(1, -1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(-1, 1));
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        }

        void LateUpdate()
        {
            if (cam != null)
            {
                transform.position = cam.transform.position;                // camera-following → infinite distance
                // Sit just INSIDE the (dynamically-changing) far clip. The old `+1` floor pushed the radius past
                // the far plane when you zoomed in very close (far shrinks with camDist): the sphere's forward cap
                // then poked through the flat far-clip plane and was clipped into a dark disc centred behind the
                // focused body — the "dark circle bigger than the planet" at extreme zoom. Pure fraction of far
                // (always > nearClip here, since far ≥ camDist*12 ≫ near) keeps the whole sky in front of the far plane.
                float r = cam.farClipPlane * 0.92f;
                transform.localScale = Vector3.one * r;
            }
            if (_mat != null)
            {
                _mat.SetFloat("_PointSize", pointSize);
                _mat.SetFloat("_Gain", gain);
                _mat.SetFloat("_MagLimit", magLimit);
            }
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
