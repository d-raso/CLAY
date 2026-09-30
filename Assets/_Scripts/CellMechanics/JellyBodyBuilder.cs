using UnityEngine;
using System.Collections.Generic; // Required for Lists

public class JellyBodyBuilder : MonoBehaviour
{
    [Header("Settings")]
    public int nodeCount = 12;
    public float radius = 2f;
    public float stiffness = 5f;
    public float dampening = 0.5f;

    public enum CellShape { Round, Oval, Star, Pear, Organic }
    [Header("Shape")]
    [Tooltip("Rest silhouette. Organic = a unique random asymmetric blob per cell (recommended).")]
    public CellShape shape = CellShape.Round;
    [Range(0f, 0.6f)] public float shapeStrength = 0.35f;
    [Tooltip("Number of lobes for the Star shape.")]
    [Range(3, 8)] public int shapeLobes = 5;

    float shapePhase;
    float[] harmAmp, harmPhase, harmFreq;   // random harmonics for the Organic silhouette

    void PrepShape()
    {
        shapePhase = Random.Range(0f, Mathf.PI * 2f);
        if (shape != CellShape.Organic) return;

        // A random handful of harmonics (freq 1..h) with random amplitude/phase → a unique
        // silhouette per cell: freq 1 = lopsided/off-centre, higher = lobes. The random harmonic
        // count and amplitude falloff make some cells gently ovoid, others wildly lobed.
        int h = Random.Range(3, 7);                      // 3..6 harmonics — varies per cell
        float falloff = Random.Range(0.6f, 1.3f);        // how fast higher frequencies fade
        harmAmp = new float[h]; harmPhase = new float[h]; harmFreq = new float[h];
        for (int k = 0; k < h; k++)
        {
            harmFreq[k]  = k + 1;
            harmAmp[k]   = Random.Range(0.25f, 1f) / Mathf.Pow(k + 1, falloff);
            harmPhase[k] = Random.Range(0f, Mathf.PI * 2f);
        }

        // Normalise by the ACTUAL peak of the summed wave so shapeStrength = true max radius
        // deviation (otherwise phase cancellation leaves the deformation tiny and it looks round).
        float peak = 0.001f;
        for (int a = 0; a < 360; a += 4)
        {
            float t = a * Mathf.Deg2Rad, s = 0f;
            for (int k = 0; k < h; k++) s += harmAmp[k] * Mathf.Sin(harmFreq[k] * t + harmPhase[k]);
            peak = Mathf.Max(peak, Mathf.Abs(s));
        }
        for (int k = 0; k < h; k++) harmAmp[k] /= peak;
    }

    /// <summary>Per-angle radius multiplier defining the cell's rest silhouette.</summary>
    float ShapeRadius(float angleDeg)
    {
        float t = angleDeg * Mathf.Deg2Rad;
        switch (shape)
        {
            case CellShape.Oval: return 1f + shapeStrength * Mathf.Cos(2f * t);
            case CellShape.Star: return 1f + shapeStrength * Mathf.Cos(shapeLobes * t);
            case CellShape.Pear: return 1f + shapeStrength * 0.8f * Mathf.Cos(t + 0.6f);
            case CellShape.Organic:
                float s = 0f;
                if (harmAmp != null)
                    for (int k = 0; k < harmAmp.Length; k++)
                        s += harmAmp[k] * Mathf.Sin(harmFreq[k] * t + harmPhase[k]);
                return 1f + shapeStrength * s;
            default: return 1f;
        }
    }

    [Header("Collision")]
    [Tooltip("Radius of the center collider as a fraction of membrane radius. Controls where cells physically collide.")]
    [Range(0.5f, 0.95f)]
    public float collisionRadiusFraction = 0.87f;

    [Header("References")]
    public GameObject nodePrefab;
    public PhysicsMaterial2D slipperyMat;

    private void Start()
    {
        GenerateBody();
    }

    void GenerateBody()
    {
        Rigidbody2D centerRB = GetComponent<Rigidbody2D>();
        List<Transform> createdNodes = new List<Transform>();

        // === CREATE CENTER COLLIDER ===
        // This defines the physical collision boundary of the cell
        CircleCollider2D centerCollider = GetComponent<CircleCollider2D>();
        if (centerCollider == null)
        {
            centerCollider = gameObject.AddComponent<CircleCollider2D>();
        }
        centerCollider.radius = radius * collisionRadiusFraction;

        // Apply physics material if we have one
        if (slipperyMat != null)
        {
            centerCollider.sharedMaterial = slipperyMat;
        }

        PrepShape();
        for (int i = 0; i < nodeCount; i++)
        {
            float angle = i * (360f / nodeCount);
            float r = radius * ShapeRadius(angle);   // per-node radius gives non-round silhouettes
            Vector3 pos = GetVectorFromAngle(angle) * r;

            // Spawn node relative to player center
            GameObject node = Instantiate(nodePrefab, transform.position + pos, Quaternion.identity, transform);
            createdNodes.Add(node.transform);

            // Add Physics Material
            Rigidbody2D nodeRB = node.GetComponent<Rigidbody2D>();
            if (slipperyMat != null) nodeRB.sharedMaterial = slipperyMat;

            // 1. Spring to Center (The Spokes) - holds this node at its shaped rest radius
            SpringJoint2D jointToCenter = node.AddComponent<SpringJoint2D>();
            jointToCenter.connectedBody = centerRB;
            jointToCenter.frequency = stiffness;
            jointToCenter.dampingRatio = dampening;
            jointToCenter.autoConfigureDistance = false;
            jointToCenter.distance = r;

            // 2. Spring to Neighbor (The Rim) - Keeps the skin tight
            if (i > 0)
            {
                SpringJoint2D jointToNeighbor = node.AddComponent<SpringJoint2D>();
                jointToNeighbor.connectedBody = createdNodes[i - 1].GetComponent<Rigidbody2D>();
                jointToNeighbor.frequency = stiffness;
                jointToNeighbor.dampingRatio = dampening;
                jointToNeighbor.autoConfigureDistance = true;
            }
        }

        // 3. Close the Loop (Connect Last Node to First Node)
        if (createdNodes.Count > 1)
        {
            GameObject lastNode = createdNodes[createdNodes.Count - 1].gameObject;
            GameObject firstNode = createdNodes[0].gameObject;

            SpringJoint2D closeLoop = lastNode.AddComponent<SpringJoint2D>();
            closeLoop.connectedBody = firstNode.GetComponent<Rigidbody2D>();
            closeLoop.frequency = stiffness;
            closeLoop.dampingRatio = dampening;
            closeLoop.autoConfigureDistance = true;
        }

        // === INITIALIZE VISUALS ===

        // 1. Tell the Line Renderer (On THIS object)
        MembraneRender lineRend = GetComponent<MembraneRender>();
        if (lineRend != null)
        {
            lineRend.Initialize(createdNodes.ToArray());
        }

        // 2. Tell the Mesh Renderer (On the CHILD object)
        // This searches the child objects for the JellyMesh script
        JellyMesh meshGen = GetComponentInChildren<JellyMesh>();
        if (meshGen != null)
        {
            meshGen.Initialize(createdNodes.ToArray());
        }
        else
        {
            Debug.LogWarning("JellyBodyBuilder: Could not find 'CytoplasmMesh' child with JellyMesh script!");
        }

        // === INITIALIZE PHYSICS ===

        // 3. Volume Preservation (prevents node overlap during fast movement)
        VolumePreservation volumePreserve = GetComponent<VolumePreservation>();
        if (volumePreserve != null)
        {
            volumePreserve.Initialize(createdNodes.ToArray());
        }
    }

    Vector3 GetVectorFromAngle(float angle)
    {
        float angleRad = angle * (Mathf.PI / 180f);
        return new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
    }
}