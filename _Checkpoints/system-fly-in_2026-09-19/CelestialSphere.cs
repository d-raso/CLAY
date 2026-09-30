using System.Collections.Generic;
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
                          int densityBoost, float densityJitter, int bulgeStars, float bulgeGain)
        {
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

            // Real catalog stars + faint jittered copies (density boost) so the diffuse BAND that follows the real
            // galaxy is visible from any location, not just near the core. Point sizes are kept small — a star is a
            // pinpoint; only the very brightest grow slightly (never a glowing orb).
            for (int i = 0; i < n; i++)
            {
                var s = galaxy.GetStar(i);
                if (s.Luminosity < 0f) continue;
                Vector3 rel = galaxy.GetStarWorld(i) - observer;
                float d2 = rel.sqrMagnitude;
                if (d2 < 1e-4f) continue;
                Vector3 dir = rel / Mathf.Sqrt(d2);
                float inten = Mathf.Max(s.Luminosity, 0.0006f) / Mathf.Max(d2, 0.25f) * starGain;
                Color bc = Astrophysics.BlackbodyColor(Mathf.Clamp(s.Temperature, 1500f, 40000f));
                Vector3 crgb = new Vector3(bc.r, bc.g, bc.b);
                AddPoint(verts, uvs, cols, tris, dir, crgb * inten);

                for (int k = 0; k < densityBoost; k++)
                {
                    Vector3 jd = (dir + new Vector3(rng.Value - 0.5f, rng.Value - 0.5f, rng.Value - 0.5f) * (2f * densityJitter)).normalized;
                    AddPoint(verts, uvs, cols, tris, jd, crgb * (inten * (0.12f + 0.35f * rng.Value)));
                }
            }

            // BULGE: a smooth diffuse glow in the true core direction (NOT a resolved ball of stars — a galactic
            // bulge reads as unresolved haze from within a system). Angular radius comes from the observer's distance
            // to the core, so it's a tight bright nucleus from afar and a broad glow near it. Layered soft discs.
            if (bulgeStars > 0 && galaxy != null)
            {
                float bulge = Mathf.Max(galaxy.BulgeSizeValue, galaxy.DiskRadiusValue * 0.08f);
                float coreDist = Mathf.Max(observer.magnitude, bulge * 0.5f);
                Vector3 cd = observer.sqrMagnitude > 1e-4f ? (-observer).normalized : Vector3.forward;
                float ang = Mathf.Clamp(Mathf.Atan(bulge * 1.5f / coreDist), 0.03f, 0.9f);
                Vector3 upv = Mathf.Abs(cd.y) < 0.9f ? Vector3.up : Vector3.right;
                Vector3 tx = Vector3.Normalize(Vector3.Cross(upv, cd));
                Vector3 ty = Vector3.Cross(cd, tx);
                Color warm = Astrophysics.BlackbodyColor(4600f);
                Vector3 wc = new Vector3(warm.r, warm.g, warm.b);

                // ONE faint haze disc for the unresolved glow (subtle — not a fog wash).
                AddGlowQuad(verts, uvs, cols, tris, cd, tx, ty, Mathf.Tan(ang), wc * (bulgeGain * 0.18f));

                // Dense STAR CLOUD that thins out smoothly (density AND brightness fall with radius → no ball edge).
                for (int i = 0; i < bulgeStars; i++)
                {
                    float rr = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-4f, 1f - rng.Value))) * 0.5f * ang;
                    float a = rng.Value * 2f * Mathf.PI;
                    Vector3 dir = (cd * Mathf.Cos(rr) + (tx * Mathf.Cos(a) + ty * Mathf.Sin(a)) * Mathf.Sin(rr)).normalized;
                    float falloff = Mathf.Exp(-(rr / ang) * (rr / ang) * 1.5f);
                    float b = Mathf.Pow(rng.Value, 2f) * bulgeGain * falloff;
                    Color sc = Astrophysics.BlackbodyColor(Mathf.Lerp(3500f, 7000f, rng.Value));
                    AddPoint(verts, uvs, cols, tris, dir, new Vector3(sc.r, sc.g, sc.b) * b);
                }
            }

            // Faint uniform fill so the anti-galaxy sky isn't pure black.
            for (int i = 0; i < backgroundStars; i++)
            {
                float z = rng.Value * 2f - 1f, az = rng.Value * 2f * Mathf.PI;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                Vector3 dir = new Vector3(r * Mathf.Cos(az), z, r * Mathf.Sin(az));
                float b = Mathf.Pow(rng.Value, 3.2f) * bgGain;
                Color bc = Astrophysics.BlackbodyColor(Mathf.Lerp(2800f, 13000f, Mathf.Pow(rng.Value, 2f)));
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
        }

        // Adds one star point. Size (in color.a) stays small — a star is a pinpoint; only the very brightest grow a
        // little, and never into a glowing orb.
        static void AddPoint(List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> t, Vector3 dir, Vector3 rgb)
        {
            float lum = Mathf.Max(rgb.x, Mathf.Max(rgb.y, rgb.z));
            float size = Mathf.Clamp(0.7f + 0.8f * Mathf.Sqrt(lum), 0.7f, 2.2f);
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
                // Sit just inside the (dynamically-changing) far clip so the sphere is NEVER frustum-clipped away,
                // yet stays behind all system geometry. Real geometry at real depth → ZTest works on every platform.
                float r = Mathf.Max(cam.farClipPlane * 0.9f, cam.nearClipPlane * 4f + 1f);
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
