using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Keeps the water stocked with edible motes around the camera. Self-contained: builds its own
/// sprites, pools, and recycles eaten morsels. Add to an empty GameObject — no wiring needed.
/// </summary>
public class FoodSpawner : MonoBehaviour
{
    [Header("Population")]
    public int   maxCount   = 18;
    public float spawnRadius = 30f;   // around the camera
    public float despawnRadius = 45f; // recycle motes that drift too far
    public float spawnPerSecond = 3f;

    [Header("Morsel")]
    public float minSize = 0.25f, maxSize = 0.5f;
    public Color color = new Color(0.7f, 0.95f, 0.55f, 0.9f);
    [Tooltip("Matter (building blocks) per morsel — fuels node growth.")]
    public float matterValue = 0.5f;
    [Tooltip("Biomass (size) per morsel — kept small so you grow gradually, not instantly.")]
    public float biomassValue = 0.003f;

    Transform cam;
    readonly List<EdibleEntity> live = new();
    readonly Stack<EdibleEntity> pool = new();
    float spawnAccum;

    void Start()
    {
        cam = Camera.main ? Camera.main.transform : null;
    }

    void Update()
    {
        if (!cam) { cam = Camera.main ? Camera.main.transform : null; if (!cam) return; }
        Vector2 c = cam.position;

        // Recycle motes that drifted out of range
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var e = live[i];
            if (e == null) { live.RemoveAt(i); continue; }
            if (Vector2.Distance(e.transform.position, c) > despawnRadius) Recycle(e);
        }

        // Spawn up to the cap
        spawnAccum += spawnPerSecond * Time.deltaTime;
        while (spawnAccum >= 1f && live.Count < maxCount)
        {
            spawnAccum -= 1f;
            SpawnOne(c);
        }
    }

    void SpawnOne(Vector2 around)
    {
        Vector2 pos = around + Random.insideUnitCircle * spawnRadius;
        EdibleEntity e = pool.Count > 0 ? pool.Pop() : Create();
        e.transform.position = pos;
        e.transform.localScale = Vector3.one * Random.Range(minSize, maxSize);
        e.matterValue = matterValue;
        e.biomassValue = biomassValue;
        e.ResetEaten();
        e.gameObject.SetActive(true);
        live.Add(e);
    }

    EdibleEntity Create()
    {
        var go = new GameObject("Food");
        go.transform.SetParent(transform);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GfxUtil.Circle();
        sr.sharedMaterial = GfxUtil.SpriteMaterial();
        sr.color = color;
        sr.sortingOrder = -40;
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.5f;
        var e = go.AddComponent<EdibleEntity>();
        e.pool = this;
        return e;
    }

    public void Recycle(EdibleEntity e)
    {
        if (e == null) return;
        live.Remove(e);
        e.gameObject.SetActive(false);
        pool.Push(e);
    }
}
