using UnityEngine;
using System.Collections.Generic;

public class FlowFieldManager : MonoBehaviour
{
    public static FlowFieldManager Instance { get; private set; }
    public enum QualityLevel { Low, Medium, High }

    [Header("Quality")]
    public QualityLevel quality = QualityLevel.Medium;

    [Header("Base Flow")]
    public Vector2 baseDirection = Vector2.zero;
    [Range(0f,2f)] public float baseStrength = 0.1f;

    [Header("Curl Noise Currents")]
    [Range(0f,5f)] public float turbulence = 2.5f;
    public float noiseScale = 10f;
    public float noiseSpeed = 0.25f;

    [Header("Current Structure")]
    [Tooltip("How much currents meander/snake (domain warp). 0 = round eddies, higher = long winding streams.")]
    [Range(0f,3f)] public float meander = 1.3f;
    [Tooltip("Overall magnitude calibration for the curl. Leave ~1; tune strength via PlanetaryEnvironment.")]
    public float curlGain = 1.6f;
    [Header("Jet Streams")]
    [Tooltip("Strength of the localized fast jet-stream channels woven through the field (0 = none).")]
    [Range(0f,4f)] public float jetStrength = 1.6f;
    [Tooltip("World size of the jet channels — larger = broader, farther-apart jets.")]
    public float jetScale = 70f;
    [Tooltip("Number of parallel meandering jet bands per region.")]
    [Range(1f,6f)] public float jetBands = 2.5f;

    [Header("Liveliness Map (calm ↔ lively regions)")]
    [Tooltip("World size of the calm / still / lively regions. Larger = broader zones.")]
    public float energyRegionScale = 28f;
    [Tooltip("Below this map value the water is DEAD STILL. Higher = MORE still water overall.")]
    [Range(0f,1f)] public float stillThreshold = 0.5f;
    [Tooltip("Above this map value the water is fully lively. The gap to stillThreshold is the width of the transitional streams.")]
    [Range(0f,1f)] public float livelyThreshold = 0.72f;
    [Tooltip("How energetic the liveliest areas get, relative to the base current.")]
    [Range(1f,6f)] public float livelyBoost = 4f;

    [Header("Weather")]
    public bool useWeatherSystem = true;
    [Range(0f,1f)] public float weatherInfluence = 0.7f;

    [Header("Field Bounds")]
    public Vector2 fieldSize = new Vector2(100f, 100f);
    public Transform fieldCenter;

    [Header("GPU (optional)")]
    public bool useGPU = false;
    public ComputeShader flowFieldCompute;

    int Resolution => quality switch { QualityLevel.Low => 32, QualityLevel.High => 128, _ => 64 };

    RenderTexture flowFieldTexture;
    int computeKernel;
    bool gpuInitialized;
    Vector2[,] flowGrid;
    Bounds fieldBounds;
    List<FlowFieldAffector> affectors = new();

    static readonly int TexID        = Shader.PropertyToID("_FlowFieldTexture");
    static readonly int BMinID       = Shader.PropertyToID("_FlowFieldBoundsMin");
    static readonly int BMaxID       = Shader.PropertyToID("_FlowFieldBoundsMax");
    static readonly int FlowDirID    = Shader.PropertyToID("_GlobalFlowDirection");
    static readonly int LocalFlowID  = Shader.PropertyToID("_LocalFlowStrength");

    Vector2 effectiveDirection;
    float effectiveStrength, effectiveTurbulence;

    void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; }

    void Start()
    {
        if (fieldCenter == null) fieldCenter = Camera.main?.transform;
        flowGrid = new Vector2[Resolution, Resolution];
        if (useGPU && flowFieldCompute != null && SystemInfo.supportsComputeShaders) InitGPU();
        else useGPU = false;
        UpdateBounds();
    }

    void InitGPU()
    {
        int res = Resolution;
        flowFieldTexture = new RenderTexture(res, res, 0, RenderTextureFormat.RGFloat)
            { enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        flowFieldTexture.Create();
        computeKernel = flowFieldCompute.FindKernel("CSMain");
        if (computeKernel < 0) { useGPU = false; return; }
        flowFieldCompute.SetTexture(computeKernel, "Result", flowFieldTexture);
        Shader.SetGlobalTexture(TexID, flowFieldTexture);
        gpuInitialized = true;
    }

    void Update()
    {
        UpdateBounds();
        UpdateWeather();
        if (useGPU && gpuInitialized) UpdateGPU(); else UpdateCPU();
        Shader.SetGlobalVector(BMinID, fieldBounds.min);
        Shader.SetGlobalVector(BMaxID, fieldBounds.max);

        // Push the actual local curl-noise flow at the camera center so the
        // distortion shader and caustics animate with the real current direction.
        Vector2 camPos   = fieldCenter != null ? (Vector2)fieldCenter.position : Vector2.zero;
        Vector2 camFlow  = SampleFlowAtPosition(camPos);
        Shader.SetGlobalVector(FlowDirID, new Vector4(camFlow.x, camFlow.y, 0f, 0f));
        Shader.SetGlobalFloat(LocalFlowID, camFlow.magnitude);
    }

    void UpdateWeather()
    {
        effectiveDirection  = baseDirection;
        effectiveStrength   = baseStrength;
        effectiveTurbulence = turbulence;
        if (!useWeatherSystem || WeatherSystem.Instance == null) return;
        var w = WeatherSystem.Instance;
        Vector2 wind = w.windDirection * w.windStrength * weatherInfluence;
        effectiveDirection = (effectiveDirection + wind).normalized;
        effectiveStrength  += w.windStrength * 0.5f * weatherInfluence;
        // Weather adds swirl on top of the base turbulence. Don't hard-clamp to 1 — that silently capped
        // the whole current no matter how high turbulence was set. Allow a generous ceiling instead.
        effectiveTurbulence = Mathf.Min(effectiveTurbulence + (w.stormIntensity + w.turbulence * 0.5f) * weatherInfluence, 8f);
    }

    void UpdateBounds()
    {
        // Keep the field centred on the view so it always covers the player. If no explicit centre is
        // set, follow the main camera (re-fetching in case it wasn't ready at Start).
        if (fieldCenter == null) fieldCenter = Camera.main != null ? Camera.main.transform : null;
        Vector3 c = fieldCenter != null ? fieldCenter.position : Vector3.zero;
        fieldBounds = new Bounds(c, new Vector3(fieldSize.x, fieldSize.y, 1f));
    }

    // Integer-lattice value noise. Unlike Mathf.PerlinNoise (which goes flat at large coordinates —
    // killing the swirl far from origin and as the time offset grows), this hashes wrapped integer
    // lattice points, so it stays crisp and varied at ANY world position or time.
    static float Hash(int x, int y)
    {
        x &= 1023; y &= 1023;                       // wrap lattice → precision-safe, seamless tiling
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }
    static float VNoise(float x, float y)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
        float a = Hash(xi, yi),     b = Hash(xi + 1, yi);
        float c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }
    // 3-octave fractional Brownian motion for richer swirling detail
    static float Fbm(float x, float y)
    {
        float val = VNoise(x, y) * 0.50f;
        val += VNoise(x * 2.03f + 3.7f, y * 2.03f + 1.3f) * 0.30f;
        val += VNoise(x * 4.07f + 7.1f, y * 4.07f + 5.9f) * 0.20f;
        return val;
    }

    // ── Stream function ψ(world) ────────────────────────────────────────────────────────────────
    // Flow is the curl of a scalar potential: v = (∂ψ/∂y, -∂ψ/∂x). Because it comes from a potential,
    // the flow is divergence-free and its streamlines are the CONTOURS of ψ — continuous, connected
    // currents (not independent per-point vectors). Big smooth ψ features = broad coherent currents;
    // where contours bunch, the flow speeds up into JETS.
    float StreamFunction(float wx, float wy, float t)
    {
        float s = Mathf.Max(1f, noiseScale);
        float nx = wx / s, ny = wy / s + t;

        // Domain warp: bend the sampling space so currents snake and braid instead of forming round blobs.
        float wX = (Fbm(nx * 0.5f + 11.3f, ny * 0.5f + 7.7f) - 0.5f) * meander;
        float wY = (Fbm(nx * 0.5f + 31.1f, ny * 0.5f + 17.9f) - 0.5f) * meander;

        // Background: large-scale, low-octave potential → broad, gentle, connected currents.
        float psi = (VNoise(nx + wX, ny + wY) - 0.5f)
                  + (VNoise((nx + wX) * 2.1f + 5f, (ny + wY) * 2.1f + 9f) - 0.5f) * 0.35f;

        // Jets: sin() of a slow large-scale field makes a few meandering bands whose contours are fast,
        // narrow channels — jet streams. Masked so they're localized to parts of the pool, not everywhere.
        if (jetStrength > 0f)
        {
            float js = Mathf.Max(1f, jetScale);
            float band = VNoise(wx / js + 50f, wy / js + 90f);              // 0..1, slowly varying
            float mask = Mathf.SmoothStep(0.35f, 0.75f, VNoise(wx / (js * 1.7f) + 200f, wy / (js * 1.7f) + 300f));
            psi += Mathf.Sin(band * Mathf.PI * 2f * jetBands) * (jetStrength * 0.5f) * mask;
        }
        return psi;
    }

    // Flow = curl of the stream function, via central differences (keeps it divergence-free).
    Vector2 CurlNoise(float wx, float wy, float time, float scale, float speed)
    {
        float t = time * speed;
        float e = Mathf.Max(0.05f, scale * 0.12f);                          // world-space sample step
        float dpsidx = StreamFunction(wx + e, wy, t) - StreamFunction(wx - e, wy, t);
        float dpsidy = StreamFunction(wx, wy + e, t) - StreamFunction(wx, wy - e, t);
        // v = (∂ψ/∂y, -∂ψ/∂x); scale-normalised so magnitude doesn't change when you resize the eddies.
        return new Vector2(dpsidy, -dpsidx) / (2f * e) * scale * curlGain;
    }

    // World-fixed liveliness map: mostly CALM/STILL, with sparse LIVELY patches. Biased low (e²) so the
    // pool is calm by default, dead-still where the map is below stillThreshold, and energetic only in a
    // few regions. Multiplies the whole current, so still zones truly stand still and lively zones surge.
    float EnergyMap(float wx, float wy)
    {
        float scale = Mathf.Max(1f, energyRegionScale);
        float n = VNoise(wx / scale + 500f, wy / scale + 500f);        // 0..1, fixed in world space
        float hi = Mathf.Max(livelyThreshold, stillThreshold + 0.05f);
        float e = Mathf.SmoothStep(stillThreshold, hi, n);             // 0 in still zones .. 1 in lively
        return e * e * livelyBoost;                                    // e² keeps most of the pool gentle
    }

    void UpdateCPU()
    {
        int res = Resolution;
        float time = Time.time;
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float wx = Mathf.Lerp(fieldBounds.min.x, fieldBounds.max.x, (float)x / (res - 1));
            float wy = Mathf.Lerp(fieldBounds.min.y, fieldBounds.max.y, (float)y / (res - 1));
            Vector2 flow = (CurlNoise(wx, wy, time, noiseScale, noiseSpeed) * effectiveTurbulence
                            + effectiveDirection * effectiveStrength)
                         * EnergyMap(wx, wy);   // gate by liveliness → still zones truly stand still
            foreach (var a in affectors) if (a != null && a.enabled) flow += a.GetFlowContribution(new Vector2(wx, wy));
            flowGrid[x, y] = flow;
        }
    }

    void UpdateGPU()
    {
        int res = Resolution;
        flowFieldCompute.SetFloat("_Time", Time.time);
        flowFieldCompute.SetVector("_BaseFlow", new Vector4(effectiveDirection.x, effectiveDirection.y, 0, 0) * effectiveStrength);
        flowFieldCompute.SetFloat("_Turbulence", effectiveTurbulence);
        flowFieldCompute.SetFloat("_NoiseScale", noiseScale);
        flowFieldCompute.SetFloat("_NoiseSpeed", noiseSpeed);
        flowFieldCompute.SetVector("_FieldMin", fieldBounds.min);
        flowFieldCompute.SetVector("_FieldMax", fieldBounds.max);
        flowFieldCompute.SetInt("_Resolution", res);
        const int max = 16;
        Vector4[] pos = new Vector4[max], par = new Vector4[max];
        int cnt = Mathf.Min(affectors.Count, max);
        for (int i = 0; i < cnt; i++)
        {
            var a = affectors[i];
            if (a == null || !a.enabled) continue;
            Vector3 p = a.transform.position;
            pos[i] = new Vector4(p.x, p.y, a.radius, (float)a.affectorType);
            par[i] = new Vector4(a.strength, a.rotationSpeed, a.falloff, 0);
        }
        flowFieldCompute.SetInt("_AffectorCount", cnt);
        flowFieldCompute.SetVectorArray("_AffectorPositions", pos);
        flowFieldCompute.SetVectorArray("_AffectorParams", par);
        int g = Mathf.CeilToInt(res / 8f);
        flowFieldCompute.Dispatch(computeKernel, g, g, 1);
    }

    /// <summary>The full analytic flow at a world position (curl swirl + base drift). The grid is just a
    /// cached sampling of this over the affector region; anywhere outside it we evaluate directly so the
    /// current is correct EVERYWHERE — not a flat uniform drift once you roam past the bounds.</summary>
    Vector2 EvaluateFlow(Vector2 p) =>
        (CurlNoise(p.x, p.y, Time.time, noiseScale, noiseSpeed) * effectiveTurbulence
         + effectiveDirection * effectiveStrength) * EnergyMap(p.x, p.y);

    public Vector2 SampleFlowAtPosition(Vector3 p) => SampleFlowAtPosition((Vector2)p);
    public Vector2 SampleFlowAtPosition(Vector2 p)
    {
        // Outside the cached grid (e.g. roamed far from origin): evaluate the field analytically so the
        // swirl is still present, instead of collapsing to a flat drift.
        if (flowGrid == null || !fieldBounds.Contains(new Vector3(p.x, p.y, 0))) return EvaluateFlow(p);
        int res = Resolution;
        float gx = Mathf.InverseLerp(fieldBounds.min.x, fieldBounds.max.x, p.x) * (res - 1);
        float gy = Mathf.InverseLerp(fieldBounds.min.y, fieldBounds.max.y, p.y) * (res - 1);
        int x0 = Mathf.FloorToInt(gx), y0 = Mathf.FloorToInt(gy);
        int x1 = Mathf.Min(x0+1,res-1), y1 = Mathf.Min(y0+1,res-1);
        float fx = gx-x0, fy = gy-y0;
        return Vector2.Lerp(Vector2.Lerp(flowGrid[x0,y0],flowGrid[x1,y0],fx), Vector2.Lerp(flowGrid[x0,y1],flowGrid[x1,y1],fx), fy);
    }

    public void RegisterAffector(FlowFieldAffector a)   { if (!affectors.Contains(a)) affectors.Add(a); }
    public void UnregisterAffector(FlowFieldAffector a) { affectors.Remove(a); }
    public void SetBaseFlow(Vector2 dir, float strength) { baseDirection = dir.normalized; baseStrength = strength; }
    public void SetTurbulence(float amt, float scale)    { turbulence = amt; noiseScale = scale; }
    public void SetQuality(QualityLevel q) { if (q == quality) return; quality = q; Cleanup(); Start(); }
    public RenderTexture GetFlowFieldTexture()  => flowFieldTexture;
    public Vector2 GetEffectiveDirection()      => effectiveDirection;
    public float   GetEffectiveStrength()       => effectiveStrength;
    public float   GetEffectiveTurbulence()     => effectiveTurbulence;

    void Cleanup() { if (flowFieldTexture != null) { flowFieldTexture.Release(); Destroy(flowFieldTexture); flowFieldTexture = null; } gpuInitialized = false; }
    void OnDestroy() { Cleanup(); if (Instance == this) Instance = null; }

    void OnDrawGizmosSelected()
    {
        Vector3 c = fieldCenter != null ? fieldCenter.position : transform.position;
        Gizmos.color = new Color(0.3f,0.6f,1f,0.3f);
        Gizmos.DrawWireCube(c, new Vector3(fieldSize.x, fieldSize.y, 0.1f));
        Gizmos.color = new Color(0f,0.5f,0.5f,0.5f);
        Gizmos.DrawRay(c, (Vector3)(baseDirection * baseStrength * 5f));
        if (!Application.isPlaying || flowGrid == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(c, (Vector3)(effectiveDirection * effectiveStrength * 5f));
        Gizmos.color = new Color(0.5f,0.8f,1f,0.5f);
        int step = Mathf.Max(1, Resolution / 8);
        for (int y = 0; y < Resolution; y += step)
        for (int x = 0; x < Resolution; x += step)
        {
            Vector3 p = new(Mathf.Lerp(fieldBounds.min.x,fieldBounds.max.x,(float)x/(Resolution-1)),
                            Mathf.Lerp(fieldBounds.min.y,fieldBounds.max.y,(float)y/(Resolution-1)),0f);
            Gizmos.DrawRay(p, (Vector3)flowGrid[x,y]*2f);
        }
    }
}
