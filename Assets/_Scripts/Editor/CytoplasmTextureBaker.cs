#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Procedurally bakes the cytoplasm map set for the CLAY/CytoplasmLit2D material:
///   • Albedo  — green slime base with Voronoi organelles, nuclei blobs and FBM mottling, on a round
///               disc (matches the JellyMesh's polar UVs) with a feathered edge.
///   • Normal  — surface relief derived from a height field (organelles bulge) so Light2D shades it.
///   • Dissolve— organic noise mask (FBM × cellular) used by the dissolve/disintegration channel.
///
/// Run via menu: CLAY ▸ Generate Cytoplasm Textures. Outputs PNGs to Assets/Materials/Cell/Generated/.
/// Deterministic (seeded) so re-runs are stable; change the seed for a different cell look.
/// </summary>
public static class CytoplasmTextureBaker
{
    const int   Size    = 512;
    const int   Seed    = 1337;
    const string OutDir = "Assets/Materials/Cell/Generated";

    [MenuItem("CLAY/Generate Cytoplasm Textures")]
    public static void Generate()
    {
        Directory.CreateDirectory(OutDir);

        // Shared fields so albedo / normal / organelles line up.
        var height = new float[Size, Size];   // 0..1 relief used for both albedo shading and normals
        var organ  = new float[Size, Size];   // organelle intensity (Voronoi)
        var disc   = new float[Size, Size];   // 1 inside the cell, feathered at the rim

        var rng = new System.Random(Seed);
        // Scatter organelle / nucleus centres.
        Vector2[] cells   = ScatterPoints(28, rng);
        Vector2[] nuclei  = ScatterPoints(4,  rng);

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
            Vector2 p = new Vector2(u, v);

            // Round disc with feathered edge (polar UV: centre→centre, edge→rim).
            float r = Vector2.Distance(p, new Vector2(0.5f, 0.5f)) * 2f; // 0 centre … 1 rim
            disc[x, y] = 1f - SStep(0.9f, 1.0f, r);

            // Voronoi F1 → bubbly organelle packing.
            float f1 = VoronoiF1(p, cells);
            float bubble = SStep(0.18f, 0.0f, f1); // bright near a cell centre
            organ[x, y] = bubble;

            // Big soft nuclei.
            float nuc = 0f;
            foreach (var n in nuclei)
            {
                float d = Vector2.Distance(p, n);
                nuc = Mathf.Max(nuc, SStep(0.16f, 0.0f, d));
            }

            // FBM mottling for fine variation.
            float fbm = Fbm(p * 6f, 5);

            // Relief: organelles bulge, nuclei bulge more, plus mottle; flattened by the disc edge.
            height[x, y] = Mathf.Clamp01((0.35f + 0.45f * bubble + 0.5f * nuc + 0.25f * (fbm - 0.5f)) * disc[x, y]);
        }

        BakeAlbedo(height, organ, disc, nuclei);
        BakeNormal(height);
        BakeDissolve(rng);

        AssetDatabase.Refresh();
        BuildMaterial();
        Debug.Log($"[CytoplasmTextureBaker] Wrote albedo / normal / dissolve + material to {OutDir}");
        EditorUtility.RevealInFinder(OutDir);
    }

    /// <summary>Create (or refresh) a CLAY/CytoplasmLit2D material with the generated maps wired in.</summary>
    static void BuildMaterial()
    {
        // The shader may have just been added this session — make sure it's imported before Find.
        var guids = AssetDatabase.FindAssets("CytoplasmLit2D t:Shader");
        if (guids.Length > 0) AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guids[0]), ImportAssetOptions.ForceSynchronousImport);

        var shader = Shader.Find("CLAY/CytoplasmLit2D");
        if (shader == null)
        {
            Debug.LogError("[CytoplasmTextureBaker] Shader 'CLAY/CytoplasmLit2D' not found/compiled yet. " +
                           "Re-run this menu item once Unity finishes importing the shader.");
            return;
        }

        var albedo   = AssetDatabase.LoadAssetAtPath<Texture2D>($"{OutDir}/cytoplasm_albedo.png");
        var normal   = AssetDatabase.LoadAssetAtPath<Texture2D>($"{OutDir}/cytoplasm_normal.png");
        var dissolve = AssetDatabase.LoadAssetAtPath<Texture2D>($"{OutDir}/cytoplasm_dissolve.png");
        if (albedo == null || normal == null || dissolve == null)
            Debug.LogError($"[CytoplasmTextureBaker] Missing generated map(s): albedo={albedo}, normal={normal}, dissolve={dissolve}");

        string matPath = $"{OutDir}/CytoplasmLit2D.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
        mat.shader = shader;
        mat.SetColor("_Color", Color.white);
        mat.SetTexture("_MainTex",     albedo);
        mat.SetTexture("_NormalMap",   normal);
        mat.SetTexture("_DissolveTex", dissolve);
        mat.SetFloat("_Dissolve", 0f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CytoplasmTextureBaker] Material ready at {matPath} — albedo wired: {albedo != null}. " +
                  "Assign it to the cell's CytoplasmMesh MeshRenderer.");
    }

    // ── maps ───────────────────────────────────────────────────────────────────────────────────

    static void BakeAlbedo(float[,] height, float[,] organ, float[,] disc, Vector2[] nuclei)
    {
        Color deep   = new Color(0.10f, 0.30f, 0.12f);  // shadowed cytoplasm
        Color mid    = new Color(0.20f, 0.55f, 0.24f);  // base slime
        Color bright = new Color(0.45f, 0.95f, 0.50f);  // lit organelle highlight
        Color nucCol = new Color(0.32f, 0.70f, 0.85f);  // teal nucleus, for contrast

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float h = height[x, y];
            Color c = Color.Lerp(deep, mid, Mathf.Clamp01(h * 1.3f));
            c = Color.Lerp(c, bright, organ[x, y] * 0.8f);

            float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
            float nuc = 0f;
            foreach (var n in nuclei) nuc = Mathf.Max(nuc, SStep(0.16f, 0.02f, Vector2.Distance(new Vector2(u, v), n)));
            c = Color.Lerp(c, nucCol, nuc * 0.6f);

            tex.SetPixel(x, y, new Color(c.r, c.g, c.b, disc[x, y]));
        }
        tex.Apply();
        WritePng(tex, "cytoplasm_albedo.png", isNormal: false, alphaTransparency: true);
    }

    static void BakeNormal(float[,] height)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        const float strength = 3.0f;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            // Sobel-ish central differences on the height field.
            float hl = height[Mathf.Max(x - 1, 0), y];
            float hr = height[Mathf.Min(x + 1, Size - 1), y];
            float hd = height[x, Mathf.Max(y - 1, 0)];
            float hu = height[x, Mathf.Min(y + 1, Size - 1)];
            Vector3 n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
            tex.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
        }
        tex.Apply();
        WritePng(tex, "cytoplasm_normal.png", isNormal: true, alphaTransparency: false);
    }

    static void BakeDissolve(System.Random rng)
    {
        Vector2[] cracks = ScatterPoints(40, rng);
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
            Vector2 p = new Vector2(u, v);
            // Cellular crack pattern + fbm so the cell dissolves in organic chunks, not a flat wipe.
            float cell = VoronoiF1(p, cracks) * 3.5f;
            float fbm = Fbm(p * 5f, 4);
            float n = Mathf.Clamp01(cell * 0.6f + fbm * 0.6f);
            // Bias so the rim dissolves slightly before the core (reads as eaten from the edge in).
            float r = Vector2.Distance(p, new Vector2(0.5f, 0.5f)) * 2f;
            n = Mathf.Clamp01(n - r * 0.15f);
            tex.SetPixel(x, y, new Color(n, n, n, 1f));
        }
        tex.Apply();
        WritePng(tex, "cytoplasm_dissolve.png", isNormal: false, alphaTransparency: false);
    }

    // ── io ───────────────────────────────────────────────────────────────────────────────────

    static void WritePng(Texture2D tex, string file, bool isNormal, bool alphaTransparency)
    {
        string path = $"{OutDir}/{file}";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.alphaIsTransparency = alphaTransparency;
        imp.mipmapEnabled = true;
        imp.SaveAndReimport();
    }

    // ── procedural noise ───────────────────────────────────────────────────────────────────────

    /// <summary>HLSL-style smoothstep (edge0,edge1,x) — NOT Unity's Mathf.SmoothStep, which blends by t.</summary>
    static float SStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    static Vector2[] ScatterPoints(int count, System.Random rng)
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
            // Wrap so the pattern tiles seamlessly.
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
        float a = Hash(i);
        float b = Hash(i + new Vector2(1, 0));
        float c = Hash(i + new Vector2(0, 1));
        float d = Hash(i + new Vector2(1, 1));
        return Mathf.Lerp(Mathf.Lerp(a, b, w.x), Mathf.Lerp(c, d, w.x), w.y);
    }

    static float Fbm(Vector2 p, int octaves)
    {
        float sum = 0f, amp = 0.5f;
        for (int o = 0; o < octaves; o++) { sum += ValueNoise(p) * amp; p *= 2f; amp *= 0.5f; }
        return Mathf.Clamp01(sum);
    }
}
#endif
