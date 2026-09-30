using System.Collections;
using System.IO;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.GalaxyMap
{
    // Click a star in the galaxy view → infer its system from galaxy context → bake a skybox of its surroundings →
    // seamless fly-in to the generated system (SystemViewer), Escape to return. Auto-added to Camera.main by
    // GalaxyBootstrap.
    [RequireComponent(typeof(Camera))]
    public class StarSystemEntry : MonoBehaviour
    {
        public GalaxyBootstrap galaxy;          // auto-found if left empty
        public float pickPixelRadius = 22f;     // click tolerance in screen pixels
        public bool showReadout = true;

        [Header("Skybox (data-driven starfield)")]
        public int panoramaWidth = 6144;        // equirect panorama width (height = width/2)
        [Range(0.1f, 3f)] public float skyboxBrightness = 1f;
        public float starExposure = 12f;        // tone-map exposure for the accumulated star light
        public float nebulaGlow = 0.5f;         // strength of nebula haze in the sky
        [Header("Sky density")]
        public float starGain = 4f;             // brightness multiplier for the real stars (crisper, brighter points)
        public int densityBoost = 9;            // faint jittered copies per real star → dense sky that FOLLOWS the galaxy
        public float densityJitter = 0.05f;     // angular jitter of the copies (radians-ish)
        public int fillStars = 3000;            // faint uniform foreground stars so empty sky isn't pure black
        public float fillGain = 0.25f;
        [Header("Galaxy character")]
        public float coreGlowGain = 0.6f;       // brightness of the galactic-bulge glow in the core direction
        public Color coreColor = new Color(1f, 0.9f, 0.72f);
        [Range(0f, 1f)] public float dustLanes = 0.6f;   // dark dust rifts carved into the Milky-Way band
        public bool exportSkyboxPreview = false;// write the panorama PNG to <project>/_SkyboxCaptures for review

        [Header("Fly-in (Phase 3)")]
        public float approachSeconds = 1.0f;    // visible dolly toward the star before the swap
        public float fadeSeconds = 0.4f;
        public float systemTimeScale = 0.0012f; // orbital animation speed in-system (SystemViewer default 0.12 = ~100× faster)
        public KeyCode exitKey = KeyCode.Escape;
        public KeyCode randomSystemKey = KeyCode.P;   // jump straight to another random system

        [Header("Celestial sphere (live, telescope-ready)")]
        public int celestialBackgroundStars = 9000;   // faint uniform fill for the anti-galaxy sky
        public int celestialDensityBoost = 2;         // faint copies per real star → the diffuse band from anywhere
        public float celestialDensityJitter = 0.03f;
        public int bulgeStars = 7000;                 // dense star nucleus in the core direction (bloom makes it glow)
        public float bulgeGain = 0.4f;
        public float dustExtinction = 2.5f;           // strength of dark dust lanes/rift across the band
        public float celestialRadius = 2000f;
        public float pointSize = 2f;
        public float nakedEyeMag = 0.02f;        // faintest star visible without a telescope
        public float normalFov = 45f;
        public float telescopeFov = 9f;
        public KeyCode telescopeKey = KeyCode.T;
        CelestialSphere _sphere;
        bool _telescope;
        public float systemBloomBoost = 1.4f;   // extra bloom inside a system (star glow; keep modest so ice worlds don't blow out)
        float _savedBloomIntensity;

        Camera _cam;
        GalaxyCameraController _flyCtrl;
        StarInferenceResult _lastResult;
        StarSystem _lastSystem;
        Vector3 _lastStarWorld;
        bool _hasPick;
        Texture2D _skyTex;                      // equirectangular panorama of the star's surroundings
        Material _skyMat;                       // Skybox/Panoramic using _skyTex

        // system-mode state
        SystemViewer _viewer;
        bool _inSystem, _transitioning;
        float _fade;                            // 0 = clear … 1 = black
        Texture2D _fadeTex;
        CameraClearFlags _savedClear; Color _savedBg; float _savedFov, _savedNear, _savedFar; Material _savedSkybox;
        Vector3 _savedCamPos; Quaternion _savedCamRot;

        public Material LastSkyboxMaterial => _skyMat;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _flyCtrl = GetComponent<GalaxyCameraController>();
            if (galaxy == null) galaxy = FindObjectOfType<GalaxyBootstrap>();
            _fadeTex = new Texture2D(1, 1); _fadeTex.SetPixel(0, 0, Color.black); _fadeTex.Apply();
        }

        void Update()
        {
            if (_transitioning) return;
            if (CLAY.Flora.FloraLab.Active || CLAY.Surface.SurfaceWorld.Active || CLAY.Surface.TerrainLab.Active) return;   // the Flora Lab owns input; don't let its Escape also exit the system
            if (Input.GetKeyDown(randomSystemKey) && galaxy != null) { StartCoroutine(JumpToRandomSystem()); return; }
            if (_inSystem)
            {
                if (Input.GetKeyDown(exitKey)) StartCoroutine(ExitSystem());
                if (Input.GetKeyDown(telescopeKey)) _telescope = !_telescope;
            }
            else if (Input.GetMouseButtonDown(0)) TryPick(Input.mousePosition);
        }

        void LateUpdate()
        {
            if (CLAY.Flora.FloraLab.Active || CLAY.Surface.SurfaceWorld.Active || CLAY.Surface.TerrainLab.Active) return;   // sub-screens own the camera
            // Enforce telescope state after SystemViewer's own camera update. A telescope = narrow FOV (magnifies,
            // separating stars) + a lower magnitude limit (a bigger aperture reveals fainter stars). Because the
            // stars are point GEOMETRY, zooming in stays crisp and resolves close pairs instead of pixelating.
            if (_inSystem && !_transitioning && _cam != null)
            {
                _cam.fieldOfView = _telescope ? telescopeFov : normalFov;
                if (_sphere != null) _sphere.magLimit = _telescope ? 0f : nakedEyeMag;
            }
        }

        void OnDestroy()
        {
            if (_skyTex != null) Destroy(_skyTex);
            if (_skyMat != null) Destroy(_skyMat);
            if (_fadeTex != null) Destroy(_fadeTex);
            if (_sphere != null) Destroy(_sphere.gameObject);
        }

        void TryPick(Vector3 mouse)
        {
            if (galaxy == null || _cam == null) return;
            int n = galaxy.StarCountLive;
            if (n == 0) return;

            int best = -1; float bestD2 = pickPixelRadius * pickPixelRadius;
            for (int i = 0; i < n; i++)
            {
                Vector3 sp = _cam.WorldToScreenPoint(galaxy.GetStarWorld(i));
                if (sp.z <= 0f) continue;                                   // behind the camera
                float dx = sp.x - mouse.x, dy = sp.y - mouse.y;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = i; }
            }
            if (best < 0) return;
            PrepareStar(best);
            StartCoroutine(EnterSequence());
        }

        // Infer + generate the system for star `idx` and stash it as the pending pick.
        void PrepareStar(int idx)
        {
            var ctx = BuildContext(idx);
            _lastResult = StarInference.Infer(ctx);
            _lastSystem = SystemGenerator.Generate(_lastResult.seed, _lastResult.genParams);
            _lastStarWorld = galaxy.GetStarWorld(idx);
            _hasPick = true;
            var st = _lastSystem.star;
            Debug.Log($"[StarSystemEntry] Star #{idx}: {st.Designation} {st.stellarMass:0.00} M☉, " +
                      $"{_lastSystem.planets.Count} planets, {_lastSystem.HabitablePlanetCount} habitable, {_lastSystem.StarCount}-star.");
        }

        // The seamless fly-in: dolly toward the star (galaxy visible) → fade → bake the skybox + hide the galaxy +
        // spawn the inferred system → fade back in. Escape returns to the galaxy.
        IEnumerator EnterSequence()
        {
            _transitioning = true;
            _hasPick = false;                                  // hide the galaxy-mode readout
            if (_flyCtrl) _flyCtrl.enabled = false;

            yield return ApproachStar(_lastStarWorld);         // visible approach
            yield return Fade(1f);                             // to black (hides the swap)
            SwapIntoSystem();
            yield return Fade(0f);
            _inSystem = true; _transitioning = false;
        }

        // Build the sky + despawn the galaxy + spawn the inferred system. Assumes the screen is already faded out.
        void SwapIntoSystem()
        {
            BuildCelestialSphere(_lastStarWorld);              // live star sphere (needs star data → before despawn)
            EnterSystemMode();
            _viewer.EnsureInit();                              // build camera/meshes/root now (no Start-order race)
            _viewer.timeScale = systemTimeScale;               // slow the orbits to a believable pace
            _viewer.LoadExternalSystem(_lastSystem);           // spawn bodies before its first Update runs
            ApplySystemCamera();                               // skybox clear AFTER SystemViewer reset it
        }

        // P: jump straight to another random system (surveying how systems generate).
        IEnumerator JumpToRandomSystem()
        {
            _transitioning = true;
            yield return Fade(1f);
            if (_inSystem) { TeardownSystem(); galaxy.Respawn(); yield return null; }   // rebuild star data to pick from
            int n = galaxy.StarCountLive;
            if (n > 0)
            {
                PrepareStar(UnityEngine.Random.Range(0, n));
                SwapIntoSystem();
            }
            yield return Fade(0f);
            _inSystem = n > 0; _transitioning = false;
        }

        IEnumerator ApproachStar(Vector3 target)
        {
            Vector3 p0 = _cam.transform.position;
            Quaternion r0 = _cam.transform.rotation;
            // aim at the star, stop a little short of it
            Vector3 dir = (target - p0).sqrMagnitude > 1e-4f ? (target - p0).normalized : _cam.transform.forward;
            Vector3 p1 = target - dir * 3f;
            Quaternion r1 = Quaternion.LookRotation(dir, Vector3.up);
            float t = 0f, dur = Mathf.Max(0.01f, approachSeconds);
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                float e = t * t * (3f - 2f * t);               // smoothstep ease
                _cam.transform.position = Vector3.Lerp(p0, p1, e);
                _cam.transform.rotation = Quaternion.Slerp(r0, r1, e);
                yield return null;
            }
        }

        IEnumerator Fade(float to)
        {
            float from = _fade, t = 0f, dur = Mathf.Max(0.01f, fadeSeconds);
            while (t < 1f) { t += Time.deltaTime / dur; _fade = Mathf.Lerp(from, to, t); yield return null; }
            _fade = to;
        }

        void EnterSystemMode()
        {
            // Save galaxy camera state, then tear the galaxy down (BRG hide is unreliable; rebuild on exit).
            _savedClear = _cam.clearFlags; _savedBg = _cam.backgroundColor; _savedFov = _cam.fieldOfView;
            _savedNear = _cam.nearClipPlane; _savedFar = _cam.farClipPlane;
            _savedSkybox = RenderSettings.skybox;
            _savedCamPos = _cam.transform.position; _savedCamRot = _cam.transform.rotation;
            _savedBloomIntensity = galaxy.BloomIntensity;
            galaxy.BloomIntensity = _savedBloomIntensity * Mathf.Max(1f, systemBloomBoost);
            if (_flyCtrl) _flyCtrl.enabled = false;            // SystemViewer drives the camera in-system
            galaxy.DespawnAll();

            var go = new GameObject("~SystemViewer");
            go.transform.position = Vector3.zero;
            _viewer = go.AddComponent<SystemViewer>();
            _viewer.autoGenerateOnStart = false;               // we feed it our inferred system
        }

        void ApplySystemCamera()
        {
            // Deep-space black; the celestial sphere provides the stars (drawn in the Background queue).
            RenderSettings.skybox = null;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _telescope = false;
            _cam.fieldOfView = normalFov;
        }

        void BuildCelestialSphere(Vector3 observer)
        {
            if (_sphere == null)
            {
                var go = new GameObject("~CelestialSphere");
                _sphere = go.AddComponent<CelestialSphere>();
            }
            _sphere.cam = _cam;
            _sphere.radius = celestialRadius;
            _sphere.pointSize = pointSize;
            _sphere.gain = 1f;
            _sphere.magLimit = nakedEyeMag;
            _sphere.Build(observer, galaxy, _lastResult.seed, celestialBackgroundStars, starGain, fillGain,
                          celestialDensityBoost, celestialDensityJitter, bulgeStars, bulgeGain, dustExtinction);
        }

        IEnumerator ExitSystem()
        {
            _transitioning = true;
            yield return Fade(1f);
            TeardownSystem();
            galaxy.Respawn();
            yield return Fade(0f);
            _inSystem = false; _transitioning = false;
        }

        // Destroy the system view + celestial sphere and restore the galaxy camera/bloom (does NOT respawn or fade).
        void TeardownSystem()
        {
            if (_viewer != null) Destroy(_viewer.gameObject);
            _viewer = null;
            if (_sphere != null) Destroy(_sphere.gameObject);
            _sphere = null;
            _telescope = false;

            RenderSettings.skybox = _savedSkybox;
            _cam.clearFlags = _savedClear; _cam.backgroundColor = _savedBg; _cam.fieldOfView = _savedFov;
            _cam.nearClipPlane = _savedNear; _cam.farClipPlane = _savedFar;
            _cam.transform.position = _savedCamPos; _cam.transform.rotation = _savedCamRot;
            if (_flyCtrl != null)
            {
                var e = _savedCamRot.eulerAngles;
                _flyCtrl.yaw = e.y; _flyCtrl.pitch = e.x > 180f ? e.x - 360f : e.x;
                _flyCtrl.enabled = true;
            }
            galaxy.BloomIntensity = _savedBloomIntensity;
        }

        // DATA-DRIVEN sky: from the star's position, splat every OTHER star as a true point of light (direction +
        // inverse-square apparent brightness + blackbody colour) into an equirectangular panorama, plus soft nebula
        // haze. No camera capture → stars are points (not bloom discs), no box artifacts, fully spherical. The Milky
        // Way band emerges naturally from the disk's star distribution. Displayed with Skybox/Panoramic.
        void BuildStarfieldPanorama(Vector3 P, int starIndex)
        {
            int w = Mathf.Clamp(panoramaWidth, 512, 8192); if ((w & 1) == 1) w++; int h = w / 2;
            if (_skyTex == null || _skyTex.width != w)
            {
                if (_skyTex != null) Destroy(_skyTex);
                _skyTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            }

            var accum = new Vector3[w * h];                     // additive HDR-ish accumulation
            int n = galaxy.StarCountLive;
            var rng = new DetRng(_lastResult.seed == 0UL ? 1UL : _lastResult.seed);

            for (int i = 0; i < n; i++)
            {
                var s = galaxy.GetStar(i);
                if (s.Luminosity < 0f) continue;
                Vector3 rel = galaxy.GetStarWorld(i) - P;
                float dist2 = rel.sqrMagnitude;
                if (dist2 < 1e-4f) continue;                    // skip the star we're standing on
                float inten = Mathf.Max(s.Luminosity, 0.0006f) / Mathf.Max(dist2, 0.25f) * starGain;

                Vector3 d = rel / Mathf.Sqrt(dist2);
                Color bc = Astrophysics.BlackbodyColor(Mathf.Clamp(s.Temperature, 1500f, 40000f));
                Vector3 col = new Vector3(bc.r, bc.g, bc.b);

                SplatStar(accum, w, h, d, col * inten);         // the real star

                // DENSITY BOOST: faint jittered copies so the sky is dense but still FOLLOWS the real galaxy
                // (bright wraparound band near the core, a distant patch from the halo) → every location differs.
                for (int k = 0; k < densityBoost; k++)
                {
                    Vector3 jd = (d + new Vector3(rng.Value - 0.5f, rng.Value - 0.5f, rng.Value - 0.5f) * (2f * densityJitter)).normalized;
                    float fb = inten * (0.15f + 0.4f * rng.Value);
                    SplatStar(accum, w, h, jd, col * fb);
                }
            }

            AddFillStars(accum, w, h, _lastResult.seed ^ 0xABCDEF01UL);
            AddCoreGlow(P, accum, w, h);        // the galactic bulge, in the true core direction
            AddDustLanes(accum, w, h, _lastResult.seed);   // dark rifts carved into the bright band
            AddNebulaGlow(P, accum, w, h);
            SmoothPoles(accum, w, h);           // remove the equirect pole pinch/"singularity" swirl

            // Tone-map the accumulation → 0..1, so bright near stars stay point-like and the faint band shows.
            var px = new Color[w * h];
            for (int j = 0; j < px.Length; j++)
            {
                Vector3 a = accum[j] * starExposure;
                px[j] = new Color(1f - Mathf.Exp(-a.x), 1f - Mathf.Exp(-a.y), 1f - Mathf.Exp(-a.z), 1f) * skyboxBrightness;
            }
            _skyTex.SetPixels(px); _skyTex.Apply(false);

            if (_skyMat == null)
            {
                var sh = Shader.Find("Skybox/Panoramic");
                if (sh != null) _skyMat = new Material(sh);
            }
            if (_skyMat != null)
            {
                _skyMat.SetTexture("_MainTex", _skyTex);
                if (_skyMat.HasProperty("_Mapping")) _skyMat.SetFloat("_Mapping", 1);       // latitude-longitude
                if (_skyMat.HasProperty("_ImageType")) _skyMat.SetFloat("_ImageType", 0);   // 360°
                if (_skyMat.HasProperty("_Layout")) _skyMat.SetFloat("_Layout", 0);
                if (_skyMat.HasProperty("_Exposure")) _skyMat.SetFloat("_Exposure", 1f);
            }

            if (exportSkyboxPreview)
            {
                string dir = Path.Combine(Application.dataPath, "..", "_SkyboxCaptures");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(dir, $"star_{starIndex}_pano.png")), _skyTex.EncodeToPNG());
            }
            Debug.Log($"[StarSystemEntry] Built {w}×{h} data-driven starfield sky from {n} stars.");
        }

        // Splat a star as a 3×3 gaussian point into the equirect (longitude wraps). Small enough that equirect pole
        // stretch is negligible for stars.
        static readonly float[] Gk3 = { 0.075f, 0.124f, 0.075f, 0.124f, 0.204f, 0.124f, 0.075f, 0.124f, 0.075f };
        void SplatStar(Vector3[] accum, int w, int h, Vector3 d, Vector3 c)
        {
            float lon = Mathf.Atan2(d.x, d.z); if (lon < 0f) lon += 2f * Mathf.PI;
            float lat = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f));
            int cx = Mathf.Clamp((int)(lon / (2f * Mathf.PI) * w), 0, w - 1);
            int cy = Mathf.Clamp((int)((lat / Mathf.PI + 0.5f) * h), 0, h - 1);
            for (int oy = -1; oy <= 1; oy++)
            {
                int py = cy + oy; if (py < 0 || py >= h) continue;
                for (int ox = -1; ox <= 1; ox++)
                {
                    int pxx = ((cx + ox) % w + w) % w;
                    accum[py * w + pxx] += c * Gk3[(oy + 1) * 3 + (ox + 1)];
                }
            }
        }

        // Faint uniform foreground stars so a sky pointing away from the galaxy still has a scattering of stars.
        void AddFillStars(Vector3[] accum, int w, int h, ulong seed)
        {
            int count = Mathf.Max(0, fillStars);
            if (count == 0) return;
            var rng = new DetRng(seed == 0UL ? 1UL : seed);
            for (int i = 0; i < count; i++)
            {
                float z = rng.Value * 2f - 1f, az = rng.Value * 2f * Mathf.PI;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                Vector3 d = new Vector3(r * Mathf.Cos(az), z, r * Mathf.Sin(az));
                float b = Mathf.Pow(rng.Value, 3.5f) * fillGain;
                Color bc = Astrophysics.BlackbodyColor(Mathf.Lerp(2800f, 13000f, Mathf.Pow(rng.Value, 2f)));
                SplatStar(accum, w, h, d, new Vector3(bc.r, bc.g, bc.b) * b);
            }
        }

        // The GALACTIC BULGE — a mottled warm glow in the true direction of the galaxy centre (world origin), sized
        // and brightened by how close the star is to the core. Gives the sky a specific bright nucleus that differs
        // wildly between a core star (huge glow) and a halo star (small distant one), instead of a generic clump.
        void AddCoreGlow(Vector3 P, Vector3[] accum, int w, int h)
        {
            if (coreGlowGain <= 0f) return;
            Vector3 rel = -P; float dist = rel.magnitude; if (dist < 1e-2f) return;   // (galaxy centred at origin)
            Vector3 d = rel / dist;
            float bulge = Mathf.Max(galaxy.BulgeSizeValue, galaxy.DiskRadiusValue * 0.08f);
            float ang = Mathf.Clamp(Mathf.Atan(bulge / dist), 0.02f, 0.5f);   // tighter, smaller nucleus
            float bright = coreGlowGain * Mathf.Clamp01(bulge * 1.2f / dist);
            if (bright < 0.003f) return;

            float lat0 = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f));
            int cy = Mathf.Clamp((int)((lat0 / Mathf.PI + 0.5f) * h), 0, h - 1);
            float lon0 = Mathf.Atan2(d.x, d.z); if (lon0 < 0f) lon0 += 2f * Mathf.PI;
            int cx0 = (int)(lon0 / (2f * Mathf.PI) * w);
            int radY = Mathf.Clamp((int)(ang / Mathf.PI * h), 2, h / 2);
            Vector3 c = new Vector3(coreColor.r, coreColor.g, coreColor.b) * bright;

            for (int oy = -radY; oy <= radY; oy++)
            {
                int py = cy + oy; if (py < 0 || py >= h) continue;
                float lat = ((py + 0.5f) / h - 0.5f) * Mathf.PI;
                float cosl = Mathf.Max(0.08f, Mathf.Cos(lat)), sinlat = Mathf.Sin(lat);
                int radX = Mathf.Clamp((int)(radY / cosl), 1, w / 2);
                for (int ox = -radX; ox <= radX; ox++)
                {
                    float nx = ox * cosl / radY, ny = (float)oy / radY;
                    float rr = nx * nx + ny * ny; if (rr > 1f) continue;
                    float lonp = ((cx0 + ox) / (float)w) * 2f * Mathf.PI;
                    Vector3 pd = new Vector3(cosl * Mathf.Sin(lonp), sinlat, cosl * Mathf.Cos(lonp));
                    float mott = 0.15f + 1.1f * Fbm3(pd * 11f);        // finer, star-like mottle (not a flat smear)
                    float fall = Mathf.Exp(-rr * 6.5f);                // tight, concentrated nucleus
                    int pxx = ((cx0 + ox) % w + w) % w;
                    accum[py * w + pxx] += c * (fall * mott * 0.05f);
                }
            }
        }

        // Dark DUST-LANE rifts: where the sky is bright (the band), carve sparse ridged-noise filaments of dust so
        // the band gets the Milky-Way's mottled dark structure instead of reading as a smooth luminous smear.
        void AddDustLanes(Vector3[] accum, int w, int h, ulong seed)
        {
            if (dustLanes <= 0f) return;
            float so = (seed % 1009UL) * 0.11f;
            for (int y = 0; y < h; y++)
            {
                float lat = ((y + 0.5f) / h - 0.5f) * Mathf.PI;
                float cosl = Mathf.Cos(lat), sinlat = Mathf.Sin(lat);
                int baseIdx = y * w;
                for (int x = 0; x < w; x++)
                {
                    Vector3 v = accum[baseIdx + x];
                    float lum = v.x + v.y + v.z;
                    if (lum < 0.03f) continue;                          // only carve the bright band
                    float lonp = (x / (float)w) * 2f * Mathf.PI;
                    Vector3 pd = new Vector3(cosl * Mathf.Sin(lonp), sinlat, cosl * Mathf.Cos(lonp));
                    float ridge = 1f - Mathf.Abs(Fbm3(pd * 4.5f + new Vector3(so, so, so)));
                    float dust = Mathf.Clamp01((ridge - 0.55f) * 3.5f);
                    accum[baseIdx + x] = v * (1f - dustLanes * dust);
                }
            }
        }

        // Nebulae as SMALL, TEXTURED patches (not big smooth pink blobs). Angular size from distance (only large up
        // close), value-noise structure, and pole-correct splatting (longitude radius scaled by 1/cos(lat)) so a
        // nebula near the zenith no longer smears/"vortexes" across the top of the equirect.
        void AddNebulaGlow(Vector3 P, Vector3[] accum, int w, int h)
        {
            if (nebulaGlow <= 0f) return;
            int nb = galaxy.NebulaCount;
            for (int k = 0; k < nb; k++)
            {
                if (!galaxy.TryGetNebula(k, out Vector3 pos, out float size, out Color col)) continue;
                Vector3 rel = pos - P; float dist = rel.magnitude; if (dist < 1e-3f) continue;
                Vector3 d = rel / dist;

                float ang = Mathf.Atan(size / dist);            // true angular radius; distant nebulae are TINY
                ang = Mathf.Min(ang, 0.35f);                    // cap (~20°) even when very close
                if (ang < 0.004f) continue;                     // sub-pixel → skip (it's just a faint star-like dot)
                float strength = nebulaGlow * Mathf.Clamp01(size / dist * 0.6f);
                if (strength < 0.004f) continue;

                float clat0 = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f));
                int cy = Mathf.Clamp((int)((clat0 / Mathf.PI + 0.5f) * h), 0, h - 1);
                int radY = Mathf.Clamp((int)(ang / Mathf.PI * h), 1, h / 2);
                float lon0 = Mathf.Atan2(d.x, d.z); if (lon0 < 0f) lon0 += 2f * Mathf.PI;
                int cx0 = (int)(lon0 / (2f * Mathf.PI) * w);
                Vector3 c = new Vector3(col.r, col.g, col.b) * strength;

                float ns = k * 5.13f;
                for (int oy = -radY; oy <= radY; oy++)
                {
                    int py = cy + oy; if (py < 0 || py >= h) continue;
                    float lat = ((py + 0.5f) / h - 0.5f) * Mathf.PI;
                    float cosl = Mathf.Max(0.08f, Mathf.Cos(lat));
                    int radX = Mathf.Clamp((int)(radY / cosl), 1, w / 2);       // pole-correct longitude extent
                    float sinlat = Mathf.Sin(lat);
                    for (int ox = -radX; ox <= radX; ox++)
                    {
                        float nx = ox * cosl / radY, ny = (float)oy / radY;
                        float rr = nx * nx + ny * ny; if (rr > 1f) continue;
                        int pxx = ((cx0 + ox) % w + w) % w;

                        // 3D fBm on the pixel's DIRECTION → resolution-independent filamentary gas (not a flat,
                        // pixel-noise splotch), detailed even when the nebula fills much of the sky up close.
                        float lonp = ((cx0 + ox) / (float)w) * 2f * Mathf.PI;
                        Vector3 pd = new Vector3(cosl * Mathf.Sin(lonp), sinlat, cosl * Mathf.Cos(lonp));
                        float baseN = Fbm3(pd * 6f + new Vector3(ns, ns, ns));
                        float ridge = 1f - Mathf.Abs(Fbm3(pd * 13f + new Vector3(9f + ns, 3f, 7f)));
                        float fine = 1f - Mathf.Abs(Fbm3(pd * 26f + new Vector3(21f, 5f, ns)));
                        float tex = Mathf.Clamp01((baseN * 0.6f + ridge * 0.6f + fine * 0.3f - 0.42f) * 1.7f);  // higher contrast

                        // DARK DUST lanes carve the gas, and a bright ionized CORE glows toward the centre → real
                        // emission-nebula structure (bright rim-lit gas + dark rifts), distinct per nebula via `ns`.
                        float dustN = Mathf.Clamp01((Fbm3(pd * 9f + new Vector3(ns + 40f, ns, 13f)) - 0.5f) * 2.6f);
                        float gas = tex * (1f - 0.9f * dustN);
                        float coreB = 1f + 2.2f * (1f - rr) * (1f - rr) * (1f - rr);   // tight bright ionized core
                        float fall = (1f - rr) * (1f - rr) * (1f - rr);               // tighter edge (no soft smear)
                        accum[py * w + pxx] += c * (fall * gas * coreB * 0.13f);
                    }
                }
            }
        }

        // Blend the polar-cap rows toward each row's mean, increasing to the pole, so the equirect's converging
        // pixels don't render as a swirling "singularity" at the top/bottom of the sky.
        static void SmoothPoles(Vector3[] accum, int w, int h)
        {
            int cap = Mathf.Max(2, h / 24);
            for (int side = 0; side < 2; side++)
            {
                for (int r = 0; r < cap; r++)
                {
                    int y = side == 0 ? r : h - 1 - r;
                    Vector3 mean = Vector3.zero;
                    int baseIdx = y * w;
                    for (int x = 0; x < w; x++) mean += accum[baseIdx + x];
                    mean /= w;
                    float t = 1f - (float)r / cap;          // 1 at the pole → 0 at the cap edge
                    t = t * t;
                    for (int x = 0; x < w; x++) accum[baseIdx + x] = Vector3.Lerp(accum[baseIdx + x], mean, t);
                }
            }
        }

        // Cheap 3D value-noise fBm for nebula gas structure.
        static float Fbm3(Vector3 p)
        {
            float v = 0f, a = 0.5f;
            for (int i = 0; i < 4; i++) { v += a * VN3(p); p *= 2.02f; a *= 0.5f; }
            return v;
        }
        static float VN3(Vector3 p)
        {
            float xi = Mathf.Floor(p.x), yi = Mathf.Floor(p.y), zi = Mathf.Floor(p.z);
            float xf = p.x - xi, yf = p.y - yi, zf = p.z - zi;
            xf = xf * xf * (3f - 2f * xf); yf = yf * yf * (3f - 2f * yf); zf = zf * zf * (3f - 2f * zf);
            float c000 = H3(xi, yi, zi), c100 = H3(xi + 1, yi, zi), c010 = H3(xi, yi + 1, zi), c110 = H3(xi + 1, yi + 1, zi);
            float c001 = H3(xi, yi, zi + 1), c101 = H3(xi + 1, yi, zi + 1), c011 = H3(xi, yi + 1, zi + 1), c111 = H3(xi + 1, yi + 1, zi + 1);
            return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(c000, c100, xf), Mathf.Lerp(c010, c110, xf), yf),
                              Mathf.Lerp(Mathf.Lerp(c001, c101, xf), Mathf.Lerp(c011, c111, xf), yf), zf);
        }
        static float H3(float x, float y, float z)
        {
            float v = Mathf.Sin(x * 127.1f + y * 311.7f + z * 74.7f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        GalaxyStarContext BuildContext(int i)
        {
            var s = galaxy.GetStar(i);                 // generation-space position + physical props
            var p = galaxy.CurrentParams();
            Vector3 world = galaxy.GetStarWorld(i);

            float rr = Mathf.Sqrt(s.Position.x * s.Position.x + s.Position.z * s.Position.z);
            float radiusFrac = Mathf.Clamp01(rr / Mathf.Max(p.DiskRadius, 1e-3f));

            // Arm proximity from the same log-spiral phase the generator uses.
            float pitch = Mathf.Max(p.PitchAngle, 0.02f);
            float refR = Mathf.Max(p.BarStrength * p.DiskRadius * 0.35f, 1f);
            float ang = Mathf.Atan2(s.Position.z, s.Position.x);
            float armPhase = Mathf.Max(1, p.ArmCount) * (ang - Mathf.Log(Mathf.Max(rr, 1e-3f) / refR) / pitch);
            float sc = (Mathf.Sin(armPhase) + 1f) * 0.5f;
            float armProximity = sc * sc;

            float nebDist = galaxy.NearestNebulaDistance(world, out float nebSize);
            float nebulaProximity = Mathf.Clamp01(1f - nebDist / Mathf.Max(nebSize * 3f, 1e-3f));

            return new GalaxyStarContext
            {
                index = i,
                galaxySeed = galaxy.CurrentSeed,
                worldPos = world,
                temperatureK = s.Temperature,
                luminosity01 = Mathf.Clamp01(s.Luminosity),
                ageGyr = s.Age,
                radiusFrac = radiusFrac,
                armProximity = armProximity,
                nebulaProximity = nebulaProximity,
                color = galaxy.GalaxyTintColor,
            };
        }

        void OnGUI()
        {
            if (CLAY.Flora.FloraLab.Active || CLAY.Surface.SurfaceWorld.Active || CLAY.Surface.TerrainLab.Active) return;   // sub-screens draw their own UI
            // Galaxy-mode inference readout.
            if (showReadout && _hasPick && !_inSystem && !_transitioning)
            {
                var st = _lastSystem.star;
                string txt = _lastResult.summary + "\n\n" +
                             $"PRIMARY: {st.Designation}  {st.stellarMass:0.00} M☉  {st.effectiveTemp:0} K  {st.luminosity:0.00} L☉\n" +
                             $"age {st.stellarAgeGyr:0.0}/{st.lifetimeGyr:0.0} Gyr   [Fe/H] {st.metallicity:0.00}   HZ {st.hzInnerAU:0.00}–{st.hzOuterAU:0.00} AU\n" +
                             $"{_lastSystem.planets.Count} planets · {_lastSystem.HabitablePlanetCount} habitable · {_lastSystem.StarCount}-star";
                var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, wordWrap = true };
                GUI.Box(new Rect(10, 10, 430, 168), txt, style);
            }

            if (_inSystem && !_transitioning)
                GUI.Label(new Rect(12, 8, 640, 22), $"◄ {exitKey} : galaxy    {telescopeKey} : telescope {(_telescope ? "ON" : "off")}    {randomSystemKey} : random system");

            // Fade-to-black overlay for the transition.
            if (_fade > 0.001f)
            {
                var c = GUI.color; GUI.color = new Color(0, 0, 0, Mathf.Clamp01(_fade));
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _fadeTex);
                GUI.color = c;
            }
        }
    }
}
