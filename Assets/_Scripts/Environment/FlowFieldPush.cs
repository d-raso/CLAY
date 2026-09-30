using UnityEngine;

/// <summary>
/// Makes a cell drift with the water current. Each physics tick it samples the
/// <see cref="FlowFieldManager"/> at every rigidbody in the cell's soft body (centre + membrane nodes)
/// and applies a mass-scaled force, so the whole cell gets carried at a size-independent acceleration
/// (a big cell and a small cell drift together, like real water carrying everything). Add to any cell —
/// player or AI. Works alongside the cell's own movement/AI; the current just adds on top.
/// </summary>
public class FlowFieldPush : MonoBehaviour
{
    [Tooltip("How hard the current pushes this cell. Higher = swept along faster.")]
    public float strength = 2.5f;

    Rigidbody2D[] bodies;

    void Start() => Rebuild();

    /// <summary>Re-cache the soft-body rigidbodies (call if the body is rebuilt).</summary>
    public void Rebuild() => bodies = GetComponentsInChildren<Rigidbody2D>(true);

    void FixedUpdate()
    {
        var mgr = FlowFieldManager.Instance;
        if (mgr == null || bodies == null || strength <= 0f) return;

        for (int i = 0; i < bodies.Length; i++)
        {
            var rb = bodies[i];
            if (rb == null) continue;
            Vector2 flow = mgr.SampleFlowAtPosition(rb.position);
            // Mass-scaled → acceleration = flow * strength regardless of cell size.
            rb.AddForce(flow * strength * rb.mass);
        }
    }
}
