using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Populates the pool with AI cells that are FULL soft-body cells — instances of the same cell
/// prefab as the player (same membrane/bioslime shader, same ICell engulf rules) — but driven by
/// AIController instead of player input. So they look like real cells and can be eaten.
///
/// Assign `cellPrefab` to a prefab of the player cell (PlayerCell). Player-only components are
/// stripped from each clone at spawn.
/// </summary>
public class AICellSpawner : MonoBehaviour
{
    [Header("Prefab (assign a PlayerCell prefab)")]
    public GameObject cellPrefab;

    [Header("Population")]
    public int   targetCount = 6;
    public float spawnRadius   = 18f;
    public float despawnRadius = 45f;
    public float respawnDelay  = 2f;

    [Header("Evolution / Variety")]
    [Tooltip("Species pool to spawn from. Leave empty to use the built-in CellVariety.Library.")]
    public CellSpecies[] species;
    [Tooltip("Seconds between evolutionary generations — later gens spawn bigger, tougher, smarter.")]
    public float secondsPerGeneration = 30f;

    Transform cam;
    readonly List<CellBiomass> live = new();
    float respawnTimer;
    int generation;
    float genTimer;

    void Start() { cam = Camera.main ? Camera.main.transform : null; }

    void Update()
    {
        if (cellPrefab == null) return;
        if (!cam) { cam = Camera.main ? Camera.main.transform : null; if (!cam) return; }
        Vector2 c = cam.position;

        // Advance the evolutionary generation over time.
        genTimer += Time.deltaTime;
        if (genTimer >= Mathf.Max(secondsPerGeneration, 1f)) { genTimer = 0f; generation++; }

        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (live[i] == null) { live.RemoveAt(i); continue; }
            if (Vector2.Distance(live[i].transform.position, c) > despawnRadius)
            { Destroy(live[i].gameObject); live.RemoveAt(i); }
        }

        if (live.Count < targetCount)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f) { Spawn(c); respawnTimer = respawnDelay; }
        }
    }

    void Spawn(Vector2 around)
    {
        Vector2 pos = around + Random.insideUnitCircle.normalized * Random.Range(spawnRadius * 0.6f, spawnRadius);

        var clone = Instantiate(cellPrefab, pos, Quaternion.identity, transform);

        // Strip player-only behaviour (Destroy(null) is safe if a component is absent)
        Destroy(clone.GetComponent<JellyMovement>());
        Destroy(clone.GetComponent<BehaviorObserver>());
        Destroy(clone.GetComponent<EvolutionNodes>());
        Destroy(clone.GetComponent<CellRippleSystem>());

        // Pick a species and apply it (geometry/size/rigidity/colour) BEFORE JellyBodyBuilder.Start()
        // runs this frame, scaled by the current evolutionary generation.
        var sp = CellVariety.Pick(species != null && species.Length > 0 ? species : null);
        CellVariety.Apply(clone, sp, generation);
        clone.name = $"AICell·{sp.name}·Gen{generation}";

        var bio = clone.GetComponent<CellBiomass>();
        if (bio != null)
        {
            bio.debugGrowKeys = false;          // don't react to the player's debug keys
            bio.destroyOnConsumed = true;       // eaten → gone (not respawn)
            bio.onConsumed += OnCellDied;       // (mass/rigidity already set by CellVariety.Apply)
            live.Add(bio);
        }

        if (clone.GetComponent<CellResources>() == null) clone.AddComponent<CellResources>();
        if (clone.GetComponent<Engulfment>()    == null) clone.AddComponent<Engulfment>();
        var ai = clone.GetComponent<AIController>() ?? clone.AddComponent<AIController>();
        ai.intelligence = 1f + generation * 0.15f;   // later generations are smarter & perceive farther
    }

    void OnCellDied(CellBiomass c) { live.Remove(c); }
}
