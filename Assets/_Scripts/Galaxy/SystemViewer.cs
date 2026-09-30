using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using CLAY.Flora;

namespace CLAY.Galaxy
{
    /// <summary>
    /// A runtime 3D viewer for generated star systems, with a Celestia-style scale transition: far out it's
    /// ICONOGRAPHIC (planets enlarged and orbits compressed so you can read the layout), and as you zoom in it
    /// blends toward TRUE relative scale (real size ratios, real orbital distances). Click a body to fly to it.
    /// Everything lives in true-AU coordinates so float precision stays sane across the scale range.
    ///
    /// Left-click: focus/fly to a body · Right-drag: orbit · Scroll: zoom.
    /// </summary>
    public class SystemViewer : MonoBehaviour
    {
        [Header("Generation")]
        public bool autoGenerateOnStart = true;  // false → an external driver (galaxy fly-in) calls LoadExternalSystem
        public ulong seed = 1;
        public int starCount = 1;
        public int planetCount = 0;              // 0 = random 3–7
        public float maxInclinationDeg = 18f;

        [Header("Animation")]
        public float timeScale = 0.015f;   // (was 0.12 — slowed ~8× so orbits & spins read calmly)
        const float SpinRef = 0.015f;      // spins are authored at this time scale; the slider scales them too

        const float EARTH_R_AU = 4.2635e-5f;     // Earth radius in AU
        const float SUN_R_AU   = 0.0046524f;     // Sun radius in AU

        Camera cam;
        Transform root;
        Shader surfaceShader, oceanShader, atmoShader, giantShader, ringShader, cloudShader, starShader, coronaShader, flaresShader, glowShader;
        Mesh smoothSphere;                       // shared high-res sphere for stars & shells (smooth limb)
        Mesh ringMesh;                           // shared annulus for gas-giant rings
        Mesh quadMesh;                           // shared unit quad for the star glow billboard
        StarSystem sys;

        const int PlanetSubdiv = 5;              // silhouette detail (displacement); colour is per-pixel
        const int SurfaceTexLow = 512;           // spawn bake (async now, so a higher res costs no stall)
        const int SurfaceTexHi = 1024;           // full-res bake, done lazily when a planet is focused
        const int SurfaceTexUltra = 2048;        // at default framing / mid zoom
        const int SurfaceTex4K = 4096;           // super-zoom crispness
        const float OceanShellScale = 1.001f;    // ocean sits right at sea level
        const float AtmoShellScale = 1.10f;      // atmosphere shell — kept above the cloud deck
        const float CloudShellScale = 1.04f;     // cloud deck hugging the surface (wet cloudy worlds have low peaks)

        // ── live tuning knobs (edited from the on-screen panel) ──
        bool showTune;
        PlanetTexture.PlanetTuning tune = PlanetTexture.Tuning;
        float knobBump = 16f, knobAmbient = 0.04f, knobRim = 3f, knobCamFill = 0.1f; // surface shader
        float knobGiantBands = 7f, knobGiantTurb = 0.5f, knobGiantSpeed = 0.1f;      // gas-giant shader
        float knobGiantChem = 0.35f, knobGiantFine = 0.5f;
        float knobGiantSpot = 0.5f, knobGiantBandVar = 1f, knobGiantSpotSize = 0f;   // likelihood, band var, size (0=gen)
        // star knobs (live when a star is focused): activity/appearance controls. Defaults are the values that
        // looked good in testing; the ranges are kept fairly tight so a star never drifts far from that look.
        float knobStarSpots = 0.26f, knobStarFlares = 1f, knobStarVolatility = 0.35f;
        float knobStarCoronaSize = 1.36f, knobStarCoronaDensity = 0.15f, knobStarCoronaArc = 0.44f;
        float knobStarGlowSize = 0.5f, knobStarGlowIntensity = 0.62f;
        float knobRingOpacity = 0.85f, knobRingWidth = 0.5f, knobRingInner = 0.45f;  // ring: opacity, width, radius start
        float knobOceanSpec = 220f, knobOceanGain = 0.7f, knobOceanFres = 2.5f, knobOceanOpacity = 0.82f; // ocean
        float knobAtmoIntensity = 0.32f, knobAtmoSoft = 1.3f;                     // atmosphere (density, softness)
        bool tuneDirty; float tuneDirtyAt;                                        // debounced re-bake

        readonly List<Body> bodies = new();
        readonly List<Transform> beltTfs = new();   // asteroid/ice belt point-cloud rings
        readonly List<float> beltMid = new();
        readonly List<Material> beltMats = new();
        Shader beltShader;
        Body focus;                              // body the camera is looking at / flying to
        bool flownIn;                            // true once you've clicked a world (scroll then orbits it)
        bool showOrbits = true;                  // orbit lines on/off (toggle with the O key)
        bool hideGui;                            // H key hides all on-screen UI (clean screenshots)
        public bool suspended;                   // true while a sub-screen (e.g. the Flora Lab) has taken over
        float systemSpan = 10f;
        float scaleT;                            // 0 = iconographic, 1 = true scale

        // camera state
        float camYaw = 35f, camPitch = 22f, camDist, camDistTarget;
        Vector2 dragStart; bool dragging;
        Vector3 focusPoint;
        GUIStyle title, label, help, story;

        class Body
        {
            public Transform tf; public Material mat; public LineRenderer line; public Transform lineTf;
            public OrbitElements orbit; public bool orbiting, isStar;
            public float realDistAU, realRadiusAU, iconDistAU, iconRadiusAU, spin;
            public string label; public PlanetData planet; public StarData star;
            public MeshFilter mf; public Mesh planetMesh; public Texture2D surfaceTex; public bool hiRes;
            public Material oceanMat, atmoMat, ringMat, cloudMat, coronaMat, flaresMat, glowMat;  // shells + ring + star glow layers
            public Transform oceanTf, atmoTf, ringTf, coronaTf, flaresTf, glowTf, cloudTf;    // shell/ring transforms (object-space cam pos, ring shadow, billboard)
            public Task<PlanetTexture.SurfacePixels> bakeTask; public int bakeWidth, texWidth;  // async bake
            public float giantSpotPotential;                    // 0..1; storm shows if < likelihood
            public Body host; public int moonIdx;               // set for moons — orbit is about the host planet
            public float oblateY = 1f;                          // <1 flattens the poles (fast rotators)
            public Color lineCol = Color.clear;                 // base orbit-line colour (alpha faded on zoom-in)
            public int starIdx = -1;                            // stars: 0 = A, 1 = B, 2 = C
            public Vector3 realPos;                             // true orbital position (AU, un-iconised) — for flux
        }

        bool _init;

        void Start()
        {
            EnsureInit();
            if (autoGenerateOnStart) Regenerate();
        }

        // Idempotent setup — safe to call before an external driver loads a system (removes Start-order hazards).
        public void EnsureInit()
        {
            if (_init) return;
            _init = true;
            surfaceShader = Shader.Find("CLAY/PlanetSurface");
            oceanShader = Shader.Find("CLAY/PlanetOcean");
            atmoShader = Shader.Find("CLAY/PlanetAtmosphere");
            giantShader = Shader.Find("CLAY/GasGiant");
            ringShader = Shader.Find("CLAY/PlanetRing");
            cloudShader = Shader.Find("CLAY/PlanetClouds");
            starShader = Shader.Find("CLAY/Star");
            coronaShader = Shader.Find("CLAY/StarCorona");
            flaresShader = Shader.Find("CLAY/StarFlares");
            glowShader = Shader.Find("CLAY/StarGlow");
            beltShader = Shader.Find("Clay/BeltPoint");
            smoothSphere = BuildUnitSphere(5);   // subdivided icosphere → smooth silhouette for stars/shells
            ringMesh = BuildRingMesh(0.15f, 96);   // wide radial span; sliders pick the visible band
            quadMesh = BuildQuad();                // camera-facing billboard for the star glow
            SetupCamera();
            root = new GameObject("SystemRoot").transform;
            root.SetParent(transform, false);
        }

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null) { var go = new GameObject("Main Camera"); go.tag = "MainCamera"; cam = go.AddComponent<Camera>(); }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.012f, 0.017f, 0.03f);
            cam.fieldOfView = 45f;
        }

        // ── generation ──────────────────────────────────────────────────────────────────────────────
        public void Regenerate()
        {
            ClearBodies();
            sys = SystemGenerator.Generate(seed, new SystemGenParams
            { starCount = Mathf.Clamp(starCount, 1, 3), planetCount = planetCount, maxInclinationDeg = maxInclinationDeg,
              metallicityOverride = float.NaN, youthBias = 0f, habitabilityBoost = 1f });
            RebuildFromSys();
        }

        // Load a PRE-GENERATED system (from the galaxy's star inference) instead of rolling a new one.
        public void LoadExternalSystem(StarSystem external)
        {
            EnsureInit();
            ClearBodies();
            sys = external;
            RebuildFromSys();
        }

        void ClearBodies()
        {
            foreach (var b in bodies)
            {
                if (b.tf) Destroy(b.tf.gameObject);
                if (b.lineTf) Destroy(b.lineTf.gameObject);
                if (b.oceanMat) Destroy(b.oceanMat);
                if (b.atmoMat) Destroy(b.atmoMat);
                if (b.ringMat) Destroy(b.ringMat);
                if (b.cloudMat) Destroy(b.cloudMat);
                if (b.coronaMat) Destroy(b.coronaMat);
                if (b.flaresMat) Destroy(b.flaresMat);
                if (b.glowMat) Destroy(b.glowMat);
                if (b.planetMesh) Destroy(b.planetMesh);
                if (b.surfaceTex) Destroy(b.surfaceTex);
            }
            bodies.Clear();
            for (int i = 0; i < beltTfs.Count; i++) if (beltTfs[i]) Destroy(beltTfs[i].gameObject);
            for (int i = 0; i < beltMats.Count; i++) if (beltMats[i]) Destroy(beltMats[i]);
            beltTfs.Clear(); beltMid.Clear(); beltMats.Clear();
        }

        void RebuildFromSys()
        {
            systemSpan = 1f;
            foreach (var pl in sys.planets) systemSpan = Mathf.Max(systemSpan, pl.orbit.semiMajorAxisAU * (1f + pl.orbit.eccentricity));
            foreach (var c in sys.companions) systemSpan = Mathf.Max(systemSpan, c.orbit.semiMajorAxisAU);

            starBodies.Clear();
            SpawnStar(sys.star, default, false);
            foreach (var c in sys.companions) SpawnStar(c, c.orbit, true);
            foreach (var pl in sys.planets)
            {
                var hb = SpawnPlanet(pl);
                foreach (var mn in pl.moons) SpawnPlanet(mn, hb);
            }
            SpawnBelts();

            focus = bodies[0];
            flownIn = false;
            focusPoint = Vector3.zero;
            camDistTarget = camDist = systemSpan * 2.2f;   // start in the iconographic overview
        }

        void SpawnStar(StarData s, OrbitElements orbit, bool orbiting)
        {
            string sname = string.IsNullOrEmpty(s.properName) ? s.Designation : s.properName;
            var b = MakeBody(sname, s.color, s.color, orbit, orbiting, true);
            b.star = s; b.starIdx = starBodies.Count; starBodies.Add(b);
            b.realRadiusAU = Mathf.Max(s.radius * SUN_R_AU, 0.002f);
            b.iconRadiusAU = systemSpan * Mathf.Lerp(0.02f, 0.05f, Mathf.InverseLerp(0.1f, 14f, s.radius));
            b.spin = 0f;   // the star shader animates its own convective churn

            if (starShader != null)
            {
                var mr = b.tf.GetComponent<MeshRenderer>();
                if (b.mat) Destroy(b.mat);
                b.mat = new Material(starShader);
                mr.sharedMaterial = b.mat;

                // Temperature-derived palette: hot cell cores lean toward white, cool lanes/spots deep red.
                Color baseC = s.color;
                Color hotC = Color.Lerp(baseC, Color.white, 0.55f);
                Color coolC = new Color(Mathf.Clamp01(baseC.r * 0.95f), baseC.g * 0.42f, baseC.b * 0.22f) * 0.9f;
                var srng = new DetRng(DetRng.Hash(sys.seed, (ulong)(s.Designation.GetHashCode()) ^ 0x57A12ADEUL));
                b.mat.SetColor("_Color", baseC);
                b.mat.SetColor("_HotColor", hotC);
                b.mat.SetColor("_CoolColor", coolC);
                b.mat.SetVector("_Seed", new Vector4(srng.Range(-40f, 40f), srng.Range(-40f, 40f), srng.Range(-40f, 40f), 0f));
                b.mat.SetFloat("_Speed", 0.6f * knobStarVolatility);
                b.mat.SetFloat("_Gran", Mathf.Lerp(16f, 8f, Mathf.InverseLerp(0.2f, 8f, s.radius)));   // big stars → larger cells
                b.mat.SetFloat("_SpotAmount", knobStarSpots);
                b.mat.SetFloat("_Brightness", 1.7f);

                // Distance-stable glow billboard — a camera-facing quad kept at ~constant screen size, so it
                // dominates (turning the star into a soft orb) once you pull back, leaving the hard detail for
                // close range. Transformed each frame in Update; a child of the star so it's cleaned up with it.
                if (glowShader != null)
                {
                    var ggo = new GameObject("Glow");
                    ggo.transform.SetParent(b.tf, false);
                    ggo.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                    b.glowMat = new Material(glowShader);
                    b.glowMat.SetColor("_Color", Color.Lerp(baseC, hotC, 0.45f));
                    b.glowMat.SetFloat("_Seed", srng.Range(0f, 50f));
                    b.glowMat.SetFloat("_Speed", 0.1f * knobStarVolatility);
                    b.glowMat.SetFloat("_Intensity", 1f);
                    ggo.AddComponent<MeshRenderer>().sharedMaterial = b.glowMat;
                    b.glowTf = ggo.transform;
                }

                // Flares / prominences — a fiery band licking just off the surface.
                if (flaresShader != null)
                {
                    const float flareScale = 1.22f;
                    b.flaresMat = new Material(flaresShader);
                    b.flaresMat.SetColor("_Color", Color.Lerp(baseC, new Color(1f, 0.3f, 0.08f), 0.5f));
                    b.flaresMat.SetColor("_HotColor", hotC);
                    b.flaresMat.SetFloat("_InnerR", 0.5f / flareScale);    // star surface radius in shell space
                    b.flaresMat.SetFloat("_Band", 0.5f - 0.5f / flareScale);   // band = shell edge − surface
                    b.flaresMat.SetFloat("_Intensity", 1.4f * knobStarFlares);
                    b.flaresMat.SetFloat("_Seed", srng.Range(0f, 50f));
                    b.flaresMat.SetFloat("_Speed", 1.2f * knobStarVolatility);
                    b.flaresTf = MakeShell(b, b.flaresMat, flareScale);
                }

                if (coronaShader != null)
                {
                    float coronaScale = knobStarCoronaSize;         // halo size (live-tunable)
                    b.coronaMat = new Material(coronaShader);
                    b.coronaMat.SetColor("_Color", Color.Lerp(baseC, hotC, 0.4f));
                    b.coronaMat.SetFloat("_InnerR", 0.5f / coronaScale);   // star surface radius in shell space
                    b.coronaMat.SetFloat("_OuterR", 0.5f);
                    b.coronaMat.SetFloat("_Intensity", 1.4f * knobStarCoronaDensity);
                    b.coronaMat.SetFloat("_Curl", knobStarCoronaArc);
                    b.coronaMat.SetFloat("_Detail", 0.6f);
                    b.coronaMat.SetFloat("_Seed", srng.Range(0f, 50f));
                    b.coronaMat.SetFloat("_Speed", 0.35f * knobStarVolatility);
                    b.coronaTf = MakeShell(b, b.coronaMat, coronaScale);
                }
            }
            else { b.mat.SetFloat("_Ambient", 1f); b.mat.SetFloat("_Emission", 1.7f); b.mat.SetColor("_RimColor", s.color); }
        }

        Body SpawnPlanet(PlanetData pl, Body host = null)
        {
            Color day = PlanetColor(pl), night = day * 0.05f;
            var b = MakeBody(pl.name, day, night, pl.orbit, host == null, false);
            b.planet = pl;
            b.host = host; b.moonIdx = pl.index;
            if (host != null)   // moons get their own faint orbit ring drawn about the host (see the Update moon branch)
            {
                var lgo = new GameObject("MoonOrbit"); lgo.transform.SetParent(root, false);
                b.lineTf = lgo.transform;
                b.line = lgo.AddComponent<LineRenderer>();
                b.line.useWorldSpace = false; b.line.loop = true; b.line.numCornerVertices = 2;
                b.line.material = new Material(Shader.Find("Sprites/Default"));
                b.line.startColor = b.line.endColor = new Color(0.6f, 0.72f, 0.9f, 0.28f);
                b.lineCol = b.line.startColor;
                b.line.numCornerVertices = 0;
                b.line.positionCount = 32; b.line.enabled = showOrbits;
            }
            b.realRadiusAU = pl.radiusEarth * EARTH_R_AU;
            b.iconRadiusAU = systemSpan * Mathf.Lerp(0.008f, 0.03f, Mathf.InverseLerp(0.5f, 12f, pl.radiusEarth));
            b.mat.SetColor("_RimColor", pl.habClass == HabClass.Habitable ? new Color(0.45f, 0.8f, 1f) : new Color(0.45f, 0.55f, 0.75f));
            // Spin from the planet's real rotation period: a 24 h day = 1°/s at the default time scale; retrograde
            // rotators (obliquity > 90°) spin backwards. (Was a flat 8°/s for every world.)
            b.spin = Mathf.Clamp(24f / Mathf.Max(pl.rotationHours, 0.5f), 0.03f, 8f) * (pl.axialTiltDeg > 90f ? -1f : 1f);
            bool giant = pl.type == PlanetType.GasGiant || pl.type == PlanetType.IceGiant;
            ulong pseed = DetRng.Hash(sys.seed, (ulong)(pl.index + 1));

            if (giant && giantShader != null) SetupGiant(b, pl, pseed);
            else
            {
                // Displaced mesh (silhouette) + equirectangular surface texture (per-pixel colour/relief).
                b.planetMesh = PlanetTexture.BuildMesh(pl, pseed, PlanetSubdiv);
                b.mf.sharedMesh = b.planetMesh;
                b.mat.SetFloat("_UseTex", 1f);
                b.mat.SetFloat("_Ambient", knobAmbient);
                b.mat.SetFloat("_RimPower", knobRim);
                b.mat.SetFloat("_BumpScale", knobBump);
                b.mat.SetFloat("_CamFill", knobCamFill);
                BakePlanetTexAsync(b, SurfaceTexLow); // async first pass (no main-thread stall); hi-res on focus

                // Ocean is rendered by the SURFACE shader itself (the sea colour is already baked into the albedo) —
                // no separate shell, so there is no z-fighting at the tiny true-AU planet scale. The surface shader
                // adds the sun-glint/sheen on water texels (height ≈ sea level).
                if (PlanetTexture.HasOcean(pl, pseed))
                {
                    b.mat.SetFloat("_HasOcean", 1f);
                    b.mat.SetFloat("_SeaLevel", PlanetTexture.SeaLevel(pl, pseed));
                    b.mat.SetFloat("_OceanSpecPower", knobOceanSpec);
                    b.mat.SetFloat("_OceanSpecGain", knobOceanGain);
                }
                else b.mat.SetFloat("_HasOcean", 0f);
            }

            // Atmosphere haze shell — rocky worlds only (a giant's own shader already looks gaseous).
            if (!giant && PlanetTexture.HasAtmosphere(pl) && atmoShader != null)
            {
                b.atmoMat = new Material(atmoShader);
                b.atmoMat.SetColor("_AtmColor", PlanetTexture.AtmosphereColor(pl, pseed));
                b.atmoMat.SetFloat("_InnerR", 0.5f / AtmoShellScale);   // planet surface radius in shell space
                b.atmoMat.SetFloat("_OuterR", 0.5f);
                ApplyAtmoKnobs(b.atmoMat, giant);
                b.atmoMat.SetFloat("_Intensity", knobAtmoIntensity * PlanetTexture.AtmosphereDensity(pl));   // by composition
                b.atmoTf = MakeShell(b, b.atmoMat, AtmoShellScale);

                // Dynamic cloud deck — sits just above the surface, drifts slowly, lit by the star. Coverage
                // scales with how wet the world is; hazy/cold worlds get tinted decks, temperate wet worlds
                // near-white. Skipped on essentially cloudless worlds (very dry + no volatiles).
                // Partial, broken cover — a full-sky deck (old formula peaked ~0.86) turned wet worlds into
                // featureless white balls and buried the terminator. Keep it well below full so surface,
                // continents, and the day/night line all read through the gaps.
                // ── Cloud / haze character by composition (diverse atmospheres) ──
                var crng = new DetRng(DetRng.Hash(pseed, 0x0C10D5UL));
                var cchem = PlanetTexture.Chem(pl, pseed);
                float tc = pl.meanTempC;
                // Baselines (temperate water world): white, broken, moderately swirled.
                Color cloudC = Color.Lerp(PlanetTexture.AtmosphereColor(pl, pseed), Color.white, tc > -40f ? 0.7f : 0.45f);
                float cover = Mathf.Clamp01(0.10f + pl.waterCoverage * 0.5f + (pl.habClass == HabClass.Habitable ? 0.08f : 0f));
                float sharp = crng.Range(0.10f, 0.22f), swirl = crng.Range(0.03f, 0.2f), haze = 0f, speed = crng.Range(0.02f, 0.08f), dens = 0.9f;
                switch (cchem.theme)
                {
                    case PlanetTexture.ChemTheme.Sulfuric:                    // Venusian: thick sulfuric overcast, some banding
                        cloudC = new Color(0.88f, 0.80f, 0.55f); haze = crng.Range(0.45f, 0.75f); cover = 0.95f; swirl = crng.Range(0.3f, 0.6f); dens = 1f; break;
                    case PlanetTexture.ChemTheme.Tholin:                      // organic orange photochemical haze
                    case PlanetTexture.ChemTheme.Methanic:
                        cloudC = new Color(0.82f, 0.56f, 0.34f); haze = crng.Range(0.2f, 0.5f); cover = 0.7f; swirl = crng.Range(0.2f, 0.45f); break;
                    case PlanetTexture.ChemTheme.AmmoniaIce:                  // pale ammonia, mild banding
                        cloudC = new Color(0.86f, 0.82f, 0.68f); cover = crng.Range(0.4f, 0.8f); swirl = crng.Range(0.25f, 0.5f); break;
                    case PlanetTexture.ChemTheme.Biosphere:                   // clean white turbulent water clouds
                        cloudC = new Color(0.97f, 0.98f, 1f); swirl = crng.Range(0.03f, 0.15f); break;
                    case PlanetTexture.ChemTheme.Cupric:                      // faint teal chemical haze
                        cloudC = new Color(0.72f, 0.86f, 0.80f); cover *= 0.8f; break;
                    case PlanetTexture.ChemTheme.Ferrous:                     // dusty, thin, ochre
                    case PlanetTexture.ChemTheme.Silicate:
                    case PlanetTexture.ChemTheme.Basaltic:
                        cloudC = new Color(0.84f, 0.74f, 0.60f); cover *= 0.65f; swirl = crng.Range(0.05f, 0.2f); break;
                }
                if (tc > 200f) { haze = Mathf.Max(haze, 0.5f); cover = Mathf.Max(cover, 0.9f); }   // runaway greenhouse → opaque
                // Slightly desaturate the cloud tint for realism.
                float cg = cloudC.r * 0.299f + cloudC.g * 0.587f + cloudC.b * 0.114f;
                cloudC = Color.Lerp(new Color(cg, cg, cg), cloudC, 0.8f);

                if (cloudShader != null && (cover > 0.22f || haze > 0.05f))
                {
                    b.cloudMat = new Material(cloudShader);
                    b.cloudMat.SetColor("_CloudColor", cloudC);
                    b.cloudMat.SetFloat("_Coverage", cover);
                    b.cloudMat.SetFloat("_Sharp", sharp);
                    b.cloudMat.SetFloat("_Swirl", swirl);
                    b.cloudMat.SetFloat("_Haze", haze);
                    b.cloudMat.SetFloat("_Density", dens);
                    b.cloudMat.SetFloat("_Speed", speed);
                    b.cloudMat.SetVector("_Seed", new Vector4(crng.Range(0f, 20f), crng.Range(0f, 20f), crng.Range(0f, 20f), 0f));
                    b.cloudTf = MakeShell(b, b.cloudMat, CloudShellScale);
                }
            }

            // Rotational identity (roadmap §I): tilt the spin axis by the world's obliquity (so caps/bands tilt
            // with it, and >90° reads as retrograde), and flatten fast rotators along that axis into an oblate
            // spheroid. Planets only — moons spin slowly, and tidally-locked worlds are oriented each frame.
            if (host == null && !pl.tidallyLocked)
            {
                var trng = new DetRng(pseed ^ 0x7115A5UL);
                b.tf.localRotation = Quaternion.Euler(0f, trng.Range(0f, 360f), pl.axialTiltDeg);
                if (!giant)
                {
                    float flat = Mathf.Clamp01((7f - pl.rotationHours) / 7f);   // <7 h → increasingly oblate
                    b.oblateY = 1f - flat * 0.16f;                             // up to ~16% polar flattening
                }
            }
            return b;
        }

        // Asteroid / ice belts as point-cloud rings (positions in AU; scaled per-frame like the orbit lines).
        void SpawnBelts()
        {
            if (sys.belts == null) return;
            foreach (var belt in sys.belts)
            {
                var go = new GameObject(belt.icy ? "IceBelt" : "AsteroidBelt");
                go.transform.SetParent(root, false);
                go.transform.localRotation = Quaternion.Euler(belt.tiltDeg, 0f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = BuildBeltMesh(belt);
                var mat = beltShader != null ? new Material(beltShader) : new Material(surfaceShader);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", belt.color);
                if (mat.HasProperty("_DayColor")) mat.SetColor("_DayColor", belt.color);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                beltTfs.Add(go.transform); beltMid.Add((belt.innerAU + belt.outerAU) * 0.5f); beltMats.Add(mat);
            }
        }

        Mesh BuildBeltMesh(AsteroidBelt belt)
        {
            var rng = new DetRng(sys.seed ^ (ulong)(belt.icy ? 0x1CEUL : 0xA57UL));
            int n = Mathf.Clamp(belt.count, 1, 4000);
            var v = new Vector3[n]; var idx = new int[n];
            for (int i = 0; i < n; i++)
            {
                float ang = rng.Value * Mathf.PI * 2f;
                float rr = Mathf.Lerp(belt.innerAU, belt.outerAU, Mathf.Sqrt(rng.Value));
                float y = (rng.Value - 0.5f) * (belt.outerAU - belt.innerAU) * 0.06f;
                v[i] = new Vector3(Mathf.Cos(ang) * rr, y, Mathf.Sin(ang) * rr); idx[i] = i;
            }
            var mesh = new Mesh { name = "Belt" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = v;
            mesh.SetIndices(idx, MeshTopology.Points, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (belt.outerAU * 2f + 1f));
            return mesh;
        }

        // Gas / ice giants get the live animated shader (per-pixel turbulence, no bake, no ocean, no mesh spin).
        void SetupGiant(Body b, PlanetData pl, ulong pseed)
        {
            b.mf.sharedMesh = smoothSphere;
            var mr = b.tf.GetComponent<MeshRenderer>();
            if (b.mat) Destroy(b.mat);
            b.mat = new Material(giantShader);
            mr.sharedMaterial = b.mat;
            b.spin = 0f;   // the shader animates rotation + flow itself

            var rng = new DetRng(pseed ^ 0xA5A5A5A5UL);
            GiantPalette(pl, ref rng, out Color eq, out Color mid, out Color pole, out Color storm);
            b.mat.SetColor("_ColEq", eq);
            b.mat.SetColor("_ColMid", mid);
            b.mat.SetColor("_ColPole", pole);
            b.mat.SetColor("_StormColor", storm);
            b.mat.SetVector("_Seed", new Vector4(rng.Range(-50f, 50f), rng.Range(-50f, 50f), rng.Range(-50f, 50f), 0f));
            // Wide ranges so no two giants look the same: few broad bands ↔ many thin; smooth ↔ chaotic.
            b.mat.SetFloat("_BandFreq", rng.Range(3f, 12f));
            b.mat.SetFloat("_Turb", rng.Range(0.25f, 1.1f));
            b.mat.SetFloat("_FineTurb", rng.Range(0.2f, 1.1f));
            b.mat.SetFloat("_BandVar", rng.Range(0.5f, 1.7f));
            b.mat.SetFloat("_Speed", rng.Range(0.06f, 0.15f));
            b.mat.SetFloat("_Ambient", knobAmbient);

            // Chemical complexity — extra cloud constituents in accent hues; metal-rich worlds get more variety.
            bool ice = pl.type == PlanetType.IceGiant;
            b.mat.SetFloat("_ChemAmt", Mathf.Clamp01(rng.Range(0.3f, 0.7f) + pl.mineralDiversity * 0.3f));
            b.mat.SetColor("_Chem1", ice ? new Color(0.86f, 0.94f, 0.98f) : new Color(0.95f, 0.92f, 0.84f));
            b.mat.SetColor("_Chem2", GiantChem2(pl, ref rng));

            // Great spot — a swirling vortex whose PRESENCE is gated by a likelihood (the "Storm likelihood"
            // knob), not opacity. Each giant rolls a potential; it shows when potential < likelihood.
            b.giantSpotPotential = rng.Value;
            float spotLat = (rng.Value < 0.5f ? -1f : 1f) * rng.Range(0.18f, 0.5f);
            b.mat.SetVector("_Spot", new Vector4(spotLat, rng.Range(-3.1f, 3.1f), rng.Range(0.16f, 0.42f),   // varied size
                                                 b.giantSpotPotential < knobGiantSpot ? 1f : 0f));
            b.mat.SetColor("_SpotColor", ice ? new Color(0.10f, 0.14f, 0.34f) : storm);   // dark spot vs red spot

            // ── Subtype signature (roadmap §K): reshape bands/turbulence/storm/haze/palette by kind ──
            switch (PlanetTexture.GiantSubtype(pl, pseed))
            {
                case PlanetTexture.GiantType.HotJupiter:      // scorched & puffed: tight fierce bands, dusky red
                    b.mat.SetFloat("_BandFreq", rng.Range(8f, 14f));
                    b.mat.SetFloat("_Turb", rng.Range(0.9f, 1.4f));
                    b.mat.SetFloat("_Speed", rng.Range(0.14f, 0.24f));
                    b.mat.SetColor("_ColEq", new Color(0.56f, 0.29f, 0.24f));
                    b.mat.SetColor("_ColMid", new Color(0.42f, 0.22f, 0.22f));
                    b.mat.SetColor("_ColPole", new Color(0.38f, 0.27f, 0.29f));
                    b.mat.SetColor("_StormColor", new Color(0.96f, 0.62f, 0.36f));
                    break;
                case PlanetTexture.GiantType.SubNeptune:       // gas dwarf: featureless, thick muted haze
                    b.mat.SetFloat("_BandFreq", rng.Range(2f, 4f));
                    b.mat.SetFloat("_Turb", rng.Range(0.12f, 0.35f));
                    b.mat.SetFloat("_FineTurb", 0.2f);
                    b.mat.SetFloat("_ChemAmt", rng.Range(0.55f, 0.85f));
                    b.mat.SetVector("_Spot", Vector4.zero);
                    break;
                case PlanetTexture.GiantType.HeliumGiant:      // H rained out: pale grey, near-featureless
                    b.mat.SetFloat("_BandFreq", rng.Range(1.5f, 3f));
                    b.mat.SetFloat("_Turb", rng.Range(0.08f, 0.28f));
                    b.mat.SetFloat("_BandVar", 0.5f);
                    b.mat.SetVector("_Spot", Vector4.zero);
                    Color he = new Color(0.80f, 0.80f, 0.83f);
                    b.mat.SetColor("_ColEq", he);
                    b.mat.SetColor("_ColMid", he * 0.9f);
                    b.mat.SetColor("_ColPole", he * 0.96f);
                    break;
                case PlanetTexture.GiantType.Superstorm:        // storm-dominated: chaotic, one great vortex forced on
                    b.mat.SetFloat("_Turb", rng.Range(1.1f, 1.6f));
                    b.mat.SetFloat("_BandVar", rng.Range(1.4f, 2f));
                    b.mat.SetFloat("_FineTurb", rng.Range(0.8f, 1.2f));
                    float slat = (rng.Value < 0.5f ? -1f : 1f) * rng.Range(0.12f, 0.4f);
                    b.mat.SetVector("_Spot", new Vector4(slat, rng.Range(-3.1f, 3.1f), rng.Range(0.34f, 0.5f), 1f));
                    break;
            }

            // Rings — a chance of a Saturn-style system: a tilted annulus sized to the planet.
            if (ringShader != null && rng.Value < 0.45f)
            {
                var rgo = new GameObject("Ring");
                rgo.transform.SetParent(b.tf, false);
                rgo.transform.localScale = Vector3.one * rng.Range(1.9f, 2.7f);          // outer ≈ N × planet radius
                rgo.transform.localRotation = Quaternion.Euler(rng.Range(8f, 32f), rng.Range(0f, 360f), 0f);
                b.ringTf = rgo.transform;
                rgo.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                b.ringMat = new Material(ringShader);
                b.ringMat.SetColor("_ColorA", ice ? new Color(0.72f, 0.80f, 0.86f) : new Color(0.82f, 0.75f, 0.62f));
                b.ringMat.SetColor("_ColorB", ice ? new Color(0.48f, 0.58f, 0.70f) : new Color(0.55f, 0.50f, 0.42f));
                b.ringMat.SetFloat("_Seed", rng.Range(0f, 100f));
                b.ringMat.SetFloat("_Density", rng.Range(0.6f, 0.95f));
                rgo.AddComponent<MeshRenderer>().sharedMaterial = b.ringMat;
            }
        }

        // Second cloud constituent, flavoured by the giant's chemistry regime (temperature).
        Color GiantChem2(PlanetData pl, ref DetRng rng)
        {
            float tc = pl.meanTempC;
            int k = rng.RangeInt(0, 3);
            if (pl.type == PlanetType.IceGiant || tc < -150f)      // methane realm — white / teal / violet
                return k == 0 ? new Color(0.86f, 0.95f, 0.98f) : k == 1 ? new Color(0.28f, 0.66f, 0.62f) : new Color(0.46f, 0.40f, 0.70f);
            if (tc < -40f)                                          // ammonia realm — sulfur gold / red-brown / white
                return k == 0 ? new Color(0.88f, 0.66f, 0.24f) : k == 1 ? new Color(0.72f, 0.30f, 0.20f) : new Color(0.94f, 0.90f, 0.80f);
            return k == 0 ? new Color(0.92f, 0.55f, 0.20f) : k == 1 ? new Color(0.78f, 0.24f, 0.20f) : new Color(0.48f, 0.54f, 0.66f);  // hot — orange / red / grey-blue
        }

        // Per-giant latitude palette (equator → mid → pole), driven by the giant's CHEMISTRY REGIME, which is
        // set by temperature: cold = methane blues (Neptune/Uranus), temperate = ammonia tan/gold/brown
        // (Jupiter/Saturn), hot = dusky reddish-brown. Plus per-planet variety within each regime.
        void GiantPalette(PlanetData pl, ref DetRng rng, out Color eq, out Color mid, out Color pole, out Color storm)
        {
            float tc = pl.meanTempC;
            float j = rng.Range(-0.06f, 0.06f), j2 = rng.Range(-0.05f, 0.05f);
            float p = rng.Value;   // sub-variety within the regime

            // ~28% of giants get a vivid "exotic chemistry" palette — wild colour variety beyond grey/blue.
            if (rng.Value < 0.28f)
            {
                switch (rng.RangeInt(0, 6))
                {
                    case 0:  eq = new Color(0.45f, 0.66f, 0.35f); mid = new Color(0.32f, 0.50f, 0.28f); pole = new Color(0.70f, 0.80f, 0.55f); storm = new Color(0.86f, 0.90f, 0.50f); return;  // chlorine green
                    case 1:  eq = new Color(0.56f, 0.42f, 0.70f); mid = new Color(0.42f, 0.32f, 0.56f); pole = new Color(0.74f, 0.68f, 0.84f); storm = new Color(0.86f, 0.50f, 0.72f); return;  // phosphorus violet
                    case 2:  eq = new Color(0.87f, 0.55f, 0.48f); mid = new Color(0.72f, 0.42f, 0.38f); pole = new Color(0.80f, 0.72f, 0.68f); storm = new Color(0.92f, 0.40f, 0.34f); return;  // salmon
                    case 3:  eq = new Color(0.28f, 0.68f, 0.68f); mid = new Color(0.20f, 0.52f, 0.56f); pole = new Color(0.60f, 0.84f, 0.84f); storm = new Color(0.88f, 0.96f, 0.90f); return;  // turquoise
                    case 4:  eq = new Color(0.84f, 0.58f, 0.26f); mid = new Color(0.62f, 0.42f, 0.22f); pole = new Color(0.50f, 0.46f, 0.40f); storm = new Color(0.92f, 0.72f, 0.34f); return;  // deep amber
                    default: eq = new Color(0.78f, 0.60f, 0.62f); mid = new Color(0.58f, 0.50f, 0.56f); pole = new Color(0.72f, 0.74f, 0.80f); storm = new Color(0.90f, 0.56f, 0.56f); return;  // rose-grey
                }
            }

            if (pl.type == PlanetType.IceGiant || tc < -170f)          // methane — deep Neptune ↔ pale Uranus, teal-leaning
            {
                eq   = Color.Lerp(new Color(0.12f, 0.32f, 0.66f), new Color(0.34f, 0.68f, 0.76f), p) + new Color(j, j2, 0f);
                mid  = Color.Lerp(new Color(0.20f, 0.46f, 0.74f), new Color(0.46f, 0.74f, 0.82f), p);
                pole = Color.Lerp(new Color(0.48f, 0.68f, 0.84f), new Color(0.74f, 0.88f, 0.92f), p);
                storm = new Color(0.09f, 0.13f, 0.34f);
            }
            else if (tc < -95f)                                         // cold — was grey; now icy-blue / sandy-tan / dusky-teal
            {
                switch (rng.RangeInt(0, 3))
                {
                    case 0:  eq = new Color(0.60f + j, 0.72f, 0.82f); mid = new Color(0.50f, 0.62f, 0.74f); pole = new Color(0.72f, 0.82f, 0.88f); break;  // icy blue
                    case 1:  eq = new Color(0.80f + j, 0.72f, 0.56f); mid = new Color(0.66f, 0.58f, 0.46f); pole = new Color(0.62f, 0.62f, 0.60f); break;  // sandy tan
                    default: eq = new Color(0.44f + j, 0.66f, 0.60f); mid = new Color(0.34f, 0.54f, 0.52f); pole = new Color(0.62f, 0.76f, 0.72f); break;  // dusky teal
                }
                storm = new Color(0.40f, 0.50f, 0.62f);
            }
            else if (tc < -40f)                                         // ammonia — Saturn gold ↔ Jupiter brown
            {
                eq   = Color.Lerp(new Color(0.90f, 0.80f, 0.56f), new Color(0.83f, 0.60f, 0.39f), p) + new Color(j, j2, 0f);
                mid  = Color.Lerp(new Color(0.78f, 0.68f, 0.50f), new Color(0.68f, 0.50f, 0.36f), p);
                pole = new Color(0.55f, 0.56f, 0.60f);     storm = new Color(0.86f, 0.40f, 0.24f);
            }
            else if (tc < 120f)                                         // warm — browner/oranger (phosphine/sulfur)
            {
                eq   = new Color(0.80f + j, 0.55f, 0.34f); mid = new Color(0.66f, 0.44f, 0.30f);
                pole = new Color(0.55f, 0.50f, 0.48f);     storm = new Color(0.92f, 0.52f, 0.30f);
            }
            else                                                        // hot — dusky reddish-brown
            {
                eq   = new Color(0.56f + j, 0.33f, 0.29f); mid = new Color(0.45f, 0.28f, 0.26f);
                pole = new Color(0.40f, 0.34f, 0.36f);     storm = new Color(0.88f, 0.46f, 0.36f);
            }
        }

        void ApplyOceanKnobs(Material m)
        {
            m.SetFloat("_SpecPower", knobOceanSpec);
            m.SetFloat("_SpecGain", knobOceanGain);
            m.SetFloat("_Fresnel", knobOceanFres);
            m.SetFloat("_Opacity", knobOceanOpacity);
        }

        void ApplyAtmoKnobs(Material m, bool giant)
        {
            m.SetFloat("_Intensity", knobAtmoIntensity * (giant ? 1.4f : 1f));
            m.SetFloat("_Softness", knobAtmoSoft);
        }

        // Synchronous bake (used for the cheap low-res spawn pass).
        void BakePlanetTex(Body b, int width)
        {
            ulong pseed = DetRng.Hash(sys.seed, (ulong)(b.planet.index + 1));
            AssignSurfaceTex(b, PlanetTexture.BakeSurface(b.planet, pseed, width), width);
        }

        // Kicks off a background (thread-pool) bake so the main thread never stalls; finalized in Update.
        void BakePlanetTexAsync(Body b, int width)
        {
            if (b.bakeTask != null) return;
            ulong pseed = DetRng.Hash(sys.seed, (ulong)(b.planet.index + 1));
            var pl = b.planet;
            b.bakeWidth = width;
            b.bakeTask = Task.Run(() => PlanetTexture.ComputeSurface(pl, pseed, width));
        }

        void AssignSurfaceTex(Body b, Texture2D tex, int width)
        {
            if (b.surfaceTex) Destroy(b.surfaceTex);
            b.surfaceTex = tex;
            b.mat.SetTexture("_SurfaceTex", tex);
            if (b.oceanMat != null) b.oceanMat.SetTexture("_SurfaceTex", tex);   // ocean clips against the current map
            // Bump is sampled over a FIXED angular step (not one texel) so relief stays consistent as the
            // colour resolution climbs — otherwise higher-res textures would flatten the shading.
            b.mat.SetFloat("_TexelU", 1f / 1024f);
            b.mat.SetFloat("_TexelV", 1f / 512f);
            b.texWidth = width;
            b.hiRes = width >= SurfaceTexHi;
        }

        static bool IsGiant(PlanetData p) => p.type == PlanetType.GasGiant || p.type == PlanetType.IceGiant;

        // Keep the focused planet's texture resolution matched to how big it is on screen, so it never looks
        // soft — 2048 at normal framing, 4096 when you zoom right up to it. Upgrades only (async, cached).
        void UpgradeFocusRes()
        {
            if (focus == null || focus.planet == null || IsGiant(focus.planet) || focus.bakeTask != null) return;
            if (!focus.hiRes) return;   // wait for the initial 1024 focus bake first

            float k = Screen.height * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float apparentPx = RenderRadius(focus) / Mathf.Max(camDist, 1e-5f) * k;   // on-screen radius, px
            int want = apparentPx < 140f ? SurfaceTexHi : apparentPx < 380f ? SurfaceTexUltra : SurfaceTex4K;
            if (want > focus.texWidth) BakePlanetTexAsync(focus, want);
        }

        // A child sphere used as a shell (ocean / atmosphere) — a smooth icosphere so the limb never facets.
        // Inherits the planet's transform, so it tracks position, spin and the scale blend automatically.
        Transform MakeShell(Body b, Material mat, float scale)
        {
            var go = new GameObject(mat.shader.name.Contains("Ocean") ? "Ocean" : mat.shader.name.Contains("Clouds") ? "Clouds" : mat.shader.name.Contains("Corona") ? "Corona" : mat.shader.name.Contains("Flares") ? "Flares" : "Atmosphere");
            go.transform.SetParent(b.tf, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = smoothSphere;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // A flat annulus in the XZ plane (outer radius 0.5, inner = innerFrac·0.5) with radial UV.x (0 at the
        // inner edge → 1 at the outer). Used for gas-giant rings.
        static Mesh BuildRingMesh(float innerFrac, int segments)
        {
            float ri = innerFrac * 0.5f, ro = 0.5f;
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[(segments + 1) * 2];
            var tris = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                verts[i * 2] = new Vector3(ca * ri, 0f, sa * ri); uvs[i * 2] = new Vector2(0f, i / (float)segments);
                verts[i * 2 + 1] = new Vector3(ca * ro, 0f, sa * ro); uvs[i * 2 + 1] = new Vector2(1f, i / (float)segments);
            }
            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                tris[i * 6] = b; tris[i * 6 + 1] = b + 1; tris[i * 6 + 2] = b + 2;
                tris[i * 6 + 3] = b + 2; tris[i * 6 + 4] = b + 1; tris[i * 6 + 5] = b + 3;
            }
            var m = new Mesh { name = "PlanetRing" };
            m.SetVertices(new List<Vector3>(verts));
            m.uv = uvs;
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // A unit quad centred at the origin in the XY plane, UV 0→1. Generous bounds so the CPU-billboarded
        // star glow (which is scaled up in the transform, not the mesh) is never frustum-culled early.
        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "StarGlowQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }

        // A unit-radius (0.5) subdivided icosphere with smooth (normalized) normals.
        static Mesh BuildUnitSphere(int subdiv)
        {
            Icosphere.Build(subdiv, out var dirs, out var tris);
            var verts = new Vector3[dirs.Length];
            for (int i = 0; i < dirs.Length; i++) verts[i] = dirs[i] * 0.5f;
            var m = new Mesh { name = $"UnitSphere_L{subdiv}" };
            if (dirs.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.SetNormals(dirs);   // outward radial = smooth sphere normals
            m.RecalculateBounds();
            return m;
        }

        Body MakeBody(string name, Color day, Color night, OrbitElements orbit, bool orbiting, bool isStar)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = smoothSphere;
            var mr = go.AddComponent<MeshRenderer>();

            var mat = new Material(surfaceShader);
            mat.SetColor("_DayColor", day); mat.SetColor("_NightColor", night);
            mat.SetFloat("_Ambient", isStar ? 1f : 0.05f);
            mr.sharedMaterial = mat;

            var b = new Body { tf = go.transform, mat = mat, mf = go.GetComponent<MeshFilter>(), orbit = orbit, orbiting = orbiting, isStar = isStar, label = name };
            b.realDistAU = orbit.semiMajorAxisAU;
            // Iconographic distance: compress the huge inner/outer range so inner planets read (exp 0.5).
            b.iconDistAU = orbiting ? systemSpan * Mathf.Pow(Mathf.Clamp01(b.realDistAU / systemSpan), 0.5f) : 0f;

            if (orbiting)
            {
                var lgo = new GameObject("Orbit"); lgo.transform.SetParent(root, false);
                b.lineTf = lgo.transform;
                b.line = lgo.AddComponent<LineRenderer>();
                b.line.useWorldSpace = false; b.line.loop = true; b.line.numCornerVertices = 2;
                b.line.material = new Material(Shader.Find("Sprites/Default"));
                b.line.startColor = b.line.endColor = isStar ? new Color(0.75f, 0.78f, 1f, 0.22f) : new Color(0.55f, 0.66f, 0.9f, 0.32f);
                b.lineCol = b.line.startColor;
                b.line.positionCount = 2;   // resized adaptively in Update
                b.line.enabled = showOrbits;
            }
            bodies.Add(b);
            return b;
        }

        static Color PlanetColor(PlanetData p)
        {
            switch (p.type)
            {
                case PlanetType.Ocean:       return new Color(0.24f, 0.55f, 0.85f);
                case PlanetType.Terrestrial: return p.habClass == HabClass.Habitable ? new Color(0.38f, 0.62f, 0.45f) : new Color(0.68f, 0.55f, 0.4f);
                case PlanetType.Desert:      return new Color(0.78f, 0.6f, 0.38f);
                case PlanetType.FrozenRock:  return new Color(0.8f, 0.86f, 0.92f);
                case PlanetType.GasGiant:    return new Color(0.82f, 0.7f, 0.52f);
                case PlanetType.IceGiant:    return new Color(0.5f, 0.72f, 0.85f);
            }
            return Color.gray;
        }

        // distance/size at the current blend: iconographic → true
        float DistScale(Body b) => b.realDistAU <= 1e-6f ? 1f : Mathf.Lerp(b.iconDistAU / b.realDistAU, 1f, scaleT);
        // ── multiple stars ─────────────────────────────────────────────────────────────────────────────
        readonly List<Body> starBodies = new();
        Vector3[] starDisp = new Vector3[1], starReal = new Vector3[1];
        Vector3 baryABDisp, baryABReal; float muB, muC;
        struct SunPick { public int i1, i2; public float w2; }
        readonly Dictionary<Body, SunPick> sunPicks = new();

        /// Two-body motion of A and B about their barycentre (period from their combined mass), with a hierarchical
        /// tertiary C orbiting the A+B barycentre. Display positions use each companion's iconographic scale.
        void ComputeStarFrame(float t)
        {
            int n = starBodies.Count;
            if (starDisp.Length != Mathf.Max(1, n)) { starDisp = new Vector3[Mathf.Max(1, n)]; starReal = new Vector3[Mathf.Max(1, n)]; }
            for (int i = 0; i < starDisp.Length; i++) { starDisp[i] = Vector3.zero; starReal[i] = Vector3.zero; }
            baryABDisp = baryABReal = Vector3.zero; muB = muC = 0f;
            if (sys == null || sys.companions.Count == 0 || n < 2) return;
            var A = sys.star; var B = sys.companions[0];
            float Mab = A.stellarMass + B.stellarMass;
            muB = B.stellarMass / Mab;
            float pB = B.orbit.PeriodYears(Mab);
            Vector3 relB = B.orbit.PositionAt(B.orbit.meanAnomalyDeg * Mathf.Deg2Rad + Mathf.PI * 2f * t / Mathf.Max(pB, 1e-4f));
            float dsB = DistScale(starBodies[1]);
            starReal[0] = -muB * relB; starReal[1] = (1f - muB) * relB;
            starDisp[0] = starReal[0] * dsB; starDisp[1] = starReal[1] * dsB;
            if (sys.companions.Count > 1 && n > 2)
            {
                var C = sys.companions[1];
                float Mt = Mab + C.stellarMass;
                muC = C.stellarMass / Mt;
                float pC = C.orbit.PeriodYears(Mt);
                Vector3 relC = C.orbit.PositionAt(C.orbit.meanAnomalyDeg * Mathf.Deg2Rad + Mathf.PI * 2f * t / Mathf.Max(pC, 1e-4f));
                float dsC = DistScale(starBodies[2]);
                baryABReal = -muC * relC; baryABDisp = baryABReal * dsC;
                starReal[2] = (1f - muC) * relC; starDisp[2] = starReal[2] * dsC;
                for (int i = 0; i < 2; i++) { starReal[i] += baryABReal; starDisp[i] += baryABDisp; }
            }
        }

        /// Direction (root space) toward the star delivering the most flux to this body, remembering the runner-up
        /// and its relative strength for the second light. Flux uses TRUE orbital positions (L / d²).
        Vector3 LightFor(Body b, out SunPick pick)
        {
            Vector3 pr = b.host != null ? b.host.realPos : b.realPos;
            float f1 = -1f, f2 = -1f; int i1 = 0, i2 = -1;
            for (int i = 0; i < starBodies.Count; i++)
            {
                float d2 = Mathf.Max((starReal[i] - pr).sqrMagnitude, 1e-6f);
                float f = starBodies[i].star.luminosity / d2;
                if (f > f1) { f2 = f1; i2 = i1; f1 = f; i1 = i; }
                else if (f > f2) { f2 = f; i2 = i; }
            }
            if (i2 == i1) i2 = -1;
            // relative brightness of the second sun, gently compressed so a faint companion still reads as a light
            pick = new SunPick { i1 = i1, i2 = i2, w2 = i2 >= 0 ? Mathf.Clamp01(Mathf.Pow(f2 / Mathf.Max(f1, 1e-9f), 0.6f)) : 0f };
            sunPicks[b] = pick;
            return starDisp[i1] - b.tf.localPosition;
        }

        void ApplySecondSun(Body b)
        {
            Color c1 = Color.white, c2 = Color.white; Vector4 d2 = Vector4.zero;
            if (sunPicks.TryGetValue(b, out var pk) && starBodies.Count > 1)
            {
                c1 = Color.Lerp(Color.white, starBodies[pk.i1].star.color, 0.45f);
                if (pk.i2 >= 0 && pk.w2 > 0.001f)
                {
                    Vector3 to2 = starDisp[pk.i2] - b.tf.localPosition;
                    if (to2.sqrMagnitude > 1e-20f)
                    {
                        Vector3 o = b.tf.InverseTransformDirection(to2.normalized);
                        d2 = new Vector4(o.x, o.y, o.z, pk.w2);
                        c2 = Color.Lerp(Color.white, starBodies[pk.i2].star.color, 0.6f);
                    }
                }
            }
            foreach (var m in new[] { b.mat, b.atmoMat, b.cloudMat })
            {
                if (m == null) continue;
                m.SetVector("_Sun2Dir", d2); m.SetColor("_Sun2Col", c2); m.SetColor("_Sun1Col", c1);
            }
        }

        /// For landing: the brightest star (sun 1) and the runner-up (sun 2) as seen from this body.
        void SunsFor(Body b, out Body s1, out Body s2, out float w2)
        {
            s1 = bodies[0]; s2 = null; w2 = 0f;
            if (starBodies.Count < 2) return;
            LightFor(b, out var pk);
            s1 = starBodies[pk.i1]; if (pk.i2 >= 0) { s2 = starBodies[pk.i2]; w2 = pk.w2; }
        }

        float RenderRadius(Body b) => Mathf.Max(Mathf.Lerp(b.iconRadiusAU, b.realRadiusAU, scaleT),
                                                camDist * (b.isStar ? 0.016f : b.host != null ? 0.0045f : 0.011f));   // moons read smaller

        // ── update ──────────────────────────────────────────────────────────────────────────────────
        void Update()
        {
            if (suspended) return;   // a sub-screen (Flora Lab) owns the camera/UI right now
            HandleInput();
            PollBakes();
            PollTuning();

            // Blend factor from zoom: near the focused body → true scale; system-wide → iconographic.
            float near = (focus != null ? focus.realRadiusAU : systemSpan * 0.01f) * 8f;
            float far  = systemSpan * 2.2f;
            scaleT = 1f - Astrophysics.Smoothstep(near, Mathf.Max(far, near * 4f), camDist);

            // Orbit lines fade out as you zoom in toward true scale. This doubles as the fix for the faint
            // precision jitter on far, non-focused orbits: by the time you're close enough for it to show, the
            // lines have dissolved. They fade back in when you pull out to the iconographic overview.
            float orbitFade = 1f - Astrophysics.Smoothstep(0.72f, 0.97f, scaleT);

            float t = Time.time * timeScale;
            ComputeStarFrame(t);

            // ── Pass 1 — geometry: place, scale and spin every body first, so the focus point (and therefore
            // the camera) have this frame's positions to work from.
            foreach (var b in bodies)
            {
                if (b.host != null)   // MOON: orbits the host planet on a blended-scale ellipse, with its own ring line
                {
                    float rSep = Mathf.Max(b.orbit.semiMajorAxisAU, 1e-6f);
                    float mper = Mathf.Max(0.02f, Mathf.Pow(rSep * 60f, 1.5f));
                    float Mm = b.orbit.meanAnomalyDeg * Mathf.Deg2Rad + Mathf.PI * 2f * t / mper;
                    float iconSep = RenderRadius(b.host) * (2.6f + 1.6f * b.moonIdx);
                    float sep = Mathf.Lerp(iconSep, rSep, scaleT);
                    sep = Mathf.Max(sep, RenderRadius(b.host) * 1.6f + RenderRadius(b));   // never inside the host
                    float scaleM = sep / rSep;                                            // stretch the true ellipse to `sep`
                    Vector3 hostLp = b.host.tf.localPosition;
                    b.tf.localPosition = hostLp + b.orbit.PositionAt(Mm) * scaleM;
                    b.tf.localScale = Vector3.one * RenderRadius(b) * 2f;
                    b.tf.Rotate(Vector3.up, b.spin * Time.deltaTime * (timeScale / SpinRef), Space.Self);
                    if (b.line != null && b.line.enabled)
                    {
                        Color lc = b.lineCol; lc.a = b.lineCol.a * orbitFade;
                        b.line.startColor = b.line.endColor = lc;
                        if (orbitFade > 0.02f)
                        {
                            // Adaptive resolution: a fixed 32-gon faceted badly once you zoomed in on a moon. Scale
                            // the ring's tessellation with how big it is relative to the zoom, like the planet orbits.
                            int segs = Mathf.Clamp(Mathf.RoundToInt(110f * Mathf.Sqrt(sep / Mathf.Max(camDist, 1e-4f))), 64, 512);
                            if (b.line.positionCount != segs) b.line.positionCount = segs;
                            for (int k = 0; k < segs; k++)
                                b.line.SetPosition(k, hostLp + b.orbit.PositionFromEccentric(k / (float)(segs - 1) * Mathf.PI * 2f) * scaleM);
                            b.line.widthMultiplier = camDist * 0.0025f;
                        }
                    }
                    continue;
                }
                float ds = DistScale(b);
                if (b.isStar)
                {
                    // stars ride the two-body (or hierarchical three-body) motion about the barycentre
                    b.tf.localPosition = starDisp[b.starIdx]; b.realPos = starReal[b.starIdx];
                }
                if (b.orbiting)
                {
                    Vector3 cDisp = Vector3.zero, cReal = Vector3.zero; float lineK = 1f;
                    float period;
                    if (b.isStar)
                    {
                        // companion: its line is its own ellipse about the barycentre it shares with its partner
                        lineK = b.starIdx == 1 ? 1f - muB : 1f - muC;
                        cDisp = b.starIdx == 1 ? baryABDisp : Vector3.zero;
                        period = 1f;   // position already set above
                    }
                    else
                    {
                        int hostI = b.planet != null ? b.planet.hostStar : 0;
                        if (hostI < 0) { cDisp = baryABDisp; cReal = baryABReal; }
                        else if (hostI < starDisp.Length) { cDisp = starDisp[hostI]; cReal = starReal[hostI]; }
                        period = b.orbit.PeriodYears(b.planet != null ? sys.HostMass(b.planet) : sys.star.stellarMass);
                        float M = b.orbit.meanAnomalyDeg * Mathf.Deg2Rad + (period > 1e-4f ? Mathf.PI * 2f * t / period : 0f);
                        Vector3 rel = b.orbit.PositionAt(M);
                        b.tf.localPosition = cDisp + rel * ds;
                        b.realPos = cReal + rel;
                    }
                    if (b.lineTf) b.lineTf.localPosition = cDisp;

                    // Orbit-line resolution scales with how far in you're zoomed, so it never facets up close.
                    if (b.line.enabled)
                    {
                        Color lc = b.lineCol; lc.a = b.lineCol.a * orbitFade;
                        b.line.startColor = b.line.endColor = lc;
                        if (orbitFade > 0.02f)
                        {
                            int segs = Mathf.Clamp(Mathf.RoundToInt(56f * Mathf.Sqrt(systemSpan / Mathf.Max(camDist, 1e-4f))), 56, 1024);
                            if (b.line.positionCount != segs) b.line.positionCount = segs;
                            for (int i = 0; i < segs; i++)
                                b.line.SetPosition(i, b.orbit.PositionFromEccentric(i / (float)(segs - 1) * Mathf.PI * 2f));
                            b.lineTf.localScale = Vector3.one * ds * lineK;
                            b.line.widthMultiplier = camDist * 0.0035f;
                        }
                    }
                }
                float bd = RenderRadius(b) * 2f;
                b.tf.localScale = new Vector3(bd, bd * b.oblateY, bd);   // oblateY < 1 flattens fast rotators' poles
                if (b.planet != null && b.planet.tidallyLocked && bodies.Count > 0)
                {
                    // Tidally locked → keep the substellar (+Z) face toward the star (matches the eyeball bake).
                    int hostIdx = b.planet.hostStar;
                    Vector3 hostL = hostIdx < 0 ? baryABDisp : hostIdx < starDisp.Length ? starDisp[hostIdx] : Vector3.zero;
                    Vector3 toStarL = hostL - b.tf.localPosition;
                    if (toStarL.sqrMagnitude > 1e-20f) b.tf.localRotation = Quaternion.LookRotation(toStarL.normalized, Vector3.up);
                }
                else b.tf.Rotate(Vector3.up, b.spin * Time.deltaTime * (timeScale / SpinRef), Space.Self);
            }

            // Belts: compress AU → iconographic like the orbit lines, blending to true scale as you zoom in.
            for (int i = 0; i < beltTfs.Count; i++)
            {
                if (beltTfs[i] == null) continue;
                float mid = Mathf.Max(beltMid[i], 1e-4f);
                float icon = systemSpan * Mathf.Pow(Mathf.Clamp01(mid / systemSpan), 0.5f);
                float ds = Mathf.Lerp(icon / mid, 1f, scaleT);
                beltTfs[i].localScale = Vector3.one * ds;
                // centred on the star (or close pair) the belt orbits — both stars move about the barycentre
                int bh = i < sys.belts.Count ? sys.belts[i].hostStar : 0;
                beltTfs[i].localPosition = bh < 0 ? baryABDisp : (starDisp.Length > 0 ? starDisp[0] : Vector3.zero);
            }

            // FLOATING ORIGIN: shift the whole system so the FOCUSED body is at the world origin. Distant planets
            // sit at tens of AU while their radius is ~1e-5 AU — a ~1e6 ratio that blows past float precision and
            // makes the mesh/orbit-line jitter. Rendering the focus at ~0 keeps its vertices precise.
            if (focus != null) root.localPosition = -focus.tf.localPosition;

            UpgradeFocusRes();
            UpdateCamera();   // move the camera BEFORE feeding uniforms, so view-anchored effects don't lag a frame

            // ── Pass 2 — shader uniforms, using THIS frame's camera. Anchoring the corona/glow/rim to a stale
            // camera made the whole halo slide when you moved fast (the "ghost halo").
            // Star position in LOCAL (root) space — the un-shifted orbital coordinates. Used for the lit direction
            // below so it isn't derived from two large floating-origin WORLD positions subtracting to a small
            // vector (that cancellation lost precision at distant planets and made the terminator jump per frame).
            Vector3 starLocal = bodies.Count > 0 ? bodies[0].tf.localPosition : Vector3.zero;
            Vector3 cw = cam.transform.position;
            foreach (var b in bodies)
            {
                float rr = RenderRadius(b);
                // Object-space camera position, computed on the CPU per shell — the view-dependent terms
                // (surface rim, ocean fresnel/specular, atmosphere haze, star limb darkening, corona halo) need
                // it, and the GPU's ObjSpaceViewDir / world-to-object inverse degenerates at tiny planet scales.
                // Set unconditionally so it also reaches the primary star, which sits at the origin (toStar = 0).
                b.mat.SetVector("_CamPosObj", b.tf.InverseTransformPoint(cw));
                if (b.oceanMat && b.oceanTf) b.oceanMat.SetVector("_CamPosObj", b.oceanTf.InverseTransformPoint(cw));
                if (b.atmoMat && b.atmoTf) b.atmoMat.SetVector("_CamPosObj", b.atmoTf.InverseTransformPoint(cw));
                if (b.coronaMat && b.coronaTf) b.coronaMat.SetVector("_CamPosObj", b.coronaTf.InverseTransformPoint(cw));
                if (b.flaresMat && b.flaresTf) b.flaresMat.SetVector("_CamPosObj", b.flaresTf.InverseTransformPoint(cw));
                if (b.cloudMat && b.cloudTf) b.cloudMat.SetVector("_CamPosObj", b.cloudTf.InverseTransformPoint(cw));

                // Star glow + brilliance. `appar` is the star's on-screen angular size (render radius / distance).
                if (b.isStar && b.mat)
                {
                    // Drive the glow off the ACTUAL camera→star distance, not the global focus-zoom distance —
                    // otherwise flying up to a planet (which is far from the star) wrongly killed the star's glow
                    // because it used the planet's zoom level instead of how far the star really is.
                    float distToStar = Vector3.Distance(cw, b.tf.position);
                    float apparStar = RenderRadius(b) / Mathf.Max(distToStar, 1e-6f);   // star's true on-screen size
                    // Rim glow: 0 up close (see the surface) → 1 far away (rim widens and floods the disc to light).
                    b.mat.SetFloat("_RimGlow", 1f - Mathf.Clamp01(apparStar / 0.28f));

                    if (b.glowTf && b.glowMat)
                    {
                        // Constant apparent size up to a cap distance, past which it finally shrinks with distance
                        // (so a far-off star recedes to a point of light instead of a fixed-size blob), but never
                        // smaller than the disc.
                        float glowWorld = Mathf.Min(distToStar * 0.34f * knobStarGlowSize, systemSpan * 0.55f);
                        glowWorld = Mathf.Max(glowWorld, RenderRadius(b) * 1.3f);
                        float parentScale = Mathf.Max(b.tf.lossyScale.x, 1e-9f);
                        b.glowTf.rotation = cam.transform.rotation;                 // camera-facing billboard
                        b.glowTf.localScale = Vector3.one * (glowWorld / parentScale);
                        // Stays glowy up close, and brightens/takes over completely from afar (a pure orb of light).
                        b.glowMat.SetFloat("_Intensity", Mathf.Lerp(1.7f, 1.05f, Mathf.Clamp01((apparStar - 0.05f) / 0.4f)) * knobStarGlowIntensity);
                    }
                }

                // Root is unrotated/unscaled, so this local-space delta is the same DIRECTION as the world one,
                // but computed from precise orbital coordinates (star ~origin, body at its orbital offset).
                Vector3 toStar = starLocal - b.tf.localPosition;   // direction from the body toward its star
                if (!b.isStar && starBodies.Count > 1) toStar = LightFor(b, out _);
                if (toStar.sqrMagnitude > 1e-20f)
                {
                    toStar.Normalize();
                    // Feed the lit direction in OBJECT space (the body and its shells share this rotation). All
                    // planet shaders now light in object space, so the terminator is scale-independent — the
                    // world-normal transform used to collapse to a constant at distant planets' tiny scales,
                    // flattening the terminator so zoomed-in worlds looked unlit.
                    Vector3 litObj = b.tf.InverseTransformDirection(toStar);
                    b.mat.SetVector("_SunDir", litObj);
                    if (b.oceanMat) b.oceanMat.SetVector("_SunDir", litObj);
                    if (b.atmoMat) b.atmoMat.SetVector("_SunDir", litObj);
                    if (b.cloudMat) b.cloudMat.SetVector("_SunDir", litObj);
                    if (!b.isStar) ApplySecondSun(b);
                    // The ring is a separately-tilted child that shades in world space; keep the world vector.
                    if (b.ringMat) { b.ringMat.SetVector("_SunDir", -toStar); b.ringMat.SetFloat("_PlanetR", rr); }

                    // Give the giant its ring geometry in object space so the ring casts a shadow on the planet:
                    // the ring-plane normal, and the annulus' inner/outer radii (mesh inner 0.075 → outer 0.5,
                    // times the ring child's scale; the visible band is [_RingInner, _RingInner+_RingWidth]).
                    if (b.ringMat && b.ringTf)
                    {
                        Vector3 rnObj = b.ringTf.localRotation * Vector3.up;
                        float ringScale = b.ringTf.localScale.x;
                        float ri = b.ringMat.GetFloat("_RingInner"), rw = b.ringMat.GetFloat("_RingWidth");
                        float innerObj = (0.075f + ri * 0.425f) * ringScale;
                        float outerObj = (0.075f + Mathf.Min(ri + rw, 1f) * 0.425f) * ringScale;
                        b.mat.SetVector("_RingNormalObj", new Vector4(rnObj.x, rnObj.y, rnObj.z, 1f));
                        b.mat.SetVector("_RingRadii", new Vector4(innerObj, outerObj, 0f, 0f));
                    }
                }
            }
        }

        void UpdateCamera()
        {
            // Track the focused body EXACTLY so it stays dead-centre even as it moves (orbit + scale blend);
            // only the zoom distance eases, which gives the smooth fly-in without drifting off the target.
            if (focus != null) focusPoint = focus.tf.position;
            camDist = Mathf.Lerp(camDist, camDistTarget, 1f - Mathf.Exp(-6f * Time.deltaTime));

            Quaternion rot = Quaternion.Euler(camPitch, camYaw, 0f);
            cam.transform.position = focusPoint + rot * (Vector3.back * camDist);
            // Use a non-degenerate up hint when looking near-vertically (steep/inclined framing) so the view
            // doesn't flip and swing onto the dark side.
            Vector3 viewDir = (focusPoint - cam.transform.position).normalized;
            Vector3 upHint = Mathf.Abs(viewDir.y) > 0.95f ? (rot * Vector3.up) : Vector3.up;
            cam.transform.LookAt(focusPoint, upHint);
            // Near/far are chosen together so the depth buffer never collapses at extreme zoom. The old pairing
            // (near floored at 1e-5, far at systemSpan*8) gave a ~3e7 range ratio when you got close to a tiny
            // moon — far beyond a 24-bit depth buffer — so the moon z-failed and vanished ("the camera clipping
            // the moons"). Keep far generous enough to still see the star and siblings, then raise near to cap the
            // ratio, while guaranteeing near stays safely in front of the focused body so it's never sliced.
            // Near/far are chosen together. The far plane reaches out past the star and belts so they stay
            // visible while you examine a planet, but two hard constraints apply: the far/near ratio must stay
            // inside the depth buffer, and the near plane must never cross in front of the focused body. For
            // planet-level zoom both hold with the star in view. Only at extreme close-ups on a tiny moon do they
            // conflict (reaching the far star would blow the depth range) — there we shrink the far plane instead,
            // so you lose the distant star at nose-to-surface range but the body stays crisp. (The previous fixed
            // camDist*3500 cap was too short for outer planets and clipped away the sun and asteroid belts.)
            float distToStar = bodies.Count > 0 ? Vector3.Distance(cam.transform.position, bodies[0].tf.position) : systemSpan;
            float far = Mathf.Max(camDist * 12f, distToStar * 1.5f);
            far = Mathf.Min(far, Mathf.Max(systemSpan * 6f, camDist * 12f));   // cap, but never below camDist*12
            float near = camDist * 0.02f;
            const float minRatio = 1e-6f;                                     // far/near ≤ 1e6 → clean under reversed-Z,
            float ratioNeed = far * minRatio;                                 // still far below the ~3e7 that hid moons
            if (ratioNeed > near) near = ratioNeed;
            float nearSafe = focus != null ? (camDist - RenderRadius(focus)) * 0.5f : camDist * 0.5f;
            if (nearSafe > 0f && near > nearSafe) { near = nearSafe; far = near / minRatio; }   // can't keep far → shrink it
            cam.nearClipPlane = Mathf.Clamp(near, 1e-6f, 50f);
            cam.farClipPlane  = Mathf.Max(far, camDist * 12f);
        }

        void HandleInput()
        {
            float dt = Time.deltaTime;

            // ── orbit ── left- or right-drag orbits freely in ANY direction (yaw+pitch move together, so a
            // diagonal mouse motion gives a diagonal orbit). A left-click without dragging picks a body.
            bool overPanel = PointerOverPanel(Input.mousePosition);
            if (Input.GetMouseButtonDown(0) && !overPanel) { dragStart = Input.mousePosition; dragging = false; }
            if (Input.GetMouseButton(0) && !overPanel)
            {
                if (((Vector2)Input.mousePosition - dragStart).sqrMagnitude > 16f) dragging = true;
                if (dragging) OrbitDrag(3.0f);
            }
            if (Input.GetMouseButton(1)) OrbitDrag(3.2f);

            float kx = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float ky = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            if (kx != 0f || ky != 0f)
            {
                camYaw += kx * 95f * dt;
                camPitch = Mathf.Clamp(camPitch - ky * 95f * dt, -85f, 85f);
            }

            Vector2 sd = Input.mouseScrollDelta;
            bool zoomMod = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            // Scroll orbits a body ONLY while you're actually zoomed in on it (scaleT → 1 near true scale).
            // Once you've zoomed back out to the overview, scroll goes back to zooming — clicking a planet
            // once no longer hijacks the scroll wheel forever.
            bool orbitScroll = flownIn && focus != null && scaleT > 0.3f;
            if (orbitScroll && !zoomMod && sd.sqrMagnitude > 1e-6f)
            {
                // Once flown to a world, two-finger scroll orbits it: horizontal → yaw, vertical → pitch.
                camYaw += sd.x * 9f;
                camPitch = Mathf.Clamp(camPitch - sd.y * 9f, -85f, 85f);
            }
            else if (Mathf.Abs(sd.y) > 1e-4f)
            {
                Zoom(Mathf.Exp(-sd.y * (zoomMod ? 0.12f : 0.25f)));   // overview scroll, or Shift+scroll when focused
            }

            // ── zoom ── +/- keys are the reliable way back out once you're focused. Guard on window focus: if
            // the Game view loses focus mid-press (e.g. an editor error toast steals it) the KeyUp never
            // arrives and Input.GetKey latches "held forever" — gating on focus stops that runaway zoom.
            if (Application.isFocused)
            {
                if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus))  Zoom(Mathf.Exp(-1.6f * dt));
                if (Input.GetKey(KeyCode.Minus)  || Input.GetKey(KeyCode.KeypadMinus)) Zoom(Mathf.Exp( 1.6f * dt));
            }

            if (Input.GetMouseButtonUp(0) && !dragging && !overPanel && !targeting) TryPick(dragStart);

            if (Input.GetKeyDown(KeyCode.O))   // toggle orbit lines
            {
                showOrbits = !showOrbits;
                foreach (var b in bodies) if (b.line) b.line.enabled = showOrbits;
            }
            if (Input.GetKeyDown(KeyCode.H)) hideGui = !hideGui;   // hide all UI for clean screenshots

            if (Input.GetKeyDown(KeyCode.Y)) CLAY.Surface.TerrainLab.Open(this);   // Terrain Lab: the ground materials one by one

            if (Input.GetKeyDown(KeyCode.G))   // open the Flora Lab directly (hovered planet → focus → a default world)
            {
                var hv = HoverPick();
                PlanetData p = (hv != null && hv.planet != null) ? hv.planet
                             : (focus != null && focus.planet != null) ? focus.planet
                             : FloraLab.DefaultGarden();
                FloraLab.Open(this, p, sys != null ? sys.seed : 1UL);
            }

            // ── LANDING TARGET: L arms a surface marker on the hovered (else focused) rocky world; click to land exactly
            // there; L again or right-click cancels. ──
            if (Input.GetKeyDown(KeyCode.L) && sys != null)
            {
                if (targeting) CancelTarget();
                else
                {
                    var hv = HoverPick();
                    var b = (hv != null && hv.planet != null) ? hv : focus;
                    if (b != null && b.planet != null && !IsGiant(b.planet) && b.tf != null) { targeting = true; targetBody = b; }
                }
            }
            if (targeting && Input.GetMouseButtonDown(1)) CancelTarget();
            if (targeting && targetHit && Input.GetMouseButtonUp(0) && !dragging && !overPanel)
            {
                var b = targetBody;
                Vector3 local = b.tf.InverseTransformPoint(targetPoint).normalized;          // the mesh's own frame
                SunsFor(b, out var sun1, out var sun2, out float sunW2);
                Vector3 sunLocal = b.tf.InverseTransformDirection((sun1.tf.position - b.tf.position).normalized);
                ulong pseed = DetRng.Hash(sys.seed, (ulong)(b.planet.index + 1));
                Vector3 hitWorld = targetPoint;
                CancelTarget();
                var ctx = BuildLandingContext(b, hitWorld, sunLocal);
                if (sun2 != null)
                {
                    ctx.sun2Local = b.tf.InverseTransformDirection((sun2.tf.position - b.tf.position).normalized);
                    ctx.sun2Weight = sunW2; ctx.sun2TempK = sun2.star.effectiveTemp;
                    // its apparent size relative to the main sun (radius / distance, true positions)
                    float d1 = Mathf.Max((sun1.realPos - b.realPos).magnitude, 1e-4f), d2 = Mathf.Max((sun2.realPos - b.realPos).magnitude, 1e-4f);
                    ctx.sun2SizeRel = Mathf.Clamp((sun2.star.radius / d2) / Mathf.Max(sun1.star.radius / d1, 1e-6f), 0.05f, 3f);
                }
                ctx.sun1TempK = sun1.star.effectiveTemp;
                CLAY.Surface.SurfaceWorld.Enter(this, b.planet, sys.seed, pseed, local, ctx);
            }
        }

        // ── landing target state ──
        bool targeting, targetHit; Body targetBody; Vector3 targetPoint, targetNormal;
        GameObject targetDot; LineRenderer targetPin; Material targetMat;

        /// Snapshot of the system at touchdown: the exact painted map, where the other bodies hang in the sky, and the
        /// star background — so the ground and sky agree with what you just saw from space.
        public static bool CaptureStarBackground = false;   // RenderToCubemap under URP stalled the surface loop — off until verified
        CLAY.Surface.SurfaceWorld.LandingContext BuildLandingContext(Body b, Vector3 hitWorld, Vector3 sunLocal)
        {
            var ctx = new CLAY.Surface.SurfaceWorld.LandingContext { sunLocal = sunLocal, viewerSpin = b.spin, bodyToWorld = b.tf.rotation };
            // 1. the painted surface map
            try
            {
                if (b.mat != null && b.mat.HasProperty("_SurfaceTex") && b.mat.GetTexture("_SurfaceTex") is Texture2D t && t.isReadable)
                { ctx.orbPx = t.GetPixels(); ctx.orbW = t.width; ctx.orbH = t.height; }
            }
            catch (System.Exception ex) { Debug.LogWarning($"[Surface] orbital map unreadable: {ex.Message}"); }

            // 2. other bodies (not the star — the surface draws its own sun — and not this world)
            for (int i = 1; i < bodies.Count; i++)
            {
                var o = bodies[i];
                if (o == b || o.tf == null || o.isStar) continue;
                Vector3 dw = o.tf.position - hitWorld;
                float dist = dw.magnitude; if (dist < 1e-3f) continue;
                float rad = o.tf.lossyScale.x * 0.5f;
                var sb = new CLAY.Surface.SurfaceWorld.SkyBody
                {
                    dir = b.tf.InverseTransformDirection(dw / dist),
                    angRadius = Mathf.Min(Mathf.Asin(Mathf.Clamp(rad / dist, 0f, 0.99f)), 0.3f),
                };
                if (sb.angRadius < 0.0015f) continue;   // too small to see as a disc
                if (o.planet != null && o.planet.type != PlanetType.GasGiant && o.planet.type != PlanetType.IceGiant)
                { sb.planet = o.planet; sb.seed = DetRng.Hash(sys.seed, (ulong)(o.planet.index + 1)); }
                else if (o.planet != null && o.mat != null)
                    sb.ownMat = new Material(o.mat);                 // giants: their own procedural look, exactly
                if (o.mat != null)
                {
                    foreach (var prop in new[] { "_SurfaceTex", "_BandTex", "_MainTex", "_BaseMap" })
                        if (o.mat.HasProperty(prop) && o.mat.GetTexture(prop) != null) { sb.tex = o.mat.GetTexture(prop); break; }
                    if (sb.tex == null)
                        foreach (var prop in new[] { "_Color", "_BaseColor", "_ColorA", "_Tint" })
                            if (o.mat.HasProperty(prop)) { sb.tint = o.mat.GetColor(prop); break; }
                }
                ctx.bodies.Add(sb);
            }

            // 3. the star background, rendered once into a cubemap from here with the system's own bodies hidden
            if (CaptureStarBackground && cam != null)
            {
                bool was = root != null && root.gameObject.activeSelf;
                try
                {
                    if (root) root.gameObject.SetActive(false);
                    var cube = new Cubemap(1024, TextureFormat.RGBA32, false) { name = "SurfaceStarCube" };
                    if (cam.RenderToCubemap(cube)) ctx.starCube = cube; else Destroy(cube);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Surface] star background capture failed: {ex.Message}"); }
                finally { if (root) root.gameObject.SetActive(was); }
            }
            Debug.Log($"[Surface] landing context: map {(ctx.orbPx != null ? $"{ctx.orbW}x{ctx.orbH}" : "none")} · {ctx.bodies.Count} sky bodies · stars {(ctx.starCube ? "captured" : "procedural")}");
            return ctx;
        }

        void CancelTarget()
        {
            targeting = false; targetHit = false; targetBody = null;
            if (targetDot) targetDot.SetActive(false);
            if (targetPin) targetPin.enabled = false;
        }

        // After the bodies have moved this frame: intersect the mouse ray with the target world and place the marker.
        void LateUpdate()
        {
            if (suspended || !targeting || targetBody == null || cam == null) { if (targetDot && !targeting) targetDot.SetActive(false); return; }
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 c = targetBody.tf.position;
            float rad = targetBody.tf.lossyScale.x * 0.5f;
            Vector3 oc = ray.origin - c;
            float bq = Vector3.Dot(oc, ray.direction), cq = Vector3.Dot(oc, oc) - rad * rad, disc = bq * bq - cq;
            targetHit = disc >= 0f && (-bq - Mathf.Sqrt(disc)) > 0f;
            if (targetMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                targetMat = new Material(sh); targetMat.SetColor("_BaseColor", new Color(1f, 0.25f, 0.2f));
            }
            if (targetDot == null)
            {
                targetDot = GameObject.CreatePrimitive(PrimitiveType.Sphere); targetDot.name = "LandingTarget";
                Destroy(targetDot.GetComponent<Collider>());
                targetDot.GetComponent<MeshRenderer>().sharedMaterial = targetMat;
                var pinGo = new GameObject("LandingPin"); targetPin = pinGo.AddComponent<LineRenderer>();
                targetPin.sharedMaterial = targetMat; targetPin.positionCount = 2; targetPin.useWorldSpace = true;
            }
            targetDot.SetActive(targetHit); targetPin.enabled = targetHit;
            if (!targetHit) return;
            float tHit = -bq - Mathf.Sqrt(disc);
            targetPoint = ray.origin + ray.direction * tHit;
            targetNormal = (targetPoint - c).normalized;
            float d = Vector3.Distance(cam.transform.position, targetPoint);
            targetDot.transform.position = targetPoint;
            targetDot.transform.localScale = Vector3.one * d * 0.012f;
            targetPin.SetPosition(0, targetPoint);
            targetPin.SetPosition(1, targetPoint + targetNormal * rad * 0.18f);
            targetPin.widthMultiplier = d * 0.003f;
        }

        void OrbitDrag(float sens)
        {
            camYaw += Input.GetAxis("Mouse X") * sens;
            camPitch = Mathf.Clamp(camPitch - Input.GetAxis("Mouse Y") * sens, -85f, 85f);
        }

        // The closest the camera may sit to a body. Shared by Zoom AND FocusOn so a click can never fly you
        // nearer than the scroll clamp allows (which was why clicking a moon zoomed you "too far in" and the
        // first scroll then snapped you back out, stranding you outside).
        float MinCamDist(Body b)
        {
            // Just outside the surface, for any body. Moons used to get a large extra floor so you couldn't fly
            // in to examine them; the near/far clip now stays depth-precise at close range, so that floor is gone.
            return (b != null ? b.realRadiusAU : 0.01f) * 2.0f;
        }

        void Zoom(float factor)
        {
            camDistTarget = Mathf.Clamp(camDistTarget * factor, MinCamDist(focus), systemSpan * 5f);
        }

        void TryPick(Vector3 mouse)
        {
            Body pick = null; float best = 1e9f;
            float k = Screen.height * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            foreach (var b in bodies)
            {
                Vector3 sp = cam.WorldToScreenPoint(b.tf.position);
                if (sp.z <= 0f) continue;
                float apparentPx = RenderRadius(b) / Mathf.Max(sp.z, 1e-5f) * k;
                float d = Vector2.Distance(new Vector2(mouse.x, mouse.y), new Vector2(sp.x, sp.y));
                float hit = Mathf.Max(apparentPx, 12f);
                if (d < hit && d < best) { best = d; pick = b; }
            }
            if (pick != null) FocusOn(pick);
        }

        // Body under the cursor right now (no click) — used for the G-key flora prompt.
        Body HoverPick()
        {
            Body pick = null; float best = 1e9f;
            float k = Screen.height * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 mouse = Input.mousePosition;
            foreach (var b in bodies)
            {
                Vector3 sp = cam.WorldToScreenPoint(b.tf.position);
                if (sp.z <= 0f) continue;
                float apparentPx = RenderRadius(b) / Mathf.Max(sp.z, 1e-5f) * k;
                float d = Vector2.Distance(new Vector2(mouse.x, mouse.y), new Vector2(sp.x, sp.y));
                if (d < Mathf.Max(apparentPx, 12f) && d < best) { best = d; pick = b; }
            }
            return pick;
        }

        string _story;                            // formation/conditions story for the focused planet

        void FocusOn(Body b)
        {
            focus = b;
            flownIn = true;
            if (b != null && b.planet != null && sys != null)
            {
                ulong pseed = DetRng.Hash(sys.seed, (ulong)(b.planet.index + 1));
                string disp = PlanetTexture.DisplayType(b.planet, pseed);
                // Catalogue name + the inhabitants' native name in quotes: e.g.  Delfor-2 "Belininian".
                string native = string.IsNullOrEmpty(b.planet.colloquial) ? "" : $" “{b.planet.colloquial}”";
                _story = (b.planet.isMoon
                            ? $"{b.planet.name}{native} · Moon of {(b.host != null ? b.host.label : "planet")} · {disp}"
                            : $"{b.planet.name}{native} · {disp}")
                       + $"\n\n{PlanetStory.Generate(sys.star, b.planet, sys.seed)}";
            }
            else _story = null;
            if (b.planet != null && !IsGiant(b.planet) && !b.hiRes) BakePlanetTexAsync(b, SurfaceTexHi);
            // Frame the body at true size, but never inside the shared min distance — otherwise a click on a moon
            // flew closer than the scroll clamp, so the next scroll snapped the view back out and locked you there.
            camDistTarget = Mathf.Max(b.realRadiusAU * (b.isStar ? 7f : 5f), MinCamDist(b));
            AimAtLitSide(b);
        }

        // Point the camera at the sunlit hemisphere (3/4 view) so a focused body is never a black disc.
        // Robust for ANY orbit inclination: the 3/4 tilt is built in a frame perpendicular to the star
        // direction, so a near-pole-on light still frames the lit face instead of clamping to the dark side.
        void AimAtLitSide(Body b)
        {
            Vector3 sun = bodies.Count > 0 ? bodies[0].tf.position : Vector3.zero;
            Vector3 lightDir = b.tf.position - sun;
            if (lightDir.sqrMagnitude < 1e-12f) return;
            Vector3 toStar = -lightDir.normalized;                          // planet → star = the lit direction
            Vector3 refUp = Mathf.Abs(toStar.y) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(refUp, toStar).normalized;
            Vector3 upP = Vector3.Cross(toStar, right).normalized;
            // Three-quarter (gibbous) framing with a CLEAR terminator: camera ~50° off the sunbeam. The
            // visible face is well lit (disc-centre N·L ≈ 0.64) but a real day→night terminator sweeps across
            // the disc so the star's directional light reads immediately — a near-frontal (sun-behind-camera)
            // framing flattens that terminator away and the planet looks "not lit by the star" even though it
            // is. Lateral-dominant so the terminator sits well inside the disc, not hidden at the limb.
            Vector3 v = (toStar * 0.64f + right * 0.90f + upP * 0.42f).normalized;
            camPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(v.y, -1f, 1f)) * Mathf.Rad2Deg, -89f, 89f);
            camYaw = Mathf.Atan2(-v.x, -v.z) * Mathf.Rad2Deg;
        }

        // Finalize any completed background bakes (upload the texture on the main thread).
        void PollBakes()
        {
            foreach (var b in bodies)
            {
                if (b.bakeTask == null || !b.bakeTask.IsCompleted) continue;
                if (b.bakeTask.Status == TaskStatus.RanToCompletion)
                    AssignSurfaceTex(b, PlanetTexture.ToTexture(b.bakeTask.Result, $"PlanetTex_{b.planet.name}"), b.bakeWidth);
                else
                    Debug.LogWarning($"Planet bake failed: {b.bakeTask.Exception?.GetBaseException().Message}");
                b.bakeTask = null;
            }
        }

        // Debounced re-bake of the focused planet after a generation knob changes (mesh + texture).
        void PollTuning()
        {
            if (!tuneDirty || Time.unscaledTime - tuneDirtyAt < 0.35f) return;
            tuneDirty = false;
            if (focus == null || focus.planet == null || IsGiant(focus.planet)) return;
            ulong pseed = DetRng.Hash(sys.seed, (ulong)(focus.planet.index + 1));
            var mesh = PlanetTexture.BuildMesh(focus.planet, pseed, PlanetSubdiv);
            if (focus.planetMesh) Destroy(focus.planetMesh);
            focus.planetMesh = mesh; focus.mf.sharedMesh = mesh;
            focus.hiRes = false; BakePlanetTexAsync(focus, SurfaceTexHi);
        }

        void SystemView()
        {
            focus = bodies.Count > 0 ? bodies[0] : null;
            flownIn = false;
            camDistTarget = systemSpan * 2.2f;
        }

        // ── on-screen controls ───────────────────────────────────────────────────────────────────
        Rect panel = new Rect(14, 14, 258, 360);
        bool PointerOverPanel(Vector3 m)
        {
            if (hideGui) return false;   // panels aren't drawn, so they shouldn't eat clicks
            Vector2 g = new Vector2(m.x, Screen.height - m.y);
            return panel.Contains(g) || (showTune && tunePanel.Contains(g));
        }

        void OnGUI()
        {
            if (suspended) return;   // the Flora Lab draws its own UI
            if (targeting && targetBody != null)
            {
                Vector2 m = Event.current.mousePosition;
                GUI.color = new Color(1f, 0.35f, 0.3f);
                GUI.DrawTexture(new Rect(m.x - 14, m.y - 1, 10, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(m.x + 4, m.y - 1, 10, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(m.x - 1, m.y - 14, 2, 10), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(m.x - 1, m.y + 4, 2, 10), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(m.x + 18, m.y + 8, 320, 22),
                    targetHit ? $"LAND on {targetBody.planet.name} — click   (L / right-click cancel)" : "aim at the planet   (L / right-click cancel)");
            }
            if (hideGui) return;   // H toggles all UI off for clean screenshots
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                label = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
                help  = new GUIStyle(GUI.skin.label) { fontSize = 11 }; help.normal.textColor = new Color(1, 1, 1, 0.5f);
                story = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, richText = true };
                story.normal.textColor = new Color(0.92f, 0.94f, 1f, 1f);
            }

            // Story panel for the focused planet (bottom-right).
            if (_story != null && focus != null && focus.planet != null)
            {
                float w = 400f, h = 210f;
                var r = new Rect(Screen.width - w - 14f, Screen.height - h - 14f, w, h);
                GUI.Box(r, GUIContent.none);
                GUILayout.BeginArea(new Rect(r.x + 12, r.y + 10, r.width - 24, r.height - 20));
                GUILayout.Label(_story, story);
                GUILayout.EndArea();
            }

            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.Label("Galaxy Viewer", title);
            if (GUILayout.Button("⟳  New system", GUILayout.Height(28))) { seed = (ulong)Random.Range(1, int.MaxValue); Regenerate(); }

            GUILayout.Space(4);
            Stepper("Stars", ref starCount, 1, 3);
            Stepper("Planets (0 = random)", ref planetCount, 0, 12);
            GUILayout.Label($"Max inclination: {maxInclinationDeg:0}°", label);
            maxInclinationDeg = GUILayout.HorizontalSlider(maxInclinationDeg, 0f, 45f);
            GUILayout.Label($"Time scale: {timeScale:0.00}", label);
            timeScale = GUILayout.HorizontalSlider(timeScale, 0f, 0.6f);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◎  System view")) SystemView();
            if (GUILayout.Button(showTune ? "▾ Tuning" : "▸ Tuning")) showTune = !showTune;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label($"Scale: <b>{(scaleT < 0.15f ? "iconographic" : scaleT > 0.85f ? "true" : "blending")}</b>  ({scaleT:0.00})", label);
            if (focus != null) DrawFocusInfo();

            GUILayout.FlexibleSpace();
            GUILayout.Label("Click: fly to body · Drag: orbit (any dir)", help);
            GUILayout.Label((flownIn ? "Scroll: orbit · +/−: zoom" : "Scroll: zoom · Arrows: orbit") + " · O: orbit lines · G: flora lab · H: hide UI", help);
            GUILayout.EndArea();

            if (showTune) DrawTunePanel();
        }

        Rect tunePanel = new Rect(0, 14, 280, 640);
        Vector2 tuneScroll;

        void DrawTunePanel()
        {
            tunePanel.x = Screen.width - tunePanel.width - 14;
            tunePanel.height = Mathf.Min(Screen.height - 28, 640);   // never taller than the view
            GUILayout.BeginArea(tunePanel, GUI.skin.box);
            bool isStar = focus != null && focus.isStar;
            bool isGiant = focus != null && focus.planet != null && IsGiant(focus.planet);
            GUILayout.Label(isStar ? "Star Knobs" : "Planet Knobs", title);
            GUILayout.Label(isStar ? $"Editing: <b>{focus.label}</b>  (star)"
                : focus != null && focus.planet != null
                    ? $"Editing: <b>{focus.label}</b>{(isGiant ? "  (gas giant)" : "")}"
                    : "<i>Fly to a body to preview live</i>", label);

            tuneScroll = GUILayout.BeginScrollView(tuneScroll);
            bool gen = false, shader = false, giant = false, star = false;

            if (isStar)
            {
                GUILayout.Label("<b>Activity</b>", label);
                Knob("Sunspot activity", ref knobStarSpots, 0f, 0.45f, ref star);
                Knob("Flare activity", ref knobStarFlares, 0f, 2.5f, ref star);
                Knob("Volatility", ref knobStarVolatility, 0f, 1.5f, ref star);
                GUILayout.Space(4); GUILayout.Label("<b>Corona</b>", label);
                Knob("Corona size", ref knobStarCoronaSize, 1.2f, 2.6f, ref star);
                Knob("Corona density", ref knobStarCoronaDensity, 0f, 1f, ref star);
                Knob("Corona arc", ref knobStarCoronaArc, 0f, 1.5f, ref star);
                GUILayout.Space(4); GUILayout.Label("<b>Glow</b>", label);
                Knob("Glow size", ref knobStarGlowSize, 0.3f, 1.3f, ref star);
                Knob("Glow intensity", ref knobStarGlowIntensity, 0f, 3f, ref star);
            }
            else if (focus != null && focus.planet != null)
            {
                GUILayout.Label("<b>Orbit</b>", label);
                float inc = focus.orbit.inclinationDeg; bool incCh = false;
                Knob("Inclination°", ref inc, 0f, 60f, ref incCh);
                if (incCh) focus.orbit.inclinationDeg = inc;

                if (isGiant)
                {
                    // Gas giants get their own live shader knobs (no terrain/ocean).
                    GUILayout.Space(4); GUILayout.Label("<b>Gas giant</b>", label);
                    Knob("Band count", ref knobGiantBands, 2f, 16f, ref giant);
                    Knob("Band variation", ref knobGiantBandVar, 0f, 2f, ref giant);
                    Knob("Turbulence", ref knobGiantTurb, 0f, 2f, ref giant);
                    Knob("Fine turbulence", ref knobGiantFine, 0f, 1.5f, ref giant);
                    Knob("Flow speed", ref knobGiantSpeed, 0f, 0.5f, ref giant);
                    Knob("Chem complexity", ref knobGiantChem, 0f, 1f, ref giant);
                    Knob("Storm likelihood", ref knobGiantSpot, 0f, 1f, ref giant);
                    Knob("Storm size (0=auto)", ref knobGiantSpotSize, 0f, 0.5f, ref giant);
                    if (focus.ringMat != null)
                    {
                        GUILayout.Space(4); GUILayout.Label("<b>Ring</b>", label);
                        Knob("Opacity", ref knobRingOpacity, 0f, 1f, ref giant);
                        Knob("Width", ref knobRingWidth, 0.05f, 1f, ref giant);
                        Knob("Radius start", ref knobRingInner, 0f, 0.9f, ref giant);
                    }
                    GUILayout.Space(4); GUILayout.Label("<b>Lighting</b>", label);
                    Knob("Ambient (night)", ref knobAmbient, 0f, 0.4f, ref giant);
                }
                else
                {
                    GUILayout.Space(4); GUILayout.Label("<b>Terrain</b> (re-bakes)", label);
                    Knob("Continent scale", ref tune.contFreq, 0.3f, 3f, ref gen);
                    Knob("Domain warp", ref tune.warp, 0f, 3f, ref gen);
                    Knob("Mountains", ref tune.mountains, 0f, 3f, ref gen);
                    Knob("Fine detail", ref tune.detail, 0f, 3f, ref gen);
                    Knob("Relief", ref tune.relief, 0f, 3f, ref gen);
                    Knob("Sea level", ref tune.seaLevel, -0.3f, 0.3f, ref gen);
                    Knob("Craters", ref tune.craters, 0f, 2f, ref gen);

                    GUILayout.Space(4); GUILayout.Label("<b>Surface</b>", label);
                    Knob("Bump", ref knobBump, 0f, 40f, ref shader);
                    Knob("Ambient (night)", ref knobAmbient, 0f, 0.4f, ref shader);
                    Knob("Camera fill", ref knobCamFill, 0f, 0.6f, ref shader);
                    Knob("Rim power", ref knobRim, 0.5f, 8f, ref shader);

                    GUILayout.Space(4); GUILayout.Label("<b>Ocean</b>", label);
                    Knob("Spec power", ref knobOceanSpec, 4f, 300f, ref shader);
                    Knob("Spec gain", ref knobOceanGain, 0f, 4f, ref shader);
                    Knob("Fresnel", ref knobOceanFres, 0.5f, 6f, ref shader);
                    Knob("Opacity", ref knobOceanOpacity, 0f, 1f, ref shader);
                }

                GUILayout.Space(4); GUILayout.Label("<b>Atmosphere</b>", label);
                Knob("Glow intensity", ref knobAtmoIntensity, 0f, 3f, ref shader);
                Knob("Glow softness", ref knobAtmoSoft, 0.5f, 4f, ref shader);
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Reset")) ResetKnobs();
            GUILayout.EndScrollView();

            if (gen) { tuneDirty = true; tuneDirtyAt = Time.unscaledTime; }
            if (shader) ApplyLiveKnobs();
            if (giant) ApplyGiantKnobs();
            if (star) ApplyStarKnobs();
            GUILayout.EndArea();
        }

        void ApplyGiantKnobs()
        {
            if (focus == null || focus.mat == null) return;
            focus.mat.SetFloat("_BandFreq", knobGiantBands);
            focus.mat.SetFloat("_BandVar", knobGiantBandVar);
            focus.mat.SetFloat("_Turb", knobGiantTurb);
            focus.mat.SetFloat("_FineTurb", knobGiantFine);
            focus.mat.SetFloat("_Speed", knobGiantSpeed);
            focus.mat.SetFloat("_ChemAmt", knobGiantChem);
            focus.mat.SetFloat("_Ambient", knobAmbient);
            // Storm likelihood gates whether THIS giant's spot shows (based on its rolled potential).
            Vector4 sp = focus.mat.GetVector("_Spot");
            sp.w = focus.giantSpotPotential < knobGiantSpot ? 1f : 0f;
            if (knobGiantSpotSize > 0.001f) sp.z = knobGiantSpotSize;   // override size (0 = keep generated)
            focus.mat.SetVector("_Spot", sp);

            if (focus.ringMat != null)
            {
                focus.ringMat.SetFloat("_Density", knobRingOpacity);
                focus.ringMat.SetFloat("_RingWidth", knobRingWidth);
                focus.ringMat.SetFloat("_RingInner", knobRingInner);
            }
        }

        // Live star tuning — maps the "activity" knobs onto the star's photosphere / flare / corona materials.
        // Volatility scales every animation speed at once. (Glow size/intensity are applied per-frame in Update
        // because they depend on the camera distance.)
        void ApplyStarKnobs()
        {
            if (focus == null || !focus.isStar || focus.mat == null) return;
            focus.mat.SetFloat("_SpotAmount", knobStarSpots);
            focus.mat.SetFloat("_Speed", 0.6f * knobStarVolatility);
            if (focus.flaresMat)
            {
                focus.flaresMat.SetFloat("_Intensity", 1.4f * knobStarFlares);
                focus.flaresMat.SetFloat("_Speed", 1.2f * knobStarVolatility);
            }
            if (focus.coronaMat)
            {
                focus.coronaMat.SetFloat("_Intensity", 1.4f * knobStarCoronaDensity);
                focus.coronaMat.SetFloat("_Curl", knobStarCoronaArc);
                focus.coronaMat.SetFloat("_Speed", 0.35f * knobStarVolatility);
                if (focus.coronaTf)
                {
                    focus.coronaTf.localScale = Vector3.one * knobStarCoronaSize;
                    focus.coronaMat.SetFloat("_InnerR", 0.5f / knobStarCoronaSize);
                }
            }
            if (focus.glowMat) focus.glowMat.SetFloat("_Speed", 0.1f * knobStarVolatility);
        }

        void Knob(string name, ref float val, float min, float max, ref bool changed)
        {
            float step = (max - min) * 0.04f;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: {val:0.##}", label, GUILayout.Width(128));
            float nv = GUILayout.HorizontalSlider(val, min, max);
            if (GUILayout.Button("−", GUILayout.Width(22))) nv = Mathf.Clamp(val - step, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22))) nv = Mathf.Clamp(val + step, min, max);
            GUILayout.EndHorizontal();
            if (Mathf.Abs(nv - val) > 1e-6f) { val = nv; changed = true; }
        }

        // Live-apply shader knobs to the focused planet's materials (no re-bake needed).
        void ApplyLiveKnobs()
        {
            if (focus == null || focus.planet == null) return;
            bool giant = focus.planet.type == PlanetType.GasGiant || focus.planet.type == PlanetType.IceGiant;
            focus.mat.SetFloat("_BumpScale", giant ? 0f : knobBump);
            focus.mat.SetFloat("_Ambient", knobAmbient);
            focus.mat.SetFloat("_RimPower", knobRim);
            focus.mat.SetFloat("_CamFill", knobCamFill);
            if (focus.oceanMat) ApplyOceanKnobs(focus.oceanMat);
            if (focus.atmoMat) ApplyAtmoKnobs(focus.atmoMat, giant);
        }

        void ResetKnobs()
        {
            tune.contFreq = tune.warp = tune.mountains = tune.detail = tune.relief = tune.craters = 1f;
            tune.seaLevel = 0f;
            knobBump = 16f; knobAmbient = 0.04f; knobRim = 3f; knobCamFill = 0.1f;
            knobGiantBands = 7f; knobGiantTurb = 0.5f; knobGiantSpeed = 0.1f;
            knobGiantChem = 0.35f; knobGiantFine = 0.5f; knobGiantSpot = 0.5f; knobGiantBandVar = 1f; knobGiantSpotSize = 0f;
            knobRingOpacity = 0.85f; knobRingWidth = 0.5f; knobRingInner = 0.45f;
            knobOceanSpec = 220f; knobOceanGain = 0.7f; knobOceanFres = 2.5f; knobOceanOpacity = 0.82f;
            knobAtmoIntensity = 0.32f; knobAtmoSoft = 1.3f;
            knobStarSpots = 0.26f; knobStarFlares = 1f; knobStarVolatility = 0.35f;
            knobStarCoronaSize = 1.36f; knobStarCoronaDensity = 0.15f; knobStarCoronaArc = 0.44f;
            knobStarGlowSize = 0.5f; knobStarGlowIntensity = 0.62f;
            ApplyLiveKnobs();
            if (focus != null && focus.isStar) ApplyStarKnobs();
            tuneDirty = true; tuneDirtyAt = Time.unscaledTime;
        }

        void DrawFocusInfo()
        {
            GUILayout.Space(4);
            if (focus.star != null)
            {
                var s = focus.star;
                string sn = string.IsNullOrEmpty(s.properName) ? s.Designation : s.properName;
                GUILayout.Label($"<b>{sn}</b>  <color=#9fb2d8>{s.Designation}</color>  {s.ClassLabel}", label);
                GUILayout.Label($"{s.stellarMass:0.00} M☉ · {Mathf.RoundToInt(s.effectiveTemp)} K · {s.luminosity:0.###} L☉", label);
            }
            else if (focus.planet != null)
            {
                var p = focus.planet;
                string hab = p.habClass == HabClass.Habitable ? "<color=#5fe0a0>Habitable</color>"
                           : p.habClass == HabClass.Marginal ? "<color=#f2c14e>Marginal</color>"
                           : p.habClass == HabClass.Hostile ? "<color=#d16b62>Hostile</color>" : "—";
                string native = string.IsNullOrEmpty(p.colloquial) ? "" : $"  <color=#c9b8e8>“{p.colloquial}”</color>";
                GUILayout.Label($"<b>{focus.label}</b>{native}  {p.type}", label);
                string massStr = p.mass < 0.1f ? $"{p.mass:0.00#}" : $"{p.mass:0.0}";
                GUILayout.Label($"{p.semiMajorAxisAU:0.00} AU · {Mathf.RoundToInt(p.meanTempC)} °C · {massStr} M⊕", label);
                GUILayout.Label(hab, label);
            }
        }

        void Stepper(string labelText, ref int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(labelText, label); GUILayout.FlexibleSpace();
            if (GUILayout.Button("−", GUILayout.Width(24)) && value > min) { value--; Regenerate(); }
            GUILayout.Label(value.ToString(), label, GUILayout.Width(18));
            if (GUILayout.Button("+", GUILayout.Width(24)) && value < max) { value++; Regenerate(); }
            GUILayout.EndHorizontal();
        }
    }
}
