using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Additive caustic light overlay — the bright rippling light-focus patterns
/// you see at the bottom of a pool. Rendered on top of everything via
/// Additive blending so it acts as an animated light layer.
///
/// SETUP: Add to an empty GameObject in the scene. No other configuration needed.
/// Two passes: broad slow shimmer + fine fast caustic lines.
/// </summary>
public class WaterCaustics : MonoBehaviour
{
    [Header("Tile Grid")]
    public float quadSize = 130f;

    // Caustic light tints come from the biome (_CausticColor global) — materials
    // stay white so the biome palette drives the color.
    [Header("Pass A — Fine lens web (small lenses, on top of everything)")]
    [Range(0.1f,1.5f)]  public float  fineScale     = 0.40f;
    public float                       fineSpeed     = 0.18f;
    [Range(0f,1.2f)]    public float  fineIntensity  = 0.55f;
    [Range(1f,8f)]      public float  fineSharpness  = 3.0f;
    public Color                       fineColor     = Color.white;
    public int                         fineSortOrder = 50;

    [Header("Pass B — Broad lens web (large lenses, behind cell)")]
    [Range(0.05f,0.6f)] public float broadScale    = 0.14f;
    public float                       broadSpeed    = 0.09f;
    [Range(0f,1.0f)]    public float broadIntensity = 0.20f;
    [Range(1f,8f)]      public float broadSharpness = 2.0f;
    public Color                       broadColor    = Color.white;
    public int                         broadSortOrder= -150;

    GameObject[,] fineGrid, broadGrid;
    Material      fineMat, broadMat;
    Transform     cam;
    Mesh          quad;

    void Start()
    {
        cam = Camera.main?.transform;
        if (!cam) { Debug.LogError("[Caustics] No main camera."); enabled = false; return; }

        var shader = Shader.Find("Custom/WaterCaustics");
        if (!shader) { Debug.LogError("[Caustics] Custom/WaterCaustics shader not found."); enabled = false; return; }

        fineMat  = MakeMat(shader, fineScale,  fineSpeed,  fineIntensity,  fineColor,  fineSharpness);
        broadMat = MakeMat(shader, broadScale, broadSpeed, broadIntensity, broadColor, broadSharpness);

        quad      = BuildQuad(quadSize);
        fineGrid  = BuildGrid("CausticFine",  fineMat,  fineSortOrder,  4f);
        broadGrid = BuildGrid("CausticBroad", broadMat, broadSortOrder, 8f);

        Snap(fineGrid,  4f);
        Snap(broadGrid, 8f);

        Debug.Log("[Caustics] Active.");
    }

    void LateUpdate()
    {
        if (!cam) return;
        var cp = cam.position;
        if (Mathf.Abs(cp.x - fineGrid[1,1].transform.position.x) > quadSize ||
            Mathf.Abs(cp.y - fineGrid[1,1].transform.position.y) > quadSize)
        {
            Snap(fineGrid,  4f);
            Snap(broadGrid, 8f);
        }
        // Push global flow direction each frame
        var flow = Shader.GetGlobalVector("_GlobalFlowDirection");
        fineMat.SetVector("_GlobalFlowDirection",  flow);
        broadMat.SetVector("_GlobalFlowDirection", flow);
        var turb = Shader.GetGlobalFloat("_WaterTurbulence");
        fineMat.SetFloat("_WaterTurbulence",  turb);
        broadMat.SetFloat("_WaterTurbulence", turb);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    static Material MakeMat(Shader s, float sc, float sp, float inten, Color col, float sharp)
    {
        var m = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
        m.SetFloat("_Scale", sc);
        m.SetFloat("_Speed", sp);
        m.SetFloat("_Intensity", inten);
        m.SetFloat("_Sharpness", sharp);
        m.SetColor("_Color", col);
        return m;
    }

    GameObject[,] BuildGrid(string tag, Material mat, int sortOrder, float z)
    {
        var grid = new GameObject[3,3];
        for (int x=0;x<3;x++) for (int y=0;y<3;y++)
        {
            var go = new GameObject($"{tag}_{x}{y}");
            go.transform.SetParent(transform);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial      = mat;
            mr.sortingOrder        = sortOrder;
            mr.shadowCastingMode   = ShadowCastingMode.Off;
            mr.receiveShadows      = false;
            mr.lightProbeUsage     = LightProbeUsage.Off;
            mr.reflectionProbeUsage= ReflectionProbeUsage.Off;
            grid[x,y] = go;
        }
        return grid;
    }

    void Snap(GameObject[,] grid, float z)
    {
        var cp = cam.position;
        float cx = Mathf.Floor(cp.x/quadSize)*quadSize;
        float cy = Mathf.Floor(cp.y/quadSize)*quadSize;
        for (int x=0;x<3;x++) for (int y=0;y<3;y++)
            grid[x,y].transform.position = new Vector3(cx+(x-1)*quadSize, cy+(y-1)*quadSize, z);
    }

    static Mesh BuildQuad(float size)
    {
        float h = size*0.5f;
        var m = new Mesh { name="CausticQuad" };
        m.vertices  = new[]{ new Vector3(-h,-h), new Vector3(h,-h), new Vector3(-h,h), new Vector3(h,h) };
        m.triangles = new[]{ 0,2,1, 2,3,1 };
        m.uv        = new[]{ new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
        m.RecalculateNormals();
        return m;
    }

    void OnDestroy()
    {
        if (quad)     Destroy(quad);
        if (fineMat)  Destroy(fineMat);
        if (broadMat) Destroy(broadMat);
    }
}
