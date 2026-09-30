using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CLAY.Galaxy;

namespace CLAY.Surface
{
    /// <summary>
    /// TERRAIN LAB (Y in the system view) — a studio for the ground materials, like the Flora Lab for plants. Shows one
    /// terrain type on a rolling 60 m test patch with the real surface shader, lighting and post-processing, so each
    /// type can be judged on its own: cycle types (← →), blend into a second type across the patch, change the base
    /// (orbital) colour, sun angle, wetness, and orbit the camera. Esc returns to the system.
    /// </summary>
    public sealed class TerrainLab : MonoBehaviour
    {
        const int LabLayer = 31, Renderer3D = 1;
        const float PatchSize = 60f; const int PatchRes = 180;

        static bool _open; static int _suppressUntil = -1;
        public static bool Active => _open || Time.frameCount <= _suppressUntil;

        SystemViewer viewer; Camera cam;
        GameObject patch, lightGo, volGo; Mesh patchMesh; Material mat; Light sun; VolumeProfile profile;

        // saved state
        CameraClearFlags sClear; Color sBg; float sNear, sFar, sFov; Vector3 sPos; Quaternion sRot; int sMask, sRenderer;
        bool sShadows, sPost; LayerMask sVolMask; float sShadowDist; int sCascades;
        AmbientMode sAmbMode; Color sAmbSky, sAmbEq, sAmbGround; SphericalHarmonicsL2 sProbe; Light sRsSun; bool sFog;

        int typeA = 0, typeB = -1;
        int baseIdx = 0; float sunElev = 35f, sunAz = 40f, wet = 0f;
        float yaw = 35f, pitch = 28f, dist = 14f; Vector2 lastMouse; bool dragging;
        bool dirty = true;
        ScriptableRendererFeature ssao;
        GUIStyle title, label, small;

        static readonly (string name, Color c)[] Bases =
        {
            ("Earth soil", new Color(0.46f, 0.38f, 0.29f)), ("Mars rust", new Color(0.62f, 0.35f, 0.2f)),
            ("Lunar grey", new Color(0.44f, 0.43f, 0.41f)), ("Carbon dark", new Color(0.12f, 0.11f, 0.1f)),
            ("Ice white", new Color(0.86f, 0.9f, 0.94f)), ("Pale sand", new Color(0.78f, 0.7f, 0.54f)),
            ("Green world", new Color(0.3f, 0.42f, 0.22f)), ("Tholin orange", new Color(0.55f, 0.36f, 0.16f)),
        };

        static readonly string[] Blurb =
        {
            "Jointed, weathered bedrock: fracture joints, ridged relief, patchy tone.",
            "Boulder field / impact ejecta: rounded blocks sitting in grit with contact shadows.",
            "Desert pavement: tightly packed pebbles with a varnished sheen (Venera, Gale crater).",
            "Loose sand with small wind ripples and glinting grains.",
            "Dune sand: sharp-crested asymmetric ripples, wind-streaked tone.",
            "Fine silty soil: clods, crumbs and the odd pebble.",
            "Dried lake hardpan: smooth glazed clay with faint shallow polygons.",
            "Desiccation cracks: polygon plates with deep cracks and curled edges.",
            "Metallic rock: polished iron-nickel with rust blooms.",
            "Molten rock: dark cooling crust over glowing, slowly pulsing seams.",
            "Glacial ice sheet: smooth blue-white ice with crevasse lines.",
            "Snow: drifts and wind-carved sastrugi, bluish in hollows, glittering.",
            "Grass: tufted, patchy ground cover.",
            "Regolith: impact-gardened dust with micro-craters; brightens with the sun behind you (opposition surge).",
            "Basalt flow: dark glassy ropes and lobes with rubbly clinker.",
            "Salt flat: brilliant evaporite crust with raised pressure-ridge polygons.",
            "Sulfur crust: Io-like yellow with orange and red-brown allotropes.",
            "Patterned ground: periglacial sorted-stone polygons.",
            "Scree: angular talus fragments.",
            "Frost: hoarfrost settling in the hollows of cold rock.",
            "Moss: soft cushions of moss / biological soil crust.",
        };

        public static void Open(SystemViewer v)
        {
            if (_open) return;
            var go = new GameObject("TerrainLab");
            var lab = go.AddComponent<TerrainLab>();
            lab.viewer = v;
            lab.Init();
        }

        void Init()
        {
            _open = true;
            if (viewer) viewer.suspended = true;
            cam = Camera.main;
            sClear = cam.clearFlags; sBg = cam.backgroundColor; sNear = cam.nearClipPlane; sFar = cam.farClipPlane; sFov = cam.fieldOfView;
            sPos = cam.transform.position; sRot = cam.transform.rotation; sMask = cam.cullingMask;
            var cd = cam.GetUniversalAdditionalCameraData();
            sShadows = cd.renderShadows; sPost = cd.renderPostProcessing; sVolMask = cd.volumeLayerMask; sRenderer = GetRendererIndex(cd);
            cd.SetRenderer(Renderer3D); cd.renderShadows = true; cd.renderPostProcessing = true; cd.volumeLayerMask = 1 << LabLayer;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.52f, 0.62f, 0.76f);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 800f; cam.fieldOfView = 45f; cam.cullingMask = 1 << LabLayer;

            var urp = UniversalRenderPipeline.asset;
            if (urp) { sShadowDist = urp.shadowDistance; sCascades = urp.shadowCascadeCount; urp.shadowDistance = 120f; urp.shadowCascadeCount = 2; }

            sAmbMode = RenderSettings.ambientMode; sAmbSky = RenderSettings.ambientSkyColor; sAmbEq = RenderSettings.ambientEquatorColor;
            sAmbGround = RenderSettings.ambientGroundColor; sProbe = RenderSettings.ambientProbe; sRsSun = RenderSettings.sun; sFog = RenderSettings.fog;
            RenderSettings.fog = false;

            Shader.SetGlobalFloat("_CurvK", 0f);
            Shader.SetGlobalVector("_SurfOrigin", Vector4.zero);

            lightGo = new GameObject("LabSun") { layer = LabLayer };
            sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.cullingMask = 1 << LabLayer;
            sun.color = new Color(1f, 0.97f, 0.92f); sun.intensity = 1.3f;
            RenderSettings.sun = sun;

            // same filmic grade as the surface
            volGo = new GameObject("LabPost") { layer = LabLayer };
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 100f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bl = profile.Add<Bloom>(true); bl.threshold.Override(1.15f); bl.intensity.Override(0.3f);
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.45f); ca.contrast.Override(12f);
            vol.sharedProfile = profile;
            ssao = SSAO.Find(); SSAO.SetActive(ssao, true); SSAO.SetIntensity(ssao, 1.3f);

            TerrainTextures.Ensure();   // bake (once per session) the ground texture sets on worker threads
            mat = new Material(Shader.Find("CLAY/SurfaceTerrain"));
            patch = new GameObject("TerrainPatch") { layer = LabLayer };
            patch.AddComponent<MeshFilter>();
            var mr = patch.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.On;
            patchMesh = new Mesh { name = "LabPatch", indexFormat = IndexFormat.UInt32 };
            patch.GetComponent<MeshFilter>().sharedMesh = patchMesh;
            BuildPatch();
        }

        // A gently rolling patch with a small ridge and a hollow, so relief, shadows and slopes all show.
        void BuildPatch()
        {
            int n = PatchRes; float st = PatchSize / n, half = PatchSize * 0.5f;
            var v = new Vector3[(n + 1) * (n + 1)]; var col = new Color[v.Length];
            var w0 = new Vector4[v.Length]; var w1 = new Vector4[v.Length];
            Color baseC = Bases[baseIdx].c; baseC.a = wet;
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float x = i * st - half, z = j * st - half;
                    float y = Mathf.PerlinNoise(x * 0.05f + 3f, z * 0.05f + 7f) * 2.2f
                            + Mathf.Exp(-((x - 10f) * (x - 10f) + (z + 6f) * (z + 6f)) / 40f) * 2.5f      // a mound
                            - Mathf.Exp(-((x + 12f) * (x + 12f) + (z - 8f) * (z - 8f)) / 60f) * 1.5f;     // a hollow
                    int k = j * (n + 1) + i;
                    v[k] = new Vector3(x, y, z);
                    col[k] = baseC;
                    // blend mode: type A on the left → type B on the right
                    float t = typeB >= 0 ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-8f, 8f, x)) : 0f;
                    w0[k] = new Vector4(1f - t, t, 0f, 0f); w1[k] = Vector4.zero;
                }
            var tri = new int[n * n * 6]; int q = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    tri[q++] = a; tri[q++] = c; tri[q++] = b; tri[q++] = b; tri[q++] = c; tri[q++] = d;
                }
            patchMesh.Clear();
            patchMesh.SetVertices(v); patchMesh.SetColors(col); patchMesh.SetUVs(0, w0); patchMesh.SetUVs(1, w1);
            patchMesh.SetTriangles(tri, 0); patchMesh.RecalculateNormals(); patchMesh.RecalculateBounds();
            Shader.SetGlobalVector("_TypeIdA", new Vector4(typeA, typeB >= 0 ? typeB : -1, -1, -1));
            Shader.SetGlobalVector("_TypeIdB", new Vector4(-1, -1, -1, -1));
            dirty = false;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            TerrainTextures.Poll();
            if (Input.GetKeyDown(KeyCode.RightArrow)) { typeA = (typeA + 1) % SurfaceGeo.TypeCount; dirty = true; }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { typeA = (typeA + SurfaceGeo.TypeCount - 1) % SurfaceGeo.TypeCount; dirty = true; }
            if (Input.GetKey(KeyCode.LeftBracket)) sunElev = Mathf.Clamp(sunElev - 25f * Time.unscaledDeltaTime, -5f, 89f);
            if (Input.GetKey(KeyCode.RightBracket)) sunElev = Mathf.Clamp(sunElev + 25f * Time.unscaledDeltaTime, -5f, 89f);
            if (dirty) BuildPatch();

            // orbit camera: right/left drag outside the panel, wheel to zoom
            Vector2 m = Input.mousePosition;
            bool overPanel = m.x < 340f;
            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !overPanel) { dragging = true; lastMouse = m; }
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1)) dragging = false;
            if (dragging) { Vector2 d = m - lastMouse; yaw += d.x * 0.25f; pitch = Mathf.Clamp(pitch - d.y * 0.2f, 3f, 85f); lastMouse = m; }
            if (!overPanel) dist = Mathf.Clamp(dist * (1f - Input.mouseScrollDelta.y * 0.1f), 1.2f, 70f);
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = new Vector3(0f, 1.5f, 0f) - rot * Vector3.forward * dist;

            // sun + sky-ish ambient
            Vector3 sd = Quaternion.Euler(0f, sunAz, 0f) * Quaternion.Euler(-sunElev, 0f, 0f) * Vector3.forward;   // toward the sun
            lightGo.transform.rotation = Quaternion.LookRotation(-sd.normalized, Vector3.up);
            float day = Mathf.Clamp01((sunElev + 2f) / 12f);
            sun.intensity = 1.3f * day;
            Color sky = Color.Lerp(new Color(0.04f, 0.05f, 0.08f), new Color(0.5f, 0.62f, 0.8f), day);
            cam.backgroundColor = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * 0.9f; RenderSettings.ambientEquatorColor = sky * 0.6f; RenderSettings.ambientGroundColor = sky * 0.2f;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(RenderSettings.ambientEquatorColor * 0.8f);
            sh.AddDirectionalLight(Vector3.up, RenderSettings.ambientSkyColor * 0.6f, 1f);
            RenderSettings.ambientProbe = sh;
        }

        void OnGUI()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
                label = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
                small = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            }
            GUILayout.BeginArea(new Rect(10, 10, 320, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("Terrain Lab", title);
            if (!TerrainTextures.Ready) GUILayout.Label("<color=#ffcc66>Synthesising texture sets… (a few seconds, once per session)</color>", label);
            GUILayout.Label($"<b>{(SurfaceGeo.TerrainType)typeA}</b>  ({typeA + 1}/{SurfaceGeo.TypeCount})", label);
            GUILayout.Label(Blurb[typeA], label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ Prev")) { typeA = (typeA + SurfaceGeo.TypeCount - 1) % SurfaceGeo.TypeCount; dirty = true; }
            if (GUILayout.Button("Next ▶")) { typeA = (typeA + 1) % SurfaceGeo.TypeCount; dirty = true; }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>All types</b>", label);
            for (int r = 0; r < SurfaceGeo.TypeCount; r += 3)
            {
                GUILayout.BeginHorizontal();
                for (int k = r; k < Mathf.Min(r + 3, SurfaceGeo.TypeCount); k++)
                {
                    GUI.color = k == typeA ? new Color(1f, 0.85f, 0.5f) : Color.white;
                    if (GUILayout.Button(((SurfaceGeo.TerrainType)k).ToString(), small)) { typeA = k; dirty = true; }
                }
                GUI.color = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label($"<b>Blend into</b> (right side): {(typeB >= 0 ? ((SurfaceGeo.TerrainType)typeB).ToString() : "none")}", label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("None", small)) { typeB = -1; dirty = true; }
            if (GUILayout.Button("◀", small)) { typeB = typeB < 0 ? SurfaceGeo.TypeCount - 1 : (typeB + SurfaceGeo.TypeCount - 1) % SurfaceGeo.TypeCount; dirty = true; }
            if (GUILayout.Button("▶", small)) { typeB = (typeB + 1) % SurfaceGeo.TypeCount; dirty = true; }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label($"<b>Base (orbital) colour</b>: {Bases[baseIdx].name}", label);
            for (int r = 0; r < Bases.Length; r += 4)
            {
                GUILayout.BeginHorizontal();
                for (int k = r; k < Mathf.Min(r + 4, Bases.Length); k++)
                    if (GUILayout.Button(Bases[k].name, small)) { baseIdx = k; dirty = true; }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label($"Sun elevation {sunElev:0}°  ([ ] keys)", label);
            sunElev = GUILayout.HorizontalSlider(sunElev, -5f, 89f);
            GUILayout.Label($"Sun azimuth {sunAz:0}°", label);
            sunAz = GUILayout.HorizontalSlider(sunAz, 0f, 360f);
            GUILayout.Label($"Wetness {wet:0.00}", label);
            float nw = GUILayout.HorizontalSlider(wet, 0f, 1f);
            if (Mathf.Abs(nw - wet) > 0.01f) { wet = nw; dirty = true; }

            GUILayout.FlexibleSpace();
            GUILayout.Label("<i>← → cycle types · drag to orbit · wheel to zoom · Esc back</i>", label);
            GUILayout.EndArea();
        }

        void Close()
        {
            if (cam)
            {
                cam.clearFlags = sClear; cam.backgroundColor = sBg; cam.nearClipPlane = sNear; cam.farClipPlane = sFar; cam.fieldOfView = sFov;
                cam.transform.position = sPos; cam.transform.rotation = sRot; cam.cullingMask = sMask;
                var cd = cam.GetUniversalAdditionalCameraData();
                cd.SetRenderer(sRenderer); cd.renderShadows = sShadows; cd.renderPostProcessing = sPost; cd.volumeLayerMask = sVolMask;
            }
            var urp = UniversalRenderPipeline.asset;
            if (urp) { urp.shadowDistance = sShadowDist; urp.shadowCascadeCount = sCascades; }
            RenderSettings.ambientMode = sAmbMode; RenderSettings.ambientSkyColor = sAmbSky; RenderSettings.ambientEquatorColor = sAmbEq;
            RenderSettings.ambientGroundColor = sAmbGround; RenderSettings.ambientProbe = sProbe; RenderSettings.sun = sRsSun; RenderSettings.fog = sFog;
            if (patch) Destroy(patch); if (patchMesh) Destroy(patchMesh); if (mat) Destroy(mat);
            if (lightGo) Destroy(lightGo); if (volGo) Destroy(volGo); if (profile) Destroy(profile);
            SSAO.SetActive(ssao, false);
            if (viewer) viewer.suspended = false;
            _open = false; _suppressUntil = Time.frameCount + 1;
            Destroy(gameObject);
        }

        static int GetRendererIndex(UniversalAdditionalCameraData d)
        {
            var f = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (f != null && d != null) ? (int)f.GetValue(d) : -1;
        }
    }
}
