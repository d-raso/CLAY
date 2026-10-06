using UnityEngine;
using CLAY.Galaxy;
using CLAY.GalaxyMap;

namespace CLAY.UI
{
    /// <summary>
    /// PLANET EDITOR — build any single world without hunting through the galaxy. A side panel exposes every input that
    /// goes into a planet (star class/mass/age/metallicity, an optional companion, orbit, spin, mass/radius, albedo,
    /// greenhouse, water, chemistry theme, volcanism, minerals, bombardment, tidal heating, life, moons). Each value can
    /// be AUTO (derived by the same physics as the galaxy generator) or forced. Changes rebuild the world live in the
    /// normal system viewer, so everything else works as usual: orbit the planet, L to land, G Flora Lab, Y Terrain Lab.
    /// Esc returns to the main menu.
    /// </summary>
    public sealed class PlanetEditor : MonoBehaviour
    {
        static PlanetEditor _inst;
        public static bool Active => _inst != null;

        GalaxyBootstrap galaxy;
        GalaxyCameraController fly;
        Camera cam;
        SystemViewer viewer;
        StarSystem sys;
        PlanetSpec spec = new PlanetSpec(), applied;
        int catFamily = 1; string catMsg;
        float dirtyAt = -1f;
        Vector2 scroll;
        bool collapsed;
        GUIStyle head, small, lbl, val;

        // saved galaxy camera state
        CameraClearFlags sClear; Color sBg; float sFov, sNear, sFar; Material sSky; Vector3 sPos; Quaternion sRot;

        public static void Open(GalaxyBootstrap galaxy)
        {
            if (_inst != null) return;
            var go = new GameObject("~PlanetEditor");
            _inst = go.AddComponent<PlanetEditor>();
            _inst.galaxy = galaxy;
            _inst.Init();
        }

        void Init()
        {
            cam = Camera.main;
            fly = cam ? cam.GetComponent<GalaxyCameraController>() : null;
            sClear = cam.clearFlags; sBg = cam.backgroundColor; sFov = cam.fieldOfView; sNear = cam.nearClipPlane; sFar = cam.farClipPlane;
            sSky = RenderSettings.skybox; sPos = cam.transform.position; sRot = cam.transform.rotation;
            if (fly) fly.enabled = false;
            if (galaxy) galaxy.DespawnAll();                         // the system viewer owns the screen now
            RenderSettings.skybox = null;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;

            var go = new GameObject("~SystemViewer");
            viewer = go.AddComponent<SystemViewer>();
            viewer.autoGenerateOnStart = false;
            viewer.EnsureInit();
            viewer.timeScale = 0.0012f;
            Preset("Earth-like");
            Rebuild();
        }

        void Rebuild()
        {
            applied = spec.Clone();
            try
            {
                sys = SystemGenerator.GenerateCustom(applied);
                viewer.LoadExternalSystem(sys);
                viewer.FocusFirstPlanet();
            }
            catch (System.Exception ex) { Debug.LogException(ex); }
            dirtyAt = -1f;
        }

        void Update()
        {
            bool sub = CLAY.Surface.SurfaceWorld.Active || CLAY.Flora.FloraLab.Active || CLAY.Surface.TerrainLab.Active;
            if (sub) { SystemViewer.ExtraUiRect = Rect.zero; return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (dirtyAt > 0f && Time.unscaledTime - dirtyAt > 0.35f) Rebuild();   // debounce slider drags
        }

        void Close()
        {
            SystemViewer.ExtraUiRect = Rect.zero;
            if (viewer) Destroy(viewer.gameObject);
            RenderSettings.skybox = sSky;
            cam.clearFlags = sClear; cam.backgroundColor = sBg; cam.fieldOfView = sFov; cam.nearClipPlane = sNear; cam.farClipPlane = sFar;
            cam.transform.position = sPos; cam.transform.rotation = sRot;
            if (galaxy) galaxy.Respawn();
            _inst = null;
            Destroy(gameObject);
            MainMenu.Show();
        }

        // ── UI ──────────────────────────────────────────────────────────────────────────────────────────
        const float W = 370f;
        static readonly string[] Types = System.Enum.GetNames(typeof(PlanetType));
        static readonly string[] Classes = System.Enum.GetNames(typeof(SpectralClass));
        static readonly string[] Themes = System.Enum.GetNames(typeof(PlanetTexture.ChemTheme));
        static readonly string[] Waters = System.Enum.GetNames(typeof(WaterChemistry));
        static readonly string[] Presets =
            { "Earth-like", "Mars-like", "Venus-like", "Titan-like", "Io-like", "Ocean world", "Snowball", "Lava world",
              "Carbon world", "Eyeball (M dwarf)", "Tatooine", "Hot Jupiter", "Jupiter", "Ice giant" };

        void OnGUI()
        {
            if (CLAY.Surface.SurfaceWorld.Active || CLAY.Flora.FloraLab.Active || CLAY.Surface.TerrainLab.Active) return;
            if (head == null)
            {
                head = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
                head.normal.textColor = new Color(0.75f, 0.85f, 1f);
                small = new GUIStyle(GUI.skin.button) { fontSize = 11 };
                lbl = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
                val = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight };
            }
            float h = Screen.height - 250f;                          // leave the viewer's story panel (bottom-right) visible
            var r = collapsed ? new Rect(Screen.width - 140, 10, 130, 30) : new Rect(Screen.width - W - 10, 10, W, h);
            SystemViewer.ExtraUiRect = r;
            if (collapsed)
            {
                if (GUI.Button(r, "◀ Planet Editor")) collapsed = false;
                return;
            }
            GUI.Box(r, GUIContent.none);
            GUILayout.BeginArea(new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 12));
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b><size=16>Planet Editor</size></b>", lbl);
            if (GUILayout.Button("▶", small, GUILayout.Width(28))) collapsed = true;
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);

            Readout();

            // presets + seed
            Section("Presets");
            for (int i = 0; i < Presets.Length; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(i + 3, Presets.Length); k++)
                    if (GUILayout.Button(Presets[k], small)) { Preset(Presets[k]); Rebuild(); }
                GUILayout.EndHorizontal();
            }
            // category picker: pick a documented category, the recipe + solver set the parameters that produce it
            Section("Category");
            var fams = CategoryRecipes.Families;
            string[] famNames = new string[fams.Length];
            for (int f = 0; f < fams.Length; f++) famNames[f] = fams[f].family;
            catFamily = GUILayout.SelectionGrid(catFamily, famNames, 3, small);
            var cats = fams[Mathf.Clamp(catFamily, 0, fams.Length - 1)].cats;
            for (int i = 0; i < cats.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < Mathf.Min(i + 2, cats.Length); k++)
                    if (GUILayout.Button(PlanetCategories.Name(cats[k]), small))
                    {
                        var baseline = new PlanetSpec { seed = spec.seed, starMassT = 0.85f, greenhouseK = 33f };
                        spec = CategoryRecipes.Solve(baseline, cats[k], out catMsg);
                        Rebuild();
                    }
                GUILayout.EndHorizontal();
            }
            if (!string.IsNullOrEmpty(catMsg)) GUILayout.Label(catMsg, lbl);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Seed {spec.seed}", lbl);
            if (GUILayout.Button("New seed", small, GUILayout.Width(80))) { spec.seed = (ulong)Random.Range(1, int.MaxValue); Dirty(); }
            GUILayout.EndHorizontal();

            // star
            Section("Star");
            int sc = GUILayout.SelectionGrid((int)spec.starClass, Classes, 7, small);
            if (sc != (int)spec.starClass) { spec.starClass = (SpectralClass)sc; Dirty(); }
            Slider("Mass within class", ref spec.starMassT, 0f, 1f, "0.00");
            Slider("Age (fraction of life)", ref spec.ageFrac, 0.002f, 1f, "0.00");
            Slider("Metallicity [Fe/H]", ref spec.metallicity, -1f, 0.6f, "+0.00;-0.00");
            Toggle("Companion star", ref spec.binary);
            if (spec.binary)
            {
                LogSlider("Separation (AU)", ref spec.companionSepAU, 0.05f, 800f);
                Slider("Mass ratio", ref spec.companionMassRatio, 0.05f, 1f, "0.00");
                GUILayout.Label("<i>< 0.6 AU = close pair: the planet orbits both stars (circumbinary)</i>", lbl);
            }

            // planet type
            Section("Planet");
            int ty = GUILayout.SelectionGrid((int)spec.type, Types, 3, small);
            if (ty != (int)spec.type)
            {
                spec.type = (PlanetType)ty;
                if (!PlanetData.IsRocky(spec.type) && spec.massEarth < 10f) spec.massEarth = spec.type == PlanetType.GasGiant ? 318f : 17f;
                if (PlanetData.IsRocky(spec.type) && spec.massEarth > 10f) spec.massEarth = 1f;
                Dirty();
            }
            LogSlider("Distance from star (AU)", ref spec.distanceAU, 0.01f, 200f);
            LogSlider("Mass (Earths)", ref spec.massEarth, 0.02f, 4000f);
            AutoSlider("Radius (Earths)", ref spec.radiusEarth, 0.2f, 16f, 1f);
            AutoSlider("Albedo", ref spec.albedo, 0.02f, 0.95f, 0.3f);
            AutoSlider("Greenhouse warming (K)", ref spec.greenhouseK, 0f, 500f, 33f);

            Section("Orbit & spin");
            Slider("Eccentricity", ref spec.eccentricity, 0f, 0.8f, "0.00");
            Slider("Inclination (°)", ref spec.inclinationDeg, 0f, 60f, "0");
            Slider("Axial tilt (°)", ref spec.axialTiltDeg, 0f, 180f, "0");
            LogSlider("Day length (hours)", ref spec.rotationHours, 2f, 5000f);
            Choice("Spin state", ref spec.lockMode, new[] { "Auto", "Locked", "Free", "3:2" });

            Section("Atmosphere & interior");
            AutoLogSlider("Surface pressure (bar)", ref spec.pressureBar, 0.0005f, 300f, 1f);
            AutoChoice("Gas mix", ref spec.atmoPreset, AtmoComposition.PresetNames, 2);
            AutoSlider("Magnetic field", ref spec.magneticField, 0f, 1f, 0.7f);
            AutoSlider("Flare dosage", ref spec.flareDose, 0f, 1f, 0.2f);
            AutoChoice("Surface liquid", ref spec.liquid, System.Enum.GetNames(typeof(LiquidType)), 3);
            AutoSlider("Redox (reduced→oxidised)", ref spec.redox, 0f, 1f, 0.5f);
            AutoChoice("Tectonics", ref spec.tectonics, System.Enum.GetNames(typeof(PlanetTexture.TectonicMode)), 2);
            GUILayout.Label("<i>Pressure / gas mix on Auto-greenhouse also set the warming (√P × absorbers).</i>", lbl);

            Section("Surface & chemistry");
            AutoSlider("Water coverage", ref spec.waterCoverage, 0f, 1f, 0.6f);
            AutoChoice("Sea chemistry", ref spec.waterChemistry, Waters, 2);
            AutoSlider("Volcanism", ref spec.volcanism, 0f, 1f, 0.4f);
            AutoSlider("Mineral diversity", ref spec.mineralDiversity, 0f, 1f, 0.5f);
            AutoSlider("Bombardment", ref spec.bombardment, 0f, 1f, 0.3f);
            AutoSlider("Tidal heating", ref spec.tidalHeat, 0f, 1f, 0.2f);
            AutoChoice("Chemistry theme", ref spec.theme, Themes, 3);
            Choice("Life", ref spec.habMode, new[] { "Auto", "Living", "Lifeless" });
            int moons = spec.moons;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Moons", lbl, GUILayout.Width(130));
            if (GUILayout.Toggle(moons < 0, "Auto", small, GUILayout.Width(50)) != (moons < 0)) { spec.moons = moons < 0 ? 1 : -1; Dirty(); }
            if (spec.moons >= 0)
            {
                int nm = Mathf.RoundToInt(GUILayout.HorizontalSlider(spec.moons, 0, 6));
                GUILayout.Label(nm.ToString(), val, GUILayout.Width(24));
                if (nm != spec.moons) { spec.moons = nm; Dirty(); }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            if (GUILayout.Button("Rebuild now")) Rebuild();
            GUILayout.Label("<i>L (aim at the planet) to land · G Flora Lab · Y Terrain Lab · Esc → main menu</i>", lbl);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Readout()
        {
            if (sys == null || sys.planets.Count == 0) return;
            var st = sys.star; var p = sys.planets[0];
            ulong pseed = DetRng.Hash(sys.seed, 1UL);
            var cl = PlanetClimate.Derive(p);
            string theme = PlanetData.IsRocky(p.type) ? PlanetTexture.Chem(p, pseed).theme.ToString() : "—";
            float g = p.mass / Mathf.Max(p.radiusEarth * p.radiusEarth, 1e-3f);
            GUILayout.Label(
                $"<b>{st.Designation}</b> {st.ClassLabel} · {st.stellarMass:0.00} M☉ · {st.luminosity:0.###} L☉ · {st.effectiveTemp:0} K · " +
                $"{st.stellarAgeGyr:0.0}/{st.lifetimeGyr:0.#} Gyr\n" +
                $"<b>{PlanetTexture.DisplayType(p, pseed)}</b> · {p.insolation:0.###} S⊕ · {p.meanTempC:0} °C · {g:0.00} g · " +
                $"{(PlanetTexture.HasAtmosphere(p) ? $"{cl.pressureBar:0.00} bar" : "airless")} · {p.habClass}" +
                $"{(p.tidallyLocked ? " · locked" : "")} · theme {theme} · {p.moons.Count} moon{(p.moons.Count == 1 ? "" : "s")}\n" +
                $"<i>{PlanetPhysics.Describe(p, cl.pressureBar)}</i>", lbl);
        }

        void Section(string s) { GUILayout.Space(6); GUILayout.Label(s, head); }
        void Dirty() { dirtyAt = Time.unscaledTime; }

        void Slider(string name, ref float v, float lo, float hi, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, lbl, GUILayout.Width(150));
            float nv = GUILayout.HorizontalSlider(v, lo, hi);
            GUILayout.Label(v.ToString(fmt), val, GUILayout.Width(52));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(nv - v) > 1e-5f) { v = nv; Dirty(); }
        }

        void LogSlider(string name, ref float v, float lo, float hi)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, lbl, GUILayout.Width(150));
            float t = Mathf.InverseLerp(Mathf.Log(lo), Mathf.Log(hi), Mathf.Log(Mathf.Clamp(v, lo, hi)));
            float nt = GUILayout.HorizontalSlider(t, 0f, 1f);
            GUILayout.Label(v < 10f ? v.ToString("0.###") : v.ToString("0"), val, GUILayout.Width(52));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(nt - t) > 1e-5f) { v = Mathf.Exp(Mathf.Lerp(Mathf.Log(lo), Mathf.Log(hi), nt)); Dirty(); }
        }

        void AutoSlider(string name, ref float v, float lo, float hi, float def)
        {
            bool auto = float.IsNaN(v);
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, lbl, GUILayout.Width(130));
            bool na = GUILayout.Toggle(auto, "Auto", small, GUILayout.Width(50));
            if (na != auto) { v = na ? float.NaN : def; Dirty(); }
            if (!na)
            {
                float nv = GUILayout.HorizontalSlider(v, lo, hi);
                GUILayout.Label(v.ToString("0.##"), val, GUILayout.Width(44));
                if (Mathf.Abs(nv - v) > 1e-5f) { v = nv; Dirty(); }
            }
            GUILayout.EndHorizontal();
        }

        void AutoLogSlider(string name, ref float v, float lo, float hi, float def)
        {
            bool auto = float.IsNaN(v);
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, lbl, GUILayout.Width(130));
            bool na = GUILayout.Toggle(auto, "Auto", small, GUILayout.Width(50));
            if (na != auto) { v = na ? float.NaN : def; Dirty(); }
            if (!na)
            {
                float t = Mathf.InverseLerp(Mathf.Log(lo), Mathf.Log(hi), Mathf.Log(Mathf.Clamp(v, lo, hi)));
                float nt = GUILayout.HorizontalSlider(t, 0f, 1f);
                GUILayout.Label(v < 10f ? v.ToString("0.###") : v.ToString("0"), val, GUILayout.Width(44));
                if (Mathf.Abs(nt - t) > 1e-5f) { v = Mathf.Exp(Mathf.Lerp(Mathf.Log(lo), Mathf.Log(hi), nt)); Dirty(); }
            }
            GUILayout.EndHorizontal();
        }

        void Toggle(string name, ref bool v)
        {
            bool nv = GUILayout.Toggle(v, " " + name);
            if (nv != v) { v = nv; Dirty(); }
        }

        void Choice(string name, ref int v, string[] opts)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, lbl, GUILayout.Width(130));
            int nv = GUILayout.Toolbar(v, opts, small);
            GUILayout.EndHorizontal();
            if (nv != v) { v = nv; Dirty(); }
        }

        void AutoChoice(string name, ref int v, string[] opts, int cols)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, lbl, GUILayout.Width(130));
            bool auto = v < 0;
            bool na = GUILayout.Toggle(auto, "Auto", small, GUILayout.Width(50));
            GUILayout.EndHorizontal();
            if (na != auto) { v = na ? -1 : 0; Dirty(); }
            if (!na)
            {
                int nv = GUILayout.SelectionGrid(Mathf.Max(v, 0), opts, cols, small);
                if (nv != v) { v = nv; Dirty(); }
            }
        }

        // ── presets: famous / archetypal worlds ──
        void Preset(string name)
        {
            ulong seed = spec.seed;
            spec = new PlanetSpec { seed = seed, starMassT = 0.85f, greenhouseK = 33f };   // a Sun-like star, Earth's greenhouse
            switch (name)
            {
                case "Earth-like": spec.habMode = 1; break;
                case "Mars-like":
                    spec.distanceAU = 1.52f; spec.massEarth = 0.107f; spec.type = PlanetType.Desert; spec.rotationHours = 24.6f;
                    spec.axialTiltDeg = 25f; spec.greenhouseK = float.NaN; spec.pressureBar = 0.006f; spec.atmoPreset = 2; spec.magneticField = 0.02f;
                    spec.redox = 0.85f; spec.waterCoverage = 0f; spec.theme = (int)PlanetTexture.ChemTheme.Ferrous; spec.habMode = 2; break;
                case "Venus-like":
                    spec.distanceAU = 0.72f; spec.massEarth = 0.815f; spec.type = PlanetType.Desert; spec.rotationHours = 2800f; spec.axialTiltDeg = 177f;
                    spec.albedo = 0.75f; spec.greenhouseK = float.NaN; spec.pressureBar = 92f; spec.atmoPreset = 1; spec.magneticField = 0.05f;
                    spec.waterCoverage = 0f; spec.theme = (int)PlanetTexture.ChemTheme.Sulfuric; spec.habMode = 2; break;
                case "Titan-like":
                    spec.distanceAU = 9.5f; spec.massEarth = 0.0225f; spec.type = PlanetType.FrozenRock; spec.rotationHours = 382f;
                    spec.greenhouseK = 12f; spec.pressureBar = 1.5f; spec.atmoPreset = 3; spec.liquid = (int)LiquidType.Methane;
                    spec.waterCoverage = 0.15f; spec.theme = (int)PlanetTexture.ChemTheme.Methanic; spec.habMode = 2; break;
                case "Io-like":
                    spec.distanceAU = 5.2f; spec.massEarth = 0.015f; spec.type = PlanetType.Terrestrial; spec.volcanism = 1f; spec.tidalHeat = 1f;
                    spec.waterCoverage = 0f; spec.theme = (int)PlanetTexture.ChemTheme.Sulfuric; spec.habMode = 2; break;
                case "Ocean world": spec.type = PlanetType.Ocean; spec.massEarth = 2.5f; spec.waterCoverage = 0.95f; spec.habMode = 1; break;
                case "Snowball":
                    spec.distanceAU = 1.9f; spec.type = PlanetType.FrozenRock; spec.waterCoverage = 0.5f; spec.theme = (int)PlanetTexture.ChemTheme.Snowball; spec.habMode = 2; break;
                case "Lava world":
                    spec.distanceAU = 0.02f; spec.massEarth = 4f; spec.type = PlanetType.Terrestrial; spec.lockMode = 1; spec.volcanism = 1f;
                    spec.theme = (int)PlanetTexture.ChemTheme.Lava; spec.habMode = 2; break;
                case "Carbon world":
                    spec.distanceAU = 0.8f; spec.massEarth = 1.4f; spec.metallicity = 0.4f; spec.waterCoverage = 0.05f;
                    spec.theme = (int)PlanetTexture.ChemTheme.Carbonaceous; spec.habMode = 2; break;
                case "Eyeball (M dwarf)":
                    spec.starClass = SpectralClass.M; spec.starMassT = 0.75f; spec.distanceAU = 0.11f; spec.massEarth = 1.2f;
                    spec.lockMode = 1; spec.waterCoverage = 0.6f; spec.habMode = 1; break;
                case "Tatooine":
                    spec.binary = true; spec.companionSepAU = 0.22f; spec.companionMassRatio = 0.7f; spec.distanceAU = 1.6f;
                    spec.type = PlanetType.Desert; spec.waterCoverage = 0.02f; spec.theme = (int)PlanetTexture.ChemTheme.Evaporite; spec.habMode = 2; break;
                case "Hot Jupiter": spec.type = PlanetType.GasGiant; spec.massEarth = 300f; spec.distanceAU = 0.05f; spec.lockMode = 1; break;
                case "Jupiter": spec.type = PlanetType.GasGiant; spec.massEarth = 318f; spec.distanceAU = 5.2f; spec.rotationHours = 9.9f; spec.axialTiltDeg = 3f; spec.moons = 4; break;
                case "Ice giant": spec.type = PlanetType.IceGiant; spec.massEarth = 17f; spec.distanceAU = 30f; spec.rotationHours = 16f; spec.axialTiltDeg = 28f; spec.moons = 2; break;
            }
        }

        void OnDestroy() { if (_inst == this) { _inst = null; SystemViewer.ExtraUiRect = Rect.zero; } }
    }
}
