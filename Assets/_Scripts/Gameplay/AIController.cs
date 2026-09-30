using UnityEngine;

/// <summary>
/// Steering brain for an AI cell. Perceives prey, predators and food within a sense radius that grows
/// with its size and (evolutionary) intelligence, then acts strategically:
///   • flee predators early, harder the closer they are (and away from several at once),
///   • intercept prey by leading their movement (not just chasing their current spot),
///   • otherwise go after the nearest food,
///   • otherwise wander.
/// It commits to a hunt target briefly so it doesn't dither between options. Eating is done by the
/// Engulfment component via overlap.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class AIController : MonoBehaviour
{
    [Header("Perception")]
    [Tooltip("Base sight radius. Effective range also grows with cell size and intelligence.")]
    public float baseSenseRadius = 9f;
    [Tooltip("Set by the spawner per generation (>1 = smarter: sees farther, reacts sooner).")]
    public float intelligence = 1f;

    [Header("Movement")]
    public float accel = 16f;
    public float maxSpeed = 5.5f;

    ICell cell;
    Rigidbody2D rb;
    readonly Collider2D[] hits = new Collider2D[32];

    Vector2 wanderDir;
    float wanderTimer;
    ICell huntTarget;       // committed prey, to avoid flip-flopping
    float commitTimer;

    void Awake()
    {
        cell = GetComponent<ICell>();
        rb = GetComponent<Rigidbody2D>();
        wanderDir = Random.insideUnitCircle.normalized;
    }

    float SenseRadius => baseSenseRadius * Mathf.Max(intelligence, 0.5f) + cell.Radius * 2f;

    void FixedUpdate()
    {
        if (cell == null || !cell.IsAlive) return;

        Vector2 pos = transform.position;
        float sense = SenseRadius;
        float mySpeed = Mathf.Max(rb.linearVelocity.magnitude, 1f);

        // ── Survey surroundings ──
        Vector2 threatPush = Vector2.zero;   // summed flee vector, weighted by closeness
        int threatCount = 0;
        ICell bestPrey = null; float bestPreyScore = float.MinValue;
        Vector2 foodPos = pos; float foodD = float.MaxValue; bool haveFood = false;

        int n = Physics2D.OverlapCircleNonAlloc(pos, sense, hits);
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h == null || h.transform == transform) continue;

            var food = h.GetComponent<EdibleEntity>();
            if (food != null && !food.Eaten)
            {
                float fd = Vector2.Distance(pos, h.transform.position);
                if (fd < foodD) { foodD = fd; foodPos = h.transform.position; haveFood = true; }
                continue;
            }

            var other = h.GetComponent<ICell>() ?? h.GetComponentInParent<ICell>();
            if (other == null || other == cell || !other.IsAlive) continue;
            float dist = Vector2.Distance(pos, other.Transform.position);

            if (EngulfMath.CanEngulf(other, cell))            // a predator
            {
                float w = 1f - Mathf.Clamp01(dist / sense);   // closer = stronger pull
                threatPush += ((Vector2)transform.position - (Vector2)other.Transform.position).normalized * (w * w);
                threatCount++;
            }
            else if (EngulfMath.CanEngulf(cell, other))       // prey — score by closeness & juiciness
            {
                float score = other.Mass * 0.5f - dist;       // bigger, nearer prey preferred
                if (score > bestPreyScore) { bestPreyScore = score; bestPrey = other; }
            }
        }

        // ── Decide ──
        Vector2 dir;
        if (threatCount > 0)                                  // 1) survive — flee (overrides all)
        {
            dir = threatPush.normalized;
            huntTarget = null;
        }
        else
        {
            // Keep chasing the committed target if it's still valid & edible.
            commitTimer -= Time.fixedDeltaTime;
            // Unity-aware destroyed check FIRST: a plain `!= null` won't catch a destroyed object,
            // and touching .IsAlive on it would throw MissingReferenceException.
            if (huntTarget != null && (huntTarget as Object) == null) huntTarget = null;
            if (huntTarget != null && (!huntTarget.IsAlive || !EngulfMath.CanEngulf(cell, huntTarget)))
                huntTarget = null;
            if (huntTarget == null && bestPrey != null) { huntTarget = bestPrey; commitTimer = 1.0f; }
            else if (commitTimer <= 0f && bestPrey != null) { huntTarget = bestPrey; commitTimer = 1.0f; }

            if (huntTarget != null)                           // 2) hunt — intercept (lead the target)
            {
                Vector2 tp = huntTarget.Transform.position;
                var trb = huntTarget.Transform.GetComponent<Rigidbody2D>();
                if (trb != null)
                {
                    float lead = Vector2.Distance(pos, tp) / mySpeed;   // time to reach
                    tp += trb.linearVelocity * Mathf.Clamp(lead, 0f, 1.5f);
                }
                dir = (tp - pos).normalized;
            }
            else if (haveFood)                                // 3) graze nearest food
                dir = (foodPos - pos).normalized;
            else                                              // 4) wander
            {
                wanderTimer -= Time.fixedDeltaTime;
                if (wanderTimer <= 0f) { wanderDir = Random.insideUnitCircle.normalized; wanderTimer = Random.Range(1.5f, 3.5f); }
                dir = wanderDir;
            }
        }

        // ── Move (bigger = slower; flee a touch faster) ──
        float speedMul = 1f / Mathf.Sqrt(Mathf.Max(cell.Mass, 0.5f));
        float urgency = threatCount > 0 ? 1.35f : 1f;
        rb.AddForce(dir * accel * speedMul * urgency);

        float ms = maxSpeed * speedMul * urgency;
        if (rb.linearVelocity.magnitude > ms) rb.linearVelocity = rb.linearVelocity.normalized * ms;
    }
}
