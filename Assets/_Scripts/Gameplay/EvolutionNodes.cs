using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// The "node" evolution mechanic (CellStage_Evolution.md §2b). Spend gathered MATTER to grow a
/// blank node on the membrane — you don't yet know what it will become. As your BehaviorObserver
/// accrues adaptive pressure, blank nodes gain organelle POTENTIAL; once unlocked you can develop a
/// node into the organelle (for this slice: a FLAGELLUM, driven by how much you move/flee/ride currents).
///
/// Debug controls (until a real evolution UI exists):
///   N  — grow a blank node (costs matter)
///   F  — develop a blank node into a flagellum (needs flagellum potential + matter)
///   Space — flagellum dash (once developed)
/// Attach to the player cell.
/// </summary>
public class EvolutionNodes : MonoBehaviour
{
    [Header("Costs (matter)")]
    public float nodeCost = 4f;
    public float flagellumCost = 8f;

    [Header("Flagellum")]
    public float flagellumSpeedBoost = 1.5f;
    public float dashForce = 45f;
    public float dashCooldown = 1.2f;

    [Header("Debug keys")]
    public KeyCode addNodeKey = KeyCode.N;
    public KeyCode developKey = KeyCode.F;
    public KeyCode dashKey     = KeyCode.Space;

    class Node { public float angle; public Transform tf; public bool differentiated; public Transform tail; }
    readonly List<Node> nodes = new();

    /// <summary>How many organelle nodes this cell has grown (blank + developed). Used by the pop system
    /// to reduce how much edible cytoplasm a popped cell leaves (organelles lock mass away).</summary>
    public int OrganelleCount => nodes.Count;

    CellResources res;
    CellBiomass bio;
    BehaviorObserver obs;
    JellyMovement move;
    Rigidbody2D rb;
    bool hasFlagellum;
    float dashTimer;

    void Start()
    {
        res  = GetComponent<CellResources>();
        bio  = GetComponent<CellBiomass>();
        obs  = GetComponent<BehaviorObserver>();
        move = GetComponent<JellyMovement>();
        rb   = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        dashTimer -= Time.deltaTime;
        if (Input.GetKeyDown(addNodeKey)) TryAddNode();
        if (Input.GetKeyDown(developKey)) TryDevelopFlagellum();
        if (hasFlagellum && Input.GetKeyDown(dashKey)) Dash();

        // Flagellum tails wiggle
        float t = Time.time;
        foreach (var node in nodes)
            if (node.tail != null)
                node.tail.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 12f + node.angle) * 18f);
    }

    void LateUpdate()
    {
        float r = bio != null ? bio.Radius : 2f;
        Vector3 c = transform.position;
        foreach (var node in nodes)
        {
            if (node.tf == null) continue;
            Vector2 dir = new Vector2(Mathf.Cos(node.angle), Mathf.Sin(node.angle));
            node.tf.position = c + (Vector3)(dir * r);
        }
    }

    void TryAddNode()
    {
        if (res == null || !res.TrySpend(nodeCost))
        {
            Debug.Log($"[Evolution] Not enough matter for a node (need {nodeCost}, have {(res ? res.Matter : 0):F1}).");
            return;
        }
        var node = new Node { angle = Random.Range(0f, Mathf.PI * 2f) };
        var go = new GameObject("Node");
        go.transform.SetParent(transform);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GfxUtil.Circle();
        sr.sharedMaterial = GfxUtil.SpriteMaterial();
        sr.color = new Color(0.15f, 0.15f, 0.2f, 0.95f); // blank node = dark, mysterious
        sr.sortingOrder = 60;
        go.transform.localScale = Vector3.one * 0.35f;
        node.tf = go.transform;
        nodes.Add(node);
        Debug.Log($"[Evolution] Grew a blank node ({nodes.Count} total). What will it become?");
    }

    void TryDevelopFlagellum()
    {
        if (obs == null || !obs.FlagellaUnlocked)
        {
            float p = obs != null ? obs.FlagellaPotential : 0f;
            Debug.Log($"[Evolution] Flagellum potential not ready ({p:P0}). Move more — through currents and danger.");
            return;
        }
        Node blank = nodes.Find(n => !n.differentiated);
        if (blank == null) { Debug.Log("[Evolution] No blank node to develop. Press N to grow one."); return; }
        if (res == null || !res.TrySpend(flagellumCost))
        {
            Debug.Log($"[Evolution] Not enough matter for a flagellum (need {flagellumCost}).");
            return;
        }

        blank.differentiated = true;
        if (blank.tf != null)
        {
            var sr = blank.tf.GetComponent<SpriteRenderer>();
            if (sr) sr.color = new Color(0.85f, 0.95f, 0.6f, 0.95f); // realised = bright

            // Tail
            var tail = new GameObject("FlagellumTail");
            tail.transform.SetParent(blank.tf, false);
            var tsr = tail.AddComponent<SpriteRenderer>();
            tsr.sprite = GfxUtil.Circle();
            tsr.sharedMaterial = GfxUtil.SpriteMaterial();
            tsr.color = new Color(0.8f, 0.9f, 0.55f, 0.8f);
            tsr.sortingOrder = 59;
            tail.transform.localScale = new Vector3(0.4f, 1.6f, 1f);
            tail.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            blank.tail = tail.transform;
        }

        hasFlagellum = true;
        if (move != null) move.externalSpeedMultiplier = flagellumSpeedBoost;
        Debug.Log("[Evolution] A node became a FLAGELLUM! You move faster now. (Space to dash.)");
    }

    void Dash()
    {
        if (dashTimer > 0f || rb == null) return;
        dashTimer = dashCooldown;
        Vector2 dir = rb.linearVelocity.sqrMagnitude > 0.1f
            ? rb.linearVelocity.normalized
            : Random.insideUnitCircle.normalized;
        rb.AddForce(dir * dashForce, ForceMode2D.Impulse);
    }
}
