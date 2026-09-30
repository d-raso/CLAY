using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Creates animated light shaft effects - beams of sunlight penetrating the water.
/// Best used in surface/shallow biomes like Tide Pool.
/// </summary>
public class LightShafts : MonoBehaviour
{
    [Header("Shaft Settings")]
    [Range(1, 20)]
    public int shaftCount = 6;

    [Tooltip("Width range of light shafts")]
    public Vector2 widthRange = new Vector2(2f, 6f);

    [Tooltip("Length/height of light shafts")]
    public float shaftLength = 40f;

    [Tooltip("Base alpha/intensity")]
    [Range(0f, 1f)]
    public float intensity = 0.15f;

    [Header("Color")]
    public Color shaftColor = new Color(1f, 0.98f, 0.9f, 1f);
    public Color shaftTip = new Color(1f, 1f, 0.95f, 0f);

    [Header("Animation")]
    [Tooltip("How much shafts sway side to side")]
    public float swayAmount = 2f;
    public float swaySpeed = 0.3f;

    [Tooltip("Intensity flicker")]
    [Range(0f, 0.5f)]
    public float flickerAmount = 0.2f;
    public float flickerSpeed = 1.5f;

    [Header("Spawn Area")]
    public float spawnWidth = 60f;
    public float spawnHeight = 20f; // Height above camera

    [Header("Depth Fade")]
    public bool fadeWithDepth = true;
    public float fadeStartY = 10f;
    public float fadeEndY = -30f;

    // Runtime
    private List<LightShaft> shafts = new List<LightShaft>();
    private Transform cameraTransform;
    private Material shaftMaterial;

    private class LightShaft
    {
        public GameObject gameObject;
        public SpriteRenderer renderer;
        public float baseX;
        public float width;
        public float swayOffset;
        public float flickerOffset;
    }

    void Start()
    {
        cameraTransform = Camera.main.transform;
        CreateShaftMaterial();
        CreateShafts();
    }

    void CreateShaftMaterial()
    {
        // Create a gradient texture for the shaft
        int width = 32;
        int height = 128;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < height; y++)
        {
            float vt = (float)y / (height - 1);
            // Fade from bottom (bright) to top (transparent)
            float alpha = 1f - vt;
            alpha = Mathf.Pow(alpha, 0.5f); // Softer falloff

            for (int x = 0; x < width; x++)
            {
                float xt = (float)x / (width - 1);
                // Soft edges horizontally
                float edgeFade = 1f - Mathf.Abs(xt - 0.5f) * 2f;
                edgeFade = Mathf.SmoothStep(0f, 1f, edgeFade);

                float finalAlpha = alpha * edgeFade;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, finalAlpha));
            }
        }
        tex.Apply();

        shaftMaterial = new Material(Shader.Find("Sprites/Default"));
        shaftMaterial.mainTexture = tex;
    }

    void CreateShafts()
    {
        for (int i = 0; i < shaftCount; i++)
        {
            CreateShaft(i);
        }
    }

    void CreateShaft(int index)
    {
        GameObject go = new GameObject($"LightShaft_{index}");
        go.transform.SetParent(transform);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();

        // Create sprite from material texture
        Texture2D tex = shaftMaterial.mainTexture as Texture2D;
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 1f), 1f);
        sr.color = shaftColor;
        sr.sortingOrder = -8; // Behind caustics, in front of far background

        // Random properties
        float width = Random.Range(widthRange.x, widthRange.y);
        float xPos = Random.Range(-spawnWidth * 0.5f, spawnWidth * 0.5f);

        LightShaft shaft = new LightShaft
        {
            gameObject = go,
            renderer = sr,
            baseX = xPos,
            width = width,
            swayOffset = Random.Range(0f, Mathf.PI * 2f),
            flickerOffset = Random.Range(0f, 100f)
        };

        // Scale
        go.transform.localScale = new Vector3(width, shaftLength, 1f);

        // Slight random rotation
        float rotation = Random.Range(-10f, 10f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, rotation);

        shafts.Add(shaft);
    }

    void Update()
    {
        if (cameraTransform == null) return;

        float time = Time.time;
        Vector3 cameraPos = cameraTransform.position;

        // Calculate depth fade
        float depthMultiplier = 1f;
        if (fadeWithDepth)
        {
            depthMultiplier = Mathf.InverseLerp(fadeEndY, fadeStartY, cameraPos.y);
            depthMultiplier = Mathf.Clamp01(depthMultiplier);
        }

        foreach (var shaft in shafts)
        {
            // Position relative to camera
            float sway = Mathf.Sin(time * swaySpeed + shaft.swayOffset) * swayAmount;
            float x = cameraPos.x + shaft.baseX + sway;
            float y = cameraPos.y + spawnHeight;

            shaft.gameObject.transform.position = new Vector3(x, y, 1f);

            // Flicker
            float flicker = 1f + (Mathf.PerlinNoise(time * flickerSpeed + shaft.flickerOffset, 0f) - 0.5f) * 2f * flickerAmount;

            // Apply intensity with depth fade and flicker
            Color color = shaftColor;
            color.a = intensity * depthMultiplier * flicker;
            shaft.renderer.color = color;
        }
    }

    /// <summary>
    /// Set intensity (useful for biome transitions)
    /// </summary>
    public void SetIntensity(float newIntensity)
    {
        intensity = Mathf.Clamp01(newIntensity);
    }

    /// <summary>
    /// Fade light shafts in/out
    /// </summary>
    public void FadeTo(float targetIntensity, float duration)
    {
        StartCoroutine(FadeRoutine(targetIntensity, duration));
    }

    System.Collections.IEnumerator FadeRoutine(float target, float duration)
    {
        float start = intensity;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            intensity = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }
        intensity = target;
    }

    /// <summary>
    /// Regenerate shafts with new count
    /// </summary>
    public void SetShaftCount(int count)
    {
        // Clear existing
        foreach (var shaft in shafts)
        {
            if (shaft.gameObject != null)
                Destroy(shaft.gameObject);
        }
        shafts.Clear();

        shaftCount = count;
        CreateShafts();
    }

    void OnDestroy()
    {
        if (shaftMaterial != null)
            Destroy(shaftMaterial);
    }
}
