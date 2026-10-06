namespace CLAY.Galaxy
{
    public enum PlanetType { Terrestrial, Ocean, Desert, GasGiant, IceGiant, FrozenRock }
    public enum HabClass   { Habitable, Marginal, Hostile, NotViable }
    public enum WaterChemistry { Iron, Sulfide, Clear, Phosphate }   // ocean tint (design §Rendering)

    /// <summary>
    /// A generated planet — design doc §2 (orbit) + §3 (physical) + the derived habitability. Rocky worlds
    /// in the habitable zone with the right mass are candidates for a cell-stage biosphere.
    /// </summary>
    [System.Serializable]
    public class PlanetData
    {
        public int    index;
        public string name;              // catalogue designation, e.g. "Delfor-2" (star name + orbital index)
        public string colloquial;        // native name the inhabitants use, e.g. "Belininian" (see NameGen)
        public PlanetType type;
        public float  semiMajorAxisAU;   // distance from star (mirror of orbit.semiMajorAxisAU)
        public OrbitElements orbit;      // full 3D orbit (inclination, eccentricity, phase…)
        public float  axialTiltDeg;      // obliquity → seasons
        public float  rotationHours;     // day length
        public float  radiusEarth;       // R⊕ (for rendering size)
        public float  mass;              // M⊕
        public float  albedo;            // 0–1
        public float  insolation;        // S⊕ (Earth around the Sun = 1)
        public float  hostStarTempK;     // effective temperature of the host star → tunes biosphere pigment
        public int    hostStar;          // 0 = primary A, 1 = B, 2 = C …; −1 = CIRCUMBINARY (orbits the A+B barycentre)
        public float  secondaryFlux;     // S⊕ arriving from the system's OTHER stars (a second sun in the sky)
        public float  secondaryStarTempK;// temperature of the brightest other star (0 if single)
        public float  bombardment;       // 0–1 impact history from nearby belts + system youth → craters/ejecta
        public float  tidalHeat;         // 0–1 tidal flexing from eccentricity + neighbour giants → fractures/volcanism
        public float  greenhouseK;       // warming above blackbody temp
        public float  meanTempC;         // derived mean surface temperature
        public bool   inHZ;
        public float  habitabilityIndex; // 0–1
        public HabClass habClass;

        // ── surface chemistry — drives the procedural terrain/appearance (design §Rendering) ──
        public float  volcanism;         // 0–1 → relief, hotspots, dark basalt
        public float  mineralDiversity;  // 0–1 → light, flat clay-shelf patches
        public float  waterCoverage;     // 0–1 → fraction of surface below sea level
        public WaterChemistry waterChemistry;
        public int themeOverride = -1;                       // ≥0 forces a PlanetTexture.ChemTheme (planet editor); −1 = derived

        // ── causal parameters (PlanetTypes.md §1). −1 / null = DERIVED by PlanetPhysics; set = forced ──
        public float pressureOverrideBar = -1f;              // surface pressure, bar
        public AtmoComposition atmo;                         // gas mix
        public float magneticField = -1f;                    // 0..1 dynamo strength
        public int   liquidOverride = -1;                    // LiquidType
        public float redox = -1f;                            // 0 reducing … 1 oxidising
        public int   tectonicsOverride = -1;                 // PlanetTexture.TectonicMode
        public float flareDose = -1f;                        // 0..1
        public bool  spinResonance32;                        // Mercury-like 3:2 spin–orbit resonance
        public float starFlareActivity;                      // host star's flare activity (set by the generator)

        public bool tidallyLocked;                           // 1:1 spin-orbit → an "eyeball" world (hot substellar, frozen night)

        // ── satellites ──
        public bool isMoon;                                   // true for moons (orbit interpreted about the host planet)
        public System.Collections.Generic.List<PlanetData> moons = new System.Collections.Generic.List<PlanetData>();

        public static bool IsRocky(PlanetType t) =>
            t == PlanetType.Terrestrial || t == PlanetType.Ocean || t == PlanetType.Desert || t == PlanetType.FrozenRock;

        public bool Rocky => IsRocky(type);
    }
}
