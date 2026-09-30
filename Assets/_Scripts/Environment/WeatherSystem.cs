using UnityEngine;

/// <summary>
/// Controls weather effects that influence water distortion and environment visuals.
/// Sets global shader properties that affect all environment sprites using EnvironmentDistortion shader.
/// Can be triggered by server events, biome changes, or local gameplay events.
/// </summary>
public class WeatherSystem : MonoBehaviour
{
    public static WeatherSystem Instance { get; private set; }

    [Header("Current Weather State")]
    [Range(0f, 1f)]
    [Tooltip("Storm intensity - adds chaotic distortion and darkens water")]
    public float stormIntensity = 0f;

    [Range(0f, 2f)]
    [Tooltip("Wind strength - adds directional water movement")]
    public float windStrength = 0f;

    [Tooltip("Wind direction (normalized)")]
    public Vector2 windDirection = new Vector2(1f, 0f);

    [Range(0f, 1f)]
    [Tooltip("Turbulence - adds random variation to wind")]
    public float turbulence = 0.2f;

    [Header("Camera Distortion Modifiers")]
    [Range(0f, 2f)]
    [Tooltip("Multiplier for camera-wide distortion during weather events")]
    public float cameraDistortionMultiplier = 1f;

    [Range(0f, 1f)]
    [Tooltip("How much the player cell is protected from distortion (0 = full effect, 1 = no effect)")]
    public float cellProtection = 0.7f;

    [Header("Transition Settings")]
    [Tooltip("How fast weather transitions happen")]
    public float transitionSpeed = 0.5f;

    [Header("Ambient Effects")]
    [Range(0f, 1f)]
    [Tooltip("Overall visibility reduction during storms")]
    public float visibilityReduction = 0f;

    [Tooltip("Color tint applied during storms")]
    public Color stormTint = new Color(0.7f, 0.75f, 0.65f, 1f);

    // Target values for smooth transitions
    private float targetStormIntensity;
    private float targetWindStrength;
    private Vector2 targetWindDirection;
    private float targetTurbulence;

    // Shader property IDs
    private static readonly int StormIntensityID = Shader.PropertyToID("_WeatherStormIntensity");
    private static readonly int WindStrengthID = Shader.PropertyToID("_WeatherWindStrength");
    private static readonly int WindDirectionID = Shader.PropertyToID("_WeatherWindDirection");
    private static readonly int TurbulenceID = Shader.PropertyToID("_WeatherTurbulence");
    private static readonly int VisibilityID = Shader.PropertyToID("_WeatherVisibility");
    private static readonly int StormTintID = Shader.PropertyToID("_WeatherStormTint");

    // Reference to camera distortion (optional)
    private WaterDistortionCamera cameraDistortion;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Initialize targets to current values
        targetStormIntensity = stormIntensity;
        targetWindStrength = windStrength;
        targetWindDirection = windDirection;
        targetTurbulence = turbulence;
    }

    void Start()
    {
        // Find camera distortion if it exists
        if (Camera.main != null)
        {
            cameraDistortion = Camera.main.GetComponent<WaterDistortionCamera>();
        }

        // Initialize shader globals
        UpdateShaderProperties();
    }

    void Update()
    {
        // Smooth transitions
        float t = Time.deltaTime * transitionSpeed;
        stormIntensity = Mathf.Lerp(stormIntensity, targetStormIntensity, t);
        windStrength = Mathf.Lerp(windStrength, targetWindStrength, t);
        windDirection = Vector2.Lerp(windDirection, targetWindDirection, t).normalized;
        turbulence = Mathf.Lerp(turbulence, targetTurbulence, t);

        // Update visibility based on storm
        visibilityReduction = stormIntensity * 0.4f;

        // Update shader properties
        UpdateShaderProperties();

        // Update camera distortion if present
        UpdateCameraDistortion();
    }

    void UpdateShaderProperties()
    {
        Shader.SetGlobalFloat(StormIntensityID, stormIntensity);
        Shader.SetGlobalFloat(WindStrengthID, windStrength);
        Shader.SetGlobalVector(WindDirectionID, windDirection);
        Shader.SetGlobalFloat(TurbulenceID, turbulence);
        Shader.SetGlobalFloat(VisibilityID, 1f - visibilityReduction);
        Shader.SetGlobalColor(StormTintID, stormTint);
    }

    void UpdateCameraDistortion()
    {
        if (cameraDistortion == null) return;

        // Modify camera distortion based on weather
        // Base distortion is boosted by storm, wind adds to it
        float weatherBoost = 1f + (stormIntensity * 0.5f) + (windStrength * 0.2f);
        // This could be used to dynamically adjust camera effect
        // cameraDistortion.distortionStrength = baseStrength * weatherBoost * cameraDistortionMultiplier;
    }

    #region Public API for Server/Gameplay Events

    /// <summary>
    /// Set storm intensity (0-1). Affects distortion chaos and water color.
    /// </summary>
    public void SetStormIntensity(float intensity)
    {
        targetStormIntensity = Mathf.Clamp01(intensity);
    }

    /// <summary>
    /// Set wind parameters. Affects directional water movement.
    /// </summary>
    public void SetWind(Vector2 direction, float strength)
    {
        targetWindDirection = direction.normalized;
        targetWindStrength = Mathf.Clamp(strength, 0f, 2f);
    }

    /// <summary>
    /// Set turbulence level (0-1). Adds randomness to water movement.
    /// </summary>
    public void SetTurbulence(float amount)
    {
        targetTurbulence = Mathf.Clamp01(amount);
    }

    /// <summary>
    /// Trigger a storm event with specified intensity and duration.
    /// </summary>
    public void TriggerStorm(float intensity, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(StormRoutine(intensity, duration));
    }

    /// <summary>
    /// Trigger a wind gust event.
    /// </summary>
    public void TriggerWindGust(Vector2 direction, float strength, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(WindGustRoutine(direction, strength, duration));
    }

    /// <summary>
    /// Set calm weather conditions.
    /// </summary>
    public void SetCalm()
    {
        targetStormIntensity = 0f;
        targetWindStrength = 0f;
        targetTurbulence = 0.1f;
    }

    /// <summary>
    /// Set weather from a preset (can be extended for biome-specific weather).
    /// </summary>
    public void ApplyWeatherPreset(WeatherPreset preset)
    {
        targetStormIntensity = preset.stormIntensity;
        targetWindStrength = preset.windStrength;
        targetWindDirection = preset.windDirection;
        targetTurbulence = preset.turbulence;
        stormTint = preset.tint;
    }

    #endregion

    #region Coroutines

    private System.Collections.IEnumerator StormRoutine(float intensity, float duration)
    {
        float originalIntensity = targetStormIntensity;
        targetStormIntensity = intensity;

        yield return new WaitForSeconds(duration);

        targetStormIntensity = originalIntensity;
    }

    private System.Collections.IEnumerator WindGustRoutine(Vector2 direction, float strength, float duration)
    {
        float originalStrength = targetWindStrength;
        Vector2 originalDirection = targetWindDirection;

        targetWindDirection = direction.normalized;
        targetWindStrength = strength;

        yield return new WaitForSeconds(duration);

        targetWindStrength = originalStrength;
        targetWindDirection = originalDirection;
    }

    #endregion

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}

/// <summary>
/// Weather preset for biome-specific or event-specific weather conditions.
/// </summary>
[System.Serializable]
public class WeatherPreset
{
    public string name = "Default";
    [Range(0f, 1f)] public float stormIntensity = 0f;
    [Range(0f, 2f)] public float windStrength = 0.2f;
    public Vector2 windDirection = Vector2.right;
    [Range(0f, 1f)] public float turbulence = 0.2f;
    public Color tint = new Color(0.8f, 0.9f, 1f, 1f);

    // Preset factory methods
    public static WeatherPreset Calm => new WeatherPreset
    {
        name = "Calm",
        stormIntensity = 0f,
        windStrength = 0.1f,
        windDirection = Vector2.right,
        turbulence = 0.1f,
        tint = new Color(0.85f, 0.92f, 1f, 1f)
    };

    public static WeatherPreset Breezy => new WeatherPreset
    {
        name = "Breezy",
        stormIntensity = 0f,
        windStrength = 0.5f,
        windDirection = new Vector2(1f, 0.3f).normalized,
        turbulence = 0.3f,
        tint = new Color(0.8f, 0.9f, 1f, 1f)
    };

    public static WeatherPreset Stormy => new WeatherPreset
    {
        name = "Stormy",
        stormIntensity = 0.7f,
        windStrength = 1.2f,
        windDirection = new Vector2(1f, -0.5f).normalized,
        turbulence = 0.6f,
        tint = new Color(0.6f, 0.65f, 0.55f, 1f)
    };

    public static WeatherPreset DeepCurrent => new WeatherPreset
    {
        name = "Deep Current",
        stormIntensity = 0.2f,
        windStrength = 0.8f,
        windDirection = Vector2.down,
        turbulence = 0.4f,
        tint = new Color(0.5f, 0.6f, 0.7f, 1f)
    };
}
