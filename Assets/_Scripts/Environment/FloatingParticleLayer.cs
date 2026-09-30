using UnityEngine;

/// <summary>
/// Five biologically distinct layers of drifting particles.
/// Lightweight: counts tuned for real-time, flow updated every 0.5s not every frame.
///
/// SETUP: Add to any empty GameObject. Requires MainCamera tag.
/// </summary>
public class FloatingParticleLayer : MonoBehaviour
{
    [Header("Spawn area half-extents (world units from camera)")]
    public Vector2 extents = new Vector2(26f, 17f);

    Camera         cam;
    ParticleSystem[] systems = new ParticleSystem[5];
    float          nextFlowUpdate;

    // Per-type config: name, minSize, maxSize, maxCount, minLife, maxLife,
    //                  color, emitRate, noiseStrength, gravity
    static readonly object[][] Cfg = {
        new object[]{"MicroPlankton", 0.02f,0.07f, 250, 8f,22f, new Color(0.92f,0.96f,1f,0.18f),  35f, 0.20f,-0.002f},
        new object[]{"Spores",        0.08f,0.30f,  70,22f,55f, new Color(0.55f,0.88f,0.40f,0.55f), 4f, 0.14f,-0.004f},
        new object[]{"MemFrags",      0.35f,1.20f,  15,40f,90f, new Color(0.62f,0.40f,0.90f,0.36f), 1f, 0.07f, 0.001f},
        new object[]{"BioMotes",      0.10f,0.40f,  40, 5f,12f, new Color(0.12f,1.00f,0.80f,0.88f), 4f, 0.28f,-0.006f},
        new object[]{"Nutrients",     0.05f,0.22f, 100,18f,42f, new Color(0.90f,0.84f,0.18f,0.50f),10f, 0.16f, 0.006f},
    };

    void Start()
    {
        cam = Camera.main;
        if (!cam) { Debug.LogError("[FPL] No main camera."); enabled = false; return; }

        // Use the first shader that's available
        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Transparent");
        if (!sh) { Debug.LogError("[FPL] No usable sprite shader found."); enabled = false; return; }
        var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        mat.mainTexture = GfxUtil.CircleTex(); // soft circle, not a white square

        for (int k = 0; k < Cfg.Length; k++)
            systems[k] = Build(Cfg[k], mat);

        Debug.Log("[FPL] 5 particle layers started.");
    }

    void Update()
    {
        if (!cam) return;
        Vector3 cp = cam.transform.position;

        // Move emitter boxes to follow camera
        for (int k = 0; k < systems.Length; k++)
        {
            if (!systems[k]) continue;
            var sh = systems[k].shape;
            sh.position = cp;
        }

        // Update flow drift only every 0.5 s — mutating the module every frame causes issues
        if (Time.time < nextFlowUpdate) return;
        nextFlowUpdate = Time.time + 0.5f;

        Vector2 flow = FlowFieldManager.Instance != null
            ? FlowFieldManager.Instance.SampleFlowAtPosition(cp)
            : Vector2.zero;

        // Subtle biome tint on the fine particles (keeps each type's own hue mostly,
        // nudges toward the biome's particle color so a bloom feels greener, etc.)
        Color bioTint = Color.white; float bioDensity = 1f;
        if (PlanetaryEnvironment.Instance != null)
        {
            var b = PlanetaryEnvironment.Instance.CurrentBiome;
            bioTint = b.particleColor;
            bioDensity = b.particleDensity;
        }

        for (int k = 0; k < systems.Length; k++)
        {
            if (!systems[k]) continue;
            float m = 0.10f + k * 0.05f;
            var fol = systems[k].forceOverLifetime;
            fol.x = new ParticleSystem.MinMaxCurve(flow.x * m);
            fol.y = new ParticleSystem.MinMaxCurve(flow.y * m);

            // Nudge color toward biome tint (30%), scale density
            var main = systems[k].main;
            Color baseCol = (Color)Cfg[k][6];
            Color tinted = Color.Lerp(baseCol, new Color(bioTint.r, bioTint.g, bioTint.b, baseCol.a), 0.30f);
            main.startColor = new ParticleSystem.MinMaxGradient(tinted,
                new Color(tinted.r*0.6f, tinted.g*0.6f, tinted.b*0.6f, tinted.a*0.5f));
            var em = systems[k].emission;
            em.rateOverTime = (float)Cfg[k][7] * Mathf.Lerp(1f, bioDensity, 0.5f);
        }
    }

    ParticleSystem Build(object[] c, Material mat)
    {
        string name   = (string)c[0];
        float  minSz  = (float)c[1], maxSz = (float)c[2];
        int    maxN   = (int)c[3];
        float  minL   = (float)c[4], maxL  = (float)c[5];
        Color  col    = (Color)c[6];
        float  rate   = (float)c[7], noise = (float)c[8], grav = (float)c[9];

        var go  = new GameObject($"FP_{name}");
        go.transform.SetParent(transform);
        var ps  = go.AddComponent<ParticleSystem>();
        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.material     = mat;
        psr.sortingOrder = -55;

        var main = ps.main;
        main.loop             = true;
        main.simulationSpace  = ParticleSystemSimulationSpace.World;
        main.maxParticles     = maxN;
        main.startLifetime    = new ParticleSystem.MinMaxCurve(minL, maxL);
        main.startSpeed       = new ParticleSystem.MinMaxCurve(0.02f, 0.30f);
        main.startSize        = new ParticleSystem.MinMaxCurve(minSz, maxSz);
        main.startColor       = new ParticleSystem.MinMaxGradient(col,
            new Color(col.r*0.6f, col.g*0.6f, col.b*0.6f, col.a*0.5f));
        main.startRotation    = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.gravityModifier  = new ParticleSystem.MinMaxCurve(grav);

        var em = ps.emission;
        em.rateOverTime = rate;

        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale     = new Vector3(extents.x*2, extents.y*2, 1f);

        // Fade in/out over lifetime
        var colOL = ps.colorOverLifetime;
        colOL.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[]{ new GradientColorKey(col,0f), new GradientColorKey(col,1f) },
            new[]{ new GradientAlphaKey(0f,0f),  new GradientAlphaKey(1f,0.12f),
                   new GradientAlphaKey(1f,0.85f),new GradientAlphaKey(0f,1f) });
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        // Scale in/out
        var szOL = ps.sizeOverLifetime;
        szOL.enabled = true;
        szOL.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f,0f), new Keyframe(0.12f,1f),
            new Keyframe(0.85f,1f), new Keyframe(1f,0f)));

        // Per-particle turbulence (1 octave = cheap)
        var noi = ps.noise;
        noi.enabled     = true;
        noi.strength    = new ParticleSystem.MinMaxCurve(noise*0.6f, noise);
        noi.frequency   = 0.20f;
        noi.octaveCount = 1;
        noi.damping     = true;

        // Force over lifetime (updated by flow field every 0.5 s)
        var fol = ps.forceOverLifetime;
        fol.enabled = true;
        fol.space   = ParticleSystemSimulationSpace.World;

        ps.Play();
        return ps;
    }
}
