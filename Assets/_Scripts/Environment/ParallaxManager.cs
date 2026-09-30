using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages the 6-layer parallax system for creating depth in the 2D world.
/// Attach to an empty GameObject in the scene, then assign or auto-generate layers.
/// </summary>
public class ParallaxManager : MonoBehaviour
{
    [System.Serializable]
    public class LayerConfig
    {
        public string layerName;
        [Tooltip("0.1 = far background, 1.0 = play area, 1.5 = near foreground")]
        public float parallaxMultiplier;
        [Tooltip("Sorting order for rendering")]
        public int sortingOrder;
        [Tooltip("Objects that can spawn on this layer")]
        public GameObject[] prefabs;
        [Tooltip("How many objects to spawn per chunk")]
        public float spawnDensity;
        [Tooltip("Scale range for spawned objects")]
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
        [Tooltip("Color tint for depth effect")]
        public Color tint = Color.white;
        [Tooltip("Alpha/transparency for depth fade")]
        [Range(0f, 1f)]
        public float alpha = 1f;

        [HideInInspector]
        public Transform container; // Runtime reference to layer container
    }

    [Header("Layer Definitions")]
    public LayerConfig[] layers = new LayerConfig[]
    {
        new LayerConfig { layerName = "FarBackground",   parallaxMultiplier = 0.1f, sortingOrder = -100, alpha = 0.4f, tint = new Color(0.5f, 0.6f, 0.7f) },
        new LayerConfig { layerName = "MidBackground",   parallaxMultiplier = 0.4f, sortingOrder = -50,  alpha = 0.6f, tint = new Color(0.7f, 0.75f, 0.8f) },
        new LayerConfig { layerName = "NearBackground",  parallaxMultiplier = 0.7f, sortingOrder = -20,  alpha = 0.8f, tint = new Color(0.85f, 0.88f, 0.9f) },
        new LayerConfig { layerName = "PlayArea",        parallaxMultiplier = 1.0f, sortingOrder = 0,    alpha = 1f,   tint = Color.white },
        new LayerConfig { layerName = "Foreground",      parallaxMultiplier = 1.2f, sortingOrder = 50,   alpha = 0.7f, tint = new Color(0.9f, 0.92f, 0.95f) },
        new LayerConfig { layerName = "NearForeground",  parallaxMultiplier = 1.5f, sortingOrder = 100,  alpha = 0.5f, tint = new Color(0.95f, 0.97f, 1f) },
    };

    [Header("Generation Settings")]
    [Tooltip("Size of area to populate around camera")]
    public float generationRadius = 50f;
    [Tooltip("How often to check for new areas to populate (seconds)")]
    public float updateInterval = 0.5f;

    [Header("Debug")]
    public bool showLayerGizmos = false;

    // Runtime
    private Dictionary<string, LayerConfig> layerLookup = new Dictionary<string, LayerConfig>();
    private Transform cameraTransform;

    void Awake()
    {
        CreateLayerContainers();
        BuildLayerLookup();
    }

    void Start()
    {
        cameraTransform = Camera.main.transform;
    }

    /// <summary>
    /// Creates empty GameObjects to hold each layer's content
    /// </summary>
    void CreateLayerContainers()
    {
        for (int i = 0; i < layers.Length; i++)
        {
            LayerConfig config = layers[i];

            // Create container for this layer
            GameObject container = new GameObject($"Layer_{i}_{config.layerName}");
            container.transform.SetParent(transform);
            container.transform.localPosition = Vector3.zero;

            // Add parallax component
            ParallaxLayer parallax = container.AddComponent<ParallaxLayer>();
            parallax.parallaxMultiplier = config.parallaxMultiplier;
            parallax.sortingOrder = config.sortingOrder;

            config.container = container.transform;
        }
    }

    void BuildLayerLookup()
    {
        layerLookup.Clear();
        foreach (var config in layers)
        {
            layerLookup[config.layerName] = config;
        }
    }

    /// <summary>
    /// Get a layer config by name
    /// </summary>
    public LayerConfig GetLayer(string layerName)
    {
        return layerLookup.TryGetValue(layerName, out var config) ? config : null;
    }

    /// <summary>
    /// Get a layer config by index
    /// </summary>
    public LayerConfig GetLayer(int index)
    {
        if (index >= 0 && index < layers.Length)
            return layers[index];
        return null;
    }

    /// <summary>
    /// Spawn an object on a specific layer
    /// </summary>
    public GameObject SpawnOnLayer(string layerName, GameObject prefab, Vector3 worldPosition, float scale = 1f)
    {
        LayerConfig config = GetLayer(layerName);
        if (config == null || config.container == null)
        {
            Debug.LogWarning($"ParallaxManager: Layer '{layerName}' not found");
            return null;
        }

        return SpawnOnLayer(config, prefab, worldPosition, scale);
    }

    /// <summary>
    /// Spawn an object on a specific layer (by config)
    /// </summary>
    public GameObject SpawnOnLayer(LayerConfig config, GameObject prefab, Vector3 worldPosition, float scale = 1f)
    {
        if (prefab == null) return null;

        GameObject instance = Instantiate(prefab, worldPosition, Quaternion.identity, config.container);
        instance.transform.localScale = Vector3.one * scale;

        // Apply layer visual settings
        SpriteRenderer sr = instance.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingOrder = config.sortingOrder;

            // Apply tint and alpha
            Color color = config.tint;
            color.a = config.alpha;
            sr.color = color;

            // Apply environment distortion material if available
            if (EnvironmentMaterialManager.Instance != null)
            {
                int layerIndex = System.Array.IndexOf(layers, config);
                if (layerIndex >= 0)
                {
                    EnvironmentMaterialManager.Instance.ApplyToSprite(sr, layerIndex);
                }
            }
        }

        return instance;
    }

    /// <summary>
    /// Spawn a random prefab from the layer's prefab list
    /// </summary>
    public GameObject SpawnRandomOnLayer(string layerName, Vector3 worldPosition)
    {
        LayerConfig config = GetLayer(layerName);
        if (config == null || config.prefabs == null || config.prefabs.Length == 0)
            return null;

        GameObject prefab = config.prefabs[Random.Range(0, config.prefabs.Length)];
        float scale = Random.Range(config.scaleRange.x, config.scaleRange.y);

        return SpawnOnLayer(config, prefab, worldPosition, scale);
    }

    /// <summary>
    /// Clear all objects from a layer
    /// </summary>
    public void ClearLayer(string layerName)
    {
        LayerConfig config = GetLayer(layerName);
        if (config == null || config.container == null) return;

        for (int i = config.container.childCount - 1; i >= 0; i--)
        {
            Destroy(config.container.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// Clear all layers
    /// </summary>
    public void ClearAllLayers()
    {
        foreach (var config in layers)
        {
            if (config.container != null)
            {
                for (int i = config.container.childCount - 1; i >= 0; i--)
                {
                    Destroy(config.container.GetChild(i).gameObject);
                }
            }
        }
    }

    /// <summary>
    /// Snap all layers to current camera position (use after teleporting player)
    /// </summary>
    public void SnapAllLayers()
    {
        foreach (var config in layers)
        {
            if (config.container != null)
            {
                ParallaxLayer parallax = config.container.GetComponent<ParallaxLayer>();
                if (parallax != null)
                {
                    parallax.SnapToCamera();
                }
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showLayerGizmos || layers == null) return;

        // Draw layer indicators
        Vector3 pos = transform.position;
        for (int i = 0; i < layers.Length; i++)
        {
            Gizmos.color = layers[i].tint;
            Gizmos.DrawWireCube(pos + Vector3.forward * i, new Vector3(10f, 2f, 0.1f));
        }
    }
}
