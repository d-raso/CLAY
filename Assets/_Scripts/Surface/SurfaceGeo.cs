using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.Surface
{
    /// <summary>
    /// The geography of one planet as seen from the ground. Pure and thread-safe (terrain and plant scatter are
    /// computed on worker threads).
    ///
    /// Coordinates: a local tangent frame at the landing point — +X east, +Z north, +Y up, metres. A point (x, z)
    /// maps to a planet direction by projecting onto the real-radius sphere, so walking 1000 km moves you ~9°
    /// across the same map that is painted on the planet from orbit.
    ///
    /// Height = the orbital height field (continents, ranges, craters — exactly what you saw from space) in metres,
    /// plus LANDFORMS too small to see from orbit, each switched on by the planet's own properties:
    ///  • rolling hills everywhere (domain-warped, so they read as eroded, not noise)
    ///  • mountain RANGES in regional belts — stronger with tectonics/volcanism
    ///  • river VALLEYS & canyons carved where there's liquid (water or methane)
    ///  • MESAS / terraced strata on dry, mineral-rich crust
    ///  • DUNE seas in dry basins of worlds with air (Titan's tholin dunes, Martian ergs)
    ///  • CRATERS on airless / bombarded worlds
    ///  • fine knolls, tussocks and micro-roughness
    /// </summary>
    public sealed class SurfaceGeo
    {
        public readonly PlanetData planet;
        public readonly PlanetTexture.SurfaceSampler s;
        public readonly PlanetClimate climate;
        public readonly double R;                 // planet radius, metres
        public readonly float heightScale;        // metres per unit of orbital height above sea level
        public readonly bool hasSea;
        readonly double cx, cy, cz, ex, ey, ez, nx, ny, nz;   // landing frame: centre, east, north (unit, double)

        // landform strengths (0..1), derived from the planet
        public readonly float mountains, valleys, mesas, dunes, craters, hills;
        public readonly PlanetCategory category;

        // ── GEOLOGICAL PROVINCES: ~140 km regions, each with its own landform mix, picked by the world's history ──
        public enum Province { Lowland, FoldBelt, Upland, Rift, VolcanicField, FloodBasalt, Badlands, Glaciated, Karst, DuneSea, ImpactBasin }
        const int ProvCount = 11;
        // per province: hills×, mountains×, then FLOORS for mountains, valleys, mesas, dunes, plateaus, chasms,
        // volcanoes, glaciation, craters
        static readonly float[,] ProvTab =
        {
            //  hl     mt    mtF   vaF   meF   duF   plF   chF   voF   glF   crF
            { 0.45f, 0.2f,  0f,   0.5f, 0f,   0f,   0f,   0f,   0f,   0f,   0f   },  // Lowland: broad plains, meandering rivers
            { 1.3f,  1f,    0.85f,0.3f, 0f,   0f,   0f,   0f,   0f,   0f,   0f   },  // FoldBelt: young ranges
            { 1.9f,  0.5f,  0f,   0.55f,0f,   0f,   0f,   0f,   0f,   0f,   0f   },  // Upland: worn-down old mountains, deep valleys
            { 0.7f,  0.6f,  0.3f, 0.3f, 0f,   0f,   0.3f, 0.75f,0.3f, 0f,   0f   },  // Rift: graben, escarpments, a few cones
            { 0.8f,  0.6f,  0f,   0.2f, 0f,   0f,   0f,   0f,   0.85f,0f,   0f   },  // VolcanicField
            { 0.35f, 0.15f, 0f,   0.3f, 0.5f, 0f,   0.9f, 0.2f, 0f,   0f,   0f   },  // FloodBasalt: stepped traps, plateaus
            { 0.8f,  0.3f,  0f,   0.75f,0.85f,0f,   0.2f, 0f,   0f,   0f,   0f   },  // Badlands: terraces, gullies
            { 1.2f,  1f,    0.4f, 0.55f,0f,   0f,   0f,   0f,   0f,   0.85f,0f   },  // Glaciated: U-valleys, cirques
            { 1.7f,  0.4f,  0f,   0.6f, 0f,   0f,   0f,   0f,   0f,   0f,   0f   },  // Karst: tower hills, sinks
            { 0.25f, 0.1f,  0f,   0f,   0f,   0.95f,0f,   0f,   0f,   0f,   0f   },  // DuneSea: erg
            { 0.6f,  0.5f,  0f,   0.2f, 0f,   0f,   0f,   0f,   0f,   0f,   0.7f },  // ImpactBasin: old eroded craters
        };
        readonly float[] provCum = new float[ProvCount];
        public Province ProvinceAt(double x, double z) { ProvinceMix((float)x, (float)z, out var pv, out _); return pv; }
        void ProvinceMix(float fx, float fz, out Province pv, out float w)
        {
            float wx = (N(fx, fz, 45000f, 401f) - 0.5f) * 50000f, wz = (N(fx, fz, 45000f, 403f) - 0.5f) * 50000f;
            PlanetTexture.SurfaceSampler.WorleyNoise(new Vector3((fx + wx) / 140000f + 41f, 7.7f, (fz + wz) / 140000f - 13f), out float f1, out float f2, out float cr);
            int i = 0; while (i < ProvCount - 1 && cr > provCum[i]) i++;
            pv = (Province)i;
            w = SS(0f, 0.14f, f2 - f1);   // provinces blend across their borders
        }          // the documented category this world is (PlanetCategories)
        public readonly float plateaus, chasms, volcanoes, glaciation;   // big landforms (0..1)
        public readonly bool volatiles;                                  // something to freeze / flow (water, methane…)
        public SurfaceHydrology hydro;                                   // rivers & lakes (set after the background build)
        public readonly float reliefG;   // gravity relief factor (1 at 1 g)
        readonly float duneAngle, duneLen, mesaStep;

        public SurfaceGeo(PlanetData p, ulong planetSeed, Vector3 landingDir)
        {
            planet = p;
            s = PlanetTexture.Sampler(p, planetSeed);
            climate = PlanetClimate.Derive(p);
            R = System.Math.Max(0.05, p.radiusEarth) * 6_371_000.0;
            // Relief scales with gravity: the tallest peak a crust can support goes roughly as 1/g (Olympus Mons on
            // Mars is 22 km, Everest 9 km), so small worlds get towering ranges and massive ones are smoothed flat.
            reliefG = Mathf.Clamp(Mathf.Pow(1f / Mathf.Max(climate.gravity, 0.05f), 0.8f), 0.3f, 3.5f);
            heightScale = 7000f * Mathf.Clamp(s.Relief / 0.055f, 0.6f, 1.6f) * reliefG;
            hasSea = s.HasLiquid;

            var pal = s.Palette;
            bool air = s.HasAtmo;
            float wet = Mathf.Clamp01(s.WaterCov * 1.6f + (pal.theme == PlanetTexture.ChemTheme.Methanic || pal.theme == PlanetTexture.ChemTheme.Tholin ? 0.45f : 0f));
            float dry = 1f - Mathf.Clamp01(s.WaterCov * 1.5f);
            var tect = PlanetTexture.Tectonics(p);
            mountains = Mathf.Clamp01(s.MountainAmp * 0.55f + s.Volcanism * 0.3f + (tect == PlanetTexture.TectonicMode.MobileLid ? 0.35f : 0f) + 0.15f);
            valleys = air ? Mathf.Clamp01(wet * 0.9f + 0.1f) : 0f;
            mesas = Mathf.Clamp01(dry * s.Mineral * 1.4f + (pal.theme == PlanetTexture.ChemTheme.Evaporite || pal.theme == PlanetTexture.ChemTheme.Ferrous ? 0.35f : 0f));
            dunes = air ? Mathf.Clamp01(pal.dune * 1.2f + dry * 0.45f + (pal.theme == PlanetTexture.ChemTheme.Tholin ? 0.5f : 0f)) : 0f;
            craters = Mathf.Clamp01(s.CraterAmp * 1.2f + (air ? 0f : 0.5f) + p.bombardment * 0.4f);
            // EROSION: rain, rivers, wind and roots erase craters within a few hundred Myr on a wet, living world
            float erosion = Mathf.Clamp01(s.WaterCov * 1.6f + (air ? 0.25f : 0f) + pal.vegAmt * 0.8f);
            craters *= 1f - erosion * 0.92f;
            hills = 0.6f + 0.4f * mountains;
            // BIG LANDFORMS from the planet's geology and climate
            volatiles = s.WaterCov > 0.02f || pal.theme == PlanetTexture.ChemTheme.Methanic || pal.theme == PlanetTexture.ChemTheme.Snowball
                     || pal.theme == PlanetTexture.ChemTheme.AmmoniaIce || pal.theme == PlanetTexture.ChemTheme.SalineIce;
            bool stagnant = tect == PlanetTexture.TectonicMode.StagnantLid || tect == PlanetTexture.TectonicMode.Dead;
            plateaus = Mathf.Clamp01(dry * 0.5f + (stagnant ? 0.3f : 0f) + mesas * 0.3f + s.Mineral * 0.15f - 0.15f);
            chasms = Mathf.Clamp01((stagnant ? 0.45f : 0.1f) + dry * 0.3f + s.Volcanism * 0.2f - 0.2f);            // rifts, Valles-Marineris trenches
            volcanoes = Mathf.Clamp01(s.Volcanism * 1.2f - 0.25f + (tect == PlanetTexture.TectonicMode.HeatPipe ? 0.3f : 0f));
            glaciation = volatiles ? Mathf.Clamp01((-(p.meanTempC - 12f)) / 40f) : 0f;

            // ── the CATEGORY's signature landforms (on top of the physics above) ──
            category = PlanetCategories.Classify(p, pal.theme, planetSeed).cat;
            switch (category)
            {
                case PlanetCategory.CrateredDead: case PlanetCategory.RegolithDust: case PlanetCategory.EjectaDusted:
                    craters = Mathf.Max(craters, 0.9f); break;
                case PlanetCategory.Canyon:
                    chasms = Mathf.Max(chasms, 0.85f); valleys = Mathf.Max(valleys, 0.5f); break;
                case PlanetCategory.Mesa:
                    mesas = Mathf.Max(mesas, 0.9f); plateaus = Mathf.Max(plateaus, 0.8f); break;
                case PlanetCategory.VolcanicHighland: case PlanetCategory.HellMoon:
                    volcanoes = Mathf.Max(volcanoes, 0.85f); mountains = Mathf.Max(mountains, 0.7f); break;
                case PlanetCategory.BasaltPlains: case PlanetCategory.Obsidian:
                    volcanoes = Mathf.Max(volcanoes, 0.45f); mountains *= 0.5f; break;
                case PlanetCategory.Glacier: case PlanetCategory.IceCappedOcean:
                    glaciation = Mathf.Max(glaciation, 0.9f); mountains = Mathf.Max(mountains, 0.55f); break;
                case PlanetCategory.Desert: case PlanetCategory.OchreIron:
                    dunes = Mathf.Max(dunes, air ? 0.75f : 0f); break;
                case PlanetCategory.Karst:
                    valleys = Mathf.Max(valleys, 0.6f); hills = Mathf.Max(hills, 1f); break;
                case PlanetCategory.Pangaea: case PlanetCategory.Continental:
                    mountains = Mathf.Max(mountains, 0.6f); break;
                case PlanetCategory.SuperEarth: case PlanetCategory.MegaEarth:
                    mountains *= 0.6f; break;
            }
            // province weights from the world's HISTORY (past volcanism, plate style, climate, water, impacts)
            {
                bool mobile = tect == PlanetTexture.TectonicMode.MobileLid;
                float past = Mathf.Clamp01(s.Volcanism + (stagnant ? 0.25f : 0f) + (mobile ? 0.25f : 0f));
                float cold = Mathf.Clamp01((22f - p.meanTempC) / 40f);
                var pw = new float[ProvCount];
                pw[(int)Province.Lowland] = 0.7f + wet * 0.6f;
                pw[(int)Province.FoldBelt] = mobile ? 1.0f : 0.25f * past;
                pw[(int)Province.Upland] = 0.6f + (mobile ? 0.3f : 0f);
                pw[(int)Province.Rift] = past * 0.5f + (mobile ? 0.2f : 0f);
                pw[(int)Province.VolcanicField] = s.Volcanism * 1.2f + (tect == PlanetTexture.TectonicMode.HeatPipe ? 0.8f : 0f);
                pw[(int)Province.FloodBasalt] = past * 0.6f + (stagnant ? 0.35f : 0f);
                pw[(int)Province.Badlands] = air ? dry * 0.7f + s.Mineral * 0.25f : 0f;
                pw[(int)Province.Glaciated] = volatiles ? cold * 0.9f : 0f;
                pw[(int)Province.Karst] = s.WaterCov > 0.05f ? wet * 0.35f + pal.vegAmt * 0.3f : 0f;
                pw[(int)Province.DuneSea] = air ? dry * dry * 0.9f : 0f;
                pw[(int)Province.ImpactBasin] = 0.05f + p.bombardment * 0.5f + (air ? 0f : 0.4f);
                float tot = 0f; for (int k = 0; k < ProvCount; k++) tot += pw[k];
                float acc = 0f; for (int k = 0; k < ProvCount; k++) { acc += pw[k] / Mathf.Max(tot, 1e-5f); provCum[k] = acc; }
            }
            var r = new DetRng(DetRng.Hash(planetSeed, 0xD0E5UL));
            duneAngle = r.Range(0f, Mathf.PI);          // prevailing wind direction
            duneLen = r.Range(160f, 420f);               // dune spacing
            mesaStep = r.Range(18f, 60f);                // stratum thickness

            Vector3 c = landingDir.sqrMagnitude > 1e-8f ? landingDir.normalized : Vector3.up;
            Vector3 north0 = Vector3.ProjectOnPlane(Vector3.up, c);
            if (north0.sqrMagnitude < 1e-6f) north0 = Vector3.ProjectOnPlane(Vector3.forward, c);
            north0.Normalize();
            Vector3 east0 = Vector3.Cross(c, north0).normalized;   // Unity (left-handed): right = up × forward
            cx = c.x; cy = c.y; cz = c.z; ex = east0.x; ey = east0.y; ez = east0.z; nx = north0.x; ny = north0.y; nz = north0.z;
        }

        /// Unit planet direction under local point (x east, z north) in metres.
        public Vector3 DirAt(double x, double z)
        {
            double px = cx * R + ex * x + nx * z, py = cy * R + ey * x + ny * z, pz = cz * R + ez * x + nz * z;
            double inv = 1.0 / System.Math.Sqrt(px * px + py * py + pz * pz);
            return new Vector3((float)(px * inv), (float)(py * inv), (float)(pz * inv));
        }

        /// A direction expressed in the local frame (x = east, y = up, z = north) — e.g. for the sun.
        public Vector3 ToLocal(Vector3 planetDir) => new Vector3(
            (float)(planetDir.x * ex + planetDir.y * ey + planetDir.z * ez),
            (float)(planetDir.x * cx + planetDir.y * cy + planetDir.z * cz),
            (float)(planetDir.x * nx + planetDir.y * ny + planetDir.z * nz));

        public struct Sample
        {
            public float heightM, h01, moist;
            public Vector3 dir;
            public bool underwater;
            public float dune, valley, strata, crater, mountain;   // landform masks for colouring
            public float fx, fz;                                    // local position (for regional materials)
            public float volcano, glacier, chasm, plateau;          // big-landform masks
            public float water, flowX, flowZ;                       // river / lake surface (1 = liquid here) and flow direction
            public int province; public float provW;                // geological province + how fully inside it
        }

        public Sample At(double x, double z)
        {
            var o = new Sample();
            o.dir = DirAt(x, z);
            o.h01 = s.Height01(o.dir);
            float above = o.h01 - s.SeaLevel;
            float baseM = above * heightScale;
            float fx = (float)x, fz = (float)z;   // fine for noise out to ±500 km at these scales

            // the local GEOLOGICAL PROVINCE reshapes the planet-wide landform mix (locals shadow the fields)
            ProvinceMix(fx, fz, out var prov, out float pw);
            int pi = (int)prov;
            float PF(float v, int col) => Mathf.Lerp(v, Mathf.Max(v, ProvTab[pi, col]), pw);
            float hills = Mathf.Lerp(this.hills, this.hills * ProvTab[pi, 0], pw);
            float mountains = PF(Mathf.Lerp(this.mountains, this.mountains * ProvTab[pi, 1], pw), 2);
            float valleys = s.HasAtmo ? PF(this.valleys, 3) : 0f;
            float mesas = PF(this.mesas, 4);
            float dunes = this.dunes > 0f ? PF(this.dunes, 5) : 0f;
            float plateaus = PF(this.plateaus, 6);
            float chasms = PF(this.chasms, 7);
            float volcanoes = PF(this.volcanoes, 8);
            float glaciation = volatiles ? PF(this.glaciation, 9) : 0f;
            float craters = PF(this.craters, 10);
            if (prov != Province.DuneSea) dunes *= Mathf.Lerp(1f, 0.5f, pw);
            o.province = pi; o.provW = pw;

            float valleyCut = 0f;
            // domain warp so every landform bends organically instead of lining up with the noise lattice
            float wx = (N(fx, fz, 6000f, 3f) - 0.5f) * 3000f, wz = (N(fx, fz, 6000f, 9f) - 0.5f) * 3000f;
            float qx = fx + wx, qz = fz + wz;

            // rolling hills
            float h = (N(qx, qz, 3800f, 0f) - 0.5f) * 2f * 240f * hills
                    + (N(qx, qz, 1100f, 5f) - 0.5f) * 2f * 60f * hills;

            // mountain ranges in regional belts: sharp ridged peaks where the belt mask is high
            float belt = SS(0.52f, 0.78f, N(fx, fz, 26000f, 17f) + Mathf.Clamp01(above * 3f) * 0.25f
                          + (prov == Province.FoldBelt || prov == Province.Glaciated ? 0.3f * pw : 0f));
            float ridge = Ridged(qx, qz, 4200f, 7f);
            float mtn = belt * mountains;
            h += ridge * ridge * (400f + 2200f * mountains) * mtn;
            h += Ridged(qx, qz, 900f, 21f) * 160f * mtn;
            o.mountain = mtn;

            // river valleys / canyons: carve along the "creases" of a warped ridged field (dendritic-looking lines)
            if (valleys > 0.01f && above > -0.02f)
            {
                float line = Ridged(qx * 0.8f, qz * 0.8f, 5200f, 31f);
                float carve = SS(0.72f, 0.97f, line) * valleys;
                float depth = 45f + 260f * mtn + 120f * mesas;
                h -= carve * depth;
                o.valley = carve;
                valleyCut = carve * depth;
            }

            // mesas / terraces: quantise elevation into strata with steep risers and flat treads
            if (mesas > 0.01f)
            {
                float t0 = baseM + h;
                float k = t0 / mesaStep, fl = Mathf.Floor(k), fr = k - fl;
                float terraced = (fl + Mathf.Pow(SS(0f, 1f, fr), 5f)) * mesaStep;
                float m = mesas * SS(0.35f, 0.6f, N(fx, fz, 14000f, 41f));
                h += (terraced - t0) * m;
                o.strata = m * fr;
            }

            // dune seas in low, flat, dry ground
            if (dunes > 0.01f)
            {
                // A real dune field, not a sine: crest lines bent by multi-scale warp, spacing that swells and narrows,
                // crests that break into separate segments (barchanoid), a rounded brink, and a second, smaller dune set
                // crossing at an angle (the wind shifts seasonally) so the field reads as organic.
                float sea = dunes * SS(0.45f, 0.7f, N(fx, fz, 18000f, 61f)) * (1f - Mathf.Clamp01(mtn * 2f));
                if (sea > 0.001f)
                {
                    float Dune(float ang, float len, float seed)
                    {
                        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                        float spacing = len * (0.7f + 0.6f * N(fx, fz, 9000f, seed + 1f));
                        float along = (fx * ca + fz * sa) / spacing
                                    + (N(fx, fz, 3200f, seed + 2f) - 0.5f) * 4.5f + (N(fx, fz, 900f, seed + 3f) - 0.5f) * 1.1f;
                        float ph = along - Mathf.Floor(along);
                        float cr = ph < 0.7f ? ph / 0.7f : (1f - ph) / 0.3f;                   // gentle stoss, steep lee
                        cr = cr * cr * (3f - 2f * cr);
                        cr = Mathf.Pow(cr, 1.3f);                                              // broad troughs, rounded brink
                        float seg = SS(0.3f, 0.6f, N(fx, fz, spacing * 2.5f, seed + 4f));     // crests break into segments
                        return cr * Mathf.Lerp(0.25f, 1f, seg);
                    }
                    float primary = Dune(duneAngle, duneLen, 53f);
                    float secondary = Dune(duneAngle + 0.9f, duneLen * 0.45f, 71f);
                    float amp = Mathf.Lerp(8f, 40f, N(fx, fz, 7000f, 67f));
                    float crest = primary + secondary * 0.3f;
                    h += crest * amp * sea;
                    o.dune = sea * Mathf.Clamp01(crest);
                }
            }

            // craters (km-scale + smaller) on airless / bombarded worlds
            if (craters > 0.01f)
            {
                float c = Crater(fx, fz, 2600f, 71f) * 180f + Crater(fx, fz, 700f, 73f) * 45f + Crater(fx, fz, 160f, 79f) * 8f;
                h += c * craters;
                o.crater = Mathf.Clamp01(-c * craters);
            }

            // PLATEAUS: uplifted blocks with sharp escarpments (and the caprock terraces the mesas code adds)
            if (plateaus > 0.01f)
            {
                float pn = N(qx, qz, 55000f, 301f) + (N(fx, fz, 9000f, 303f) - 0.5f) * 0.12f;
                float plat = SS(0.56f, 0.6f, pn) * plateaus;
                h += plat * Mathf.Lerp(300f, 1400f, N(fx, fz, 90000f, 305f));
                o.plateau = plat;
            }
            // CHASMS / RIFTS: long winding trenches with near-vertical walls and flat floors
            if (chasms > 0.01f)
            {
                float line = Ridged(qx * 0.9f, qz * 0.9f, 140000f, 311f);
                float wall = SS(0.955f, 0.975f, line);
                float floorW = SS(0.975f, 0.99f, line);
                float depth = Mathf.Lerp(400f, 2600f, N(fx, fz, 120000f, 313f)) * chasms;
                float ledge = Mathf.Floor(wall * 5f) / 5f;                               // stepped wall ledges
                h -= Mathf.Lerp(wall, ledge, 0.35f) * depth * 0.85f + floorW * depth * 0.15f;
                o.chasm = wall;
                o.valley = Mathf.Max(o.valley, wall * 0.5f);
            }
            // VOLCANOES: cones in Worley cells on volcanic worlds, a summit caldera, lava around the vents
            if (volcanoes > 0.01f)
            {
                PlanetTexture.SurfaceSampler.WorleyNoise(new Vector3(fx / 80000f + 17f, 3.3f, fz / 80000f - 5f), out float f1, out _, out float vr);
                if (vr < volcanoes * 0.45f)
                {
                    float Rr = Mathf.Lerp(0.18f, 0.42f, Frac(vr * 13.7f));                 // cone radius (cell units)
                    float u = f1 / Rr;
                    if (u < 1.4f)
                    {
                        bool shield = Frac(vr * 29.3f) < 0.5f;                              // broad shield vs steep strato
                        float H = Mathf.Lerp(1200f, 5500f, Frac(vr * 7.1f)) * (shield ? 0.8f : 1f);
                        float cone = u < 1f ? Mathf.Pow(1f - u, shield ? 1.3f : 2.2f) : 0f;
                        float cald = Mathf.Lerp(0.07f, 0.16f, Frac(vr * 3.3f));
                        float bowl = u < cald ? (1f - (u / cald) * (u / cald)) * H * 0.18f : 0f;
                        float gully = (Ridged(qx, qz, 1800f, 317f) - 0.5f) * 90f * cone;     // radial erosion gullies
                        h += cone * H - bowl + gully;
                        o.volcano = Mathf.Clamp01(cone * 1.2f + (u < cald * 1.3f ? 0.5f : 0f));
                    }
                }
            }

            // fine detail
            h += (N(fx, fz, 120f, 23f) - 0.5f) * 14f * (0.4f + mtn)
               + (N(fx, fz, 28f, 37f) - 0.5f) * 2.4f
               + (N(fx, fz, 6f, 51f) - 0.5f) * 0.35f;

            if (above < 0f) h *= 0.35f;   // seabeds are smoother
            h *= reliefG;                 // local landforms obey the same gravity limit as the orbital relief
            o.heightM = baseM + h;
            o.fx = fx; o.fz = fz;

            // GLACIERS: where it's cold enough at this height & latitude, ice fills the valleys (V → broad U), smooth-topped
            if (glaciation > 0.01f && (o.valley > 0.05f || mtn > 0.2f))
            {
                float lat = Mathf.Abs(o.dir.y);
                float tEst = planet.meanTempC + 12f - 6.5f * Mathf.Max(o.heightM, 0f) / 1000f - 35f * lat * lat;
                float g = SS(-1f, -12f, tEst) * Mathf.Clamp01(o.valley * 1.4f + mtn * 0.35f) * Mathf.Clamp01(glaciation * 2f + 0.3f);
                if (g > 0.01f)
                {
                    float fill = valleyCut * 0.8f * reliefG + 25f;
                    o.heightM += fill * g - (N(fx, fz, 40f, 331f) - 0.5f) * 3f * g;        // ice surface: smooth, gently rippled
                    o.glacier = g;
                }
            }

            // RIVERS & LAKES (drainage network built in the background)
            if (hydro != null) hydro.Apply(ref o, x, z);
            o.underwater = (hasSea && o.heightM < 0f) || o.water > 0.5f;
            o.moist = Mathf.Clamp01(s.Moisture(o.dir) * 0.8f + (N(fx, fz, 1800f, 71f) - 0.5f) * 0.6f + 0.1f + o.valley * 0.4f);
            return o;
        }

        static float N(float x, float z, float scale, float seed)
            => PlanetTexture.SurfaceSampler.Noise(new Vector3(x / scale + seed, seed * 0.37f, z / scale - seed), 4);
        static float Ridged(float x, float z, float scale, float seed)
            => PlanetTexture.SurfaceSampler.RidgedNoise(new Vector3(x / scale + seed, seed * 0.51f, z / scale - seed), 4);

        // One crater per Worley cell (some cells empty): bowl + sharp raised rim, varied size/depth.
        static float Crater(float x, float z, float scale, float seed)
        {
            PlanetTexture.SurfaceSampler.WorleyNoise(new Vector3(x / scale + seed, seed * 0.29f, z / scale - seed), out float f1, out float _, out float cr);
            if (cr < 0.45f) return 0f;
            float rad = Mathf.Lerp(0.18f, 0.45f, Frac(cr * 7.13f));
            if (f1 >= rad * 1.35f) return 0f;
            float u = f1 / rad;
            float depth = Mathf.Lerp(0.4f, 1f, Frac(cr * 23.7f));
            float bowl = u < 1f ? -(1f - u * u) * depth : 0f;
            float rim = Mathf.Exp(-18f * (u - 1f) * (u - 1f)) * 0.25f * depth;
            return bowl + rim;
        }
        static float Frac(float v) => v - Mathf.Floor(v);

        /// HLSL-style smoothstep(edge0, edge1, x) → 0..1. (Unity's Mathf.SmoothStep(a, b, t) instead INTERPOLATES
        /// between a and b — using it as a mask was a bug that applied every landform everywhere.)
        static float SS(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public LocalClimate ClimateAt(in Sample sm) => climate.At(sm.dir, Mathf.Max(0f, sm.heightM), sm.moist, sm.underwater, false);

        /// Ground colour: the orbital albedo (so far terrain matches the view from space) blended toward a
        /// biome-specific soil/cover at close range, then modulated by landforms — pale dune crests, dark wet valley
        /// floors, banded mesa strata, bright crater ejecta — with rock on steep slopes and snow where it never thaws.
        /// Bedrock / regolith colour: geological PROVINCES of different rock suites (the planet's low/mid/high palette),
        /// dark BASALT flows (more on volcanic worlds), bright MINERAL pans & clays (on mineral-rich worlds), ACCENT
        /// staining (the world's signature mineral: iron oxide, sulfur, verdigris, salt…), and fine mottling.
        public Color Region(in Sample sm)
        {
            var pal = s.Palette;
            float prov = N(sm.fx, sm.fz, 9000f, 101f), prov2 = N(sm.fx, sm.fz, 2600f, 103f);
            float patch = N(sm.fx, sm.fz, 650f, 107f), fine = N(sm.fx, sm.fz, 70f, 109f);
            float t = Mathf.Clamp01(prov * 1.4f - 0.2f + (sm.h01 - s.SeaLevel) * 0.8f);
            Color c = t < 0.5f ? Color.Lerp(pal.landLow, pal.landMid, t * 2f) : Color.Lerp(pal.landMid, pal.landHigh, (t - 0.5f) * 2f);
            float basalt = SS(0.58f, 0.72f, prov2) * Mathf.Clamp01(s.Volcanism * 1.5f + 0.1f);
            c = Color.Lerp(c, Color.Lerp(new Color(0.11f, 0.10f, 0.10f), pal.landLow, 0.25f), basalt * 0.8f);
            float pan = SS(0.66f, 0.8f, 1f - prov2) * Mathf.Clamp01(s.Mineral * 1.4f);
            c = Color.Lerp(c, Color.Lerp(pal.landHigh, Color.white, 0.35f), pan * 0.7f);
            float stain = SS(0.6f, 0.78f, patch) * Mathf.Clamp01(pal.accentAmt * 1.5f + 0.1f);
            c = Color.Lerp(c, pal.accent, Mathf.Clamp01(stain) * 0.8f);
            c *= 0.84f + fine * 0.32f;
            c.a = 1f;
            return c;
        }

        /// The orbital albedo alone (the costly part of Ground) — terrain tiles sample it on a coarse grid.
        public Color Orbital(in Sample sm, float slope01)
        {
            // Prefer the EXACT map the system view painted on this planet (guarantees the ground matches what you
            // aimed at). Under the sea that map shows water, so the seabed falls back to the computed land colour.
            if (orbPx != null && !sm.underwater && (!hasSea || sm.h01 >= s.SeaLevel)) return SampleMap(sm.dir);   // dry worlds: basins below the datum are still land
            return s.Albedo(sm.dir, Mathf.Max(sm.h01, s.SeaLevel + 0.0005f), slope01);
        }

        Color[] orbPx; int orbW, orbH;
        public void SetOrbitalMap(Color[] px, int w, int h) { orbPx = px; orbW = w; orbH = h; }
        public bool HasMap => orbPx != null;
        /// Bilinear lookup with the same equirectangular mapping as PlanetSurface.shader (row 0 = +90° latitude).
        public Color SampleMap(Vector3 d)
        {
            float u = Mathf.Atan2(d.z, d.x) * 0.15915494f + 0.5f;
            float v = 0.5f - Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * 0.31830989f;
            float fx = u * orbW - 0.5f, fy = v * orbH - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            int xa = ((x0 % orbW) + orbW) % orbW, xb = (xa + 1) % orbW;
            int ya = Mathf.Clamp(y0, 0, orbH - 1), yb = Mathf.Clamp(y0 + 1, 0, orbH - 1);
            Color c = Color.Lerp(Color.Lerp(orbPx[ya * orbW + xa], orbPx[ya * orbW + xb], tx),
                                 Color.Lerp(orbPx[yb * orbW + xa], orbPx[yb * orbW + xb], tx), ty);
            c.a = 1f;
            return c;
        }

        /// A local-frame direction (x east, y up, z north) back in the planet's body frame.
        public Vector3 FromLocal(Vector3 l) => new Vector3(
            (float)(ex * l.x + cx * l.y + nx * l.z), (float)(ey * l.x + cy * l.y + ny * l.z), (float)(ez * l.x + cz * l.y + nz * l.z));

        public Color Ground(in Sample sm, in LocalClimate lc, float slope01) => Ground(sm, lc, slope01, Orbital(sm, slope01), out _, out _);

        // ── TERRAIN TYPES ─────────────────────────────────────────────────────────────────────────────────
        // Each has its own procedural PBR surface in SurfaceTerrain.shader (ids must match). Grounded in what landers
        // and rovers have actually seen: jointed bedrock, boulder fields & ejecta, desert pavement (Venera, Mars),
        // aeolian sand & dunes, playas & mud cracks, meteoric iron (Widmanstätten), pāhoehoe/ʻaʻā lava and live
        // flows, glacial ice & sastrugi snow, lunar regolith (with the opposition surge), evaporite salt polygons,
        // Io's sulfur allotropes, periglacial patterned ground, talus scree, hoarfrost, biological soil crusts.
        public enum TerrainType
        {
            Rocky, Bouldery, Gravel, Sandy, Dunes, FineSoil, DriedLake, Cracked, MetallicRock, Molten,
            IceSheet, Snow, Grassy, Regolith, BasaltFlow, SaltFlat, Sulfur, PatternedGround, Scree, Frost, Mossy,
        }
        public const int TypeCount = 21, PaletteSize = 8;
        int[] palette;                                   // up to 8 type ids for this planet; [0] is always Rocky
        readonly int[] slotOf = new int[TypeCount];
        [System.ThreadStatic] static float[] tsScores, tsW;
        public int[] Palette => palette;
        public int SlotOf(TerrainType t) => palette != null ? slotOf[(int)t] : -1;

        /// Survey the landing region (and a wider ring) to choose the 8 terrain types this world actually shows.
        /// Every vertex then carries 8 smoothly-interpolating weights over this palette — no seams between types.
        /// How strewn with boulders this spot is (outcrops, crater ejecta, mid slopes) — drives rock-mesh density.
        public float Boulderiness(in Sample sm, float slope01 = 0.2f)
        {
            float pB = N(sm.fx, sm.fz, 380f, 143f), pC = N(sm.fx, sm.fz, 95f, 149f);
            float outcrop = SS(0.64f, 0.78f, pB * 0.7f + pC * 0.3f);
            float midSlope = SS(0.22f, 0.42f, slope01) * (1f - SS(0.65f, 0.85f, slope01));
            return Mathf.Clamp01(outcrop * 1.3f + sm.crater * 0.9f + midSlope * 0.5f + sm.mountain * 0.3f);
        }

        public void BuildPalette(ulong seed)
        {
            palette = null;
            var total = new float[TypeCount];
            var rng = new DetRng(DetRng.Hash(seed, 0x7E44A1UL));
            for (int k = 0; k < 900; k++)
            {
                float rad = k < 600 ? 60000f : 600000f;
                double x = rng.Range(-rad, rad), z = rng.Range(-rad, rad);
                var sm = At(x, z);
                float hx = At(x + 4, z).heightM - sm.heightM, hz = At(x, z + 4).heightM - sm.heightM;
                Vector3 nrm = new Vector3(-hx / 4f, 1f, -hz / 4f).normalized;
                float slope = Mathf.Clamp01((1f - nrm.y) * 3.2f);
                Ground(sm, ClimateAt(sm), slope, Orbital(sm, 0.2f), out _, out _);
                var sc = tsScores; float sum = 0f;
                for (int t = 0; t < TypeCount; t++) sum += sc[t];
                if (sum <= 0f) continue;
                for (int t = 0; t < TypeCount; t++) total[t] += sc[t] / sum;
            }
            var order = new System.Collections.Generic.List<int>();
            for (int t = 0; t < TypeCount; t++) order.Add(t);
            order.Sort((a, b) => total[b].CompareTo(total[a]));
            var pal = new System.Collections.Generic.List<int> { (int)TerrainType.Rocky };   // rocks & cliffs always need it
            foreach (int t in order) { if (pal.Count >= PaletteSize) break; if (t != (int)TerrainType.Rocky && total[t] > 0.5f) pal.Add(t); }
            while (pal.Count < PaletteSize) pal.Add(-1);
            for (int t = 0; t < TypeCount; t++) slotOf[t] = -1;
            for (int k = 0; k < PaletteSize; k++) if (pal[k] >= 0) slotOf[pal[k]] = k;
            palette = pal.ToArray();
        }

        static float Lum(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        static float Sat(Color c) { float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)); return mx > 1e-4f ? (mx - mn) / mx : 0f; }
        /// c re-scaled to have luminance L (keeps its hue — how local materials inherit the orbital brightness).
        static Color WithLum(Color c, float L) { float l = Mathf.Max(Lum(c), 0.004f); Color o = c * (L / l); o.a = 1f; return o; }

        /// Ground albedo + MATERIAL WEIGHTS (x rock, y sand/regolith, z snow/ice, w soil/vegetated) for the terrain
        /// shader, which gives each material its own micro-relief, grain and specular response. The ORBITAL colour
        /// is the anchor: every local material is derived from it (hue-shifted / brightness-scaled), so a dark
        /// carbon world stays dark up close and ice seen from orbit is ice on the ground. alpha = wetness.
        public Color Ground(in Sample sm, in LocalClimate lc, float slope01, Color orbital, out Vector4 mat, out Vector4 mat2)
        {
            var sc = tsScores ??= new float[TypeCount];
            System.Array.Clear(sc, 0, TypeCount);
            float volc = s.Volcanism;
            bool airy = s.HasAtmo;
            var theme = s.Palette.theme;
            var pal = s.Palette;
            // green seen from orbit is the PLANTS, not the dirt: strip it out of the ground colour so only the
            // grassy / mossy types carry vegetation colour (blended back in by their weight at the end)
            Color orbitalRaw = orbital;
            float vegHue = 0f;
            if (pal.vegAmt > 0.01f)
            {
                Color co0 = orbital / Mathf.Max(orbital.r + orbital.g + orbital.b, 0.02f);
                Color cv0 = pal.veg / Mathf.Max(pal.veg.r + pal.veg.g + pal.veg.b, 0.02f);
                Color d0 = co0 - cv0;
                vegHue = Mathf.Clamp01(1f - Mathf.Sqrt(d0.r * d0.r + d0.g * d0.g + d0.b * d0.b) * 4f);
                float l0 = Lum(orbital);
                orbital = WithLum(Color.Lerp(orbital, pal.landLow, vegHue * 0.9f), l0); orbital.a = 1f;
            }
            float oL = Mathf.Max(Lum(orbital), 0.01f), oS = Sat(orbital);
            Color region = Region(sm);
            float midL = Mathf.Max(Lum(Color.Lerp(pal.landLow, pal.landHigh, 0.5f)), 0.02f);
            // regional geology as RELATIVE brightness + a partial hue shift, around the orbital colour
            float rel = Mathf.Clamp(Lum(region) / midL, 0.55f, 1.6f);
            // bright, colourless orbital pixels (frost, salt, ice) keep their own colour — no palette hue creeping in
            float white = SS(0.45f, 0.75f, oL) * (1f - SS(0.1f, 0.3f, oS));
            Color bed = Color.Lerp(orbital, WithLum(region, oL), 0.35f * (1f - white)) * Mathf.Lerp(rel, 1f, white); bed.a = 1f;

            // materials derived from the local bedrock / orbital colour
            Color rock = WithLum(Color.Lerp(bed, pal.landHigh, 0.2f * (1f - white)), oL * Mathf.Lerp(0.82f, 0.95f, white));
            Color sand = WithLum(Color.Lerp(orbital, pal.landHigh, 0.12f), Mathf.Min(oL * 1.15f + 0.01f, 0.9f));
            Color soil = WithLum(Color.Lerp(bed, pal.landLow, 0.4f * (1f - white)), oL * Mathf.Lerp(0.72f, 0.92f, white));
            // snow/ice takes the ORBITAL colour where the map is already icy (a carbon world's frost isn't the palette ice)
            Color snow = Color.Lerp(pal.ice, orbital, SS(0.45f, 0.75f, oL) * 0.8f); snow = WithLum(snow, Mathf.Max(oL, Lum(pal.ice) * 0.9f)); snow.a = 1f;

            float wR, wS, wI, wV, wet = 0f, crack = 0f, patchLush = 0.5f, patchCanopy = 0f;
            if (sm.underwater)
            {
                wR = 0.2f; wS = 0.8f; wI = 0f; wV = 0f; wet = 1f;
                sc[(int)TerrainType.Sandy] = 1f; sc[(int)TerrainType.FineSoil] = 0.6f; sc[(int)TerrainType.Gravel] = 0.2f;
            }
            else
            {
                // how icy the orbital pixel itself is: bright and colourless
                float iceO = SS(0.5f, 0.78f, oL) * (1f - SS(0.12f, 0.35f, oS));
                bool vegBiome = false; float cover = 0f;
                switch (lc.biome)
                {
                    case Biome.Ice: iceO = Mathf.Max(iceO, 0.9f); break;
                    case Biome.Desert: cover = 0.2f; break;
                    case Biome.ColdDesert: cover = 0.1f; break;
                    case Biome.Tundra: vegBiome = true; cover = 0.45f; break;
                    case Biome.Grassland: case Biome.Savanna: vegBiome = true; cover = 0.7f; break;
                    case Biome.Boreal: case Biome.Wetland:
                    case Biome.TemperateForest: case Biome.TemperateRainforest:
                    case Biome.TropicalSeasonal: case Biome.TropicalRainforest: vegBiome = true; cover = 0.85f; break;
                }
                // the orbital map is the authority on vegetation: green seen from space is growth on the ground
                float vegO = 0f;
                if (pal.vegAmt > 0.01f)
                {
                    Color co = orbitalRaw / Mathf.Max(orbitalRaw.r + orbitalRaw.g + orbitalRaw.b, 0.02f);
                    Color cv = pal.veg / Mathf.Max(pal.veg.r + pal.veg.g + pal.veg.b, 0.02f);
                    Color dv = co - cv;
                    vegO = Mathf.Clamp01(1f - Mathf.Sqrt(dv.r * dv.r + dv.g * dv.g + dv.b * dv.b) * 6f) * (1f - SS(0.55f, 0.8f, oL));
                    if (vegO > 0.3f) { vegBiome = true; cover = Mathf.Max(cover, vegO); }
                }
                float steep = SS(0.4f, 0.75f, slope01);
                float dry = 1f - sm.moist;
                wR = 0.25f + steep * 2.5f + sm.mountain * 0.6f + sm.strata * 0.6f + sm.crater * 0.2f + (vegBiome ? 0f : 0.25f);
                wS = sm.dune * 2.5f + (vegBiome ? 0.05f : 0.08f + dry * 0.25f) * (1f - steep)
                   + ((lc.biome == Biome.Desert || lc.biome == Biome.ColdDesert) ? 0.45f : 0f);   // sand only where it collects
                if (hasSea && sm.heightM < 3f) wS += 2f * (1f - Mathf.Clamp01(sm.heightM / 3f));   // beaches
                wV = vegBiome ? cover * 1.6f * (1f - steep) : 0f;
                float climSnow = lc.biome != Biome.Desert ? SS(-8f, -22f, lc.tMinC) : 0f;
                bool frozenWorld = planet.type == PlanetType.FrozenRock || planet.meanTempC < -30f;
                if (frozenWorld) iceO = Mathf.Max(iceO, SS(0.35f, 0.6f, oL) * (1f - SS(0.2f, 0.45f, oS)));
                wI = Mathf.Max(iceO, climSnow * SS(0.42f, 0.68f, oL)) * (frozenWorld ? 4f : 2.5f) * (1f - steep * 0.8f);
                wet = Mathf.Clamp01(sm.valley * 0.8f + (lc.biome == Biome.Wetland ? 0.6f : 0f)
                                    + (hasSea && sm.heightM < 1.5f ? 1f - sm.heightM / 1.5f : 0f));
                // DRY LAKE BEDS (playas, mineral pans): mud-cracked flats, only where it is flat, dry, bare and not frozen
                float playa = SS(0.58f, 0.7f, N(sm.fx, sm.fz, 5200f, 131f));
                float pan = SS(0.66f, 0.8f, 1f - N(sm.fx, sm.fz, 2600f, 103f)) * Mathf.Clamp01(s.Mineral * 1.4f + 0.2f);
                crack = Mathf.Max(playa, pan) * SS(0.55f, 0.8f, dry) * (1f - SS(0.12f, 0.3f, slope01))
                      * (vegBiome ? 0f : 1f) * (1f - Mathf.Clamp01(iceO * 2f)) * (lc.tMaxC > 0f ? 1f : 0f) * (1f - wet);
                if (crack > 0.01f) wS += crack * 2f;

                // PATCHINESS — the ground is a mosaic, not one material: meadows and bare openings, rock outcrops,
                // drifts of sand, at several scales (km stands → 100 m clearings)
                float pA = N(sm.fx, sm.fz, 1400f, 141f), pB = N(sm.fx, sm.fz, 380f, 143f), pC = N(sm.fx, sm.fz, 95f, 149f);
                float stand = SS(0.3f, 0.7f, pA * 0.6f + pB * 0.4f);
                wV *= Mathf.Lerp(0.2f, 1.6f, stand);
                float outcrop = SS(0.64f, 0.78f, pB * 0.7f + pC * 0.3f);
                wR += outcrop * (vegBiome ? 1.4f : 0.8f);
                wS += SS(0.66f, 0.8f, pA) * 0.5f * dry * (1f - outcrop);
                patchLush = Mathf.Clamp01(sm.moist * 0.7f + (pB - 0.5f) * 0.8f + (pC - 0.5f) * 0.4f + sm.valley * 0.5f);
                patchCanopy = vegBiome ? Mathf.Clamp01(cover - 0.6f) * 2.5f * stand : 0f;

                // ── terrain-type scores ──
                float flat = 1f - SS(0.12f, 0.35f, slope01);
                float midSlope = SS(0.22f, 0.42f, slope01) * (1f - SS(0.65f, 0.85f, slope01));
                float bare = vegBiome ? 1f - cover * stand : 1f;
                float regionA = N(sm.fx, sm.fz, 6000f, 211f), regionB = N(sm.fx, sm.fz, 2200f, 223f);
                bool cold = lc.tMaxC < 8f;
                float warm = lc.tMaxC > 0f ? 1f : 0f;
                sc[(int)TerrainType.Rocky]    = 0.3f + steep * 1.6f + sm.mountain * 0.8f + sm.strata * 0.8f;
                sc[(int)TerrainType.Scree]    = SS(0.42f, 0.68f, slope01) * (0.4f + sm.mountain) * 1.1f;
                sc[(int)TerrainType.Bouldery] = 0f;   // boulder fields are real rock meshes (SurfaceRocks uses Boulderiness), not a texture
                sc[(int)TerrainType.Gravel]   = (dry * 0.45f * (1f - sm.dune) + sm.valley * 0.9f) * bare * flat;
                sc[(int)TerrainType.Sandy]    = (vegBiome ? 0.08f : 0.25f + dry * 0.35f) * flat
                                              + ((lc.biome == Biome.Desert || lc.biome == Biome.ColdDesert) ? 0.5f : 0f)
                                              + (hasSea && sm.heightM < 3f ? 2.5f * (1f - Mathf.Clamp01(sm.heightM / 3f)) : 0f);
                sc[(int)TerrainType.Dunes]    = sm.dune * 3f;
                sc[(int)TerrainType.FineSoil] = ((vegBiome ? 0.5f : 0.15f) + sm.moist * 0.35f) * flat * (1f - iceO);
                float lakeBed = playa * SS(0.55f, 0.8f, dry) * flat * bare * warm * (1f - wet);
                sc[(int)TerrainType.DriedLake] = lakeBed * 2.2f * SS(0.72f, 0.85f, dry);                          // long-dry: smooth hardpan
                sc[(int)TerrainType.Cracked]   = lakeBed * 2.2f * (1f - SS(0.72f, 0.85f, dry)) + sm.valley * dry * 0.4f * bare;   // recently wet: deep cracks
                sc[(int)TerrainType.SaltFlat]  = pan * SS(0.55f, 0.8f, dry) * flat * bare * warm * 2f * SS(0.35f, 0.6f, oL)
                                               * ((theme == PlanetTexture.ChemTheme.Evaporite || theme == PlanetTexture.ChemTheme.Halide) ? 2f : 1f);
                sc[(int)TerrainType.MetallicRock] = theme == PlanetTexture.ChemTheme.Metallic ? 1.4f * (0.5f + outcrop + steep)
                                                  : (theme == PlanetTexture.ChemTheme.Ferrous || theme == PlanetTexture.ChemTheme.Cupric)
                                                    ? SS(0.62f, 0.78f, regionA) * (outcrop + steep) * 1.2f : 0f;
                float lavaField = SS(0.55f, 0.7f, regionB);
                sc[(int)TerrainType.Molten] = theme == PlanetTexture.ChemTheme.Lava ? lavaField * 2.2f
                                            : (volc > 0.8f && lc.tMaxC > 300f ? lavaField * volc : 0f);
                sc[(int)TerrainType.BasaltFlow] = (theme == PlanetTexture.ChemTheme.Basaltic || theme == PlanetTexture.ChemTheme.Lava) ? 1.1f * flat + 0.3f
                                                : volc * SS(0.58f, 0.72f, regionA) * 1.3f * bare;
                sc[(int)TerrainType.Sulfur] = theme == PlanetTexture.ChemTheme.Sulfuric ? 1.6f * (0.4f + flat)
                                            : volc > 0.65f ? SS(0.72f, 0.82f, regionB) * volc * bare : 0f;
                // WHITE types need EVIDENCE: the orbital map must actually be pale & colourless there (cold alone isn't
                // enough — a dry, cold, rust-brown world has no snow or ice to show). The map is the authority.
                float whiteEvidence = SS(0.42f, 0.68f, oL) * (1f - SS(0.15f, 0.4f, oS));
                bool volatiles = s.WaterCov > 0.02f || frozenWorld || theme == PlanetTexture.ChemTheme.Snowball
                               || theme == PlanetTexture.ChemTheme.AmmoniaIce || theme == PlanetTexture.ChemTheme.SalineIce
                               || theme == PlanetTexture.ChemTheme.Methanic;
                float iceOk = whiteEvidence * (volatiles ? 1f : 0.15f);
                sc[(int)TerrainType.IceSheet] = iceO * iceOk * flat * (frozenWorld || lc.biome == Biome.Ice ? 2.4f : 1.2f);
                sc[(int)TerrainType.Snow]     = (climSnow * 1.5f + 0.5f) * iceOk * 2f * (1f - steep * 0.7f);
                sc[(int)TerrainType.Frost]    = ((frozenWorld ? (steep + sm.mountain) * 1.5f : 0f) + SS(-40f, -90f, lc.tMinC) * 0.8f * (1f - flat * 0.5f))
                                              * Mathf.Max(iceOk, whiteEvidence * 0.3f);
                sc[(int)TerrainType.Grassy]   = vegBiome ? cover * stand * 2.4f * (1f - steep) * SS(-5f, 8f, lc.tMeanC) : 0f;
                sc[(int)TerrainType.Mossy]    = vegBiome ? cover * (cold || sm.moist > 0.72f ? 1.1f : 0.25f) * (1f - steep * 0.5f) : 0f;
                sc[(int)TerrainType.PatternedGround] = (lc.tMaxC < 10f && lc.tMinC < -15f && sm.moist > 0.3f) ? flat * 1.3f * bare : 0f;
                sc[(int)TerrainType.Regolith] = !airy ? 1.4f * (1f - steep) : 0f;
                // big landforms speak for themselves
                sc[(int)TerrainType.IceSheet] += sm.glacier * 3.5f;
                sc[(int)TerrainType.BasaltFlow] += sm.volcano * volc * 2.2f;
                sc[(int)TerrainType.Molten] += SS(0.85f, 1f, sm.volcano) * SS(0.7f, 0.95f, volc) * (theme == PlanetTexture.ChemTheme.Lava ? 3f : 1.2f);
                sc[(int)TerrainType.Scree] += sm.chasm * 1.5f;
                sc[(int)TerrainType.Gravel] += sm.valley * (hydro != null && !hydro.wet ? 0.8f : 0f);   // dry riverbeds
                // the category's signature ground
                switch (category)
                {
                    case PlanetCategory.Obsidian: case PlanetCategory.BasaltPlains: sc[(int)TerrainType.BasaltFlow] += 1.6f; break;
                    case PlanetCategory.LavaOcean: case PlanetCategory.GlassRain: case PlanetCategory.SilicateVapor:
                    case PlanetCategory.PostGiantImpact: case PlanetCategory.CarbonLava: sc[(int)TerrainType.Molten] += 1.4f; sc[(int)TerrainType.BasaltFlow] += 0.8f; break;
                    case PlanetCategory.Sulfur: case PlanetCategory.HellMoon: sc[(int)TerrainType.Sulfur] += 1.5f; break;
                    case PlanetCategory.SaltFlat: case PlanetCategory.Halide: sc[(int)TerrainType.SaltFlat] += 1.2f * flat; break;
                    case PlanetCategory.IronWorld: case PlanetCategory.SuperMercury: case PlanetCategory.IronSnow: sc[(int)TerrainType.MetallicRock] += 1.4f; break;
                    case PlanetCategory.CrateredDead: case PlanetCategory.RegolithDust: case PlanetCategory.FlareScoured: sc[(int)TerrainType.Regolith] += 1.2f; break;
                    case PlanetCategory.Glacier: case PlanetCategory.Snowball: case PlanetCategory.EuropaType: case PlanetCategory.EnceladusType:
                    case PlanetCategory.NitrogenIce: case PlanetCategory.DryIce: sc[(int)TerrainType.IceSheet] += 1.2f * flat; sc[(int)TerrainType.Snow] += 0.6f; break;
                    case PlanetCategory.FrostDesert: sc[(int)TerrainType.Frost] += 1.4f; break;
                    case PlanetCategory.Tar: case PlanetCategory.Carbon: sc[(int)TerrainType.FineSoil] += 1f; break;
                    case PlanetCategory.Canyon: case PlanetCategory.Mesa: sc[(int)TerrainType.Rocky] += 0.6f; sc[(int)TerrainType.Scree] += 0.5f; break;
                    case PlanetCategory.ClayShelf: sc[(int)TerrainType.DriedLake] += 0.9f * flat; sc[(int)TerrainType.Cracked] += 0.6f * flat; break;
                    case PlanetCategory.Desert: case PlanetCategory.OchreIron: sc[(int)TerrainType.Dunes] += 0.6f; sc[(int)TerrainType.Sandy] += 0.5f; break;
                    case PlanetCategory.FungalLichen: case PlanetCategory.MicrobialMat: sc[(int)TerrainType.Mossy] += 1.2f; break;
                    case PlanetCategory.Steppe: sc[(int)TerrainType.Grassy] += 1f; break;
                }
                // the geological province's ground
                float pw = sm.provW;
                switch ((Province)sm.province)
                {
                    case Province.FoldBelt: sc[(int)TerrainType.Rocky] += 0.5f * pw; sc[(int)TerrainType.Scree] += 0.4f * pw; break;
                    case Province.Rift: sc[(int)TerrainType.Scree] += 0.4f * pw; sc[(int)TerrainType.BasaltFlow] += 0.3f * pw; break;
                    case Province.VolcanicField: sc[(int)TerrainType.BasaltFlow] += 0.9f * pw; break;
                    case Province.FloodBasalt: sc[(int)TerrainType.BasaltFlow] += 0.6f * pw; sc[(int)TerrainType.Rocky] += 0.3f * pw; break;
                    case Province.Badlands: sc[(int)TerrainType.Cracked] += 0.5f * pw; sc[(int)TerrainType.Sandy] += 0.4f * pw; sc[(int)TerrainType.Grassy] *= 1f - 0.6f * pw; break;
                    case Province.Glaciated: sc[(int)TerrainType.Gravel] += 0.5f * pw; sc[(int)TerrainType.Mossy] += 0.4f * pw; break;
                    case Province.Karst: sc[(int)TerrainType.Rocky] += 0.35f * pw; break;
                    case Province.DuneSea: sc[(int)TerrainType.Dunes] += 0.9f * pw; sc[(int)TerrainType.Grassy] *= 1f - 0.8f * pw; break;
                    case Province.ImpactBasin: sc[(int)TerrainType.Gravel] += 0.3f * pw; break;
                    case Province.Lowland: sc[(int)TerrainType.FineSoil] += 0.3f * pw; break;
                }
            }
            // CLIFFS: nothing but bare rock holds on very steep ground — no grass, soil, sand or moss on a rock face
            if (!sm.underwater)
            {
                float cliff = SS(0.45f, 0.8f, slope01);
                if (cliff > 0f)
                {
                    for (int k = 0; k < TypeCount; k++)
                    {
                        var tt = (TerrainType)k;
                        bool rocky = tt == TerrainType.Rocky || tt == TerrainType.Scree || tt == TerrainType.Bouldery || tt == TerrainType.BasaltFlow
                                  || tt == TerrainType.MetallicRock || tt == TerrainType.Molten || tt == TerrainType.IceSheet;
                        if (!rocky) sc[k] *= 1f - cliff * 0.97f;
                    }
                    sc[(int)TerrainType.Rocky] += cliff * 3f;
                    wV *= 1f - cliff; wS *= 1f - cliff; wR += cliff * 3f;
                }
            }
            float sum = wR + wS + wI + wV;
            mat = new Vector4(wR, wS, wI, wV) / Mathf.Max(sum, 1e-4f);

            // soil / vegetated ground varies: sun-cured straw where dry, deep saturated growth where lush, dark
            // leaf-litter under closed canopy
            {
                Color straw = WithLum(Color.Lerp(soil, sand, 0.55f) + new Color(0.06f, 0.04f, -0.04f), Mathf.Min(Lum(soil) * 1.35f + 0.02f, 0.85f));
                Color lush = soil * 0.78f; lush = Color.Lerp(lush, WithLum(lush, Lum(lush)) * 1.1f - new Color(0.02f, 0f, 0.02f) * 0.5f, 0.5f);
                Color litter = WithLum(Color.Lerp(soil, rock, 0.35f), Lum(soil) * 0.55f);
                Color v = Color.Lerp(straw, lush, SS(0.25f, 0.75f, patchLush));
                soil = Color.Lerp(v, litter, patchCanopy);
                soil.r = Mathf.Max(soil.r, 0f); soil.g = Mathf.Max(soil.g, 0f); soil.b = Mathf.Max(soil.b, 0f);
            }
            Color soilBase = soil;
            Color c = rock * mat.x + sand * mat.y + snow * mat.z + soil * mat.w;
            // landform tints
            // (crater floors are NOT darker — that painted flat dark discs where erosion had left no relief)
            float band = Mathf.Sin(sm.heightM * 0.35f) * 0.5f + 0.5f;
            c = Color.Lerp(c, c * (0.8f + band * 0.4f), sm.strata);
            if (sm.underwater) c = Color.Lerp(c, pal.oceanDeep, Mathf.Clamp01(-sm.heightM / 60f));
            c = Color.Lerp(c, c * 0.7f, wet * 0.5f);   // damp ground is darker
            c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b);
            c = Color.Lerp(c, WithLum(c, Mathf.Min(Lum(c) * 1.12f, 0.9f)), crack * 0.6f);   // sun-bleached crust
            c.a = wet;   // alpha = wetness

            // palette weights: sharpen (a few types dominate a spot), keep what's in this world's palette
            mat = Vector4.zero; mat2 = Vector4.zero;
            if (palette != null)
            {
                float tot = 0f;
                var wv = tsW ??= new float[PaletteSize]; System.Array.Clear(wv, 0, PaletteSize);
                for (int k = 0; k < PaletteSize; k++)
                {
                    int t = palette[k]; if (t < 0) continue;
                    float v = sc[t]; v = v * v * v;
                    wv[k] = v; tot += v;
                }
                if (tot <= 1e-6f) { wv[0] = 1f; tot = 1f; }
                for (int k = 0; k < PaletteSize; k++) { float v = wv[k] / tot; if (v < 0.04f) v = 0f; wv[k] = v; }
                tot = 0f; for (int k = 0; k < PaletteSize; k++) tot += wv[k];
                for (int k = 0; k < PaletteSize; k++) wv[k] /= Mathf.Max(tot, 1e-6f);
                mat = new Vector4(wv[0], wv[1], wv[2], wv[3]); mat2 = new Vector4(wv[4], wv[5], wv[6], wv[7]);
                // vegetation colour only where grass / moss actually grows
                float vegShare = 0f;
                for (int k = 0; k < PaletteSize; k++)
                    if (palette[k] == (int)TerrainType.Grassy || palette[k] == (int)TerrainType.Mossy) vegShare += wv[k];
                if (vegShare > 0f && pal.vegAmt > 0.01f)
                {
                    float wa = c.a;
                    Color vc = WithLum(Color.Lerp(pal.veg, Color.Lerp(pal.veg, soilBase, 0.35f), 1f - patchLush), Mathf.Max(Lum(orbitalRaw) * 0.85f, 0.04f));
                    c = Color.Lerp(c, vc, Mathf.Clamp01(vegShare * 1.1f)); c.a = wa;
                }
            }
            return c;
        }
    }
}
