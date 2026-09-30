using System.Collections.Generic;
using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>A ring of small bodies (asteroid belt, Kuiper/ice belt, or a debris ring).</summary>
    [System.Serializable]
    public class AsteroidBelt
    {
        public float innerAU, outerAU, tiltDeg;
        public int count;            // rendered rock count
        public Color color;
        public bool icy;             // icy (outer/Kuiper) vs rocky (inner asteroid belt)
        public int hostStar;         // 0 = primary A, −1 = around the close A+B pair (circumbinary)
    }

    /// <summary>
    /// Architecture of a multiple system (real binaries: ~⅓ of Sun-like stars, separations log-normal around ~40 AU).
    /// Close pairs host CIRCUMBINARY (P-type) planets beyond ~2–4× their separation (Kepler-16/Tatooine); wide pairs let
    /// each star keep its own (S-type) planets inside ~¼–⅓ of the separation (α Centauri); intermediate pairs
    /// (≈1–10 AU) destabilise almost everything.
    /// </summary>
    public enum BinaryKind { Single, Close, Intermediate, Wide }

    /// <summary>A star and its planets, reproducible from <see cref="seed"/>.</summary>
    [System.Serializable]
    public class StarSystem
    {
        public ulong seed;
        public StarData star;                          // the primary
        public List<StarData> companions = new();      // secondary/tertiary stars (binary/trinary)
        public List<PlanetData> planets = new();
        public List<AsteroidBelt> belts = new();       // asteroid / Kuiper / debris rings

        public int StarCount => 1 + companions.Count;

        /// How the A–B pair is arranged (drives where planets can survive).
        public BinaryKind binary;

        /// Star by index: 0 = primary, 1.. = companions.
        public StarData Star(int i) => i <= 0 ? star : companions[Mathf.Min(i - 1, companions.Count - 1)];

        /// Mass a planet actually orbits: the host star, or the A+B pair for circumbinary worlds (M☉).
        public float HostMass(PlanetData p) =>
            p.hostStar < 0 ? star.stellarMass + (companions.Count > 0 ? companions[0].stellarMass : 0f)
                           : Star(p.hostStar).stellarMass;

        /// Mass of the inner pair (A+B), which the tertiary orbits.
        public float InnerMass => star.stellarMass + (companions.Count > 0 ? companions[0].stellarMass : 0f);

        public int HabitablePlanetCount
        {
            get { int n = 0; foreach (var p in planets) if (p.habClass == HabClass.Habitable) n++; return n; }
        }
    }
}
