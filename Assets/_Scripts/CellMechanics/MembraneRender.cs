using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class MembraneRender : MonoBehaviour
{
    public Transform[] nodes; // This gets filled by the Builder

    [Tooltip("How many smooth points to generate between each physics node. Matches JellyMesh.")]
    [Range(1, 20)]
    public int curveResolution = 5;

    private LineRenderer line;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
    }

    // This function is called by the Builder when it is ready
    public void Initialize(Transform[] generatedNodes)
    {
        nodes = generatedNodes;
        line.useWorldSpace = true;
    }

    void LateUpdate()
    {
        // Safety check: Don't run if nodes aren't ready
        if (nodes == null || nodes.Length == 0) return;

        UpdateSmoothLine();
    }

    void UpdateSmoothLine()
    {
        int nodeCount = nodes.Length;

        // Calculate total points: (Segments * Resolution) + 1 to close the loop
        int totalPoints = (nodeCount * curveResolution) + 1;
        line.positionCount = totalPoints;

        int pointIndex = 0;

        for (int i = 0; i < nodeCount; i++)
        {
            // We need 4 points to calculate the curve
            Vector3 p0 = nodes[(i - 1 + nodeCount) % nodeCount].position;
            Vector3 p1 = nodes[i].position;
            Vector3 p2 = nodes[(i + 1) % nodeCount].position;
            Vector3 p3 = nodes[(i + 2) % nodeCount].position;

            // Generate intermediate points between p1 and p2
            for (int j = 0; j < curveResolution; j++)
            {
                float t = (float)j / curveResolution;
                Vector3 pos = GetCatmullRomPosition(t, p0, p1, p2, p3);

                line.SetPosition(pointIndex, pos);
                pointIndex++;
            }
        }

        // CLOSE THE LOOP: Make sure the very last point connects back to the very first point
        Vector3 startPoint = line.GetPosition(0);
        line.SetPosition(totalPoints - 1, startPoint);
    }

    // The Math Helper function for Curves
    Vector3 GetCatmullRomPosition(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Vector3 a = 2f * p1;
        Vector3 b = p2 - p0;
        Vector3 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
        Vector3 d = -p0 + 3f * p1 - 3f * p2 + p3;

        return 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));
    }
}