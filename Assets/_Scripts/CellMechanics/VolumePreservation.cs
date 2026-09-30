using UnityEngine;

/// <summary>
/// Preserves each cell's SPAWNED silhouette (however asymmetric) and its area as it grows — instead
/// of forcing everything back to a circle. At spawn it records every node's offset from the centre;
/// each tick it pulls nodes toward those offsets, scaled to the current target area. So a lumpy or
/// lopsided cell stays lumpy and lopsided, and its volume is that shape's area. A light neighbour
/// separation keeps the membrane from pinching, but nothing rounds the shape.
/// </summary>
public class VolumePreservation : MonoBehaviour
{
    [Header("Shape holding")]
    [Tooltip("How firmly nodes return to the spawned silhouette. Higher = holds shape better / stiffer; lower = wobblier.")]
    public float shapeStiffness = 35f;
    [Tooltip("Damping on node velocity so the membrane settles instead of jittering.")]
    public float damping = 0.6f;

    [Header("Anti-pinch safety")]
    public float minNodeDistance = 0.22f;
    public float separationForce = 60f;

    // Runtime
    private Transform[] nodes;
    private Rigidbody2D[] nodeRBs;
    private Rigidbody2D centerRB;
    private Vector2[] restLocal;   // node offset from centre at spawn — defines the (arbitrary) silhouette
    private float restArea;
    private float targetArea;
    private int nodeCount;

    /// <summary>Call after JellyBodyBuilder creates the nodes (with the cell in its rest shape).</summary>
    public void Initialize(Transform[] membraneNodes)
    {
        nodes = membraneNodes;
        nodeCount = nodes.Length;
        centerRB = GetComponent<Rigidbody2D>();

        nodeRBs = new Rigidbody2D[nodeCount];
        restLocal = new Vector2[nodeCount];
        Vector2 c = centerRB.position;
        for (int i = 0; i < nodeCount; i++)
        {
            nodeRBs[i] = nodes[i].GetComponent<Rigidbody2D>();
            restLocal[i] = (Vector2)nodes[i].position - c;   // the spawned, possibly-asymmetric shape
        }

        restArea = CalculatePolygonArea();
        targetArea = restArea;
    }

    /// <summary>The area the cell currently holds (scales the recorded silhouette).</summary>
    public float TargetArea => targetArea;

    /// <summary>Set the target area (e.g. when biomass grows). The silhouette scales to match.</summary>
    public void SetTargetArea(float area) => targetArea = Mathf.Max(0.01f, area);

    void FixedUpdate()
    {
        if (nodes == null || nodeCount == 0 || restLocal == null || centerRB == null) return;

        Vector2 center = centerRB.position;
        float scale = Mathf.Sqrt(Mathf.Max(targetArea / Mathf.Max(restArea, 0.0001f), 0.0001f));

        // Pull every node toward its (scaled) spawned position — preserves the exact silhouette.
        for (int i = 0; i < nodeCount; i++)
        {
            if (nodeRBs[i] == null) continue;
            Vector2 desired = center + restLocal[i] * scale;
            Vector2 pos = nodeRBs[i].position;
            Vector2 force = (desired - pos) * shapeStiffness - nodeRBs[i].linearVelocity * damping;
            nodeRBs[i].AddForce(force);
        }

        EnforceMinimumNodeDistance();
    }

    /// <summary>Gentle push apart only for adjacent nodes that get too close — prevents pinching,
    /// without the old non-adjacent checks that flattened concavities into a circle.</summary>
    void EnforceMinimumNodeDistance()
    {
        for (int i = 0; i < nodeCount; i++)
        {
            int j = (i + 1) % nodeCount;
            if (nodeRBs[i] == null || nodeRBs[j] == null) continue;
            Vector2 a = nodeRBs[i].position, b = nodeRBs[j].position;
            float d = Vector2.Distance(a, b);
            if (d < minNodeDistance && d > 0.001f)
            {
                Vector2 dir = (b - a) / d;
                float f = (minNodeDistance - d) * separationForce;
                nodeRBs[i].AddForce(-dir * f);
                nodeRBs[j].AddForce(dir * f);
            }
        }
    }

    float CalculatePolygonArea()
    {
        float area = 0f;
        Vector2 c = centerRB.position;
        for (int i = 0; i < nodeCount; i++)
        {
            Vector2 cur = nodes[i].position;
            Vector2 nxt = nodes[(i + 1) % nodeCount].position;
            area += (cur.x - c.x) * (nxt.y - c.y) - (nxt.x - c.x) * (cur.y - c.y);
        }
        return Mathf.Abs(area) * 0.5f;
    }
}
