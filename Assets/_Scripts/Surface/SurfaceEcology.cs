using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using CLAY.Galaxy;
using CLAY.Flora;

namespace CLAY.Surface
{
    public enum Role { Canopy, Understory, Ground, Accent }

    /// <summary>
    /// A planet's plant life.
    ///
    /// CATALOGUE — built once per planet, deterministically:
    ///  1. Survey the climate: sample the whole planet (plus densely around the landing site) with the same
    ///     height/moisture fields and climate model the ground uses, and average each biome's conditions.
    ///  2. Lineages: a few ancestral genomes per planet. Every species descends from one of them, inheriting its
    ///     pigments, surface textures, paint job, bark and organ style — so related forms recur across biomes
    ///     and the planet's flora reads as one evolutionary tree, not a random pile.
    ///  3. For each biome, fill ecological roles (canopy / understory / ground cover / oddity) from biome-appropriate
    ///     body plans, re-rolling a lineage descendant until it is VIABLE there (PlantBiology). Species that already
    ///     fit a biome are reused there first, so ranges overlap naturally. Plants the player saved in the Flora
    ///     Lab join the catalogue wherever they're viable.
    ///
    /// PLACEMENT — per 32 m cell on worker threads: role densities from the local biome; each candidate spot is
    /// checked against the EXACT local climate (altitude, moisture), and species are chosen with patchy noise so
    /// they grow in stands instead of a uniform salt-and-pepper mix.
    ///
    /// RENDERING — GPU-instanced (Graphics.RenderMeshInstanced) with 3 levels of detail built lazily per species.
    /// </summary>
    public sealed class SurfaceEcology
    {
        static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        bool hardyWorld;
        public static bool DebugPlainMaterial; static Material plainMat;   // vegetated from orbit but the strict model found no fit → hardy fallback flora
        public sealed class Species
        {
            public bool hardy;   // fallback species: grows wherever the orbital map shows life
            public string name;
            public PlantGenome g;
            public PlantSurvivability s;
            public Role role;
            public int lineage;
            public readonly HashSet<Biome> biomes = new();
            public float patchScale, patchSeed, usable;
            public float rangeScale, rangeSeed, rangeCut;   // geographic range: each species lives in its own regions
            public float xeric, hydric, shadeTol;
            public float crownR = -1f, crownLo, crownHi;    // crown footprint radius + height band (unit scale), from the built foliage            // niche: dry-site specialist, water-lover, shade tolerance (0..1)
            public Material wood, leaf, flower;
            public readonly Mesh[,] mesh = new Mesh[3, 3];      // [lod, part]
            public readonly bool[] built = new bool[3], requested = new bool[3];
        }

        struct Inst { public int sp; public float x, y, z, rot, scale, sy, sxz, tiltX, tiltZ; public bool dead; }
        sealed class Cell { public long cx, cz; public Task<List<Inst>> task; public List<Inst> inst; }

        const double CellSize = 32;
        static readonly float[] LodDetail = { 1f, 0.42f, 0.15f };
        static readonly float[] LodDist = { 40f, 140f };
        static readonly float[] RoleDist = { 420f, 140f, 45f, 220f };

        readonly SurfaceGeo geo;
        readonly PlanetData planet;
        readonly ulong seed;
        readonly int layer;
        public readonly List<Species> species = new();
        public readonly Dictionary<Biome, BiomeStats> biomeStats = new();

        readonly Dictionary<long, Cell> cells = new();
        readonly Queue<(Species sp, int lod)> buildQueue = new();
        readonly List<Matrix4x4>[,] batches;
        readonly Matrix4x4[] buf = new Matrix4x4[1023];
        int runningCells;
        bool allowScatter;
        const int MaxInstancesPerFrame = 7000;

        public struct BiomeStats { public int n; public float tMin, tMax, tMean, precip, wind; }

        public SurfaceEcology(SurfaceGeo g, PlanetData p, ulong planetSeed, ulong systemSeed, int layer)
        {
            geo = g; planet = p; seed = planetSeed; this.layer = layer;
            Survey();
            BuildCatalogue(systemSeed);
            batches = new List<Matrix4x4>[species.Count, 3];
            for (int i = 0; i < species.Count; i++) for (int l = 0; l < 3; l++) batches[i, l] = new List<Matrix4x4>(256);
        }

        // ── 1. climate survey ─────────────────────────────────────────────────────────────────────────────
        void Survey()
        {
            var acc = new Dictionary<Biome, (int n, double tMin, double tMax, double tMean, double p, double w)>();
            void Add(in LocalClimate lc)
            {
                acc.TryGetValue(lc.biome, out var a);
                acc[lc.biome] = (a.n + 1, a.tMin + lc.tMinC, a.tMax + lc.tMaxC, a.tMean + lc.tMeanC, a.p + lc.precipMm, a.w + lc.windLoad);
            }
            // planet-wide (Fibonacci sphere), orbital-scale heights
            int N = 1600;
            for (int i = 0; i < N; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / N, rr = Mathf.Sqrt(1f - y * y), phi = i * 2.39996323f;
                Vector3 d = new Vector3(Mathf.Cos(phi) * rr, y, Mathf.Sin(phi) * rr);
                float h01 = geo.s.Height01(d);
                float hm = (h01 - geo.s.SeaLevel) * geo.heightScale;
                bool under = geo.hasSea && hm < 0f;
                Add(geo.climate.At(d, Mathf.Max(0f, hm), geo.s.Moisture(d), under, false));
            }
            // dense survey around the landing site (±150 km) so local biomes are always represented
            var r = new DetRng(DetRng.Hash(seed, 0x5A11UL));
            for (int i = 0; i < 500; i++)
            {
                var sm = geo.At(r.Range(-150000f, 150000f), r.Range(-150000f, 150000f));
                Add(geo.ClimateAt(sm));
            }
            foreach (var kv in acc)
            {
                var a = kv.Value; if (a.n == 0) continue;
                biomeStats[kv.Key] = new BiomeStats
                {
                    n = a.n, tMin = (float)(a.tMin / a.n), tMax = (float)(a.tMax / a.n), tMean = (float)(a.tMean / a.n),
                    precip = (float)(a.p / a.n), wind = (float)(a.w / a.n),
                };
            }
        }

        // ── 2–3. lineages → species per biome role ────────────────────────────────────────────────────────
        void BuildCatalogue(ulong systemSeed)
        {
            var r = new DetRng(DetRng.Hash(seed, 0xF10AUL));
            int nLin = 3 + r.RangeInt(0, 3);
            var lineages = new List<PlantGenome>();
            for (int i = 0; i < nLin; i++) lineages.Add(PlantGenome.Random(planet, (int)(r.Value * int.MaxValue)));

            var biomes = new List<Biome>(biomeStats.Keys);
            biomes.RemoveAll(b => b == Biome.Ocean || b == Biome.Ice || b == Biome.Barren || biomeStats[b].n < 4);
            biomes.Sort((a, b) => biomeStats[b].n.CompareTo(biomeStats[a].n));

            foreach (var b in biomes)
            {
                var st = biomeStats[b];
                foreach (var (role, count) in RolesFor(b))
                    for (int k = 0; k < count && species.Count < 32; k++)
                    {
                        // continuity first: an existing species of this role that is already viable here spreads in
                        Species reuse = null;
                        if (r.Value < 0.55f)
                            foreach (var sp in species)
                                if (sp.role == role && !sp.biomes.Contains(b) && Fits(sp.s, sp.usable, st)) { reuse = sp; break; }
                        if (reuse != null) { reuse.biomes.Add(b); continue; }

                        var pool = Pool(b, role);
                        for (int attempt = 0; attempt < 45; attempt++)
                        {
                            int lin = r.RangeInt(0, lineages.Count);
                            var arch = pool[r.RangeInt(0, pool.Length)];
                            var g = PlantGenome.Random(planet, (int)(r.Value * int.MaxValue), arch);
                            Inherit(g, lineages[lin], ref r, role);
                            RoleSize(g, role, b, ref r);
                            var s = PlantBiology.Derive(g);
                            if (!PlantBiology.PlanetWideOK(s, planet, geo.climate, out _)) continue;
                            float usable = PlantBiology.UsableLight(s, planet, geo.climate);
                            if (!Fits(s, usable, st)) continue;
                            var nsp = NewSpecies(g, s, role, lin, usable, ref r);
                            nsp.biomes.Add(b);
                            // it may also thrive in other surveyed biomes
                            foreach (var ob in biomes) if (ob != b && Fits(s, usable, biomeStats[ob])) nsp.biomes.Add(ob);
                            break;
                        }
                    }
            }

            // A world painted green from orbit HAS a biosphere, even where our strict survival model finds no fit —
            // the orbital view is the authority. Seed hardy descendants of the lineages across every land biome.
            if (species.Count == 0 && geo.s.Palette.vegAmt > 0.03f)
            {
                var land = new List<Biome>(biomeStats.Keys);
                land.RemoveAll(b => b == Biome.Ocean);
                if (land.Count == 0) land.Add(Biome.Grassland);
                foreach (var (role, count) in RolesFor(Biome.Grassland))
                    for (int k = 0; k < Mathf.Max(1, count) && species.Count < 12; k++)
                    {
                        var pool = Pool(Biome.Grassland, role);
                        int lin = r.RangeInt(0, lineages.Count);
                        var g = PlantGenome.Random(planet, (int)(r.Value * int.MaxValue), pool[r.RangeInt(0, pool.Length)]);
                        Inherit(g, lineages[lin], ref r, role);
                        RoleSize(g, role, Biome.Grassland, ref r);
                        var s = PlantBiology.Derive(g);
                        var nsp = NewSpecies(g, s, role, lin, Mathf.Max(0.3f, PlantBiology.UsableLight(s, planet, geo.climate)), ref r);
                        nsp.biomes.UnionWith(land); nsp.hardy = true; hardyWorld = true;
                    }
                Debug.Log($"[Surface] vegetated-from-orbit fallback: {species.Count} species (strict model found none)");
            }

            // the player's own Flora Lab creations join wherever they're viable
            foreach (var g in HabitatRegistry.Get(systemSeed, planet.index))
            {
                var s = PlantBiology.Derive(g);
                if (!PlantBiology.PlanetWideOK(s, planet, geo.climate, out _)) continue;
                float usable = PlantBiology.UsableLight(s, planet, geo.climate);
                var sp = NewSpecies(g, s, RoleOf(g), -1, usable, ref r);
                foreach (var b in biomes) if (Fits(s, usable, biomeStats[b])) sp.biomes.Add(b);
                if (sp.biomes.Count == 0) species.Remove(sp);
            }
        }

        // A species' niche from its body plan and biology: succulent / stone forms are dry-site specialists; algae,
        // ferns and ribbons are water-lovers; low shade-adapted forms fill understoreys, while sun-loving xerics don't.
        static void Niche(Species sp)
        {
            var a = sp.g.archetype;
            bool succulent = a == PlantArchetype.Cactus || a == PlantArchetype.SucculentRosette || a == PlantArchetype.Globe
                          || a == PlantArchetype.LivingStone || a == PlantArchetype.Crystal;
            bool wetForm = a == PlantArchetype.MatAlgae || a == PlantArchetype.Fern || a == PlantArchetype.Ribbon || a == PlantArchetype.Tube;
            bool shadeForm = a == PlantArchetype.Fern || a == PlantArchetype.Mushroom || a == PlantArchetype.MatAlgae
                          || a == PlantArchetype.FloweringHerb || a == PlantArchetype.Vine || a == PlantArchetype.Tendril;
            float dryBio = Mathf.Clamp01(1f - sp.s.precipMinMm / 600f);            // needs little rain
            float wetBio = Mathf.Clamp01((sp.s.precipMinMm - 400f) / 900f);        // needs a lot of rain
            sp.xeric = Mathf.Clamp01((succulent ? 0.75f : 0f) + dryBio * 0.35f);
            sp.hydric = Mathf.Clamp01((wetForm ? 0.7f : 0f) + wetBio * 0.5f);
            sp.shadeTol = Mathf.Clamp01((shadeForm ? 0.8f : 0.25f) + (sp.role == Role.Understory || sp.role == Role.Ground ? 0.2f : 0f)
                                        - (succulent ? 0.3f : 0f));
        }

        // How well plants root in each terrain type (general, and for dry-site specialists).
        static float Fertility(SurfaceGeo.TerrainType t, float xeric)
        {
            switch (t)
            {
                case SurfaceGeo.TerrainType.FineSoil: case SurfaceGeo.TerrainType.Grassy: case SurfaceGeo.TerrainType.Mossy:
                    return Mathf.Lerp(1f, 0.55f, xeric);
                case SurfaceGeo.TerrainType.Sandy: case SurfaceGeo.TerrainType.Dunes:
                    return Mathf.Lerp(0.25f, 1f, xeric);
                case SurfaceGeo.TerrainType.Gravel: case SurfaceGeo.TerrainType.PatternedGround:
                    return Mathf.Lerp(0.45f, 0.95f, xeric);
                case SurfaceGeo.TerrainType.Rocky: case SurfaceGeo.TerrainType.Scree: case SurfaceGeo.TerrainType.Bouldery:
                case SurfaceGeo.TerrainType.BasaltFlow:
                    return Mathf.Lerp(0.3f, 0.7f, xeric);                            // crack-dwellers only
                case SurfaceGeo.TerrainType.DriedLake: case SurfaceGeo.TerrainType.Cracked:
                    return Mathf.Lerp(0.2f, 0.6f, xeric);
                case SurfaceGeo.TerrainType.SaltFlat: case SurfaceGeo.TerrainType.Sulfur:
                    return Mathf.Lerp(0.02f, 0.25f, xeric);                          // only halophyte/extremophile hangers-on
                default:                                                             // ice, snow, frost, lava, metal, regolith
                    return 0.02f;
            }
        }

        Species NewSpecies(PlantGenome g, PlantSurvivability s, Role role, int lin, float usable, ref DetRng r)
        {
            var sp = new Species
            {
                g = g, s = s, role = role, lineage = lin, usable = usable,
                patchScale = r.Range(40f, 260f), patchSeed = r.Range(0f, 1000f),
                rangeScale = r.Range(15000f, 70000f), rangeSeed = r.Range(0f, 1000f), rangeCut = r.Range(0.4f, 0.55f),
                name = SpeciesName(ref r, lin),
                wood = PlantMaterials.Make(false), leaf = PlantMaterials.Make(true), flower = PlantMaterials.Make(true),
            };
            PlantMaterials.Apply(g, sp.wood, sp.leaf, sp.flower);
            Niche(sp);
            species.Add(sp);
            return sp;
        }

        static bool Fits(in PlantSurvivability s, float usable, in BiomeStats b)
            => PlantBiology.FitsLocal(s, b.tMin, b.tMax, b.precip, b.wind, usable, out _);

        static Role RoleOf(PlantGenome g) => g.archetype switch
        {
            PlantArchetype.Tree => Role.Canopy,
            PlantArchetype.GrassClump or PlantArchetype.MatAlgae or PlantArchetype.LivingStone or PlantArchetype.Mushroom => Role.Ground,
            _ => g.heightM > 4f ? Role.Canopy : Role.Understory,
        };

        // What each biome's plant community is made of: (role, how many species fill it).
        static IEnumerable<(Role, int)> RolesFor(Biome b) => b switch
        {
            Biome.TropicalRainforest or Biome.TemperateRainforest => new[] { (Role.Canopy, 2), (Role.Understory, 2), (Role.Ground, 2), (Role.Accent, 1) },
            Biome.TemperateForest or Biome.TropicalSeasonal or Biome.Boreal => new[] { (Role.Canopy, 2), (Role.Understory, 2), (Role.Ground, 1), (Role.Accent, 1) },
            Biome.Wetland => new[] { (Role.Understory, 2), (Role.Ground, 2), (Role.Accent, 1) },
            Biome.Grassland or Biome.Savanna => new[] { (Role.Ground, 2), (Role.Understory, 1), (Role.Canopy, 1), (Role.Accent, 1) },
            Biome.Desert => new[] { (Role.Canopy, 1), (Role.Understory, 2), (Role.Ground, 1), (Role.Accent, 1) },
            Biome.ColdDesert or Biome.Tundra => new[] { (Role.Ground, 2), (Role.Understory, 1), (Role.Accent, 1) },
            _ => new[] { (Role.Ground, 1) },
        };

        static PlantArchetype[] Pool(Biome b, Role role)
        {
            bool desert = b == Biome.Desert, cold = b == Biome.Tundra || b == Biome.ColdDesert, open = b == Biome.Grassland || b == Biome.Savanna;
            switch (role)
            {
                case Role.Canopy:
                    return desert ? new[] { PlantArchetype.Cactus, PlantArchetype.Spire, PlantArchetype.Umbrella }
                         : open ? new[] { PlantArchetype.Tree, PlantArchetype.Umbrella, PlantArchetype.Spire }
                         : new[] { PlantArchetype.Tree, PlantArchetype.Tree, PlantArchetype.Umbrella };
                case Role.Understory:
                    return desert ? new[] { PlantArchetype.SucculentRosette, PlantArchetype.Globe, PlantArchetype.Crystal, PlantArchetype.Shrub }
                         : cold ? new[] { PlantArchetype.Segmented, PlantArchetype.Shrub, PlantArchetype.Spire }
                         : new[] { PlantArchetype.Shrub, PlantArchetype.Fern, PlantArchetype.FloweringHerb, PlantArchetype.OrbCluster, PlantArchetype.Tendril, PlantArchetype.Segmented };
                case Role.Ground:
                    return desert ? new[] { PlantArchetype.LivingStone, PlantArchetype.GrassClump, PlantArchetype.MatAlgae }
                         : cold ? new[] { PlantArchetype.MatAlgae, PlantArchetype.GrassClump, PlantArchetype.LivingStone, PlantArchetype.Mushroom }
                         : new[] { PlantArchetype.GrassClump, PlantArchetype.MatAlgae, PlantArchetype.Mushroom, PlantArchetype.FloweringHerb };
                default:
                    return new[] { PlantArchetype.Tube, PlantArchetype.Ribbon, PlantArchetype.Crystal, PlantArchetype.OrbCluster,
                                   PlantArchetype.Umbrella, PlantArchetype.Globe, PlantArchetype.Spire, PlantArchetype.Tendril };
            }
        }

        // Lineage traits that descendants carry (with small drift): colour scheme, textures, patterning, bark,
        // organ style, glow — the family resemblance.
        static void Inherit(PlantGenome g, PlantGenome L, ref DetRng r, Role role)
        {
            g.pigment = PlantColor.Vary(L.pigment, r.Range(-0.035f, 0.035f), r.Range(0.85f, 1.15f), r.Range(0.85f, 1.15f));
            g.accent = PlantColor.Vary(L.accent, r.Range(-0.05f, 0.05f), r.Range(0.85f, 1.15f), r.Range(0.85f, 1.15f));
            g.underside = PlantColor.Vary(L.underside, r.Range(-0.03f, 0.03f), 1f, r.Range(0.9f, 1.1f));
            g.flowerColor = PlantColor.Vary(L.flowerColor, r.Range(-0.06f, 0.06f), r.Range(0.9f, 1.1f), 1f);
            g.woodColor = PlantColor.Vary(L.woodColor, r.Range(-0.03f, 0.03f), 1f, r.Range(0.85f, 1.15f));
            if (r.Value < 0.7f) g.leafStyle = L.leafStyle;
            if (r.Value < 0.7f) g.organTexture = L.organTexture;
            if (r.Value < 0.6f) g.paint = L.paint;
            if (r.Value < 0.7f) g.barkType = L.barkType;
            if (r.Value < 0.7f) { g.organShape = L.organShape; g.organColor = L.organColor; }
            g.glow = L.glow * r.Range(0.6f, 1.2f);
            g.petalCount = L.petalCount;
            if (role != Role.Ground && g.leaf != LeafShape.None && L.leaf != LeafShape.None && L.leaf != LeafShape.Membrane && r.Value < 0.45f)
                g.leaf = L.leaf;
            if (r.Value < 0.4f) g.terminalOrgan = L.terminalOrgan;
        }

        static void RoleSize(PlantGenome g, Role role, Biome b, ref DetRng r)
        {
            bool forest = b == Biome.TropicalRainforest || b == Biome.TemperateRainforest || b == Biome.TemperateForest
                       || b == Biome.TropicalSeasonal || b == Biome.Boreal;
            switch (role)
            {
                case Role.Canopy:     g.heightM = r.Range(forest ? 9f : 3f, forest ? 32f : 12f); break;
                case Role.Understory: g.heightM = r.Range(0.4f, 3f); break;
                case Role.Ground:     g.heightM = r.Range(0.06f, 0.6f); break;
                default:              g.heightM = r.Range(0.4f, 7f); break;
            }
        }

        static float Density(Biome b, Role role)   // instances per m²
        {
            switch (b)
            {
                case Biome.TropicalRainforest: return role switch { Role.Canopy => 1f / 70f, Role.Understory => 1f / 30f, Role.Ground => 1f / 10f, _ => 1f / 500f };
                case Biome.TemperateRainforest: return role switch { Role.Canopy => 1f / 80f, Role.Understory => 1f / 35f, Role.Ground => 1f / 10f, _ => 1f / 600f };
                case Biome.TemperateForest:
                case Biome.TropicalSeasonal: return role switch { Role.Canopy => 1f / 110f, Role.Understory => 1f / 45f, Role.Ground => 1f / 9f, _ => 1f / 700f };
                case Biome.Boreal: return role switch { Role.Canopy => 1f / 90f, Role.Understory => 1f / 80f, Role.Ground => 1f / 14f, _ => 1f / 900f };
                case Biome.Wetland: return role switch { Role.Canopy => 1f / 900f, Role.Understory => 1f / 25f, Role.Ground => 1f / 5f, _ => 1f / 400f };
                case Biome.Grassland: return role switch { Role.Canopy => 1f / 4000f, Role.Understory => 1f / 160f, Role.Ground => 1f / 4f, _ => 1f / 1500f };
                case Biome.Savanna: return role switch { Role.Canopy => 1f / 1400f, Role.Understory => 1f / 120f, Role.Ground => 1f / 5f, _ => 1f / 1200f };
                case Biome.Desert: return role switch { Role.Canopy => 1f / 900f, Role.Understory => 1f / 220f, Role.Ground => 1f / 90f, _ => 1f / 1500f };
                case Biome.ColdDesert: return role switch { Role.Canopy => 0f, Role.Understory => 1f / 400f, Role.Ground => 1f / 60f, _ => 1f / 3000f };
                case Biome.Tundra: return role switch { Role.Canopy => 0f, Role.Understory => 1f / 150f, Role.Ground => 1f / 8f, _ => 1f / 2000f };
                default: return 0f;
            }
        }

        // ── placement ─────────────────────────────────────────────────────────────────────────────────────
        public void Update(double camX, double camZ, bool terrainBusy)
        {
            allowScatter = !terrainBusy;
            // keep cells within the furthest role distance; build missing ones off-thread
            float reach = 0f; foreach (var d in RoleDist) reach = Mathf.Max(reach, d);
            long c0x = (long)System.Math.Floor(camX / CellSize), c0z = (long)System.Math.Floor(camZ / CellSize);
            int rc = Mathf.CeilToInt(reach / (float)CellSize) + 1;
            var keep = new HashSet<long>();
            var todo = new List<(long, long, double)>();
            for (long z = c0z - rc; z <= c0z + rc; z++)
                for (long x = c0x - rc; x <= c0x + rc; x++)
                {
                    double dx = (x + 0.5) * CellSize - camX, dz = (z + 0.5) * CellSize - camZ;
                    double dd = System.Math.Sqrt(dx * dx + dz * dz);
                    if (dd > reach + CellSize) continue;
                    long k = CellKey(x, z); keep.Add(k);
                    if (!cells.ContainsKey(k)) todo.Add((x, z, dd));
                }
            todo.Sort((a, b) => a.Item3.CompareTo(b.Item3));
            // Terrain has priority on the CPU: plants scatter on ONE worker, and only once nearby ground is loaded.
            int maxJobs = allowScatter ? 1 : 0;
            foreach (var (x, z, _) in todo)
            {
                if (runningCells >= maxJobs) break;
                var cell = new Cell { cx = x, cz = z };
                long cx = x, cz = z;
                cell.task = Task.Run(() => Scatter(cx, cz));
                cells[CellKey(x, z)] = cell; runningCells++;
            }
            var drop = new List<long>();
            foreach (var kv in cells)
            {
                var c = kv.Value;
                if (c.task != null && c.task.IsCompleted)
                {
                    runningCells--;
                    if (c.task.IsFaulted) Debug.LogException(c.task.Exception); else c.inst = c.task.Result;
                    c.task = null;
                }
                if (!keep.Contains(kv.Key) && c.task == null) drop.Add(kv.Key);
            }
            foreach (var k in drop) cells.Remove(k);

            // lazily build plant meshes (a small time budget per frame)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (buildQueue.Count > 0 && sw.ElapsedMilliseconds < 10)
            {
                var (sp, lod) = buildQueue.Dequeue();
                PlantBuilder.Detail = LodDetail[lod];
                var b = PlantBuilder.Build(sp.g);
                PlantBuilder.Detail = 1f;
                sp.mesh[lod, 0] = b.wood; sp.mesh[lod, 1] = b.foliage; sp.mesh[lod, 2] = b.flower;
                if (lod == 0 && b.foliage != null && b.foliage.vertexCount > 0)
                {
                    var fb = b.foliage.bounds;
                    sp.crownLo = fb.min.y; sp.crownHi = fb.max.y;
                    sp.crownR = Mathf.Max(Mathf.Max(Mathf.Abs(fb.min.x), fb.max.x), Mathf.Max(Mathf.Abs(fb.min.z), fb.max.z)) * 0.85f;
                }
                sp.built[lod] = true;
            }
        }

        static long CellKey(long x, long z) => ((x + (1L << 30)) << 32) | (z + (1L << 30));

        List<Inst> Scatter(long cx, long cz)
        {
            var list = new List<Inst>();
            if (species.Count == 0) return list;
            var r = new DetRng(DetRng.Hash(seed, (ulong)CellKey(cx, cz)));
            double ox = cx * CellSize, oz = cz * CellSize;
            var centre = geo.At(ox + CellSize * 0.5, oz + CellSize * 0.5);
            if (centre.underwater) return list;
            var lc0 = geo.ClimateAt(centre);
            float area = (float)(CellSize * CellSize);

            // SOIL: a coarse 3×3 grid of the ground's terrain-type weights over the cell (same palette as the shader)
            var pal = geo.Palette;
            var soilW = new float[9 * 8];
            for (int gj = 0; gj < 3; gj++)
                for (int gi = 0; gi < 3; gi++)
                {
                    var gs = geo.At(ox + CellSize * (gi * 0.5), oz + CellSize * (gj * 0.5));
                    if (gs.underwater) continue;
                    geo.Ground(gs, geo.ClimateAt(gs), 0.2f, geo.Orbital(gs, 0.2f), out Vector4 wa, out Vector4 wb);
                    for (int k = 0; k < 8; k++) soilW[(gj * 3 + gi) * 8 + k] = k < 4 ? wa[k] : wb[k - 4];
                }
            float SoilFert(float px, float pz, float xeric)
            {
                if (pal == null) return 1f;
                float fu = px / (float)CellSize * 2f, fv = pz / (float)CellSize * 2f;
                int i0 = Mathf.Min((int)fu, 1), j0 = Mathf.Min((int)fv, 1);
                float tu = fu - i0, tv = fv - j0, f = 0f, tot = 0f;
                for (int k = 0; k < 8; k++)
                {
                    if (pal[k] < 0) continue;
                    float w00 = soilW[(j0 * 3 + i0) * 8 + k], w10 = soilW[(j0 * 3 + i0 + 1) * 8 + k];
                    float w01 = soilW[((j0 + 1) * 3 + i0) * 8 + k], w11 = soilW[((j0 + 1) * 3 + i0 + 1) * 8 + k];
                    float wk = Mathf.Lerp(Mathf.Lerp(w00, w10, tu), Mathf.Lerp(w01, w11, tu), tv);
                    f += wk * Fertility((SurfaceGeo.TerrainType)pal[k], xeric); tot += wk;
                }
                return tot > 0.01f ? f / tot : 1f;
            }
            // COMPETITION: canopy placed so far in this cell (for shade + dominance)
            var canopyPts = new List<Vector3>(64);                      // x, z, species
            var canopyCount = new Dictionary<int, int>();
            float windA = (float)(DetRng.Hash(seed, 0x71D1UL) % 6283UL) / 1000f;
            Vector2 windDir = new Vector2(Mathf.Cos(windA), Mathf.Sin(windA));
            var cand = new List<int>(); var w = new List<float>();
            // CROWN SPACE: crowns already standing in this cell (x, z, radius, bottom, top) — plants compete for space,
            // they don't grow through each other: a newcomer whose crown would intersect one shrinks to fit or isn't placed
            var crowns = new List<(float x, float z, float r, float lo, float hi)>(64);
            for (int role = 0; role < 4; role++)
            {
                float dens = Density(lc0.biome, (Role)role);
                if (hardyWorld) dens = Mathf.Max(dens, Density(Biome.Grassland, (Role)role) * 0.6f);
                float expect = dens * area * (role == (int)Role.Ground ? 0.35f : 1f) * (role <= (int)Role.Understory ? 2.2f : 1.2f);   // gated below → denser stands, open ground between
                int n = Mathf.FloorToInt(expect) + (r.Value < expect - Mathf.Floor(expect) ? 1 : 0);
                for (int k = 0; k < n; k++)
                {
                    float px = r.Range(0f, (float)CellSize), pz = r.Range(0f, (float)CellSize);
                    double wx = ox + px, wz = oz + pz;
                    var sm = geo.At(wx, wz);
                    if (sm.underwater) continue;
                    var lc = geo.ClimateAt(sm);
                    // WHERE plants grow: water first (moisture, valleys, shores), then stands. Canopy trees form forest
                    // blobs; understorey grows mostly inside them; ground cover and accents are patchier and sparser.
                    float nearWater = geo.hasSea ? Mathf.Clamp01(1f - sm.heightM / 60f) : 0f;
                    float wetness = Mathf.Clamp01(sm.moist * 1.3f - 0.25f + sm.valley * 0.9f + nearWater * 0.6f);
                    float forest = SS(0.48f, 0.62f, PlanetTexture.SurfaceSampler.Noise(new Vector3((float)(wx / 3500.0) + 11f, 3.3f, (float)(wz / 3500.0) - 7f), 3)
                                               + (wetness - 0.5f) * 0.35f);
                    var rl = (Role)role;
                    float gate = rl == Role.Canopy ? forest * wetness
                               : rl == Role.Understory ? Mathf.Lerp(0.15f, 1f, forest) * wetness
                               : rl == Role.Ground ? Mathf.Lerp(0.3f, 1f, wetness)
                               : 0.35f + 0.4f * wetness;
                    if (r.Value > gate) continue;
                    float sx0 = geo.At(wx + 1.5, wz).heightM - sm.heightM, sz0 = geo.At(wx, wz + 1.5).heightM - sm.heightM;
                    float slope = Mathf.Sqrt(sx0 * sx0 + sz0 * sz0) / 1.5f;
                    if (role != (int)Role.Ground && slope > 0.9f) continue;          // trees don't stand on cliffs
                    // exposure: standing proud of the ground ~25 m around (ridges, knolls) → wind-blasted
                    float exposure = 0f;
                    if (role <= (int)Role.Understory)
                    {
                        float avg = (geo.At(wx + 25, wz).heightM + geo.At(wx - 25, wz).heightM + geo.At(wx, wz + 25).heightM + geo.At(wx, wz - 25).heightM) * 0.25f;
                        exposure = SS(1.5f, 10f, sm.heightM - avg) * Mathf.Clamp01(lc.windLoad * 0.5f + 0.3f);
                    }
                    // shade & crowding from canopy already standing nearby
                    float shade = 0f;
                    foreach (var cpt in canopyPts)
                    {
                        float ddx = cpt.x - px, ddz = cpt.y - pz, d2 = ddx * ddx + ddz * ddz;
                        if (d2 < 49f) shade += 1f - d2 / 49f;
                    }
                    shade = Mathf.Clamp01(shade * 0.6f);
                    cand.Clear(); w.Clear();
                    float total = 0f;
                    for (int i = 0; i < species.Count; i++)
                    {
                        var sp = species[i];
                        if ((int)sp.role != role || !sp.biomes.Contains(lc.biome)) continue;
                        if (!sp.hardy && !PlantBiology.FitsLocal(sp.s, lc.tMinC, lc.tMaxC, lc.precipMm, lc.windLoad, sp.usable, out _)) continue;
                        // patchy stands: each species has its own clumping field
                        // each species has a geographic RANGE (tens of km) — no species grows everywhere
                        float range = PlanetTexture.SurfaceSampler.Noise(new Vector3((float)(wx / sp.rangeScale) + sp.rangeSeed, sp.rangeSeed * 0.3f, (float)(wz / sp.rangeScale) - sp.rangeSeed), 3);
                        if (range < sp.rangeCut) continue;
                        float patch = PlanetTexture.SurfaceSampler.Noise(new Vector3((float)(wx / sp.patchScale) + sp.patchSeed, sp.patchSeed, (float)(wz / sp.patchScale)), 3);
                        patch *= SS(sp.rangeCut, sp.rangeCut + 0.12f, range);   // thinning toward the range edge
                        float wt = Mathf.Pow(Mathf.Clamp01(patch * 1.6f - 0.3f), 2f) + 0.02f;
                        // NICHE: soil, water, light
                        wt *= SoilFert(px, pz, sp.xeric);
                        wt *= Mathf.Lerp(1f, Mathf.Lerp(0.25f, 1.6f, 1f - wetness), sp.xeric);   // dry-site specialists
                        wt *= Mathf.Lerp(1f, Mathf.Lerp(0.2f, 1.8f, wetness), sp.hydric);        // water-lovers
                        wt *= Mathf.Lerp(1f, sp.shadeTol * 1.4f + 0.05f, shade);                 // under canopy: shade-tolerant only
                        // DOMINANCE: a canopy species already standing here spreads (monodominant stands)
                        if (rl == Role.Canopy && canopyCount.TryGetValue(i, out int already)) wt *= 1f + already * 0.8f;
                        cand.Add(i); w.Add(wt); total += wt;
                    }
                    if (cand.Count == 0) continue;
                    // the best-suited candidate sets how likely anything grows here at all (poor soil → sparse)
                    float best = 0f; foreach (float wv in w) best = Mathf.Max(best, wv);
                    if (r.Value > Mathf.Clamp01(best * 2.2f)) continue;
                    float pick = r.Value * total; int chosen = cand[cand.Count - 1];
                    for (int i = 0; i < cand.Count; i++) { pick -= w[i]; if (pick <= 0f) { chosen = cand[i]; break; } }
                    var csp = species[chosen];

                    // SHAPE from the site
                    float crowd = rl == Role.Canopy ? shade : 0f;
                    float sy = 1f + crowd * 0.3f - exposure * 0.45f;                // crowded → tall; exposed → stunted
                    float sxz = 1f - crowd * 0.18f + (1f - crowd) * 0.08f + exposure * 0.1f;   // open → spreading
                    // near the species' cold / dry limits: smaller (the treeline, the desert margin)
                    float coldMargin = SS(0f, 14f, lc.tMinC - csp.s.tempMinC);
                    float dryMargin = SS(0f, 400f, lc.precipMm - csp.s.precipMinMm);
                    float limit = Mathf.Lerp(0.5f, 1f, Mathf.Min(coldMargin, dryMargin));
                    // AGE: seedlings, adults, old giants; standing dead snags (more on stressed sites)
                    float age = r.Value, scale;
                    bool dead = false;
                    if (rl <= Role.Understory && age < 0.16f) scale = r.Range(0.22f, 0.45f);
                    else if (rl == Role.Canopy && age > 0.93f) scale = r.Range(1.25f, 1.55f);
                    else scale = r.Range(0.75f, 1.2f);
                    if (rl == Role.Canopy && scale > 0.6f && r.Value < 0.03f + (1f - limit) * 0.12f + exposure * 0.05f) dead = true;
                    if (rl <= Role.Understory)
                    {
                        float cr = csp.crownR > 0f ? csp.crownR : csp.g.heightM * 0.3f;
                        float clo = csp.crownR > 0f ? csp.crownLo : csp.g.heightM * 0.45f, chi = csp.crownR > 0f ? csp.crownHi : csp.g.heightM;
                        float sc0 = scale * limit;
                        float fit = 1f;
                        foreach (var c in crowns)
                        {
                            float dx = c.x - px, dz = c.z - pz, dist = Mathf.Sqrt(dx * dx + dz * dz);
                            // the largest scale at which the crowns don't overlap in plan AND height
                            float sH = Mathf.Max(0f, (dist - c.r * 0.9f) / Mathf.Max(cr * sxz * 0.9f, 1e-3f));
                            float nLo = sm.heightM + clo * sy * sc0, nHi = sm.heightM + chi * sy * sc0;
                            bool clear = nHi < c.lo || nLo > c.hi;                            // stacked (understorey under canopy) is fine
                            if (!clear) fit = Mathf.Min(fit, sH / Mathf.Max(sc0, 1e-3f));
                        }
                        if (fit < 0.55f) continue;                                       // no room: something else already holds this spot
                        scale *= Mathf.Min(fit, 1f);
                        float fs = scale * limit;
                        crowns.Add((px, pz, cr * sxz * fs, sm.heightM + clo * sy * fs, sm.heightM + chi * sy * fs));
                    }
                    // wind-bent: lean downwind on exposed ground
                    float lean = exposure * Mathf.Clamp(lc.windLoad, 0.2f, 3f) * 7f + slope * 2f;
                    list.Add(new Inst
                    {
                        sp = chosen, x = px, y = sm.heightM - 0.05f, z = pz, rot = r.Range(0f, 360f), scale = scale * limit,
                        sy = Mathf.Max(0.35f, sy), sxz = Mathf.Max(0.5f, sxz),
                        tiltX = windDir.y * lean, tiltZ = -windDir.x * lean, dead = dead,
                    });
                    if (rl == Role.Canopy && !dead)
                    {
                        canopyPts.Add(new Vector3(px, pz, chosen));
                        canopyCount[chosen] = (canopyCount.TryGetValue(chosen, out int cc) ? cc : 0) + 1;
                    }
                }
            }
            return list;
        }

        // ── rendering ─────────────────────────────────────────────────────────────────────────────────────
        public void Render(Camera cam, double ox, double oz)
        {
            EnsureDeadBatches();
            for (int i = 0; i < species.Count; i++) for (int l = 0; l < 3; l++) { batches[i, l].Clear(); deadBatches[i, l].Clear(); }
            Vector3 cp = cam.transform.position;
            int drawn = 0;
            foreach (var c in cells.Values)
            {
                if (c.inst == null) continue;
                float bx = (float)(c.cx * CellSize - ox), bz = (float)(c.cz * CellSize - oz);
                foreach (var it in c.inst)
                {
                    if (drawn >= MaxInstancesPerFrame) break;
                    var sp = species[it.sp];
                    Vector3 pos = new Vector3(bx + it.x, it.y, bz + it.z);
                    float d = Vector3.Distance(pos, cp);
                    if (d > RoleDist[(int)sp.role]) continue;
                    int lod = d < LodDist[0] ? 0 : d < LodDist[1] ? 1 : 2;
                    if (sp.role == Role.Ground) lod = Mathf.Min(lod, 1);
                    int use = Available(sp, lod);
                    if (use < 0) continue;
                    var m4 = Matrix4x4.TRS(pos, Quaternion.Euler(it.tiltX, 0f, it.tiltZ) * Quaternion.Euler(0f, it.rot, 0f),
                                           new Vector3(it.scale * it.sxz, it.scale * it.sy, it.scale * it.sxz));
                    (it.dead ? deadBatches : batches)[it.sp, use].Add(m4);
                    drawn++;
                }
            }
            for (int i = 0; i < species.Count; i++)
            {
                var sp = species[i];
                for (int l = 0; l < 3; l++)
                {
                    var list = batches[i, l];
                    if (list.Count == 0) continue;
                    bool casts = sp.role == Role.Canopy ? l < 2 : sp.role != Role.Ground && l == 0;
                    Draw(sp.mesh[l, 0], sp.wood, list, casts, cam);
                    Draw(sp.mesh[l, 1], sp.leaf, list, casts, cam);
                    Draw(sp.mesh[l, 2], sp.flower, list, casts && l == 0, cam);
                }
                for (int l = 0; l < 3; l++)                                      // standing dead: bare wood
                    if (deadBatches[i, l].Count > 0) Draw(sp.mesh[l, 0], deadWood(sp), deadBatches[i, l], l < 2, cam);
            }
        }

        List<Matrix4x4>[,] deadBatches;
        readonly Dictionary<Species, Material> deadMats = new();
        void EnsureDeadBatches()
        {
            if (deadBatches != null && deadBatches.GetLength(0) == species.Count) return;
            deadBatches = new List<Matrix4x4>[species.Count, 3];
            for (int i = 0; i < species.Count; i++) for (int l = 0; l < 3; l++) deadBatches[i, l] = new List<Matrix4x4>(16);
        }
        // weathered, bleached wood for snags
        Material deadWood(Species sp)
        {
            if (deadMats.TryGetValue(sp, out var m) && m) return m;
            m = new Material(sp.wood) { enableInstancing = true };
            Color c = sp.wood.GetColor("_BaseColor");
            float g = c.grayscale;
            m.SetColor("_BaseColor", Color.Lerp(c, new Color(g, g, g) * 1.25f, 0.6f));
            deadMats[sp] = m;
            return m;
        }

        // Pick the requested LOD if built; otherwise request it and fall back to any built LOD (coarse first).
        int Available(Species sp, int lod)
        {
            if (sp.built[lod]) return lod;
            if (!sp.requested[lod]) { sp.requested[lod] = true; buildQueue.Enqueue((sp, lod)); }
            for (int l = 2; l >= 0; l--) if (sp.built[l]) return l;
            if (!sp.requested[2]) { sp.requested[2] = true; buildQueue.Enqueue((sp, 2)); }
            return -1;
        }

        void Draw(Mesh mesh, Material mat, List<Matrix4x4> list, bool casts, Camera cam)
        {
            if (mesh == null || mesh.vertexCount < 4) return;
            if (DebugPlainMaterial)   // U key: draw with stock URP Lit — tells a Flora-shader fault from a placement fault
            {
                if (plainMat == null) { plainMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true }; plainMat.SetColor("_BaseColor", new Color(1f, 0.1f, 0.9f)); }
                mat = plainMat;
            }
            var rp = new RenderParams(mat)
            {
                layer = layer, camera = cam, receiveShadows = true,
                shadowCastingMode = casts ? ShadowCastingMode.On : ShadowCastingMode.Off,
                worldBounds = new Bounds(cam.transform.position, Vector3.one * 2000f),
            };
            for (int start = 0; start < list.Count; start += buf.Length)
            {
                int n = Mathf.Min(buf.Length, list.Count - start);
                list.CopyTo(start, buf, 0, n);
                Graphics.RenderMeshInstanced(rp, mesh, 0, buf, n);
            }
        }

        // ── what grows here (for the HUD) ──
        public void SpeciesHere(in LocalClimate lc, List<Species> into)
        {
            into.Clear();
            foreach (var sp in species)
                if (sp.biomes.Contains(lc.biome) && (sp.hardy || PlantBiology.FitsLocal(sp.s, lc.tMinC, lc.tMaxC, lc.precipMm, lc.windLoad, sp.usable, out _)))
                    into.Add(sp);
        }

        public int InstanceCount { get { int n = 0; foreach (var c in cells.Values) if (c.inst != null) n += c.inst.Count; return n; } }

        /// Re-scatter everything (the geography changed, e.g. rivers arrived). In-flight jobs finish unobserved.
        public void Invalidate() { cells.Clear(); runningCells = 0; }

        public void Dispose()
        {
            foreach (var sp in species)
            {
                Object.Destroy(sp.wood); Object.Destroy(sp.leaf); Object.Destroy(sp.flower);
                if (deadMats.TryGetValue(sp, out var dm) && dm) Object.Destroy(dm);
                foreach (var m in sp.mesh) if (m) Object.Destroy(m);
            }
            species.Clear(); cells.Clear();
        }

        static readonly string[] Syl1 = { "Vel", "Tor", "Ash", "Myr", "Kel", "Sol", "Ven", "Or", "Tha", "Lum", "Qir", "Zan", "Ep", "Hal", "Mor", "Syl" };
        static readonly string[] Syl2 = { "a", "i", "o", "ae", "u", "ei", "y" };
        static readonly string[] Syl3 = { "phyta", "dendron", "carpa", "flora", "thallus", "spora", "cactea", "morpha", "phylla", "bulbis" };
        static readonly string[] Epi = { "gigantea", "minor", "radians", "pendula", "velutina", "spinosa", "lucens", "vulgaris", "aurea", "nocturna", "alata", "tortuosa" };
        static readonly string[] LinNames = { "Aeth", "Brom", "Cyr", "Dhal", "Eld", "Fyr" };
        static string SpeciesName(ref DetRng r, int lin)
        {
            string genus = (lin >= 0 ? LinNames[lin % LinNames.Length] : Syl1[r.RangeInt(0, Syl1.Length)])
                         + Syl2[r.RangeInt(0, Syl2.Length)] + Syl3[r.RangeInt(0, Syl3.Length)];
            return genus + " " + Epi[r.RangeInt(0, Epi.Length)];
        }
    }
}
