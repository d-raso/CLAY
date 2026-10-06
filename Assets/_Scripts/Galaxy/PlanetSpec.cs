using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>
    /// Every input the planet editor exposes. Floats left as NaN and ints left at −1 mean AUTO: the value is derived
    /// exactly the way the galaxy's SystemGenerator derives it. Anything set is forced.
    /// </summary>
    [System.Serializable]
    public sealed class PlanetSpec
    {
        public ulong seed = 1;

        // ── star ──
        public SpectralClass starClass = SpectralClass.G;
        public float starMassT = 0.5f;          // 0..1 position inside the class's mass range (log)
        public float ageFrac = 0.35f;           // 0..1 of min(main-sequence lifetime, 13.6 Gyr)
        public float metallicity = 0f;          // [Fe/H]
        public bool binary;                     // add a companion star
        public float companionSepAU = 40f;      // < 0.6 AU → close pair (planet is circumbinary)
        public float companionMassRatio = 0.5f;

        // ── orbit & spin ──
        public PlanetType type = PlanetType.Terrestrial;
        public float distanceAU = 1f;
        public float eccentricity = 0.02f;
        public float inclinationDeg = 1.5f;
        public float axialTiltDeg = 23f;
        public float rotationHours = 24f;
        public int lockMode = 0;                // 0 auto, 1 tidally locked, 2 free spin, 3 3:2 resonance

        // ── body ──
        public float massEarth = 1f;
        public float radiusEarth = float.NaN;   // auto from mass & type
        public float albedo = float.NaN;
        public float greenhouseK = float.NaN;

        // ── surface ──
        public float waterCoverage = float.NaN;
        public int waterChemistry = -1;         // WaterChemistry, −1 auto
        public float volcanism = float.NaN;
        public float mineralDiversity = float.NaN;
        public float bombardment = float.NaN;
        public float tidalHeat = float.NaN;
        public int habMode = 0;                 // 0 auto, 1 force habitable (life), 2 force lifeless
        public int theme = -1;                  // PlanetTexture.ChemTheme, −1 auto
        public int moons = -1;                  // −1 auto, else up to this many

        // ── atmosphere & interior (causal parameters) ──
        public float pressureBar = float.NaN;   // surface pressure
        public int atmoPreset = -1;             // AtmoComposition.Preset index, −1 auto
        public float magneticField = float.NaN;
        public float flareDose = float.NaN;
        public int liquid = -1;                 // LiquidType, −1 auto
        public float redox = float.NaN;         // 0 reducing … 1 oxidising
        public int tectonics = -1;              // PlanetTexture.TectonicMode, −1 auto

        public PlanetSpec Clone() => (PlanetSpec)MemberwiseClone();
    }
}
