using UnityEngine;

/// <summary>
/// Infinite tiling background. Tiles reposition around the camera.
/// Generates rich Voronoi + FBM procedural textures that look organic / underwater.
/// </summary>
public class InfiniteBackground : MonoBehaviour
{
    [Header("Tiling")]
    public Vector2 tileSize = new Vector2(50f, 50f);
    public float parallaxMultiplier = 0.05f;

    [Header("Base Colors (overridden by PlanetaryEnvironment)")]
    public Color topColor    = new Color(0.12f, 0.22f, 0.28f);
    public Color bottomColor = new Color(0.04f, 0.08f, 0.14f);

    [Header("Texture")]
    [Tooltip("Resolution of the procedural texture (power of 2)")]
    public int textureResolution = 128;
    [Range(0f,1f)]
    public float patternContrast = 0.55f;
    [Tooltip("Seed changes the Voronoi point layout")]
    public int textureSeed = 42;

    private Transform      cam;
    private Vector3        prevCamPos;
    private GameObject[,]  tiles   = new GameObject[3, 3];
    private SpriteRenderer[,] srs  = new SpriteRenderer[3, 3];
    private Texture2D      noiseTex;
    private Sprite         noiseSprite;

    void Start()
    {
        cam = Camera.main.transform;
        prevCamPos = cam.position;
        RegenerateTexture();
        CreateTiles();
        SnapTiles();
    }

    // ── Texture generation ──────────────────────────────────────────────────

    void RegenerateTexture()
    {
        int res = textureResolution;
        noiseTex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };

        Color[] px = new Color[res * res];

        // Generate Voronoi seed points
        System.Random rng = new System.Random(textureSeed);
        const int numPts = 18;
        Vector2[] pts = new Vector2[numPts];
        for (int k = 0; k < numPts; k++)
            pts[k] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());

        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float u = (float)x / res;
            float v = (float)y / res;

            // Toroidal Voronoi distance (wraps so tiles are seamless)
            float d1 = float.MaxValue, d2 = float.MaxValue;
            for (int k = 0; k < numPts; k++)
            {
                float dx = WrappedDist(u, pts[k].x);
                float dy = WrappedDist(v, pts[k].y);
                float d  = dx*dx + dy*dy;
                if (d < d1) { d2 = d1; d1 = d; }
                else if (d < d2) { d2 = d; }
            }
            float cell = Mathf.Sqrt(d1) * 6f;          // interior dist
            float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1); // edge proximity

            // FBM overlay for organic detail
            float fbmVal = FBM(u * 3.5f, v * 3.5f, 4);

            // Combine: bright interior, darker edges, noise overlay
            float pattern = Mathf.Clamp01(cell * 0.7f + fbmVal * 0.3f);
            float edgeMask = Mathf.Clamp01(edge * 12f); // 0 = on edge
            pattern = Mathf.Lerp(pattern * 0.3f, pattern, edgeMask);

            // Vertical gradient
            float gradT = (float)y / (res - 1);
            Color baseCol = Color.Lerp(bottomColor, topColor, gradT);

            // Tint by pattern — brighter regions = slightly lighter variation of base
            float bright = Mathf.Lerp(1f - patternContrast, 1f + patternContrast * 0.4f, pattern);
            Color final = new Color(
                Mathf.Clamp01(baseCol.r * bright),
                Mathf.Clamp01(baseCol.g * bright),
                Mathf.Clamp01(baseCol.b * bright),
                1f);

            px[y * res + x] = final;
        }

        noiseTex.SetPixels(px);
        noiseTex.Apply();
        noiseSprite = Sprite.Create(noiseTex, new Rect(0,0,res,res), new Vector2(0.5f,0.5f), res / tileSize.x);
    }

    static float WrappedDist(float a, float b)
    {
        float d = Mathf.Abs(a - b);
        return Mathf.Min(d, 1f - d);
    }

    static float Noise(float x, float y)
    {
        // Cheap but seamless-tileable value noise
        return Mathf.PerlinNoise(x, y) * 2f - 1f;
    }

    static float FBM(float x, float y, int octaves)
    {
        float v=0, a=0.5f;
        for (int i=0; i<octaves; i++)
        {
            v += Noise(x, y) * a;
            x *= 2.1f; y *= 2.1f; a *= 0.5f;
        }
        return v * 0.5f + 0.5f;
    }

    // ── Tile management ──────────────────────────────────────────────────────

    void CreateTiles()
    {
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 3; y++)
        {
            var go = new GameObject($"BG_{x}_{y}");
            go.transform.SetParent(transform);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = noiseSprite;
            sr.sortingOrder = -1000;
            sr.drawMode     = SpriteDrawMode.Tiled;
            sr.size         = tileSize;
            tiles[x,y] = go;
            srs[x,y]   = sr;
        }
    }

    void SnapTiles()
    {
        Vector3 c = cam.position;
        float cx = Mathf.Floor(c.x / tileSize.x) * tileSize.x;
        float cy = Mathf.Floor(c.y / tileSize.y) * tileSize.y;
        for (int x=0;x<3;x++)
        for (int y=0;y<3;y++)
            tiles[x,y].transform.position = new Vector3(cx+(x-1)*tileSize.x, cy+(y-1)*tileSize.y, 10f);
    }

    void LateUpdate()
    {
        if (cam == null) return;
        Vector3 delta = cam.position - prevCamPos;
        float px = delta.x * (1f - parallaxMultiplier);
        float py = delta.y * (1f - parallaxMultiplier);
        foreach (var t in tiles) t.transform.position += new Vector3(px, py, 0f);

        Vector3 cp = cam.position, cp0 = tiles[1,1].transform.position;
        if (Mathf.Abs(cp.x-cp0.x) > tileSize.x || Mathf.Abs(cp.y-cp0.y) > tileSize.y)
            SnapTiles();

        prevCamPos = cam.position;
    }

    /// <summary>Update colors and regenerate the texture at runtime.</summary>
    public void SetGradientColors(Color top, Color bottom)
    {
        topColor = top; bottomColor = bottom;
        RegenerateTexture();
        foreach (var sr in srs) if (sr) sr.sprite = noiseSprite;
    }
}
