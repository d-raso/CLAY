using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Death / engulf visual effects built from the cell's OWN geometry and material — no glowy sprites.
///
/// A cell on screen is a filled cytoplasm mesh (JellyMesh, drawn with the BioSlime shader) ringed by
/// a thin membrane line (LineRenderer). When a cell leaves the world we snapshot that exact mesh and
/// material and animate the snapshot, so the debris is made of the same slime, lit and shaded the
/// same way, and fades through the shader's own <c>_BodyOpacity</c> instead of a sprite alpha.
///
///   • Pop    — the cytoplasm mesh is sliced into wedges that fling outward, spin, shrink and fade;
///              the membrane outline tears into curling arcs.
///   • Engulf — the whole cytoplasm snapshot is dragged into the predator, shrinking away, with the
///              membrane arcs trailing in after it.
/// </summary>
public static class CellFx
{
    // ── cytoplasm burst (real particle system) ─────────────────────────────────────────────────
    /// <summary>
    /// An explosion of liquid cytoplasm: opaque slime droplets shot out fast and stretched along
    /// their velocity (so they read as flung fluid, not fuzzy fog), decelerating hard and popping out
    /// of existence rather than slowly fading. Mixes big globs with fine spray. Self-destructs.
    /// </summary>
    public static void CytoplasmBurst(Vector3 center, float radius, Color color, int count = 180)
    {
        radius = Mathf.Max(radius, 0.2f);
        Color liquid = color; liquid.a = 1f; // opaque, not translucent

        var go = new GameObject("CytoplasmBurst");
        go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);            // brief — dissipates fast
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 8f, radius * 20f); // fast radial burst
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.1f, radius * 0.5f); // FINE mist, not chunky globs
        main.startColor = liquid;
        main.gravityModifier = 0.4f;                 // droplets arc down → wet, physical
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 32;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius * 0.35f;
        shape.radiusThickness = 1f;

        // Strong drag so the spray shoots out then arrests — liquid losing momentum, not drifting smoke.
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.85f;

        // Droplets shrink only at the very end (a glob keeps its size, then breaks up).
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0f)));

        // Hold full opacity, then vanish fast — no smoky gradual fade.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(liquid, 0f), new GradientColorKey(liquid, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.material = ParticleMat();
        pr.renderMode = ParticleSystemRenderMode.Billboard; // round droplets (Stretch looked "electric")
        pr.sortingOrder  = -25;     // above cells (~-30), below membrane arcs

        ps.Play();
        Object.Destroy(go, 2f);
    }

    static Material _particleMat;
    static Material ParticleMat()
    {
        if (_particleMat != null) return _particleMat;
        Shader sh = Shader.Find("Sprites/Default")
                 ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit");
        _particleMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _particleMat.mainTexture = GfxUtil.BlobTex(); // near-solid droplet, not fuzzy
        return _particleMat;
    }

    // ── CYTOPLASM SWARM (lingering cloud of granular point-particles) ─────────────────────────────
    /// <summary>
    /// A big swarm of tiny point-particles — the cell's dispersed cytoplasm/granules — that burst out,
    /// drift organically (noise) and slowly thin away over <paramref name="lifetime"/>. This is the
    /// lingering "cloud of shattered contents" look, so the pop reads as a spray of matter rather than a
    /// few round bubbles. Purely visual; the edible mass rides on a few CytoGobbet fragments alongside.
    /// </summary>
    public static void CytoplasmSwarm(Vector3 center, float radius, Color color, float lifetime)
    {
        radius = Mathf.Max(radius, 0.2f);
        lifetime = Mathf.Max(lifetime, 0.5f);
        Color c = color; c.a = 1f;

        var go = new GameObject("CytoplasmSwarm");
        go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.5f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 2f, radius * 9f);      // scatter wide
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.02f, radius * 0.06f); // MANY tiny specks
        main.startColor = new ParticleSystem.MinMaxGradient(c, Color.Lerp(c, Color.white, 0.35f));
        main.gravityModifier = 0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1200;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Random.Range(500, 800)) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius * 0.4f;
        shape.radiusThickness = 1f;

        // Fast dispersal that arrests → the swarm spreads then hangs, drifting.
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.9f;

        // Organic wandering so it churns like suspended matter, not a static puff.
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = radius * 0.35f;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.25f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(Color.Lerp(c, Color.white, 0.25f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.material = ParticleMat();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sortingOrder = 7;

        ps.Play();
        Object.Destroy(go, lifetime + 1f);
    }

    // ── METABALL BURST (fused organic goo) ──────────────────────────────────────────────────────
    /// <summary>
    /// The cell-pop as MERGED liquid: a quad runs a metaball shader over ~N simulated blobs that fly
    /// out, so overlapping blobs FUSE into one wobbling organic mass (not separate dots). Tinted to
    /// the cell. Falls back to the particle spray if the shader is missing. Self-cleaning.
    /// </summary>
    public static void MetaballBurst(Vector3 center, float radius, Color color, int count = 14)
    {
        radius = Mathf.Max(radius, 0.3f);
        var sh = Shader.Find("CLAY/MetaballGoo");
        if (sh == null) { CytoplasmBurst(center, radius, color); return; }

        float worldSize = radius * 16f;  // lots of room so the goo can violently fling apart without clipping
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "MetaballBurst";
        var pc = go.GetComponent<Collider>(); if (pc != null) Object.Destroy(pc);
        go.transform.position = new Vector3(center.x, center.y, center.z);
        go.transform.localScale = Vector3.one * worldSize;

        var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        Color tint = color * 0.85f; tint.a = 1f;                            // darker so it reads as matter
        mat.SetColor("_Tint", tint);
        mat.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.2f));   // barely lighter, not a glow
        mat.SetFloat("_Threshold", 1.6f);      // higher = each blob stays a distinct glob → gobbets, not one disc
        mat.SetFloat("_EdgeSoftness", 0.22f);  // crisper wet edge on each gobbet
        mat.SetFloat("_NoiseScale", Random.Range(7f, 12f));                // per-burst mottle pattern
        mat.SetFloat("_Opacity", 0.6f);                                    // more transparent goo
        mat.SetFloat("_GlobalAlpha", 1f);

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = 10;   // draw over the cells

        go.AddComponent<MetaballRunner>().Init(mat, worldSize, radius, Mathf.Clamp(count, 8, 24), 1.1f);
    }

    /// <summary>
    /// The EATEN counterpart to MetaballBurst: the same fused organic goo, but instead of exploding it is
    /// SUCKED into the predator — gobbets stream/spiral inward, stretching and unravelling into the mouth.
    /// Rendered at <paramref name="sortingOrder"/> so it draws UNDER the eater (absorbed, not on top).
    /// </summary>
    public static void MetaballSiphon(Vector3 center, float radius, Color color, Transform predator, int sortingOrder, int count = 12)
    {
        radius = Mathf.Max(radius, 0.3f);
        var sh = Shader.Find("CLAY/MetaballGoo");
        if (sh == null) { CytoplasmSiphon(center, predator, color); return; }

        float worldSize = radius * 10f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "MetaballSiphon";
        var pc = go.GetComponent<Collider>(); if (pc != null) Object.Destroy(pc);
        go.transform.position = new Vector3(center.x, center.y, center.z);
        go.transform.localScale = Vector3.one * worldSize;

        var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        Color tint = color * 0.9f; tint.a = 1f;
        mat.SetColor("_Tint", tint);
        mat.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.2f));
        mat.SetFloat("_Threshold", 1.3f);       // a bit lower → gobbets fuse into streaming strands
        mat.SetFloat("_EdgeSoftness", 0.28f);
        mat.SetFloat("_NoiseScale", Random.Range(7f, 12f));
        mat.SetFloat("_Opacity", 0.7f);
        mat.SetFloat("_GlobalAlpha", 1f);

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = sortingOrder;         // UNDER the eater

        go.AddComponent<MetaballRunner>().InitSiphon(mat, worldSize, radius, Mathf.Clamp(count, 6, 20), 0.5f, predator, center);
    }

    // ── SHOCKWAVE (impact flash) ────────────────────────────────────────────────────────────────
    /// <summary>A quick bright pulse that expands and fades — the "pop" of the explosion.</summary>
    public static void Shockwave(Vector3 center, float radius, Color color)
    {
        radius = Mathf.Max(radius, 0.3f);
        Color c = Color.Lerp(color, Color.white, 0.5f); c.a = 0.55f;
        var go = new GameObject("Shockwave");
        go.transform.position = center;
        go.transform.localScale = Vector3.one * radius * 1.2f;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GfxUtil.Circle();
        sr.sharedMaterial = GfxUtil.SpriteMaterial();
        sr.color = c;
        sr.sortingOrder = -24;               // above the droplet burst
        var run = go.AddComponent<CellFxRunner>();
        run.StartCoroutine(run.ExpandRoutine(radius * 0.8f, radius * 4.5f, 0.22f));   // snappier, wider pulse
    }

    // ── PRESSURE WAVE (physical shove) ──────────────────────────────────────────────────────────
    /// <summary>
    /// A radial impulse on every cell body caught in the blast radius — nearby cells get physically
    /// shoved away from the rupture, so a pop reads as a pressure event, not just a visual. The popping
    /// cell (<paramref name="ignore"/>) is skipped.
    /// </summary>
    public static void PressureWave(Vector3 center, float radius, float strength, Transform ignore = null)
    {
        float reach = Mathf.Max(radius, 0.3f) * 4.5f;
        var hits = Physics2D.OverlapCircleAll(center, reach);
        foreach (var h in hits)
        {
            if (h == null) continue;
            var rb = h.attachedRigidbody;
            if (rb == null) continue;
            if (ignore != null && (h.transform == ignore || h.transform.IsChildOf(ignore))) continue;

            Vector2 d = (Vector2)(rb.worldCenterOfMass - (Vector2)center);
            float dist = d.magnitude;
            if (dist < 1e-3f) { d = Random.insideUnitCircle.normalized; dist = 0.01f; }
            float falloff = Mathf.Clamp01(1f - dist / reach);           // strongest at the centre
            rb.AddForce(d.normalized * strength * falloff * falloff, ForceMode2D.Impulse);
        }
    }

    // ── HITSTOP (brief freeze for impact) ───────────────────────────────────────────────────────
    /// <summary>Momentarily slows time (real-time based, so it always lasts the same wall-clock length),
    /// then restores it — a punchy little freeze on the instant of the burst.</summary>
    public static void Hitstop(float scale = 0.15f, float realSeconds = 0.06f)
    {
        var go = new GameObject("Hitstop");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<CellFxRunner>().StartCoroutine(CellFxRunner.HitstopRoutine(go, scale, realSeconds));
    }

    // ── DUST POOF (a fragment disintegrating) ────────────────────────────────────────────────────
    /// <summary>A little burst of tiny motes that scatter and vanish fast — a cytoplasm fragment
    /// "poofing" into dust as it disintegrates (instead of a slow fade).</summary>
    public static void DustPoof(Vector3 center, float radius, Color color)
    {
        radius = Mathf.Max(radius, 0.1f);
        Color c = Color.Lerp(color, Color.white, 0.35f); c.a = 0.85f;
        int n = Random.Range(5, 9);
        for (int i = 0; i < n; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            float sz = radius * Random.Range(0.25f, 0.6f);
            var d = new GameObject("Dust");
            d.transform.position = center + (Vector3)(Random.insideUnitCircle * radius * 0.4f);
            d.transform.localScale = Vector3.one * sz;
            var sr = d.AddComponent<SpriteRenderer>();
            sr.sprite = GfxUtil.Blob();
            sr.sharedMaterial = GfxUtil.SpriteMaterial();
            sr.color = c;
            sr.sortingOrder = 9;   // just above the gobbets so the poof reads
            var run = d.AddComponent<CellFxRunner>();
            run.StartCoroutine(run.FlyRoutine(dir * radius * Random.Range(1.5f, 3.5f),
                                              Random.Range(-180f, 180f), Random.Range(0.28f, 0.5f)));
        }
    }

    // ── CYTOPLASM CLOUD (diffuse haze left by a dead cell) ───────────────────────────────────────
    /// <summary>A broad, soft, translucent cloud of spilled cytoplasm that expands and lingers over the
    /// death site — the diffuse "the water is cloudy here" remnant, distinct from the solid fragments.</summary>
    public static void CytoplasmCloud(Vector3 center, float radius, Color color)
    {
        radius = Mathf.Max(radius, 0.3f);
        // A few overlapping soft puffs at slightly different sizes/offsets → diffuse, uneven haze.
        int puffs = 3;
        for (int i = 0; i < puffs; i++)
        {
            Vector3 pos = center + (Vector3)(Random.insideUnitCircle * radius * 0.6f);
            float size = radius * Random.Range(2.6f, 4.2f);
            var go = new GameObject("CytoCloud");
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GfxUtil.Blob();
            sr.sharedMaterial = GfxUtil.SpriteMaterial();
            sr.color = new Color(color.r, color.g, color.b, Random.Range(0.16f, 0.26f));
            sr.sortingOrder = 5;   // under the fragments (8), above the cells → visible haze
            var run = go.AddComponent<CellFxRunner>();
            run.StartCoroutine(run.LingerRoutine(Vector2.zero,
                grow: Random.Range(1.5f, 2.0f), settle: Random.Range(0.6f, 1.0f),
                hold: Random.Range(2.5f, 4f),   fade: Random.Range(2.5f, 4f)));
        }
    }

    // ── EATEN: cytoplasm siphon (a strand of goo drawn INTO the predator) ────────────────────────
    /// <summary>A handful of cytoplasm droplets that stream from the eaten cell into the predator's
    /// mouth, accelerating in and shrinking away — the opposite of the pop's outward spray.</summary>
    public static void CytoplasmSiphon(Vector3 from, Transform predator, Color color)
    {
        Color c = color; c.a = 0.8f;
        int n = 6;
        for (int i = 0; i < n; i++)
        {
            Vector3 start = from + (Vector3)(Random.insideUnitCircle * 0.45f);
            var d = NewBlob("CytoSiphon", start, Random.Range(0.25f, 0.6f), c, 2);
            var run = d.AddComponent<CellFxRunner>();
            run.StartCoroutine(run.SiphonRoutine(predator, start, Random.Range(0.22f, 0.42f), Random.Range(0f, 0.12f)));
        }
    }

    // ── EATEN: absorb pulse (the predator's soft "gulp" flash in the prey's colour) ───────────────
    /// <summary>A soft coloured ring that swells and fades at the predator — reads as it absorbing the
    /// prey's mass. Gentler and inward-feeling, unlike the pop's sharp white shockwave.</summary>
    public static void AbsorbPulse(Vector3 center, float radius, Color color)
    {
        radius = Mathf.Max(radius, 0.3f);
        Color c = color; c.a = 0.5f;
        var go = new GameObject("AbsorbPulse");
        go.transform.position = center;
        go.transform.localScale = Vector3.one * radius * 1.5f;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GfxUtil.Circle();
        sr.sharedMaterial = GfxUtil.SpriteMaterial();
        sr.color = c;
        sr.sortingOrder = -20;
        var run = go.AddComponent<CellFxRunner>();
        run.StartCoroutine(run.ExpandRoutine(radius * 1.5f, radius * 2.3f, 0.3f));
    }

    // ── MEMBRANE FRAGMENTS (the wall bursting apart) ────────────────────────────────────────────
    /// <summary>
    /// Torn strips of membrane flung outward, spinning, shrinking and fading — the ruptured cell wall.
    /// Elongated (oriented along travel) and tinted to the membrane so it reads as the skin bursting.
    /// </summary>
    public static void MembraneFragments(Vector3 center, float radius, Color color, int count)
    {
        radius = Mathf.Max(radius, 0.3f);
        // Bright, like the glowing membrane edge itself.
        Color c = color; c.r = Mathf.Clamp01(c.r * 1.2f); c.g = Mathf.Clamp01(c.g * 1.2f); c.b = Mathf.Clamp01(c.b * 1.2f); c.a = 1f;
        count = Mathf.Clamp(count, 6, 18);
        float a0 = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < count; i++)
        {
            float ang = a0 + (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.4f, 0.4f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float len = radius * Random.Range(0.18f, 0.42f);           // smaller torn bits of wall
            var go = NewBlob("MembraneFrag", center + (Vector3)dir * radius * 0.4f, len, c, 3);
            go.transform.localScale = new Vector3(len, len * 0.16f, 1f); // thin torn strip (less circular)
            go.transform.right = dir;
            var run = go.AddComponent<CellFxRunner>();
            // Flung outward and arcing, but not far or fast — a wall tearing, not an explosion.
            run.StartCoroutine(run.FlyRoutine(dir * radius * Random.Range(3.5f, 6.5f),
                                              Random.Range(-480f, 480f), Random.Range(0.45f, 0.75f),
                                              gravity: radius * 2.5f));
        }
    }

    // ── CYTOPLASM SPILL (lingering remnant) ─────────────────────────────────────────────────
    /// <summary>
    /// A burst of cytoplasm that sprays outward, SETTLES, and LINGERS as a green remnant for a few
    /// seconds before slowly fading — so a pop leaves goo behind instead of just vanishing.
    /// Translucent and tinted to the slime so it reads as spilled cytoplasm, not a flat puff.
    /// </summary>
    public static void CytoplasmSpill(Vector3 center, float radius, Color color, float density = 1f)
    {
        radius = Mathf.Max(radius, 0.3f);
        density = Mathf.Clamp(density, 0.15f, 1f);   // <1 = thinner, more transparent, fewer droplets

        // Central stain: a broad translucent pool that spreads and lingers longest.
        var stain = NewBlob("CytoStain", center, radius * 2.4f, new Color(color.r, color.g, color.b, 0.45f * density), 0);
        stain.AddComponent<CellFxRunner>().StartCoroutine(
            stain.GetComponent<CellFxRunner>().LingerRoutine(Vector2.zero, grow: 1.35f, settle: 0.35f, hold: 1.8f, fade: 2.2f));

        // Droplets: fly out, decelerate, settle, then linger and fade. Varied sizes = goo, not dots.
        int n = Mathf.Max(4, Mathf.RoundToInt(10 * density));
        float a0 = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < n; i++)
        {
            float ang = a0 + (i / (float)n) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float size = radius * Random.Range(0.35f, 0.7f);
            var d = NewBlob("CytoDrop", center + (Vector3)dir * radius * 0.25f, size * 2f,
                            new Color(color.r, color.g, color.b, Random.Range(0.6f, 0.85f) * density), 1);
            var run = d.AddComponent<CellFxRunner>();
            run.StartCoroutine(run.LingerRoutine(dir * radius * Random.Range(2.5f, 5f),
                                                 grow: Random.Range(1.0f, 1.3f),
                                                 settle: 0.35f, hold: Random.Range(1.4f, 2.2f),
                                                 fade: Random.Range(1.5f, 2.5f)));
        }
    }

    static GameObject NewBlob(string name, Vector3 pos, float worldDiameter, Color color, int sortingOffset)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * Mathf.Max(worldDiameter, 0.01f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GfxUtil.Blob();
        sr.sharedMaterial = GfxUtil.SpriteMaterial();
        sr.color = color;
        sr.sortingOrder = -28 + sortingOffset;
        return go;
    }

    // ── public API ───────────────────────────────────────────────────────────────────────────
    public static void Pop(GameObject cell, Vector3 center, Color tint, int wedges = 6)
    {
        if (!GetParts(cell, out var mf, out var mr, out var line)) return;

        Mesh src = mf.sharedMesh;
        if (src != null && src.vertexCount > 3)
        {
            foreach (var (mesh, radialDir) in SliceWedges(src, wedges))
            {
                var go = NewMeshObject("CytoWedge", mf.transform, mesh, mr.sharedMaterial, mr.sortingOrder);
                Vector3 outDir = mf.transform.TransformDirection(radialDir).normalized;
                var run = go.AddComponent<CellFxRunner>();
                run.StartCoroutine(run.WedgeRoutine(outDir * Random.Range(2.5f, 5f),
                                                    spin: Random.Range(-300f, 300f),
                                                    duration: Random.Range(0.55f, 0.85f),
                                                    pullTarget: null));
            }
        }

        TearMembrane(line, center, arcs: 5,
                     outwardSpeed: () => Random.Range(2.5f, 5f),
                     spin: () => Random.Range(-360f, 360f),
                     duration: () => Random.Range(0.55f, 0.85f),
                     pullTarget: null, tint: tint);
    }

    public static void Engulf(GameObject cell, Vector3 center, Color tint, Transform predator)
    {
        if (!GetParts(cell, out var mf, out var mr, out var line)) return;
        Vector3 mouth = predator != null ? predator.position : center;

        Mesh src = mf.sharedMesh;
        if (src != null && src.vertexCount > 3)
        {
            var snapshot = CopyMesh(src);
            var go = NewMeshObject("CytoSucked", mf.transform, snapshot, mr.sharedMaterial, mr.sortingOrder);
            var run = go.AddComponent<CellFxRunner>();
            run.StartCoroutine(run.SuckRoutine(predator, 0.3f));
        }

        TearMembrane(line, center, arcs: 4,
                     outwardSpeed: () => Random.Range(0.3f, 1f),
                     spin: () => Random.Range(-200f, 200f),
                     duration: () => Random.Range(0.28f, 0.4f),
                     pullTarget: mouth, tint: tint);
    }

    // ── geometry helpers ───────────────────────────────────────────────────────────────────────

    static bool GetParts(GameObject cell, out MeshFilter mf, out MeshRenderer mr, out LineRenderer line)
    {
        mf = null; mr = null; line = null;
        if (cell == null) return false;
        var jelly = cell.GetComponentInChildren<JellyMesh>();
        if (jelly != null) { mf = jelly.GetComponent<MeshFilter>(); mr = jelly.GetComponent<MeshRenderer>(); }
        line = cell.GetComponentInChildren<LineRenderer>();
        return mf != null && mr != null && mf.sharedMesh != null; // need a mesh to be worth anything
    }

    static Mesh CopyMesh(Mesh src)
    {
        var m = new Mesh { name = "CellFxSnapshot" };
        m.vertices  = src.vertices;
        m.triangles = src.triangles;
        m.uv        = src.uv;
        m.normals   = src.normals;
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// The JellyMesh is a triangle fan: vertex 0 is the centre, 1..N the rim in order. Split the rim
    /// into contiguous wedges; each wedge is a little fan (centre + its slice of rim). Returns each
    /// wedge mesh plus the average radial direction it points (so it can fly outward).
    /// </summary>
    static IEnumerable<(Mesh, Vector3)> SliceWedges(Mesh src, int wedges)
    {
        var verts = src.vertices;
        int rim = verts.Length - 1;            // exclude the centre vertex
        if (rim < 4) yield break;
        wedges = Mathf.Clamp(wedges, 2, rim / 2);
        int per = rim / wedges;
        Vector3 centre = verts[0];

        for (int w = 0; w < wedges; w++)
        {
            int start = 1 + w * per;
            int end   = (w == wedges - 1) ? rim : start + per; // last wedge mops up the remainder
            int span  = end - start + 1;                       // inclusive of the closing vertex
            if (span < 2) continue;

            var vlist = new List<Vector3>(span + 1) { centre };
            Vector3 radial = Vector3.zero;
            for (int i = 0; i <= end - start; i++)
            {
                int idx = start + i;
                if (idx > rim) idx = idx - rim; // wrap the very last closing vertex back to rim start
                vlist.Add(verts[idx]);
                radial += (verts[idx] - centre);
            }

            var tris = new List<int>();
            for (int i = 1; i < vlist.Count - 1; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            if (tris.Count < 3) continue;

            var mesh = new Mesh { name = "Wedge" };
            mesh.SetVertices(vlist);
            mesh.SetTriangles(tris, 0);
            var normals = new Vector3[vlist.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();

            yield return (mesh, radial.sqrMagnitude > 1e-5f ? radial.normalized : Random.insideUnitCircle.normalized);
        }
    }

    /// <summary>Spawn a standalone object that reproduces a piece of the cell mesh, with its own
    /// material instance (so fading it doesn't touch living cells).</summary>
    static GameObject NewMeshObject(string name, Transform from, Mesh mesh, Material srcMat, int sorting)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(from.position, from.rotation);
        go.transform.localScale = from.lossyScale;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = srcMat != null ? new Material(srcMat) : GfxUtil.SpriteMaterial();
        mat.hideFlags = HideFlags.HideAndDontSave;
        mr.sharedMaterial = mat;
        mr.sortingOrder = sorting + 1;
        return go;
    }

    // ── membrane tearing (real LineRenderer outline) ───────────────────────────────────────────

    // ── MEMBRANE RUPTURE (balloon-rubber recoil) ────────────────────────────────────────────────
    /// <summary>
    /// The defining moment of a POP: the intact membrane outline tears into a few connected arcs that
    /// snap OUTWARD and curl away fast — like balloon rubber recoiling from the rupture — then thin and
    /// vanish. Reads as the skin failing suddenly, not the cell crumbling into bits.
    /// </summary>
    public static void RuptureMembrane(LineRenderer membrane, Vector3 center, float radius, Color tint)
    {
        if (membrane == null) return;
        radius = Mathf.Max(radius, 0.3f);
        TearMembrane(membrane, center, arcs: 5,
            outwardSpeed: () => radius * Random.Range(3f, 6f),   // snap outward hard
            spin:         () => Random.Range(-900f, 900f),        // whip/curl
            duration:     () => Random.Range(0.28f, 0.5f),        // brief — it's a rupture
            pullTarget: null, tint: tint);
    }

    static void TearMembrane(LineRenderer src, Vector3 center, int arcs,
                             System.Func<float> outwardSpeed, System.Func<float> spin,
                             System.Func<float> duration, Vector3? pullTarget, Color tint)
    {
        if (src == null || src.positionCount < 6) return;
        int n = src.positionCount;
        var raw = new Vector3[n];
        src.GetPositions(raw);
        for (int i = 0; i < n; i++) if (!src.useWorldSpace) raw[i] = src.transform.TransformPoint(raw[i]);
        bool closed = (raw[0] - raw[n - 1]).sqrMagnitude < 1e-4f;
        int count = closed ? n - 1 : n;

        arcs = Mathf.Clamp(arcs, 2, count / 2);
        int gap = Mathf.Max(1, count / (arcs * 8));
        float width = Mathf.Max(src.widthMultiplier, 0.02f);
        Color baseCol = src.startColor; if (baseCol.a < 0.05f) baseCol = tint;
        Material mat = src.sharedMaterial != null ? src.sharedMaterial : GfxUtil.SpriteMaterial();
        int sorting = src.sortingOrder + 2;

        int idx = Random.Range(0, count);
        for (int a = 0; a < arcs; a++)
        {
            int len = Mathf.Max(2, count / arcs - gap);
            var pts = new Vector3[len];
            Vector3 centroid = Vector3.zero;
            for (int k = 0; k < len; k++) { pts[k] = raw[(idx + k) % count]; centroid += pts[k]; }
            centroid /= len;
            idx = (idx + len + gap) % count;

            var go = new GameObject("MembraneArc");
            go.transform.position = centroid;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = len;
            for (int k = 0; k < len; k++) lr.SetPosition(k, pts[k] - centroid);
            lr.widthMultiplier = width;
            lr.numCapVertices = 2; lr.numCornerVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sharedMaterial = mat;
            lr.startColor = lr.endColor = baseCol;
            lr.sortingOrder = sorting;

            Vector3 outward = (centroid - center).sqrMagnitude > 1e-4f
                ? (centroid - center).normalized : (Vector3)Random.insideUnitCircle.normalized;
            Vector3 vel = outward * outwardSpeed();
            if (pullTarget.HasValue) vel += (pullTarget.Value - centroid).normalized * 3f;

            var run = go.AddComponent<CellFxRunner>();
            run.StartCoroutine(run.ArcRoutine(vel, spin(), duration(), pullTarget));
        }
    }
}

/// <summary>Runs a single FX coroutine on a throwaway object, then destroys it (and its material).</summary>
public class CellFxRunner : MonoBehaviour
{
    const string OpacityProp = "_BodyOpacity";
    Material ownedMat;

    /// <summary>Dip Time.timeScale for a fixed wall-clock beat, then restore it — impact hitstop.</summary>
    public static IEnumerator HitstopRoutine(GameObject owner, float scale, float realSeconds)
    {
        Time.timeScale = Mathf.Clamp(scale, 0.01f, 1f);
        yield return new WaitForSecondsRealtime(Mathf.Max(realSeconds, 0.01f));
        Time.timeScale = 1f;
        Object.Destroy(owner);
    }

    /// <summary>Expand a sprite from one scale to another while fading — impact shockwave. Self-destructs.</summary>
    public IEnumerator ExpandRoutine(float fromScale, float toScale, float duration)
    {
        var sr = GetComponent<SpriteRenderer>();
        Color c = sr.color; float a0 = c.a;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(duration, 0.01f);
            float e = Mathf.SmoothStep(0f, 1f, t);
            transform.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, e);
            c.a = Mathf.Lerp(a0, 0f, e); sr.color = c;
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>A torn membrane strip: flung outward (lightly drag + optional gravity so it arcs),
    /// spins, shrinks and fades. Self-destructs.</summary>
    public IEnumerator FlyRoutine(Vector2 velocity, float spin, float duration, float gravity = 0f)
    {
        var sr = GetComponent<SpriteRenderer>();
        Color c = sr.color; float a0 = c.a;
        Vector3 baseScale = transform.localScale;
        float t = 0f;
        while (t < 1f)
        {
            float dt = Time.deltaTime;
            t += dt / Mathf.Max(duration, 0.01f);
            velocity *= Mathf.Max(0f, 1f - 1.2f * dt);        // light drag → chunks travel far
            velocity += Vector2.down * gravity * dt;          // arc down under gravity
            transform.position += (Vector3)(velocity * dt);
            transform.Rotate(0f, 0f, spin * dt);
            transform.localScale = baseScale * Mathf.Lerp(1f, 0.4f, t);
            c.a = Mathf.Lerp(a0, 0f, t * t); sr.color = c;
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>Spilled goo: sprays out & swells (settle), holds full (linger), then fades. Self-destructs.</summary>
    public IEnumerator LingerRoutine(Vector2 velocity, float grow, float settle, float hold, float fade)
    {
        var sr = GetComponent<SpriteRenderer>();
        Color c = sr.color; float a0 = c.a;
        float baseScale = transform.localScale.x;

        float t = 0f;
        while (t < 1f)   // settle: fly outward with drag while swelling
        {
            t += Time.deltaTime / Mathf.Max(settle, 0.01f);
            velocity *= 0.85f;
            transform.position += (Vector3)(velocity * Time.deltaTime);
            transform.localScale = Vector3.one * baseScale * Mathf.Lerp(0.6f, grow, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        yield return new WaitForSeconds(hold);   // linger as a remnant
        t = 0f;
        while (t < 1f)   // fade out
        {
            t += Time.deltaTime / Mathf.Max(fade, 0.01f);
            c.a = Mathf.Lerp(a0, 0f, t);
            sr.color = c;
            yield return null;
        }
        Destroy(gameObject);
    }

    void StartFade(MeshRenderer mr, out float startOpacity)
    {
        ownedMat = mr != null ? mr.sharedMaterial : null;
        startOpacity = (ownedMat != null && ownedMat.HasProperty(OpacityProp))
            ? ownedMat.GetFloat(OpacityProp) : 1f;
    }
    void SetOpacity(float v) { if (ownedMat != null && ownedMat.HasProperty(OpacityProp)) ownedMat.SetFloat(OpacityProp, v); }
    void OnDestroy() { if (ownedMat != null) Destroy(ownedMat); }

    /// <summary>A wedge of cytoplasm flung outward (or pulled to a mouth): spins, shrinks, fades.</summary>
    public IEnumerator WedgeRoutine(Vector3 velocity, float spin, float duration, Vector3? pullTarget)
    {
        var mr = GetComponent<MeshRenderer>();
        StartFade(mr, out float op0);
        Vector3 baseScale = transform.localScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(duration, 0.01f);
            if (pullTarget.HasValue)
                velocity = Vector3.Lerp(velocity, (pullTarget.Value - transform.position) * 6f, 0.15f);
            velocity *= 0.94f;
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(0f, 0f, spin * Time.deltaTime);
            transform.localScale = baseScale * Mathf.Lerp(1f, 0.3f, t);
            SetOpacity(Mathf.Lerp(op0, 0f, t * t));
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>The whole cytoplasm snapshot dragged into the predator, squashing as it shrinks away.</summary>
    public IEnumerator SuckRoutine(Transform predator, float duration)
    {
        var mr = GetComponent<MeshRenderer>();
        StartFade(mr, out float op0);
        Vector3 start = transform.position;
        Vector3 baseScale = transform.localScale;
        Quaternion baseRot = transform.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(duration, 0.01f);
            float e = Mathf.SmoothStep(0f, 1f, t);
            Vector3 target = predator != null ? predator.position : start;
            transform.position = Vector3.Lerp(start, target, e * 0.85f);

            float shrink = Mathf.Lerp(1f, 0.05f, e);
            float stretch = 1f + 0.6f * e, squash = 1f / Mathf.Sqrt(stretch);
            Vector3 toPred = target - start;
            if (toPred.sqrMagnitude > 1e-4f)
            {
                float ang = Mathf.Atan2(toPred.y, toPred.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0, 0, ang);
                transform.localScale = new Vector3(baseScale.x * shrink * stretch, baseScale.y * shrink * squash, baseScale.z);
            }
            else transform.localScale = baseScale * shrink;

            SetOpacity(Mathf.Lerp(op0, 0f, e * e));
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>A cytoplasm droplet siphoned into the predator: after an optional delay it accelerates
    /// from its start point to the predator's (live) position, shrinking and fading away. Sprite-based.</summary>
    public IEnumerator SiphonRoutine(Transform predator, Vector3 start, float duration, float delay)
    {
        var sr = GetComponent<SpriteRenderer>();
        Color c = sr.color; float a0 = c.a;
        Vector3 baseScale = transform.localScale;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(duration, 0.01f);
            float e = t * t;                                   // ease-in → accelerates into the mouth
            Vector3 target = predator != null ? predator.position : start;
            transform.position = Vector3.Lerp(start, target, e);
            transform.localScale = baseScale * Mathf.Lerp(1f, 0.1f, e);
            c.a = Mathf.Lerp(a0, 0f, e); sr.color = c;
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>A torn membrane strip: flies out (or curls toward a mouth), spins, thins, and fades.</summary>
    public IEnumerator ArcRoutine(Vector3 velocity, float spinDegPerSec, float duration, Vector3? pullTarget)
    {
        var lr = GetComponent<LineRenderer>();
        Color c = lr.startColor; float a0 = c.a;
        float w0 = lr.widthMultiplier;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(duration, 0.01f);
            if (pullTarget.HasValue)
                velocity = Vector3.Lerp(velocity, (pullTarget.Value - transform.position) * 6f, 0.15f);
            velocity *= 0.95f;
            transform.position += velocity * Time.deltaTime;
            transform.Rotate(0f, 0f, spinDegPerSec * Time.deltaTime);
            lr.widthMultiplier = w0 * Mathf.Lerp(1f, 0.25f, t);
            c.a = Mathf.Lerp(a0, 0f, t * t);
            lr.startColor = lr.endColor = c;
            yield return null;
        }
        Destroy(gameObject);
    }
}

/// <summary>
/// Simulates the metaball blobs for a cell-pop (fly out, drag, gravity, shrink) and feeds them to the
/// CLAY/MetaballGoo shader each frame, then destroys itself. Created by CellFx.MetaballBurst.
/// </summary>
public class MetaballRunner : MonoBehaviour
{
    const int MaxBlobs = 32;
    struct Blob { public Vector2 pos, vel; public float radius, r0; }
    Blob[] blobs;
    readonly Vector4[] data = new Vector4[MaxBlobs];   // shader array is float4[32]
    Material mat;
    float worldSize, life, age, grav, baseRadius;
    int count;
    bool siphon;
    Transform target;
    Vector3 centerWorld;

    public void Init(Material m, float worldSizeUnits, float cellRadius, int blobCount, float lifetime)
    {
        mat = m;
        worldSize = Mathf.Max(worldSizeUnits, 0.01f);
        life = Mathf.Max(lifetime, 0.05f);
        grav = cellRadius * 2f;
        count = Mathf.Clamp(blobCount, 1, MaxBlobs);
        blobs = new Blob[count];
        for (int i = 0; i < count; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            // Start tightly packed, then spread apart into separate gobbets — but not far or fast.
            blobs[i].pos = dir * cellRadius * Random.Range(0.02f, 0.2f);
            blobs[i].vel = dir * cellRadius * Random.Range(3.5f, 7f);
            blobs[i].r0 = cellRadius * Random.Range(0.22f, 0.45f);
            blobs[i].radius = blobs[i].r0;
        }
        Push();
    }

    /// <summary>EATEN mode: the goo starts as gobbets spread across the cell body, then gets sucked into
    /// the predator — spiralling/streaming inward, stretching and shrinking away into the mouth.</summary>
    public void InitSiphon(Material m, float worldSizeUnits, float cellRadius, int blobCount, float lifetime,
                           Transform predator, Vector3 quadCenterWorld)
    {
        mat = m;
        worldSize = Mathf.Max(worldSizeUnits, 0.01f);
        life = Mathf.Max(lifetime, 0.05f);
        baseRadius = Mathf.Max(cellRadius, 0.1f);
        siphon = true;
        target = predator;
        centerWorld = quadCenterWorld;
        count = Mathf.Clamp(blobCount, 1, MaxBlobs);
        blobs = new Blob[count];
        for (int i = 0; i < count; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            blobs[i].pos = dir * cellRadius * Random.Range(0.3f, 0.95f);           // spread across the body
            blobs[i].vel = new Vector2(-dir.y, dir.x) * cellRadius * Random.Range(-1.5f, 1.5f); // slight swirl
            blobs[i].r0 = cellRadius * Random.Range(0.28f, 0.55f);
            blobs[i].radius = blobs[i].r0;
        }
        Push();
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / life);
        float dt = Time.deltaTime;

        if (siphon)
        {
            Vector2 tgt = target != null ? (Vector2)(target.position - centerWorld) : Vector2.zero;
            float pull = baseRadius * Mathf.Lerp(9f, 45f, t);        // accelerate inward as it's consumed
            for (int i = 0; i < count; i++)
            {
                Vector2 to = tgt - blobs[i].pos;
                blobs[i].vel += to.normalized * pull * dt;
                blobs[i].vel *= Mathf.Max(0f, 1f - 3.5f * dt);       // damp so they stream, not overshoot
                blobs[i].pos += blobs[i].vel * dt;
                // Thin out as they near the mouth AND over time → unravel to nothing.
                float near = Mathf.Clamp01(to.magnitude / (baseRadius * 1.6f));
                blobs[i].radius = blobs[i].r0 * Mathf.Min(near, Mathf.Lerp(1f, 0.08f, t));
            }
            Push();
            if (mat != null) mat.SetFloat("_GlobalAlpha", Mathf.Clamp01(1f - t * t));
            if (age >= life) { if (mat != null) Destroy(mat); Destroy(gameObject); }
            return;
        }

        for (int i = 0; i < count; i++)
        {
            blobs[i].vel *= Mathf.Max(0f, 1f - 0.9f * dt);   // light drag → gobbets keep flying, really disperse
            blobs[i].vel += Vector2.down * grav * dt;         // gravity — goo arcs down
            blobs[i].pos += blobs[i].vel * dt;
            // Hold size early (the rip), then shrink to nothing as each gobbet thins out and dies.
            blobs[i].radius = blobs[i].r0 * Mathf.Lerp(1f, 0.15f, t * t);
        }
        Push();
        if (mat != null) mat.SetFloat("_GlobalAlpha", Mathf.Clamp01(1f - t * t));
        if (age >= life) { if (mat != null) Destroy(mat); Destroy(gameObject); }
    }

    void Push()
    {
        if (mat == null) return;
        for (int i = 0; i < count; i++)
        {
            Vector2 uv = new Vector2(0.5f, 0.5f) + blobs[i].pos / worldSize;
            data[i] = new Vector4(uv.x, uv.y, blobs[i].radius / worldSize, 1f);
        }
        mat.SetVectorArray("_Blobs", data);
        mat.SetInt("_BlobCount", count);
    }
}
