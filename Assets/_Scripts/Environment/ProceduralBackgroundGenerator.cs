using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates procedural background elements for testing the parallax system.
/// Creates simple shapes (circles, blobs) on each layer to demonstrate depth.
/// </summary>
public class ProceduralBackgroundGenerator : MonoBehaviour
{
    [Header("References")]
    public ParallaxManager parallaxManager;

    [Header("Generation Area")]
    [Tooltip("Radius around player to generate content")]
    public float generationRadius = 200f;
    [Tooltip("How often to check for new areas")]
    public float updateInterval = 0.5f;

    [Header("Layer Content Settings")]
    public LayerGenerationSettings[] layerSettings = new LayerGenerationSettings[]
    {
        // Far Background - handled by InfiniteBackground; skip here
        new LayerGenerationSettings { layerIndex = 0, objectCount = 0,  sizeMin = 0f,   sizeMax = 0f,   baseColor = Color.clear },
        // Mid Background - large organic masses, mineral shelves
        new LayerGenerationSettings { layerIndex = 1, objectCount = 6,  sizeMin = 8f,   sizeMax = 25f,  baseColor = new Color(0.3f, 0.35f, 0.4f, 0.6f) },
        // Near Background - rocks, biofilm clumps
        new LayerGenerationSettings { layerIndex = 2, objectCount = 10, sizeMin = 2f,   sizeMax = 8f,   baseColor = new Color(0.4f, 0.5f, 0.45f, 0.75f) },
        // Play Area - skip
        new LayerGenerationSettings { layerIndex = 3, objectCount = 0,  sizeMin = 0f,   sizeMax = 0f,   baseColor = Color.white },
        // Foreground - debris, near-camera detail
        new LayerGenerationSettings { layerIndex = 4, objectCount = 12, sizeMin = 0.2f, sizeMax = 0.8f, baseColor = new Color(0.6f, 0.65f, 0.7f, 0.5f) },
        // Near Foreground
        new LayerGenerationSettings { layerIndex = 5, objectCount = 8,  sizeMin = 0.4f, sizeMax = 1.5f, baseColor = new Color(0.7f, 0.75f, 0.8f, 0.35f) },
    };

    [Header("Visual Style")]
    public Color environmentTint = new Color(0.3f, 0.5f, 0.4f); // Underwater green-blue
    [Range(0f, 1f)]
    public float colorVariation = 0.2f;

    [Header("Distortion Materials")]
    [Tooltip("Assign the pre-made distortion materials for each layer (from Assets/Materials/Environment/)")]
    public Material[] layerMaterials = new Material[6];

    [Header("Debug")]
    public bool regenerateOnStart = true;
    public bool showGenerationArea = false;

    // Runtime
    private Transform playerTransform;
    private HashSet<Vector2Int> generatedCells = new HashSet<Vector2Int>();
    private float cellSize = 20f;
    private float lastUpdateTime;

    [System.Serializable]
    public class LayerGenerationSettings
    {
        public int layerIndex;
        public int objectCount;
        public float sizeMin;
        public float sizeMax;
        public Color baseColor;
    }

    void Start()
    {
        if (parallaxManager == null)
        {
            parallaxManager = FindFirstObjectByType<ParallaxManager>();
        }

        // Find player
        var playerCell = FindFirstObjectByType<JellyBodyBuilder>();
        if (playerCell != null)
        {
            playerTransform = playerCell.transform;
        }
        else
        {
            playerTransform = Camera.main.transform;
        }

        if (regenerateOnStart)
        {
            GenerateAroundPosition(playerTransform.position);
        }
    }

    void Update()
    {
        if (Time.time - lastUpdateTime > updateInterval)
        {
            lastUpdateTime = Time.time;
            GenerateAroundPosition(playerTransform.position);
        }
    }

    /// <summary>
    /// Generate background content around a world position
    /// </summary>
    public void GenerateAroundPosition(Vector3 worldPos)
    {
        if (parallaxManager == null) return;

        // Determine which cells need content
        Vector2Int centerCell = WorldToCell(worldPos);
        int cellRadius = Mathf.CeilToInt(generationRadius / cellSize);

        for (int x = -cellRadius; x <= cellRadius; x++)
        {
            for (int y = -cellRadius; y <= cellRadius; y++)
            {
                Vector2Int cell = centerCell + new Vector2Int(x, y);

                if (!generatedCells.Contains(cell))
                {
                    GenerateCell(cell);
                    generatedCells.Add(cell);
                }
            }
        }
    }

    void GenerateCell(Vector2Int cell)
    {
        Vector2 cellWorldPos = CellToWorld(cell);
        System.Random rng = new System.Random(cell.x * 73856093 ^ cell.y * 19349663); // Deterministic seed

        foreach (var settings in layerSettings)
        {
            if (settings.objectCount <= 0) continue;

            var layerConfig = parallaxManager.GetLayer(settings.layerIndex);
            if (layerConfig == null || layerConfig.container == null) continue;

            for (int i = 0; i < settings.objectCount; i++)
            {
                // Random position within cell
                float x = cellWorldPos.x + (float)rng.NextDouble() * cellSize;
                float y = cellWorldPos.y + (float)rng.NextDouble() * cellSize;
                Vector3 pos = new Vector3(x, y, 0);

                // Size varies with world region — some areas have giant blobs, others tiny ones
                float regionScale = Mathf.PerlinNoise(x * 0.005f + settings.layerIndex, y * 0.005f + settings.layerIndex * 3f);
                float size = Mathf.Lerp(settings.sizeMin, settings.sizeMax * (0.5f + regionScale * 2f), (float)rng.NextDouble());

                // Create procedural sprite
                GameObject obj = CreateProceduralSprite(pos, size, settings, layerConfig, rng);
                obj.transform.SetParent(layerConfig.container);
            }
        }
    }

    GameObject CreateProceduralSprite(Vector3 position, float size, LayerGenerationSettings settings, ParallaxManager.LayerConfig layerConfig, System.Random rng)
    {
        GameObject obj = new GameObject("BG_Element");
        obj.transform.position = position;
        obj.transform.localScale = Vector3.one * size;

        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = CreateProceduralShape(settings.layerIndex, rng, position);
        sr.sortingOrder = layerConfig.sortingOrder + (int)(rng.NextDouble() * 5);

        // World-position noise drives regional color identity — nearby areas share a palette
        float wx = position.x, wy = position.y;
        float regionR = Mathf.PerlinNoise(wx * 0.01f, wy * 0.01f + 0f);
        float regionG = Mathf.PerlinNoise(wx * 0.01f + 17f, wy * 0.01f + 5f);
        float regionB = Mathf.PerlinNoise(wx * 0.01f + 31f, wy * 0.01f + 11f);
        Color regionTint = new Color(regionR, regionG, regionB);

        Color color = settings.baseColor;
        color.r += (float)(rng.NextDouble() - 0.5) * colorVariation;
        color.g += (float)(rng.NextDouble() - 0.5) * colorVariation;
        color.b += (float)(rng.NextDouble() - 0.5) * colorVariation;

        // Blend: base → environment tint → world-region tint
        color = Color.Lerp(color, environmentTint, 0.2f);
        color = Color.Lerp(color, regionTint, 0.45f);

        // Apply layer alpha
        color.a = settings.baseColor.a * layerConfig.alpha;

        sr.color = color;

        // Apply distortion material if assigned for this layer
        if (settings.layerIndex >= 0 && settings.layerIndex < layerMaterials.Length && layerMaterials[settings.layerIndex] != null)
        {
            sr.material = layerMaterials[settings.layerIndex];
        }

        // Random rotation for variety
        obj.transform.rotation = Quaternion.Euler(0, 0, (float)rng.NextDouble() * 360f);

        return obj;
    }

    // One unique sprite per object — varied organic shapes
    Sprite CreateProceduralShape(int layerIndex, System.Random rng, Vector3 worldPos)
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[size * size];

        // Pick shape type based on layer and world region
        int shapeType = rng.Next(0, 3); // 0=blob, 1=mineral/crystal, 2=biofilm patch

        float cx = size * 0.5f, cy = size * 0.5f, r = size * 0.45f;
        float noiseOffX = (float)rng.NextDouble() * 100f;
        float noiseOffY = (float)rng.NextDouble() * 100f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (float)x/size, v = (float)y/size;
            float dx = x - cx, dy = y - cy;
            float dist = Mathf.Sqrt(dx*dx + dy*dy);
            float angle = Mathf.Atan2(dy, dx);
            float alpha = 0f;

            if (shapeType == 0)
            {
                // Organic blob: circle with Perlin edge deformation
                float deform = Mathf.PerlinNoise(noiseOffX + Mathf.Cos(angle)*1.5f,
                                                  noiseOffY + Mathf.Sin(angle)*1.5f);
                float edgeR = r * (0.6f + deform * 0.55f);
                alpha = 1f - Mathf.SmoothStep(edgeR * 0.7f, edgeR, dist);
                // Interior texture
                float interior = Mathf.PerlinNoise(u*4+noiseOffX, v*4+noiseOffY);
                alpha *= (0.7f + interior * 0.3f);
            }
            else if (shapeType == 1)
            {
                // Crystal/mineral: Voronoi cells within circle
                float circMask = 1f - Mathf.SmoothStep(r*0.6f, r, dist);
                // Mini voronoi
                float d1=float.MaxValue;
                for(int k=0;k<6;k++)
                {
                    float px2 = size*(0.15f+(float)((rng.Next()%100)/100f)*0.7f);
                    float py2 = size*(0.15f+(float)((rng.Next()%100)/100f)*0.7f);
                    float dd = (x-px2)*(x-px2)+(y-py2)*(y-py2);
                    if(dd<d1) d1=dd;
                }
                float cellPat = Mathf.Clamp01(Mathf.Sqrt(d1)/20f);
                alpha = circMask * (0.4f + cellPat * 0.6f);
            }
            else
            {
                // Biofilm / irregular patch: FBM shape
                float fbmVal = 0f; float fa=0.5f;
                float fx2=u*3+noiseOffX, fy2=v*3+noiseOffY;
                for(int o=0;o<4;o++){fbmVal+=Mathf.PerlinNoise(fx2,fy2)*fa;fx2*=2.1f;fy2*=2.1f;fa*=0.5f;}
                alpha = Mathf.SmoothStep(0.35f, 0.65f, fbmVal) * (1f - Mathf.Clamp01(dist/r));
            }

            px[y*size+x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0,0,size,size), new Vector2(0.5f,0.5f), size);
    }

    Vector2Int WorldToCell(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / cellSize),
            Mathf.FloorToInt(worldPos.y / cellSize)
        );
    }

    Vector2 CellToWorld(Vector2Int cell)
    {
        return new Vector2(cell.x * cellSize, cell.y * cellSize);
    }

    /// <summary>
    /// Clear all generated content and regenerate
    /// </summary>
    [ContextMenu("Regenerate Background")]
    public void Regenerate()
    {
        if (parallaxManager != null)
        {
            parallaxManager.ClearAllLayers();
        }
        generatedCells.Clear();

        if (playerTransform != null)
        {
            GenerateAroundPosition(playerTransform.position);
        }
    }

    void OnDrawGizmos()
    {
        if (!showGenerationArea) return;

        Gizmos.color = new Color(0, 1, 0, 0.2f);
        Vector3 center = playerTransform != null ? playerTransform.position : transform.position;
        Gizmos.DrawWireSphere(center, generationRadius);
    }
}
