using UnityEngine;

/// <summary>
/// The cell POP as an EXPANDING METABALL SHELL, not a cloud of separate bubbles.
///
/// A set of mass-points start on a small ring and blow OUTWARD. All of them feed a single metaball
/// surface (one quad, CLAY/MetaballGoo), so neighbouring points fuse into ARCS while the growing gaps
/// between them open up — the shell is "visible only in some parts", breaking apart as it expands, then
/// thinning away. Those same points ARE the edible mass: each carries an invisible trigger collider +
/// EdibleEntity, so nearby cells nibble the shell away chunk by chunk (the goo vanishes where eaten).
/// Uneaten chunks dissolve (poof) after a water-condition lifetime.
///
/// So the invisible mass-points dictate where the food goes AND define the visible pop — one system.
/// </summary>
public class PopShell : MonoBehaviour
{
    const int Max = 32;

    class Chunk
    {
        public Vector2 pos, vel;
        public float r0, radius, life, age;
        public bool dead;
        public GameObject go;         // invisible collider carrier (child)
        public CircleCollider2D col;
    }

    Chunk[] chunks;
    int count;
    Material mat;
    float worldSize;
    Color color;
    readonly Vector4[] data = new Vector4[Max];

    public static void Burst(Vector3 center, float cellRadius, float cellMass, int organelles, Color color)
    {
        cellRadius = Mathf.Max(cellRadius, 0.3f);
        var cfg = CytoGobbetConfig.Active;
        float cytoMass = Mathf.Max(0f, cellMass * cfg.CytoFraction(organelles));

        var root = new GameObject("PopShell");
        root.transform.position = center;
        root.AddComponent<PopShell>().Init(cellRadius, cytoMass, color, cfg);
    }

    void Init(float cellRadius, float cytoMass, Color col, CytoGobbetConfig cfg)
    {
        color = col;
        worldSize = cellRadius * 8f;
        count = Mathf.Clamp(Mathf.RoundToInt(cellRadius * 4f), 10, Max);

        var sh = Shader.Find("CLAY/MetaballGoo");
        if (sh != null)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ShellGoo";
            var mc = quad.GetComponent<Collider>(); if (mc != null) DestroyImmediate(mc);
            quad.transform.SetParent(transform, false);
            quad.transform.localScale = Vector3.one * worldSize;
            mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            Color tint = color * 0.9f; tint.a = 1f;
            mat.SetColor("_Tint", tint);
            mat.SetColor("_RimColor", Color.Lerp(color, Color.white, 0.3f));
            mat.SetFloat("_Threshold", 1.25f);       // arcs fuse; gaps open as points spread
            mat.SetFloat("_EdgeSoftness", 0.24f);
            mat.SetFloat("_NoiseScale", Random.Range(7f, 12f));
            mat.SetFloat("_Opacity", 0.8f);
            mat.SetFloat("_GlobalAlpha", 1f);
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingOrder = 9;
        }

        // Mass-points on a ring, sized by mass; they blow outward at varied speeds so the shell breaks
        // into uneven arcs rather than a perfect expanding ring.
        chunks = new Chunk[count];
        float[] r0 = new float[count]; float areaSum = 0f;
        for (int i = 0; i < count; i++) { r0[i] = cellRadius * Random.Range(0.2f, 0.4f); areaSum += r0[i] * r0[i]; }

        float a0 = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < count; i++)
        {
            float ang = a0 + (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

            var c = new Chunk
            {
                pos = dir * cellRadius * 0.4f,
                vel = dir * cellRadius * Random.Range(4f, 6.5f),   // expand outward as a shell (snappier)
                r0 = r0[i],
                radius = r0[i],
                life = cfg.DissolveTime(),
            };

            // Invisible edible carrier (a trigger collider + EdibleEntity the eating system already reads).
            var cgo = new GameObject("ShellChunk");
            cgo.transform.SetParent(transform, false);
            cgo.transform.localPosition = c.pos;
            var rb = cgo.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;                 // we move it by transform; cheap
            var cc = cgo.AddComponent<CircleCollider2D>();
            cc.isTrigger = true;
            cc.radius = Mathf.Max(0.05f, c.radius);
            float mass = areaSum > 0f ? cytoMass * (r0[i] * r0[i] / areaSum) : 0f;
            var e = cgo.AddComponent<EdibleEntity>();
            e.biomassValue = mass;
            e.matterValue = mass * cfg.matterPerMass;

            c.go = cgo; c.col = cc;
            chunks[i] = c;
        }
        Push();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        var fm = FlowFieldManager.Instance;
        Vector2 centerW = transform.position;
        int alive = 0;

        for (int i = 0; i < count; i++)
        {
            var c = chunks[i];
            if (c.dead) continue;
            if (c.go == null) { c.dead = true; continue; }   // eaten (EdibleEntity destroyed the carrier)

            c.age += dt;
            c.vel *= Mathf.Max(0f, 1f - 2.2f * dt);          // lighter drag → carries out faster/further
            if (fm != null) c.vel += fm.SampleFlowAtPosition(centerW + c.pos) * 2f * dt;   // then drifts
            c.pos += c.vel * dt;

            float t = Mathf.Clamp01(c.age / c.life);
            c.radius = c.r0 * Mathf.Lerp(1f, 0.06f, t);      // thins steadily throughout → visibly dissolves away

            if (c.age >= c.life)
            {
                CellFx.DustPoof(centerW + c.pos, c.r0 * 0.6f, color);   // poof, don't fade
                Destroy(c.go); c.go = null; c.dead = true;
                continue;
            }

            c.go.transform.localPosition = c.pos;
            c.col.radius = Mathf.Max(0.05f, c.radius);
            alive++;
        }

        Push();
        if (alive == 0) { if (mat != null) Destroy(mat); Destroy(gameObject); }
    }

    void Push()
    {
        if (mat == null) return;
        int n = 0;
        for (int i = 0; i < count && n < Max; i++)
        {
            var c = chunks[i];
            if (c.dead) continue;
            data[n++] = new Vector4(0.5f + c.pos.x / worldSize, 0.5f + c.pos.y / worldSize, c.radius / worldSize, 0f);
        }
        mat.SetVectorArray("_Blobs", data);
        mat.SetInt("_BlobCount", n);
    }

    void OnDestroy() { if (mat != null) Destroy(mat); }
}
