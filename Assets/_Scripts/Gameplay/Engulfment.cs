using UnityEngine;

/// <summary>
/// Lets a cell engulf nearby food and smaller/softer cells. Uses an overlap query around the
/// membrane each physics tick. Eating a cell is gated by EngulfMath (size × rigidity).
/// Attach to any cell (player or AI) — finds its own ICell + CellResources.
/// </summary>
public class Engulfment : MonoBehaviour
{
    [Tooltip("Target's CENTRE must be within this fraction of our membrane radius to be swallowed.")]
    [Range(0.4f, 1.2f)] public float swallowFactor = 0.95f;
    [Tooltip("Biomass gained as a fraction of an engulfed cell's mass. Eating a whole cell should pay off.")]
    public float cellBiomassYield = 0.4f;
    [Tooltip("Matter gained as a fraction of an engulfed cell's mass.")]
    public float cellMatterYield = 0.35f;

    ICell self;
    CellResources res;
    readonly Collider2D[] hits = new Collider2D[24];

    void Awake()
    {
        self = GetComponent<ICell>();
        res  = GetComponent<CellResources>();
    }

    void FixedUpdate()
    {
        if (self == null || !self.IsAlive) return;
        // Detect a bit beyond our membrane so we catch cells/food touching us.
        float reach = self.Radius * 1.25f + 0.3f;
        int n = Physics2D.OverlapCircleNonAlloc(transform.position, reach, hits);
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h == null || h.transform == transform) continue;

            // Food — always edible
            var food = h.GetComponent<EdibleEntity>();
            if (food != null && !food.Eaten) { food.Consume(res, self); continue; }

            // Another cell — engulf if the math allows and it's well inside our membrane
            var other = h.GetComponent<ICell>() ?? h.GetComponentInParent<ICell>();
            if (other == null || other == self || !other.IsAlive) continue;

            if (EngulfMath.CanEngulf(self, other))
            {
                float d = Vector2.Distance(transform.position, other.Transform.position);
                // Overlap-based: swallow once the prey's centre is inside our membrane.
                if (d <= self.Radius * Mathf.Max(swallowFactor, 0.95f))
                {
                    // Kill the prey FIRST (guaranteed), then grow — otherwise if growing tips us over
                    // our own pop threshold mid-eat, the prey could be left alive.
                    float preyMass = other.Mass;
                    other.Consumed(self);
                    self.GainBiomass(preyMass * cellBiomassYield);
                    if (res != null) res.AddMatter(preyMass * cellMatterYield);
                }
            }
        }
    }
}
