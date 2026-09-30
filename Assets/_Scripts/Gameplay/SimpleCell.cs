using UnityEngine;

/// <summary>
/// A lightweight (non-soft-body) cell for AI critters. Implements ICell so it shares the
/// engulfment rules with the player. Grows when it eats; dies when engulfed.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class SimpleCell : MonoBehaviour, ICell
{
    public float mass = 1f;
    [Range(0f, 1f)] public float rigidity = 0.3f;
    public float baseRadius = 0.6f;

    Rigidbody2D rb;
    SpriteRenderer sr;
    CircleCollider2D col;
    bool alive = true;

    public System.Action<SimpleCell> OnDeath;

    public Transform Transform => transform;
    public float Mass        => mass;
    public float Radius      => baseRadius * Mathf.Sqrt(Mathf.Max(mass, 0.05f));
    public float Rigidity    => rigidity;
    public float EngulfPower => Mathf.Lerp(1f, 0.4f, rigidity);
    public bool  IsAlive     => alive;
    public Rigidbody2D Body  => rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.linearDamping = 1.5f;

        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = GfxUtil.Circle();
        sr.sharedMaterial = GfxUtil.SpriteMaterial();
        sr.sortingOrder = -30;

        col = GetComponent<CircleCollider2D>();
        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
        col.radius = 0.5f; // unit sprite; transform scale sets world size
    }

    public void Init(float startMass, float rig, Color color)
    {
        mass = startMass;
        rigidity = rig;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        sr.color = color;
        alive = true;
        ApplyVisual();
    }

    public void GainBiomass(float amount)
    {
        mass = Mathf.Max(0.05f, mass + amount);
        ApplyVisual();
    }

    public void Consumed(ICell by)
    {
        if (!alive) return;
        alive = false;
        OnDeath?.Invoke(this);
        // SimpleCell has no JellyMesh/membrane, so CellFx no-ops; left for parity with soft cells.
        CellFx.Engulf(gameObject, transform.position, sr != null ? sr.color : Color.white, by?.Transform);
        Destroy(gameObject);
    }

    void ApplyVisual()
    {
        float diameter = Radius * 2f;
        transform.localScale = Vector3.one * diameter;
    }
}
