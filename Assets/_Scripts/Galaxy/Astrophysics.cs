using System.Collections.Generic;
using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>Morgan–Keenan spectral classes (main sequence), hot → cool.</summary>
    public enum SpectralClass { O, B, A, F, G, K, M }

    /// <summary>
    /// Real main-sequence stellar physics — the relations that turn a star's MASS into everything else
    /// (luminosity, temperature, radius, lifetime, habitable zone, colour). Ported from the validated
    /// Star-Forge model; grounded in CLAY / PlanetaryParameters §1. Pure functions, no allocation.
    /// </summary>
    public static class Astrophysics
    {
        public struct ClassInfo { public float massLo, massHi, weight; public string label; }

        /// <summary>Mass range (M☉) + rough galactic abundance (initial-mass-function weight) per class.</summary>
        public static readonly Dictionary<SpectralClass, ClassInfo> Classes = new()
        {
            { SpectralClass.O, new ClassInfo{ massLo=16f,  massHi=60f,  weight=0.00003f, label="Blue supergiant" } },
            { SpectralClass.B, new ClassInfo{ massLo=2.1f, massHi=16f,  weight=0.0013f,  label="Blue-white star" } },
            { SpectralClass.A, new ClassInfo{ massLo=1.4f, massHi=2.1f, weight=0.006f,   label="White star" } },
            { SpectralClass.F, new ClassInfo{ massLo=1.04f,massHi=1.4f, weight=0.03f,    label="Yellow-white star" } },
            { SpectralClass.G, new ClassInfo{ massLo=0.8f, massHi=1.04f,weight=0.076f,   label="Yellow dwarf" } },
            { SpectralClass.K, new ClassInfo{ massLo=0.45f,massHi=0.8f, weight=0.121f,   label="Orange dwarf" } },
            { SpectralClass.M, new ClassInfo{ massLo=0.08f,massHi=0.45f,weight=0.764f,   label="Red dwarf" } },
        };

        public const float SunTempK = 5772f;

        /// <summary>Mass–luminosity relation (piecewise), L in L☉.</summary>
        public static float MassToLuminosity(float M)
        {
            if (M < 0.43f) return 0.23f * Mathf.Pow(M, 2.3f);
            if (M < 2f)    return Mathf.Pow(M, 4f);
            if (M < 55f)   return 1.4f * Mathf.Pow(M, 3.5f);
            return 32000f * M;
        }

        /// <summary>Main-sequence mass–radius (approx), R in R☉.</summary>
        public static float MassToRadius(float M) => M <= 1f ? Mathf.Pow(M, 0.8f) : Mathf.Pow(M, 0.57f);

        /// <summary>Stefan–Boltzmann: L = 4πR²σT⁴  →  T = T☉·(L/R²)^¼. Clamped to physical bounds.</summary>
        public static float EffectiveTemp(float L, float R) =>
            Mathf.Clamp(SunTempK * Mathf.Pow(L / (R * R), 0.25f), 2400f, 52000f);

        /// <summary>Main-sequence lifetime (Gyr): fuel ∝ M, burn rate ∝ L. ~10 Gyr for the Sun.</summary>
        public static float LifetimeGyr(float M, float L) => 10f * M / L;

        /// <summary>Conservative habitable-zone edges (AU): runaway-greenhouse inner, maximum-greenhouse outer.</summary>
        public static void HabitableZone(float L, out float innerAU, out float outerAU)
        {
            innerAU = Mathf.Sqrt(L / 1.1f);
            outerAU = Mathf.Sqrt(L / 0.53f);
        }

        public static float Smoothstep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>1 inside [lo,hi], falling linearly to 0 over <paramref name="soft"/> beyond each edge.</summary>
        public static float Bell(float x, float lo, float hi, float soft)
        {
            if (x >= lo && x <= hi) return 1f;
            float d = x < lo ? lo - x : x - hi;
            return Mathf.Clamp01(1f - d / soft);
        }

        /// <summary>Spectral designation: class + subclass digit 0–9 (0 = hot end of the class) from temperature.</summary>
        public static (SpectralClass cls, int sub) Designation(float T)
        {
            (SpectralClass c, float lo, float hi)[] bands =
            {
                (SpectralClass.O, 30000f, 50000f), (SpectralClass.B, 10000f, 30000f), (SpectralClass.A, 7500f, 10000f),
                (SpectralClass.F, 6000f, 7500f),   (SpectralClass.G, 5200f, 6000f),   (SpectralClass.K, 3700f, 5200f),
                (SpectralClass.M, 2400f, 3700f)
            };
            if (T >= 50000f) return (SpectralClass.O, 0);
            if (T < 2400f)   return (SpectralClass.M, 9);
            foreach (var (c, lo, hi) in bands)
                if (T >= lo && T < hi) return (c, Mathf.Clamp(Mathf.RoundToInt(9f * (hi - T) / (hi - lo)), 0, 9));
            return (SpectralClass.O, 0);
        }

        /// <summary>Blackbody colour of a star at temperature T (Tanner-Helland approximation).</summary>
        public static Color BlackbodyColor(float T)
        {
            float t = Mathf.Clamp(T, 1500f, 40000f) / 100f, r, g, b;
            if (t <= 66f) { r = 255f; g = 99.47f * Mathf.Log(t) - 161.12f; }
            else          { r = 329.7f * Mathf.Pow(t - 60f, -0.1332f); g = 288.12f * Mathf.Pow(t - 60f, -0.0755f); }
            if (t >= 66f) b = 255f; else if (t <= 19f) b = 0f; else b = 138.52f * Mathf.Log(t - 10f) - 305.04f;
            return new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f));
        }
    }
}
