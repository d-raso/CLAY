using UnityEngine;

/// <summary>
/// Central controller for all environment effects.
/// Manages biome presets and transitions between them.
/// </summary>
public class EnvironmentController : MonoBehaviour
{
    [Header("Component References")]
    public InfiniteBackground infiniteBackground;
    public ParallaxManager parallaxManager;
    public ProceduralBackgroundGenerator backgroundGenerator;
    public CausticsEffect caustics;
    public AmbientParticles particles;
    public LightShafts lightShafts;
    public FlowFieldManager flowFieldManager;
    public Camera mainCamera;

    [Header("Current Biome")]
    public BiomePreset currentBiome;

    [Header("Transition")]
    public float transitionDuration = 2f;

    [System.Serializable]
    public class BiomePreset
    {
        public string biomeName;

        [Header("Background")]
        public Color backgroundTop = new Color(0.1f, 0.2f, 0.25f);
        public Color backgroundBottom = new Color(0.02f, 0.05f, 0.08f);
        public Color cameraBackground = new Color(0.02f, 0.05f, 0.08f);

        [Header("Environment Tint")]
        public Color environmentTint = new Color(0.3f, 0.5f, 0.4f);

        [Header("Caustics")]
        public bool enableCaustics = true;
        [Range(0f, 1f)]
        public float causticsIntensity = 0.3f;
        public Color causticsColor = new Color(0.8f, 0.95f, 1f);

        [Header("Light Shafts")]
        public bool enableLightShafts = true;
        [Range(0f, 1f)]
        public float lightShaftIntensity = 0.15f;
        public Color lightShaftColor = new Color(1f, 0.98f, 0.9f);

        [Header("Particles")]
        [Range(0f, 2f)]
        public float particleDensity = 1f;
        public Color debrisColor = new Color(0.5f, 0.55f, 0.5f, 0.6f);
        public Color planktonColor = new Color(0.6f, 0.8f, 0.5f, 0.4f);
        public Color bubbleColor = new Color(0.8f, 0.9f, 1f, 0.3f);
        public Color siltColor = new Color(0.6f, 0.55f, 0.45f, 0.2f);
        public bool enableBubbles = true;

        [Header("Water Current")]
        public Vector2 currentDirection = new Vector2(0.1f, 0.02f);
        public float currentStrength = 0.5f;

        [Header("Advanced Flow")]
        [Range(0f, 1f)]
        public float flowTurbulence = 0.3f;
        public float flowNoiseScale = 5f;

        [Header("Water Distortion")]
        public bool enableWaterDistortion = true;
        [Range(0f, 0.05f)]
        public float distortionStrength = 0.02f;

        [Header("GPU Caustics")]
        public bool useGPUCaustics = true;
        public bool enableGodRays = false;
        [Range(0f, 1f)]
        public float godRaysIntensity = 0.15f;
    }

    // Built-in biome presets
    public static class BiomePresets
    {
        public static BiomePreset TidePool = new BiomePreset
        {
            biomeName = "Tide Pool",
            backgroundTop = new Color(0.2f, 0.4f, 0.5f),
            backgroundBottom = new Color(0.1f, 0.2f, 0.25f),
            cameraBackground = new Color(0.1f, 0.2f, 0.25f),
            environmentTint = new Color(0.4f, 0.6f, 0.5f),
            enableCaustics = true,
            causticsIntensity = 0.4f,
            causticsColor = new Color(0.9f, 1f, 0.95f),
            enableLightShafts = true,
            lightShaftIntensity = 0.2f,
            lightShaftColor = new Color(1f, 0.98f, 0.9f),
            particleDensity = 1f,
            debrisColor = new Color(0.5f, 0.55f, 0.5f, 0.5f),
            planktonColor = new Color(0.5f, 0.8f, 0.4f, 0.4f),
            bubbleColor = new Color(0.85f, 0.95f, 1f, 0.35f),
            siltColor = new Color(0.6f, 0.55f, 0.5f, 0.15f),
            enableBubbles = true,
            currentDirection = new Vector2(0.15f, 0.02f),
            currentStrength = 0.6f,
            flowTurbulence = 0.3f,
            flowNoiseScale = 5f,
            enableWaterDistortion = true,
            distortionStrength = 0.02f,
            useGPUCaustics = true,
            enableGodRays = true,
            godRaysIntensity = 0.2f
        };

        public static BiomePreset ClayBed = new BiomePreset
        {
            biomeName = "Clay Bed",
            backgroundTop = new Color(0.25f, 0.2f, 0.15f),
            backgroundBottom = new Color(0.1f, 0.08f, 0.05f),
            cameraBackground = new Color(0.1f, 0.08f, 0.05f),
            environmentTint = new Color(0.5f, 0.4f, 0.3f),
            enableCaustics = true,
            causticsIntensity = 0.15f,
            causticsColor = new Color(0.9f, 0.85f, 0.7f),
            enableLightShafts = false,
            lightShaftIntensity = 0f,
            particleDensity = 0.8f,
            debrisColor = new Color(0.5f, 0.4f, 0.3f, 0.5f),
            planktonColor = new Color(0.6f, 0.5f, 0.4f, 0.3f),
            bubbleColor = new Color(0.7f, 0.65f, 0.6f, 0.2f),
            siltColor = new Color(0.55f, 0.45f, 0.35f, 0.3f),
            enableBubbles = false,
            currentDirection = new Vector2(0.05f, 0f),
            currentStrength = 0.2f,
            flowTurbulence = 0.15f,
            flowNoiseScale = 8f,
            enableWaterDistortion = true,
            distortionStrength = 0.01f,
            useGPUCaustics = true,
            enableGodRays = false,
            godRaysIntensity = 0f
        };

        public static BiomePreset HydrothermalVent = new BiomePreset
        {
            biomeName = "Hydrothermal Vent",
            backgroundTop = new Color(0.15f, 0.1f, 0.08f),
            backgroundBottom = new Color(0.02f, 0.01f, 0.01f),
            cameraBackground = new Color(0.02f, 0.01f, 0.01f),
            environmentTint = new Color(0.3f, 0.2f, 0.15f),
            enableCaustics = false,
            causticsIntensity = 0f,
            enableLightShafts = false,
            lightShaftIntensity = 0f,
            particleDensity = 1.5f,
            debrisColor = new Color(0.3f, 0.25f, 0.2f, 0.6f),
            planktonColor = new Color(0.8f, 0.5f, 0.3f, 0.4f), // Orange heat-loving microbes
            bubbleColor = new Color(0.4f, 0.35f, 0.3f, 0.5f), // Dark volcanic bubbles
            siltColor = new Color(0.4f, 0.3f, 0.2f, 0.4f),
            enableBubbles = true,
            currentDirection = new Vector2(0f, 0.3f), // Upward thermal current
            currentStrength = 1f,
            flowTurbulence = 0.6f,
            flowNoiseScale = 3f,
            enableWaterDistortion = true,
            distortionStrength = 0.03f,
            useGPUCaustics = false,
            enableGodRays = false,
            godRaysIntensity = 0f
        };

        public static BiomePreset DeepOcean = new BiomePreset
        {
            biomeName = "Deep Ocean",
            backgroundTop = new Color(0.03f, 0.05f, 0.1f),
            backgroundBottom = new Color(0.01f, 0.01f, 0.03f),
            cameraBackground = new Color(0.01f, 0.01f, 0.03f),
            environmentTint = new Color(0.1f, 0.15f, 0.25f),
            enableCaustics = false,
            causticsIntensity = 0f,
            enableLightShafts = false,
            lightShaftIntensity = 0f,
            particleDensity = 0.3f,
            debrisColor = new Color(0.2f, 0.25f, 0.3f, 0.3f),
            planktonColor = new Color(0.3f, 0.6f, 0.8f, 0.5f), // Bioluminescent
            bubbleColor = new Color(0.3f, 0.4f, 0.5f, 0.1f),
            siltColor = new Color(0.15f, 0.18f, 0.22f, 0.2f),
            enableBubbles = false,
            currentDirection = new Vector2(0.02f, -0.01f),
            currentStrength = 0.1f,
            flowTurbulence = 0.1f,
            flowNoiseScale = 10f,
            enableWaterDistortion = true,
            distortionStrength = 0.005f,
            useGPUCaustics = false,
            enableGodRays = false,
            godRaysIntensity = 0f
        };

        public static BiomePreset BacterialMat = new BiomePreset
        {
            biomeName = "Bacterial Mat",
            backgroundTop = new Color(0.25f, 0.15f, 0.3f),
            backgroundBottom = new Color(0.1f, 0.08f, 0.15f),
            cameraBackground = new Color(0.1f, 0.08f, 0.15f),
            environmentTint = new Color(0.4f, 0.3f, 0.5f),
            enableCaustics = true,
            causticsIntensity = 0.1f,
            causticsColor = new Color(0.9f, 0.8f, 1f),
            enableLightShafts = false,
            lightShaftIntensity = 0f,
            particleDensity = 1.2f,
            debrisColor = new Color(0.5f, 0.4f, 0.55f, 0.5f),
            planktonColor = new Color(0.6f, 0.8f, 0.5f, 0.5f), // Green bacteria
            bubbleColor = new Color(0.7f, 0.8f, 0.6f, 0.3f), // Gas bubbles
            siltColor = new Color(0.4f, 0.35f, 0.45f, 0.25f),
            enableBubbles = true,
            currentDirection = new Vector2(0.08f, 0.03f),
            currentStrength = 0.3f,
            flowTurbulence = 0.4f,
            flowNoiseScale = 4f,
            enableWaterDistortion = true,
            distortionStrength = 0.015f,
            useGPUCaustics = true,
            enableGodRays = false,
            godRaysIntensity = 0f
        };
    }

    void Start()
    {
        // Auto-find components if not assigned
        if (infiniteBackground == null) infiniteBackground = FindFirstObjectByType<InfiniteBackground>();
        if (parallaxManager == null) parallaxManager = FindFirstObjectByType<ParallaxManager>();
        if (backgroundGenerator == null) backgroundGenerator = FindFirstObjectByType<ProceduralBackgroundGenerator>();
        if (caustics == null) caustics = FindFirstObjectByType<CausticsEffect>();
        if (particles == null) particles = FindFirstObjectByType<AmbientParticles>();
        if (lightShafts == null) lightShafts = FindFirstObjectByType<LightShafts>();
        if (flowFieldManager == null) flowFieldManager = FindFirstObjectByType<FlowFieldManager>();
        if (mainCamera == null) mainCamera = Camera.main;

        // Apply initial biome if set
        if (currentBiome != null)
        {
            ApplyBiome(currentBiome, instant: true);
        }
    }

    /// <summary>
    /// Apply a biome preset immediately
    /// </summary>
    public void ApplyBiome(BiomePreset preset, bool instant = false)
    {
        if (preset == null) return;

        currentBiome = preset;

        if (instant)
        {
            ApplyBiomeInstant(preset);
        }
        else
        {
            StartCoroutine(TransitionToBiome(preset));
        }
    }

    void ApplyBiomeInstant(BiomePreset preset)
    {
        // Background
        if (infiniteBackground != null)
        {
            infiniteBackground.SetGradientColors(preset.backgroundTop, preset.backgroundBottom);
        }

        // Camera
        if (mainCamera != null)
        {
            mainCamera.backgroundColor = preset.cameraBackground;
        }

        // Background generator tint
        if (backgroundGenerator != null)
        {
            backgroundGenerator.environmentTint = preset.environmentTint;
        }

        // Caustics
        if (caustics != null)
        {
            caustics.gameObject.SetActive(preset.enableCaustics);
            caustics.intensity = preset.causticsIntensity;
            caustics.causticsColor = preset.causticsColor;
        }

        // Light shafts
        if (lightShafts != null)
        {
            lightShafts.gameObject.SetActive(preset.enableLightShafts);
            lightShafts.intensity = preset.lightShaftIntensity;
            lightShafts.shaftColor = preset.lightShaftColor;
        }

        // Particles
        if (particles != null)
        {
            particles.densityMultiplier = preset.particleDensity;
            particles.SetColors(preset.debrisColor, preset.planktonColor, preset.bubbleColor, preset.siltColor);
            particles.enableBubbles = preset.enableBubbles;
            particles.currentDirection = preset.currentDirection;
            particles.currentStrength = preset.currentStrength;
        }

        // NOTE: currents are owned by the FlowFieldManager (tune them there). Biome presets no longer
        // overwrite the flow field — doing so clobbered the hand-tuned FlowFieldManager settings.

        // GPU Caustics (via render features - uses global shader properties)
        // Note: Render feature settings need to be updated via direct reference if needed
        // The flow field manager already sets global shader properties that caustics use
    }

    System.Collections.IEnumerator TransitionToBiome(BiomePreset target)
    {
        BiomePreset start = currentBiome ?? BiomePresets.TidePool;
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            float t = elapsed / transitionDuration;
            t = Mathf.SmoothStep(0f, 1f, t); // Ease in/out

            // Lerp all values
            if (infiniteBackground != null)
            {
                Color top = Color.Lerp(start.backgroundTop, target.backgroundTop, t);
                Color bottom = Color.Lerp(start.backgroundBottom, target.backgroundBottom, t);
                infiniteBackground.SetGradientColors(top, bottom);
            }

            if (mainCamera != null)
            {
                mainCamera.backgroundColor = Color.Lerp(start.cameraBackground, target.cameraBackground, t);
            }

            if (caustics != null)
            {
                caustics.intensity = Mathf.Lerp(start.causticsIntensity, target.causticsIntensity, t);
                caustics.causticsColor = Color.Lerp(start.causticsColor, target.causticsColor, t);
            }

            if (lightShafts != null)
            {
                lightShafts.intensity = Mathf.Lerp(start.lightShaftIntensity, target.lightShaftIntensity, t);
            }

            if (particles != null)
            {
                particles.densityMultiplier = Mathf.Lerp(start.particleDensity, target.particleDensity, t);
            }

            // (Currents owned by FlowFieldManager — biome transitions no longer overwrite the flow field.)

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Ensure final state
        ApplyBiomeInstant(target);
    }

    // Context menu shortcuts for testing
    [ContextMenu("Apply Tide Pool")]
    void ApplyTidePool() => ApplyBiome(BiomePresets.TidePool, instant: true);

    [ContextMenu("Apply Clay Bed")]
    void ApplyClayBed() => ApplyBiome(BiomePresets.ClayBed, instant: true);

    [ContextMenu("Apply Hydrothermal Vent")]
    void ApplyHydrothermalVent() => ApplyBiome(BiomePresets.HydrothermalVent, instant: true);

    [ContextMenu("Apply Deep Ocean")]
    void ApplyDeepOcean() => ApplyBiome(BiomePresets.DeepOcean, instant: true);

    [ContextMenu("Apply Bacterial Mat")]
    void ApplyBacterialMat() => ApplyBiome(BiomePresets.BacterialMat, instant: true);

    [ContextMenu("Transition to Tide Pool")]
    void TransitionTidePool() => ApplyBiome(BiomePresets.TidePool, instant: false);

    [ContextMenu("Transition to Deep Ocean")]
    void TransitionDeepOcean() => ApplyBiome(BiomePresets.DeepOcean, instant: false);
}
