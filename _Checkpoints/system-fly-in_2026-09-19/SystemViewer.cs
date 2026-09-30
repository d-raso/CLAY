using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

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
        public float timeScale = 0.12f;

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
        const int SurfaceTexLow = 256;           // cheap bake at spawn (fine for tiny overview dots)
        const int SurfaceTexHi = 1024;           // full-res bake, done lazily when a planet is focused
        const int SurfaceTexUltra = 2048;        // at default framing / mid zoom
        const int SurfaceTex4K = 4096;           // super-zoom crispness
        const float OceanShellScale = 1.001f;    // ocean sits right at sea level
        const float AtmoShellScale = 1.08f;      // thin atmosphere shell hugging the planet (soft edge)
        const float CloudShellScale = 1.015f;    // cloud deck just above the surface, below the haze shell

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
        Body focus;                              // body the camera is looking at / flying to
        bool flownIn;                            // true once you've clicked a world (scroll then orbits it)
        bool showOrbits = true;                  // orbit lines on/off (toggle with the O key)
        float systemSpan = 10f;
        float scaleT;                            // 0 = iconographic, 1 = true scale

        // camera state
        float camYaw = 35f, camPitch = 22f, camDist, camDistTarget;
        Vector2 dragStart; bool dragging;
        Vector3 focusPoint;
        GUIStyle title, label, help;

        class Body
        {
            public Transform tf; public Material mat; public LineRenderer line; public Transform lineTf;
            public OrbitElements orbit; public bool orbiting, isStar;
            public float realDistAU, realRadiusAU, iconDistAU, iconRadiusAU, spin;
            public string label; public PlanetData planet; public StarData star;
            public MeshFilter mf; public Mesh planetMesh; public Texture2D surfaceTex; public bool hiRes;
            public Material oceanMat, atmoMat, ringMat, cloudMat, coronaMat, flaresMat, glowMat;  // shells + ring + star glow layers
            public Transform oceanTf, atmoTf, ringTf, coronaTf, flaresTf, glowTf;    // shell/ring transforms (object-space cam pos, ring shadow, billboard)
            public Task<PlanetTexture.SurfacePixels> bakeTask; public int bakeWidth, texWidth;  // async bake
            public float giantSpotPotential;                    // 0..1; storm shows if < likelihood
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
        }

        void RebuildFromSys()
        {
            systemSpan = 1f;
            foreach (var pl in sys.planets) systemSpan = Mathf.Max(systemSpan, pl.orbit.semiMajorAxisAU * (1f + pl.orbit.eccentricity));
            foreach (var c in sys.companions) systemSpan = Mathf.Max(systemSpan, c.orbit.semiMajorAxisAU);

            SpawnStar(sys.star, default, false);
            foreach (var c in sys.companions) SpawnStar(c, c.orbit, true);
            foreach (var pl in sys.planets) SpawnPlanet(pl);

            focus = bodies[0];
            flownIn = false;
            focusPoint = Vector3.zero;
            camDistTarget = camDist = systemSpan * 2.2f;   // start in the iconographic overview
        }

        void SpawnStar(StarData s, OrbitElements orbit, bool orbiting)
        {
            var b = MakeBody($"{s.Designation}", s.color, s.color, orbit, orbiting, true);
            b.star = s;
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

        void SpawnPlanet(PlanetData pl)
        {
            Color day = PlanetColor(pl), night = day * 0.05f;
            var b = MakeBody($"{sys.star.spectralClass}{sys.star.subclass} {pl.name}", day, night, pl.orbit, true, false);
            b.planet = pl;
            b.realRadiusAU = pl.radiusEarth * EARTH_R_AU;
            b.iconRadiusAU = systemSpan * Mathf.Lerp(0.008f, 0.03f, Mathf.InverseLerp(0.5f, 12f, pl.radiusEarth));
            b.mat.SetColor("_RimColor", pl.habClass == HabClass.Habitable ? new Color(0.45f, 0.8f, 1f) : new Color(0.45f, 0.55f, 0.75f));
            b.spin = 8f;
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
                BakePlanetTex(b, SurfaceTexLow);      // cheap first pass; upgraded to hi-res on focus

                if (PlanetTexture.HasOcean(pl, pseed) && oceanShader != null)
                {
                    b.oceanMat = new Material(oceanShader);
                    b.oceanMat.SetColor("_DeepColor", PlanetTexture.OceanDeep(pl, pseed));
                    b.oceanMat.SetColor("_ShallowColor", PlanetTexture.OceanShallow(pl, pseed));
                    ApplyOceanKnobs(b.oceanMat);
                    b.oceanTf = MakeShell(b, b.oceanMat, OceanShellScale);   // right at sea level; land pokes through
                }
            }

            // Atmosphere haze shell — rocky worlds only (a giant's own shader already looks gaseous).
            if (!giant && PlanetTexture.HasAtmosphere(pl) && atmoShader != null)
            {
                b.atmoMat = new Material(atmoShader);
                b.atmoMat.SetColor("_AtmColor", PlanetTexture.AtmosphereColor(pl, pseed));
                b.atmoMat.SetFloat("_InnerR", 0.5f / AtmoShellScale);   // planet surface radius in shell space
                b.atmoMat.SetFloat("_OuterR", 0.5f);
                ApplyAtmoKnobs(b.atmoMat, giant);
                b.atmoTf = MakeShell(b, b.atmoMat, AtmoShellScale);

                // Dynamic cloud deck — sits just above the surface, drifts slowly, lit by the star. Coverage
                // scales with how wet the world is; hazy/cold worlds get tinted decks, temperate wet worlds
                // near-white. Skipped on essentially cloudless worlds (very dry + no volatiles).
                // Partial, broken cover — a full-sky deck (old formula peaked ~0.86) turned wet worlds into
                // featureless white balls and buried the terminator. Keep it well below full so surface,
                // continents, and the day/night line all read through the gaps.
                float cover = Mathf.Clamp01(0.10f + pl.waterCoverage * 0.38f + (pl.habClass == HabClass.Habitable ? 0.06f : 0f));
                if (cloudShader != null && cover > 0.22f)
                {
                    var crng = new DetRng(DetRng.Hash(pseed, 0x0C10D5UL));
                    Color atmC = PlanetTexture.AtmosphereColor(pl, pseed);
                    Color cloudC = Color.Lerp(atmC, Color.white, pl.meanTempC > -40f ? 0.72f : 0.4f);
                    b.cloudMat = new Material(cloudShader);
                    b.cloudMat.SetColor("_CloudColor", cloudC);
                    b.cloudMat.SetFloat("_Coverage", cover);
                    b.cloudMat.SetFloat("_Speed", crng.Range(0.03f, 0.09f));
                    b.cloudMat.SetVector("_Seed", new Vector4(crng.Range(0f, 20f), crng.Range(0f, 20f), crng.Range(0f, 20f), 0f));
                    MakeShell(b, b.cloudMat, CloudShellScale);
                }
            }
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
        float RenderRadius(Body b) => Mathf.Max(Mathf.Lerp(b.iconRadiusAU, b.realRadiusAU, scaleT), camDist * (b.isStar ? 0.016f : 0.011f));

        // ── update ──────────────────────────────────────────────────────────────────────────────────
        void Update()
        {
            HandleInput();
            PollBakes();
            PollTuning();

            // Blend factor from zoom: near the focused body → true scale; system-wide → iconographic.
            float near = (focus != null ? focus.realRadiusAU : systemSpan * 0.01f) * 8f;
            float far  = systemSpan * 2.2f;
            scaleT = 1f - Astrophysics.Smoothstep(near, Mathf.Max(far, near * 4f), camDist);

            float t = Time.time * timeScale;

            // ── Pass 1 — geometry: place, scale and spin every body first, so the focus point (and therefore
            // the camera) have this frame's positions to work from.
            foreach (var b in bodies)
            {
                float ds = DistScale(b);
                if (b.orbiting)
                {
                    float period = b.orbit.PeriodYears(sys.star.stellarMass);
                    float M = b.orbit.meanAnomalyDeg * Mathf.Deg2Rad + (period > 1e-4f ? Mathf.PI * 2f * t / period : 0f);
                    b.tf.localPosition = b.orbit.PositionAt(M) * ds;

                    // Orbit-line resolution scales with how far in you're zoomed, so it never facets up close.
                    if (b.line.enabled)
                    {
                        int segs = Mathf.Clamp(Mathf.RoundToInt(56f * Mathf.Sqrt(systemSpan / Mathf.Max(camDist, 1e-4f))), 56, 1024);
                        if (b.line.positionCount != segs) b.line.positionCount = segs;
                        for (int i = 0; i < segs; i++)
                            b.line.SetPosition(i, b.orbit.PositionFromEccentric(i / (float)(segs - 1) * Mathf.PI * 2f));
                        b.lineTf.localScale = Vector3.one * ds;
                        b.line.widthMultiplier = camDist * 0.0035f;
                    }
                }
                b.tf.localScale = Vector3.one * RenderRadius(b) * 2f;
                b.tf.Rotate(Vector3.up, b.spin * Time.deltaTime, Space.Self);
            }

            UpgradeFocusRes();
            UpdateCamera();   // move the camera BEFORE feeding uniforms, so view-anchored effects don't lag a frame

            // ── Pass 2 — shader uniforms, using THIS frame's camera. Anchoring the corona/glow/rim to a stale
            // camera made the whole halo slide when you moved fast (the "ghost halo").
            Vector3 starPos = bodies.Count > 0 ? bodies[0].tf.position : Vector3.zero;
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

                Vector3 toStar = starPos - b.tf.position;     // world direction from the body toward its star
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
            cam.nearClipPlane = Mathf.Clamp(camDist * 0.02f, 1e-5f, 50f);
            cam.farClipPlane  = Mathf.Max(systemSpan * 8f, camDist * 6f);
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

            if (Input.GetMouseButtonUp(0) && !dragging && !overPanel) TryPick(dragStart);

            if (Input.GetKeyDown(KeyCode.O))   // toggle orbit lines
            {
                showOrbits = !showOrbits;
                foreach (var b in bodies) if (b.line) b.line.enabled = showOrbits;
            }
        }

        void OrbitDrag(float sens)
        {
            camYaw += Input.GetAxis("Mouse X") * sens;
            camPitch = Mathf.Clamp(camPitch - Input.GetAxis("Mouse Y") * sens, -85f, 85f);
        }

        void Zoom(float factor)
        {
            float min = (focus != null ? focus.realRadiusAU : 0.01f) * 1.6f;
            camDistTarget = Mathf.Clamp(camDistTarget * factor, min, systemSpan * 5f);
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

        void FocusOn(Body b)
        {
            focus = b;
            flownIn = true;
            if (b.planet != null && !IsGiant(b.planet) && !b.hiRes) BakePlanetTexAsync(b, SurfaceTexHi);
            camDistTarget = Mathf.Max(b.realRadiusAU * (b.isStar ? 7f : 5f), 1e-5f);   // frame the body at true size
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
            Vector2 g = new Vector2(m.x, Screen.height - m.y);
            return panel.Contains(g) || (showTune && tunePanel.Contains(g));
        }

        void OnGUI()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                label = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
                help  = new GUIStyle(GUI.skin.label) { fontSize = 11 }; help.normal.textColor = new Color(1, 1, 1, 0.5f);
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
            GUILayout.Label((flownIn ? "Scroll: orbit · +/−: zoom" : "Scroll: zoom · Arrows: orbit") + " · O: orbit lines", help);
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
                GUILayout.Label($"<b>{s.Designation}</b>  {s.ClassLabel}", label);
                GUILayout.Label($"{s.stellarMass:0.00} M☉ · {Mathf.RoundToInt(s.effectiveTemp)} K · {s.luminosity:0.###} L☉", label);
            }
            else if (focus.planet != null)
            {
                var p = focus.planet;
                string hab = p.habClass == HabClass.Habitable ? "<color=#5fe0a0>Habitable</color>"
                           : p.habClass == HabClass.Marginal ? "<color=#f2c14e>Marginal</color>"
                           : p.habClass == HabClass.Hostile ? "<color=#d16b62>Hostile</color>" : "—";
                GUILayout.Label($"<b>{focus.label}</b>  {p.type}", label);
                GUILayout.Label($"{p.semiMajorAxisAU:0.00} AU · {Mathf.RoundToInt(p.meanTempC)} °C · {p.mass:0.0} M⊕", label);
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
