using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Spawns one or more STATIONARY soft-body cells you can practice on — swim into a small one to eat
/// it, swim into a big one to get eaten — so the engulf/pop FX can be tested deterministically
/// instead of waiting for wandering AI. Each test cell respawns a moment after it dies, so you can
/// retry as many times as you like.
///
/// Setup: drop this on an empty GameObject in the scene, assign the same PlayerCell prefab you give
/// AICellSpawner, position the GameObject where you want the test cells, and press Play.
///   • A SMALL cell (mass below yours) → you eat it   → engulf FX.
///   • A LARGE, rigid cell             → it eats you  → you respawn small.
///   • Press <forcePopKey> (default P) → force the nearest test cell to POP, to preview that FX.
/// </summary>
public class StationaryTestCell : MonoBehaviour
{
    [System.Serializable]
    public struct Spec
    {
        public Vector2 localOffset;   // position relative to this GameObject
        public float   mass;          // small = edible by you; large = can eat you
        [Range(0f, 1f)] public float rigidity;
    }

    [Header("Prefab (assign the PlayerCell prefab)")]
    public GameObject cellPrefab;

    [Header("Test cells to place")]
    public Spec[] cells = new Spec[]
    {
        new Spec { localOffset = new Vector2(-4f, 0f), mass = 0.6f, rigidity = 0.2f }, // tiny prey
        new Spec { localOffset = new Vector2( 4f, 0f), mass = 8f,   rigidity = 0.8f }, // big predator
    };

    [Header("Behaviour")]
    [Tooltip("Seconds after a test cell dies before it respawns in place.")]
    public float respawnDelay = 1.5f;
    [Tooltip("Freeze the cell so it never drifts away while you line up your approach.")]
    public bool freezeInPlace = true;

    [Header("Debug")]
    [Tooltip("Force the nearest test cell to pop — instant preview of the rupture FX.")]
    public KeyCode forcePopKey = KeyCode.P;

    readonly List<CellBiomass> live = new();
    readonly List<float> respawnTimers = new();

    void Start()
    {
        if (cellPrefab == null) { Debug.LogWarning("[StationaryTestCell] No cellPrefab assigned."); return; }
        for (int i = 0; i < cells.Length; i++) { live.Add(null); respawnTimers.Add(0f); Spawn(i); }
    }

    void Update()
    {
        if (cellPrefab == null) return;

        for (int i = 0; i < cells.Length; i++)
        {
            if (live[i] == null)
            {
                respawnTimers[i] -= Time.deltaTime;
                if (respawnTimers[i] <= 0f) Spawn(i);
            }
        }

        if (Input.GetKeyDown(forcePopKey)) ForcePopNearest();
    }

    void Spawn(int i)
    {
        Vector3 pos = transform.position + (Vector3)cells[i].localOffset;
        var clone = Instantiate(cellPrefab, pos, Quaternion.identity, transform);
        clone.name = $"TestCell_{i}";

        // Strip player-only / AI behaviour so it just sits there as a soft body.
        Destroy(clone.GetComponent<JellyMovement>());
        Destroy(clone.GetComponent<BehaviorObserver>());
        Destroy(clone.GetComponent<EvolutionNodes>());
        Destroy(clone.GetComponent<CellRippleSystem>());
        Destroy(clone.GetComponent<AIController>()); // never chase — stay put

        var bio = clone.GetComponent<CellBiomass>();
        if (bio != null)
        {
            bio.debugGrowKeys = false;
            bio.destroyOnConsumed = true;        // eaten → gone, then we respawn it
            bio.membraneRigidity = cells[i].rigidity;
            bio.SetMass(cells[i].mass);
            int idx = i;
            bio.onConsumed += _ => OnDied(idx);
            live[i] = bio;
        }

        // It needs to be able to eat the player too (so "being eaten" is testable).
        if (clone.GetComponent<CellResources>() == null) clone.AddComponent<CellResources>();
        if (clone.GetComponent<Engulfment>()    == null) clone.AddComponent<Engulfment>();

        if (freezeInPlace)
        {
            var rb = clone.GetComponent<Rigidbody2D>();
            if (rb != null) rb.constraints = RigidbodyConstraints2D.FreezeAll; // centre stays; membrane still wobbles
        }
    }

    void OnDied(int i)
    {
        live[i] = null;
        respawnTimers[i] = respawnDelay;
    }

    void ForcePopNearest()
    {
        var player = Camera.main ? Camera.main.transform.position : transform.position;
        CellBiomass nearest = null;
        float best = float.MaxValue;
        foreach (var c in live)
        {
            if (c == null) continue;
            float d = Vector2.Distance(c.transform.position, player);
            if (d < best) { best = d; nearest = c; }
        }
        if (nearest != null)
        {
            // Push it past its max mass so CheckPop() fires the rupture. The cell runs its own
            // rupture-and-die sequence and invokes onConsumed at the end, which schedules its respawn.
            nearest.SetMass(nearest.MaxMass + 0.01f);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        if (cells == null) return;
        foreach (var s in cells)
            Gizmos.DrawWireSphere(transform.position + (Vector3)s.localOffset, Mathf.Sqrt(Mathf.Max(s.mass, 0.05f)) * 0.5f);
    }
}
