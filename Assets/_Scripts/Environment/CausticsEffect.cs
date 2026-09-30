using UnityEngine;

/// <summary>
/// Creates animated caustic light patterns that overlay the scene.
/// Simulates light refracting through water surface onto the seafloor.
/// </summary>
public class CausticsEffect : MonoBehaviour
{
    [Header("Caustics Settings")]
    [Tooltip("Base intensity of the caustics effect")]
    [Range(0f, 1f)]
    public float intensity = 0.3f;

    [Tooltip("How fast the caustics animate")]
    public float animationSpeed = 0.5f;

    [Tooltip("Scale of the caustics pattern (smaller = more detailed)")]
    public float patternScale = 0.1f;

    [Tooltip("Secondary pattern scale for layered effect")]
    public float secondaryScale = 0.07f;

    [Header("Color")]
    public Color causticsColor = new Color(0.8f, 0.95f, 1f, 1f); // Slight blue tint

    [Header("Depth Fade")]
    [Tooltip("Caustics fade out in deeper/darker areas")]
    public bool fadeWithDepth = true;
    [Tooltip("Y position where caustics start fading")]
    public float fadeStartY = 0f;
    [Tooltip("Y position where caustics fully disappear")]
    public float fadeEndY = -50f;

    [Header("Flicker")]
    [Tooltip("Random intensity variation")]
    [Range(0f, 0.5f)]
    public float flickerAmount = 0.15f;
    public float flickerSpeed = 2f;

    [Header("References")]
    [Tooltip("Assign to limit caustics to specific area, or leave empty for full screen")]
    public Transform followTarget;

    [Header("Size")]
    public Vector2 effectSize = new Vector2(100f, 100f);

    // Runtime
    private Material causticsMaterial;
    private MeshRenderer meshRenderer;
    private float timeOffset;

    void Start()
    {
        CreateCausticsQuad();
        CreateCausticsMaterial();

        // Random start offset for variety
        timeOffset = Random.Range(0f, 100f);

        if (followTarget == null)
        {
            // Default to following camera
            followTarget = Camera.main.transform;
        }
    }

    void CreateCausticsQuad()
    {
        // Create a quad mesh to render caustics on
        MeshFilter mf = gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.AddComponent<MeshRenderer>();

        // Create simple quad mesh
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(0.5f, 0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0)
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };
        mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();

        mf.mesh = mesh;

        // Scale to effect size
        transform.localScale = new Vector3(effectSize.x, effectSize.y, 1f);

        // Set sorting order to render on top of background but below gameplay
        meshRenderer.sortingOrder = -5;
    }

    void CreateCausticsMaterial()
    {
        // Create material with custom shader
        Shader causticsShader = Shader.Find("Custom/Caustics");

        if (causticsShader == null)
        {
            // Fallback: create shader at runtime via shader code
            causticsShader = CreateCausticsShader();
        }

        if (causticsShader != null)
        {
            causticsMaterial = new Material(causticsShader);
        }
        else
        {
            // Ultimate fallback: use transparent sprite shader
            causticsMaterial = new Material(Shader.Find("Sprites/Default"));
            Debug.LogWarning("CausticsEffect: Could not find caustics shader, using fallback");
        }

        meshRenderer.material = causticsMaterial;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    Shader CreateCausticsShader()
    {
        // This creates a runtime shader - but Unity doesn't support this well
        // Instead, we'll use a procedural approach in Update()
        // Return null to use fallback
        return null;
    }

    void Update()
    {
        if (causticsMaterial == null) return;

        // Follow target (usually camera)
        if (followTarget != null)
        {
            Vector3 pos = followTarget.position;
            pos.z = 5f; // In front of far background, behind gameplay
            transform.position = pos;
        }

        // Calculate animated caustics values
        float time = Time.time * animationSpeed + timeOffset;

        // Flicker
        float flicker = 1f + Mathf.PerlinNoise(time * flickerSpeed, 0) * flickerAmount * 2f - flickerAmount;

        // Depth fade
        float depthMultiplier = 1f;
        if (fadeWithDepth && followTarget != null)
        {
            float y = followTarget.position.y;
            depthMultiplier = Mathf.InverseLerp(fadeEndY, fadeStartY, y);
            depthMultiplier = Mathf.Clamp01(depthMultiplier);
        }

        float finalIntensity = intensity * flicker * depthMultiplier;

        // Update material
        Color color = causticsColor;
        color.a = finalIntensity;
        causticsMaterial.color = color;

        // Pass time to shader for animation (if shader supports it)
        if (causticsMaterial.HasProperty("_Time"))
        {
            causticsMaterial.SetFloat("_Time", time);
        }

        // Generate procedural caustics texture
        UpdateCausticsTexture(time);
    }

    // Procedural caustics texture
    private Texture2D causticsTexture;
    private Color[] pixelBuffer;
    private int textureSize = 128;
    private float lastTextureUpdate;
    private float textureUpdateInterval = 0.033f; // ~30 FPS for texture updates

    void UpdateCausticsTexture(float time)
    {
        // Throttle texture updates for performance
        if (Time.time - lastTextureUpdate < textureUpdateInterval)
            return;
        lastTextureUpdate = Time.time;

        // Create texture if needed
        if (causticsTexture == null)
        {
            causticsTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            causticsTexture.wrapMode = TextureWrapMode.Repeat;
            causticsTexture.filterMode = FilterMode.Bilinear;
            pixelBuffer = new Color[textureSize * textureSize];
        }

        // Generate caustics pattern using layered noise
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float u = (float)x / textureSize;
                float v = (float)y / textureSize;

                // Layer 1: Primary caustics
                float caustic1 = CalculateCausticValue(u, v, time, patternScale);

                // Layer 2: Secondary caustics (different speed and scale)
                float caustic2 = CalculateCausticValue(u, v, time * 0.7f + 50f, secondaryScale);

                // Combine layers
                float combined = (caustic1 + caustic2) * 0.5f;

                // Sharpen the caustics (make the bright spots brighter)
                combined = Mathf.Pow(combined, 0.7f);

                pixelBuffer[y * textureSize + x] = new Color(combined, combined, combined, combined);
            }
        }

        causticsTexture.SetPixels(pixelBuffer);
        causticsTexture.Apply();

        causticsMaterial.mainTexture = causticsTexture;
    }

    float CalculateCausticValue(float u, float v, float time, float scale)
    {
        // Use multiple overlapping sine waves to create caustic-like pattern
        float x = u / scale;
        float y = v / scale;

        // Wave 1
        float wave1 = Mathf.Sin(x * 2.3f + time) * Mathf.Cos(y * 2.7f + time * 0.8f);

        // Wave 2 (offset angle)
        float angle = 0.7f;
        float x2 = x * Mathf.Cos(angle) - y * Mathf.Sin(angle);
        float y2 = x * Mathf.Sin(angle) + y * Mathf.Cos(angle);
        float wave2 = Mathf.Sin(x2 * 3.1f + time * 1.2f) * Mathf.Cos(y2 * 2.9f + time * 0.9f);

        // Wave 3 (another angle)
        angle = 1.4f;
        float x3 = x * Mathf.Cos(angle) - y * Mathf.Sin(angle);
        float y3 = x * Mathf.Sin(angle) + y * Mathf.Cos(angle);
        float wave3 = Mathf.Sin(x3 * 2.5f + time * 0.7f) * Mathf.Cos(y3 * 3.3f + time * 1.1f);

        // Combine and normalize to 0-1
        float combined = (wave1 + wave2 + wave3) / 3f;
        combined = (combined + 1f) * 0.5f; // Normalize from -1,1 to 0,1

        // Create sharp bright lines (caustic effect)
        combined = Mathf.Pow(combined, 2f);
        combined = Mathf.Clamp01(combined * 2f);

        return combined;
    }

    /// <summary>
    /// Set intensity at runtime (useful for biome transitions)
    /// </summary>
    public void SetIntensity(float newIntensity)
    {
        intensity = Mathf.Clamp01(newIntensity);
    }

    /// <summary>
    /// Fade caustics in/out over time
    /// </summary>
    public void FadeTo(float targetIntensity, float duration)
    {
        StartCoroutine(FadeRoutine(targetIntensity, duration));
    }

    System.Collections.IEnumerator FadeRoutine(float targetIntensity, float duration)
    {
        float startIntensity = intensity;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            intensity = Mathf.Lerp(startIntensity, targetIntensity, elapsed / duration);
            yield return null;
        }

        intensity = targetIntensity;
    }

    void OnDestroy()
    {
        if (causticsTexture != null)
        {
            Destroy(causticsTexture);
        }
        if (causticsMaterial != null)
        {
            Destroy(causticsMaterial);
        }
    }
}
