using UnityEngine;

/// <summary>
/// Creates ambient underwater particles - debris, plankton, bubbles, silt.
/// Follows the camera and creates an immersive underwater atmosphere.
/// </summary>
public class AmbientParticles : MonoBehaviour
{
    [Header("Particle Types")]
    public bool enableDebris = true;
    public bool enablePlankton = true;
    public bool enableBubbles = true;
    public bool enableSilt = true;

    [Header("General Settings")]
    [Tooltip("Area around camera to spawn particles")]
    public Vector2 spawnArea = new Vector2(40f, 30f);
    [Tooltip("Overall particle density multiplier")]
    [Range(0f, 2f)]
    public float densityMultiplier = 1f;

    [Header("Debris Settings")]
    public Color debrisColor = new Color(0.5f, 0.55f, 0.5f, 0.6f);
    [Range(0f, 50f)]
    public float debrisCount = 20f;
    public Vector2 debrisSize = new Vector2(0.05f, 0.2f);

    [Header("Plankton Settings")]
    public Color planktonColor = new Color(0.6f, 0.8f, 0.5f, 0.4f);
    [Range(0f, 100f)]
    public float planktonCount = 40f;
    public Vector2 planktonSize = new Vector2(0.02f, 0.08f);

    [Header("Bubble Settings")]
    public Color bubbleColor = new Color(0.8f, 0.9f, 1f, 0.3f);
    [Range(0f, 30f)]
    public float bubbleCount = 10f;
    public Vector2 bubbleSize = new Vector2(0.05f, 0.15f);
    public float bubbleRiseSpeed = 0.5f;

    [Header("Silt Settings")]
    public Color siltColor = new Color(0.6f, 0.55f, 0.45f, 0.2f);
    [Range(0f, 200f)]
    public float siltCount = 80f;
    public Vector2 siltSize = new Vector2(0.01f, 0.03f);

    [Header("Current/Drift")]
    public Vector2 currentDirection = new Vector2(0.1f, 0.02f);
    public float currentStrength = 0.5f;
    [Range(0f, 1f)]
    public float turbulence = 0.3f;

    [Header("Flow Field Integration")]
    [Tooltip("Use FlowFieldManager for particle velocities instead of fixed current")]
    public bool useFlowField = true;
    [Tooltip("How often to update particle velocities from flow field (seconds)")]
    public float flowFieldUpdateInterval = 0.1f;

    // Runtime - particle systems
    private ParticleSystem debrisPS;
    private ParticleSystem planktonPS;
    private ParticleSystem bubblesPS;
    private ParticleSystem siltPS;

    private Transform cameraTransform;
    private float flowFieldUpdateTimer;

    void Start()
    {
        cameraTransform = Camera.main.transform;

        if (enableDebris) debrisPS = CreateDebrisSystem();
        if (enablePlankton) planktonPS = CreatePlanktonSystem();
        if (enableBubbles) bubblesPS = CreateBubbleSystem();
        if (enableSilt) siltPS = CreateSiltSystem();
    }

    void LateUpdate()
    {
        // Follow camera
        if (cameraTransform != null)
        {
            transform.position = cameraTransform.position;
        }

        // Update particle velocities from flow field
        if (useFlowField)
        {
            flowFieldUpdateTimer -= Time.deltaTime;
            if (flowFieldUpdateTimer <= 0f)
            {
                flowFieldUpdateTimer = flowFieldUpdateInterval;
                UpdateParticleVelocitiesFromFlowField();
            }
        }
    }

    /// <summary>
    /// Update all particle system velocities based on the current flow field.
    /// </summary>
    void UpdateParticleVelocitiesFromFlowField()
    {
        Vector2 flow;

        if (FlowFieldManager.Instance != null)
        {
            flow = FlowFieldManager.Instance.SampleFlowAtPosition(transform.position);
        }
        else
        {
            // Fallback to fixed current if no flow field manager
            flow = currentDirection * currentStrength;
        }

        // Apply flow to each particle system with different multipliers
        SetParticleVelocity(debrisPS, flow * 1.0f);
        SetParticleVelocity(planktonPS, flow * 0.5f);
        SetParticleVelocity(bubblesPS, flow * 0.3f + Vector2.up * bubbleRiseSpeed);
        SetParticleVelocity(siltPS, flow * 1.2f);
    }

    /// <summary>
    /// Set the velocity over lifetime for a particle system.
    /// </summary>
    void SetParticleVelocity(ParticleSystem ps, Vector2 velocity)
    {
        if (ps == null) return;

        var vel = ps.velocityOverLifetime;
        vel.x = velocity.x;
        vel.y = velocity.y;
    }

    ParticleSystem CreateDebrisSystem()
    {
        GameObject go = new GameObject("PS_Debris");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 15f;
        main.startSpeed = 0.1f;
        main.startSize = new ParticleSystem.MinMaxCurve(debrisSize.x, debrisSize.y);
        main.startColor = debrisColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.RoundToInt(debrisCount * densityMultiplier * 3);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var emission = ps.emission;
        emission.rateOverTime = debrisCount * densityMultiplier;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnArea.x, spawnArea.y, 1f);

        // Slow tumbling rotation
        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

        // Drift with current (use constant mode for all axes)
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        float vx = currentDirection.x * currentStrength;
        float vy = currentDirection.y * currentStrength;
        velocity.x = vx;
        velocity.y = vy;
        velocity.z = 0f;

        // Noise for organic movement
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = turbulence * 0.3f;
        noise.frequency = 0.5f;
        noise.octaveCount = 2;

        // Fade out
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) }
        );
        colorOverLifetime.color = gradient;

        SetupRenderer(ps, -2);

        return ps;
    }

    ParticleSystem CreatePlanktonSystem()
    {
        GameObject go = new GameObject("PS_Plankton");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 12f;
        main.startSpeed = 0.05f;
        main.startSize = new ParticleSystem.MinMaxCurve(planktonSize.x, planktonSize.y);
        main.startColor = planktonColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.RoundToInt(planktonCount * densityMultiplier * 3);

        var emission = ps.emission;
        emission.rateOverTime = planktonCount * densityMultiplier;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnArea.x, spawnArea.y, 1f);

        // Erratic movement
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.2f;
        noise.frequency = 1f;
        noise.octaveCount = 3;

        // Slight upward drift (phototaxis) - use constants for all axes
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = 0f;
        velocity.y = 0.03f;
        velocity.z = 0f;

        // Pulsing size (swimming motion)
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0f);
        sizeCurve.AddKey(0.1f, 1f);
        sizeCurve.AddKey(0.9f, 1f);
        sizeCurve.AddKey(1f, 0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        SetupRenderer(ps, -1);

        return ps;
    }

    ParticleSystem CreateBubbleSystem()
    {
        GameObject go = new GameObject("PS_Bubbles");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 8f;
        main.startSpeed = bubbleRiseSpeed;
        main.startSize = new ParticleSystem.MinMaxCurve(bubbleSize.x, bubbleSize.y);
        main.startColor = bubbleColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.RoundToInt(bubbleCount * densityMultiplier * 3);

        var emission = ps.emission;
        emission.rateOverTime = bubbleCount * densityMultiplier;

        // Spawn from bottom of area
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnArea.x, 2f, 1f);
        shape.position = new Vector3(0f, -spawnArea.y * 0.4f, 0f);

        // Rise upward with wobble - use constants for all axes
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = 0f;
        velocity.y = bubbleRiseSpeed;
        velocity.z = 0f;

        // Wobble side to side
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.15f;
        noise.frequency = 2f;
        noise.scrollSpeed = 0.5f;
        noise.damping = false;

        // Grow slightly as they rise (pressure decrease)
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.8f);
        sizeCurve.AddKey(1f, 1.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Fade in/out
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.8f), new GradientAlphaKey(0f, 1f) }
        );
        colorOverLifetime.color = gradient;

        SetupRenderer(ps, 2);

        return ps;
    }

    ParticleSystem CreateSiltSystem()
    {
        GameObject go = new GameObject("PS_Silt");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 20f;
        main.startSpeed = 0.02f;
        main.startSize = new ParticleSystem.MinMaxCurve(siltSize.x, siltSize.y);
        main.startColor = siltColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.RoundToInt(siltCount * densityMultiplier * 3);

        var emission = ps.emission;
        emission.rateOverTime = siltCount * densityMultiplier;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnArea.x, spawnArea.y, 1f);

        // Very slow drift - use constants, let noise handle variation
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = 0f;
        velocity.y = 0f;
        velocity.z = 0f;

        // Subtle movement
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.05f;
        noise.frequency = 0.3f;

        SetupRenderer(ps, -3);

        return ps;
    }

    void SetupRenderer(ParticleSystem ps, int sortingOrder)
    {
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = sortingOrder;
        renderer.material = CreateParticleMaterial();
    }

    Material CreateParticleMaterial()
    {
        // Create soft circle material
        Material mat = new Material(Shader.Find("Sprites/Default"));

        // Create soft circle texture
        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / (size / 2f);
                float alpha = 1f - Mathf.SmoothStep(0.3f, 1f, dist);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        mat.mainTexture = tex;

        return mat;
    }

    /// <summary>
    /// Set overall density (useful for biome transitions)
    /// </summary>
    public void SetDensity(float density)
    {
        densityMultiplier = density;
        UpdateEmissionRates();
    }

    /// <summary>
    /// Set current direction and strength
    /// </summary>
    public void SetCurrent(Vector2 direction, float strength)
    {
        currentDirection = direction.normalized;
        currentStrength = strength;
        // Note: Would need to update velocity modules to take effect immediately
    }

    void UpdateEmissionRates()
    {
        if (debrisPS != null)
        {
            var emission = debrisPS.emission;
            emission.rateOverTime = debrisCount * densityMultiplier;
        }
        if (planktonPS != null)
        {
            var emission = planktonPS.emission;
            emission.rateOverTime = planktonCount * densityMultiplier;
        }
        if (bubblesPS != null)
        {
            var emission = bubblesPS.emission;
            emission.rateOverTime = bubbleCount * densityMultiplier;
        }
        if (siltPS != null)
        {
            var emission = siltPS.emission;
            emission.rateOverTime = siltCount * densityMultiplier;
        }
    }

    /// <summary>
    /// Update colors for biome transition
    /// </summary>
    public void SetColors(Color debris, Color plankton, Color bubble, Color silt)
    {
        debrisColor = debris;
        planktonColor = plankton;
        bubbleColor = bubble;
        siltColor = silt;

        if (debrisPS != null)
        {
            var main = debrisPS.main;
            main.startColor = debris;
        }
        if (planktonPS != null)
        {
            var main = planktonPS.main;
            main.startColor = plankton;
        }
        if (bubblesPS != null)
        {
            var main = bubblesPS.main;
            main.startColor = bubble;
        }
        if (siltPS != null)
        {
            var main = siltPS.main;
            main.startColor = silt;
        }
    }
}
