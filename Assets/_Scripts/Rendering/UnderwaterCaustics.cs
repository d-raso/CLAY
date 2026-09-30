using UnityEngine;

/// <summary>
/// Creates animated caustic light patterns on underwater surfaces.
/// Uses procedural Voronoi-based pattern generation.
/// Attach to a quad/sprite renderer behind your gameplay layer.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class UnderwaterCaustics : MonoBehaviour
{
    [Header("Appearance")]
    [Range(0f, 1f)]
    public float intensity = 0.3f;
    public Color causticsColor = new Color(0.8f, 0.95f, 1f, 0.3f);
    [Range(0.5f, 5f)]
    public float scale = 2f;

    [Header("Animation")]
    [Range(0f, 2f)]
    public float speed = 0.5f;
    [Range(0f, 1f)]
    public float distortion = 0.3f;

    [Header("Layers")]
    [Range(1, 3)]
    public int layers = 2;
    public float layerOffset = 0.3f;

    [Header("Light Direction")]
    public Vector2 lightDirection = new Vector2(0.2f, -0.8f);
    [Range(0f, 1f)]
    public float directionalBias = 0.3f;

    private SpriteRenderer spriteRenderer;
    private Material causticsMaterial;
    private static Shader causticsShader;

    // Shader property IDs
    private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    private static readonly int CausticsColorID = Shader.PropertyToID("_CausticsColor");
    private static readonly int ScaleID = Shader.PropertyToID("_Scale");
    private static readonly int SpeedID = Shader.PropertyToID("_Speed");
    private static readonly int DistortionID = Shader.PropertyToID("_Distortion");
    private static readonly int LayersID = Shader.PropertyToID("_Layers");
    private static readonly int LayerOffsetID = Shader.PropertyToID("_LayerOffset");
    private static readonly int LightDirectionID = Shader.PropertyToID("_LightDirection");
    private static readonly int DirectionalBiasID = Shader.PropertyToID("_DirectionalBias");
    private static readonly int TimeID = Shader.PropertyToID("_CausticsTime");

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        CreateMaterial();
    }

    void CreateMaterial()
    {
        if (causticsShader == null)
        {
            causticsShader = Shader.Find("Custom/UnderwaterCaustics");
        }

        if (causticsShader == null)
        {
            Debug.LogWarning("UnderwaterCaustics: Shader not found. Using fallback.");
            // Create a procedural caustics texture as fallback
            CreateProceduralCaustics();
            return;
        }

        causticsMaterial = new Material(causticsShader);
        spriteRenderer.material = causticsMaterial;
    }

    void CreateProceduralCaustics()
    {
        // Fallback: Create a simple animated material using standard shader
        causticsMaterial = new Material(Shader.Find("Sprites/Default"));
        causticsMaterial.color = causticsColor;
        spriteRenderer.material = causticsMaterial;

        // Create caustics texture procedurally
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Repeat;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;

                // Simple Voronoi-like pattern
                float value = VoronoiNoise(u * 4f, v * 4f);
                value = Mathf.Pow(value, 3f); // Sharpen edges

                pixels[y * size + x] = new Color(1f, 1f, 1f, value * 0.5f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(true);
        spriteRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
    }

    float VoronoiNoise(float x, float y)
    {
        int xi = Mathf.FloorToInt(x);
        int yi = Mathf.FloorToInt(y);
        float xf = x - xi;
        float yf = y - yi;

        float minDist = 1f;

        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                // Random point in cell
                float px = Hash(xi + i, yi + j, 0);
                float py = Hash(xi + i, yi + j, 1);

                float dx = i + px - xf;
                float dy = j + py - yf;
                float dist = dx * dx + dy * dy;

                minDist = Mathf.Min(minDist, dist);
            }
        }

        return 1f - Mathf.Sqrt(minDist);
    }

    float Hash(int x, int y, int seed)
    {
        int n = x + y * 57 + seed * 131;
        n = (n << 13) ^ n;
        return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / (float)0x7fffffff;
    }

    void Update()
    {
        if (causticsMaterial == null) return;

        // Update shader parameters
        causticsMaterial.SetFloat(IntensityID, intensity);
        causticsMaterial.SetColor(CausticsColorID, causticsColor);
        causticsMaterial.SetFloat(ScaleID, scale);
        causticsMaterial.SetFloat(SpeedID, speed);
        causticsMaterial.SetFloat(DistortionID, distortion);
        causticsMaterial.SetInt(LayersID, layers);
        causticsMaterial.SetFloat(LayerOffsetID, layerOffset);
        causticsMaterial.SetVector(LightDirectionID, lightDirection.normalized);
        causticsMaterial.SetFloat(DirectionalBiasID, directionalBias);
        causticsMaterial.SetFloat(TimeID, Time.time);

        // Animate UV offset for fallback
        if (causticsShader == null)
        {
            Vector2 offset = new Vector2(
                Mathf.Sin(Time.time * speed * 0.7f) * 0.1f,
                Mathf.Cos(Time.time * speed) * 0.1f
            );
            causticsMaterial.mainTextureOffset = offset;
            causticsMaterial.color = new Color(causticsColor.r, causticsColor.g, causticsColor.b, intensity);
        }
    }

    /// <summary>
    /// Set intensity at runtime (useful for depth changes)
    /// </summary>
    public void SetIntensity(float newIntensity)
    {
        intensity = Mathf.Clamp01(newIntensity);
    }

    void OnValidate()
    {
        if (Application.isPlaying && causticsMaterial != null)
        {
            Update();
        }
    }
}
