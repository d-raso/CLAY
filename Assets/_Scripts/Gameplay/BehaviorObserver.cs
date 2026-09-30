using UnityEngine;

/// <summary>
/// Watches HOW the player lives and turns it into adaptive pressure (CellStage_Evolution.md §2②).
/// For this slice we track LOCOMOTION pressure → flagellum potential: moving fast, amplified by
/// strong currents and nearby predators, and reinforced by successful escapes. Also logs simple
/// outcomes (deaths, escapes, peak mass) so we can see whether a behaviour is paying off.
/// Attach to the player cell.
/// </summary>
public class BehaviorObserver : MonoBehaviour
{
    [Header("Live readout (flagellum domain)")]
    [SerializeField] float locomotionPressure;
    [SerializeField] float currentExposure;
    [SerializeField] int   nearbyPredators;
    [SerializeField, Range(0,1)] float flagellaPotential;

    [Header("Outcomes")]
    [SerializeField] int   deaths;
    [SerializeField] int   escapes;
    [SerializeField] float peakMass;
    [SerializeField] float distanceTravelled;

    [Header("Tuning")]
    public float pressureToUnlock = 25f;
    public float dangerRadius = 6f;
    public float currentWeight = 0.4f;
    public float predatorWeight = 0.6f;
    public float escapeReward = 2f;

    public float FlagellaPotential => flagellaPotential;
    public bool  FlagellaUnlocked  => flagellaPotential >= 1f;

    Rigidbody2D rb;
    CellBiomass bio;
    JellyMovement move;
    ICell self;
    readonly Collider2D[] hits = new Collider2D[24];
    Vector2 lastPos;
    bool wasInDanger;
    float lastMass = 1f, logTimer;

    void Start()
    {
        rb   = GetComponent<Rigidbody2D>();
        bio  = GetComponent<CellBiomass>();
        move = GetComponent<JellyMovement>();
        self = GetComponent<ICell>();
        lastPos = transform.position;
        if (bio != null) { lastMass = bio.Mass; peakMass = bio.Mass; }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector2 pos = transform.position;

        distanceTravelled += Vector2.Distance(pos, lastPos);
        lastPos = pos;

        float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
        float maxS  = move != null ? move.maxSpeed : 10f;
        float speedNorm = Mathf.Clamp01(speed / Mathf.Max(maxS, 0.1f));

        currentExposure = FlowFieldManager.Instance != null
            ? FlowFieldManager.Instance.SampleFlowAtPosition(pos).magnitude : 0f;

        // Count predators (cells that could engulf me) nearby
        nearbyPredators = 0;
        bool inDanger = false;
        int n = Physics2D.OverlapCircleNonAlloc(pos, dangerRadius, hits);
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h == null || h.transform == transform) continue;
            var other = h.GetComponent<ICell>() ?? h.GetComponentInParent<ICell>();
            if (other == null || other == self || !other.IsAlive) continue;
            if (EngulfMath.CanEngulf(other, self)) { nearbyPredators++; inDanger = true; }
        }

        // Pressure: moving fast matters more amid currents and predators (your idea).
        float boost = 1f + currentExposure * currentWeight + nearbyPredators * predatorWeight;
        locomotionPressure += speedNorm * dt * boost;

        // Surviving a near-death flight reinforces the adaptation.
        if (wasInDanger && !inDanger && (self == null || self.IsAlive))
        {
            escapes++;
            locomotionPressure += escapeReward;
        }
        wasInDanger = inDanger;

        flagellaPotential = Mathf.Clamp01(locomotionPressure / Mathf.Max(pressureToUnlock, 0.1f));

        // Outcome bookkeeping
        if (bio != null)
        {
            if (bio.Mass < lastMass * 0.5f && lastMass > 2f) deaths++; // big sudden drop = died/popped
            lastMass = bio.Mass;
            peakMass = Mathf.Max(peakMass, bio.Mass);
        }

        logTimer -= dt;
        if (logTimer <= 0f)
        {
            logTimer = 10f;
            Debug.Log($"[Behaviour] flagellaPotential {flagellaPotential:F2} | predators {nearbyPredators} | " +
                      $"current {currentExposure:F1} | escapes {escapes} | deaths {deaths} | peakMass {peakMass:F1}");
        }
    }
}
