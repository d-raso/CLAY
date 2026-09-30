using UnityEngine;

/// <summary>
/// Tracks the cell's movement and emits expanding ripple events as global shader properties.
/// FullScreenDistortion.shader reads _Ripple0-3 to add ring-shaped lens distortions.
/// Each ripple: (screenU, screenV, radiusUV, strength)
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CellRippleSystem : MonoBehaviour
{
    [Header("Ripple Emission")]
    [Tooltip("Min speed (world units/s) before ripples emit")]
    public float velocityThreshold = 2.5f;
    [Tooltip("Seconds between ripple spawns")]
    public float emitCooldown = 0.28f;
    [Tooltip("Peak distortion strength per ripple")]
    public float rippleStrength = 0.05f;

    [Header("Ripple Shape")]
    [Tooltip("Seconds for a ripple to fully expand and fade")]
    public float rippleLifetime = 1.8f;
    [Tooltip("How fast the ring expands in screen-height fractions per second")]
    public float rippleExpandSpeed = 0.55f;

    struct Ripple
    {
        public Vector2 worldPos;
        public float   age;
        public float   initStrength;
    }

    Ripple[]    ripples   = new Ripple[4];
    Rigidbody2D rb;
    Camera      cam;
    float       lastEmit;

    static readonly int[] ids = {
        Shader.PropertyToID("_Ripple0"),
        Shader.PropertyToID("_Ripple1"),
        Shader.PropertyToID("_Ripple2"),
        Shader.PropertyToID("_Ripple3"),
    };

    void Awake()
    {
        rb  = GetComponent<Rigidbody2D>();
        cam = Camera.main;
        // Start with all ripples dead
        for (int k = 0; k < 4; k++)
            ripples[k].age = rippleLifetime + 1f;
    }

    void Update()
    {
        if (!cam) { cam = Camera.main; return; }

        float speed = rb.linearVelocity.magnitude;

        // Emit a new ripple when moving fast enough
        if (speed > velocityThreshold && Time.time - lastEmit > emitCooldown)
        {
            Emit(transform.position, Mathf.InverseLerp(velocityThreshold, 8f, speed));
            lastEmit = Time.time;
        }

        // Update ages and push shader properties
        float screenH = cam.orthographicSize * 2f; // world-height visible on screen
        for (int k = 0; k < 4; k++)
        {
            ripples[k].age += Time.deltaTime;
            float t = ripples[k].age / rippleLifetime;
            if (t >= 1f) { Shader.SetGlobalVector(ids[k], Vector4.zero); continue; }

            float radiusWorld = t * rippleExpandSpeed * rippleLifetime * screenH;
            float radiusUV    = radiusWorld / screenH; // fraction of screen height
            float str         = ripples[k].initStrength * (1f - t) * (1f - t); // quadratic falloff

            // Convert world pos to viewport UV (0-1)
            Vector3 vp = cam.WorldToViewportPoint(ripples[k].worldPos);
            Shader.SetGlobalVector(ids[k], new Vector4(vp.x, vp.y, radiusUV, str));
        }
    }

    void Emit(Vector2 pos, float fraction)
    {
        // Find the oldest (most faded) slot to overwrite
        int   slot    = 0;
        float maxAge  = -1f;
        for (int k = 0; k < 4; k++)
            if (ripples[k].age > maxAge) { maxAge = ripples[k].age; slot = k; }

        ripples[slot] = new Ripple
        {
            worldPos     = pos,
            age          = 0f,
            initStrength = rippleStrength * Mathf.Max(fraction, 0.4f),
        };
    }
}
