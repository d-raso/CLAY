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
            GenerateCompanions(ref rng, sys, p);
            GeneratePlanets(ref rng, sys, p);
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
            float L = Astrophysics.MassToLuminosity(M);
            float R = Astrophysics.MassToRadius(M);
            float T = Astrophysics.EffectiveTemp(L, R);
            float life = Astrophysics.LifetimeGyr(M, L);
            // youthBias (near nebulae / star-forming arms) pushes the age young → hotter, more active.
            float ageHi = Mathf.Lerp(1f, 0.28f, Mathf.Clamp01(p.youthBias));
            float age = rng.Range(0.05f, ageHi) * Mathf.Min(life, 13.6f);
            float youth = 1f - Mathf.Clamp01(age / life);
            float flare = Mathf.Clamp01((0.62f - M) / 0.5f) * (0.35f + 0.65f * youth); // red dwarfs, esp. young
            flare = Mathf.Clamp01(flare * (1f + 0.5f * Mathf.Clamp01(p.youthBias)));
            var d = Astrophysics.Designation(T);
            Astrophysics.HabitableZone(L, out float hin, out float hout);
            float feh = float.IsNaN(p.metallicityOverride)
                      ? Mathf.Round(rng.Range(-0.6f, 0.45f) * 100f) / 100f
                      : Mathf.Round(Mathf.Clamp(p.metallicityOverride, -1f, 0.6f) * 100f) / 100f;

            return new StarData
            {
                spectralClass = d.cls, subclass = d.sub,
                stellarMass = M, luminosity = L, radius = R, effectiveTemp = T,
                lifetimeGyr = life, stellarAgeGyr = age, flareActivity = flare,
                metallicity = feh,
                hzInnerAU = hin, hzOuterAU = hout, color = Astrophysics.BlackbodyColor(T),
            };
        }

        static void GenerateCompanions(ref DetRng rng, StarSystem sys, SystemGenParams p)
        {
            int extra = Mathf.Clamp(p.starCount, 1, 3) - 1;
            for (int k = 0; k < extra; k++)
            {
                var c = GenerateStar(ref rng, RollClass(ref rng), p);
                c.isCompanion = true;
                c.orbit = new OrbitElements
                {
                    semiMajorAxisAU = rng.LogRange(4f, 120f),                    // wide binary separation
                    eccentricity = rng.Range(0f, 0.4f),
                    inclinationDeg = rng.Range(0f, Mathf.Max(p.maxInclinationDeg, 45f)),
                    ascendingNodeDeg = rng.Range(0f, 360f),
                    argPeriapsisDeg = rng.Range(0f, 360f),
                    meanAnomalyDeg = rng.Range(0f, 360f),
                };
                sys.companions.Add(c);
            }
        }

        static void GeneratePlanets(ref DetRng rng, StarSystem sys, SystemGenParams gp)
        {
            var s = sys.star;
            float stellarSuit = s.Suitability().overall;
            float frost = 2.7f * Mathf.Sqrt(s.luminosity);                 // snow line scales with √L
            float boost = Mathf.Clamp(gp.habitabilityBoost, 0.3f, 2f);     // metal-rich → richer planetary systems
            int n = gp.planetCount > 0 ? Mathf.Clamp(gp.planetCount, 1, 12)
                                       : rng.RangeInt(3, 8) + (boost > 1.15f ? 1 : 0);

            float d = 0.28f * Mathf.Sqrt(s.luminosity) * rng.Range(0.8f, 1.2f);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) d *= rng.Range(1.4f, 1.9f);                     // geometric spacing (Titius–Bode-ish)
                bool inHZ = d >= s.hzInnerAU * 0.75f && d <= s.hzOuterAU * 1.25f;

                PlanetType type;
                if (d > frost)
                    type = rng.Value < 0.6f ? (d > frost * 3f ? PlanetType.IceGiant : PlanetType.GasGiant) : PlanetType.FrozenRock;
                else
                    type = inHZ ? (rng.Value < 0.45f ? PlanetType.Ocean : PlanetType.Terrestrial)
                                : (rng.Value < 0.5f ? PlanetType.Desert : PlanetType.Terrestrial);

                bool rocky = PlanetData.IsRocky(type);
                float mass = rocky ? rng.LogRange(0.08f, 6f)
                           : type == PlanetType.GasGiant ? rng.LogRange(50f, 3000f) : rng.LogRange(10f, 50f);
                float albedo = (type == PlanetType.FrozenRock || type == PlanetType.IceGiant)
                             ? rng.Range(0.5f, 0.7f) : rng.Range(0.15f, 0.42f);
                float S = s.luminosity / (d * d);                          // insolation, S⊕
                float green = rocky ? Mathf.Clamp01(Mathf.Log(mass + 1f) / 1.6f) * rng.Range(10f, 70f) * Mathf.Clamp01(S / 1.2f) : 0f;
                float Teq = 278.5f * Mathf.Pow(S * (1f - albedo), 0.25f);  // blackbody equilibrium, K
                float Tc = Teq + green - 273.15f;                          // mean surface, °C

                // Occasionally a steeply inclined orbit; mostly near the plane.
                float inc = rng.Range(0f, gp.maxInclinationDeg) * (rng.Value < 0.25f ? rng.Range(1.5f, 2.5f) : 1f);
                var orbit = new OrbitElements
                {
                    semiMajorAxisAU = d,
                    eccentricity = rng.Range(0f, 0.28f) * (rocky ? 1f : 0.6f),
                    inclinationDeg = inc,
                    ascendingNodeDeg = rng.Range(0f, 360f),
                    argPeriapsisDeg = rng.Range(0f, 360f),
                    meanAnomalyDeg = rng.Range(0f, 360f),
                };

                var p = new PlanetData
                {
                    index = i, name = Roman[Mathf.Min(i, Roman.Length - 1)], type = type,
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

                // Surface chemistry → appearance (volcanism relief, clay shelves, ocean extent/tint).
                p.volcanism = rocky ? Mathf.Clamp01(rng.Range(0.15f, 1f) * (1f - 0.4f * s.stellarAgeGyr / Mathf.Max(1f, s.lifetimeGyr))) : 0f;
                p.mineralDiversity = Mathf.Clamp01(rng.Range(0.2f, 1f) * (0.6f + s.metallicity));
                p.waterChemistry = (WaterChemistry)rng.RangeInt(0, 4);
                p.waterCoverage = p.habClass == HabClass.Habitable ? rng.Range(0.45f, 0.75f)
                                : type == PlanetType.Ocean ? rng.Range(0.6f, 0.9f)
                                : (rocky && Tc > -25f && Tc < 90f ? rng.Range(0.05f, 0.35f) : 0f);

                sys.planets.Add(p);
            }
        }
    }
}
