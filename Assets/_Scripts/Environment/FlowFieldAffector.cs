using UnityEngine;

/// <summary>
/// Component for creating local flow disturbances in the flow field.
/// Attach to any GameObject to create vortices, jets, sinks, or sources.
/// </summary>
public class FlowFieldAffector : MonoBehaviour
{
    public enum AffectorType
    {
        Vortex,     // Swirling flow around center
        Jet,        // Directional flow outward
        Sink,       // Flow toward center
        Source,     // Flow away from center
        Turbulence  // Random local disturbance
    }

    [Header("Affector Settings")]
    public AffectorType affectorType = AffectorType.Vortex;

    [Tooltip("Radius of effect in world units")]
    [Range(0.5f, 20f)]
    public float radius = 5f;

    [Tooltip("Strength of the effect")]
    [Range(0f, 5f)]
    public float strength = 1f;

    [Tooltip("Rotation speed for vortex type (positive = CCW, negative = CW)")]
    public float rotationSpeed = 2f;

    [Tooltip("How quickly the effect falls off toward the edge (1 = linear, 2 = quadratic)")]
    [Range(0.5f, 3f)]
    public float falloff = 1.5f;

    [Header("Jet Settings")]
    [Tooltip("Direction of jet flow (for Jet type only)")]
    public Vector2 jetDirection = Vector2.up;

    [Header("Animation")]
    [Tooltip("Animate strength over time")]
    public bool animateStrength = false;
    public float strengthPulseSpeed = 1f;
    public float strengthPulseAmount = 0.3f;

    [Tooltip("Animate rotation speed over time (vortex only)")]
    public bool animateRotation = false;
    public float rotationVariationSpeed = 0.5f;
    public float rotationVariationAmount = 0.5f;

    // Runtime
    private float baseStrength;
    private float baseRotationSpeed;
    private float timeOffset;

    void Start()
    {
        baseStrength = strength;
        baseRotationSpeed = rotationSpeed;
        timeOffset = Random.Range(0f, 100f);

        // Register with FlowFieldManager
        if (FlowFieldManager.Instance != null)
        {
            FlowFieldManager.Instance.RegisterAffector(this);
        }
    }

    void Update()
    {
        // Animate parameters
        if (animateStrength)
        {
            float pulse = Mathf.Sin((Time.time + timeOffset) * strengthPulseSpeed) * strengthPulseAmount;
            strength = baseStrength * (1f + pulse);
        }

        if (animateRotation && affectorType == AffectorType.Vortex)
        {
            float variation = Mathf.Sin((Time.time + timeOffset) * rotationVariationSpeed) * rotationVariationAmount;
            rotationSpeed = baseRotationSpeed * (1f + variation);
        }
    }

    void OnEnable()
    {
        if (FlowFieldManager.Instance != null)
        {
            FlowFieldManager.Instance.RegisterAffector(this);
        }
    }

    void OnDisable()
    {
        if (FlowFieldManager.Instance != null)
        {
            FlowFieldManager.Instance.UnregisterAffector(this);
        }
    }

    void OnDestroy()
    {
        if (FlowFieldManager.Instance != null)
        {
            FlowFieldManager.Instance.UnregisterAffector(this);
        }
    }

    /// <summary>
    /// Calculate this affector's contribution to the flow at a world position.
    /// Called by FlowFieldManager during CPU flow field update.
    /// </summary>
    public Vector2 GetFlowContribution(Vector2 worldPosition)
    {
        Vector2 myPos = transform.position;
        Vector2 toPoint = worldPosition - myPos;
        float distance = toPoint.magnitude;

        // Outside radius - no contribution
        if (distance > radius || distance < 0.001f)
        {
            return Vector2.zero;
        }

        // Calculate falloff (1 at center, 0 at edge)
        float normalizedDist = distance / radius;
        float falloffMultiplier = Mathf.Pow(1f - normalizedDist, falloff);

        Vector2 flow = Vector2.zero;

        switch (affectorType)
        {
            case AffectorType.Vortex:
                // Perpendicular to direction from center (tangent)
                Vector2 tangent = new Vector2(-toPoint.y, toPoint.x).normalized;
                flow = tangent * rotationSpeed * falloffMultiplier * strength;
                break;

            case AffectorType.Jet:
                // Directional flow
                flow = jetDirection.normalized * strength * falloffMultiplier;
                break;

            case AffectorType.Sink:
                // Flow toward center
                flow = -toPoint.normalized * strength * falloffMultiplier;
                break;

            case AffectorType.Source:
                // Flow away from center
                flow = toPoint.normalized * strength * falloffMultiplier;
                break;

            case AffectorType.Turbulence:
                // Random local variation using noise
                float noiseX = Mathf.PerlinNoise(worldPosition.x * 0.5f + Time.time, worldPosition.y * 0.5f) - 0.5f;
                float noiseY = Mathf.PerlinNoise(worldPosition.x * 0.5f, worldPosition.y * 0.5f + Time.time + 100f) - 0.5f;
                flow = new Vector2(noiseX, noiseY) * 2f * strength * falloffMultiplier;
                break;
        }

        return flow;
    }

    void OnDrawGizmosSelected()
    {
        // Draw radius
        Color gizmoColor = affectorType switch
        {
            AffectorType.Vortex => new Color(0.5f, 0.5f, 1f, 0.5f),
            AffectorType.Jet => new Color(0f, 1f, 0.5f, 0.5f),
            AffectorType.Sink => new Color(1f, 0.5f, 0f, 0.5f),
            AffectorType.Source => new Color(1f, 1f, 0f, 0.5f),
            AffectorType.Turbulence => new Color(1f, 0f, 1f, 0.5f),
            _ => Color.white
        };

        Gizmos.color = gizmoColor;
        DrawCircle(transform.position, radius, 32);

        // Draw inner circle for falloff visualization
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.8f);
        DrawCircle(transform.position, radius * 0.3f, 16);

        // Draw direction indicator
        switch (affectorType)
        {
            case AffectorType.Vortex:
                // Draw rotation direction
                Gizmos.color = Color.cyan;
                float angle = rotationSpeed > 0 ? 90f : -90f;
                Vector3 arrowDir = Quaternion.Euler(0, 0, angle) * Vector3.right;
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f * Mathf.Deg2Rad;
                    Vector3 pos = transform.position + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius * 0.6f;
                    Vector3 tangent = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0) * Mathf.Sign(rotationSpeed);
                    Gizmos.DrawRay(pos, tangent * radius * 0.3f);
                }
                break;

            case AffectorType.Jet:
                Gizmos.color = Color.green;
                Gizmos.DrawRay(transform.position, (Vector3)jetDirection.normalized * radius * 0.8f);
                break;

            case AffectorType.Sink:
                Gizmos.color = Color.red;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    Gizmos.DrawRay(transform.position + dir * radius * 0.8f, -dir * radius * 0.4f);
                }
                break;

            case AffectorType.Source:
                Gizmos.color = Color.yellow;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    Gizmos.DrawRay(transform.position + dir * radius * 0.3f, dir * radius * 0.5f);
                }
                break;
        }
    }

    void DrawCircle(Vector3 center, float r, int segments)
    {
        Vector3 prevPoint = center + Vector3.right * r;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            Vector3 point = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * r;
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }
    }
}
