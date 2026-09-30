using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CLAY.Galaxy;

namespace CLAY.Flora
{
    // A self-contained "flora generation" screen. Opened from the system viewer when the player presses G over a
    // habitable planet and clicks Generate. Spawns a procedurally-built plant on a small ground disk, lets the user
    // randomize and tweak its structural genome (with a LIVE, derived survivability readout), then Save it into the
    // planet's habitat. Suspends the system viewer while open and restores the camera on exit.
    public class FloraLab : MonoBehaviour
    {
        SystemViewer viewer;
        PlanetData planet;
        ulong systemSeed;

        PlantGenome genome;
        GameObject plantRoot, ground, lightGo;
        Material woodMat, leafMat, flowerMat;
        Camera cam;

        // saved camera state
        CameraClearFlags savedClear; Color savedBg; float savedNear, savedFar, savedFov; Vector3 savedPos; Quaternion savedRot; int savedMask;
        Color savedAmbient; AmbientMode savedAmbientMode; bool savedShadows; int savedRendererIndex;
        const int LabLayer = 31;      // render ONLY lab objects (hide the galaxy/stars)
        const int Renderer3D = 1;     // GalaxyRenderer3D (Universal/Forward) — supports 3D lights + shadows

        static readonly Vector3 Stage = Vector3.zero;   // at the origin for best depth precision (cull mask hides the system)

        // True while the lab owns the screen. Other input handlers (e.g. StarSystemEntry's Escape-to-exit) must check
        // this so the SAME Escape press that closes the lab doesn't also tear down the system. Stays true through the
        // closing frame (+1) so there's no same-frame race regardless of script execution order.
        static bool _open; static int _suppressUntilFrame = -1;
        public static bool Active => _open || Time.frameCount <= _suppressUntilFrame;
        float yaw = 30f, pitch = 12f, dist = 12f;
        Vector3 panOffset;
        Vector2 lastMouse; bool haveMouse;
        int nextSeed = 1;
        Vector2 scroll;
        GUIStyle title, label, head;

        public static FloraLab Open(SystemViewer v, PlanetData p, ulong seed)
        {
            var go = new GameObject("FloraLab");
            var lab = go.AddComponent<FloraLab>();
            lab.viewer = v; lab.planet = p; lab.systemSeed = seed;
            lab.Init();
            return lab;
        }

        void Init()
        {
            _open = true;
            if (viewer != null) viewer.suspended = true;
            cam = Camera.main;
            savedClear = cam.clearFlags; savedBg = cam.backgroundColor; savedNear = cam.nearClipPlane;
            savedFar = cam.farClipPlane; savedFov = cam.fieldOfView; savedPos = cam.transform.position; savedRot = cam.transform.rotation;
            savedMask = cam.cullingMask;

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.31f, 0.33f);   // neutral studio grey (no space)
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 600f; cam.fieldOfView = 45f;
            cam.cullingMask = 1 << LabLayer;                        // show ONLY the lab

            // Switch to the 3D (Universal) renderer so real lights + shadows work (the galaxy uses the 2D renderer).
            var camData = cam.GetUniversalAdditionalCameraData();
            savedShadows = camData.renderShadows;
            savedRendererIndex = GetRendererIndex(camData);   // capture whatever the galaxy/system was using (default = -1)
            camData.SetRenderer(Renderer3D);
            camData.renderShadows = true;

            // Modest ambient (URP samples this via spherical harmonics).
            savedAmbientMode = RenderSettings.ambientMode; savedAmbient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.32f, 0.35f);

            var sh = Shader.Find("CLAY/Flora");
            woodMat = MakeMat(sh, twoSided: false);
            leafMat = MakeMat(sh, twoSided: true);
            flowerMat = MakeMat(sh, twoSided: true);

            // Round studio ground (receives shadows).
            ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ground.GetComponent<Collider>());
            ground.transform.position = Stage + new Vector3(0f, -0.05f, 0f);
            ground.transform.localScale = new Vector3(40f, 0.05f, 40f);
            var gm = MakeMat(sh, twoSided: false);
            Color gc = new Color(0.40f, 0.40f, 0.42f);
            gm.SetColor("_BaseColor", gc); gm.SetColor("_AccentColor", gc);
            ground.GetComponent<MeshRenderer>().sharedMaterial = gm;
            SetLayer(ground);

            // Studio key light — a real directional light casting soft shadows.
            lightGo = new GameObject("StudioLight");
            lightGo.transform.position = Stage;
            lightGo.transform.rotation = Quaternion.Euler(48f, 40f, 0f);
            var lt = lightGo.AddComponent<Light>();
            lt.type = LightType.Directional; lt.intensity = 1.0f; lt.color = new Color(1f, 0.98f, 0.94f);
            lt.shadows = LightShadows.Soft; lt.shadowStrength = 0.7f;
            lt.cullingMask = 1 << LabLayer;

            genome = PlantGenome.Random(planet, nextSeed++);
            Rebuild();
            FrameCamera();
        }

        void Rebuild()
        {
            if (plantRoot) Destroy(plantRoot);
            plantRoot = new GameObject("Plant");
            plantRoot.transform.position = Stage;

            // Vines grow over whatever structure is near them (they raycast colliders), so give them host rocks.
            EnsureHost(genome.archetype == PlantArchetype.Vine);
            var built = PlantBuilder.Build(genome);

            Color stemCol = StemColor(genome.stemMaterial, genome.pigment, genome.woodColor);
            woodMat.SetColor("_BaseColor", stemCol); woodMat.SetColor("_AccentColor", stemCol * 1.15f);
            woodMat.SetFloat("_StemMat", genome.stemMaterial); woodMat.SetFloat("_BarkType", (int)genome.barkType);
            woodMat.SetFloat("_BarkScale", genome.barkScale);
            woodMat.SetFloat("_BarkWarp", genome.barkWarp);
            woodMat.SetFloat("_BarkNoise", genome.barkNoise);
            woodMat.SetFloat("_StemDetail", 1f);
            woodMat.SetFloat("_BumpStrength", Mathf.Lerp(0.03f, 0.09f, genome.stemMaterial) * genome.barkRelief);
            float sm = genome.stemMaterial;
            float smooth = sm < 0.33f ? Mathf.Lerp(0.1f, 0.4f, sm / 0.33f)
                         : sm < 0.66f ? Mathf.Lerp(0.4f, 0.28f, (sm - 0.33f) / 0.33f)
                         : Mathf.Lerp(0.28f, 0.12f, (sm - 0.66f) / 0.34f);
            woodMat.SetFloat("_Smoothness", smooth); woodMat.SetFloat("_SpecStrength", Mathf.Lerp(0.15f, 0.55f, smooth));
            leafMat.SetColor("_BaseColor", genome.pigment); leafMat.SetColor("_AccentColor", genome.accent);
            leafMat.SetColor("_Underside", genome.underside);
            leafMat.SetFloat("_PlantHeight", Mathf.Max(genome.heightM, 0.2f)); leafMat.SetFloat("_Understorey", 0.5f);
            leafMat.SetFloat("_Translucency", genome.leafTranslucency);
            leafMat.SetFloat("_LeafDetail", 1f);
            leafMat.SetFloat("_LeafStyle", (int)genome.leafStyle);
            woodMat.SetFloat("_LeafDetail", 0f);
            flowerMat.SetColor("_BaseColor", genome.flowerColor); flowerMat.SetColor("_AccentColor", genome.accent);
            flowerMat.SetColor("_Underside", genome.flowerColor * 0.8f);
            leafMat.SetFloat("_LeafBump", 0.015f + genome.leafJitter * 0.025f + genome.organRelief * 0.015f);
            flowerMat.SetFloat("_LeafBump", 0.02f + genome.organRelief * 0.04f);
            flowerMat.SetFloat("_LeafDetail", 1f);                              // flowers & flower-mesh organs get
            flowerMat.SetFloat("_LeafStyle", (int)genome.organTexture);         // their own procedural surface

            woodMat.SetFloat("_Glow", 0f);
            leafMat.SetFloat("_Glow", genome.glow);
            flowerMat.SetFloat("_Glow", genome.glow * 1.3f);

            AddPart(built.wood, woodMat, "Wood");
            AddPart(built.foliage, leafMat, "Foliage");
            AddPart(built.flower, flowerMat, "Flower");
            SetLayer(plantRoot);
        }

        GameObject host;
        void EnsureHost(bool want)
        {
            if (!want) { if (host) { DestroyImmediate(host); host = null; } return; }
            if (host) return;
            host = new GameObject("VineHost");
            host.transform.position = Stage;
            var rm = MakeMat(Shader.Find("CLAY/Flora"), twoSided: false);
            Color rc = new Color(0.36f, 0.34f, 0.32f);
            rm.SetColor("_BaseColor", rc); rm.SetColor("_AccentColor", rc);
            rm.SetFloat("_StemDetail", 0f); rm.SetFloat("_Smoothness", 0.15f);
            void Rock(PrimitiveType t, Vector3 pos, Vector3 scale, Vector3 euler)
            {
                var go = GameObject.CreatePrimitive(t);
                DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(host.transform, false);
                go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localEulerAngles = euler;
                go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = rm;
            }
            Rock(PrimitiveType.Sphere, new Vector3(1.6f, 0.5f, 0.2f), new Vector3(2.6f, 1.9f, 2.2f), new Vector3(0f, 20f, 8f));    // boulder
            Rock(PrimitiveType.Cube,   new Vector3(3.2f, 1.6f, -0.6f), new Vector3(1.2f, 3.4f, 1.8f), new Vector3(6f, 25f, -4f));   // standing slab to climb
            Rock(PrimitiveType.Sphere, new Vector3(-0.9f, 0.15f, 1.3f), new Vector3(1.0f, 0.6f, 0.9f), Vector3.zero);             // pebble
            SetLayer(host);
            Physics.SyncTransforms();   // so the builder's raycasts see them this frame
        }

        void AddPart(Mesh m, Material mat, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(plantRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void SetLayer(GameObject go)
        {
            go.layer = LabLayer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject);
        }

        Material MakeMat(Shader sh, bool twoSided)
        {
            var m = new Material(sh);
            // Leaves/petals read waxy (smoother); bark is matte.
            m.SetFloat("_Smoothness", twoSided ? 0.5f : 0.2f);
            m.SetFloat("_SpecStrength", twoSided ? 0.5f : 0.15f);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.SetFloat("_TwoSided", twoSided ? 1f : 0f);
            return m;
        }

        void Update()
        {
            if (cam == null) return;
            Vector2 mp = Input.mousePosition;
            // Raw per-frame pixel delta (crisp, 1:1) instead of the smoothed, framerate-dependent "Mouse X/Y" axes.
            Vector2 md = haveMouse ? (mp - lastMouse) : Vector2.zero;
            lastMouse = mp; haveMouse = true;
            bool overUI = PanelRect().Contains(new Vector2(mp.x, Screen.height - mp.y));   // don't drive the camera from the panel
            if (!overUI)
            {
                if (Input.GetMouseButton(0))   // left-drag: orbit (0.25°/pixel)
                {
                    yaw += md.x * 0.25f;
                    pitch = Mathf.Clamp(pitch - md.y * 0.25f, -85f, 85f);
                }
                if (Input.GetMouseButton(1) || Input.GetMouseButton(2))   // right/middle-drag: pan
                    panOffset -= (cam.transform.right * md.x + cam.transform.up * md.y) * dist * 0.0016f;

                float sd = Input.mouseScrollDelta.y;   // scroll: zoom
                if (Mathf.Abs(sd) > 1e-4f) dist = Mathf.Clamp(dist * Mathf.Exp(-sd * 0.12f), 1f, 250f);
            }

            // Arrow keys pan the view.
            float ax = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float ay = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            if (ax != 0f || ay != 0f)
                panOffset += (cam.transform.right * ax + cam.transform.up * ay) * dist * Time.unscaledDeltaTime * 0.9f;

            Vector3 target = Stage + Vector3.up * genome.heightM * 0.45f + panOffset;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.position = target + rot * (Vector3.back * dist);
            cam.transform.LookAt(target, Vector3.up);
            // Adaptive clip planes: keep the near:far ratio sane at every zoom so the projection stays numerically
            // stable (a fixed 0.01 near with a 600 far is what produced the "screen position out of view frustum" spam).
            cam.nearClipPlane = Mathf.Clamp(dist * 0.03f, 0.02f, 5f);
            cam.farClipPlane = dist * 4f + 100f;

            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        // Trunk tissue colour across the material scale: spongy flesh → herbaceous green → fibrous tan → brown wood.
        static Color StemColor(float mat, Color pigment, Color wood)
        {
            Color spongy = new Color(0.62f, 0.66f, 0.52f);
            Color plant = Color.Lerp(pigment, new Color(0.32f, 0.5f, 0.26f), 0.5f);
            Color fibrous = new Color(0.66f, 0.56f, 0.4f);
            if (mat < 0.33f) return Color.Lerp(spongy, plant, mat / 0.33f);
            if (mat < 0.66f) return Color.Lerp(plant, fibrous, (mat - 0.33f) / 0.33f);
            return Color.Lerp(fibrous, wood, (mat - 0.66f) / 0.34f);
        }

        Rect PanelRect() => new Rect(14, 14, 300, Mathf.Min(Screen.height - 28, 620));
        void FrameCamera() { dist = Mathf.Max(genome.heightM * 1.5f + 3f, 4f); panOffset = Vector3.zero; yaw = 30f; pitch = 12f; }

        // A neutral Earth-like world so flora can be generated even when not sitting on a habitable planet.
        public static PlanetData DefaultGarden() => new PlanetData
        {
            name = "Testbed", index = 0, mass = 1f, radiusEarth = 1f, meanTempC = 16f, insolation = 1f,
            waterCoverage = 0.6f, hostStarTempK = 5800f, axialTiltDeg = 23f,
            rotationHours = 24f, greenhouseK = 33f, volcanism = 0.25f, habitabilityIndex = 0.8f, albedo = 0.3f,
            habClass = HabClass.Habitable, type = PlanetType.Terrestrial,
        };

        void Close()
        {
            if (cam != null)
            {
                cam.clearFlags = savedClear; cam.backgroundColor = savedBg; cam.nearClipPlane = savedNear;
                cam.farClipPlane = savedFar; cam.fieldOfView = savedFov; cam.cullingMask = savedMask;
                cam.transform.position = savedPos; cam.transform.rotation = savedRot;
                var cd = cam.GetUniversalAdditionalCameraData();
                cd.SetRenderer(savedRendererIndex);    // restore the EXACT renderer the galaxy/system had (not a hardcoded 0)
                cd.renderShadows = savedShadows;
                RenderSettings.ambientMode = savedAmbientMode; RenderSettings.ambientLight = savedAmbient;
            }
            if (viewer != null) viewer.suspended = false;
            if (plantRoot) Destroy(plantRoot);
            if (ground) Destroy(ground);
            if (lightGo) Destroy(lightGo);
            if (host) Destroy(host);
            _open = false; _suppressUntilFrame = Time.frameCount + 1;   // swallow this frame's Escape from other handlers
            Destroy(gameObject);
        }

        // URP exposes no public getter for the camera's renderer index (m_RendererIndex is private/serialized),
        // so read it via reflection. -1 means "use the pipeline default"; SetRenderer(-1) restores that cleanly.
        static int GetRendererIndex(UniversalAdditionalCameraData d)
        {
            var f = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (f != null && d != null) ? (int)f.GetValue(d) : -1;
        }

        // ── UI ──────────────────────────────────────────────────────────────────────────────────
        void OnGUI()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
                head = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
                label = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
            }

            GUILayout.BeginArea(PanelRect(), GUI.skin.box);
            GUILayout.Label("Flora Lab", title);
            GUILayout.Label($"<b>{planet.name}</b>  ·  photosynthetic organism", label);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("⟳ Randomize", GUILayout.Height(26))) { genome = PlantGenome.Random(planet, nextSeed++); Rebuild(); FrameCamera(); }
            if (GUILayout.Button("↩ Back", GUILayout.Height(26))) { Close(); GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);
            bool ch = false;

            GUILayout.Label("<b>Habit</b>", head);
            ch |= Cycle("Growth form", ref genome.archetype);
            ch |= Slider("Height (m)", ref genome.heightM, 0.1f, 90f);
            ch |= Slider("Trunk taper", ref genome.trunkTaper, 0.4f, 0.95f);
            ch |= Cycle("Branch style", ref genome.branchStyle);
            ch |= Stepper("Branch depth", ref genome.branchDepth, 1, 6);
            ch |= Slider("Branch angle", ref genome.branchAngle, 10f, 70f);
            ch |= Slider("Branch density", ref genome.branchDensity, 0f, 1f);
            ch |= Stepper("Stems", ref genome.stemCount, 1, 6);
            ch |= Toggle("Trunk splits (dichotomous)", ref genome.trunkSplits);
            ch |= Slider("Trunk bulge", ref genome.trunkBulge, 0f, 1f);
            ch |= Slider("Stem material (spongy→woody)", ref genome.stemMaterial, 0f, 1f);
            ch |= Cycle("Bark type", ref genome.barkType);
            ch |= Slider("Bark grain scale", ref genome.barkScale, 0.4f, 2.5f);
            ch |= Slider("Bark relief", ref genome.barkRelief, 0f, 2f);
            ch |= Slider("Bark warp (irregularity)", ref genome.barkWarp, 0f, 1f);
            ch |= Slider("Bark fine noise", ref genome.barkNoise, 0f, 1f);
            ch |= Slider("Lean / sprawl", ref genome.lean, 0f, 1f);
            ch |= Slider("Bare trunk (palm/umbrella)", ref genome.bareTrunkFrac, 0f, 0.9f);
            ch |= Slider("Pendant / dangle", ref genome.pendant, 0f, 1f);
            ch |= Slider("Stilt roots", ref genome.stiltRoots, 0f, 1f);
            ch |= Slider("Body girth", ref genome.bodyGirth, 0.5f, 2f);
            ch |= Slider("Ribbing", ref genome.ribbing, 0f, 1f);
            ch |= Slider("Spikiness", ref genome.spininess, 0f, 1f);

            GUILayout.Space(4); GUILayout.Label("<b>Foliage</b>", head);
            ch |= Cycle("Leaf shape", ref genome.leaf);
            ch |= Slider("Leaf size", ref genome.leafSize, 0.15f, 2.6f);
            ch |= Slider("Leaf density", ref genome.leafDensity, 0.2f, 2f);
            ch |= Cycle("Leaf surface", ref genome.leafStyle);
            ch |= Slider("Leaf translucency", ref genome.leafTranslucency, 0f, 1f);
            ch |= Slider("Leaf ruffle / frill", ref genome.leafRuffle, 0f, 1f);
            ch |= Slider("Leaf imperfection", ref genome.leafJitter, 0f, 1f);
            ch |= Slider("Leaf colour variation", ref genome.leafColorVar, 0f, 1f);
            if (genome.leaf == LeafShape.Frond || genome.archetype == PlantArchetype.Fern)
            {
                ch |= Slider("Frond leaflets", ref genome.frondPinnae, 0.4f, 2.2f);
                ch |= Slider("Frond leaflet width", ref genome.frondWidth, 0.3f, 2f);
                ch |= Slider("Frond division (lacy)", ref genome.frondDivision, 0f, 1f);
                ch |= Slider("Fiddlehead curl", ref genome.frondCurl, 0f, 1f);
            }
            ch |= HueSlider("Trunk hue", ref genome.woodColor);

            GUILayout.Space(4); GUILayout.Label("<b>Terminal organ</b>", head);
            ch |= Cycle("Tip organ", ref genome.terminalOrgan);
            if (genome.terminalOrgan != CLAY.Flora.TerminalOrgan.None)
                ch |= Slider("Organ size", ref genome.terminalSize, 0.4f, 2.2f);
            ch |= Cycle("Organ surface", ref genome.organShape);
            ch |= Cycle("Organ pattern", ref genome.organColor);
            ch |= Slider("Organ relief", ref genome.organRelief, 0f, 1f);
            ch |= Cycle("Flower/organ texture", ref genome.organTexture);

            GUILayout.Space(4); GUILayout.Label("<b>Flowers</b>", head);
            ch |= Toggle("Has flowers", ref genome.hasFlowers);
            if (genome.hasFlowers)
            {
                ch |= Slider("Flower amount", ref genome.flowerAmount, 0f, 1f);
                ch |= Stepper("Petals", ref genome.petalCount, 3, 12);
                ch |= Slider("Flower size", ref genome.flowerSize, 0.4f, 2f);
                ch |= HueSlider("Flower hue", ref genome.flowerColor);
            }

            GUILayout.Space(4); GUILayout.Label("<b>Paint job</b>", head);
            ch |= Cycle("Pattern", ref genome.paint);
            ch |= HueSlider("Primary hue", ref genome.pigment);
            ch |= HueSlider("Accent hue", ref genome.accent);
            ch |= Slider("Bioluminescence", ref genome.glow, 0f, 1.5f);

            GUILayout.EndScrollView();

            if (ch) Rebuild();

            // Derived survivability — never picked, always computed from the structure above.
            GUILayout.Space(6); GUILayout.Label("<b>Where it can live</b> <i>(derived)</i>", head);
            var s = PlantBiology.Derive(genome);
            GUILayout.Label($"{s.pathway} photosynthesis · CO₂ ≥ {s.minCO2kPa * 1e4f:0} ppm-eq · air ≥ {s.minPressureBar:0.00} bar", label);
            GUILayout.Label($"Temp {s.tempMinC:0}–{s.tempMaxC:0} °C · rain {s.precipMinMm:0}–{s.precipMaxMm:0} mm/yr", label);
            GUILayout.Label($"Gravity ≤ {s.gMax:0.0} g · wind ≤ ×{s.maxWindLoad:0.0} · UV ≤ ×{s.maxUV:0.0} · far-red use {s.farRedUse * 100f:0}%", label);
            GUILayout.Label($"Prefers: {PlantBiology.Habitat(genome)}", label);

            var climate = PlanetClimate.Derive(planet);
            GUILayout.Label($"<b>{planet.name} climate</b>", head);
            GUILayout.Label(climate.Summary(), label);

            var verdict = PlantBiology.Evaluate(genome, planet);
            bool viable = verdict.viable;
            GUILayout.Label(viable
                ? $"<color=#5fe0a0>Viable in: {string.Join(", ", verdict.goodZones)} (~{verdict.habitableArea * 100f:0}% of surface)</color>"
                : $"<color=#d16b62>Cannot live here: {verdict.limiting}</color>", label);

            GUILayout.Space(6);
            GUI.enabled = viable;
            if (GUILayout.Button("💾 Save to habitat", GUILayout.Height(30)))
            {
                HabitatRegistry.Add(systemSeed, planet.index, Clone(genome));
                genome = PlantGenome.Random(planet, nextSeed++); Rebuild(); FrameCamera();   // ready to make the next one
            }
            GUI.enabled = true;
            GUILayout.Label($"Saved so far: {HabitatRegistry.Count(systemSeed, planet.index)}", label);
            GUILayout.Label("L-drag: rotate · R-drag/arrows: pan · Scroll: zoom · Esc: back", label);
            GUILayout.EndArea();
        }

        // ── small IMGUI helpers ──
        bool Slider(string name, ref float val, float lo, float hi)
        {
            GUILayout.Label($"{name}: {val:0.##}", label);
            float nv = GUILayout.HorizontalSlider(val, lo, hi);
            if (!Mathf.Approximately(nv, val)) { val = nv; return true; }
            return false;
        }
        bool Stepper(string name, ref int val, int lo, int hi)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: {val}", label);
            bool ch = false;
            if (GUILayout.Button("−", GUILayout.Width(26)) && val > lo) { val--; ch = true; }
            if (GUILayout.Button("+", GUILayout.Width(26)) && val < hi) { val++; ch = true; }
            GUILayout.EndHorizontal();
            return ch;
        }
        bool Cycle<T>(string name, ref T val) where T : struct, System.Enum
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: {val}", label);
            bool ch = false;
            if (GUILayout.Button("‹ ›", GUILayout.Width(40)))
            {
                var vals = (T[])System.Enum.GetValues(typeof(T));
                int idx = System.Array.IndexOf(vals, val);
                val = vals[(idx + 1) % vals.Length]; ch = true;
            }
            GUILayout.EndHorizontal();
            return ch;
        }
        bool Toggle(string name, ref bool val)
        {
            bool nv = GUILayout.Toggle(val, $" {name}");
            if (nv != val) { val = nv; return true; }
            return false;
        }
        bool HueSlider(string name, ref Color c)
        {
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            GUILayout.Label($"{name}", label);
            float nh = GUILayout.HorizontalSlider(h, 0f, 1f);
            if (!Mathf.Approximately(nh, h)) { var nc = Color.HSVToRGB(nh, sat, v); nc.a = 1f; c = nc; return true; }
            return false;
        }

        static PlantGenome Clone(PlantGenome g) => new PlantGenome
        {
            archetype = g.archetype, leaf = g.leaf, branchStyle = g.branchStyle, paint = g.paint, heightM = g.heightM, trunkTaper = g.trunkTaper,
            branchDepth = g.branchDepth, branchAngle = g.branchAngle, branchDensity = g.branchDensity,
            stemCount = g.stemCount, trunkSplits = g.trunkSplits, trunkBulge = g.trunkBulge, stemMaterial = g.stemMaterial,
            barkType = g.barkType, barkScale = g.barkScale, barkRelief = g.barkRelief,
            barkWarp = g.barkWarp, barkNoise = g.barkNoise,
            lean = g.lean, bareTrunkFrac = g.bareTrunkFrac, pendant = g.pendant, stiltRoots = g.stiltRoots, leafSize = g.leafSize, leafDensity = g.leafDensity,
            leafTranslucency = g.leafTranslucency, leafRuffle = g.leafRuffle,
            leafStyle = g.leafStyle, leafColorVar = g.leafColorVar, leafJitter = g.leafJitter,
            frondPinnae = g.frondPinnae, frondWidth = g.frondWidth, frondDivision = g.frondDivision, frondCurl = g.frondCurl,
            ribbing = g.ribbing, spininess = g.spininess, bodyGirth = g.bodyGirth,
            terminalOrgan = g.terminalOrgan, terminalSize = g.terminalSize,
            organShape = g.organShape, organColor = g.organColor, organRelief = g.organRelief, organTexture = g.organTexture,
            hasFlowers = g.hasFlowers,
            flowerAmount = g.flowerAmount, petalCount = g.petalCount, flowerSize = g.flowerSize, glow = g.glow,
            pigment = g.pigment, accent = g.accent, underside = g.underside, woodColor = g.woodColor,
            flowerColor = g.flowerColor, seed = g.seed,
        };

        Color SkyColor()
        {
            // A tinted sky suggestive of the star + atmosphere; fallback pale blue.
            float t = planet != null ? planet.hostStarTempK : 5800f;
            return t < 3900f ? new Color(0.28f, 0.16f, 0.16f) : t < 5300f ? new Color(0.35f, 0.30f, 0.26f)
                 : new Color(0.42f, 0.55f, 0.72f);
        }
        Color GroundColor()
        {
            float wet = planet != null ? planet.waterCoverage : 0.4f;
            return Color.Lerp(new Color(0.42f, 0.34f, 0.24f), new Color(0.30f, 0.28f, 0.20f), wet);
        }
    }
}
