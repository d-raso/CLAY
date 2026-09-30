using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class JellyMesh : MonoBehaviour
{
    public Transform[] nodes;
    [Tooltip("How many smooth points to generate between each physics node. Higher = Smoother.")]
    [Range(1, 20)]
    public int curveResolution = 5;

    [Tooltip("Per-cell cytoplasm tint, multiplied into the BioSlime body (set by CellVariety).")]
    public Color tint = Color.white;

    private Mesh mesh;
    private readonly List<Color> colorsList = new List<Color>();

    // We use Lists now because the number of vertices is dynamic (12 * Resolution)
    private List<Vector3> verticesList = new List<Vector3>();
    private List<int> trianglesList = new List<int>();
    private List<Vector2> uvsList = new List<Vector2>();
    private List<Vector3> normalsList = new List<Vector3>();

    public void Initialize(Transform[] generatedNodes)
    {
        nodes = generatedNodes;
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
    }

    void Update()
    {
        // Safety Check
        if (nodes == null || nodes.Length == 0) return;

        GenerateSmoothMesh();
    }

    void GenerateSmoothMesh()
    {
        // 1. Clear old data
        verticesList.Clear();
        trianglesList.Clear();
        uvsList.Clear();
        normalsList.Clear();

        int nodeCount = nodes.Length;

        // 2. Add Center Vertex
        // The center is always local (0,0) relative to this object
        verticesList.Add(Vector3.zero);
        uvsList.Add(new Vector2(0.5f, 0.5f));
        normalsList.Add(Vector3.back); // Point toward camera for 2D lighting

        // 3. Generate Smooth Rim Vertices
        for (int i = 0; i < nodeCount; i++)
        {
            // We need 4 points to calculate a curve: Previous, Current, Next, Next-Next
            // The % operator handles the wrapping around the circle automatically
            Vector3 p0 = transform.InverseTransformPoint(nodes[(i - 1 + nodeCount) % nodeCount].position);
            Vector3 p1 = transform.InverseTransformPoint(nodes[i].position);
            Vector3 p2 = transform.InverseTransformPoint(nodes[(i + 1) % nodeCount].position);
            Vector3 p3 = transform.InverseTransformPoint(nodes[(i + 2) % nodeCount].position);

            // Create intermediate points between p1 and p2
            for (int j = 0; j < curveResolution; j++)
            {
                float t = (float)j / curveResolution;

                // Calculate position using Spline math
                Vector3 pos = GetCatmullRomPosition(t, p0, p1, p2, p3);
                verticesList.Add(pos);

                // Calculate UVs based on Angle (keeps texture stable even when wobbling)
                float totalSteps = nodeCount * curveResolution;
                float currentStep = (i * curveResolution) + j;
                float angle = (currentStep / totalSteps) * Mathf.PI * 2;

                float u = Mathf.Cos(angle) * 0.5f + 0.5f;
                float v = Mathf.Sin(angle) * 0.5f + 0.5f;
                uvsList.Add(new Vector2(u, v));
                normalsList.Add(Vector3.back); // All normals point toward camera
            }
        }

        // 4. Generate Triangles (Fan shape from center)
        // We skip index 0 (Center) for the loop logic
        int rimCount = verticesList.Count - 1;

        for (int i = 0; i < rimCount; i++)
        {
            trianglesList.Add(0);           // Center
            trianglesList.Add(i + 1);       // Current Rim Vertex

            // Connect to the next vertex (wrapping back to 1 at the end)
            int nextIndex = (i + 1) % rimCount + 1;
            trianglesList.Add(nextIndex);   // Next Rim Vertex
        }

        // 5. Apply to Mesh
        mesh.Clear();
        mesh.SetVertices(verticesList);
        mesh.SetTriangles(trianglesList, 0);
        mesh.SetUVs(0, uvsList);
        mesh.SetNormals(normalsList);

        // Per-cell tint via vertex colors (the Sprite-Lit BioSlime multiplies these in), so different
        // species can have different cytoplasm hues from the same material.
        if (colorsList.Count != verticesList.Count)
        {
            colorsList.Clear();
            for (int v = 0; v < verticesList.Count; v++) colorsList.Add(tint);
        }
        else
        {
            for (int v = 0; v < colorsList.Count; v++) colorsList[v] = tint;
        }
        mesh.SetColors(colorsList);

        mesh.RecalculateBounds();
    }

    // This is the math function that creates the curves
    Vector3 GetCatmullRomPosition(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Vector3 a = 2f * p1;
        Vector3 b = p2 - p0;
        Vector3 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
        Vector3 d = -p0 + 3f * p1 - 3f * p2 + p3;

        return 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));
    }
}