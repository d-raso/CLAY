using UnityEngine;

/// <summary>
/// Pushes distortion settings as global shader properties each frame.
/// Boosts distortion strength when the cell is caught in a strong current.
/// The actual full-screen effect runs via FullScreenPassRendererFeature.
/// </summary>
[RequireComponent(typeof(Camera))]
public class WaterDistortionCamera : MonoBehaviour
{
    [Header("Effect Settings")]
    [Range(0f,  0.15f)] public float distortionStrength  = 0.009f;
    [Range(0f,  0.05f)] public float chromaticAberration = 0.0015f;
    [Range(0.1f, 5f)]   public float noiseSpeed          = 0.8f;
    [Range(1f,  12f)]   public float noiseScale          = 7.5f;

    [Header("Current Response")]
    [Tooltip("How much a strong current boosts the distortion effect (0 = none)")]
    [Range(0f, 3f)] public float flowDistortionScale = 0.8f;
    [Tooltip("Max extra distortion added by flow")]
    [Range(0f, 0.12f)] public float maxFlowBoost = 0.035f;

    // The cell sits closest to the viewer (least water in front of it), so it should
    // be distorted far less than the open background. This carves a soft low-distortion
    // bubble around the cell.
    [Header("Cell Proximity Mask")]
    [Tooltip("Cell radius in world units (where distortion is fully reduced)")]
    public float cellRadiusWorld = 2.0f;
    [Tooltip("Falloff distance (world units) from the cell out to full distortion")]
    public float cellEdgeFeather = 4.0f;
    [Tooltip("Fraction of distortion remaining at the cell center (0 = none, 1 = full)")]
    [Range(0f, 1f)] public float cellMinDistortion = 0.06f;

    static readonly int sStr  = Shader.PropertyToID("_DistortionStrength");
    static readonly int sCa   = Shader.PropertyToID("_ChromaticAberration");
    static readonly int sSp   = Shader.PropertyToID("_NoiseSpeed");
    static readonly int sSc   = Shader.PropertyToID("_NoiseScale");
    static readonly int sMask = Shader.PropertyToID("_CellDistortMask");  // (u, v, radiusUV, featherUV)
    static readonly int sMin  = Shader.PropertyToID("_CellMinDistort");

    Transform playerTransform;
    Camera    cam;

    void Start()
    {
        cam = GetComponent<Camera>();

        // Disable the legacy ScriptableRendererFeature — it corrupts image in Unity 6/DX12
        var legacy = FindFirstObjectByType<WaterDistortionFeature>();
        if (legacy) legacy.SetEnabled(false);

        // Find the cell (player) for local-flow distortion boost
        var jelly = FindFirstObjectByType<JellyMovement>();
        if (jelly) playerTransform = jelly.transform;

        PushGlobals();
    }

    void OnEnable() => PushGlobals();
    void Update()   => PushGlobals();

    void PushGlobals()
    {
        float str = distortionStrength;

        // Boost distortion when the cell is in a strong current
        if (playerTransform != null && FlowFieldManager.Instance != null)
        {
            Vector2 localFlow = FlowFieldManager.Instance.SampleFlowAtPosition(playerTransform.position);
            float flowMag = localFlow.magnitude;
            float boost = Mathf.Clamp(flowMag * flowDistortionScale * 0.05f, 0f, maxFlowBoost);
            str = Mathf.Clamp(str + boost, 0f, 0.15f);
        }

        Shader.SetGlobalFloat(sStr, str);
        Shader.SetGlobalFloat(sCa,  chromaticAberration);
        Shader.SetGlobalFloat(sSp,  noiseSpeed);
        Shader.SetGlobalFloat(sSc,  noiseScale);

        // Cell proximity mask — convert the cell's world footprint to viewport space.
        // Because the camera is orthographic and follows the cell, the cell's screen
        // size is independent of where it is in the world (it only shrinks when YOU
        // zoom out, which is exactly the "more water = more distortion" relationship).
        if (!cam) cam = GetComponent<Camera>();
        if (playerTransform != null && cam != null && cam.orthographic)
        {
            Vector3 vp = cam.WorldToViewportPoint(playerTransform.position);
            float worldH    = cam.orthographicSize * 2f;       // world units across screen height
            float radiusUV  = cellRadiusWorld / worldH;        // height-fraction radius
            float featherUV = Mathf.Max(cellEdgeFeather / worldH, 0.001f);
            Shader.SetGlobalVector(sMask, new Vector4(vp.x, vp.y, radiusUV, featherUV));
            Shader.SetGlobalFloat(sMin, cellMinDistortion);
        }
        else
        {
            // No mask: full distortion everywhere
            Shader.SetGlobalVector(sMask, new Vector4(0.5f, 0.5f, 0f, 0.001f));
            Shader.SetGlobalFloat(sMin, 1f);
        }
    }

    public void SetDistortionStrength(float s) => distortionStrength = Mathf.Clamp(s, 0f, 0.15f);
}
