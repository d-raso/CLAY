using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>How suitable a star is for life, broken into the constraints it imposes (each 0–1).</summary>
    public struct StellarSuitability
    {
        public float lifetime;    // enough Gyr on the main sequence for life to arise?
        public float radiation;   // low enough UV / not sterilising?
        public float flares;      // calm enough not to strip close-in atmospheres?
        public float overall;     // combined 0–1
    }

    /// <summary>
    /// A generated star — the design doc's §1 StarSystem params. Runtime-generated (by
    /// <see cref="SystemGenerator"/>) or authorable; wrap in a ScriptableObject later if desired.
    /// </summary>
    [System.Serializable]
    public class StarData
    {
        public SpectralClass spectralClass;
        public int   subclass;         // 0–9 (0 = hot end of the class)
        public float stellarMass;      // M☉
        public float luminosity;       // L☉
        public float radius;           // R☉
        public float effectiveTemp;    // K
        public float stellarAgeGyr;
        public float lifetimeGyr;
        public float flareActivity;    // 0–1
        public float metallicity;      // [Fe/H]
        public float hzInnerAU, hzOuterAU;
        public Color color;
        public bool  isCompanion;        // false for the primary
        public string properName;        // catalogue proper name, e.g. "Delfor" (see NameGen)
        public OrbitElements orbit;      // companion's orbit about the primary (unused for primary)

        public string Designation => $"{spectralClass}{subclass}V";
        public string ClassLabel  => Astrophysics.Classes[spectralClass].label;

        /// <summary>The habitability constraints this star imposes on any world around it.</summary>
        public StellarSuitability Suitability()
        {
            float lifetime  = Astrophysics.Smoothstep(0.35f, 3.5f, lifetimeGyr);      // O/B/A die too young
            float radiation = 1f - Astrophysics.Smoothstep(6600f, 10500f, effectiveTemp); // hot stars: UV + fast burn
            float flares    = 1f - 0.85f * flareActivity;                            // red-dwarf flare stripping
            return new StellarSuitability
            {
                lifetime = lifetime,
                radiation = radiation,
                flares = flares,
                overall = lifetime * radiation * (0.35f + 0.65f * flares),
            };
        }
    }
}
