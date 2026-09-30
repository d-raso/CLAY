using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CLAY.GalaxyMap
{
    // Runtime galaxy: generates stars with the Burst job, then spawns one rendered entity per star (Entities
    // Graphics instancing). Put this on a GameObject in the Galaxy scene and assign the Clay/InstancedStar
    // material (or leave it null to auto-create one from the shader).
    public class GalaxyBootstrap : MonoBehaviour
    {
        [Header("Galaxy")]
        public int StarCount = 50000;
        public uint WorldSeed = 12345;
        public float PositionScale = 1f;   // world-units per generated unit
        public float StarSize = 0.25f;     // per-star quad size

        [Header("Size")]
        [Range(10f, 200f)] public float DiskRadius = 60f;
        [Range(0.005f, 0.15f)] public float DiskThickness = 0.03f;

        [Header("Morphology")]
        [Range(0f, 1f)] public float BulgeToDiskRatio = 0.25f;
        [Range(1f, 40f)] public float BulgeSize = 8f;
        [Range(0f, 1f)] public float Ellipticity = 0.2f;
        [Range(0f, 1f)] public float BarStrength = 0.35f;
        [Range(0f, 1f)] public float Irregularity = 0.12f;

        [Header("Spiral Arms")]
        [Range(1, 8)] public int ArmCount = 2;
        [Range(0.1f, 0.6f)] public float PitchAngle = 0.25f;
        [Range(0f, 12f)] public float ArmWidth = 4f;
        [Range(0f, 1f)] public float ArmDistinction = 0.75f;
        [Range(-1f, 1f)] public float ArmTaper = 0f;

        [Header("Composition")]
        [Range(0f, 1f)] public float StarBirthRate = 0.5f;

        [Header("Stellar Populations (0 = old/red, 1 = young/blue)")]
        [Range(0f, 1f)] public float CorePop = 0.1f;    // bulge / core
        [Range(0f, 1f)] public float ArmPop = 0.75f;    // spiral arms
        [Range(0f, 1f)] public float DiskPop = 0.35f;   // inter-arm disk

        [Header("Deformation")]
        [Range(0f, 1f)] public float DiskWarpS = 0f;         // integral-sign disk bend
        [Range(0f, 0.5f)] public float TidalTailStrength = 0f;
        [Range(0f, 1f)] public float Elongation = 0f;        // stretch the galaxy into an oval
        [Range(0f, 1f)] public float Ellipticalness = 0f;    // 0 = spiral, 1 = smooth spheroidal elliptical
        [Range(0, 5)] public int AnomalyCount = 0;           // seed-typed voids / disruptions

        [Header("Rendering")]
        public Material StarMaterial;      // Clay/InstancedStar; auto-created if left empty

        [Header("Dust")]
        public int DustParticleCount = 60000;               // dust particles (many are near-invisible by design)
        [Range(0.5f, 6f)] public float DustSize = 3f;       // dust billboard size (world units)
        [Range(0f, 8f)] public float DustOpacity = 1.0f;    // how strongly dense lanes occlude the stars
        [Range(0f, 1.5f)] public float DustDensity = 0.35f; // overall amount of dust structure (per galaxy)
        public Color DustColor = new Color(0.09f, 0.05f, 0.035f, 1f);   // reddening tint (extinction ∝ λ⁻¹)
        public Material DustMaterial;      // Clay/GalacticDust; auto-created if left empty

        [Header("Dust Formation")]
        [Range(0.01f, 0.2f)] public float DustCloudScale = 0.05f;   // overall scale — smaller = larger features
        [Range(0.2f, 3f)] public float DustBigNoise = 1f;           // large-scale bands / vortices frequency
        [Range(1f, 8f)] public float DustFineNoise = 3f;            // fine turbulence frequency
        [Range(0f, 1f)] public float DustFineWeight = 0.6f;         // how much fine detail breaks up the big forms
        [Range(0f, 3f)] public float DustSwirl = 1.2f;              // vortex strength (gas-giant swirls)
        [Range(0f, 3f)] public float DustWarp = 1.6f;               // flow-warp displacement
        [Range(0f, 1f)] public float DustArmInfluence = 0.35f;      // 0 = loose noise cloud, 1 = follows arms
        [Range(0f, 1f)] public float DustCoverage = 0.4f;           // 0 = sparse, 1 = continuous sheet
        [Range(0f, 1f)] public float DustClumpiness = 0.35f;        // 0 = uniform, 1 = large patches with voids
        [Range(0f, 0.6f)] public float DustContrast = 0.2f;         // higher = sparser, more voids
        [Range(0f, 6f)] public float DustShadowStrength = 2.5f;     // self-shadow depth in the lit dust
        [Range(0f, 1f)] public float DustFloat = 0.15f;            // 0 = dust in the disc plane, 1 = drifts off-plane
        [Range(0f, 1f)] public float DustWebbing = 0.5f;           // 0 = smooth clouds, 1 = marbled/webbed veins
        [Range(0f, 1f)] public float DustWebAmount = 0.55f;        // Voronoi filament web (fine cellular threads)
        [Range(0.02f, 0.4f)] public float DustWebScale = 0.12f;    // web cell size (bigger = finer web)
        [Range(0f, 30f)] public float DustNearFade = 6f;           // dust fades out this close to the camera (flythrough)

        [Header("Star Rendering")]
        [Range(0.0005f, 0.01f)] public float StarResolveSize = 0.0022f;  // below this on-screen size, stars blend (surface brightness held)
        [Range(0.01f, 0.15f)] public float StarMaxScreenSize = 0.03f;    // near stars shrink+dim above this, so flythroughs don't blow out
        public Color GalaxyTint = Color.white;                          // per-galaxy overall tint (between-galaxy variety)
        [Range(0f, 0.4f)] public float StarHueJitter = 0.14f;           // per-star colour variation (within-galaxy variety)

        [Header("Nebulae")]
        public int NebulaRegions = 70;                       // discrete star-forming regions in the arms
        [Range(1, 40)] public int PuffsPerRegion = 12;       // glow puffs per region (irregular cloud shape)
        [Range(1f, 20f)] public float NebulaRegionRadius = 5f;
        [Range(1f, 30f)] public float NebulaSize = 3.5f;     // puff billboard size
        [Range(0f, 1.5f)] public float NebulaBrightness = 0.16f;
        [Range(0f, 1f)] public float NebulaSizeJitter = 0.6f;     // few big complexes vs many small regions
        [Range(0f, 2f)] public float NebulaArmSpread = 0.6f;      // lateral scatter off the arm centre
        [Range(0f, 0.6f)] public float NebulaRadialMin = 0.1f;    // inner disk fraction nebulae start
        [Range(0.3f, 1.2f)] public float NebulaRadialMax = 0.8f;  // outer disk fraction they end
        [Range(0f, 1f)] public float NebulaTeal = 0.5f;           // how much O III teal shows in the hot cores
        [Range(0f, 1f)] public float NebulaEvaporation = 0.4f;    // photoevaporation: erode gas exposed to the cluster
        [Range(0f, 1f)] public float ReflectionFraction = 0.12f;  // cool reflection nebulae vs warm emission
        public Material NebulaMaterial;    // Clay/NebulaEmission; auto-created if left empty

        [Header("Absorption Nebulae (dark sculpted dust — Pillars of Creation)")]
        public int AbsorptionRegions = 40;                       // dark sculpted dust masses in the arms
        [Range(1, 60)] public int AbsorptionPuffs = 26;          // dense clustering → solid sculpted mass
        [Range(1f, 15f)] public float AbsorptionRegionRadius = 3.5f;
        [Range(1f, 25f)] public float AbsorptionSize = 5f;
        [Range(0f, 1f)] public float AbsorptionOpacity = 0.9f;
        [Range(0.02f, 0.4f)] public float AbsorptionEdge = 0.12f;      // lower = sharper, harder sculpted edges
        [Range(0.2f, 0.8f)] public float AbsorptionThreshold = 0.5f;   // higher = smaller, denser masses
        [Range(0f, 1f)] public float AbsorptionEdgeTurb = 0.5f;        // frayed Kelvin-Helmholtz borders
        [Range(0f, 1f)] public float AbsorptionEvaporation = 0.85f;    // carves pillars pointing at the cluster
        public Color AbsorptionColor = new Color(0.02f, 0.012f, 0.008f, 1f);
        public Material AbsorptionMaterial;   // Clay/AbsorptionNebula; auto-created if left empty

        [Header("Volumetric Hero Nebulae (raymarched — keep the count low, they're expensive)")]
        [Range(0, 60)] public int VolumetricNebulaCount = 26;
        [Range(2f, 80f)] public float VolumetricNebulaSize = 4f;
        [Range(16, 160)] public int VolumetricSteps = 80;   // finer raymarch → more detail resolved

        [Header("Nebula Preview (press N: one hero nebula + a few stars, no galaxy)")]
        public KeyCode NebulaPreviewKey = KeyCode.N;
        [Range(0, 3000)] public int PreviewStarCount = 600;
        [Range(1f, 6f)] public float PreviewNebulaSizeMul = 2.5f;   // hero nebula = VolumetricNebulaSize × this
        bool _previewMode;

        [Header("Absorption Test (single isolated nebula)")]
        public bool AddTestAbsorption = false;                       // spawn ONE big absorption nebula only, for tuning
        public Vector3 TestAbsorptionPos = new Vector3(28f, 0f, 0f);
        [Range(2f, 40f)] public float TestAbsorptionSize = 12f;      // puff size
        [Range(5f, 40f)] public float TestAbsorptionRadius = 12f;    // cluster spread
        [Range(10, 400)] public int TestAbsorptionPuffs = 120;

        [Header("Star Depth Occlusion")]
        public bool OccludeStarsWithDepth = true;   // dust occludes only stars behind it (depth-only star cores)

        [Header("Debug")]
        public bool AddTestStar = false;                            // one big bright star far out in the disc plane
        public Vector3 TestStarPos = new Vector3(120f, 0f, 0f);
        [Range(0.5f, 20f)] public float TestStarSize = 6f;

        [Header("Post FX — starlight smear (GPU bloom)")]
        public bool EnableBloom = true;                     // far stars bleed together into milky light
        [Range(0f, 4f)] public float BloomIntensity = 1.2f;
        [Range(0f, 1f)] public float BloomScatter = 0.75f;  // higher = wider, softer smear
        [Range(0f, 1.5f)] public float BloomThreshold = 0f; // 0 = catch even faint stars

        [Header("Controls")]
        public KeyCode RegenerateKey = KeyCode.R;   // rebuild with the current Inspector values at runtime
        public bool RandomizeOnPlay = true;         // reroll seed + all sliders on Play (and on each Regenerate)

        EntityManager _em;
        Material _mat;
        Material _dustMat;
        Mesh _mesh;
        NativeArray<Entity> _entities;
        NativeArray<Entity> _dust;
        Material _starDepthMat;
        NativeArray<Entity> _starDepth;
        Material _nebulaMat;
        NativeArray<Entity> _nebula;
        Material _absorptionMat;
        NativeArray<Entity> _absorption;
        readonly List<GameObject> _volNebulae = new List<GameObject>();
        Entity _testStar = Entity.Null;
        GameObject _postFx;
        Bloom _bloom;
        bool _ready;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) { Debug.LogError("[Galaxy] No default ECS world — is the Entities package active?"); return; }
            _em = world.EntityManager;

            _mat = StarMaterial;
            if (_mat == null)
            {
                var sh = Shader.Find("Clay/InstancedStar");
                if (sh == null) { Debug.LogError("[Galaxy] Shader 'Clay/InstancedStar' not found."); return; }
                _mat = new Material(sh) { enableInstancing = true };
            }
            _dustMat = DustMaterial;
            if (_dustMat == null)
            {
                var sh = Shader.Find("Clay/GalacticDust");
                if (sh != null) _dustMat = new Material(sh) { enableInstancing = true };
                else Debug.LogWarning("[Galaxy] Shader 'Clay/GalacticDust' not found — dust disabled.");
            }
            {
                var sh = Shader.Find("Clay/StarDepth");
                if (sh != null) _starDepthMat = new Material(sh) { enableInstancing = true };
                else Debug.LogWarning("[Galaxy] Shader 'Clay/StarDepth' not found — depth occlusion disabled.");
            }
            _nebulaMat = NebulaMaterial;
            if (_nebulaMat == null)
            {
                var sh = Shader.Find("Clay/NebulaEmission");
                if (sh != null) _nebulaMat = new Material(sh) { enableInstancing = true };
                else Debug.LogWarning("[Galaxy] Shader 'Clay/NebulaEmission' not found — nebulae disabled.");
            }
            _absorptionMat = AbsorptionMaterial;
            if (_absorptionMat == null)
            {
                var sh = Shader.Find("Clay/AbsorptionNebula");
                if (sh != null) _absorptionMat = new Material(sh) { enableInstancing = true };
                else Debug.LogWarning("[Galaxy] Shader 'Clay/AbsorptionNebula' not found — absorption nebulae disabled.");
            }
            _mesh = BuildQuad();
            _ready = true;
            SetupPostFX();
            if (RandomizeOnPlay) RandomizeParameters();
            Generate();
        }

        void Update()
        {
            if (_mat != null)
            {
                _mat.SetFloat("_MinPointNdc", StarResolveSize);
                _mat.SetFloat("_MaxPointNdc", StarMaxScreenSize);
            }
            if (_starDepthMat != null)
            {
                _starDepthMat.SetFloat("_MinPointNdc", StarResolveSize);
                _starDepthMat.SetFloat("_MaxPointNdc", StarMaxScreenSize);
            }
            if (_dustMat != null)
            {
                _dustMat.SetFloat("_NearFade", DustNearFade);
                _dustMat.SetFloat("_CoreGlowDist", DiskRadius * 0.6f);   // amber back-scatter reaches ~60% of the disk
            }

            // Live-tune bloom from the Inspector (starlight smear).
            if (_bloom != null)
            {
                _bloom.active = EnableBloom;
                _bloom.intensity.Override(BloomIntensity);
                _bloom.scatter.Override(BloomScatter);
                _bloom.threshold.Override(BloomThreshold);
            }

            if (_ready && Input.GetKeyDown(RegenerateKey))
            {
                _previewMode = false;
                if (RandomizeOnPlay) RandomizeParameters();
                Generate();
            }

            // N → isolated nebula preview (one hero nebula + a small star cluster, no galaxy). Press again for a
            // fresh one; press R to return to a full galaxy.
            if (_ready && Input.GetKeyDown(NebulaPreviewKey))
            {
                _previewMode = true;
                Generate();
                FrameNebulaPreview();
            }
        }

        // Reroll the seed and every aesthetic parameter within tasteful ranges → a fresh galaxy each Play / R.
        void RandomizeParameters()
        {
            WorldSeed = (uint)UnityEngine.Random.Range(1, int.MaxValue);

            DiskRadius = UnityEngine.Random.Range(40f, 95f);
            DiskThickness = UnityEngine.Random.Range(0.02f, 0.07f);

            BulgeSize = UnityEngine.Random.Range(5f, 16f);
            Ellipticity = UnityEngine.Random.Range(0f, 0.4f);
            Irregularity = UnityEngine.Random.Range(0.05f, 0.28f);

            ArmCount = UnityEngine.Random.Range(2, 6);
            PitchAngle = UnityEngine.Random.Range(0.14f, 0.42f);
            ArmWidth = UnityEngine.Random.Range(2.5f, 6.5f);
            ArmTaper = UnityEngine.Random.Range(-0.5f, 0.7f);
            StarBirthRate = UnityEngine.Random.Range(0.3f, 0.8f);

            Elongation = UnityEngine.Random.value < 0.35f ? UnityEngine.Random.Range(0.05f, 0.35f) : 0f;
            AnomalyCount = UnityEngine.Random.value < 0.4f ? UnityEngine.Random.Range(1, 4) : 0;

            // Galaxy TYPE roll: ~30% elliptical, ~20% lenticular (S0), ~50% spiral.
            float typeRoll = UnityEngine.Random.value;
            if (typeRoll < 0.3f)
            {
                // Elliptical: old, red, spheroidal, no arms/bars, very little dust/gas.
                Ellipticalness = UnityEngine.Random.Range(0.75f, 1f);
                BulgeToDiskRatio = UnityEngine.Random.Range(0.75f, 0.95f);
                BarStrength = 0f;
                ArmDistinction = 0f;
                ArmWidth = UnityEngine.Random.Range(6f, 12f);
                DiskThickness = UnityEngine.Random.Range(0.1f, 0.3f);
                DiskWarpS = 0f;
                Elongation = UnityEngine.Random.Range(0.1f, 0.6f);   // E0–E7 shapes
                CorePop = UnityEngine.Random.Range(0f, 0.12f);
                ArmPop = UnityEngine.Random.Range(0f, 0.12f);
                DiskPop = UnityEngine.Random.Range(0f, 0.12f);
                NebulaRegions = UnityEngine.Random.Range(0, 12);
                AbsorptionRegions = UnityEngine.Random.Range(0, 4);
                DustDensity = UnityEngine.Random.Range(0.02f, 0.1f);    // gas-poor
                DustOpacity = UnityEngine.Random.Range(0.2f, 0.6f);
            }
            else if (typeRoll < 0.5f)
            {
                // Lenticular (S0): disk + prominent bulge but arms are very wide / indistinct; modest dust.
                Ellipticalness = UnityEngine.Random.Range(0.15f, 0.45f);
                BulgeToDiskRatio = UnityEngine.Random.Range(0.4f, 0.7f);
                BarStrength = UnityEngine.Random.Range(0f, 0.5f);
                ArmDistinction = UnityEngine.Random.Range(0f, 0.15f);   // no clear arms
                ArmWidth = UnityEngine.Random.Range(7f, 12f);           // very wide, washed out
                DiskWarpS = UnityEngine.Random.Range(0f, 0.2f);
                CorePop = UnityEngine.Random.Range(0.02f, 0.2f);
                ArmPop = UnityEngine.Random.Range(0.15f, 0.45f);        // older disk, little star formation
                DiskPop = UnityEngine.Random.Range(0.1f, 0.35f);
                NebulaRegions = UnityEngine.Random.Range(5, 40);
                AbsorptionRegions = UnityEngine.Random.Range(5, 25);
                DustDensity = UnityEngine.Random.Range(0.1f, 0.3f);
                DustOpacity = UnityEngine.Random.Range(0.4f, 1.2f);
            }
            else
            {
                // Spiral / barred spiral.
                Ellipticalness = 0f;
                BulgeToDiskRatio = UnityEngine.Random.Range(0.12f, 0.4f);
                BarStrength = UnityEngine.Random.Range(0f, 1f);         // full bar-strength variation
                ArmDistinction = UnityEngine.Random.Range(0.2f, 0.6f);
                ArmWidth = UnityEngine.Random.Range(2.5f, 7f);
                DiskWarpS = UnityEngine.Random.Range(0f, 0.35f);
                CorePop = UnityEngine.Random.Range(0.02f, 0.25f);      // core: old & red
                ArmPop = UnityEngine.Random.Range(0.6f, 0.98f);        // arms: young & blue
                DiskPop = UnityEngine.Random.Range(0.15f, 0.5f);       // inter-arm: middling
                NebulaRegions = UnityEngine.Random.Range(40, 120);
                AbsorptionRegions = UnityEngine.Random.Range(25, 65);
                DustDensity = UnityEngine.Random.Range(0.2f, 0.55f);
                DustOpacity = UnityEngine.Random.Range(0.6f, 1.8f);
            }

            TidalTailStrength = UnityEngine.Random.value < 0.25f ? UnityEngine.Random.Range(0f, 0.12f) : 0f;

            // Dust formation — wide ranges so structure differs a lot galaxy-to-galaxy (DustDensity/DustOpacity
            // are set per galaxy-type above).
            DustCloudScale = UnityEngine.Random.Range(0.025f, 0.11f);
            DustBigNoise = UnityEngine.Random.Range(0.5f, 2f);
            DustFineNoise = UnityEngine.Random.Range(1.8f, 6f);
            DustFineWeight = UnityEngine.Random.Range(0.2f, 0.9f);
            DustSwirl = UnityEngine.Random.Range(0.4f, 2.5f);
            DustWarp = UnityEngine.Random.Range(0.8f, 3f);
            DustArmInfluence = UnityEngine.Random.Range(0.05f, 0.7f);
            DustCoverage = UnityEngine.Random.Range(0.2f, 0.65f);
            DustClumpiness = UnityEngine.Random.Range(0.05f, 0.6f);
            DustContrast = UnityEngine.Random.Range(0.08f, 0.35f);
            DustFloat = UnityEngine.Random.Range(0.05f, 0.4f);
            DustWebbing = UnityEngine.Random.Range(0.1f, 0.7f);
            DustWebAmount = UnityEngine.Random.Range(0.35f, 0.8f);
            DustWebScale = UnityEngine.Random.Range(0.07f, 0.2f);

            // Per-galaxy colour character (between-galaxy variety): warm/red ↔ cool/blue with a slight green jitter.
            float warm = UnityEngine.Random.Range(-1f, 1f);
            GalaxyTint = new Color(1f + 0.28f * warm, 1f + 0.14f * UnityEngine.Random.Range(-1f, 1f), 1f - 0.28f * warm, 1f);
            StarHueJitter = UnityEngine.Random.Range(0.1f, 0.28f);

            // Nebulae.
            ReflectionFraction = UnityEngine.Random.Range(0.05f, 0.25f);
            NebulaBrightness = UnityEngine.Random.Range(0.1f, 0.28f);
            NebulaSize = UnityEngine.Random.Range(2.5f, 5f);
            NebulaSizeJitter = UnityEngine.Random.Range(0.3f, 0.9f);
            NebulaArmSpread = UnityEngine.Random.Range(0.3f, 1.0f);
            NebulaTeal = UnityEngine.Random.Range(0.2f, 0.7f);
            NebulaRadialMin = UnityEngine.Random.Range(0.05f, 0.25f);
            NebulaRadialMax = UnityEngine.Random.Range(0.7f, 1.1f);   // sometimes reach the disc edge
            NebulaEvaporation = UnityEngine.Random.Range(0.2f, 0.7f);
            AbsorptionEvaporation = UnityEngine.Random.Range(0.6f, 1f);
            AbsorptionEdgeTurb = UnityEngine.Random.Range(0.3f, 0.85f);
        }

        // GPU starlight smear via URP Bloom: bright stars bleed into neighbours, so a distant (screen-dense)
        // galaxy melts into milky unresolved light while a near (spread-out) one keeps distinct stars. Runs as a
        // post-process on the composited frame — no per-star camera math, so it can't hit the BRG shader limits.
        void SetupPostFX()
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[Galaxy] No Camera.main — bloom smear disabled (tag the galaxy camera 'MainCamera')."); return; }
            cam.allowHDR = true;
            var acd = cam.GetUniversalAdditionalCameraData();
            if (acd != null) acd.renderPostProcessing = true;

            _postFx = new GameObject("GalaxyPostFX");
            _postFx.transform.SetParent(transform, false);
            var vol = _postFx.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = profile;
            _bloom = profile.Add<Bloom>(true);
            _bloom.threshold.Override(BloomThreshold);
            _bloom.intensity.Override(BloomIntensity);
            _bloom.scatter.Override(BloomScatter);
            _bloom.active = EnableBloom;
        }

        GalaxyParameters BuildParams() => new GalaxyParameters
        {
            DiskRadius = DiskRadius, DiskThickness = DiskThickness,
            BulgeToDiskRatio = BulgeToDiskRatio, BulgeSize = BulgeSize, Ellipticity = Ellipticity, BarStrength = BarStrength, Irregularity = Irregularity,
            ArmCount = ArmCount, PitchAngle = PitchAngle, ArmWidth = ArmWidth, ArmDistinction = ArmDistinction, ArmTaper = ArmTaper,
            GasDustDensity = DustDensity, StarBirthRate = StarBirthRate,
            CorePop = CorePop, ArmPop = ArmPop, DiskPop = DiskPop,
            TidalTailStrength = TidalTailStrength, DiskWarpS = DiskWarpS, CollisionRingPhase = 0f,
            Elongation = Elongation, DustFloat = DustFloat, Ellipticalness = Ellipticalness, AnomalyCount = AnomalyCount,
            WorldSeed = WorldSeed,
        };

        // Rebuild the whole galaxy from the current parameters (destroys the previous entities first).
        [ContextMenu("Regenerate")]
        public void Generate()
        {
            if (!_ready) return;
            ClearEntities();

            // PREVIEW MODE (press N): shrink to a small star clump so the hero nebula at the origin can be studied
            // on its own. Saved params are restored at the end so R rebuilds a normal galaxy.
            int _pvStar = StarCount; float _pvDisk = DiskRadius, _pvThick = DiskThickness, _pvBulge = BulgeSize;
            int _pvAnom = AnomalyCount;
            if (_previewMode)
            {
                StarCount = math.max(1, PreviewStarCount);
                DiskRadius = VolumetricNebulaSize * PreviewNebulaSizeMul * 3.5f;
                DiskThickness = 0.6f;
                BulgeSize = 0f;
                AnomalyCount = 0;
            }

            // 1) Generate star positions/temperatures off the main thread.
            var stars = new NativeArray<GalaxyStar>(StarCount, Allocator.TempJob);
            var genJob = new GenerateGalaxyJob { Params = BuildParams(), StarOutput = stars };
            // Fully-qualified so it binds to Unity.Jobs' parallel-for Schedule (Unity.Entities also adds a
            // query-based Schedule extension, which otherwise makes the call ambiguous).
            Unity.Jobs.IJobParallelForExtensions.Schedule(genJob, StarCount, 256).Complete();

            // 2) Build a render prototype entity (Entities Graphics render components + our instanced props).
            var desc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: false);
            var rma = new RenderMeshArray(new[] { _mat }, new[] { _mesh });
            var proto = _em.CreateEntity();
            RenderMeshUtility.AddComponents(proto, _em, in desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            if (!_em.HasComponent<LocalToWorld>(proto)) _em.AddComponentData(proto, new LocalToWorld { Value = float4x4.identity });
            _em.AddComponentData(proto, new StarColor { Value = new float4(1f, 1f, 1f, 1f) });

            // Optional depth-only twin population (Clay/StarDepth): same transforms, writes core depth so dust
            // occludes only the stars actually behind it.
            bool depth = OccludeStarsWithDepth && _starDepthMat != null;
            int depthCount = StarCount + (AddTestStar ? 1 : 0);
            Entity depthProto = Entity.Null;
            if (depth)
            {
                var drma = new RenderMeshArray(new[] { _starDepthMat }, new[] { _mesh });
                depthProto = _em.CreateEntity();
                RenderMeshUtility.AddComponents(depthProto, _em, in desc, drma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                if (!_em.HasComponent<LocalToWorld>(depthProto)) _em.AddComponentData(depthProto, new LocalToWorld { Value = float4x4.identity });
                _starDepth = new NativeArray<Entity>(depthCount, Allocator.Persistent);
                _em.Instantiate(depthProto, _starDepth);
            }

            // 3) Instantiate one entity per star and place/colour it.
            _entities = new NativeArray<Entity>(StarCount, Allocator.Persistent);
            _em.Instantiate(proto, _entities);
            float3 gStarTint = new float3(GalaxyTint.r, GalaxyTint.g, GalaxyTint.b);
            for (int i = 0; i < StarCount; i++)
            {
                GalaxyStar s = stars[i];
                // Wide luminosity range: a power-law draw (many faint, a few brilliant), biased brighter for hot
                // O/B stars. Bright > 1 blooms under additive blending, which sells the luminous outliers. Size
                // grows with both temperature and luminosity so the giants read bigger, not just brighter.
                float hot = math.saturate((s.Temperature - 3000f) / 27000f);
                float lum = math.max(s.Luminosity, 0f);
                float bright = math.lerp(0.15f, 4.0f, lum) * math.lerp(0.7f, 1.5f, hot);
                float size = s.Luminosity < 0f ? 0f   // anomaly void → hidden
                    : StarSize * math.lerp(0.75f, 1.7f, hot) * math.lerp(0.85f, 1.6f, lum);
                var xform = new LocalToWorld { Value = float4x4.TRS(s.Position * PositionScale, quaternion.identity, new float3(size)) };
                _em.SetComponentData(_entities[i], xform);

                // Colour = blackbody × per-star hue jitter (within-galaxy variety) × per-galaxy tint (between).
                float4 bc = BlackbodyColor(s.Temperature, bright);
                float j1 = math.frac(s.Age * 13.73f) - 0.5f;
                float j2 = math.frac(s.Age * 7.19f + 0.3f) - 0.5f;
                float3 col = bc.xyz * (1f + StarHueJitter * new float3(j1, (j1 + j2) * 0.5f, j2)) * gStarTint;
                _em.SetComponentData(_entities[i], new StarColor { Value = new float4(math.max(col, 0f), 1f) });
                if (depth) _em.SetComponentData(_starDepth[i], xform);
            }
            _em.DestroyEntity(proto);   // keep only the instances

            // Debug: one big bright star far out in the disc plane, to test dust occlusion from either side.
            if (AddTestStar)
            {
                var testProto = _em.CreateEntity();
                RenderMeshUtility.AddComponents(testProto, _em, in desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                if (!_em.HasComponent<LocalToWorld>(testProto)) _em.AddComponentData(testProto, new LocalToWorld { Value = float4x4.identity });
                _em.AddComponentData(testProto, new StarColor { Value = new float4(1f, 1f, 1f, 1f) });
                _testStar = _em.Instantiate(testProto);
                var txf = new LocalToWorld { Value = float4x4.TRS((float3)TestStarPos, quaternion.identity, new float3(TestStarSize)) };
                _em.SetComponentData(_testStar, txf);
                _em.SetComponentData(_testStar, new StarColor { Value = new float4(6f, 5.5f, 5f, 1f) });   // very bright white
                if (depth) _em.SetComponentData(_starDepth[StarCount], txf);
                _em.DestroyEntity(testProto);
            }

            if (depth) _em.DestroyEntity(depthProto);

            stars.Dispose();

            if (_previewMode)
            {
                SpawnSingleNebulaPreview();
                Debug.Log($"[Galaxy] Nebula preview: 1 hero nebula + {StarCount} stars.");
            }
            else
            {
                GenerateDust();
                GenerateNebulae();
                GenerateAbsorption();
                SpawnVolumetricNebulae();
                Debug.Log($"[Galaxy] Spawned {StarCount} stars + {(_dust.IsCreated ? _dust.Length : 0)} dust + {(_nebula.IsCreated ? _nebula.Length : 0)} nebula puffs (seed {WorldSeed}).");
            }

            // Restore params overridden for preview so a later R rebuilds a full-size galaxy.
            if (_previewMode)
            {
                StarCount = _pvStar; DiskRadius = _pvDisk; DiskThickness = _pvThick;
                BulgeSize = _pvBulge; AnomalyCount = _pvAnom;
            }
        }

        // Dark absorption nebulae: dense sculpted dust masses (reuses the region-clustering job, tighter, rendered
        // with the hard-edged occluding AbsorptionNebula material). Foreground stars punch through via depth.
        void GenerateAbsorption()
        {
            if (_absorptionMat == null) return;
            _absorptionMat.SetFloat("_Edge", AbsorptionEdge);
            _absorptionMat.SetFloat("_Threshold", AbsorptionThreshold);
            _absorptionMat.SetFloat("_EdgeTurb", AbsorptionEdgeTurb);

            if (AddTestAbsorption) { GenerateTestAbsorption(); return; }

            int count = AbsorptionRegions * math.max(1, AbsorptionPuffs);
            if (count <= 0) return;

            var neb = new NativeArray<GalaxyStar>(count, Allocator.TempJob);
            var job = new GenerateNebulaJob
            {
                Params = BuildParams(),
                PuffsPerRegion = math.max(1, AbsorptionPuffs),
                RegionRadius = AbsorptionRegionRadius,
                ReflectionFrac = 0f,
                SizeJitter = 0.5f,
                ArmSpread = NebulaArmSpread,
                RadialMin = NebulaRadialMin,
                RadialMax = NebulaRadialMax,
                Evaporation = AbsorptionEvaporation,
                Output = neb,
            };
            Unity.Jobs.IJobParallelForExtensions.Schedule(job, count, 128).Complete();

            var desc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: false);
            var rma = new RenderMeshArray(new[] { _absorptionMat }, new[] { _mesh });
            var proto = _em.CreateEntity();
            RenderMeshUtility.AddComponents(proto, _em, in desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            if (!_em.HasComponent<LocalToWorld>(proto)) _em.AddComponentData(proto, new LocalToWorld { Value = float4x4.identity });
            _em.AddComponentData(proto, new StarColor { Value = new float4(1f, 1f, 1f, 1f) });

            float3 dark = new float3(AbsorptionColor.r, AbsorptionColor.g, AbsorptionColor.b);
            _absorption = new NativeArray<Entity>(count, Allocator.Persistent);
            _em.Instantiate(proto, _absorption);
            for (int i = 0; i < count; i++)
            {
                GalaxyStar s = neb[i];
                float size = AbsorptionSize * math.max(s.Age, 0.25f);
                float alpha = math.saturate(s.Luminosity * AbsorptionOpacity);
                if (alpha <= 0.02f) size = 0f;
                _em.SetComponentData(_absorption[i], new LocalToWorld
                {
                    Value = float4x4.TRS(s.Position * PositionScale, quaternion.identity, new float3(size))
                });
                _em.SetComponentData(_absorption[i], new StarColor { Value = new float4(dark, alpha) });
            }
            _em.DestroyEntity(proto);
            neb.Dispose();
        }

        // Discrete emission/reflection nebulae clustered in the arms (GenerateNebulaJob). Additive coloured puffs.
        void GenerateNebulae()
        {
            int count = NebulaRegions * math.max(1, PuffsPerRegion);
            if (count <= 0 || _nebulaMat == null) return;

            var neb = new NativeArray<GalaxyStar>(count, Allocator.TempJob);
            var job = new GenerateNebulaJob
            {
                Params = BuildParams(),
                PuffsPerRegion = math.max(1, PuffsPerRegion),
                RegionRadius = NebulaRegionRadius,
                ReflectionFrac = ReflectionFraction,
                SizeJitter = NebulaSizeJitter,
                ArmSpread = NebulaArmSpread,
                RadialMin = NebulaRadialMin,
                RadialMax = NebulaRadialMax,
                Evaporation = NebulaEvaporation,
                Output = neb,
            };
            Unity.Jobs.IJobParallelForExtensions.Schedule(job, count, 128).Complete();

            var desc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: false);
            var rma = new RenderMeshArray(new[] { _nebulaMat }, new[] { _mesh });
            var proto = _em.CreateEntity();
            RenderMeshUtility.AddComponents(proto, _em, in desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            if (!_em.HasComponent<LocalToWorld>(proto)) _em.AddComponentData(proto, new LocalToWorld { Value = float4x4.identity });
            _em.AddComponentData(proto, new StarColor { Value = new float4(1f, 1f, 1f, 1f) });

            _nebula = new NativeArray<Entity>(count, Allocator.Persistent);
            _em.Instantiate(proto, _nebula);
            for (int i = 0; i < count; i++)
            {
                GalaxyStar s = neb[i];
                float size = NebulaSize * math.max(s.Age, 0.2f);   // s.Age carries the per-puff size factor
                _em.SetComponentData(_nebula[i], new LocalToWorld
                {
                    Value = float4x4.TRS(s.Position * PositionScale, quaternion.identity, new float3(size))
                });
                _em.SetComponentData(_nebula[i], new StarColor { Value = NebulaColor(s.Temperature, s.Luminosity) });
            }
            _em.DestroyEntity(proto);
            neb.Dispose();
        }

        // A single big dark absorption nebula at TestAbsorptionPos, for inspecting/tuning the sculpted look.
        void GenerateTestAbsorption()
        {
            int count = math.max(1, TestAbsorptionPuffs);
            var desc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: false);
            var rma = new RenderMeshArray(new[] { _absorptionMat }, new[] { _mesh });
            var proto = _em.CreateEntity();
            RenderMeshUtility.AddComponents(proto, _em, in desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            if (!_em.HasComponent<LocalToWorld>(proto)) _em.AddComponentData(proto, new LocalToWorld { Value = float4x4.identity });
            _em.AddComponentData(proto, new StarColor { Value = new float4(1f, 1f, 1f, 1f) });

            float3 dark = new float3(AbsorptionColor.r, AbsorptionColor.g, AbsorptionColor.b);
            _absorption = new NativeArray<Entity>(count, Allocator.Persistent);
            _em.Instantiate(proto, _absorption);
            var rnd = new Unity.Mathematics.Random(24680u);
            for (int i = 0; i < count; i++)
            {
                float3 g = new float3(NextGauss(ref rnd), NextGauss(ref rnd) * 0.6f, NextGauss(ref rnd));
                float3 pos = (float3)TestAbsorptionPos + g * TestAbsorptionRadius;
                float d = math.length(g);
                float alpha = math.saturate(math.exp(-d * d * 0.4f) * AbsorptionOpacity + 0.08f);

                // Photoevaporation toward the cluster centre → pillars point at the (test) star cluster.
                float3 toC = (float3)TestAbsorptionPos - pos;
                float distC = math.length(toC);
                float3 sdir = math.normalizesafe(toC, new float3(1f, 0f, 0f));
                float shield = 0f;
                for (int s = 1; s <= 4; s++)
                {
                    float3 sp = (pos + sdir * (distC * (s / 5f))) * 0.12f;
                    shield = math.max(shield, math.saturate(noise.snoise(sp) * 0.6f + 0.5f));
                }
                float exposed = math.saturate(1f - shield * 1.8f);
                alpha *= math.lerp(1f, 0.06f, exposed * AbsorptionEvaporation);

                float size = TestAbsorptionSize * rnd.NextFloat(0.6f, 1.4f);
                _em.SetComponentData(_absorption[i], new LocalToWorld
                {
                    Value = float4x4.TRS(pos * PositionScale, quaternion.identity, new float3(size))
                });
                _em.SetComponentData(_absorption[i], new StarColor { Value = new float4(dark, alpha) });
            }
            _em.DestroyEntity(proto);
        }

        static float NextGauss(ref Unity.Mathematics.Random r)
        {
            float u1 = math.max(r.NextFloat(), 1e-6f);
            return math.sqrt(-2f * math.log(u1)) * math.cos(2f * math.PI * r.NextFloat());
        }

        // A few raymarched (volumetric) hero nebulae placed in the arms. These are real GameObjects with the
        // Clay/VolumetricNebula material — expensive per-pixel, so keep the count small.
        void SpawnVolumetricNebulae()
        {
            ClearVolumetric();
            if (VolumetricNebulaCount <= 0 || Shader.Find("Clay/VolumetricNebula") == null) return;

            int arms = Mathf.Max(1, ArmCount);
            float pitch = Mathf.Max(PitchAngle, 0.02f);
            float barLen = BarStrength * DiskRadius * 0.35f;

            for (int i = 0; i < VolumetricNebulaCount; i++)
            {
                int a = UnityEngine.Random.Range(0, arms);
                float offset = a * (Mathf.PI * 2f / arms);
                float align = 0.5f + 0.5f * Mathf.Cos(2f * offset);
                float barW = Mathf.Clamp01(BarStrength * 2f);
                float rootR = Mathf.Max(Mathf.Lerp(barLen * 0.3f, Mathf.Max(barLen * 0.85f, 1f), align * barW), 1f);
                float cr = Mathf.Max(rootR + UnityEngine.Random.Range(0.25f, 0.8f) * DiskRadius, BulgeSize * 1.6f + DiskRadius * 0.06f);
                float baseAngle = Mathf.Log(cr / rootR) / pitch + offset;
                Vector3 dir = new Vector3(Mathf.Cos(baseAngle), 0f, Mathf.Sin(baseAngle));
                Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
                Vector3 pos = dir * cr + perp * (UnityEngine.Random.Range(-1f, 1f) * ArmWidth);
                pos.y = UnityEngine.Random.Range(-1f, 1f) * DiskThickness * DiskRadius;

                var go = new GameObject($"VolNebula_{i}");
                go.SetActive(false);                 // configure before OnEnable builds it
                go.transform.SetParent(transform, false);
                go.transform.position = pos * PositionScale;
                var vn = go.AddComponent<VolumetricNebula>();
                ConfigureNebula(vn, VolumetricNebulaSize, tinted: true);
                go.SetActive(true);
                _volNebulae.Add(go);
            }
        }

        // One isolated hero nebula at the origin (press N) — a bigger, higher-quality volume with off-centre
        // lighting so its shape/structure can be judged without hunting for it in the galaxy.
        void SpawnSingleNebulaPreview()
        {
            ClearVolumetric();
            if (Shader.Find("Clay/VolumetricNebula") == null) return;

            var go = new GameObject("VolNebula_Preview");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.position = Vector3.zero;
            var vn = go.AddComponent<VolumetricNebula>();
            ConfigureNebula(vn, VolumetricNebulaSize * PreviewNebulaSizeMul, tinted: false);
            vn.Steps = math.max(vn.Steps, 110);            // extra-crisp for close inspection
            go.SetActive(true);
            _volNebulae.Add(go);
        }

        // Assigns a random CLASS (planetary / big / fragment) and per-nebula shape, size, structure and colour so
        // every nebula is wildly different. `sizeBase` is the reference size; each class scales it to its own range.
        void ConfigureNebula(VolumetricNebula vn, float sizeBase, bool tinted)
        {
            float cr = UnityEngine.Random.value;
            int cls = cr < 0.28f ? 0 : (cr < 0.63f ? 1 : 2);   // planetary 28% / big 35% / fragment 37%
            vn.NebulaClass = cls;

            if (cls == 0)          // PLANETARY — small, bipolar shells, faint
            {
                vn.Size = sizeBase * UnityEngine.Random.Range(0.28f, 0.55f);
                vn.Shape = new Vector3(1f, UnityEngine.Random.Range(1.1f, 2.0f), UnityEngine.Random.Range(0.8f, 1.2f));
                vn.Frequency = UnityEngine.Random.Range(6f, 10f);
                vn.Warp = UnityEngine.Random.Range(0.15f, 0.4f);
                vn.Density = UnityEngine.Random.Range(4f, 6f);
                vn.Threshold = UnityEngine.Random.Range(0.12f, 0.25f);
                vn.Absorption = UnityEngine.Random.Range(2f, 4f);
                vn.EmissionStrength = UnityEngine.Random.Range(2.5f, 4f);
                vn.Steps = Mathf.Max(VolumetricSteps, 96);
            }
            else if (cls == 1)     // BIG — large, sculpted, dusty (Eagle / Carina)
            {
                vn.Size = sizeBase * UnityEngine.Random.Range(1.3f, 2.4f);
                vn.Shape = new Vector3(UnityEngine.Random.Range(0.7f, 1.3f), UnityEngine.Random.Range(0.55f, 1.0f), UnityEngine.Random.Range(0.7f, 1.3f));
                vn.Frequency = UnityEngine.Random.Range(3.5f, 6.5f);
                vn.Warp = UnityEngine.Random.Range(0.85f, 1.4f);
                vn.Density = UnityEngine.Random.Range(4f, 6.5f);
                vn.Threshold = UnityEngine.Random.Range(0.24f, 0.36f);
                vn.Absorption = UnityEngine.Random.Range(4f, 7f);
                vn.EmissionStrength = UnityEngine.Random.Range(2.5f, 4f);
                vn.Steps = Mathf.Max(VolumetricSteps, 96);
            }
            else                   // FRAGMENT — medium, wildly irregular, broken floaty bits
            {
                vn.Size = sizeBase * UnityEngine.Random.Range(0.5f, 1.2f);
                vn.Shape = new Vector3(UnityEngine.Random.Range(0.4f, 1.6f), UnityEngine.Random.Range(0.4f, 1.4f), UnityEngine.Random.Range(0.4f, 1.6f));
                vn.Frequency = UnityEngine.Random.Range(5f, 9f);
                vn.Warp = UnityEngine.Random.Range(0.9f, 1.5f);
                vn.Density = UnityEngine.Random.Range(3.5f, 5.5f);
                vn.Threshold = UnityEngine.Random.Range(0.3f, 0.44f);
                vn.Absorption = UnityEngine.Random.Range(4f, 7f);
                vn.EmissionStrength = UnityEngine.Random.Range(2.5f, 4.5f);
                vn.Steps = Mathf.Max(VolumetricSteps, 84);
            }

            vn.Anisotropy = UnityEngine.Random.Range(0.2f, 0.6f);
            vn.ScatterStrength = UnityEngine.Random.Range(1.0f, 2.0f);
            vn.Seed = UnityEngine.Random.Range(1, 99999);

            float hue = UnityEngine.Random.value;
            Color outer, core, second;
            if (hue < 0.5f)      { outer = new Color(1.0f, 0.30f, 0.55f); core = new Color(0.20f, 0.95f, 0.85f); second = new Color(0.55f, 0.35f, 0.95f); }  // pink + teal + violet
            else if (hue < 0.8f) { outer = new Color(0.95f, 0.25f, 0.6f); core = new Color(1.0f, 0.8f, 0.4f);  second = new Color(0.3f, 0.5f, 1.0f); }      // magenta + gold + blue
            else                 { outer = new Color(1.0f, 0.4f, 0.3f);   core = new Color(0.35f, 0.9f, 1.0f); second = new Color(1.0f, 0.65f, 0.25f); }     // red + cyan + orange
            bool multiColor = UnityEngine.Random.value < 0.5f;
            float bri = UnityEngine.Random.Range(0.5f, 1.15f);
            Color tint = tinted ? GalaxyTint : Color.white;
            vn.Emission = outer * tint * bri;
            vn.Emission2 = core * tint * bri;
            vn.Emission3 = (multiColor ? second : outer) * tint * bri;
            vn.LightColor = new Color(0.95f, 0.82f, 0.65f) * tint;

            Vector3 offDir = UnityEngine.Random.onUnitSphere;
            offDir.y *= 0.5f;
            vn.StarOffset = offDir.normalized * vn.Size * UnityEngine.Random.Range(0.28f, 0.5f);
        }

        // Put the camera at a good framing distance from the origin nebula (inside the distance-fade range).
        void FrameNebulaPreview()
        {
            var cam = Camera.main;
            if (cam == null) return;
            // Frame on the ACTUAL spawned nebula (size now varies by class), using its largest stretched axis.
            float size = VolumetricNebulaSize * PreviewNebulaSizeMul;
            if (_volNebulae.Count > 0 && _volNebulae[_volNebulae.Count - 1] != null)
            {
                var pv = _volNebulae[_volNebulae.Count - 1].GetComponent<VolumetricNebula>();
                if (pv != null) size = pv.Size * Mathf.Max(pv.Shape.x, Mathf.Max(pv.Shape.y, pv.Shape.z));
            }
            var ctrl = cam.GetComponent<GalaxyCameraController>();
            Vector3 p = new Vector3(0f, size * 0.35f, -size * 1.9f);
            cam.transform.position = p;
            if (ctrl != null)
            {
                ctrl.yaw = 0f;
                ctrl.pitch = 10f;
                ctrl.speed = Mathf.Max(size * 0.5f, 1f);
            }
            cam.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
        }

        void ClearVolumetric()
        {
            for (int i = 0; i < _volNebulae.Count; i++)
                if (_volNebulae[i] != null) Destroy(_volNebulae[i]);
            _volNebulae.Clear();
        }

        // hue < 0 → blue reflection nebula; 0..1 → emission (H-alpha magenta → O III teal core).
        float4 NebulaColor(float hue, float bright)
        {
            // MUTED WARM palette — varied hue (rose ↔ salmon ↔ dusty orange ↔ ruddy magenta) but low saturation,
            // so nebulae read as soft nebulous haze, not saturated dots. Reflection nebulae are a cool-grey accent.
            float3 c;
            if (hue < 0f)
            {
                c = new float3(0.42f, 0.44f, 0.52f);                     // muted lavender-grey reflection
            }
            else
            {
                float3 rose   = new float3(0.72f, 0.40f, 0.44f);
                float3 salmon = new float3(0.78f, 0.46f, 0.38f);
                float3 orange = new float3(0.70f, 0.48f, 0.34f);
                float3 ruddy  = new float3(0.68f, 0.36f, 0.42f);
                c = math.lerp(rose, salmon, math.saturate(hue * 1.6f));
                c = math.lerp(c, orange, math.saturate((hue - 0.4f) * 1.8f));
                c = math.lerp(c, ruddy, math.saturate((hue - 0.75f) * 3f));
                // desaturate toward luminance so it's muted, not candy-coloured
                float lum = math.dot(c, new float3(0.3f, 0.5f, 0.2f));
                c = math.lerp(new float3(lum), c, 0.65f);
            }
            c *= new float3(GalaxyTint.r, GalaxyTint.g, GalaxyTint.b);   // per-galaxy colour character
            return new float4(c * bright * NebulaBrightness, 1f);
        }

        // Interstellar dust: a fBm-carved density field (GenerateDustJob) drives per-particle opacity. Alpha-blended
        // reddening billboards drawn after the stars, so dense lanes cut dark, ruddy bands through the arms.
        void GenerateDust()
        {
            if (DustParticleCount <= 0 || _dustMat == null) return;

            var dust = new NativeArray<GalaxyStar>(DustParticleCount, Allocator.TempJob);
            var job = new GenerateDustJob
            {
                Params = BuildParams(),
                Output = dust,
                CloudScale = DustCloudScale,
                Contrast = DustContrast,
                ShadowStrength = DustShadowStrength,
                Warp = DustWarp,
                Coverage = DustCoverage,
                Clumpiness = DustClumpiness,
                Swirl = DustSwirl,
                BigNoise = DustBigNoise,
                FineNoise = DustFineNoise,
                FineWeight = DustFineWeight,
                ArmInfluence = DustArmInfluence,
                Webbing = DustWebbing,
                WebAmount = DustWebAmount,
                WebScale = DustWebScale,
            };
            Unity.Jobs.IJobParallelForExtensions.Schedule(job, DustParticleCount, 256).Complete();

            var desc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: false);
            var rma = new RenderMeshArray(new[] { _dustMat }, new[] { _mesh });
            var proto = _em.CreateEntity();
            RenderMeshUtility.AddComponents(proto, _em, in desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            if (!_em.HasComponent<LocalToWorld>(proto)) _em.AddComponentData(proto, new LocalToWorld { Value = float4x4.identity });
            _em.AddComponentData(proto, new StarColor { Value = new float4(1f, 1f, 1f, 1f) });

            // Dust palette. Extinction is reddish-brown, but real dust carries hue variety — rusty warm knots,
            // ochre, neutral browns, cold blue-grey in the outskirts. Mixed by radius + two per-particle hashes so
            // no two patches are the same tone. DustColor is a global tint the user can shift the whole palette by.
            float3 rust  = new float3(0.17f, 0.06f, 0.03f);
            float3 ochre = new float3(0.16f, 0.11f, 0.05f);
            float3 brown = new float3(0.10f, 0.07f, 0.055f);
            float3 slate = new float3(0.04f, 0.05f, 0.075f);
            float3 gTint = new float3(DustColor.r, DustColor.g, DustColor.b) / 0.06f;   // normalised around default

            _dust = new NativeArray<Entity>(DustParticleCount, Allocator.Persistent);
            _em.Instantiate(proto, _dust);
            for (int i = 0; i < DustParticleCount; i++)
            {
                GalaxyStar s = dust[i];
                float density = s.Luminosity;
                float rand = s.Age;                                       // per-particle variety
                float light = s.Temperature;                              // illumination × self-shadow (0..1)
                float rr = math.length(new float2(s.Position.x, s.Position.z));
                float rn = math.saturate(rr / math.max(DiskRadius, 1e-3f));

                float h1 = math.frac(rand * 7.13f);
                float h2 = math.frac(rand * 3.71f + 0.37f);
                float3 warmMix = math.lerp(rust, ochre, h1);              // inner: rust ↔ ochre
                float3 coldMix = math.lerp(brown, slate, h1);            // outer: brown ↔ slate
                float3 tint = math.lerp(warmMix, coldMix, math.saturate(rn * 0.9f));
                tint *= math.lerp(0.6f, 1.4f, h2) * gTint;                // brightness jitter + global tint
                tint *= math.lerp(0.2f, 1.9f, light);                     // LIT: bright core-facing, dark in shadow

                // Multi-scale form: a few big faint haze clouds (rand→1), many small dense knots (rand→0).
                float big = rand * rand;
                float size = DustSize * math.lerp(0.6f, 2.0f, big);   // capped so clouds top out near 2× DustSize
                float alpha = density * DustOpacity * math.lerp(1.1f, 0.4f, big);   // may exceed 1 (frag wisp/edge cuts it back)
                if (alpha <= 0.01f) size = 0f;   // cull near-invisible particles to a zero-size quad

                _em.SetComponentData(_dust[i], new LocalToWorld
                {
                    Value = float4x4.TRS(s.Position * PositionScale, quaternion.identity, new float3(size))
                });
                _em.SetComponentData(_dust[i], new StarColor { Value = new float4(tint, alpha) });
            }
            _em.DestroyEntity(proto);
            dust.Dispose();
        }

        void ClearEntities()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            bool live = world != null && world.IsCreated;
            if (_entities.IsCreated)
            {
                if (live) world.EntityManager.DestroyEntity(_entities);
                _entities.Dispose();
            }
            if (_dust.IsCreated)
            {
                if (live) world.EntityManager.DestroyEntity(_dust);
                _dust.Dispose();
            }
            if (_starDepth.IsCreated)
            {
                if (live) world.EntityManager.DestroyEntity(_starDepth);
                _starDepth.Dispose();
            }
            if (_nebula.IsCreated)
            {
                if (live) world.EntityManager.DestroyEntity(_nebula);
                _nebula.Dispose();
            }
            if (_absorption.IsCreated)
            {
                if (live) world.EntityManager.DestroyEntity(_absorption);
                _absorption.Dispose();
            }
            if (_testStar != Entity.Null)
            {
                if (live && world.EntityManager.Exists(_testStar)) world.EntityManager.DestroyEntity(_testStar);
                _testStar = Entity.Null;
            }
            ClearVolumetric();
        }

        void OnDestroy()
        {
            ClearEntities();
            if (_postFx != null) Destroy(_postFx);
        }

        // Planckian-locus approximation (Tanner Helland), RGB in 0..1, scaled by a brightness factor.
        static float4 BlackbodyColor(float tempK, float brightness)
        {
            float temp = math.clamp(tempK, 1000f, 40000f) / 100f;
            float r, g, b;
            if (temp <= 66f) r = 255f;
            else r = 329.698727446f * math.pow(math.max(temp - 60f, 1e-3f), -0.1332047592f);
            if (temp <= 66f) g = 99.4708025861f * math.log(math.max(temp, 1e-3f)) - 161.1195681661f;
            else g = 288.1221695283f * math.pow(math.max(temp - 60f, 1e-3f), -0.0755148492f);
            if (temp >= 66f) b = 255f;
            else if (temp <= 19f) b = 0f;
            else b = 138.5177312231f * math.log(math.max(temp - 10f, 1e-3f)) - 305.0447927307f;
            float3 c = math.saturate(new float3(r, g, b) / 255f);
            // Physical blackbody colours are near-white for most temperatures and wash out under additive
            // blending, so push saturation up to make the red↔blue temperature spread actually read.
            float lum = math.dot(c, new float3(0.299f, 0.587f, 0.114f));
            c = math.saturate(math.lerp(new float3(lum), c, 1.35f));
            return new float4(c * brightness, 1f);
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "StarQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),  new Vector3(0.5f, 0.5f, 0f)
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }
    }
}
