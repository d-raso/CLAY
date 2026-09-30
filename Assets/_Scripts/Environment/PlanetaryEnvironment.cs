using UnityEngine;

/// <summary>
/// Samples world-position to derive wildly varying local environment conditions.
/// Small position changes can cause large parameter swings — intentionally exaggerated.
/// Also holds global planetary baseline values.
/// </summary>
public class PlanetaryEnvironment : MonoBehaviour
{
    public static PlanetaryEnvironment Instance { get; private set; }

    [Header("Planetary Baseline")]
    [Range(-90f,90f)]   public float latitude        = 0f;
    [Range(-180f,180f)] public float longitude       = 0f;
    [Range(0f,14f)]     public float baseAcidity     = 7f;
    [Range(0f,50f)]     public float baseSalinity    = 35f;
    [Range(-5f,120f)]   public float baseTemperature = 15f;
    [Range(0.5f,5f)]    public float baseViscosity   = 1f;

    [Header("Spatial Chaos")]
    [Tooltip("How zoomed-in the noise is — smaller = more variation per unit of movement")]
    public float noiseFrequency = 0.04f;
    [Tooltip("How extreme the swings are — higher = wilder environment changes")]
    [Range(1f,10f)]
    public float chaosFactor = 4f;
    [Tooltip("How fast local conditions update as player moves")]
    public float updateRate = 0.2f;


    [Header("Biome Placement")]
    [Tooltip("World-space frequency of biome regions (smaller = larger biomes)")]
    public float biomeScale = 0.012f;
    [Tooltip("How fast biome colors cross-fade as you move between regions")]
    public float biomeBlendSpeed = 1.5f;

    [Header("Live Readout (read-only in play mode)")]
    [SerializeField] float localTemperature;
    [SerializeField] float localAcidity;
    [SerializeField] float localTurbulence;
    [SerializeField] float localViscosity;
    [SerializeField] float localNutrients;
    [SerializeField] float localCurrentStrength;

    FlowFieldManager     flowField;
    JellyMovement        player;
    EnvironmentController envController;
    float nextUpdate;

    // Smoothed biome profile (cross-fades toward the sampled target)
    BiomeLibrary.Profile biome;
    bool biomeInit;

    static readonly int IdDeep      = Shader.PropertyToID("_BiomeDeepColor");
    static readonly int IdMid       = Shader.PropertyToID("_BiomeMidColor");
    static readonly int IdShallow   = Shader.PropertyToID("_BiomeShallowColor");
    static readonly int IdMurk      = Shader.PropertyToID("_BiomeMurkiness");
    static readonly int IdCausStr   = Shader.PropertyToID("_CausticStrength");
    static readonly int IdCausCol   = Shader.PropertyToID("_CausticColor");
    static readonly int IdPartCol   = Shader.PropertyToID("_BiomeParticleColor");
    static readonly int IdPartDen   = Shader.PropertyToID("_BiomeParticleDensity");
    static readonly int IdOrgCol    = Shader.PropertyToID("_BiomeOrganicColor");
    static readonly int IdOrgDen    = Shader.PropertyToID("_BiomeOrganicDensity");

    void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; }

    void Start()
    {
        flowField     = FindFirstObjectByType<FlowFieldManager>();
        player        = FindFirstObjectByType<JellyMovement>();
        envController = FindFirstObjectByType<EnvironmentController>();
        SampleAndApply(Vector3.zero);
    }

    void Update()
    {
        if (Time.time < nextUpdate) return;
        nextUpdate = Time.time + updateRate;
        Vector3 pos = player != null ? player.transform.position : Vector3.zero;
        SampleAndApply(pos);
    }

    // Multi-octave noise at world position — small freq + chaos = dramatic local shifts
    float Sample(float x, float y, float seed, int octaves = 3)
    {
        float f = noiseFrequency;
        float v = 0f, amp = 1f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            v     += Mathf.PerlinNoise(x * f + seed, y * f + seed * 1.3f) * amp;
            total += amp;
            f     *= 2.1f;
            amp   *= 0.55f;
        }
        return v / total;
    }

    void SampleAndApply(Vector3 pos)
    {
        float x = pos.x, y = pos.y;

        // Each parameter gets its own noise seed so they vary independently
        float tNoise  = Sample(x, y, 0f);
        float aNoise  = Sample(x, y, 47f);
        float turNoise = Sample(x, y, 91f);
        float visNoise = Sample(x, y, 133f);
        float nutNoise = Sample(x, y, 179f);
        float curNoise = Sample(x, y, 223f);

        // Apply chaos: remap 0-1 noise to full parameter ranges, amplified
        float cf = chaosFactor;
        localTemperature    = Mathf.Lerp(baseTemperature - 30f * cf, baseTemperature + 30f * cf, tNoise);
        localAcidity        = Mathf.Clamp(Mathf.Lerp(baseAcidity - 3f * cf, baseAcidity + 3f * cf, aNoise), 0f, 14f);
        localTurbulence     = Mathf.Clamp01(turNoise * cf * 0.3f);
        localViscosity      = Mathf.Clamp(Mathf.Lerp(baseViscosity * 0.3f, baseViscosity * cf, visNoise), 0.1f, 8f);
        localNutrients      = Mathf.Clamp01(nutNoise);
        localCurrentStrength = Mathf.Clamp(curNoise * cf * 0.6f, 0f, 3f);

        // NOTE: the water CURRENTS are now owned entirely by the FlowFieldManager (tune turbulence,
        // noiseScale, noiseSpeed, meander, jets there). PlanetaryEnvironment no longer overwrites them —
        // it used to clobber your FlowFieldManager settings every frame. Spatial calm/rough variation
        // lives in FlowFieldManager's own world-fixed turbulence map instead.
        if (player != null)
            player.drag = localViscosity * 2f;

        // ── Biome system (broad climate → medium blobs → fine shader noise) ──
        // BROAD: climate temperature from latitude (equator warm, poles cold) + planet base.
        float latFactor   = 1f - Mathf.Abs(latitude) / 90f;        // 1 equator .. 0 pole
        float climateTemp = baseTemperature + (latFactor * 2f - 1f) * 25f
                          + (Sample(x, y, 311f, 2) - 0.5f) * 20f;   // broad regional drift

        // MEDIUM: blend biomes by climate gate × spatial presence blobs at this position.
        var target = BiomeLibrary.Blend(new Vector2(x, y), climateTemp, biomeScale);

        if (!biomeInit) { biome = target; biomeInit = true; }
        else
        {
            float k = 1f - Mathf.Exp(-biomeBlendSpeed * updateRate);
            biome.deep            = Color.Lerp(biome.deep,    target.deep,    k);
            biome.mid             = Color.Lerp(biome.mid,     target.mid,     k);
            biome.shallow         = Color.Lerp(biome.shallow, target.shallow, k);
            biome.causticColor    = Color.Lerp(biome.causticColor,  target.causticColor,  k);
            biome.particleColor   = Color.Lerp(biome.particleColor, target.particleColor, k);
            biome.organicColor    = Color.Lerp(biome.organicColor,  target.organicColor,  k);
            biome.murkiness       = Mathf.Lerp(biome.murkiness,       target.murkiness,       k);
            biome.causticStrength = Mathf.Lerp(biome.causticStrength, target.causticStrength, k);
            biome.particleDensity = Mathf.Lerp(biome.particleDensity, target.particleDensity, k);
            biome.organicDensity  = Mathf.Lerp(biome.organicDensity,  target.organicDensity,  k);
        }

        if (envController != null && envController.currentBiome != null)
        {
            envController.currentBiome.backgroundTop    = biome.shallow;
            envController.currentBiome.backgroundBottom = biome.deep;
            envController.currentBiome.flowTurbulence   = localTurbulence;
            envController.currentBiome.currentStrength  = localCurrentStrength;
        }

        // ── Global shader properties ──
        Shader.SetGlobalFloat("_WaterTemperature",    localTemperature);
        Shader.SetGlobalFloat("_WaterAcidity",        localAcidity);
        Shader.SetGlobalFloat("_WaterViscosity",      localViscosity);
        Shader.SetGlobalFloat("_NutrientDensity",     localNutrients);
        Shader.SetGlobalFloat("_WaterTurbulence",     localTurbulence);

        Shader.SetGlobalColor(IdDeep,    biome.deep);
        Shader.SetGlobalColor(IdMid,     biome.mid);
        Shader.SetGlobalColor(IdShallow, biome.shallow);
        Shader.SetGlobalFloat(IdMurk,    biome.murkiness);
        Shader.SetGlobalFloat(IdCausStr, biome.causticStrength);
        Shader.SetGlobalColor(IdCausCol, biome.causticColor);
        Shader.SetGlobalColor(IdPartCol, biome.particleColor);
        Shader.SetGlobalFloat(IdPartDen, biome.particleDensity);
        Shader.SetGlobalColor(IdOrgCol,  biome.organicColor);
        Shader.SetGlobalFloat(IdOrgDen,  biome.organicDensity);

        // Keep _WaterEnvColor for any legacy reader — use the mid biome tone.
        Shader.SetGlobalColor("_WaterEnvColor", biome.mid);
    }

    /// <summary>Current blended biome — read by particle/organic layers for tinting.</summary>
    public BiomeLibrary.Profile CurrentBiome => biome;
}
