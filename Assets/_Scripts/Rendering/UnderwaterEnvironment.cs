using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Controls underwater atmosphere: post-processing, fog density, and visual depth.
/// Attach to a GameObject with a Volume component for localized effects,
/// or use as a global controller.
/// </summary>
public class UnderwaterEnvironment : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Volume to control. If null, will look for one on this GameObject.")]
    public Volume postProcessVolume;

    [Header("Water Color")]
    public Color shallowWaterColor = new Color(0.2f, 0.5f, 0.45f, 1f);
    public Color deepWaterColor = new Color(0.05f, 0.15f, 0.2f, 1f);
    [Range(0f, 1f)]
    public float currentDepth = 0.3f;

    [Header("Murkiness")]
    [Range(0f, 1f)]
    [Tooltip("Overall water clarity. 0 = crystal clear, 1 = extremely murky")]
    public float murkiness = 0.4f;
    [Tooltip("Multiplier on the film grain (the cell stage turns it down at protocell scale).")]
    public float grainScale = 1f;
    [Tooltip("Gaussian depth-of-field blur on/off.")]
    public bool depthOfFieldOn = true;
    [Range(0f, 50f)]
    public float visibilityDistance = 20f;

    [Header("Light")]
    [Range(0f, 2f)]
    public float causticsIntensity = 0.3f;
    [Range(0f, 1f)]
    public float lightShaftIntensity = 0.5f;
    public Color lightTint = new Color(0.7f, 0.9f, 1f, 1f);

    [Header("Atmosphere Animation")]
    public float colorShiftSpeed = 0.1f;
    public float colorShiftAmount = 0.05f;

    // Post-processing overrides
    private Vignette vignette;
    private Bloom bloom;
    private ColorAdjustments colorAdjustments;
    private ChromaticAberration chromaticAberration;
    private FilmGrain filmGrain;
    private DepthOfField depthOfField;

    // Shader globals
    private static readonly int WaterColorID = Shader.PropertyToID("_WaterColor");
    private static readonly int WaterMurkinessID = Shader.PropertyToID("_WaterMurkiness");
    private static readonly int WaterVisibilityID = Shader.PropertyToID("_WaterVisibility");
    private static readonly int CausticsIntensityID = Shader.PropertyToID("_CausticsIntensity");
    private static readonly int UnderwaterTimeID = Shader.PropertyToID("_UnderwaterTime");

    void Start()
    {
        SetupVolume();
        ApplyUnderwaterSettings();
    }

    void Update()
    {
        UpdateShaderGlobals();
        AnimateAtmosphere();
    }

    void SetupVolume()
    {
        if (postProcessVolume == null)
            postProcessVolume = GetComponent<Volume>();

        if (postProcessVolume == null)
        {
            Debug.LogWarning("UnderwaterEnvironment: No Volume found. Creating one.");
            postProcessVolume = gameObject.AddComponent<Volume>();
            postProcessVolume.isGlobal = true;
            postProcessVolume.priority = 1;
        }

        // Get or create profile
        if (postProcessVolume.profile == null)
        {
            postProcessVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        // Get or add components
        if (!postProcessVolume.profile.TryGet(out vignette))
            vignette = postProcessVolume.profile.Add<Vignette>(true);

        if (!postProcessVolume.profile.TryGet(out bloom))
            bloom = postProcessVolume.profile.Add<Bloom>(true);

        if (!postProcessVolume.profile.TryGet(out colorAdjustments))
            colorAdjustments = postProcessVolume.profile.Add<ColorAdjustments>(true);

        if (!postProcessVolume.profile.TryGet(out chromaticAberration))
            chromaticAberration = postProcessVolume.profile.Add<ChromaticAberration>(true);

        if (!postProcessVolume.profile.TryGet(out filmGrain))
            filmGrain = postProcessVolume.profile.Add<FilmGrain>(true);

        if (!postProcessVolume.profile.TryGet(out depthOfField))
            depthOfField = postProcessVolume.profile.Add<DepthOfField>(true);
    }

    /// <summary>
    /// Apply underwater post-processing settings based on current parameters.
    /// </summary>
    public void ApplyUnderwaterSettings()
    {
        if (vignette == null) return;

        // Vignette - darker edges for underwater feel
        vignette.active = true;
        vignette.intensity.Override(0.35f + murkiness * 0.2f);
        vignette.smoothness.Override(0.4f);
        vignette.color.Override(Color.Lerp(shallowWaterColor, deepWaterColor, currentDepth) * 0.3f);

        // Bloom - soft glow for light diffusion through water
        bloom.active = true;
        bloom.threshold.Override(0.8f);
        bloom.intensity.Override(0.3f + lightShaftIntensity * 0.4f);
        bloom.scatter.Override(0.7f);
        bloom.tint.Override(lightTint);

        // Color adjustments - underwater color cast
        colorAdjustments.active = true;
        Color waterTint = Color.Lerp(shallowWaterColor, deepWaterColor, currentDepth);
        colorAdjustments.colorFilter.Override(Color.Lerp(Color.white, waterTint, 0.3f + murkiness * 0.3f));
        colorAdjustments.saturation.Override(-10f - murkiness * 20f); // Desaturate with depth/murkiness
        colorAdjustments.contrast.Override(5f - murkiness * 15f); // Lower contrast in murky water

        // Chromatic aberration - water refraction
        chromaticAberration.active = true;
        chromaticAberration.intensity.Override(0.1f + murkiness * 0.15f);

        // Film grain - organic underwater texture
        filmGrain.active = true;
        filmGrain.type.Override(FilmGrainLookup.Medium3);
        filmGrain.intensity.Override((0.15f + murkiness * 0.2f) * grainScale);
        filmGrain.active = grainScale > 0.001f;
        filmGrain.response.Override(0.5f);

        // Depth of field - visibility falloff
        depthOfField.active = depthOfFieldOn;
        depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
        depthOfField.gaussianStart.Override(visibilityDistance * 0.5f);
        depthOfField.gaussianEnd.Override(visibilityDistance);
        depthOfField.gaussianMaxRadius.Override(1f + murkiness * 0.5f);
    }

    void UpdateShaderGlobals()
    {
        Color currentWaterColor = Color.Lerp(shallowWaterColor, deepWaterColor, currentDepth);
        Shader.SetGlobalColor(WaterColorID, currentWaterColor);
        Shader.SetGlobalFloat(WaterMurkinessID, murkiness);
        Shader.SetGlobalFloat(WaterVisibilityID, visibilityDistance);
        Shader.SetGlobalFloat(CausticsIntensityID, causticsIntensity);
        Shader.SetGlobalFloat(UnderwaterTimeID, Time.time);
    }

    void AnimateAtmosphere()
    {
        // Subtle color shift over time for organic feel
        float shift = Mathf.Sin(Time.time * colorShiftSpeed) * colorShiftAmount;

        if (colorAdjustments != null)
        {
            colorAdjustments.hueShift.Override(shift * 10f);
        }
    }

    /// <summary>
    /// Set depth (0 = surface, 1 = deep). Updates all visuals accordingly.
    /// </summary>
    public void SetDepth(float depth)
    {
        currentDepth = Mathf.Clamp01(depth);
        ApplyUnderwaterSettings();
    }

    /// <summary>
    /// Set murkiness level. Use for biome transitions.
    /// </summary>
    public void SetMurkiness(float newMurkiness)
    {
        murkiness = Mathf.Clamp01(newMurkiness);
        ApplyUnderwaterSettings();
    }

    /// <summary>
    /// Transition to new water parameters over time.
    /// </summary>
    public void TransitionTo(Color newShallowColor, Color newDeepColor, float newMurkiness, float duration)
    {
        StartCoroutine(TransitionCoroutine(newShallowColor, newDeepColor, newMurkiness, duration));
    }

    private System.Collections.IEnumerator TransitionCoroutine(Color targetShallow, Color targetDeep, float targetMurkiness, float duration)
    {
        Color startShallow = shallowWaterColor;
        Color startDeep = deepWaterColor;
        float startMurkiness = murkiness;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t); // Smoothstep

            shallowWaterColor = Color.Lerp(startShallow, targetShallow, t);
            deepWaterColor = Color.Lerp(startDeep, targetDeep, t);
            murkiness = Mathf.Lerp(startMurkiness, targetMurkiness, t);

            ApplyUnderwaterSettings();
            yield return null;
        }

        shallowWaterColor = targetShallow;
        deepWaterColor = targetDeep;
        murkiness = targetMurkiness;
        ApplyUnderwaterSettings();
    }

    void OnValidate()
    {
        if (Application.isPlaying && postProcessVolume != null)
        {
            ApplyUnderwaterSettings();
        }
    }
}
