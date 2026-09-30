using UnityEngine;

/// <summary>
/// An edible fragment of cytoplasm left when a cell POPS. It carries a share of the dead cell's mass
/// (∝ its size), drifts on the current, and can be engulfed by any nearby cell (via its EdibleEntity)
/// for that mass. Rendered as a small, wobbly, unstable metaball fragment (a few sub-blobs jittering),
/// not a smooth ball. If nobody eats it, it destabilises and POOFS into dust after a water-condition
/// lifetime rather than fading away.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CytoGobbet : MonoBehaviour
{
    Rigidbody2D rb;
    Material mat;          // metaball material (null → sprite fallback)
    SpriteRenderer sr;     // sprite fallback
    float lifetime, age, worldSize;
    Vector3 baseScale;
    Color color;
    const float FlowInfluence = 4f;

    // A handful of sub-blobs (UV space) whose offsets/radii wobble → irregular, unstable fragment.
    const int NB = 6;
    readonly Vector2[] bOff = new Vector2[NB];
    readonly float[] bR = new float[NB];
    readonly float[] bPhase = new float[NB];
    readonly Vector4[] blobData = new Vector4[32];

    /// <summary>Spawn the spray of edible fragments for a popped cell: total edible mass = cellMass ×
    /// cyto-fraction (reduced by organelles), split across fragments by their area.</summary>
    public static void SpawnBurst(Vector3 center, float cellRadius, float cellMass, int organelles, Color color)
    {
        var cfg = CytoGobbetConfig.Active;
        float cytoMass = cellMass * cfg.CytoFraction(organelles);
        if (cytoMass <= 0.02f) return;

        // A handful of small, very distorted fragments carry the edible mass — the bulk of the visual is
        // the swarm. Smaller and more numerous than before so nothing reads as a big round bubble.
        int n = Mathf.Clamp(Mathf.RoundToInt(cellRadius * 2.2f), 3, Mathf.Max(3, cfg.maxGobbets));
        float[] r = new float[n];
        float areaSum = 0f;
        for (int i = 0; i < n; i++) { r[i] = cellRadius * Random.Range(0.09f, 0.2f); areaSum += r[i] * r[i]; }

        for (int i = 0; i < n; i++)
        {
            float gMass = cytoMass * (r[i] * r[i] / areaSum);
            float ang = Random.value * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector3 pos = center + (Vector3)(dir * cellRadius * Random.Range(0.1f, 0.7f));
            Vector2 vel = dir * cellRadius * Random.Range(0.6f, 2f);   // ooze out slightly, then settle
            Spawn(pos, vel, gMass, r[i], color, cfg.DissolveTime());
        }
    }

    public static CytoGobbet Spawn(Vector3 pos, Vector2 vel, float mass, float radius, Color color, float lifetime)
    {
        var cfg = CytoGobbetConfig.Active;
        var sh = Shader.Find("CLAY/MetaballGoo");

        GameObject go = sh != null ? GameObject.CreatePrimitive(PrimitiveType.Quad) : new GameObject("CytoGobbet");
        go.name = "CytoGobbet";
        // Remove the Quad's 3D MeshCollider IMMEDIATELY (a deferred Destroy still conflicts with Rigidbody2D).
        var mc = go.GetComponent<Collider>(); if (mc != null) Object.DestroyImmediate(mc);
        go.transform.position = pos;

        Material mat = null; SpriteRenderer sr = null;
        float worldSize = radius * 4f;
        if (sh != null)
        {
            go.transform.localScale = Vector3.one * worldSize;
            mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            Color tint = color * 0.9f; tint.a = 1f;
            mat.SetColor("_Tint", tint);
            mat.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.25f));
            mat.SetFloat("_Threshold", 1.15f);
            mat.SetFloat("_EdgeSoftness", 0.3f);
            mat.SetFloat("_NoiseScale", Random.Range(8f, 14f));
            mat.SetFloat("_Opacity", 0.85f);
            mat.SetFloat("_GlobalAlpha", 1f);       // no fade — it poofs instead
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingOrder = 8;
        }
        else
        {
            go.transform.localScale = Vector3.one * radius * 2.2f;
            sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GfxUtil.Blob();
            sr.sharedMaterial = GfxUtil.SpriteMaterial();
            sr.color = color; sr.sortingOrder = 8;
        }

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.linearDamping = 1.6f;
        rb.linearVelocity = vel;

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = sh != null ? 0.28f : 0.5f;   // local units; ≈ the visual size after transform scale

        var e = go.AddComponent<EdibleEntity>();
        e.biomassValue = mass;
        e.matterValue  = mass * cfg.matterPerMass;

        var g = go.AddComponent<CytoGobbet>();
        g.Init(rb, mat, sr, lifetime, worldSize, color);
        return g;
    }

    void Init(Rigidbody2D body, Material metaballMat, SpriteRenderer spriteRenderer, float life, float ws, Color col)
    {
        rb = body; mat = metaballMat; sr = spriteRenderer;
        lifetime = Mathf.Max(0.5f, life);
        worldSize = ws; color = col;
        baseScale = transform.localScale;
        for (int i = 0; i < NB; i++)
        {
            bOff[i] = Random.insideUnitCircle * 0.22f;      // wide, ASYMMETRIC cluster → lumpy amoeba, not a ball
            bR[i]   = Random.Range(0.05f, 0.12f);            // varied blob sizes → uneven silhouette
            bPhase[i] = Random.Range(0f, Mathf.PI * 2f);
        }
        FeedBlobs(1f);
    }

    void FixedUpdate()
    {
        var fm = FlowFieldManager.Instance;
        if (fm != null) rb.AddForce(fm.SampleFlowAtPosition(rb.position) * FlowInfluence * rb.mass);
    }

    void Update()
    {
        age += Time.deltaTime;

        // Instability ramps up over the last ~30% of life — the fragment shakes harder before it goes.
        float t01 = age / lifetime;
        float instab = 1f + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, t01)) * 2f;
        FeedBlobs(instab);

        if (age >= lifetime)
        {
            CellFx.DustPoof(transform.position, worldSize * 0.25f, color);   // poof into dust, don't fade
            Destroy(gameObject);
        }
    }

    // Jitter the sub-blobs each frame → wobbly, unstable fragment.
    void FeedBlobs(float instab)
    {
        if (mat == null) return;
        float time = Time.time;
        for (int i = 0; i < NB; i++)
        {
            float wob = 0.06f * instab;
            Vector2 off = bOff[i] + new Vector2(Mathf.Sin(time * 6.0f + bPhase[i]),
                                                Mathf.Cos(time * 5.3f + bPhase[i] * 1.3f)) * wob;
            float r = bR[i] * (1f + Mathf.Sin(time * 7.0f + bPhase[i]) * 0.28f * instab);
            blobData[i] = new Vector4(0.5f + off.x, 0.5f + off.y, r, 0f);
        }
        mat.SetVectorArray("_Blobs", blobData);
        mat.SetInt("_BlobCount", NB);
    }

    void OnDestroy() { if (mat != null) Destroy(mat); }
}
