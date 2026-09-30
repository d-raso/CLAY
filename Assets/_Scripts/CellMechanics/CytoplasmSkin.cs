using UnityEngine;

/// <summary>
/// Generates the cytoplasm albedo + dissolve textures procedurally AT RUNTIME and assigns them to the
/// cell's CytoplasmMesh material — no Editor baking, no manual texture wiring. Add this to the cell
/// (same object as CellBiomass / JellyBodyBuilder).
///
/// Works with any material that exposes <c>_MainTex</c> (and optionally <c>_DissolveTex</c>) — e.g. the
/// CLAY/CytoplasmLit2D shader or the default sprite shaders. The textures are generated once and shared
/// across every cell (cheap). The per-cell dissolve animation is driven separately by CellBiomass via a
/// MaterialPropertyBlock, so sharing the material is fine.
/// </summary>
public class CytoplasmSkin : MonoBehaviour
{
    [Tooltip("Texture resolution. 256 is plenty for a wobbly blob; 512 for crisper organelles.")]
    public int resolution = 512;
    [Tooltip("Seed for the organelle/noise pattern.")]
    public int seed = 1337;
    [Tooltip("Re-tint the generated albedo by this (white = use generated greens as-is).")]
    public Color tint = Color.white;

    static Texture2D _albedo, _dissolve;
    static int _builtSeed = int.MinValue, _builtRes;

    void Start()
    {
        var jelly = GetComponentInChildren<JellyMesh>();
        var mr = jelly != null ? jelly.GetComponent<MeshRenderer>() : GetComponentInChildren<MeshRenderer>();
        if (mr == null) { Debug.LogWarning("[CytoplasmSkin] No MeshRenderer found for the cytoplasm."); return; }

        EnsureTextures();

        // Assign onto the shared material once (all cells share the same maps).
        var mat = mr.sharedMaterial;
        if (mat == null) { Debug.LogWarning("[CytoplasmSkin] Cytoplasm MeshRenderer has no material."); return; }
        if (mat.HasProperty("_MainTex")     && mat.GetTexture("_MainTex") != _albedo)       mat.SetTexture("_MainTex", _albedo);
        if (mat.HasProperty("_DissolveTex") && mat.GetTexture("_DissolveTex") != _dissolve) mat.SetTexture("_DissolveTex", _dissolve);
        if (mat.HasProperty("_Color"))      mat.SetColor("_Color", tint);
    }

    void EnsureTextures()
    {
        if (_albedo != null && _builtSeed == seed && _builtRes == resolution) return;
        int n = Mathf.Clamp(resolution, 64, 1024);
        var rng = new System.Random(seed);

        Vector2[] cells  = Scatter(14, rng);  // fewer, larger organelles — less polka-dot
        Vector2[] nuclei = Scatter(3,  rng);
        Vector2[] cracks = Scatter(40, rng);

        // A cohesive, fairly light green so the whole disc reads as one cell — organelles are gentle
        // highlights, not stark dots on a dark base.
        Color baseGreen = new Color(0.30f, 0.62f, 0.34f);
        Color shadow    = new Color(0.18f, 0.42f, 0.22f);
        Color bright     = new Color(0.55f, 0.92f, 0.55f);
        Color nucCol     = new Color(0.40f, 0.72f, 0.80f);

        var alb = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var dis = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var aPix = new Color[n * n];
        var dPix = new Color[n * n];

        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            Vector2 p = new Vector2(u, v);
            float r = Vector2.Distance(p, new Vector2(0.5f, 0.5f)) * 2f; // 0 centre … 1 rim
            float disc = 1f - SStep(0.9f, 1.0f, r);

            float bubble = SStep(0.26f, 0.05f, VoronoiF1(p, cells)); // wider, softer organelles
            float nuc = 0f;
            foreach (var nn in nuclei) nuc = Mathf.Max(nuc, SStep(0.16f, 0.02f, Vector2.Distance(p, nn)));
            float fbm = Fbm(p * 6f, 5);

            // Cohesive green disc: gentle mottling + soft organelle highlights, not hard dots.
            Color c = Color.Lerp(baseGreen, shadow, (1f - fbm) * 0.30f);
            c = Color.Lerp(c, bright, bubble * 0.28f);   // gentle highlights, not stark dots
            c = Color.Lerp(c, nucCol, nuc * 0.4f);
            // Subtle radial shading so it reads as round/3D even without lights.
            c *= Mathf.Lerp(1f, 0.8f, r * r);
            aPix[y * n + x] = new Color(c.r, c.g, c.b, disc);

            float cell = VoronoiF1(p, cracks) * 3.5f;
            float dn = Mathf.Clamp01(cell * 0.6f + Fbm(p * 5f, 4) * 0.6f - r * 0.15f);
            dPix[y * n + x] = new Color(dn, dn, dn, 1f);
        }

        alb.SetPixels(aPix); alb.Apply();
        dis.SetPixels(dPix); dis.Apply();
        _albedo = alb; _dissolve = dis; _builtSeed = seed; _builtRes = n;
    }

    /// <summary>HLSL-style smoothstep (edge0,edge1,x) — NOT Unity's Mathf.SmoothStep, which blends by t.</summary>
    static float SStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    // ── procedural noise ───────────────────────────────────────────────────────────────────────
    static Vector2[] Scatter(int count, System.Random rng)
    {
        var pts = new Vector2[count];
        for (int i = 0; i < count; i++) pts[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
        return pts;
    }

    static float VoronoiF1(Vector2 p, Vector2[] pts)
    {
        float best = 999f;
        foreach (var q in pts)
        {
            Vector2 d = p - q;
            d.x -= Mathf.Round(d.x); d.y -= Mathf.Round(d.y);
            best = Mathf.Min(best, d.magnitude);
        }
        return best;
    }

    static float Hash(Vector2 c)
    {
        float h = Mathf.Sin(Vector2.Dot(c, new Vector2(127.1f, 311.7f))) * 43758.5453f;
        return h - Mathf.Floor(h);
    }

    static float ValueNoise(Vector2 p)
    {
        Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
        Vector2 f = p - i;
        Vector2 w = new Vector2(f.x * f.x * (3 - 2 * f.x), f.y * f.y * (3 - 2 * f.y));
        float a = Hash(i), b = Hash(i + new Vector2(1, 0)), c = Hash(i + new Vector2(0, 1)), d = Hash(i + new Vector2(1, 1));
        return Mathf.Lerp(Mathf.Lerp(a, b, w.x), Mathf.Lerp(c, d, w.x), w.y);
    }

    static float Fbm(Vector2 p, int octaves)
    {
        float sum = 0f, amp = 0.5f;
        for (int o = 0; o < octaves; o++) { sum += ValueNoise(p) * amp; p *= 2f; amp *= 0.5f; }
        return Mathf.Clamp01(sum);
    }
}
