using UnityEngine;

/// <summary>
/// Drifting ORGANIC MATTER — a step up in complexity from the fine particle layer.
/// These are the things that make the player wonder "what is that?": tumbling
/// detritus clumps, stretched filament strands, and slow translucent cysts.
/// Not gameplay objects — just life and texture in the water.
///
/// Tint and density follow the current biome (PlanetaryEnvironment.CurrentBiome):
/// an algal bloom teems with green strands; cold brine is nearly barren.
///
/// SETUP: Add to any empty GameObject. Requires a MainCamera.
/// </summary>
public class OrganicMatterLayer : MonoBehaviour
{
    [Header("Spawn area half-extents (world units from camera)")]
    public Vector2 extents = new Vector2(28f, 18f);

    [Header("Density")]
    [Tooltip("Global multiplier on top of the biome's organic density")]
    [Range(0f,2f)] public float densityMultiplier = 1f;

    Camera         cam;
    ParticleSystem[] systems;
    float          nextSlowUpdate;

    // name, minSize, maxSize, maxCount, minLife, maxLife, baseRate,
    // noiseStrength, rotationSpeed(deg/s range), stretch(velocity), gravity
    static readonly object[][] Cfg =
    {
        // Detritus clumps — irregular tumbling specks of dead matter
        new object[]{"Detritus",  0.18f,0.55f, 45, 30f,70f, 5f,  0.10f, 40f,  0f,    0.0008f},
        // Filament strands — stretched wisps that streak along the current
        new object[]{"Filaments", 0.10f,0.30f, 30, 25f,60f, 3f,  0.18f, 10f,  2.6f, -0.0006f},
        // Cysts — slow translucent globules drifting upward
        new object[]{"Cysts",     0.30f,0.90f, 14, 45f,100f,1.2f,0.06f, 15f,  0f,   -0.0010f},
    };

    void Start()
    {
        cam = Camera.main;
        if (!cam) { Debug.LogError("[Organic] No main camera."); enabled = false; return; }

        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Transparent");
        if (!sh) { Debug.LogError("[Organic] No usable sprite shader."); enabled = false; return; }
        var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        mat.mainTexture = GfxUtil.CircleTex(); // soft blob, not a white square

        systems = new ParticleSystem[Cfg.Length];
        for (int k = 0; k < Cfg.Length; k++)
            systems[k] = Build(Cfg[k], mat);

        Debug.Log("[Organic] Organic matter layer started.");
    }

    void Update()
    {
        if (!cam) return;
        Vector3 cp = cam.transform.position;

        for (int k = 0; k < systems.Length; k++)
        {
            if (!systems[k]) continue;
            var shp = systems[k].shape;
            shp.position = cp;
        }

        // Throttle the heavier per-system updates (flow + biome tint)
        if (Time.time < nextSlowUpdate) return;
        nextSlowUpdate = Time.time + 0.5f;

        Vector2 flow = FlowFieldManager.Instance != null
            ? FlowFieldManager.Instance.SampleFlowAtPosition(cp)
            : Vector2.zero;

        // Biome tint/density
        Color tint = Color.white; float bioDensity = 1f;
        if (PlanetaryEnvironment.Instance != null)
        {
            var b = PlanetaryEnvironment.Instance.CurrentBiome;
            tint = b.organicColor;
            bioDensity = b.organicDensity;
        }

        for (int k = 0; k < systems.Length; k++)
        {
            if (!systems[k]) continue;
            float m = 0.12f + k * 0.05f;
            var fol = systems[k].forceOverLifetime;
            fol.x = new ParticleSystem.MinMaxCurve(flow.x * m);
            fol.y = new ParticleSystem.MinMaxCurve(flow.y * m);

            // Re-tint start color (preserve the per-type alpha set at build time)
            var main = systems[k].main;
            var grad = main.startColor;
            Color baseCol = grad.colorMin;
            Color a = tint; a.a = baseCol.a;
            Color bCol = tint * 0.6f; bCol.a = baseCol.a * 0.7f;
            main.startColor = new ParticleSystem.MinMaxGradient(a, bCol);

            // Density via emission rate
            float baseRate = (float)Cfg[k][6];
            var em = systems[k].emission;
            em.rateOverTime = baseRate * bioDensity * densityMultiplier;
        }
    }

    ParticleSystem Build(object[] c, Material mat)
    {
        string name   = (string)c[0];
        float  minSz  = (float)c[1],  maxSz = (float)c[2];
        int    maxN   = (int)c[3];
        float  minL   = (float)c[4],  maxL  = (float)c[5];
        float  rate   = (float)c[6],  noise = (float)c[7];
        float  rotSpd = (float)c[8],  stretch = (float)c[9], grav = (float)c[10];

        var go  = new GameObject($"OM_{name}");
        go.transform.SetParent(transform);
        var ps  = go.AddComponent<ParticleSystem>();
        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.material     = mat;
        psr.sortingOrder = -52;
        if (stretch > 0f)
        {
            psr.renderMode      = ParticleSystemRenderMode.Stretch;
            psr.velocityScale   = stretch;
            psr.lengthScale     = 1.2f;
        }

        var main = ps.main;
        main.loop            = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = maxN;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(minL, maxL);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.02f, 0.25f);
        main.startSize       = new ParticleSystem.MinMaxCurve(minSz, maxSz);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(0.55f,0.7f,0.45f,0.45f), new Color(0.35f,0.45f,0.3f,0.3f));
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(grav);

        var em = ps.emission;
        em.rateOverTime = rate;

        var shp = ps.shape;
        shp.shapeType = ParticleSystemShapeType.Box;
        shp.scale     = new Vector3(extents.x*2, extents.y*2, 1f);

        // Tumbling rotation
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-rotSpd * Mathf.Deg2Rad, rotSpd * Mathf.Deg2Rad);

        // Fade in/out
        var colOL = ps.colorOverLifetime;
        colOL.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[]{ new GradientColorKey(Color.white,0f), new GradientColorKey(Color.white,1f) },
            new[]{ new GradientAlphaKey(0f,0f), new GradientAlphaKey(1f,0.15f),
                   new GradientAlphaKey(1f,0.80f), new GradientAlphaKey(0f,1f) });
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        // Grow/shrink
        var szOL = ps.sizeOverLifetime;
        szOL.enabled = true;
        szOL.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,0.3f), new Keyframe(0.2f,1f),
            new Keyframe(0.8f,1f), new Keyframe(1f,0.3f)));

        // Gentle turbulence
        var noi = ps.noise;
        noi.enabled     = true;
        noi.strength    = new ParticleSystem.MinMaxCurve(noise*0.6f, noise);
        noi.frequency   = 0.15f;
        noi.octaveCount = 1;
        noi.damping     = true;

        var fol = ps.forceOverLifetime;
        fol.enabled = true;
        fol.space   = ParticleSystemSimulationSpace.World;

        ps.Play();
        return ps;
    }
}
