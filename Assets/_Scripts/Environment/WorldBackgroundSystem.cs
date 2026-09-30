using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Replaces InfiniteBackground + ProceduralBackgroundGenerator.
/// Three depth layers of MeshRenderer quads, always snapped around the camera.
/// A world-position shader (Custom/WorldBackground) generates all visuals on the GPU —
/// no sprites, no culling, no disappearing, infinite seamless coverage.
/// </summary>
public class WorldBackgroundSystem : MonoBehaviour
{
    [Header("Grid")]
    [Tooltip("World-unit size of each tile — 3x3 grid per layer")]
    public float quadSize = 150f;

    [Header("Far Layer (geology / vast formations)")]
    [Range(0.01f,0.3f)] public float farScale      = 0.055f;
    public float farSpeed   = 0.008f;
    public float farGlow    = 0.5f;
    public int   farSort    = -1000;

    [Header("Mid Layer (organic masses / mineral veins)")]
    [Range(0.01f,0.3f)] public float midScale      = 0.13f;
    public float midSpeed   = 0.018f;
    public float midGlow    = 0.4f;
    public int   midSort    = -500;

    [Header("Near Layer (crystal detail / biofilm)")]
    [Range(0.01f,0.3f)] public float nearScale     = 0.27f;
    public float nearSpeed  = 0.032f;
    public float nearGlow   = 0.3f;
    public int   nearSort   = -200;

    GameObject[,,] tiles;   // [layer, x, y]
    Material[]     mats;
    Transform      cam;
    Mesh           quad;

    static readonly float[] zDepths = { 60f, 35f, 12f };

    void Start()
    {
        cam = Camera.main?.transform;
        if (!cam) { Debug.LogError("[WorldBG] No main camera found."); enabled = false; return; }

        // Disable old background systems so nothing conflicts
        var ib  = FindFirstObjectByType<InfiniteBackground>();
        var pbg = FindFirstObjectByType<ProceduralBackgroundGenerator>();
        if (ib)  ib.enabled  = false;
        if (pbg) pbg.enabled = false;

        var shader = Shader.Find("Custom/WorldBackground");
        if (!shader) { Debug.LogError("[WorldBG] Shader 'Custom/WorldBackground' not found. Make sure WorldBackground.shader is in the project."); enabled = false; return; }

        quad  = BuildQuad(quadSize);
        tiles = new GameObject[3, 3, 3];
        mats  = new Material[3];

        float[] scales = { farScale,  midScale,  nearScale  };
        float[] speeds = { farSpeed,  midSpeed,  nearSpeed  };
        float[] glows  = { farGlow,   midGlow,   nearGlow   };
        int[]   sorts  = { farSort,   midSort,   nearSort   };
        float[] brights= { 1.0f,      0.88f,     0.75f      };

        for (int L = 0; L < 3; L++)
        {
            var mat = new Material(shader);
            mat.SetFloat("_LayerIndex", L);
            mat.SetFloat("_Scale",      scales[L]);
            mat.SetFloat("_Speed",      speeds[L]);
            mat.SetFloat("_GlowStr",    glows[L]);
            mat.SetFloat("_Brightness", brights[L]);
            mats[L] = mat;

            for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
            {
                var go = new GameObject($"WorldBG_L{L}_{x}{y}");
                go.transform.SetParent(transform);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial        = mat;
                mr.sortingOrder          = sorts[L];
                mr.shadowCastingMode     = ShadowCastingMode.Off;
                mr.receiveShadows        = false;
                mr.lightProbeUsage       = LightProbeUsage.Off;
                mr.reflectionProbeUsage  = ReflectionProbeUsage.Off;
                tiles[L, x, y] = go;
            }

            SnapLayer(L);
        }
    }

    void LateUpdate()
    {
        if (!cam) return;
        Vector3 cp = cam.position;
        for (int L = 0; L < 3; L++)
        {
            var center = tiles[L, 1, 1].transform.position;
            if (Mathf.Abs(cp.x - center.x) > quadSize ||
                Mathf.Abs(cp.y - center.y) > quadSize)
                SnapLayer(L);
        }
    }

    void SnapLayer(int L)
    {
        Vector3 cp = cam.position;
        float cx = Mathf.Floor(cp.x / quadSize) * quadSize;
        float cy = Mathf.Floor(cp.y / quadSize) * quadSize;
        float z  = zDepths[L];
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 3; y++)
            tiles[L, x, y].transform.position =
                new Vector3(cx + (x - 1) * quadSize, cy + (y - 1) * quadSize, z);
    }

    static Mesh BuildQuad(float size)
    {
        float h = size * 0.5f;
        var m = new Mesh { name = "WorldBGQuad" };
        m.vertices  = new[] { new Vector3(-h,-h,0), new Vector3(h,-h,0), new Vector3(-h,h,0), new Vector3(h,h,0) };
        m.triangles = new[] { 0,2,1, 2,3,1 };
        m.uv        = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
        m.RecalculateNormals();
        return m;
    }

    void OnDestroy()
    {
        if (quad) Destroy(quad);
        if (mats != null) foreach (var m in mats) if (m) Destroy(m);
    }
}
