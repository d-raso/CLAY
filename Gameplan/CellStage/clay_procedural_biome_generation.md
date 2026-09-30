# Clay: Procedural Biome Generation System
## Technical Design Document for Implementation

---

## Overview

This document describes how to generate diverse, visually stunning microscopic environments for Clay. The goal is to create biomes that feel alive, scientifically grounded, and visually distinct - solving the "flat background" problem while enabling procedural variety.

**Core Philosophy:** The environment isn't a backdrop. It's a character. Each biome should feel like a place with personality, dangers, and opportunities.

---

## Visual Layering System

The key to depth in a 2D microscopic world is **layered parallax with atmospheric effects**.

### Layer Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  LAYER 6: POST-PROCESSING                                       │
│  - Depth of field                                               │
│  - Color grading                                                │
│  - Vignette                                                     │
│  - Bloom                                                        │
├─────────────────────────────────────────────────────────────────┤
│  LAYER 5: NEAR FOREGROUND (closest, most blurred)              │
│  - Large particles very close to "camera"                       │
│  - Occasional debris floating past                              │
│  - Parallax: 1.5x player movement                               │
├─────────────────────────────────────────────────────────────────┤
│  LAYER 4: FOREGROUND                                            │
│  - Small particles, debris                                      │
│  - Slight blur                                                  │
│  - Parallax: 1.2x player movement                               │
├─────────────────────────────────────────────────────────────────┤
│  LAYER 3: PLAY AREA (sharp focus)                              │
│  - Player cell                                                  │
│  - Other cells                                                  │
│  - Interactive objects                                          │
│  - Parallax: 1.0x (reference)                                   │
├─────────────────────────────────────────────────────────────────┤
│  LAYER 2: NEAR BACKGROUND                                       │
│  - Terrain features (rocks, clay beds)                          │
│  - Bacterial mats, algae                                        │
│  - Moderate blur                                                │
│  - Parallax: 0.7x player movement                               │
├─────────────────────────────────────────────────────────────────┤
│  LAYER 1: MID BACKGROUND                                        │
│  - Larger structures (mineral chimneys, distant terrain)        │
│  - More blur                                                    │
│  - Parallax: 0.4x player movement                               │
├─────────────────────────────────────────────────────────────────┤
│  LAYER 0: FAR BACKGROUND                                        │
│  - Gradient / atmosphere                                        │
│  - Silhouettes                                                  │
│  - Heavy blur or just color                                     │
│  - Parallax: 0.1x player movement                               │
└─────────────────────────────────────────────────────────────────┘
```

### Implementation

```csharp
public class ParallaxLayer : MonoBehaviour
{
    [Header("Parallax Settings")]
    public float parallaxMultiplier = 1f;    // 0 = static, 1 = moves with player, >1 = foreground
    public bool infiniteHorizontal = true;
    public bool infiniteVertical = false;
    
    [Header("Visual Settings")]
    public float blurAmount = 0f;            // 0-1, applied via shader or post-process
    public float brightnessMultiplier = 1f;
    public float saturationMultiplier = 1f;
    
    private Vector3 startPosition;
    private Transform cameraTransform;
    
    void Start()
    {
        startPosition = transform.position;
        cameraTransform = Camera.main.transform;
    }
    
    void LateUpdate()
    {
        Vector3 cameraMovement = cameraTransform.position - startPosition;
        Vector3 parallaxOffset = cameraMovement * (1 - parallaxMultiplier);
        
        transform.position = startPosition + parallaxOffset;
        
        // Handle infinite scrolling
        if (infiniteHorizontal)
        {
            HandleInfiniteScroll(Vector2.right);
        }
    }
}

public class BiomeLayerManager : MonoBehaviour
{
    [System.Serializable]
    public class LayerConfig
    {
        public string layerName;
        public float parallaxMultiplier;
        public float blurAmount;
        public int sortingOrder;
        public GameObject[] prefabs;           // Objects that can spawn on this layer
        public float spawnDensity;
        public Vector2 scaleRange;
    }
    
    public LayerConfig[] layers = new LayerConfig[]
    {
        new LayerConfig { layerName = "FarBackground", parallaxMultiplier = 0.1f, blurAmount = 0.8f, sortingOrder = -100 },
        new LayerConfig { layerName = "MidBackground", parallaxMultiplier = 0.4f, blurAmount = 0.5f, sortingOrder = -50 },
        new LayerConfig { layerName = "NearBackground", parallaxMultiplier = 0.7f, blurAmount = 0.2f, sortingOrder = -20 },
        new LayerConfig { layerName = "PlayArea", parallaxMultiplier = 1.0f, blurAmount = 0f, sortingOrder = 0 },
        new LayerConfig { layerName = "Foreground", parallaxMultiplier = 1.2f, blurAmount = 0.15f, sortingOrder = 50 },
        new LayerConfig { layerName = "NearForeground", parallaxMultiplier = 1.5f, blurAmount = 0.4f, sortingOrder = 100 },
    };
}
```

---

## Biome Definitions

### Biome Data Structure

```csharp
[CreateAssetMenu(fileName = "Biome", menuName = "Clay/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    [Header("Identity")]
    public string biomeId;
    public string biomeName;
    public string description;
    
    [Header("Color Palette")]
    public Color primaryColor;
    public Color secondaryColor;
    public Color accentColor;
    public Color backgroundColor;
    public Gradient depthGradient;            // Color shift with depth
    public Gradient atmosphereGradient;       // Fog/atmosphere color
    
    [Header("Lighting")]
    public LightingMode lightingMode;
    public Color ambientColor;
    public float ambientIntensity = 1f;
    public LightSource[] lightSources;
    public bool hasCaustics = false;
    public float causticsIntensity = 0.5f;
    
    [Header("Particles")]
    public ParticleConfig[] particleSystems;
    public float particleDensity = 1f;
    
    [Header("Terrain")]
    public TerrainFeature[] terrainFeatures;
    public float terrainDensity = 1f;
    public Material terrainMaterial;
    
    [Header("Environment")]
    public float baseTemperature = 20f;
    public float temperatureVariance = 5f;
    public float basePH = 7f;
    public float phVariance = 0.5f;
    public float currentStrength = 0f;
    public Vector2 currentDirection;
    public float lightLevel = 1f;             // 0 = pitch black, 1 = full light
    
    [Header("Hazards")]
    public EnvironmentalHazard[] hazards;
    
    [Header("Safe Zones")]
    public SafeZoneSpawnConfig[] safeZoneConfigs;
    
    [Header("Resources")]
    public ResourceSpawnConfig[] resourceConfigs;
    
    [Header("Audio")]
    public AudioClip ambientLoop;
    public float ambientVolume = 0.5f;
    public AudioClip[] randomSounds;
}

public enum LightingMode
{
    Surface,          // Light from above, caustics
    Diffuse,          // Even murky light
    Dark,             // Minimal ambient, point lights only
    Bioluminescent,   // Self-lit organisms provide light
    Volcanic          // Light from below (vents)
}

[System.Serializable]
public class LightSource
{
    public LightSourceType type;
    public Color color;
    public float intensity;
    public float range;
    public float flickerAmount;
    public Vector2 positionVariance;
}

public enum LightSourceType
{
    Ambient,
    Directional,      // Sun through water
    Point,            // Bioluminescence, vents
    Spot,             // Focused light shafts
    Area              // Glowing regions
}
```

### Biome Definitions

```csharp
public static class BiomeTemplates
{
    // ================================
    // TIDE POOL (Surface, Bright)
    // ================================
    public static BiomeDefinition TidePool = new BiomeDefinition
    {
        biomeId = "TIDE_POOL",
        biomeName = "Tide Pool",
        description = "Warm, sunlit shallows where life thrives",
        
        // Warm, inviting colors
        primaryColor = new Color(0.2f, 0.6f, 0.8f),      // Ocean blue
        secondaryColor = new Color(0.9f, 0.85f, 0.7f),   // Sandy tan
        accentColor = new Color(0.3f, 0.8f, 0.4f),       // Algae green
        backgroundColor = new Color(0.7f, 0.9f, 1f),      // Light sky blue
        
        lightingMode = LightingMode.Surface,
        ambientColor = new Color(1f, 0.98f, 0.9f),
        ambientIntensity = 1.2f,
        hasCaustics = true,
        causticsIntensity = 0.7f,
        lightLevel = 1f,
        
        baseTemperature = 25f,
        temperatureVariance = 10f,
        basePH = 8.1f,
        phVariance = 0.3f,
        
        particleSystems = new[]
        {
            new ParticleConfig { type = ParticleType.Debris, density = 0.5f, sizeRange = new Vector2(0.01f, 0.05f) },
            new ParticleConfig { type = ParticleType.Plankton, density = 0.8f, sizeRange = new Vector2(0.02f, 0.08f) },
            new ParticleConfig { type = ParticleType.Bubbles, density = 0.3f, sizeRange = new Vector2(0.05f, 0.2f) },
            new ParticleConfig { type = ParticleType.LightShafts, density = 0.2f }
        },
        
        terrainFeatures = new[]
        {
            new TerrainFeature { type = TerrainType.Sand, frequency = 0.8f },
            new TerrainFeature { type = TerrainType.Pebbles, frequency = 0.4f },
            new TerrainFeature { type = TerrainType.Algae, frequency = 0.6f },
            new TerrainFeature { type = TerrainType.SeaGrass, frequency = 0.3f }
        }
    };
    
    // ================================
    // CLAY BED (Origin, Earthy)
    // ================================
    public static BiomeDefinition ClayBed = new BiomeDefinition
    {
        biomeId = "CLAY_BED",
        biomeName = "Clay Bed",
        description = "Ancient mineral deposits where life first stirred",
        
        primaryColor = new Color(0.6f, 0.45f, 0.3f),     // Clay brown
        secondaryColor = new Color(0.8f, 0.7f, 0.5f),    // Ochre
        accentColor = new Color(0.4f, 0.35f, 0.3f),      // Dark earth
        backgroundColor = new Color(0.3f, 0.25f, 0.2f),   // Deep brown
        
        lightingMode = LightingMode.Diffuse,
        ambientColor = new Color(0.9f, 0.8f, 0.6f),
        ambientIntensity = 0.7f,
        lightLevel = 0.5f,
        
        baseTemperature = 30f,
        basePH = 7.5f,
        
        particleSystems = new[]
        {
            new ParticleConfig { type = ParticleType.Silt, density = 0.7f, sizeRange = new Vector2(0.01f, 0.03f) },
            new ParticleConfig { type = ParticleType.MineralFlakes, density = 0.4f },
            new ParticleConfig { type = ParticleType.OrganicDebris, density = 0.3f }
        },
        
        terrainFeatures = new[]
        {
            new TerrainFeature { type = TerrainType.ClayLayers, frequency = 1f },
            new TerrainFeature { type = TerrainType.MineralDeposits, frequency = 0.5f },
            new TerrainFeature { type = TerrainType.AncientRock, frequency = 0.3f }
        },
        
        // Clay beds are primary safe zones
        safeZoneConfigs = new[]
        {
            new SafeZoneSpawnConfig { type = SafeZoneType.ClayBed, frequency = 0.8f, minDistance = 20f }
        }
    };
    
    // ================================
    // HYDROTHERMAL VENT (Dramatic, Dangerous)
    // ================================
    public static BiomeDefinition HydrothermalVent = new BiomeDefinition
    {
        biomeId = "HYDROTHERMAL_VENT",
        biomeName = "Hydrothermal Vent",
        description = "Superheated water erupts from the seafloor, rich with minerals and energy",
        
        primaryColor = new Color(0.1f, 0.1f, 0.15f),     // Deep black
        secondaryColor = new Color(0.8f, 0.4f, 0.1f),    // Volcanic orange
        accentColor = new Color(1f, 0.6f, 0.2f),         // Hot glow
        backgroundColor = new Color(0.05f, 0.05f, 0.08f), // Abyss
        
        lightingMode = LightingMode.Volcanic,
        ambientColor = new Color(0.3f, 0.2f, 0.1f),
        ambientIntensity = 0.3f,
        lightLevel = 0.2f,
        
        lightSources = new[]
        {
            new LightSource 
            { 
                type = LightSourceType.Point, 
                color = new Color(1f, 0.5f, 0.2f), 
                intensity = 2f, 
                range = 15f,
                flickerAmount = 0.3f 
            }
        },
        
        baseTemperature = 80f,
        temperatureVariance = 40f,  // Hot near vents, cooler away
        basePH = 3f,                // Acidic
        phVariance = 2f,
        
        particleSystems = new[]
        {
            new ParticleConfig { type = ParticleType.Smoke, density = 1f, color = new Color(0.2f, 0.2f, 0.2f, 0.5f) },
            new ParticleConfig { type = ParticleType.Embers, density = 0.5f, color = new Color(1f, 0.5f, 0.2f) },
            new ParticleConfig { type = ParticleType.MineralFlakes, density = 0.8f, color = new Color(0.8f, 0.6f, 0.2f) },
            new ParticleConfig { type = ParticleType.HeatDistortion, density = 0.3f }
        },
        
        terrainFeatures = new[]
        {
            new TerrainFeature { type = TerrainType.VentChimney, frequency = 0.3f },
            new TerrainFeature { type = TerrainType.MineralDeposits, frequency = 0.7f },
            new TerrainFeature { type = TerrainType.Blackite, frequency = 0.5f },
            new TerrainFeature { type = TerrainType.TubeWorms, frequency = 0.4f }
        },
        
        hazards = new[]
        {
            new EnvironmentalHazard { type = HazardType.Heat, damage = 5f, radius = 8f },
            new EnvironmentalHazard { type = HazardType.AcidicWater, damage = 2f, radius = 15f }
        }
    };
    
    // ================================
    // DEEP OCEAN (Dark, Sparse, Bioluminescent)
    // ================================
    public static BiomeDefinition DeepOcean = new BiomeDefinition
    {
        biomeId = "DEEP_OCEAN",
        biomeName = "Deep Ocean",
        description = "The lightless abyss where strange creatures drift",
        
        primaryColor = new Color(0.02f, 0.05f, 0.1f),    // Near black blue
        secondaryColor = new Color(0.1f, 0.15f, 0.25f),  // Dark blue
        accentColor = new Color(0.3f, 0.8f, 1f),         // Bioluminescent cyan
        backgroundColor = new Color(0.01f, 0.02f, 0.05f), // Void
        
        lightingMode = LightingMode.Bioluminescent,
        ambientColor = new Color(0.1f, 0.1f, 0.2f),
        ambientIntensity = 0.1f,
        lightLevel = 0.05f,
        
        lightSources = new[]
        {
            new LightSource 
            { 
                type = LightSourceType.Point, 
                color = new Color(0.3f, 0.8f, 1f), 
                intensity = 0.5f, 
                range = 5f,
                flickerAmount = 0.1f 
            }
        },
        
        baseTemperature = 4f,
        basePH = 7.8f,
        currentStrength = 0.2f,
        
        particleSystems = new[]
        {
            new ParticleConfig { type = ParticleType.MarineSnow, density = 0.3f },
            new ParticleConfig { type = ParticleType.BioluminescentSpecs, density = 0.2f }
        },
        
        terrainFeatures = new[]
        {
            new TerrainFeature { type = TerrainType.AbyssalPlain, frequency = 0.9f },
            new TerrainFeature { type = TerrainType.BoneYard, frequency = 0.1f },
            new TerrainFeature { type = TerrainType.MudVolcano, frequency = 0.05f }
        }
    };
    
    // ================================
    // BACTERIAL MAT (Colorful, Slimy)
    // ================================
    public static BiomeDefinition BacterialMat = new BiomeDefinition
    {
        biomeId = "BACTERIAL_MAT",
        biomeName = "Bacterial Mat",
        description = "Dense colonies of ancient microbes create a living landscape",
        
        primaryColor = new Color(0.6f, 0.2f, 0.5f),      // Purple
        secondaryColor = new Color(0.2f, 0.7f, 0.4f),    // Green
        accentColor = new Color(0.9f, 0.7f, 0.2f),       // Golden
        backgroundColor = new Color(0.2f, 0.15f, 0.25f),
        
        lightingMode = LightingMode.Diffuse,
        ambientColor = new Color(0.8f, 0.6f, 0.9f),
        ambientIntensity = 0.8f,
        lightLevel = 0.6f,
        
        baseTemperature = 35f,
        basePH = 6.5f,
        
        particleSystems = new[]
        {
            new ParticleConfig { type = ParticleType.Spores, density = 0.6f },
            new ParticleConfig { type = ParticleType.SlimeStrands, density = 0.4f },
            new ParticleConfig { type = ParticleType.GasBubbles, density = 0.3f }
        },
        
        terrainFeatures = new[]
        {
            new TerrainFeature { type = TerrainType.BacterialMat, frequency = 0.9f },
            new TerrainFeature { type = TerrainType.Stromatolite, frequency = 0.3f },
            new TerrainFeature { type = TerrainType.SlimeMound, frequency = 0.5f }
        }
    };
}
```

---

## Procedural Generation System

### Chunk-Based World Generation

```csharp
public class WorldGenerator : MonoBehaviour
{
    [Header("Chunk Settings")]
    public int chunkSize = 50;                    // Units per chunk
    public int loadRadius = 3;                    // Chunks to keep loaded around player
    public int generateAheadRadius = 4;           // Chunks to pre-generate
    
    [Header("Biome Settings")]
    public float biomeScale = 0.01f;              // Noise scale for biome placement
    public float transitionWidth = 10f;           // Blend zone between biomes
    
    private Dictionary<Vector2Int, Chunk> loadedChunks = new();
    private Transform playerTransform;
    
    void Update()
    {
        Vector2Int playerChunk = WorldToChunk(playerTransform.position);
        
        // Load/unload chunks based on player position
        UpdateLoadedChunks(playerChunk);
    }
    
    void UpdateLoadedChunks(Vector2Int centerChunk)
    {
        // Determine which chunks should be loaded
        HashSet<Vector2Int> shouldBeLoaded = new();
        
        for (int x = -loadRadius; x <= loadRadius; x++)
        {
            for (int y = -loadRadius; y <= loadRadius; y++)
            {
                shouldBeLoaded.Add(centerChunk + new Vector2Int(x, y));
            }
        }
        
        // Unload chunks that are too far
        var toUnload = loadedChunks.Keys.Where(k => !shouldBeLoaded.Contains(k)).ToList();
        foreach (var chunkPos in toUnload)
        {
            UnloadChunk(chunkPos);
        }
        
        // Load new chunks
        foreach (var chunkPos in shouldBeLoaded)
        {
            if (!loadedChunks.ContainsKey(chunkPos))
            {
                LoadChunk(chunkPos);
            }
        }
    }
    
    void LoadChunk(Vector2Int chunkPos)
    {
        Chunk chunk = GenerateChunk(chunkPos);
        loadedChunks[chunkPos] = chunk;
    }
    
    Chunk GenerateChunk(Vector2Int chunkPos)
    {
        Chunk chunk = new Chunk(chunkPos, chunkSize);
        
        // Determine biome(s) for this chunk
        BiomeData biomeData = SampleBiomeAt(ChunkToWorld(chunkPos));
        chunk.primaryBiome = biomeData.primary;
        chunk.secondaryBiome = biomeData.secondary;
        chunk.blendFactor = biomeData.blendFactor;
        
        // Generate terrain
        GenerateTerrain(chunk);
        
        // Generate particles
        GenerateParticles(chunk);
        
        // Generate safe zones
        GenerateSafeZones(chunk);
        
        // Generate resources
        GenerateResources(chunk);
        
        return chunk;
    }
    
    BiomeData SampleBiomeAt(Vector2 worldPos)
    {
        // Use layered noise to determine biome
        float biomeNoise = SampleBiomeNoise(worldPos);
        float depthFactor = GetDepthFactor(worldPos);
        float temperatureNoise = SampleTemperatureNoise(worldPos);
        
        // Combine factors to select biome
        BiomeDefinition primary = SelectBiome(biomeNoise, depthFactor, temperatureNoise);
        
        // Check for transition zones
        BiomeDefinition secondary = null;
        float blendFactor = 0f;
        
        // Sample nearby points to detect transitions
        Vector2[] offsets = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
        foreach (var offset in offsets)
        {
            Vector2 samplePos = worldPos + offset * transitionWidth;
            BiomeDefinition neighbor = SelectBiome(
                SampleBiomeNoise(samplePos),
                GetDepthFactor(samplePos),
                SampleTemperatureNoise(samplePos)
            );
            
            if (neighbor != primary)
            {
                secondary = neighbor;
                blendFactor = CalculateBlendFactor(worldPos, samplePos);
                break;
            }
        }
        
        return new BiomeData { primary = primary, secondary = secondary, blendFactor = blendFactor };
    }
    
    BiomeDefinition SelectBiome(float biomeNoise, float depth, float temperature)
    {
        // Depth is primary selector
        if (depth > 0.8f)
        {
            return BiomeTemplates.DeepOcean;
        }
        else if (depth < 0.2f)
        {
            return BiomeTemplates.TidePool;
        }
        
        // Temperature/noise for mid-depths
        if (temperature > 0.7f)
        {
            return BiomeTemplates.HydrothermalVent;
        }
        else if (biomeNoise > 0.6f)
        {
            return BiomeTemplates.BacterialMat;
        }
        else
        {
            return BiomeTemplates.ClayBed;
        }
    }
}

public class Chunk
{
    public Vector2Int position;
    public int size;
    public BiomeDefinition primaryBiome;
    public BiomeDefinition secondaryBiome;
    public float blendFactor;
    
    public List<GameObject> terrainObjects = new();
    public List<ParticleSystem> particleSystems = new();
    public List<SafeZone> safeZones = new();
    public List<Resource> resources = new();
    
    public Chunk(Vector2Int pos, int size)
    {
        this.position = pos;
        this.size = size;
    }
}
```

### Terrain Generation

```csharp
public class TerrainGenerator : MonoBehaviour
{
    [Header("Noise Settings")]
    public float terrainScale = 0.1f;
    public int octaves = 4;
    public float persistence = 0.5f;
    public float lacunarity = 2f;
    
    public void GenerateTerrain(Chunk chunk)
    {
        BiomeDefinition biome = chunk.primaryBiome;
        Vector2 chunkWorldPos = new Vector2(chunk.position.x, chunk.position.y) * chunk.size;
        
        foreach (TerrainFeature feature in biome.terrainFeatures)
        {
            GenerateFeatureType(chunk, feature, chunkWorldPos);
        }
    }
    
    void GenerateFeatureType(Chunk chunk, TerrainFeature feature, Vector2 chunkOrigin)
    {
        // Use Poisson disk sampling for natural distribution
        List<Vector2> points = PoissonDiskSampling.Generate(
            chunk.size, 
            chunk.size, 
            feature.minSpacing,
            30  // Max attempts
        );
        
        foreach (Vector2 localPos in points)
        {
            // Apply noise-based density
            Vector2 worldPos = chunkOrigin + localPos;
            float densityNoise = Mathf.PerlinNoise(worldPos.x * terrainScale, worldPos.y * terrainScale);
            
            if (densityNoise < feature.frequency)
            {
                SpawnTerrainFeature(chunk, feature, worldPos);
            }
        }
    }
    
    void SpawnTerrainFeature(Chunk chunk, TerrainFeature feature, Vector2 worldPos)
    {
        GameObject prefab = GetPrefabForFeature(feature.type);
        if (prefab == null) return;
        
        // Determine layer based on feature type
        int layer = GetLayerForFeature(feature.type);
        
        // Random rotation and scale
        float rotation = Random.Range(0f, 360f);
        float scale = Random.Range(feature.scaleRange.x, feature.scaleRange.y);
        
        GameObject instance = Instantiate(prefab, worldPos, Quaternion.Euler(0, 0, rotation));
        instance.transform.localScale = Vector3.one * scale;
        instance.GetComponent<SpriteRenderer>().sortingOrder = layer;
        
        // Apply biome coloring
        ApplyBiomeColors(instance, chunk.primaryBiome, chunk.secondaryBiome, chunk.blendFactor);
        
        chunk.terrainObjects.Add(instance);
    }
    
    void ApplyBiomeColors(GameObject obj, BiomeDefinition primary, BiomeDefinition secondary, float blend)
    {
        var renderer = obj.GetComponent<SpriteRenderer>();
        if (renderer == null) return;
        
        Color baseColor = primary.primaryColor;
        
        if (secondary != null && blend > 0)
        {
            baseColor = Color.Lerp(primary.primaryColor, secondary.primaryColor, blend);
        }
        
        // Apply color with some variation
        float variation = 0.1f;
        baseColor.r += Random.Range(-variation, variation);
        baseColor.g += Random.Range(-variation, variation);
        baseColor.b += Random.Range(-variation, variation);
        
        renderer.color = baseColor;
    }
}

public enum TerrainType
{
    // Tide Pool
    Sand,
    Pebbles,
    Algae,
    SeaGrass,
    
    // Clay Bed
    ClayLayers,
    MineralDeposits,
    AncientRock,
    
    // Hydrothermal Vent
    VentChimney,
    Blackite,
    TubeWorms,
    
    // Deep Ocean
    AbyssalPlain,
    BoneYard,
    MudVolcano,
    
    // Bacterial Mat
    BacterialMat,
    Stromatolite,
    SlimeMound
}

[System.Serializable]
public class TerrainFeature
{
    public TerrainType type;
    public float frequency;                      // 0-1, density
    public float minSpacing = 5f;                // Minimum distance between instances
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
    public bool interactable = false;
    public bool blocksMovement = false;
}
```

### Particle System Generation

```csharp
public class BiomeParticleManager : MonoBehaviour
{
    public void GenerateParticles(Chunk chunk)
    {
        BiomeDefinition biome = chunk.primaryBiome;
        Vector2 chunkCenter = new Vector2(chunk.position.x + 0.5f, chunk.position.y + 0.5f) * chunk.size;
        
        foreach (ParticleConfig config in biome.particleSystems)
        {
            ParticleSystem ps = CreateParticleSystem(config, chunkCenter, chunk.size);
            chunk.particleSystems.Add(ps);
        }
    }
    
    ParticleSystem CreateParticleSystem(ParticleConfig config, Vector2 center, float areaSize)
    {
        GameObject psObject = new GameObject($"Particles_{config.type}");
        psObject.transform.position = center;
        
        ParticleSystem ps = psObject.AddComponent<ParticleSystem>();
        var main = ps.main;
        var emission = ps.emission;
        var shape = ps.shape;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        
        // Configure based on particle type
        switch (config.type)
        {
            case ParticleType.Debris:
                ConfigureDebrisParticles(ps, config, areaSize);
                break;
            case ParticleType.Plankton:
                ConfigurePlanktonParticles(ps, config, areaSize);
                break;
            case ParticleType.Bubbles:
                ConfigureBubbleParticles(ps, config, areaSize);
                break;
            case ParticleType.Smoke:
                ConfigureSmokeParticles(ps, config, areaSize);
                break;
            case ParticleType.MarineSnow:
                ConfigureMarineSnowParticles(ps, config, areaSize);
                break;
            case ParticleType.BioluminescentSpecs:
                ConfigureBioluminescentParticles(ps, config, areaSize);
                break;
            case ParticleType.LightShafts:
                ConfigureLightShaftParticles(ps, config, areaSize);
                break;
        }
        
        return ps;
    }
    
    void ConfigureDebrisParticles(ParticleSystem ps, ParticleConfig config, float areaSize)
    {
        var main = ps.main;
        main.startLifetime = 20f;
        main.startSpeed = 0.1f;
        main.startSize = new ParticleSystem.MinMaxCurve(config.sizeRange.x, config.sizeRange.y);
        main.startColor = config.color != default ? config.color : new Color(0.5f, 0.5f, 0.5f, 0.5f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.RoundToInt(100 * config.density);
        
        var emission = ps.emission;
        emission.rateOverTime = 5f * config.density;
        
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(areaSize, areaSize, 1f);
        
        // Slow drift
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
    }
    
    void ConfigureBioluminescentParticles(ParticleSystem ps, ParticleConfig config, float areaSize)
    {
        var main = ps.main;
        main.startLifetime = 5f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startColor = new Color(0.3f, 0.8f, 1f, 0.8f);  // Cyan glow
        
        var emission = ps.emission;
        emission.rateOverTime = 2f * config.density;
        
        // Pulsing glow
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.cyan, 0f),
                new GradientColorKey(Color.blue, 0.5f),
                new GradientColorKey(Color.cyan, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.3f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = gradient;
        
        // Add light (if performance allows)
        var lights = ps.lights;
        lights.enabled = true;
        lights.ratio = 0.1f;
        lights.intensity = 0.5f;
        lights.range = 2f;
    }
    
    void ConfigureLightShaftParticles(ParticleSystem ps, ParticleConfig config, float areaSize)
    {
        var main = ps.main;
        main.startLifetime = 10f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startColor = new Color(1f, 1f, 0.9f, 0.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 10f;
        
        var emission = ps.emission;
        emission.rateOverTime = 0.5f * config.density;
        
        // Shimmer
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.1f;
        noise.frequency = 0.5f;
    }
}

public enum ParticleType
{
    Debris,
    Plankton,
    Bubbles,
    Smoke,
    Embers,
    MineralFlakes,
    Silt,
    OrganicDebris,
    MarineSnow,
    BioluminescentSpecs,
    Spores,
    SlimeStrands,
    GasBubbles,
    LightShafts,
    HeatDistortion
}

[System.Serializable]
public class ParticleConfig
{
    public ParticleType type;
    public float density = 1f;
    public Vector2 sizeRange = new Vector2(0.01f, 0.05f);
    public Color color = default;
    public int layer = 0;  // Which parallax layer
}
```

---

## Post-Processing

```csharp
public class BiomePostProcessing : MonoBehaviour
{
    [Header("References")]
    public Volume globalVolume;
    public DepthOfField dofSettings;
    public ColorAdjustments colorSettings;
    public Vignette vignetteSettings;
    public Bloom bloomSettings;
    
    public void ApplyBiomePostProcessing(BiomeDefinition biome, float transitionDuration = 1f)
    {
        StartCoroutine(TransitionPostProcessing(biome, transitionDuration));
    }
    
    IEnumerator TransitionPostProcessing(BiomeDefinition biome, float duration)
    {
        // Capture current values
        Color startColorFilter = colorSettings.colorFilter.value;
        float startSaturation = colorSettings.saturation.value;
        float startVignette = vignetteSettings.intensity.value;
        Color startVignetteColor = vignetteSettings.color.value;
        float startBloom = bloomSettings.intensity.value;
        
        // Target values based on biome
        Color targetColorFilter = GetColorFilterForBiome(biome);
        float targetSaturation = GetSaturationForBiome(biome);
        float targetVignette = GetVignetteForBiome(biome);
        Color targetVignetteColor = biome.backgroundColor;
        float targetBloom = GetBloomForBiome(biome);
        
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            t = Mathf.SmoothStep(0, 1, t);  // Ease in/out
            
            colorSettings.colorFilter.value = Color.Lerp(startColorFilter, targetColorFilter, t);
            colorSettings.saturation.value = Mathf.Lerp(startSaturation, targetSaturation, t);
            vignetteSettings.intensity.value = Mathf.Lerp(startVignette, targetVignette, t);
            vignetteSettings.color.value = Color.Lerp(startVignetteColor, targetVignetteColor, t);
            bloomSettings.intensity.value = Mathf.Lerp(startBloom, targetBloom, t);
            
            elapsed += Time.deltaTime;
            yield return null;
        }
    }
    
    Color GetColorFilterForBiome(BiomeDefinition biome)
    {
        switch (biome.biomeId)
        {
            case "TIDE_POOL":
                return new Color(1f, 1f, 0.95f);      // Warm sunlight
            case "DEEP_OCEAN":
                return new Color(0.7f, 0.8f, 1f);     // Cold blue
            case "HYDROTHERMAL_VENT":
                return new Color(1f, 0.9f, 0.8f);     // Warm volcanic
            case "BACTERIAL_MAT":
                return new Color(0.95f, 0.9f, 1f);    // Slight purple
            default:
                return Color.white;
        }
    }
    
    float GetSaturationForBiome(BiomeDefinition biome)
    {
        switch (biome.lightingMode)
        {
            case LightingMode.Dark:
            case LightingMode.Bioluminescent:
                return -20f;   // Desaturated
            case LightingMode.Surface:
                return 10f;    // Vibrant
            default:
                return 0f;
        }
    }
    
    float GetVignetteForBiome(BiomeDefinition biome)
    {
        // Darker biomes have stronger vignette
        return 0.2f + (1f - biome.lightLevel) * 0.3f;
    }
    
    float GetBloomForBiome(BiomeDefinition biome)
    {
        switch (biome.biomeId)
        {
            case "HYDROTHERMAL_VENT":
                return 1.5f;   // Hot glow
            case "DEEP_OCEAN":
                return 0.8f;   // Bioluminescence
            case "TIDE_POOL":
                return 0.5f;   // Soft light
            default:
                return 0.3f;
        }
    }
}
```

---

## Caustics System (For Surface Biomes)

```csharp
public class CausticsEffect : MonoBehaviour
{
    [Header("Caustics Settings")]
    public Texture2D causticsTexture;
    public float scrollSpeed = 0.5f;
    public float scale = 10f;
    public float intensity = 0.5f;
    
    [Header("Animation")]
    public int frameCount = 32;
    public float frameRate = 24f;
    public Texture2D[] causticsFrames;
    
    private Material causticsMaterial;
    private int currentFrame = 0;
    private float frameTimer = 0f;
    
    void Start()
    {
        causticsMaterial = new Material(Shader.Find("Custom/Caustics"));
        GetComponent<SpriteRenderer>().material = causticsMaterial;
    }
    
    void Update()
    {
        // Animate frames
        frameTimer += Time.deltaTime;
        if (frameTimer >= 1f / frameRate)
        {
            frameTimer = 0f;
            currentFrame = (currentFrame + 1) % frameCount;
            causticsMaterial.SetTexture("_CausticsTex", causticsFrames[currentFrame]);
        }
        
        // Scroll
        Vector2 offset = new Vector2(Time.time * scrollSpeed, Time.time * scrollSpeed * 0.7f);
        causticsMaterial.SetTextureOffset("_CausticsTex", offset);
        
        // Intensity variation
        float flicker = 0.8f + Mathf.PerlinNoise(Time.time * 2f, 0) * 0.4f;
        causticsMaterial.SetFloat("_Intensity", intensity * flicker);
    }
}
```

---

## Biome Transition System

```csharp
public class BiomeTransitionManager : MonoBehaviour
{
    [Header("Transition Settings")]
    public float transitionWidth = 20f;
    public float checkInterval = 0.5f;
    
    private BiomeDefinition currentBiome;
    private BiomeDefinition targetBiome;
    private float transitionProgress = 1f;
    
    void Start()
    {
        InvokeRepeating(nameof(CheckBiomeTransition), 0f, checkInterval);
    }
    
    void CheckBiomeTransition()
    {
        Vector2 playerPos = player.transform.position;
        BiomeData biomeData = worldGenerator.SampleBiomeAt(playerPos);
        
        if (biomeData.primary != currentBiome)
        {
            StartTransition(biomeData.primary);
        }
    }
    
    void StartTransition(BiomeDefinition newBiome)
    {
        targetBiome = newBiome;
        transitionProgress = 0f;
        
        // Start all transition effects
        StartCoroutine(TransitionRoutine());
    }
    
    IEnumerator TransitionRoutine()
    {
        float duration = 2f;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            transitionProgress = elapsed / duration;
            
            // Interpolate everything
            UpdateLighting(transitionProgress);
            UpdateParticles(transitionProgress);
            UpdateAudio(transitionProgress);
            UpdatePostProcessing(transitionProgress);
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        currentBiome = targetBiome;
        transitionProgress = 1f;
    }
    
    void UpdateLighting(float t)
    {
        Color ambientColor = Color.Lerp(currentBiome.ambientColor, targetBiome.ambientColor, t);
        float intensity = Mathf.Lerp(currentBiome.ambientIntensity, targetBiome.ambientIntensity, t);
        
        RenderSettings.ambientLight = ambientColor * intensity;
    }
    
    void UpdateAudio(float t)
    {
        // Crossfade ambient audio
        if (currentBiome.ambientLoop != targetBiome.ambientLoop)
        {
            currentAmbientSource.volume = currentBiome.ambientVolume * (1 - t);
            targetAmbientSource.volume = targetBiome.ambientVolume * t;
        }
    }
}
```

---

## Implementation Priority

### Phase 1: Layer System (Week 1)
1. Implement parallax layer manager
2. Create 6-layer rendering setup
3. Test with placeholder sprites

### Phase 2: Single Biome Polish (Week 2-3)
1. Pick one biome (suggest Tide Pool - most visually accessible)
2. Create all terrain prefabs
3. Configure particle systems
4. Set up post-processing
5. **Goal: One beautiful, complete biome**

### Phase 3: Chunk System (Week 4-5)
1. Implement chunk loading/unloading
2. Terrain generation with Poisson sampling
3. Particle system pooling

### Phase 4: Additional Biomes (Week 6-8)
1. Add remaining biomes one at a time
2. Create unique assets for each
3. Test transitions between them

### Phase 5: Biome Transitions (Week 9-10)
1. Implement noise-based biome selection
2. Smooth transitions (visual, audio, post-processing)
3. Blend zones

### Phase 6: Polish (Week 11-12)
1. Performance optimization
2. Visual variety (more terrain prefabs)
3. Biome-specific safe zone visuals
4. Sound design per biome

---

## Performance Considerations

- **Object pooling**: All particles and terrain features should use pooling
- **LOD**: Far background layers can use lower-resolution sprites
- **Culling**: Disable particle systems and complex shaders off-screen
- **Chunk limits**: Maximum 9-16 chunks loaded at once
- **Particle budgets**: Cap total particles per biome (500-1000)
- **Shader complexity**: Background layers use simpler shaders

---

## Testing Checklist

- [ ] Does parallax create convincing depth?
- [ ] Are biomes visually distinct at a glance?
- [ ] Do transitions feel smooth, not jarring?
- [ ] Does each biome have a unique "feel"?
- [ ] Are particles enhancing, not distracting?
- [ ] Does post-processing reinforce biome mood?
- [ ] Is performance stable with full particle load?
- [ ] Do safe zones feel like natural parts of the biome?
