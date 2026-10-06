using System.Collections.Generic;
using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>Knobs for one generated system (the viewer exposes these on its UI).</summary>
    public struct SystemGenParams
    {
        public int starCount;          // 1–3 (primary + companions)
        public int planetCount;        // ≤0 → random 3–7
        public float maxInclinationDeg;
        public SpectralClass? forceClass;

        // --- Optional inference biases (fed from a star's galaxy context; see StarInference). ---
        public float metallicityOverride;  // [Fe/H] to use instead of rolling; NaN = roll normally
        public float youthBias;            // 0..1 → younger age + stronger flares/volcanism (near nebulae / arms)
        public float habitabilityBoost;    // multiplier on terrestrial odds & habitability (metal-rich → more)

        public static SystemGenParams Default => new SystemGenParams
        {
            starCount = 1, planetCount = 0, maxInclinationDeg = 18f, forceClass = null,
            metallicityOverride = float.NaN, youthBias = 0f, habitabilityBoost = 1f,
        };
    }

    /// <summary>
    /// Procedurally generates star systems: a primary star (spectral class by real IMF abundance → mass →
    /// all derived physics), optional companion stars, then planets on inclined Keplerian orbits with types
    /// set by the snow line, then folds the star's habitability constraints onto each world. Deterministic.
    /// </summary>
    public static class SystemGenerator
    {
        static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };

        public static StarSystem Generate(ulong seed, SpectralClass? forceClass = null)
        {
            var d = SystemGenParams.Default; d.forceClass = forceClass;
            return Generate(seed, d);
        }

        public static StarSystem Generate(ulong seed, SystemGenParams p)
        {
            if (p.habitabilityBoost <= 0f) p.habitabilityBoost = 1f;   // 0 = an un-set struct; treat as neutral
            var rng = new DetRng(seed);
            var sys = new StarSystem { seed = seed };
            sys.star = GenerateStar(ref rng, p.forceClass ?? RollClass(ref rng), p);
            sys.star.properName = NameGen.Star(seed);
            GenerateCompanions(ref rng, sys, p);
            // Companion stars are named as bright secondaries of the primary: "Delfor B", "Delfor C".
            for (int ci = 0; ci < sys.companions.Count; ci++)
                sys.companions[ci].properName = $"{sys.star.properName} {(char)('B' + ci)}";
            GeneratePlanets(ref rng, sys, p);
            GenerateMoons(ref rng, sys);
            GenerateBelts(ref rng, sys);
            ApplyCrossBodyFactors(sys);   // bombardment (belts) + tidal heating (neighbour giants) → surface diversity
            return sys;
        }

        // Diversity that depends on a body's PLACE among its neighbours, not just its own composition: how heavily
        // it's been cratered (proximity to a debris belt + system youth), and how much it's flexed by tides
        // (eccentric orbit + a massive neighbour near resonance + closeness to the star). These feed the texture
        // generator (ejecta blankets, impact cratering, fractured/molten crust, raised volcanism).
        static void ApplyCrossBodyFactors(StarSystem sys)
        {
            float youth = Mathf.Clamp01(1f - sys.star.stellarAgeGyr / 6f);   // young systems are still being battered
            for (int i = 0; i < sys.planets.Count; i++)
            {
                var p = sys.planets[i];

                float bomb = youth * 0.35f;
                if (sys.belts != null)
                    foreach (var belt in sys.belts)
                    {
                        float mid = (belt.innerAU + belt.outerAU) * 0.5f;
                        float d = Mathf.Abs(p.semiMajorAxisAU - mid) / Mathf.Max(mid, 0.1f);
                        bomb += Mathf.Clamp01(1f - d * 2f) * 0.6f;           // just inside/outside a belt → pelted
                    }
                p.bombardment = Mathf.Clamp01(bomb);

                float tide = p.orbit.eccentricity * 1.6f;
                for (int j = 0; j < sys.planets.Count; j++)
                {
                    if (j == i) continue;
                    var q = sys.planets[j];
                    if (q.hostStar != p.hostStar) continue;
                    if (q.type != PlanetType.GasGiant && q.type != PlanetType.IceGiant) continue;
                    float hi = Mathf.Max(p.semiMajorAxisAU, q.semiMajorAxisAU);
                    float lo = Mathf.Max(1e-3f, Mathf.Min(p.semiMajorAxisAU, q.semiMajorAxisAU));
                    float ratio = hi / lo;
                    if (ratio < 2.2f) tide += (2.2f - ratio) * 0.35f;        // adjacent giant → resonant flexing
                }
                if (p.semiMajorAxisAU < 0.2f) tide += 0.3f;                   // deep in the star's gravity well
                p.tidalHeat = Mathf.Clamp01(tide);
                if (p.Rocky) p.volcanism = Mathf.Clamp01(Mathf.Max(p.volcanism, p.tidalHeat * 0.9f));   // Io-like heating
            }
        }

        /// <summary>
        /// A single-planet system built from explicit parameters (the PLANET EDITOR). Uses the same physics as the
        /// galaxy generator — insolation from every star, equilibrium + greenhouse temperature, habitability, tidal
        /// locking, chemistry, moons — but any value the spec sets is forced instead of rolled.
        /// </summary>
        public static StarSystem GenerateCustom(PlanetSpec spec)
        {
            static bool Auto(float v) => float.IsNaN(v);
            var gp = SystemGenParams.Default;
            var rng = new DetRng(spec.seed == 0UL ? 1UL : spec.seed);
            var sys = new StarSystem { seed = spec.seed };

            // star: mass inside the chosen class, age as a fraction of its life, metallicity
            var info = Astrophysics.Classes[spec.starClass];
            float M = Mathf.Exp(Mathf.Lerp(Mathf.Log(info.massLo), Mathf.Log(info.massHi), Mathf.Clamp01(spec.starMassT)));
            float life = Astrophysics.LifetimeGyr(M, Astrophysics.MassToLuminosity(M));
            float age = Mathf.Max(0.01f, Mathf.Clamp01(spec.ageFrac) * Mathf.Min(life, 13.6f));
            sys.star = StarFromMass(M, age, spec.metallicity, gp);
            sys.star.properName = NameGen.Star(spec.seed);
            var A = sys.star;

            // optional companion
            float L = A.luminosity, Mhost = A.stellarMass, extFlux = 0f, extTemp = 0f; int host = 0;
            sys.binary = BinaryKind.Single;
            if (spec.binary)
            {
                float sep = Mathf.Max(0.03f, spec.companionSepAU);
                var B = StarFromMass(Mathf.Max(0.08f, M * Mathf.Clamp(spec.companionMassRatio, 0.05f, 1f)), age, spec.metallicity, gp);
                B.isCompanion = true; B.properName = A.properName + " B";
                B.orbit = new OrbitElements { semiMajorAxisAU = sep, eccentricity = sep < 0.6f ? 0.05f : 0.2f, inclinationDeg = 3f,
                                              ascendingNodeDeg = rng.Range(0f, 360f), argPeriapsisDeg = rng.Range(0f, 360f), meanAnomalyDeg = rng.Range(0f, 360f) };
                sys.companions.Add(B);
                sys.binary = sep < 0.6f ? BinaryKind.Close : sep < 9f ? BinaryKind.Intermediate : BinaryKind.Wide;
                if (sys.binary == BinaryKind.Close) { L += B.luminosity; Mhost += B.stellarMass; host = -1; }
                else { extFlux = B.luminosity / (sep * sep); extTemp = B.effectiveTemp; }
            }

            // planet
            var type = spec.type;
            bool rocky = PlanetData.IsRocky(type);
            float d = Mathf.Max(0.005f, spec.distanceAU);
            float mass = Mathf.Max(0.01f, spec.massEarth);
            float S = L / (d * d) + extFlux;
            Astrophysics.HabitableZone(L, out float hzIn, out float hzOut);
            bool inHZ = S <= L / Mathf.Pow(hzIn * 0.75f, 2f) && S >= L / Mathf.Pow(hzOut * 1.25f, 2f);
            float albedo = !Auto(spec.albedo) ? spec.albedo
                         : (type == PlanetType.FrozenRock || type == PlanetType.IceGiant) ? 0.6f : 0.3f;
            float green = !Auto(spec.greenhouseK) ? spec.greenhouseK
                        : rocky ? Mathf.Clamp01(Mathf.Log(mass + 1f) / 1.6f) * 33f * Mathf.Clamp01(S / 1.2f) : 0f;
            if (Auto(spec.greenhouseK) && rocky && (!Auto(spec.pressureBar) || spec.atmoPreset >= 0))
            {
                // warming from the column of absorbers: √P × (baseline + CO₂ + CH₄ + H₂O); Mars ≈ 5 K, Earth ≈ 30 K, Venus ≈ 500 K
                float P = !Auto(spec.pressureBar) ? spec.pressureBar : 1f;
                var gas = spec.atmoPreset >= 0 ? AtmoComposition.Preset(spec.atmoPreset).Normalised() : new AtmoComposition { n2 = 0.78f, o2 = 0.21f, h2o = 0.01f };
                green = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(P, 0f)) * (25f + 55f * gas.co2 + 40f * gas.ch4 + 120f * gas.h2o + 30f * gas.h2 * Mathf.Min(P, 10f) / 10f), 0f, 520f);
            }
            float Teq = 278.5f * Mathf.Pow(Mathf.Max(S, 1e-6f) * (1f - albedo), 0.25f);
            float Tc = Teq + green - 273.15f;
            float radius = !Auto(spec.radiusEarth) ? spec.radiusEarth
                         : rocky ? Mathf.Pow(mass, 0.27f)
                         : type == PlanetType.GasGiant ? Mathf.Clamp(11.2f * Mathf.Pow(mass / 318f, -0.04f), 8f, 14f)
                         : Mathf.Clamp(3.9f * Mathf.Pow(mass / 17f, 0.3f), 2f, 6f);
            float hostT = A.effectiveTemp;
            if (host < 0)
            {
                var B = sys.companions[0];
                hostT = Mathf.Pow((A.luminosity * Mathf.Pow(A.effectiveTemp, 4f) + B.luminosity * Mathf.Pow(B.effectiveTemp, 4f)) / L, 0.25f);
            }

            var p = new PlanetData
            {
                index = 0, name = A.properName + "-b", type = type, hostStarTempK = hostT,
                hostStar = host, secondaryFlux = extFlux, secondaryStarTempK = extTemp,
                colloquial = NameGen.Colloquial(DetRng.Hash(spec.seed, 13UL)),
                semiMajorAxisAU = d, mass = mass, albedo = albedo, insolation = S, greenhouseK = green, meanTempC = Tc, inHZ = inHZ,
                axialTiltDeg = spec.axialTiltDeg, rotationHours = Mathf.Max(0.5f, spec.rotationHours), radiusEarth = radius,
                orbit = new OrbitElements { semiMajorAxisAU = d, eccentricity = Mathf.Clamp(spec.eccentricity, 0f, 0.9f),
                                            inclinationDeg = spec.inclinationDeg, ascendingNodeDeg = rng.Range(0f, 360f),
                                            argPeriapsisDeg = rng.Range(0f, 360f), meanAnomalyDeg = rng.Range(0f, 360f) },
                themeOverride = spec.theme,
                starFlareActivity = A.flareActivity,
                pressureOverrideBar = Auto(spec.pressureBar) ? -1f : Mathf.Max(0f, spec.pressureBar),
                atmo = spec.atmoPreset >= 0 ? AtmoComposition.Preset(spec.atmoPreset) : null,
                magneticField = Auto(spec.magneticField) ? -1f : spec.magneticField,
                flareDose = Auto(spec.flareDose) ? -1f : spec.flareDose,
                liquidOverride = spec.liquid,
                redox = Auto(spec.redox) ? -1f : spec.redox,
                tectonicsOverride = spec.tectonics,
            };

            // habitability (same model as the galaxy), then the editor's override
            float stellarSuit = A.Suitability().overall;
            float tempScore = Astrophysics.Bell(Tc, 0f, 42f, 45f), massScore = Astrophysics.Bell(mass, 0.4f, 3.5f, 3.5f);
            float presScore = rocky ? Astrophysics.Bell(Mathf.Log(mass + 1f), 0.3f, 2.2f, 1.2f) : 0f;
            p.habitabilityIndex = Mathf.Clamp01((rocky ? 1f : 0f) * tempScore * massScore * presScore * stellarSuit);
            p.habClass = !rocky ? HabClass.NotViable
                       : p.habitabilityIndex >= 0.55f ? HabClass.Habitable
                       : p.habitabilityIndex >= 0.28f ? HabClass.Marginal : HabClass.Hostile;
            if (p.habClass == HabClass.Habitable && !PlanetTexture.HasAtmosphere(p)) p.habClass = HabClass.Hostile;
            if (rocky && spec.habMode == 1) { p.habClass = HabClass.Habitable; p.habitabilityIndex = Mathf.Max(p.habitabilityIndex, 0.7f); }
            if (spec.habMode == 2 && p.habClass == HabClass.Habitable) p.habClass = HabClass.Hostile;

            // composition
            p.volcanism = !Auto(spec.volcanism) ? Mathf.Clamp01(spec.volcanism)
                        : rocky ? Mathf.Clamp01(0.55f * (1f - 0.4f * A.stellarAgeGyr / Mathf.Max(1f, A.lifetimeGyr))) : 0f;
            p.mineralDiversity = !Auto(spec.mineralDiversity) ? Mathf.Clamp01(spec.mineralDiversity) : Mathf.Clamp01(0.6f * (0.6f + A.metallicity));

            // spin
            bool lockAuto = rocky && d < 0.45f * Mhost;
            p.tidallyLocked = spec.lockMode == 1 || (spec.lockMode == 0 && lockAuto);
            if (p.tidallyLocked) p.rotationHours = p.orbit.PeriodYears(Mhost) * 8766f;
            if (spec.lockMode == 3)                                     // Mercury: 3 spins per 2 orbits
            {
                p.spinResonance32 = true; p.tidallyLocked = false;
                p.rotationHours = p.orbit.PeriodYears(Mhost) * 8766f * 2f / 3f;
            }

            // water
            p.waterChemistry = spec.waterChemistry >= 0 ? (WaterChemistry)spec.waterChemistry
                             : p.habClass == HabClass.Habitable ? WaterChemistry.Clear
                             : p.volcanism > 0.6f ? WaterChemistry.Iron
                             : p.mineralDiversity > 0.6f ? WaterChemistry.Phosphate : WaterChemistry.Clear;
            p.waterCoverage = !Auto(spec.waterCoverage) ? Mathf.Clamp01(spec.waterCoverage)
                            : p.habClass == HabClass.Habitable ? 0.6f
                            : type == PlanetType.Ocean ? 0.8f
                            : (rocky && Tc > -25f && Tc < 90f ? 0.2f : 0f);

            sys.planets.Add(p);

            // moons: the generator's model, then capped / topped up to the requested count
            if (spec.moons != 0)
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    p.moons.Clear();
                    GenerateMoons(ref rng, sys);
                    if (spec.moons < 0 || p.moons.Count >= spec.moons) break;
                }
                if (spec.moons > 0 && p.moons.Count > spec.moons) p.moons.RemoveRange(spec.moons, p.moons.Count - spec.moons);
            }

            ApplyCrossBodyFactors(sys);
            if (!Auto(spec.bombardment)) p.bombardment = Mathf.Clamp01(spec.bombardment);
            if (!Auto(spec.tidalHeat)) p.tidalHeat = Mathf.Clamp01(spec.tidalHeat);
            return sys;
        }

        /// <summary>A whole stellar population — <paramref name="count"/> systems from one galaxy seed.</summary>
        public static List<StarSystem> GeneratePopulation(int count, ulong galaxySeed)
        {
            var list = new List<StarSystem>(count);
            for (int i = 0; i < count; i++) list.Add(Generate(DetRng.Hash(galaxySeed, (ulong)i)));
            return list;
        }

        /// <summary>Pick a spectral class weighted by real galactic abundance (≈76% M dwarfs, ≈8% G, etc.).</summary>
        public static SpectralClass RollClass(ref DetRng rng)
        {
            float total = 0f;
            foreach (var kv in Astrophysics.Classes) total += kv.Value.weight;
            float x = rng.Value * total, acc = 0f;
            foreach (SpectralClass c in System.Enum.GetValues(typeof(SpectralClass)))
            {
                acc += Astrophysics.Classes[c].weight;
                if (x <= acc) return c;
            }
            return SpectralClass.M;
        }

        static StarData GenerateStar(ref DetRng rng, SpectralClass cls, SystemGenParams p)
        {
            var info = Astrophysics.Classes[cls];
            float M = rng.LogRange(info.massLo, info.massHi);
            float life = Astrophysics.LifetimeGyr(M, Astrophysics.MassToLuminosity(M));
            // youthBias (near nebulae / star-forming arms) pushes the age young -> hotter, more active.
            float ageHi = Mathf.Lerp(1f, 0.28f, Mathf.Clamp01(p.youthBias));
            float age = rng.Range(0.05f, ageHi) * Mathf.Min(life, 13.6f);
            float feh = float.IsNaN(p.metallicityOverride)
                      ? Mathf.Round(rng.Range(-0.6f, 0.45f) * 100f) / 100f
                      : Mathf.Round(Mathf.Clamp(p.metallicityOverride, -1f, 0.6f) * 100f) / 100f;
            return StarFromMass(M, age, feh, p);
        }

        /// A main-sequence star of mass M (M_sun) at a given age and [Fe/H]. Companions share the primary's age and
        /// metallicity (binary stars form together from one cloud).
        static StarData StarFromMass(float M, float age, float feh, SystemGenParams p)
        {
            float L = Astrophysics.MassToLuminosity(M);
            float R = Astrophysics.MassToRadius(M);
            float T = Astrophysics.EffectiveTemp(L, R);
            float life = Astrophysics.LifetimeGyr(M, L);
            age = Mathf.Min(age, life * 0.98f);
            float youth = 1f - Mathf.Clamp01(age / life);
            float flare = Mathf.Clamp01((0.62f - M) / 0.5f) * (0.35f + 0.65f * youth); // red dwarfs, esp. young
            flare = Mathf.Clamp01(flare * (1f + 0.5f * Mathf.Clamp01(p.youthBias)));
            var d = Astrophysics.Designation(T);
            Astrophysics.HabitableZone(L, out float hin, out float hout);
            return new StarData
            {
                spectralClass = d.cls, subclass = d.sub,
                stellarMass = M, luminosity = L, radius = R, effectiveTemp = T,
                lifetimeGyr = life, stellarAgeGyr = age, flareActivity = flare,
                metallicity = feh,
                hzInnerAU = hin, hzOuterAU = hout, color = Astrophysics.BlackbodyColor(T),
            };
        }

        // Orbital stability in binaries (Holman & Wiegert 1999, fits to numerical integrations).
        /// S-type: largest stable planet orbit around ONE star, as a fraction of the binary separation.
        /// mu = companion mass / total, e = binary eccentricity.
        public static float StableSTypeFrac(float mu, float e) =>
            Mathf.Max(0f, 0.464f - 0.380f * mu - 0.631f * e + 0.586f * mu * e + 0.150f * e * e - 0.198f * mu * e * e);
        /// P-type: smallest stable CIRCUMBINARY orbit, as a multiple of the binary separation.
        public static float StablePTypeMult(float mu, float e) =>
            1.60f + 5.10f * e - 2.22f * e * e + 4.12f * mu - 4.27f * e * mu - 5.09f * mu * mu + 4.61f * e * e * mu * mu;

        static void GenerateCompanions(ref DetRng rng, StarSystem sys, SystemGenParams p)
        {
            int extra = Mathf.Clamp(p.starCount, 1, 3) - 1;
            var A = sys.star;
            sys.binary = BinaryKind.Single;
            for (int k = 0; k < extra; k++)
            {
                float a, e, q, inc;
                if (k == 0)
                {
                    // Separation from the observed (log-normal) distribution: close pairs are tidally circularised and
                    // lean toward twins; wide pairs have "thermal" eccentricities and any mass ratio.
                    float r = rng.Value;
                    if (r < 0.32f) { sys.binary = BinaryKind.Close; a = rng.LogRange(0.04f, 0.6f); e = rng.Range(0f, 0.12f); q = rng.Range(0.4f, 1f); inc = rng.Range(0f, 4f); }
                    else if (r < 0.46f) { sys.binary = BinaryKind.Intermediate; a = rng.LogRange(0.8f, 9f); e = rng.Range(0.05f, 0.5f); q = rng.Range(0.15f, 1f); inc = rng.Range(0f, 25f); }
                    else { sys.binary = BinaryKind.Wide; a = rng.LogRange(14f, 600f); e = Mathf.Sqrt(rng.Value) * 0.75f; q = rng.Range(0.1f, 1f); inc = rng.Range(0f, 60f); }
                }
                else
                {
                    // hierarchical tertiary: far outside the inner pair (triples are only stable when a_out >~ 3-5 a_in)
                    var B = sys.companions[0];
                    float inner = B.orbit.semiMajorAxisAU * (1f + B.orbit.eccentricity);
                    a = Mathf.Max(rng.LogRange(40f, 900f), inner * rng.Range(6f, 12f));
                    e = rng.Range(0f, 0.5f); q = rng.Range(0.1f, 0.9f); inc = rng.Range(0f, 70f);
                }
                var c = StarFromMass(Mathf.Max(0.08f, q * A.stellarMass), A.stellarAgeGyr, A.metallicity, p);
                c.isCompanion = true;
                c.orbit = new OrbitElements   // B: relative orbit about A; C: about the A+B barycentre
                {
                    semiMajorAxisAU = a, eccentricity = e, inclinationDeg = inc,
                    ascendingNodeDeg = rng.Range(0f, 360f), argPeriapsisDeg = rng.Range(0f, 360f), meanAnomalyDeg = rng.Range(0f, 360f),
                };
                sys.companions.Add(c);
            }
        }

        /// One region where planets can form and survive: around a single star (S-type) or around the close pair.
        struct Zone
        {
            public int host;                  // star index, or -1 = circumbinary
            public string prefix;             // name prefix for its planets
            public float L, M, T, suit, innerAU, outerAU;
            public float extFlux, extTemp;    // steady flux from the other stars (S_earth) and the brightest one's temperature
            public float countScale;
        }

        static List<Zone> PlanetZones(StarSystem sys)
        {
            var zones = new List<Zone>();
            var A = sys.star;
            if (sys.companions.Count == 0)
            {
                zones.Add(new Zone { host = 0, prefix = A.properName, L = A.luminosity, M = A.stellarMass, T = A.effectiveTemp,
                                     suit = A.Suitability().overall, innerAU = 0f, outerAU = 1e9f, countScale = 1f });
                return zones;
            }
            var B = sys.companions[0];
            float aB = B.orbit.semiMajorAxisAU, eB = B.orbit.eccentricity;
            float Mab = A.stellarMass + B.stellarMass;
            // a tertiary caps the inner system's planets and adds a faint steady flux
            float capC = 1e9f, extC = 0f, tC = 0f;
            if (sys.companions.Count > 1)
            {
                var C = sys.companions[1];
                float muC = C.stellarMass / (Mab + C.stellarMass);
                capC = C.orbit.semiMajorAxisAU * StableSTypeFrac(muC, C.orbit.eccentricity);
                extC = C.luminosity / (C.orbit.semiMajorAxisAU * C.orbit.semiMajorAxisAU); tC = C.effectiveTemp;
            }

            if (sys.binary == BinaryKind.Close)
            {
                // circumbinary disc: both stars light the planets from (nearly) the same place
                float mu = B.stellarMass / Mab;
                float aCrit = aB * StablePTypeMult(mu, eB) * 1.08f;
                float T = Mathf.Pow((A.luminosity * Mathf.Pow(A.effectiveTemp, 4f) + B.luminosity * Mathf.Pow(B.effectiveTemp, 4f)) / (A.luminosity + B.luminosity), 0.25f);
                zones.Add(new Zone { host = -1, prefix = A.properName + " AB", L = A.luminosity + B.luminosity, M = Mab, T = T,
                                     suit = Mathf.Min(A.Suitability().overall, B.Suitability().overall),
                                     innerAU = aCrit, outerAU = capC, extFlux = extC, extTemp = tC, countScale = 1f });
            }
            else
            {
                // S-type: each star keeps planets inside a fraction of the separation (tighter for eccentric pairs)
                float muA = B.stellarMass / Mab, muB = A.stellarMass / Mab;
                float outA = Mathf.Min(aB * StableSTypeFrac(muA, eB), capC);
                float outB = Mathf.Min(aB * StableSTypeFrac(muB, eB), capC);
                zones.Add(new Zone { host = 0, prefix = A.properName, L = A.luminosity, M = A.stellarMass, T = A.effectiveTemp,
                                     suit = A.Suitability().overall, innerAU = 0f, outerAU = outA,
                                     extFlux = B.luminosity / (aB * aB) + extC, extTemp = B.effectiveTemp, countScale = 1f });
                zones.Add(new Zone { host = 1, prefix = B.properName, L = B.luminosity, M = B.stellarMass, T = B.effectiveTemp,
                                     suit = B.Suitability().overall, innerAU = 0f, outerAU = outB,
                                     extFlux = A.luminosity / (aB * aB) + extC, extTemp = A.effectiveTemp, countScale = 0.7f });
            }
            // a very wide tertiary may keep planets of its own
            if (sys.companions.Count > 1)
            {
                var C = sys.companions[1];
                float muC = Mab / (Mab + C.stellarMass);
                float aC = C.orbit.semiMajorAxisAU;
                zones.Add(new Zone { host = 2, prefix = C.properName, L = C.luminosity, M = C.stellarMass, T = C.effectiveTemp,
                                     suit = C.Suitability().overall, innerAU = 0f, outerAU = aC * StableSTypeFrac(muC, C.orbit.eccentricity),
                                     extFlux = (A.luminosity + B.luminosity) / (aC * aC), extTemp = A.effectiveTemp, countScale = 0.5f });
            }
            return zones;
        }

        static void GeneratePlanets(ref DetRng rng, StarSystem sys, SystemGenParams gp)
        {
            var s0 = sys.star;   // age / metallicity are shared by every star of the system
            float boost = Mathf.Clamp(gp.habitabilityBoost, 0.3f, 2f);     // metal-rich -> richer planetary systems
            int idx = 0;
            foreach (var z in PlanetZones(sys))
            {
                int n = gp.planetCount > 0 ? Mathf.Clamp(gp.planetCount, 1, 12)
                                           : rng.RangeInt(3, 8) + (boost > 1.15f ? 1 : 0);
                n = Mathf.Max(0, Mathf.RoundToInt(n * z.countScale));
                GenerateZonePlanets(ref rng, sys, gp, z, n, s0, boost, ref idx);
            }
        }

        static void GenerateZonePlanets(ref DetRng rng, StarSystem sys, SystemGenParams gp, Zone z, int n, StarData s0, float boost, ref int idx)
        {
            float stellarSuit = z.suit;
            Astrophysics.HabitableZone(z.L, out float hzIn, out float hzOut);
            // habitable-zone and snow-line tests use the TOTAL flux received (host + other stars), in S_earth
            float sHzHot = z.L / Mathf.Pow(hzIn * 0.75f, 2f), sHzCold = z.L / Mathf.Pow(hzOut * 1.25f, 2f);
            const float SFrost = 0.137f;                                   // flux at Sol's snow line (2.7 AU)

            float d = Mathf.Max(0.28f * Mathf.Sqrt(z.L) * rng.Range(0.8f, 1.2f), z.innerAU * rng.Range(1.0f, 1.25f));
            for (int k = 0; k < n; k++)
            {
                if (k > 0) d *= rng.Range(1.4f, 1.9f);                     // geometric spacing (Titius-Bode-ish)
                if (d > z.outerAU) break;                                  // beyond the stable zone: ejected long ago
                int i = idx++;
                float Sd = z.L / (d * d) + z.extFlux;
                bool inHZ = Sd <= sHzHot && Sd >= sHzCold;

                PlanetType type;
                if (Sd < SFrost)
                    type = rng.Value < 0.6f ? (Sd < SFrost / 9f ? PlanetType.IceGiant : PlanetType.GasGiant) : PlanetType.FrozenRock;
                else
                    type = inHZ ? (rng.Value < 0.45f ? PlanetType.Ocean : PlanetType.Terrestrial)
                                : (rng.Value < 0.5f ? PlanetType.Desert : PlanetType.Terrestrial);

                bool rocky = PlanetData.IsRocky(type);
                float mass = rocky ? rng.LogRange(0.08f, 6f)
                           : type == PlanetType.GasGiant ? rng.LogRange(50f, 3000f) : rng.LogRange(10f, 50f);
                float albedo = (type == PlanetType.FrozenRock || type == PlanetType.IceGiant)
                             ? rng.Range(0.5f, 0.7f) : rng.Range(0.15f, 0.42f);
                float S = Sd;   // insolation from ALL stars                          // insolation, S⊕
                float green = rocky ? Mathf.Clamp01(Mathf.Log(mass + 1f) / 1.6f) * rng.Range(10f, 70f) * Mathf.Clamp01(S / 1.2f) : 0f;
                float Teq = 278.5f * Mathf.Pow(S * (1f - albedo), 0.25f);  // blackbody equilibrium, K
                float Tc = Teq + green - 273.15f;                          // mean surface, °C

                // Occasionally a steeply inclined orbit; mostly near the plane.
                float inc = rng.Range(0f, gp.maxInclinationDeg) * (rng.Value < 0.25f ? rng.Range(1.5f, 2.5f) : 1f);
                var orbit = new OrbitElements
                {
                    semiMajorAxisAU = d,
                    eccentricity = Mathf.Min(rng.Range(0f, 0.28f) * (rocky ? 1f : 0.6f), Mathf.Max(0f, z.outerAU / d - 1f) * 0.8f),   // whole ellipse stays in the stable zone
                    inclinationDeg = inc,
                    ascendingNodeDeg = rng.Range(0f, 360f),
                    argPeriapsisDeg = rng.Range(0f, 360f),
                    meanAnomalyDeg = rng.Range(0f, 360f),
                };

                var p = new PlanetData
                {
                    index = i, name = $"{z.prefix}-{k + 1}", hostStarTempK = z.T,
                    hostStar = z.host, secondaryFlux = z.extFlux, secondaryStarTempK = z.extTemp,
                    starFlareActivity = z.host > 0 ? sys.Star(z.host).flareActivity : sys.star.flareActivity,
                    colloquial = NameGen.Colloquial(DetRng.Hash(sys.seed, (ulong)(i * 2749u + 13u))), type = type,
                    semiMajorAxisAU = d, orbit = orbit, mass = mass, albedo = albedo, insolation = S,
                    greenhouseK = green, meanTempC = Tc, inHZ = inHZ,
                    axialTiltDeg = rng.Range(0f, 40f), rotationHours = rng.LogRange(6f, 120f),
                    radiusEarth = rocky ? Mathf.Pow(mass, 0.27f)
                                : type == PlanetType.GasGiant ? rng.Range(8f, 13f) : rng.Range(3.5f, 4.8f),
                };

                // Habitability = viable type × liquid-water temp × atmosphere-holding mass × stellar suitability.
                float typeScore = rocky ? 1f : 0f;
                float tempScore = Astrophysics.Bell(Tc, 0f, 42f, 45f);
                float massScore = Astrophysics.Bell(mass, 0.4f, 3.5f, 3.5f);
                float presScore = rocky ? Astrophysics.Bell(Mathf.Log(mass + 1f), 0.3f, 2.2f, 1.2f) : 0f;
                p.habitabilityIndex = Mathf.Clamp01(typeScore * tempScore * massScore * presScore * stellarSuit * boost);
                p.habClass = !rocky ? HabClass.NotViable
                           : p.habitabilityIndex >= 0.55f ? HabClass.Habitable
                           : p.habitabilityIndex >= 0.28f ? HabClass.Marginal : HabClass.Hostile;
                // A world with NO atmosphere cannot be habitable, however favourable its temperature/mass looked
                // (it can't hold liquid water and is sterilised by radiation). Downgrade airless "habitable" worlds.
                if (p.habClass == HabClass.Habitable && !PlanetTexture.HasAtmosphere(p)) p.habClass = HabClass.Hostile;

                // Surface chemistry → appearance (volcanism relief, clay shelves, ocean extent/tint).
                p.volcanism = rocky ? Mathf.Clamp01(rng.Range(0.15f, 1f) * (1f - 0.4f * s0.stellarAgeGyr / Mathf.Max(1f, s0.lifetimeGyr))) : 0f;
                p.mineralDiversity = Mathf.Clamp01(rng.Range(0.2f, 1f) * (0.6f + s0.metallicity));
                // Tidal locking: close-in rocky worlds (strong tides, esp. around low-mass stars) synchronise spin to
                // orbit → an "eyeball" world (hot substellar point, frozen night side). Lock radius grows with mass.
                p.tidallyLocked = rocky && d < 0.45f * z.M;
                if (p.tidallyLocked) p.rotationHours = p.orbit.PeriodYears(z.M) * 8766f;
                else
                {
                    // Spin-state diversity (roadmap §I, #84–87): the base roll only gave modest tilts and mid
                    // rotation, so fast/retrograde/extreme-obliquity worlds never appeared. Give a minority of
                    // free-spinning worlds a distinct spin class — eccentricity and youth make extremes likelier.
                    float chaos = 0.4f + p.orbit.eccentricity + Mathf.Clamp01(1f - s0.stellarAgeGyr / 6f) * 0.4f;
                    float st = rng.Value / Mathf.Max(chaos, 0.3f);
                    if (st < 0.10f) p.axialTiltDeg = rng.Range(95f, 168f);        // retrograde (obliquity > 90°)
                    else if (st < 0.24f) p.axialTiltDeg = rng.Range(45f, 82f);    // high obliquity — wandering caps
                    float sr = rng.Value / Mathf.Max(chaos, 0.3f);
                    if (sr < 0.16f) p.rotationHours = rng.Range(3f, 6f);          // fast rotator → oblate, banded
                    else if (sr < 0.24f) p.rotationHours = rng.LogRange(280f, 1400f); // slow rotator → big day/night swing
                }
                // Sea chemistry follows composition (so the colour has a REASON the story can explain): volcanic
                // worlds → iron-rust or sulfide seas; mineral-rich → phosphate teal; temperate/living → clear blue.
                if (p.habClass == HabClass.Habitable && rng.Value < 0.7f) p.waterChemistry = WaterChemistry.Clear;
                else if (p.volcanism > 0.6f) p.waterChemistry = rng.Value < 0.5f ? WaterChemistry.Iron : WaterChemistry.Sulfide;
                else if (p.mineralDiversity > 0.6f) p.waterChemistry = WaterChemistry.Phosphate;
                else p.waterChemistry = (WaterChemistry)rng.RangeInt(0, 4);
                p.waterCoverage = p.habClass == HabClass.Habitable ? rng.Range(0.45f, 0.75f)
                                : type == PlanetType.Ocean ? rng.Range(0.6f, 0.9f)
                                : (rocky && Tc > -25f && Tc < 90f ? rng.Range(0.05f, 0.35f) : 0f);

                sys.planets.Add(p);
            }
        }

        // Moons: count from host mass (giants keep many, small worlds few/none); icy beyond the frost line, and
        // close-in moons of giants get tidal heating (Io-like volcanism). Orbits are about the HOST planet (AU).
        static void GenerateMoons(ref DetRng rng, StarSystem sys)
        {
            float hs = sys.star.Suitability().overall;
            foreach (var pl in sys.planets)
            {
                bool giant = pl.type == PlanetType.GasGiant || pl.type == PlanetType.IceGiant;
                int maxM = giant ? 6 : pl.mass > 0.5f ? 2 : pl.mass > 0.15f ? 1 : 0;
                if (maxM <= 0) continue;
                int nm = rng.RangeInt(giant ? 2 : 0, maxM + 1);
                bool icy = pl.insolation < 0.137f / 0.64f;   // beyond ~0.8x the snow line (flux from all stars)
                for (int m = 0; m < nm; m++)
                {
                    float mr = rng.Range(0.03f, giant ? 0.35f : 0.22f);              // R⊕
                    if (rng.Value < 0.18f) mr = rng.Range(0.35f, giant ? 0.65f : 0.45f);   // occasional major moon (Titan/Luna-class)
                    float sep = rng.LogRange(giant ? 0.002f : 0.0008f, giant ? 0.03f : 0.006f);  // AU about host
                    float tidal = (giant && sep < 0.007f) ? rng.Range(0.4f, 1f) : rng.Range(0f, 0.3f);
                    float tc = pl.meanTempC + rng.Range(-15f, 15f) + tidal * 40f;
                    var moon = new PlanetData
                    {
                        isMoon = true, index = m, name = $"{pl.name}{(char)('a' + m)}", hostStarTempK = pl.hostStarTempK,
                        hostStar = pl.hostStar, insolation = pl.insolation, secondaryFlux = pl.secondaryFlux, secondaryStarTempK = pl.secondaryStarTempK,
                        colloquial = NameGen.Colloquial(DetRng.Hash(sys.seed, (ulong)((pl.index + 1) * 9173u + m + 501u))),
                        radiusEarth = mr, mass = Mathf.Pow(mr, 3f) * rng.Range(0.6f, 1.4f),
                        semiMajorAxisAU = pl.semiMajorAxisAU, meanTempC = tc,
                        volcanism = tidal, mineralDiversity = rng.Range(0.2f, 0.8f),
                        albedo = icy ? 0.6f : 0.22f, axialTiltDeg = rng.Range(0f, 8f), rotationHours = rng.LogRange(50f, 900f),
                        habClass = HabClass.NotViable,
                        orbit = new OrbitElements
                        {
                            semiMajorAxisAU = sep, eccentricity = rng.Range(0f, 0.05f),
                            inclinationDeg = rng.Range(0f, giant ? 25f : 8f),
                            ascendingNodeDeg = rng.Range(0f, 360f), argPeriapsisDeg = rng.Range(0f, 360f), meanAnomalyDeg = rng.Range(0f, 360f),
                        },
                    };
                    moon.type = icy ? (tc < -50f ? PlanetType.FrozenRock : PlanetType.Ocean) : PlanetType.Terrestrial;
                    moon.waterCoverage = icy ? rng.Range(0.3f, 0.9f) : (tc > -25f && tc < 60f ? rng.Range(0f, 0.2f) : 0f);
                    moon.waterChemistry = (WaterChemistry)rng.RangeInt(0, 4);

                    // Moons CAN be worlds in their own right — a massive, temperate, watery moon with a retained
                    // atmosphere (Titan/Pandora-class) may be habitable; most are airless icy/rock and are not.
                    bool mAtmo = PlanetTexture.HasAtmosphere(moon);
                    float mScore = Astrophysics.Bell(tc, 8f, 40f, 45f)
                                 * Astrophysics.Bell(moon.mass, 0.15f, 0.9f, 0.12f)   // only fairly massive moons hold up
                                 * Mathf.Clamp01(moon.waterCoverage * 2.5f)
                                 * (mAtmo ? 1f : 0f) * hs;
                    moon.habitabilityIndex = Mathf.Clamp01(mScore);
                    moon.habClass = !mAtmo ? HabClass.Hostile
                                  : moon.habitabilityIndex >= 0.50f ? HabClass.Habitable
                                  : moon.habitabilityIndex >= 0.22f ? HabClass.Marginal : HabClass.Hostile;
                    pl.moons.Add(moon);
                }
            }
        }

        // Belts: a rocky asteroid belt near the frost line (a shepherded "failed planet" gap) and an icy Kuiper belt
        // beyond the outermost planet.
        static void GenerateBelts(ref DetRng rng, StarSystem sys)
        {
            if (sys.planets.Count == 0) return;
            // Belts belong to the primary's planetary system (or to the close pair, circumbinary), and — like planets —
            // only survive inside the region the companion hasn't cleared (Holman–Wiegert).
            int host = sys.binary == BinaryKind.Close ? -1 : 0;
            float L = sys.star.luminosity + (host < 0 && sys.companions.Count > 0 ? sys.companions[0].luminosity : 0f);
            float frost = 2.7f * Mathf.Sqrt(Mathf.Max(L, 1e-3f));
            float stable = 1e9f, inner = 0f;
            if (sys.companions.Count > 0)
            {
                var B = sys.companions[0];
                float Mab = sys.star.stellarMass + B.stellarMass;
                if (host < 0) inner = B.orbit.semiMajorAxisAU * StablePTypeMult(B.stellarMass / Mab, B.orbit.eccentricity) * 1.1f;
                else stable = B.orbit.semiMajorAxisAU * StableSTypeFrac(B.stellarMass / Mab, B.orbit.eccentricity);
                if (sys.companions.Count > 1)
                {
                    var C = sys.companions[1];
                    stable = Mathf.Min(stable, C.orbit.semiMajorAxisAU * StableSTypeFrac(C.stellarMass / (Mab + C.stellarMass), C.orbit.eccentricity));
                }
            }
            if (rng.Value < 0.6f)
            {
                float a = frost * rng.Range(0.7f, 1.1f);
                if (a * 0.85f > inner && a * 1.2f < stable)
                    sys.belts.Add(new AsteroidBelt { innerAU = a * 0.85f, outerAU = a * 1.2f, tiltDeg = rng.Range(0f, 6f), hostStar = host,
                        count = rng.RangeInt(400, 900), color = new Color(0.5f, 0.45f, 0.4f), icy = false });
            }
            float outer = 0f; foreach (var p in sys.planets) if (p.hostStar == host) outer = Mathf.Max(outer, p.semiMajorAxisAU);
            if (rng.Value < 0.7f && outer > 0f)
            {
                float k = outer * rng.Range(1.3f, 1.8f), k2 = k * rng.Range(1.4f, 2.2f);
                if (k2 > stable) k2 = stable * 0.95f;
                if (k2 > k * 1.15f)
                    sys.belts.Add(new AsteroidBelt { innerAU = k, outerAU = k2, tiltDeg = rng.Range(0f, 10f), hostStar = host,
                        count = rng.RangeInt(500, 1200), color = new Color(0.6f, 0.66f, 0.72f), icy = true });
            }
        }
    }
}
