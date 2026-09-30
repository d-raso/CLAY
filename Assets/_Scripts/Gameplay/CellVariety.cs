using UnityEngine;

/// <summary>
/// A cell "species" — a bundle of geometry, stats and look that makes one AI cell visibly distinct
/// from another. Applied to a freshly-instantiated cell clone by <see cref="CellVariety"/>.
/// </summary>
[System.Serializable]
public class CellSpecies
{
    public string name = "Cell";

    [Header("Geometry (set on JellyBodyBuilder before it builds)")]
    [Tooltip("Membrane node count. Few (6-8) = angular/spiky; many (14-18) = smooth/round.")]
    public int nodeCount = 12;
    public float radius = 2f;
    [Tooltip("Membrane spring frequency. High = firm/tense; low = floppy/wobbly.")]
    public float stiffness = 5f;
    [Range(0.05f, 1f)] public float dampening = 0.5f;

    [Header("Silhouette")]
    public JellyBodyBuilder.CellShape shape = JellyBodyBuilder.CellShape.Round;
    [Range(0f, 0.6f)] public float shapeStrength = 0.35f;
    [Range(3, 8)] public int shapeLobes = 5;

    [Header("Stats")]
    public Vector2 massRange = new Vector2(0.7f, 2f);
    [Range(0f, 1f)] public float rigidity = 0.3f;

    [Header("Look")]
    [Tooltip("Cytoplasm tint (vertex colour on the body).")]
    public Color bodyColor = new Color(0.4f, 1f, 0.5f);
    public Color membraneColor = new Color(0.5f, 1f, 0.6f);
    public float membraneWidth = 0.12f;
    [Range(0.1f, 1f)] public float bodyOpacity = 0.5f;
}

/// <summary>
/// Holds a built-in library of distinct species and applies a chosen species (scaled by evolutionary
/// generation) to a cell clone. Generation scaling makes later generations bigger, tougher and a bit
/// recoloured — so successive waves visibly "evolve".
/// </summary>
public static class CellVariety
{
    /// <summary>Built-in species — visually and mechanically distinct. Used when a spawner has none set.</summary>
    public static readonly CellSpecies[] Library =
    {
        new CellSpecies {
            name = "Verdant Blob", nodeCount = 16, radius = 2.1f, stiffness = 4f, dampening = 0.6f,
            shape = JellyBodyBuilder.CellShape.Organic, shapeStrength = 0.55f,
            massRange = new Vector2(0.9f, 2.4f), rigidity = 0.25f,
            bodyColor = new Color(0.35f, 1f, 0.5f), membraneColor = new Color(0.5f, 1f, 0.65f),
            membraneWidth = 0.13f, bodyOpacity = 0.5f },

        new CellSpecies {
            name = "Spiked Drifter", nodeCount = 14, radius = 1.7f, stiffness = 9f, dampening = 0.3f,
            shape = JellyBodyBuilder.CellShape.Organic, shapeStrength = 0.5f,
            massRange = new Vector2(0.6f, 1.5f), rigidity = 0.55f,
            bodyColor = new Color(1f, 0.65f, 0.3f), membraneColor = new Color(1f, 0.8f, 0.35f),
            membraneWidth = 0.1f, bodyOpacity = 0.65f },

        new CellSpecies {
            name = "Amoeba", nodeCount = 18, radius = 2.4f, stiffness = 2.5f, dampening = 0.8f,
            shape = JellyBodyBuilder.CellShape.Organic, shapeStrength = 0.5f,
            massRange = new Vector2(1.0f, 3.0f), rigidity = 0.15f,
            bodyColor = new Color(0.55f, 0.85f, 1f), membraneColor = new Color(0.6f, 0.9f, 1f),
            membraneWidth = 0.11f, bodyOpacity = 0.4f },

        new CellSpecies {
            name = "Armored Cyst", nodeCount = 10, radius = 1.9f, stiffness = 12f, dampening = 0.4f,
            shape = JellyBodyBuilder.CellShape.Organic, shapeStrength = 0.4f,
            massRange = new Vector2(1.2f, 3.2f), rigidity = 0.85f,
            bodyColor = new Color(0.8f, 0.5f, 0.9f), membraneColor = new Color(0.95f, 0.6f, 1f),
            membraneWidth = 0.16f, bodyOpacity = 0.75f },

        new CellSpecies {
            name = "Runt", nodeCount = 9, radius = 1.3f, stiffness = 7f, dampening = 0.45f,
            shape = JellyBodyBuilder.CellShape.Organic, shapeStrength = 0.45f,
            massRange = new Vector2(0.4f, 0.9f), rigidity = 0.2f,
            bodyColor = new Color(1f, 0.95f, 0.5f), membraneColor = new Color(1f, 1f, 0.6f),
            membraneWidth = 0.09f, bodyOpacity = 0.55f },

        new CellSpecies {
            name = "Crimson Hunter", nodeCount = 12, radius = 2.0f, stiffness = 6f, dampening = 0.5f,
            shape = JellyBodyBuilder.CellShape.Organic, shapeStrength = 0.5f,
            massRange = new Vector2(1.0f, 2.6f), rigidity = 0.4f,
            bodyColor = new Color(1f, 0.4f, 0.45f), membraneColor = new Color(1f, 0.5f, 0.5f),
            membraneWidth = 0.13f, bodyOpacity = 0.6f },
    };

    public static CellSpecies Pick(CellSpecies[] pool) =>
        (pool != null && pool.Length > 0) ? pool[UnityEngine.Random.Range(0, pool.Length)]
                                          : Library[UnityEngine.Random.Range(0, Library.Length)];

    /// <summary>Rotate a colour's hue by <paramref name="delta"/> (0..1), keeping saturation/value/alpha.</summary>
    static Color ShiftHue(Color c, float delta)
    {
        Color.RGBToHSV(c, out float h, out float sat, out float v);
        Color r = Color.HSVToRGB(Mathf.Repeat(h + delta, 1f), sat, v);
        r.a = c.a;
        return r;
    }

    /// <summary>Multiply a colour's saturation (keeps hue/value/alpha), so tints pop against the water.</summary>
    static Color Saturate(Color c, float mul)
    {
        Color.RGBToHSV(c, out float h, out float sat, out float v);
        Color r = Color.HSVToRGB(h, Mathf.Clamp01(sat * mul), v);
        r.a = c.a;
        return r;
    }

    /// <summary>
    /// Apply a species to a freshly-instantiated cell. Call BEFORE the clone's JellyBodyBuilder.Start()
    /// runs (i.e. the same frame it was Instantiated) so the geometry takes. <paramref name="generation"/>
    /// (0-based) scales the cell up and tints it slightly so later waves look evolved.
    /// </summary>
    public static void Apply(GameObject clone, CellSpecies s, int generation = 0)
    {
        if (clone == null || s == null) return;
        float genMass = 1f + generation * 0.12f;                 // later gens are larger
        float genRigid = Mathf.Clamp01(s.rigidity + generation * 0.04f);

        // ── Geometry ──
        var builder = clone.GetComponent<JellyBodyBuilder>();
        if (builder != null)
        {
            builder.nodeCount = Mathf.Clamp(s.nodeCount, 5, 24);
            builder.radius    = s.radius;
            builder.stiffness = Mathf.Max(s.stiffness, 5f);       // floor so AI cells aren't much wobblier than the player
            builder.dampening = Mathf.Max(s.dampening, 0.45f);
            builder.shape         = s.shape;
            builder.shapeStrength = Random.Range(0.3f, 0.62f);   // per-cell so every silhouette differs
            builder.shapeLobes    = s.shapeLobes;
        }

        // ── Stats ──
        var bio = clone.GetComponent<CellBiomass>();
        if (bio != null)
        {
            bio.membraneRigidity = genRigid;
            bio.mass = UnityEngine.Random.Range(s.massRange.x, s.massRange.y) * genMass;
            bio.fxColor = s.bodyColor;   // pop spill matches the body
            // Raise the burst ceiling a lot so cells don't POP just from eating a meal — popping is
            // now a rare "overgrew massively" event, not something a single kill triggers.
            bio.baseMaxMass = 16f;
            bio.rigidityMassBonus = 24f;
        }

        // ── Look ──
        var jelly = clone.GetComponentInChildren<JellyMesh>();
        if (jelly != null)
        {
            jelly.tint = Color.white;                 // colour now comes from the material, not vertex tint
            var mr = jelly.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                // Give the cell its own instance of the scripted body shader so colour + vein pattern
                // are unique. Falls back to whatever material is already there if the shader is missing.
                var sh = Shader.Find("CLAY/CellBody2D");
                if (sh != null)
                {
                    var mat = new Material(sh);
                    // Duller, more muted palette — desaturated a touch so cells don't look neon.
                    Color body = Saturate(s.bodyColor, 0.72f) * 0.9f;
                    // Vein colour: mostly a real hue shift → multicolour cells; sometimes a deep shade.
                    Color vein = Random.value < 0.7f
                        ? Saturate(ShiftHue(body, Random.Range(-0.45f, 0.45f)), 0.8f) * Random.Range(0.55f, 1.0f)
                        : body * Random.Range(0.3f, 0.55f);
                    // Organelle spots: a contrasting hue or a soft speckle.
                    Color spot = Random.value < 0.6f ? Saturate(ShiftHue(body, Random.Range(0.25f, 0.65f)), 0.85f)
                                                     : Color.Lerp(body, Color.white, 0.45f);

                    mat.SetColor("_Tint", body);
                    mat.SetColor("_Tint2", vein);
                    mat.SetColor("_SpotColor", spot);
                    mat.SetColor("_RimColor", Saturate(s.membraneColor, 0.85f) * Random.Range(0.8f, 1.3f));
                    mat.SetFloat("_NoiseScale",   Random.Range(2.5f, 9f));
                    mat.SetFloat("_VeinContrast", Random.Range(0.9f, 2.2f));   // stronger two-tone
                    mat.SetFloat("_SpotScale",    Random.Range(5f, 20f));
                    // ~80% of cells have visible organelle spots now; the rest are smooth.
                    mat.SetFloat("_SpotStrength", Random.value < 0.8f ? Random.Range(0.25f, 0.7f) : 0f);
                    mat.SetFloat("_RimPower",    Random.Range(1.4f, 4.5f));
                    mat.SetFloat("_RimStrength", Random.Range(0.7f, 1.9f));
                    mat.SetFloat("_Seed", Random.Range(0f, 999f));            // unique pattern per cell
                    // Transparency varies widely — some near-solid, some ghostly.
                    mat.SetFloat("_BodyOpacity", Mathf.Clamp01(s.bodyOpacity * Random.Range(0.45f, 1.4f)));
                    mr.material = mat;
                }
                else if (mr.material.HasProperty("_BodyOpacity"))
                {
                    mr.material.SetFloat("_BodyOpacity", s.bodyOpacity);
                }
            }
        }

        var line = clone.GetComponentInChildren<LineRenderer>();
        if (line != null)
        {
            line.startColor = line.endColor = s.membraneColor;
            line.widthMultiplier = s.membraneWidth;
        }

        // Let the water current carry this AI cell (player gets its own FlowFieldPush in the scene).
        if (clone.GetComponent<FlowFieldPush>() == null)
            clone.AddComponent<FlowFieldPush>();
    }
}
