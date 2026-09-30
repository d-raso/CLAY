using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using CLAY.Galaxy;

namespace CLAY.Surface
{
    /// <summary>
    /// The on-foot planet surface. Built exactly like the (proven) Flora Lab: objects live in the CURRENT scene on
    /// a dedicated layer while the galaxy + system stay alive and suspended underneath — no rebuild on return.
    /// The main camera is borrowed (3D renderer for real shadows, culling only the surface layer) and the scene's
    /// fog/ambient/sun settings are saved on landing and restored on takeoff. (A separate runtime scene rendered
    /// nothing with this camera setup, so it's not used.)
    ///
    /// Controls: RMB-drag look · WASD move · Space/E up · C/Q down · Shift fast · Ctrl slow · scroll = fly speed ·
    /// F walk/fly · [ ] time of day · H hide HUD · Esc take off.
    /// </summary>
    public class SurfaceWorld : MonoBehaviour
    {
        public const int SurfaceLayer = 31;   // same layer the Flora Lab renders on (the two are never open together)
        const int Renderer3D = 1;

        static SurfaceWorld _inst; static int _suppressUntil = -1;
        public static bool Active => _inst != null || Time.frameCount <= _suppressUntil;

        SystemViewer viewer; PlanetData planet; ulong pSeed;
        // saved scene render settings (restored on takeoff)
        bool rsFog; FogMode rsFogMode; Color rsFogColor; float rsFogDensity; AmbientMode rsAmbMode;
        Color rsAmbSky, rsAmbEq, rsAmbGround, rsAmbLight; SphericalHarmonicsL2 rsProbe; Light rsSun;
        SurfaceGeo geo; SurfaceTerrain terrain; SurfaceEcology ecology;
        GameObject root, sunGo, water, skyGo; Light sun; Material terrainMat, waterMat, skyMat;
        bool cursorFree;
        SurfaceRocks rocks;
        SurfaceGrass grass;
        SurfaceWeather weather;
        // the whole planet as a globe beneath the local terrain (for flying high)
        GameObject globe; Material globeMat; System.Threading.Tasks.Task<GlobeData> globeTask;
        struct GlobeData { public Vector3[] v; public Color32[] c; public int[] t; }
        float lastAlt;
        Camera cam;

        // floating origin: Unity (0,0,0) sits at world (ox, 0, oz) metres in the landing frame
        double ox, oz;
        float yaw, pitch, flySpeed = 25f, hourAngle = -40f;
        bool walk = true, hideHud;
        Vector2 lastMouse; bool haveMouse;
        readonly List<SurfaceEcology.Species> here = new();
        LocalClimate hereClimate; float hereElev; float hudTimer; float sunElevDeg;
        bool showDiag = true, curvatureOff;
        float shotTimer; bool autoShotDone;

        void TakeShot()
        {
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "SurfaceShots");
            System.IO.Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, $"surface_{System.DateTime.Now:HHmmss}.png");
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log($"[Surface] screenshot → {file}");
        }

        // saved camera / pipeline state
        CameraClearFlags sClear; Color sBg; float sNear, sFar, sFov; Vector3 sPos; Quaternion sRot; int sMask, sRenderer;
        bool sShadows, sPost; float sShadowDist, sRenderScale = 1f; int sCascades = 1, sShadowRes = 2048; int sMsaa = 1;
        readonly List<Camera> silenced = new();   // other cameras disabled while on the surface (restored on exit)
        GameObject marker;

        // day cycle: the star's direction in the planet's body frame at landing, rotated back by the accumulated spin
        Vector3 sunPlanet0 = Vector3.forward; float spinDeg, spinSign = 1f; bool haveSun;

        /// Everything the system view knows at the moment of landing that the ground should agree with.
        public sealed class SkyBody
        {
            public Vector3 dir;          // direction in the landed planet's BODY frame
            public float angRadius;      // radians, as seen from the landing point
            public Texture tex; public Color tint = Color.white; public bool emissive;
            public PlanetData planet; public ulong seed;   // to bake a sharp map of it after landing
            public Material ownMat;                        // gas giants: a copy of their system-view material
        }
        public sealed class LandingContext
        {
            public Vector3 sunLocal;                 // toward the star, body frame
            public float viewerSpin;
            public Color[] orbPx; public int orbW, orbH;   // the exact map painted on the planet in the system view
            public readonly List<SkyBody> bodies = new();
            public Cubemap starCube;                 // the system view's star background (viewer world frame)
            public Quaternion bodyToWorld = Quaternion.identity;
            public float sun1TempK;                  // the brightest star (0 → planet.hostStarTempK)
            public Vector3 sun2Local;                // second sun, body frame (zero = none)
            public float sun2Weight, sun2TempK, sun2SizeRel;
        }
        LandingContext ctx; long tTerrain;
        System.Threading.Tasks.Task<SurfaceHydrology> hydroTask; string terrainTypesLabel = "";
        Volume postVol; VolumeProfile postProfile; LayerMask sVolMask;
        ScriptableRendererFeature ssao;
        CameraOverrideOption sDepthOpt, sColorOpt;
        GameObject sun2Go; Light sun2; float sun2Elev = -1f;
        readonly List<GameObject> skyBodyGos = new();
        readonly List<Material> skyBodyMats = new();
        readonly List<System.Threading.Tasks.Task<PlanetTexture.SurfacePixels>> skyBodyBakes = new();
        readonly List<Texture2D> skyBodyTex = new();

        public static void Enter(SystemViewer viewer, PlanetData p, ulong systemSeed, ulong planetSeed, Vector3 landingDir,
                                 LandingContext lc)
        {
            if (_inst != null || p == null) return;
            var go = new GameObject("SurfaceWorld");
            _inst = go.AddComponent<SurfaceWorld>();
            _inst.ctx = lc ?? new LandingContext();
            if (_inst.ctx.sunLocal.sqrMagnitude > 1e-6f) { _inst.sunPlanet0 = _inst.ctx.sunLocal.normalized; _inst.haveSun = true; }
            _inst.spinSign = _inst.ctx.viewerSpin < 0f ? -1f : 1f;
            _inst.hourAngle = 0f;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log($"[Surface] landing on {p.name} ({p.type}, r={p.radiusEarth:0.00} R⊕, {p.meanTempC:0}°C) dir={landingDir}");
            try { _inst.Init(viewer, p, systemSeed, planetSeed, landingDir); }
            catch (System.Exception ex) { _inst.Fail("landing", ex); }
            Debug.Log($"[Surface] landing finished in {sw.ElapsedMilliseconds} ms");
        }

        // Errors are shown on the HUD (and the first few logged) instead of silently freezing the scene.
        readonly List<string> errors = new();
        void Fail(string where, System.Exception ex)
        {
            string msg = $"{where}: {ex.GetType().Name}: {ex.Message}";
            if (!errors.Contains(msg)) { errors.Add(msg); Debug.LogException(ex); }
        }

        void Init(SystemViewer v, PlanetData p, ulong systemSeed, ulong planetSeed, Vector3 landingDir)
        {
            viewer = v; planet = p; pSeed = planetSeed;
            if (viewer) viewer.suspended = true;
            cam = Camera.main;
            if (cam == null) throw new System.Exception("No Camera.main (tag the camera MainCamera)");

            rsFog = RenderSettings.fog; rsFogMode = RenderSettings.fogMode; rsFogColor = RenderSettings.fogColor;
            rsFogDensity = RenderSettings.fogDensity; rsAmbMode = RenderSettings.ambientMode;
            rsAmbSky = RenderSettings.ambientSkyColor; rsAmbEq = RenderSettings.ambientEquatorColor;
            rsAmbGround = RenderSettings.ambientGroundColor; rsAmbLight = RenderSettings.ambientLight;
            rsProbe = RenderSettings.ambientProbe; rsSun = RenderSettings.sun;

            TerrainTextures.Ensure();   // bake (once per session) the ground texture sets on worker threads
            geo = new SurfaceGeo(p, planetSeed, landingDir);
            if (ctx.orbPx != null) geo.SetOrbitalMap(ctx.orbPx, ctx.orbW, ctx.orbH);
            var psw = System.Diagnostics.Stopwatch.StartNew();
            geo.BuildPalette(planetSeed);
            var ids = geo.Palette;
            Shader.SetGlobalVector("_TypeIdA", new Vector4(ids[0], ids[1], ids[2], ids[3]));
            Shader.SetGlobalVector("_TypeIdB", new Vector4(ids[4], ids[5], ids[6], ids[7]));
            var names = new List<string>(); foreach (int t in ids) if (t >= 0) names.Add(((SurfaceGeo.TerrainType)t).ToString());
            terrainTypesLabel = string.Join(", ", names);
            Debug.Log($"[Surface] terrain palette ({psw.ElapsedMilliseconds} ms): {terrainTypesLabel}");
            // RIVERS & LAKES: the drainage network needs ~100k height samples, so it's built in the background; the
            // terrain (and everything scattered on it) re-builds in place when it lands. Liquid rivers need something to
            // flow; dry worlds with air still get carved arroyos; airless worlds get none.
            {
                var rpal = geo.s.Palette;
                bool meth = rpal.theme == PlanetTexture.ChemTheme.Methanic || rpal.theme == PlanetTexture.ChemTheme.Tholin;
                Color rc = meth ? new Color(0.22f, 0.12f, 0.04f) : Color.Lerp(rpal.oceanDeep, rpal.oceanShallow, 0.35f).linear * 0.7f;
                Shader.SetGlobalVector("_RiverColor", new Vector4(rc.r, rc.g, rc.b, 1f));
            }
            if (geo.s.HasAtmo)
            {
                bool wetRivers = geo.hasSea || geo.volatiles;
                var hg = geo;
                hydroTask = System.Threading.Tasks.Task.Run(() => SurfaceHydrology.Build(hg, wetRivers));
            }
            Shader.SetGlobalFloat("_CurvK", (float)(1.0 / (2.0 * geo.R)));

            // camera + pipeline
            sClear = cam.clearFlags; sBg = cam.backgroundColor; sNear = cam.nearClipPlane; sFar = cam.farClipPlane; sFov = cam.fieldOfView;
            sPos = cam.transform.position; sRot = cam.transform.rotation; sMask = cam.cullingMask;
            var cd = cam.GetUniversalAdditionalCameraData();
            sShadows = cd.renderShadows; sRenderer = GetRendererIndex(cd); sPost = cd.renderPostProcessing;
            cd.SetRenderer(Renderer3D); cd.renderShadows = true;
            // The galaxy's post stack (strong bloom/exposure tuned to make faint stars glow) washes a daylit
            // landscape out to white — and spreads any bad pixel across the screen. Off while on the surface.
            // The galaxy's own post stack (heavy bloom for faint stars) must not reach the surface, so the camera only
            // listens to volumes on the SURFACE layer: a filmic grade of its own (ACES tone mapping, gentle bloom for
            // bright sky / glints / sun, mild contrast, a whisper of vignette).
            sVolMask = cd.volumeLayerMask;
            sDepthOpt = cd.requiresDepthOption; sColorOpt = cd.requiresColorOption;
            cd.requiresDepthOption = CameraOverrideOption.On; cd.requiresColorOption = CameraOverrideOption.On;   // water refraction
            cd.volumeLayerMask = 1 << SurfaceLayer;
            cd.renderPostProcessing = true;
            var vgo = new GameObject("SurfacePost") { layer = SurfaceLayer };
            postVol = vgo.AddComponent<Volume>(); postVol.isGlobal = true; postVol.priority = 100f;
            postProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = postProfile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var bl = postProfile.Add<Bloom>(true); bl.threshold.Override(1.15f); bl.intensity.Override(0.3f); bl.scatter.Override(0.65f);
            var ca = postProfile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.25f); ca.contrast.Override(12f); ca.saturation.Override(-3f);
            var vg = postProfile.Add<Vignette>(true); vg.intensity.Override(0.16f); vg.smoothness.Override(0.5f);
            postVol.sharedProfile = postProfile;
            // SSAO lives (inactive) on the 3D renderer asset; the surface switches it on while it owns the camera
            ssao = SSAO.Find(); SSAO.SetActive(ssao, true);
            var urp = UniversalRenderPipeline.asset;
            if (urp) { sShadowDist = urp.shadowDistance; urp.shadowDistance = 350f; sRenderScale = urp.renderScale; urp.renderScale = 1f;
                   sCascades = urp.shadowCascadeCount; urp.shadowCascadeCount = 4; urp.shadowDistance = 220f;
                   sShadowRes = urp.mainLightShadowmapResolution; urp.mainLightShadowmapResolution = 4096; }
            cam.cullingMask = 1 << SurfaceLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = 60f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 150000f;
            cam.enabled = true;
            // NOTE: other cameras are deliberately left alone. Toggling them made the galaxy re-upload its entire
            // 2M-star GPU buffer (~290 MB) in one go, overflowing D3D12's upload heap → editor-wide flashing.

            root = new GameObject("SurfaceRoot") { layer = SurfaceLayer };
            var tsh = Shader.Find("CLAY/SurfaceTerrain");
            if (tsh == null) throw new System.Exception("Shader 'CLAY/SurfaceTerrain' not found (did it compile?)");
            terrainMat = new Material(tsh);
            terrain = new SurfaceTerrain(geo, root.transform, terrainMat, SurfaceLayer);
            try { ecology = new SurfaceEcology(geo, p, planetSeed, systemSeed, SurfaceLayer); }
            catch (System.Exception ex) { Fail("flora catalogue", ex); }
            try { weather = new SurfaceWeather(geo, planetSeed, SurfaceLayer); }
            catch (System.Exception ex) { Fail("weather", ex); }
            try { grass = new SurfaceGrass(geo, planetSeed, SurfaceLayer); }
            catch (System.Exception ex) { Fail("grass", ex); }
            try { rocks = new SurfaceRocks(geo, planetSeed, SurfaceLayer); }
            catch (System.Exception ex) { Fail("rocks", ex); }
            var gGeo = geo;
            globeTask = System.Threading.Tasks.Task.Run(() => BuildGlobe(gGeo));

            // spawn on land near where you aimed from orbit (spiral search outward)
            double sx = 0, sz = 0; bool found = false;
            for (int ring = 0; ring < 40 && !found; ring++)
            {
                double rad = ring * ring * 300.0;   // spirals out to ~470 km (inside the terrain area) to find land
                int n = ring == 0 ? 1 : 12 + ring * 2;
                for (int k = 0; k < n; k++)
                {
                    double a = k * 2.0 * System.Math.PI / n;
                    double x = System.Math.Cos(a) * rad, z = System.Math.Sin(a) * rad;
                    var smp = geo.At(x, z);
                    // with no sea, every point is dry land (basins can sit far below the "sea level" datum)
                    if (!smp.underwater && (!geo.hasSea || smp.heightM > 1f)) { sx = x; sz = z; found = true; break; }
                }
            }
            ox = sx; oz = sz;
            float ground = geo.At(sx, sz).heightM;
            walk = found;
            cam.transform.position = new Vector3(0f, Mathf.Max(ground, 0f) + (walk ? 1.7f : 40f), 0f);
            yaw = 0f; pitch = 5f;
            Debug.Log($"[Surface] spawn found={found} at ({sx:0},{sz:0}) ground={ground:0.0} m · cam={cam.transform.position} · " +
                      $"heightScale={geo.heightScale:0} R={geo.R / 1000.0:0} km sea={geo.hasSea} · landforms m{geo.mountains:0.00} v{geo.valleys:0.00} " +
                      $"mesa{geo.mesas:0.00} dune{geo.dunes:0.00} crater{geo.craters:0.00} · species={(ecology != null ? ecology.species.Count : -1)}");
            var swt = System.Diagnostics.Stopwatch.StartNew();
            try { terrain.BuildNow(ox, oz); }   // immediate first tile (and a real stack trace if tiles can't build)
            catch (System.Exception ex) { Fail("terrain tile", ex); }
            Debug.Log($"[Surface] first tile built in {swt.ElapsedMilliseconds} ms");

            // sun
            sunGo = new GameObject("Sun") { layer = SurfaceLayer };
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.85f;
            sun.cullingMask = 1 << SurfaceLayer;
            RenderSettings.sun = sun;   // the surface scene is active → its sun is URP's main light
            if (ctx.sun2Local.sqrMagnitude > 1e-6f && ctx.sun2Weight > 0.001f)
            {
                // a second star: an additional (unshadowed) directional light + its own disc in the sky
                sun2Go = new GameObject("Sun2") { layer = SurfaceLayer };
                sun2 = sun2Go.AddComponent<Light>();
                sun2.type = LightType.Directional; sun2.shadows = LightShadows.None;
                sun2.cullingMask = 1 << SurfaceLayer;
                sun2.renderMode = LightRenderMode.ForcePixel;
            }

            // sky dome (camera-centred, inside the far plane, drawn behind everything)
            var skySh = Shader.Find("CLAY/SurfaceSky");
            if (skySh != null)
            {
                skyMat = new Material(skySh);
                skyGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(skyGo.GetComponent<Collider>());
                skyGo.name = "SkyDome"; skyGo.layer = SurfaceLayer;
                skyGo.transform.localScale = Vector3.one * 180000f;   // radius 90 km (< far clip 150 km)
                var smr = skyGo.GetComponent<MeshRenderer>();
                smr.sharedMaterial = skyMat; smr.shadowCastingMode = ShadowCastingMode.Off; smr.receiveShadows = false;
            }

            // capture the mouse for FPS-style looking (Tab frees it for the HUD)
            SetCursor(false);

            // sea
            if (geo.hasSea)
            {
                var pal = geo.s.Palette;
                waterMat = new Material(Shader.Find("CLAY/SurfaceWater"));
                SetupLiquid(pal);
                water = new GameObject("Sea") { layer = SurfaceLayer };
                water.transform.SetParent(root.transform, false);
                water.AddComponent<MeshFilter>().sharedMesh = WaterMesh(0, 80000f);
                var mr = water.AddComponent<MeshRenderer>(); mr.sharedMaterial = waterMat; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            ApplySkyAndSun();
        }

        // ── per frame ─────────────────────────────────────────────────────────────────────────────────────
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            TerrainTextures.Poll();
            if (geo == null || cam == null) { LogStatus("geo/cam missing — landing failed"); return; }
            LogStatus(null);
            if (Input.GetKeyDown(KeyCode.H)) hideHud = !hideHud;
            if (Input.GetKeyDown(KeyCode.F)) walk = !walk;
            if (Input.GetKeyDown(KeyCode.K)) ToggleMarker();
            if (Input.GetKeyDown(KeyCode.J)) showDiag = !showDiag;
            if (Input.GetKeyDown(KeyCode.M)) ToggleMenu();
            if (Input.GetKeyDown(KeyCode.U)) { SurfaceEcology.DebugPlainMaterial = !SurfaceEcology.DebugPlainMaterial; Debug.Log($"[Surface] plain plant material {(SurfaceEcology.DebugPlainMaterial ? "ON" : "OFF")}"); }
            // Screenshots for remote diagnosis: one automatically 5 s after landing, and F12 any time.
            shotTimer += Time.unscaledDeltaTime;
            if ((!autoShotDone && shotTimer > 5f) || Input.GetKeyDown(KeyCode.F12)) { autoShotDone = true; TakeShot(); }
            // Diagnostics: O hides the sky dome, P turns horizon curvature off — isolates what's blocking distant land.
            if (Input.GetKeyDown(KeyCode.O) && skyGo) { skyGo.SetActive(!skyGo.activeSelf); Debug.Log($"[Surface] sky dome {(skyGo.activeSelf ? "ON" : "OFF")}"); }
            if (Input.GetKeyDown(KeyCode.P))
            {
                curvatureOff = !curvatureOff;
                Shader.SetGlobalFloat("_CurvK", curvatureOff ? 0f : (float)(1.0 / (2.0 * geo.R)));
                Debug.Log($"[Surface] curvature {(curvatureOff ? "OFF" : "ON")}");
            }
            if (Input.GetKey(KeyCode.LeftBracket)) hourAngle -= 20f * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.RightBracket)) hourAngle += 20f * Time.unscaledDeltaTime;

            try { Fly(); } catch (System.Exception ex) { Fail("camera", ex); }
            Vector3 cpos = cam.transform.position;
            if (float.IsNaN(cpos.x) || float.IsNaN(cpos.y) || float.IsNaN(cpos.z) || float.IsInfinity(cpos.y))
            {
                Fail("camera", new System.Exception($"camera position became invalid {cpos} — terrain height sampling returned NaN"));
                cam.transform.position = new Vector3(0f, 200f, 0f);
            }

            // floating origin: keep the camera near Unity's origin for float precision
            Vector3 p = cam.transform.position;
            if (Mathf.Abs(p.x) > 1500f || Mathf.Abs(p.z) > 1500f)
            {
                ox += p.x; oz += p.z;
                cam.transform.position = new Vector3(0f, p.y, 0f);
                p = cam.transform.position;
            }
            double wx = ox + p.x, wz = oz + p.z;
            Shader.SetGlobalVector("_SurfOrigin", new Vector4((float)(ox % 65536.0), (float)(oz % 65536.0), 0f, 0f));
            float ground = geo.At(wx, wz).heightM;
            float alt = Mathf.Max(1f, p.y - (geo.hasSea ? Mathf.Max(ground, 0f) : ground));

            // each subsystem isolated: one failing must not freeze the others
            var tsw = System.Diagnostics.Stopwatch.StartNew();
            if (terrain != null) try { terrain.Update(wx, alt, wz, ox, oz); } catch (System.Exception ex) { Fail("terrain", ex); }
            tTerrain = tsw.ElapsedMilliseconds;
            lastAlt = alt;
            try { UpdateGlobe(); } catch (System.Exception ex) { Fail("globe", ex); }
            if (hydroTask != null && hydroTask.IsCompleted)
            {
                if (hydroTask.IsFaulted) Fail("rivers", hydroTask.Exception.GetBaseException());
                else
                {
                    geo.hydro = hydroTask.Result;
                    if (terrain != null) terrain.Version++;
                    ecology?.Invalidate(); rocks?.Invalidate(); grass?.Invalidate();
                    Debug.Log($"[Surface] drainage ready: {geo.hydro.RiverCount} river segments, {geo.hydro.LakeCells} lake cells ({(geo.hydro.wet ? "flowing" : "dry arroyos")})");
                }
                hydroTask = null;
            }
            if (water) water.transform.position = new Vector3(p.x, 0f, p.z);   // the radial sea mesh is centred on the camera
            bool tBusy = terrain == null || !terrain.AnyReady || terrain.PendingCount > 2;
            if (rocks != null)
            {
                try { rocks.Update(wx, wz, tBusy); } catch (System.Exception ex) { Fail("rocks update", ex); }
                if (alt < 2000f) try { rocks.Render(cam, ox, oz); } catch (System.Exception ex) { Fail("rocks render", ex); }
            }
            if (weather != null) try { weather.Update(Time.unscaledDeltaTime, cam.transform.position, hereClimate, sunElevDeg); } catch (System.Exception ex) { Fail("weather", ex); }
            if (grass != null && grass.enabled)
            {
                try { grass.Update(wx, wz, tBusy); } catch (System.Exception ex) { Fail("grass update", ex); }
                if (alt < 250f) try { grass.Render(cam, ox, oz); } catch (System.Exception ex) { Fail("grass render", ex); }
            }
            if (ecology != null)
            {
                bool terrainBusy = tBusy;
                try { ecology.Update(wx, wz, terrainBusy); } catch (System.Exception ex) { Fail("flora update", ex); }
                try { ecology.Render(cam, ox, oz); } catch (System.Exception ex) { Fail("flora render", ex); }
            }
            // Enforce the camera every frame: on the landing frame the system viewer finishes ITS camera update after
            // our Init (overwriting position/far plane — far=0 rendered nothing, far=1000 made a sky-coloured "wall"),
            // and the system HUD's LateUpdate touches the FOV. The surface owns the camera while it's active.
            // draw range grows with altitude out to the true horizon (and the globe beyond the terrain area)
            float horizon = Mathf.Sqrt((float)(2.0 * geo.R * alt) + alt * alt);
            cam.farClipPlane = Mathf.Clamp(horizon * 1.25f + 30000f, 150000f, (float)(geo.R * 3.0));
            cam.nearClipPlane = Mathf.Clamp(alt * 0.004f, 0.15f, Mathf.Max(60f, cam.farClipPlane * 2e-6f));
            cam.fieldOfView = 60f;
            cam.cullingMask = 1 << SurfaceLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;

            try { if (sun) ApplySkyAndSun(); } catch (System.Exception ex) { Fail("sky", ex); }

            hudTimer -= Time.unscaledDeltaTime;
            if (hudTimer <= 0f)
            {
                hudTimer = 0.4f;
                var sm = geo.At(wx, wz);
                hereElev = sm.heightM;
                hereClimate = geo.ClimateAt(sm);
                ecology?.SpeciesHere(hereClimate, here);
            }
        }

        void Fly()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) SetCursor(!cursorFree);
            if (!cursorFree)
            {
                // captured cursor: the mouse steers the view directly (raw deltas, no smoothing)
                yaw += Input.GetAxisRaw("Mouse X") * 2.2f;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2.2f, -89f, 89f);
                haveMouse = false;
            }
            else
            {
                // free cursor (using the HUD): hold RMB to look
                Vector2 mp = Input.mousePosition;
                Vector2 md = haveMouse ? mp - lastMouse : Vector2.zero; lastMouse = mp; haveMouse = true;
                if (Input.GetMouseButton(1)) { yaw += md.x * 0.18f; pitch = Mathf.Clamp(pitch - md.y * 0.18f, -89f, 89f); }
            }
            float sc = Input.mouseScrollDelta.y;
            if (Mathf.Abs(sc) > 0.01f) flySpeed = Mathf.Clamp(flySpeed * Mathf.Pow(1.25f, sc), 1f, 250000f);

            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            // W/S always move horizontally (along the heading); only Space/C change height
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 mv = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) mv += fwd; if (Input.GetKey(KeyCode.S)) mv -= fwd;
            if (Input.GetKey(KeyCode.D)) mv += right; if (Input.GetKey(KeyCode.A)) mv -= right;
            if (!walk)
            {
                if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E)) mv += Vector3.up;
                if (Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.Q)) mv -= Vector3.up;
            }
            float spd = walk ? 4.5f : flySpeed;
            if (Input.GetKey(KeyCode.LeftShift)) spd *= walk ? 3f : 6f;
            if (Input.GetKey(KeyCode.LeftControl)) spd *= 0.2f;
            Vector3 pos = cam.transform.position + mv.normalized * spd * Time.unscaledDeltaTime;

            float ground = Mathf.Max(geo.At(ox + pos.x, oz + pos.z).heightM, geo.hasSea ? 0f : -1e6f);
            if (walk) pos.y = ground + 1.7f;
            else pos.y = Mathf.Max(pos.y, ground + 1.2f);
            cam.transform.position = pos;
        }

        // ── sun, sky, fog, ambient ────────────────────────────────────────────────────────────────────────
        void ApplySkyAndSun()
        {
            // Sun direction in the planet frame: a locked world keeps its star fixed at +Z (the orbital "eyeball"
            // convention); otherwise the star is in the equatorial plane at the chosen hour angle from the local
            // meridian (equinox, declination 0). Then express it in the local east/up/north frame.
            Vector3 sunPlanet;
            if (planet.tidallyLocked) sunPlanet = haveSun ? sunPlanet0 : Vector3.forward;
            else if (haveSun)
            {
                // one local hour passes per real minute; the ground turns +spin about the axis, so the sun turns back
                spinDeg += 360f / (Mathf.Max(planet.rotationHours, 0.5f) * 60f) * Time.unscaledDeltaTime * spinSign;
                sunPlanet = Quaternion.AngleAxis(-(spinDeg + hourAngle), Vector3.up) * sunPlanet0;
            }
            else
            {
                Vector3 c = geo.DirAt(ox, oz);
                float lon = Mathf.Atan2(c.z, c.x);
                float a = lon + hourAngle * Mathf.Deg2Rad;
                sunPlanet = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            Vector3 sunL = geo.ToLocal(sunPlanet).normalized;
            float elev = sunL.y;
            sunElevDeg = Mathf.Asin(Mathf.Clamp(elev, -1f, 1f)) * Mathf.Rad2Deg;
            sunGo.transform.rotation = Quaternion.LookRotation(-sunL, Vector3.up);

            var cl = geo.climate;
            bool air = cl.hasAtmosphere && cl.pressureBar > 0.01f;
            Color star = StarColor(ctx.sun1TempK > 0f ? ctx.sun1TempK : planet.hostStarTempK);
            float day = Mathf.Clamp01(elev * 4f + 0.15f);
            // low sun through thick air reddens (Rayleigh: blue scattered out along the long path)
            float path = air ? Mathf.Clamp01((1f - Mathf.Clamp01(elev * 3f)) * Mathf.Clamp01(cl.pressureBar)) : 0f;
            Color sunCol = Color.Lerp(star, star * new Color(1f, 0.55f, 0.3f), path * 0.8f);
            float lux = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(planet.insolation, 0.01f)), 0.3f, 1.6f);
            sun.color = sunCol;
            // fade the light out smoothly through sunset, keeping shadows on while it is still lit (turning shadows off
            // while the sun still had intensity lit every shaded slope at once — the nightfall flash)
            float sunFade = Mathf.Clamp01((elev + 0.01f) / 0.1f); sunFade = sunFade * sunFade * (3f - 2f * sunFade);
            sun.intensity = 1.15f * lux * sunFade * (weather != null ? weather.SunFactor : 1f);   // overcast dims the direct sun
            sun.shadows = sunFade > 0.001f ? LightShadows.Soft : LightShadows.None;
            // NEVER disable the sun: URP would promote another scene directional light (the system view's star) to
            // main light and floodlight the region at nightfall. Keep it on at zero intensity instead.
            sun.enabled = true;
            SSAO.SetIntensity(ssao, Mathf.Lerp(1.6f, 0.9f, Mathf.Clamp01(elev * 1.6f)));

            // SECOND SUN: same spin as the first; brightness relative to the main star (true flux ratio, compressed)
            Vector3 sun2L = Vector3.down; float day2 = 0f; Color sun2Col = Color.black;
            if (sun2)
            {
                Quaternion rot2 = planet.tidallyLocked || !haveSun ? Quaternion.identity : Quaternion.AngleAxis(-(spinDeg + hourAngle), Vector3.up);
                sun2L = geo.ToLocal(rot2 * ctx.sun2Local.normalized).normalized;
                sun2Elev = sun2L.y;
                float f2 = Mathf.Clamp01((sun2L.y + 0.01f) / 0.1f); f2 = f2 * f2 * (3f - 2f * f2);
                Color s2 = StarColor(ctx.sun2TempK);
                float path2 = air ? Mathf.Clamp01((1f - Mathf.Clamp01(sun2L.y * 3f)) * Mathf.Clamp01(cl.pressureBar)) : 0f;
                sun2Col = Color.Lerp(s2, s2 * new Color(1f, 0.55f, 0.3f), path2 * 0.8f);
                sun2.color = sun2Col;
                sun2.intensity = 1.15f * lux * ctx.sun2Weight * f2 * (weather != null ? weather.SunFactor : 1f);
                sun2Go.transform.rotation = Quaternion.LookRotation(-sun2L, Vector3.up);
                day2 = Mathf.Clamp01(sun2L.y * 4f + 0.15f) * ctx.sun2Weight;
                // the sky is lit by whichever sun is up
                day = Mathf.Max(day, day2);
            }

            // the rest of the sky turns with the same spin: other worlds, moons, the parent giant, the star background
            Quaternion skyRot = planet.tidallyLocked || !haveSun ? Quaternion.identity : Quaternion.AngleAxis(-(spinDeg + hourAngle), Vector3.up);
            {
                bool air0 = geo.climate.hasAtmosphere && geo.climate.pressureBar > 0.01f;
                float skyLum = air0 ? day * Mathf.Clamp01(geo.climate.pressureBar * 2f) * Mathf.Exp(-lastAlt / 15000f) : 0f;
                float transmit = air0 ? Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(geo.climate.pressureBar / 3f)) : 1f;
                UpdateSkyBodies(skyRot, sunL, sunCol, transmit, 1f - Mathf.Clamp01(skyLum * 1.5f));
            }
            if (skyMat && ctx.starCube)
            {
                Quaternion q = ctx.bodyToWorld * Quaternion.Inverse(skyRot);
                var m = Matrix4x4.identity;
                m.SetColumn(0, q * geo.FromLocal(Vector3.right));
                m.SetColumn(1, q * geo.FromLocal(Vector3.up));
                m.SetColumn(2, q * geo.FromLocal(Vector3.forward));
                m.SetColumn(3, new Vector4(0, 0, 0, 1));
                skyMat.SetMatrix("_StarRot", m);
                skyMat.SetTexture("_StarCube", ctx.starCube);
                skyMat.SetFloat("_UseCube", 1f);
            }

            Color atm = air ? PlanetTexture.AtmosphereColor(planet, pSeed) : Color.black;
            // the sky thins toward black space as you climb (≈ an 8–15 km atmospheric scale height)
            float atmo = air ? Mathf.Exp(-lastAlt / 15000f) : 0f;
            Color zenith = Color.Lerp(new Color(0.01f, 0.012f, 0.02f), atm * 0.85f, day * atmo * Mathf.Clamp01(cl.pressureBar * 1.4f));
            Color horizon = Color.Lerp(zenith, Color.Lerp(atm, sunCol, 0.35f) * 1.05f, day * 0.8f * Mathf.Sqrt(atmo));
            horizon = Color.Lerp(horizon, sunCol * 0.9f, path * 0.35f * day * atmo);
            if (air)
            {
                // physically based: the SAME scattering model as the sky shader, so fog + ambient match the sky exactly
                SetupScatter(cl, atm, atmo);
                float i1 = 1.15f * lux, i2 = sun2 ? 1.15f * lux * ctx.sun2Weight : 0f;
                Color z = SkyScatter(Vector3.up, sunL, star, i1) + (sun2 ? SkyScatter(Vector3.up, sun2L, StarColor(ctx.sun2TempK), i2) : Color.black);
                Color h = Color.black;
                for (int k = 0; k < 6; k++)
                {
                    float az = k * Mathf.PI / 3f; Vector3 hd = new Vector3(Mathf.Cos(az), 0.04f, Mathf.Sin(az)).normalized;
                    h += SkyScatter(hd, sunL, star, i1) + (sun2 ? SkyScatter(hd, sun2L, StarColor(ctx.sun2TempK), i2) : Color.black);
                }
                h /= 6f;
                Color floorC = new Color(0.008f, 0.01f, 0.016f);
                zenith = z + floorC; horizon = h + floorC;
                zenith.a = horizon.a = 1f;
            }
            cam.backgroundColor = horizon;

            // Aerial perspective: thin distance fog (scaled by air pressure & humidity) whose colour is EXACTLY the sky
            // dome's horizon colour, so terrain fades into the sky instead of into a flat wall.
            RenderSettings.fog = air;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = horizon;
            RenderSettings.fogDensity = air ? 2.2e-6f * Mathf.Pow(Mathf.Clamp(cl.pressureBar, 0.05f, 10f), 0.8f) * (1f + cl.humidity * 0.6f) * Mathf.Max(atmo, 0.02f) : 0f;
            if (weather != null && air)
            {
                RenderSettings.fogDensity *= weather.FogFactor;
                if (weather.Dust > 0.01f)
                {
                    Color dc = weather.DustColor * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(sunL.y * 3f + 0.2f));
                    RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, dc, Mathf.Clamp01(weather.Dust * 1.3f));
                }
                else if (weather.PrecipAmt > 0.02f)
                    RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, RenderSettings.fogColor * 0.75f + new Color(0.08f, 0.08f, 0.09f), weather.PrecipAmt * 0.6f);
            }
            // UNDERWATER: the liquid itself is the fog — its colour, and a density from how strongly it absorbs
            if (geo.hasSea && waterMat && cam.transform.position.y < 0f)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                float lightDown = Mathf.Clamp01(sunL.y + 0.2f);
                RenderSettings.fogColor = liquidDeep.gamma * (0.35f + 0.65f * lightDown);
                RenderSettings.fogDensity = (liquidAbsorb.x + liquidAbsorb.y + liquidAbsorb.z) / 3f * 0.35f;
                cam.backgroundColor = RenderSettings.fogColor;
            }
            if (skyMat)
            {
                skyGo.transform.position = cam.transform.position;
                skyMat.SetColor("_Zenith", zenith);
                skyMat.SetColor("_Horizon", horizon);
                // below the eye-level horizontal (where the ground curves away) there is only more sky — hazy under air,
                // plain space without it (a dim "ground" band there read as a wall)
                skyMat.SetColor("_Ground", air ? Color.Lerp(horizon, geo.s.Palette.landLow * day, 0.35f) : horizon);
                skyMat.SetColor("_SunColor", star);   // unreddened: the shader reddens it through the air itself
                skyMat.SetFloat("_Air", air ? 1f : 0f);
                skyMat.SetVector("_KR", scKR); skyMat.SetVector("_KM", scKM); skyMat.SetVector("_KA", scKA);
                skyMat.SetVector("_SunI", new Vector4(1.15f * lux, sun2 ? 1.15f * lux * ctx.sun2Weight : 0f, 0f, 0f));
                skyMat.SetFloat("_SkyGain", SkyGain);
                skyMat.SetVector("_SunDir", sunL);
                skyMat.SetVector("_Sun2Dir", sun2 ? new Vector4(sun2L.x, sun2L.y, sun2L.z, ctx.sun2Weight) : Vector4.zero);
                skyMat.SetColor("_Sun2Color", sun2 ? (air ? sun2Col : StarColor(ctx.sun2TempK)) : Color.black);
                skyMat.SetFloat("_Haze", air ? Mathf.Clamp(0.06f + cl.pressureBar * 0.1f, 0.06f, 0.6f) : 0.02f);
                skyMat.SetFloat("_Glow", air ? Mathf.Clamp(cl.pressureBar, 0.2f, 1.5f) : 0.15f);
                // Stars: always out on an airless world (even by day, like the Moon); under air they fade as the sky
                // brightens with daylight and thickness.
                float skyBright = air ? day * Mathf.Clamp01(cl.pressureBar * 2f) * atmo : 0f;
                skyMat.SetFloat("_Stars", Mathf.Clamp01(1f - skyBright * 1.3f));
                skyMat.SetVector("_BandDir", geo.ToLocal(new Vector3(0.35f, 0.55f, 0.76f).normalized));   // fixed galactic plane per planet frame
                // angular size of the star from its temperature proxy & distance (bigger + dimmer around M dwarfs)
                if (sun2) skyMat.SetFloat("_Sun2Size", Mathf.Clamp(0.004f * Mathf.Sqrt(Mathf.Max(planet.insolation, 0.05f)) * ctx.sun2SizeRel, 0.0006f, 0.03f));
                skyMat.SetFloat("_SunSize", Mathf.Clamp(0.004f * Mathf.Sqrt(Mathf.Max(planet.insolation, 0.05f)) * (planet.hostStarTempK < 4000f ? 2.5f : 1f), 0.0015f, 0.03f));
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // ambient with a night floor (starlight + eye adaptation) so the ground is always readable, never pitch black
            Color nightFloor = new Color(0.07f, 0.075f, 0.09f);
            RenderSettings.ambientSkyColor = zenith * 0.9f + nightFloor;
            RenderSettings.ambientEquatorColor = horizon * 0.6f + nightFloor * 0.8f;
            RenderSettings.ambientGroundColor = geo.s.Palette.landLow * 0.25f * day + nightFloor * 0.5f;
            // Push the same sky/horizon/ground gradient into the SH probe that URP's SampleSH() reads.
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(RenderSettings.ambientEquatorColor * 0.8f);
            sh.AddDirectionalLight(Vector3.up, RenderSettings.ambientSkyColor * 0.9f, 1f);
            sh.AddDirectionalLight(Vector3.down, RenderSettings.ambientGroundColor * 0.8f, 1f);
            // Shadowed ground is lit by the WHOLE sky dome and by sunlight bouncing off the surrounding terrain —
            // together ~15–25% of direct sun on a clear day. Without this, sun-facing dune flanks were lit and the lee
            // sides went pitch black.
            {
                float sunI = sun ? sun.intensity : 0f;
                Color sunC = sun ? sun.color : Color.black;
                Color bounce = sunC * Mathf.Lerp(0.35f, 0.55f, Lum(geo.s.Palette.landMid)) * 0.18f * sunI;   // off the ground
                Color skyFill = Color.Lerp(sunC, RenderSettings.ambientSkyColor, 0.6f) * 0.12f * sunI;
                sh.AddAmbientLight(skyFill);
                sh.AddDirectionalLight(Vector3.down, bounce, 1f);
                sh.AddDirectionalLight(new Vector3(-sun.transform.forward.x, 0.15f, -sun.transform.forward.z).normalized, bounce * 0.6f, 1f);
            }
            RenderSettings.ambientProbe = sh;
            if (waterMat) { waterMat.SetColor("_SkyZenith", zenith); waterMat.SetColor("_SkyHorizon", horizon); }
        }

        // Periodic status to the Editor log (so problems can be diagnosed from the log without screenshots).
        float logTimer; int frames; float frameAccum;
        void LogStatus(string note)
        {
            frames++; frameAccum += Time.unscaledDeltaTime;
            logTimer -= Time.unscaledDeltaTime;
            if (logTimer > 0f) return;
            logTimer = 2f;
            string s = note ?? "";
            if (cam != null && geo != null)
            {
                Vector3 p = cam.transform.position;
                float g = geo.At(ox + p.x, oz + p.z).heightM;
                s += $"cam=({p.x:0.0},{p.y:0.0},{p.z:0.0}) world=({(ox + p.x) / 1000.0:0.000},{(oz + p.z) / 1000.0:0.000}) km ground={g:0.0} alt={p.y - Mathf.Max(g, 0f):0.0} " +
                     $"yaw={yaw:0} pitch={pitch:0} walk={walk} cursorFree={cursorFree} ";
            }
            if (terrain != null) s += $"tiles ready={terrain.ReadyCount} pending={terrain.PendingCount} tracked={terrain.ChunkCount} failed={terrain.Failures} ";
            s += $"sun={sunElevDeg:0}° int={(sun ? sun.intensity : 0f):0.00} bg={cam?.backgroundColor} fog={(RenderSettings.fog ? RenderSettings.fogDensity : 0f):0.0e0} ";
            if (ecology != null) s += $"plants={ecology.InstanceCount} ";
            if (grass != null && grass.enabled) s += $"grass={grass.Count} ";
            if (geo != null && cam != null)
            {
                var fs = geo.At(ox + cam.transform.position.x, oz + cam.transform.position.z);
                Color oc = geo.Orbital(fs, 0.2f);
                Color gc = geo.Ground(fs, geo.ClimateAt(fs), 0.1f, oc, out Vector4 mw, out Vector4 mw2);
                s += $"orbital=#{ColorUtility.ToHtmlStringRGB(oc)} ground=#{ColorUtility.ToHtmlStringRGB(gc)} types({FeetTypes(mw, mw2)}) map={(geo.HasMap ? "yes" : "NO")} ";
            }
            s += $"frame={(frames > 0 ? frameAccum / frames * 1000f : 0f):0.0} ms (terrain.Update {tTerrain} ms, jobs {terrain?.PendingCount}) errors={errors.Count}";
            frames = 0; frameAccum = 0f;
            Debug.Log("[Surface] " + s);
        }

        // The whole planet, built off-thread from the SAME orbital height & colour fields, as a true sphere expressed in
        // the landing frame (centre at (0,−R,0)). It sits a little below the local terrain, so up close the detailed
        // tiles cover it; from high altitude it fills in everything past the terrain area, out to the real horizon.
        static GlobeData BuildGlobe(SurfaceGeo geo)
        {
            int lon = 256, lat = 128;
            var v = new Vector3[(lon + 1) * (lat + 1)]; var c = new Color32[v.Length];
            double R = geo.R;
            float sink = 1500f * geo.reliefG;
            for (int i = 0; i <= lat; i++)
            {
                float a = Mathf.PI * 0.5f - i / (float)lat * Mathf.PI;
                for (int j = 0; j <= lon; j++)
                {
                    float b = j / (float)lon * Mathf.PI * 2f - Mathf.PI;
                    Vector3 d = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(b));
                    float h01 = geo.s.Height01(d);
                    float hm = (h01 - geo.s.SeaLevel) * geo.heightScale;
                    if (geo.hasSea && hm < 0f) hm = 0f;                            // sea surface
                    double r = R + hm - sink;
                    Vector3 l = geo.ToLocal(d);
                    v[i * (lon + 1) + j] = new Vector3((float)(l.x * r), (float)(l.y * r - R), (float)(l.z * r));
                    c[i * (lon + 1) + j] = geo.s.Albedo(d, h01, 0.2f);
                }
            }
            var t = new int[lat * lon * 6]; int k = 0;
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int p0 = i * (lon + 1) + j, p1 = p0 + 1, p2 = p0 + lon + 1, p3 = p2 + 1;
                    t[k++] = p0; t[k++] = p1; t[k++] = p2; t[k++] = p1; t[k++] = p3; t[k++] = p2;
                }
            return new GlobeData { v = v, c = c, t = t };
        }

        void UpdateGlobe()
        {
            if (globe == null && globeTask != null && globeTask.IsCompleted)
            {
                if (globeTask.IsFaulted) { Fail("globe", globeTask.Exception.GetBaseException()); globeTask = null; return; }
                var d = globeTask.Result; globeTask = null;
                var m = new Mesh { name = "PlanetGlobe", indexFormat = IndexFormat.UInt32 };
                m.SetVertices(d.v); m.SetColors(d.c); m.SetTriangles(d.t, 0);
                m.RecalculateNormals();
                // normals must face outward from the planet centre regardless of winding
                var nv = m.normals; var vv = m.vertices;
                for (int i = 0; i < nv.Length; i++) { Vector3 o = vv[i] - new Vector3(0f, -(float)geo.R, 0f); if (Vector3.Dot(nv[i], o) < 0f) nv[i] = -nv[i]; }
                m.normals = nv;
                m.bounds = new Bounds(new Vector3(0f, -(float)geo.R, 0f), Vector3.one * (float)(geo.R * 2.2));
                globeMat = new Material(Shader.Find("CLAY/SurfaceTerrain"));
                globeMat.SetFloat("_NoCurve", 1f); globeMat.SetFloat("_Grain", 0f);
                globe = new GameObject("PlanetGlobe") { layer = SurfaceLayer };
                globe.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = globe.AddComponent<MeshRenderer>();
                mr.sharedMaterial = globeMat; mr.shadowCastingMode = ShadowCastingMode.Off;
                Debug.Log("[Surface] planet globe ready");
            }
            if (globe)
            {
                globe.transform.position = new Vector3((float)-ox, 0f, (float)-oz);
                // only needed once the horizon reaches past the terrain area (and would poke through deep valleys below)
                globe.SetActive(lastAlt > 12000f);
            }
        }

        string FeetTypes(Vector4 a, Vector4 b)
        {
            var ids = geo.Palette; if (ids == null) return "";
            var parts = new List<string>();
            for (int k = 0; k < 8; k++)
            {
                float w = k < 4 ? a[k] : b[k - 4];
                if (w > 0.05f && ids[k] >= 0) parts.Add($"{(SurfaceGeo.TerrainType)ids[k]} {w:0.00}");
            }
            return string.Join(" ", parts);
        }

        static float Lum(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        // ── atmosphere scattering (mirrors SurfaceSky.shader) ──
        const float SkyGain = 14f;
        Vector4 scKR, scKM, scKA;
        void SetupScatter(PlanetClimate cl, Color atm, float atmo)
        {
            float P = Mathf.Clamp(cl.pressureBar, 0f, 30f) * atmo;
            // Rayleigh (λ⁻⁴, like Earth's N₂/O₂ at 1 bar) scales with the column of gas
            Vector3 kR = new Vector3(0.04f, 0.10f, 0.24f) * P;
            // aerosols / photochemical haze: tholin & methane worlds are haze-dominated (Titan's orange murk)
            var theme = geo.s.Palette.theme;
            float haze = (theme == PlanetTexture.ChemTheme.Tholin || theme == PlanetTexture.ChemTheme.Methanic) ? 1f
                       : Mathf.Clamp01((cl.pressureBar - 1.5f) * 0.25f) + Mathf.Clamp01(cl.humidity) * 0.15f;
            float mx = Mathf.Max(atm.r, Mathf.Max(atm.g, atm.b), 0.05f);
            Vector3 an = new Vector3(atm.r / mx, atm.g / mx, atm.b / mx);
            float kMd = (0.02f + haze * 0.35f) * Mathf.Min(P, 3f);
            Vector3 kA = (Vector3.one - an) * haze * 0.45f * Mathf.Min(P, 3f);   // haze absorbs the colours it isn't
            scKR = new Vector4(kR.x, kR.y, kR.z, 0f);
            scKM = new Vector4(an.x, an.y, an.z, kMd);
            scKA = new Vector4(kA.x, kA.y, kA.z, 0f);
        }
        static float AirMass(float y)
        {
            float z = Mathf.Acos(Mathf.Clamp(y, -0.05f, 1f)) * Mathf.Rad2Deg;
            return 1f / (Mathf.Max(y, -0.05f) + 0.50572f * Mathf.Pow(Mathf.Max(96.07995f - z, 0.5f), -1.6364f));
        }
        Color SkyScatter(Vector3 d, Vector3 sd, Color sunC, float intensity)
        {
            float mu = Vector3.Dot(d, sd);
            float phR = 0.0596831f * (1f + mu * mu);
            const float g = 0.76f;
            float phM = 0.0795775f * (1f - g * g) / Mathf.Pow(Mathf.Max(1f + g * g - 2f * g * mu, 1e-4f), 1.5f);
            float amV = AirMass(d.y), amS = AirMass(sd.y);
            float day = Mathf.Clamp01((sd.y + 0.12f) / 0.14f); day = day * day * (3f - 2f * day);
            float[] o = new float[3];
            for (int c = 0; c < 3; c++)
            {
                float kr = scKR[c], km = scKM.w * scKM[c], ka = scKA[c];
                float kt = kr + scKM.w + ka;
                float viewT = Mathf.Exp(-kt * amV), sunT = Mathf.Exp(-kt * amS);
                o[c] = sunC[c] * intensity * sunT * (kr * phR + km * phM) / Mathf.Max(kt, 1e-4f) * (1f - viewT) * SkyGain * day;
            }
            return new Color(o[0], o[1], o[2], 1f);
        }

        // Other bodies of the system (moons, the parent giant, sibling worlds) as lit spheres at a fixed sky distance,
        // sized to their angular size from the landing point. Lit by the surface sun, so they show real phases.
        void UpdateSkyBodies(Quaternion skyRot, Vector3 sunL, Color sunCol, float transmit, float occlude)
        {
            if (ctx == null || ctx.bodies.Count == 0) return;
            const float D = 70000f;
            if (skyBodyGos.Count == 0)
            {
                var sh = Shader.Find("CLAY/SurfaceSkyBody");
                foreach (var b in ctx.bodies)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(go.GetComponent<Collider>());
                    go.name = "SkyBody"; go.layer = SurfaceLayer;
                    var m = b.ownMat != null ? b.ownMat : new Material(sh);
                    if (b.ownMat == null)
                    {
                        if (b.tex) m.SetTexture("_BaseMap", b.tex);
                        m.SetColor("_BaseColor", b.tint);
                    }
                    var mr = go.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                    skyBodyGos.Add(go); skyBodyMats.Add(m);
                    var bp = b.planet; ulong bs = b.seed;
                    skyBodyBakes.Add(bp != null ? System.Threading.Tasks.Task.Run(() => PlanetTexture.ComputeSurface(bp, bs, 512)) : null);
                }
            }
            for (int i = 0; i < ctx.bodies.Count; i++)
            {
                var b = ctx.bodies[i]; var go = skyBodyGos[i];
                var bk = skyBodyBakes[i];
                if (bk != null && bk.IsCompleted)
                {
                    skyBodyBakes[i] = null;
                    if (!bk.IsFaulted)
                    {
                        var tx = PlanetTexture.ToTexture(bk.Result, "SkyBodyMap");
                        skyBodyTex.Add(tx); skyBodyMats[i].SetTexture("_BaseMap", tx); skyBodyMats[i].SetColor("_BaseColor", Color.white);
                    }
                }
                Vector3 l = geo.ToLocal(skyRot * b.dir).normalized;
                go.transform.position = cam.transform.position + l * D;
                go.transform.localScale = Vector3.one * (2f * D * Mathf.Tan(b.angRadius));
                go.SetActive(l.y > -0.2f);
                var bm = skyBodyMats[i];
                if (b.ownMat != null)
                {
                    // the giant's own shader lights in OBJECT space: sun + camera in the sky sphere's frame; the sky's
                    // brightness lifts its night side (the atmosphere in front of it scatters light)
                    bm.SetVector("_SunDir", go.transform.InverseTransformDirection(sunL).normalized);
                    bm.SetVector("_CamPosObj", go.transform.InverseTransformPoint(cam.transform.position));
                    bm.SetFloat("_Ambient", Mathf.Lerp(0.04f, 0.5f, 1f - occlude));
                    continue;
                }
                bm.SetVector("_SunDir", sunL); bm.SetColor("_SunColor", sunCol);
                bm.SetFloat("_Transmit", transmit); bm.SetFloat("_Occlude", occlude);
            }
        }

        void SetCursor(bool free)
        {
            cursorFree = free;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        // Diagnostic: a bright unlit pillar 15 m ahead on the surface layer. If you can see it but not the ground,
        // the terrain is the problem; if you can't see even this, the camera/renderer path is.
        void ToggleMarker()
        {
            if (marker) { Destroy(marker); marker = null; return; }
            marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(marker.GetComponent<Collider>());
            marker.name = "SurfaceMarker"; marker.layer = SurfaceLayer;
            Vector3 f = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            marker.transform.position = cam.transform.position + f * 15f;
            marker.transform.localScale = new Vector3(1f, 6f, 1f);
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh != null ? sh : Shader.Find("Hidden/InternalErrorShader"));
            m.SetColor("_BaseColor", new Color(1f, 0.1f, 0.8f));
            marker.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        static Color StarColor(float T)
        {
            if (T <= 0f) T = 5778f;
            float t = Mathf.Clamp(T, 2400f, 12000f) / 100f;   // Tanner Helland blackbody approximation
            float r = t <= 66f ? 1f : Mathf.Clamp01(1.2929f * Mathf.Pow(t - 60f, -0.1332f));
            float g = t <= 66f ? Mathf.Clamp01(0.3901f * Mathf.Log(t) - 0.6318f) : Mathf.Clamp01(1.1298f * Mathf.Pow(t - 60f, -0.0755f));
            float b = t >= 66f ? 1f : t <= 19f ? 0f : Mathf.Clamp01(0.5432f * Mathf.Log(t - 10f) - 1.1962f);
            return new Color(r, g, b);
        }

        // Radial sea mesh: rings spaced geometrically from ~0.4 m to `span`, so the Gerstner swells have real geometry
        // near the camera while the far sea is a handful of big triangles. The mesh follows the camera every frame.
        // The liquid's optics and waves. Absorption per metre (Beer–Lambert) comes from what the sea is made of:
        // clear water absorbs red fastest (teal → deep blue), iron brines absorb blue/green (rust-brown), sulfide
        // seas absorb blue (yellow-green), phosphate is teal, and liquid methane/ethane absorbs blue (amber, glassy).
        void SetupLiquid(PlanetTexture.ChemPalette pal)
        {
            var theme = pal.theme;
            bool methane = theme == PlanetTexture.ChemTheme.Methanic || theme == PlanetTexture.ChemTheme.Tholin;
            Vector3 absorb; float amp = 1f, visc = 1f;
            if (methane) { absorb = new Vector3(0.05f, 0.12f, 0.4f); amp = 0.35f; visc = 0.6f; }                 // low-viscosity but low wind stress
            else switch (planet.waterChemistry)
            {
                case WaterChemistry.Iron:      absorb = new Vector3(0.12f, 0.35f, 0.6f); amp = 0.8f; visc = 1.3f; break;
                case WaterChemistry.Sulfide:   absorb = new Vector3(0.2f, 0.12f, 0.55f); amp = 0.85f; visc = 1.2f; break;
                case WaterChemistry.Phosphate: absorb = new Vector3(0.4f, 0.08f, 0.1f); break;
                default:                       absorb = new Vector3(0.45f, 0.09f, 0.05f); break;           // clear water
            }
            // in-scatter: the deep colour of the sea as seen from orbit (linear), dimmed
            Color deep = pal.oceanDeep.linear;
            waterMat.SetVector("_Absorb", absorb);
            waterMat.SetColor("_Scatter", deep * 0.6f);
            float grav = Mathf.Max(geo.climate.gravity, 0.05f) * 9.81f;
            float wind = Mathf.Clamp(geo.climate.windLoad * 0.35f, 0.05f, 1.5f);
            waterMat.SetFloat("_Gravity", grav);
            // fully developed seas scale with wind²/g; keep it in a sane visual range
            float swell = Mathf.Clamp(wind * wind * 9.81f / grav, 0.08f, 2.2f) * amp / visc;
            waterMat.SetFloat("_WaveAmp", swell);
            waterMat.SetFloat("_WaveLen", Mathf.Lerp(10f, 60f, Mathf.Clamp01(swell / 1.5f)));
            waterMat.SetFloat("_Choppy", methane ? 0.25f : 0.6f);
            waterMat.SetFloat("_Smooth", methane ? 0.98f : 0.94f);
            float wa = new DetRng(DetRng.Hash(pSeed, 0x5EA5UL)).Range(0f, Mathf.PI * 2f);
            Shader.SetGlobalVector("_WaterWind", new Vector4(Mathf.Cos(wa), Mathf.Sin(wa), wind, 0f));
            liquidDeep = deep; liquidAbsorb = absorb;
            Debug.Log($"[Surface] sea: {(methane ? "methane/ethane" : planet.waterChemistry.ToString())}, swell ×{swell:0.00}, g {grav:0.0} m/s²");
        }
        Color liquidDeep; Vector3 liquidAbsorb;

        static Mesh WaterMesh(int n, float span)
        {
            int rings = 200, segs = 160;
            float r0 = 0.4f, g = Mathf.Pow(span / r0, 1f / rings);
            var v = new List<Vector3>((rings + 1) * segs + 1) { Vector3.zero };
            var t = new List<int>(rings * segs * 6);
            for (int k = 0; k <= rings; k++)
            {
                float r = r0 * Mathf.Pow(g, k);
                for (int j = 0; j < segs; j++)
                {
                    float a2 = j / (float)segs * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a2) * r, 0f, Mathf.Sin(a2) * r));
                }
            }
            for (int j = 0; j < segs; j++) { t.Add(0); t.Add(1 + (j + 1) % segs); t.Add(1 + j); }
            for (int k = 0; k < rings; k++)
                for (int j = 0; j < segs; j++)
                {
                    int a0 = 1 + k * segs + j, a1 = 1 + k * segs + (j + 1) % segs;
                    int b0 = a0 + segs, b1 = a1 + segs;
                    t.Add(a0); t.Add(a1); t.Add(b0); t.Add(a1); t.Add(b1); t.Add(b0);
                }
            var m = new Mesh { name = "Sea", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, new Vector3(span * 2f, 120000f, span * 2f));
            return m;
        }

        // ── HUD ───────────────────────────────────────────────────────────────────────────────────────────
        GUIStyle hud, hudHead;
        // ── menu (M or the button): for now one page — the plants of this world ──
        bool menuOpen, floraOpen; Vector2 floraScroll;
        void DrawMenu()
        {
            float x = Screen.width - 150;
            if (GUI.Button(new Rect(x, 14, 136, 28), menuOpen ? "Close menu" : "Menu  (M)")) ToggleMenu();
            if (!menuOpen) return;
            if (GUI.Button(new Rect(x, 46, 136, 26), floraOpen ? "▸ Plants of this world" : "Plants of this world")) floraOpen = !floraOpen;
            if (!floraOpen) return;

            var list = ecology != null ? ecology.species : null;
            var r = new Rect(Screen.width - 560, 80, 546, Screen.height - 100);
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label($"<b>Flora of {planet.name}</b> — {(list != null ? list.Count : 0)} species from {(list != null ? CountLineages(list) : 0)} lineages", hudHead);
            floraScroll = GUILayout.BeginScrollView(floraScroll);
            if (list == null || list.Count == 0) GUILayout.Label("<color=#d16b62>Nothing grows on this world.</color>", hud);
            else foreach (var sp in list)
            {
                GUILayout.Label($"<b><i>{sp.name}</i></b>  <color=#9fb8ff>{sp.g.archetype} · {sp.role}{(sp.lineage >= 0 ? $" · lineage {(char)('A' + sp.lineage)}" : " · your creation")}</color>", hud);
                GUILayout.Label(Describe(sp), hud);
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void ToggleMenu() { menuOpen = !menuOpen; if (menuOpen) SetCursor(true); }

        static int CountLineages(List<SurfaceEcology.Species> l)
        { var h = new HashSet<int>(); foreach (var s in l) if (s.lineage >= 0) h.Add(s.lineage); return h.Count; }

        static string Describe(SurfaceEcology.Species sp)
        {
            var s = sp.s;
            string form = sp.role switch
            {
                Role.Canopy => "A canopy-forming plant that towers over its surroundings",
                Role.Understory => "An understorey plant living in the shade of taller growth",
                Role.Ground => "A low ground-cover plant carpeting open soil",
                _ => "An accent species scattered in small stands",
            };
            var biomes = new List<string>(); foreach (var b in sp.biomes) biomes.Add(Nice(b));
            string where = biomes.Count > 0 ? $"Found in {string.Join(", ", biomes)}." : "";
            string tol = $"Survives {s.tempMinC:0} to {s.tempMaxC:0} °C, needs {s.precipMinMm:0}–{s.precipMaxMm:0} mm of rain a year, withstands winds ×{s.maxWindLoad:0.0} Earth.";
            string extra = sp.hardy ? " A hardy pioneer that thrives beyond what its biology suggests." : "";
            return $"{form}. {where} {tol}{extra}";
        }

        void OnGUI()
        {
            if (hud == null)
            {
                hud = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
                hudHead = new GUIStyle(hud) { fontSize = 15, fontStyle = FontStyle.Bold };
            }
            string terrErr = terrain?.LastError;
            if (errors.Count > 0 || terrErr != null)
            {
                GUILayout.BeginArea(new Rect(Screen.width - 520, 14, 506, Screen.height * 0.6f), GUI.skin.box);
                GUILayout.Label("<color=#ff7b6b><b>Surface errors</b> (also in the Console)</color>", hud);
                foreach (var e in errors) GUILayout.Label($"<color=#ffb3a8>{e}</color>", hud);
                if (terrErr != null) GUILayout.Label($"<color=#ffb3a8>tile build ×{terrain.Failures}: {terrErr.Split('\n')[0]}</color>", hud);
                GUILayout.Label("Esc returns to space.", hud);
                GUILayout.EndArea();
            }
            if (geo == null || cam == null) return;
            DrawMenu();
            if (hideHud) return;
            GUILayout.BeginArea(new Rect(14, 14, 380, Screen.height - 28), GUI.skin.box);
            GUILayout.Label($"{planet.name}" + (string.IsNullOrEmpty(planet.colloquial) ? "" : $"  “{planet.colloquial}”"), hudHead);
            Vector3 d = geo.DirAt(ox + cam.transform.position.x, oz + cam.transform.position.z);
            float lat = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg, lon = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
            GUILayout.Label($"{Mathf.Abs(lat):0.000}°{(lat >= 0 ? "N" : "S")}  {Mathf.Abs(lon):0.000}°{(lon >= 0 ? "E" : "W")} · ground {hereElev:0} m · {(walk ? "walking" : $"flying {flySpeed:0} m/s")}", hud);
            Vector3 cp = cam.transform.position;
            double wxH = ox + cp.x, wzH = oz + cp.z;
            GUILayout.Label($"<b>Position</b> {wxH / 1000.0:0.000} km E, {wzH / 1000.0:0.000} km N of landing · altitude {cp.y:0.0} m ({cp.y - Mathf.Max(hereElev, 0f):0.0} m above ground)", hud);
            if (showDiag)
            {
                var cdd = cam.GetUniversalAdditionalCameraData();
                int camsOn = 0; foreach (var c in Camera.allCameras) if (c.enabled) camsOn++;
                GUILayout.Label($"<color=#9fb8ff>diag: unity pos ({cp.x:0.0}, {cp.y:0.0}, {cp.z:0.0}) · look yaw {yaw:0}° pitch {pitch:0}° · sun {sunElevDeg:0}° · " +
                                $"renderer #{GetRendererIndex(cdd)} · mask 0x{cam.cullingMask:X} · near {cam.nearClipPlane:0.00} far {cam.farClipPlane:0} · " +
                                $"cameras on {camsOn} (silenced {silenced.Count}) · K = test pillar · O = sky dome on/off · P = curvature on/off · J = hide diag</color>", hud);
            }
            GUILayout.Label($"<b>{Nice(hereClimate.biome)}</b> · {hereClimate.tMinC:0} to {hereClimate.tMaxC:0} °C (mean {hereClimate.tMeanC:0}) · {hereClimate.precipMm:0} mm/yr · wind ×{hereClimate.windLoad:0.0}", hud);
            if (terrainTypesLabel.Length > 0) GUILayout.Label($"<i>Terrain: {terrainTypesLabel}</i>", hud);
            if (weather != null) GUILayout.Label($"<i>Weather: {weather.Describe()}</i>", hud);
            GUILayout.Label($"<i>{geo.climate.pressureBar:0.00} bar · CO₂ {geo.climate.co2ppm:0} ppm · UV ×{geo.climate.uvIndex:0.0} · {geo.climate.gravity:0.00} g</i>", hud);
            GUILayout.Space(6);
            int total = ecology != null ? ecology.species.Count : 0;
            GUILayout.Label($"<b>Growing here</b> ({here.Count} of {total} species on this world)", hud);
            int shown = 0;
            foreach (var sp in here)
            {
                if (shown++ >= 10) break;
                GUILayout.Label($"• <i>{sp.name}</i> — {sp.g.archetype} ({sp.role}{(sp.lineage >= 0 ? $", lineage {(char)('A' + sp.lineage)}" : ", yours")})", hud);
            }
            if (total == 0) GUILayout.Label("<color=#d16b62>Nothing can grow on this world.</color>", hud);
            GUILayout.Space(6);
            if (terrain != null)
                GUILayout.Label($"<i>terrain: {terrain.ReadyCount} tiles built · {terrain.PendingCount} building · {terrain.ChunkCount} tracked" +
                                (terrain.Failures > 0 ? $" · <color=#ff7b6b>{terrain.Failures} failed</color>" : "") + "</i>", hud);
            GUILayout.Label("Mouse look · Tab free cursor · WASD move · Space/C up/down · Shift fast · scroll speed · F walk/fly · [ ] time · H hide · Esc take off", hud);
            GUILayout.EndArea();
        }

        static string Nice(Biome b) => b switch
        {
            Biome.TemperateForest => "Temperate forest", Biome.TemperateRainforest => "Temperate rainforest",
            Biome.TropicalSeasonal => "Tropical seasonal forest", Biome.TropicalRainforest => "Tropical rainforest",
            Biome.ColdDesert => "Cold desert", _ => b.ToString(),
        };

        // ── exit ──────────────────────────────────────────────────────────────────────────────────────────
        void Close()
        {
            if (cam)
            {
                cam.clearFlags = sClear; cam.backgroundColor = sBg; cam.nearClipPlane = sNear; cam.farClipPlane = sFar;
                cam.fieldOfView = sFov; cam.cullingMask = sMask;
                cam.transform.position = sPos; cam.transform.rotation = sRot;
                var cd = cam.GetUniversalAdditionalCameraData();
                cd.SetRenderer(sRenderer); cd.renderShadows = sShadows; cd.renderPostProcessing = sPost; cd.volumeLayerMask = sVolMask; cd.requiresDepthOption = sDepthOpt; cd.requiresColorOption = sColorOpt;
            }
            var urp = UniversalRenderPipeline.asset;
            if (urp) { urp.shadowDistance = sShadowDist; urp.renderScale = sRenderScale; urp.shadowCascadeCount = sCascades; urp.mainLightShadowmapResolution = sShadowRes; }
            Shader.SetGlobalFloat("_CurvK", 0f);
            foreach (var c in silenced) if (c) c.enabled = true;
            silenced.Clear();
            terrain?.Dispose(); ecology?.Dispose(); rocks?.Dispose(); grass?.Dispose(); weather?.Dispose();
            if (globe) { var gm = globe.GetComponent<MeshFilter>().sharedMesh; if (gm) Destroy(gm); Destroy(globe); }
            if (globeMat) Destroy(globeMat);
            foreach (var g in skyBodyGos) if (g) Destroy(g);
            if (sun2Go) Destroy(sun2Go);
            SSAO.SetActive(ssao, false);
            if (postVol) Destroy(postVol.gameObject);
            if (postProfile) Destroy(postProfile);
            foreach (var m in skyBodyMats) if (m) Destroy(m);
            foreach (var tx in skyBodyTex) if (tx) Destroy(tx);
            if (ctx != null && ctx.starCube) Destroy(ctx.starCube);
            if (terrainMat) Destroy(terrainMat);
            if (waterMat) Destroy(waterMat);
            if (root) Destroy(root);
            if (sunGo) Destroy(sunGo);
            if (marker) Destroy(marker);
            if (skyGo) Destroy(skyGo);
            if (skyMat) Destroy(skyMat);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;

            RenderSettings.fog = rsFog; RenderSettings.fogMode = rsFogMode; RenderSettings.fogColor = rsFogColor;
            RenderSettings.fogDensity = rsFogDensity; RenderSettings.ambientMode = rsAmbMode;
            RenderSettings.ambientSkyColor = rsAmbSky; RenderSettings.ambientEquatorColor = rsAmbEq;
            RenderSettings.ambientGroundColor = rsAmbGround; RenderSettings.ambientLight = rsAmbLight;
            RenderSettings.ambientProbe = rsProbe; RenderSettings.sun = rsSun;

            if (viewer) viewer.suspended = false;
            _inst = null; _suppressUntil = Time.frameCount + 1;
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
