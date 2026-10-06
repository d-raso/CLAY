using UnityEngine;

/// <summary>
/// Toggleable in-GAME visualiser for the <see cref="FlowFieldManager"/> current field: a grid of
/// arrows pinned to WORLD positions, each pointing along the local flow and coloured/scaled by speed.
/// Lets you see where the water is still, streaming, or lively. Press <see cref="toggleKey"/> (F).
///
/// Drawn SCREEN-SPACE via GL in the IMGUI/Repaint pass (after the camera + renderer features) so it's
/// immune to the full-screen water distortion, but the arrows are anchored to a fixed WORLD lattice so
/// they pan and scale with the camera. Samples the real field; adds no forces.
/// </summary>
[DefaultExecutionOrder(100)]
public class FlowFieldDebugArrows : MonoBehaviour
{
    [Header("Toggle")]
    public KeyCode toggleKey = KeyCode.F;
    public bool visibleOnStart = false;

    [Header("Grid (world-anchored)")]
    [Tooltip("Spacing between arrows in WORLD units. Arrows are pinned to world positions.")]
    public float worldSpacing = 2.5f;
    [Tooltip("Fraction of the spacing an arrow occupies at full length.")]
    [Range(0.3f, 1.2f)] public float maxArrowFill = 0.9f;
    [Tooltip("Arrow line thickness in pixels.")]
    public float lineWidthPx = 2.5f;
    [Tooltip("Cap on arrows drawn (coarsens the grid when zoomed far out).")]
    public int maxArrows = 2600;

    [Header("Colour by speed")]
    [Tooltip("Flow magnitude that maps to the 'fast' colour. Lower = more sensitive.")]
    public float fastSpeed = 2.5f;
    [Tooltip("Below this speed the water reads as 'still' (arrows dimmed).")]
    public float stillSpeed = 0.15f;
    public Color calmColor = new Color(0.3f, 0.8f, 1f, 1f);     // slow current
    public Color fastColor = new Color(1f, 0.35f, 0.55f, 1f);   // fast current / stream

    [Header("Readout")]
    public bool showHud = true;

    Camera cam;
    Material glMat;
    bool shown;
    float sampledMagAtCenter;

    void Start()
    {
        cam = Camera.main;
        shown = visibleOnStart;
        var sh = Shader.Find("Hidden/Internal-Colored");
        glMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        glMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        glMat.SetInt("_ZWrite", 0);
        glMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) shown = !shown;
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        DrawHud();
        if (!shown) return;
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        var mgr = FlowFieldManager.Instance;
        if (mgr == null || glMat == null) return;

        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        Vector3 c = cam.transform.position;

        float sp = Mathf.Max(0.25f, worldSpacing);
        int ix0 = Mathf.FloorToInt((c.x - halfW) / sp), ix1 = Mathf.CeilToInt((c.x + halfW) / sp);
        int iy0 = Mathf.FloorToInt((c.y - halfH) / sp), iy1 = Mathf.CeilToInt((c.y + halfH) / sp);
        int nx = ix1 - ix0 + 1, ny = iy1 - iy0 + 1;
        int stride = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt((float)nx * ny / Mathf.Max(1, maxArrows))));
        float maxLen = sp * stride * maxArrowFill;

        sampledMagAtCenter = mgr.SampleFlowAtPosition(c).magnitude;

        glMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix();
        GL.Begin(GL.QUADS);

        for (int iy = iy0; iy <= iy1; iy += stride)
        for (int ix = ix0; ix <= ix1; ix += stride)
        {
            Vector2 p = new Vector2(ix * sp, iy * sp);
            Vector2 flow = mgr.SampleFlowAtPosition(p);
            float mag = flow.magnitude;

            // Still water: draw a small dim dot so you can SEE it's dead-calm (not just missing).
            float t = Mathf.Clamp01(mag / fastSpeed);
            Color col = Color.Lerp(calmColor, fastColor, t);
            col.a = mag < stillSpeed ? 0.25f : 0.9f;
            GL.Color(col);

            Vector2 dir = mag > 1e-4f ? flow / mag : Vector2.right;
            float len = maxLen * Mathf.Lerp(0.35f, 1f, t);
            Vector3 wTail = new Vector3(p.x - dir.x * len * 0.5f, p.y - dir.y * len * 0.5f, 0f);
            Vector3 wHead = new Vector3(p.x + dir.x * len * 0.5f, p.y + dir.y * len * 0.5f, 0f);

            Vector3 sTail = cam.WorldToScreenPoint(wTail);
            Vector3 sHead = cam.WorldToScreenPoint(wHead);
            // OnGUI's pixel space has its origin at the TOP-left; WorldToScreenPoint's is bottom-left — flip y, or every
            // arrow is drawn vertically mirrored (wrong place, wrong vertical direction)
            Vector2 a = new Vector2(sTail.x, Screen.height - sTail.y);
            Vector2 b = new Vector2(sHead.x, Screen.height - sHead.y);

            if (mag < stillSpeed)
            {
                // A tiny cross/dot at the point for still water.
                Vector2 mid = (a + b) * 0.5f;
                Segment(mid + Vector2.left * 2f, mid + Vector2.right * 2f, lineWidthPx);
                continue;
            }

            Vector2 sd = b - a; float sl = sd.magnitude;
            if (sl < 0.5f) continue;
            sd /= sl;
            Vector2 perp = new Vector2(-sd.y, sd.x);

            Segment(a, b, lineWidthPx);                                   // shaft
            float barb = Mathf.Min(10f, sl * 0.4f);
            Segment(b, b - sd * barb + perp * barb * 0.55f, lineWidthPx); // head barbs
            Segment(b, b - sd * barb - perp * barb * 0.55f, lineWidthPx);
        }

        GL.End();
        GL.PopMatrix();
    }

    void Segment(Vector2 a, Vector2 b, float w)
    {
        Vector2 d = b - a; float l = d.magnitude; if (l < 1e-4f) return; d /= l;
        Vector2 o = new Vector2(-d.y, d.x) * (w * 0.5f);
        GL.Vertex3(a.x - o.x, a.y - o.y, 0);
        GL.Vertex3(a.x + o.x, a.y + o.y, 0);
        GL.Vertex3(b.x + o.x, b.y + o.y, 0);
        GL.Vertex3(b.x - o.x, b.y - o.y, 0);
    }

    void DrawHud()
    {
        if (!showHud) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        style.normal.textColor = shown ? new Color(0.6f, 1f, 0.7f) : new Color(1f, 1f, 1f, 0.6f);
        string s = $"[{toggleKey}] flow field: {(shown ? "ON" : "OFF")}";
        if (shown) s += $"   center current speed: {sampledMagAtCenter:0.00}";
        GUI.Label(new Rect(12, 12, 600, 24), s, style);
    }

    void OnDestroy() { if (glMat != null) Destroy(glMat); }
}
