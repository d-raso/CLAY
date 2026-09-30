using System.Text;
using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>
    /// In-engine test harness for the star/planet generator. Drop on any GameObject, then right-click the
    /// component header for the context-menu actions. No scene wiring or play mode needed.
    /// </summary>
    public class GalaxyGenDebug : MonoBehaviour
    {
        [Header("Single system")]
        public ulong seed = 424242;
        public bool forceClass = false;
        public SpectralClass classToForce = SpectralClass.G;

        [Header("Population sample")]
        public int populationSample = 2000;

        [ContextMenu("Generate one system")]
        public void GenerateOne()
        {
            var sys = SystemGenerator.Generate(seed, forceClass ? classToForce : (SpectralClass?)null);
            Debug.Log(Describe(sys));
        }

        [ContextMenu("Sample stellar population (distribution)")]
        public void SamplePopulation()
        {
            var pop = SystemGenerator.GeneratePopulation(populationSample, seed);
            var counts = new int[System.Enum.GetValues(typeof(SpectralClass)).Length];
            int habWorlds = 0, habSystems = 0;
            foreach (var sys in pop)
            {
                counts[(int)sys.star.spectralClass]++;
                int h = sys.HabitablePlanetCount;
                habWorlds += h;
                if (h > 0) habSystems++;
            }

            var sb = new StringBuilder($"[Galaxy] Population of {pop.Count} systems (seed {seed}):\n");
            foreach (SpectralClass c in System.Enum.GetValues(typeof(SpectralClass)))
                sb.AppendLine($"  {c}: {counts[(int)c],5}  ({100f * counts[(int)c] / pop.Count,5:0.00}%)   {Astrophysics.Classes[c].label}");
            sb.AppendLine($"  → {habWorlds} habitable worlds across {habSystems} systems " +
                          $"({100f * habSystems / pop.Count:0.0}% of systems have a cradle).");
            Debug.Log(sb.ToString());
        }

        static string Describe(StarSystem sys)
        {
            var s = sys.star;
            var su = s.Suitability();
            var sb = new StringBuilder();
            sb.AppendLine($"[Galaxy] {s.Designation}  ({s.ClassLabel})   seed {sys.seed}");
            sb.AppendLine($"   mass {s.stellarMass:0.00} M☉ · L {s.luminosity:0.####} L☉ · T {s.effectiveTemp:0} K · R {s.radius:0.00} R☉");
            sb.AppendLine($"   age {s.stellarAgeGyr:0.0} / lifespan {FormatGyr(s.lifetimeGyr)} · flares {s.flareActivity:0.00} · [Fe/H] {s.metallicity:0.00}");
            sb.AppendLine($"   habitable zone {s.hzInnerAU:0.00}–{s.hzOuterAU:0.00} AU · stellar suitability {su.overall * 100f:0}% " +
                          $"(life {su.lifetime * 100f:0} · UV {su.radiation * 100f:0} · calm {su.flares * 100f:0})");
            foreach (var p in sys.planets)
                sb.AppendLine($"     {p.name,-4} {p.type,-12} {p.semiMajorAxisAU,7:0.00} AU · {p.insolation,7:0.00} S⊕ · " +
                              $"{p.meanTempC,5:0}°C · {p.mass,7:0.0} M⊕ · {p.habClass} ({p.habitabilityIndex * 100f:0}%)");
            return sb.ToString();
        }

        static string FormatGyr(float g) =>
            g >= 1000f ? $"{g / 1000f:0.0} Tyr" : g >= 1f ? $"{g:0.#} Gyr" : $"{Mathf.Max(1f, g * 1000f):0} Myr";
    }
}
