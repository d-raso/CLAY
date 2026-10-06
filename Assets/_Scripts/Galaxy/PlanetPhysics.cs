using UnityEngine;

namespace CLAY.Galaxy
{
    public enum LiquidType { None, Water, Ammonia, Methane, Brine, Supercritical }

    /// <summary>Atmosphere by volume fraction (they needn't sum exactly to 1; normalised on use).</summary>
    [System.Serializable]
    public sealed class AtmoComposition
    {
        public float n2, o2, co2, ch4, nh3, so2, h2, he, h2o, cl2;

        public AtmoComposition Normalised()
        {
            float s = n2 + o2 + co2 + ch4 + nh3 + so2 + h2 + he + h2o + cl2;
            if (s <= 1e-6f) return new AtmoComposition { n2 = 1f };
            return new AtmoComposition { n2 = n2 / s, o2 = o2 / s, co2 = co2 / s, ch4 = ch4 / s, nh3 = nh3 / s, so2 = so2 / s,
                                         h2 = h2 / s, he = he / s, h2o = h2o / s, cl2 = cl2 / s };
        }

        public string Dominant()
        {
            var c = Normalised();
            (string n, float v)[] g = { ("N₂", c.n2), ("O₂", c.o2), ("CO₂", c.co2), ("CH₄", c.ch4), ("NH₃", c.nh3), ("SO₂", c.so2),
                                        ("H₂", c.h2), ("He", c.he), ("H₂O", c.h2o), ("Cl₂", c.cl2) };
            System.Array.Sort(g, (a, b) => b.v.CompareTo(a.v));
            return g[1].v > 0.05f ? $"{g[0].n}/{g[1].n}" : g[0].n;
        }

        // named mixes for the planet editor
        public static readonly string[] PresetNames = { "Earth N₂/O₂", "Venus CO₂", "Mars thin CO₂", "Titan N₂/CH₄", "Hycean H₂", "Volcanic SO₂", "Chlorine", "Ammonia", "Steam H₂O", "Helium" };
        public static AtmoComposition Preset(int i) => i switch
        {
            0 => new AtmoComposition { n2 = 0.78f, o2 = 0.21f, co2 = 0.0004f, h2o = 0.01f },
            1 => new AtmoComposition { co2 = 0.965f, n2 = 0.035f, so2 = 0.0002f },
            2 => new AtmoComposition { co2 = 0.95f, n2 = 0.03f },
            3 => new AtmoComposition { n2 = 0.95f, ch4 = 0.05f },
            4 => new AtmoComposition { h2 = 0.85f, he = 0.1f, h2o = 0.05f },
            5 => new AtmoComposition { so2 = 0.6f, co2 = 0.3f, n2 = 0.1f },
            6 => new AtmoComposition { cl2 = 0.35f, n2 = 0.5f, co2 = 0.15f },
            7 => new AtmoComposition { nh3 = 0.6f, n2 = 0.3f, ch4 = 0.1f },
            8 => new AtmoComposition { h2o = 0.9f, co2 = 0.1f },
            _ => new AtmoComposition { he = 0.8f, h2 = 0.15f, ch4 = 0.05f },
        };
    }

    /// <summary>
    /// The CAUSAL parameter layer (PlanetTypes.md §1): surface pressure, atmospheric composition, magnetic dynamo,
    /// liquid type, redox state, tectonic mode, spin resonance and flare dosage. Each is an optional override on
    /// PlanetData; when unset it is DERIVED here from the planet's physics, so galaxy worlds get sensible values and the
    /// planet editor can force any of them. Downstream code (climate, texturing, surface) reads these functions.
    /// </summary>
    public static class PlanetPhysics
    {
        // ── magnetic field (0..1): a molten, convecting core spun up by rotation; small/old/slow worlds lose it ──
        public static float Magnetic(PlanetData p)
        {
            if (p.magneticField >= 0f) return Mathf.Clamp01(p.magneticField);
            if (!PlanetData.IsRocky(p.type)) return 1f;                               // giants: metallic-hydrogen dynamos
            float size = Mathf.Clamp01(Mathf.Log(p.mass * 8f + 1f) / 3f);              // bigger → core stays molten longer
            float spin = Mathf.Clamp01(24f / Mathf.Max(p.rotationHours, 1f));          // fast rotation organises the dynamo
            float heat = Mathf.Clamp01(p.volcanism * 0.8f + p.tidalHeat * 0.4f + 0.15f);
            float m = size * Mathf.Lerp(0.35f, 1f, spin) * Mathf.Lerp(0.4f, 1f, heat);
            if (p.tidallyLocked) m *= 0.45f;                                           // slow synchronous spin
            return Mathf.Clamp01(m);
        }

        // ── flare dosage (0..1): how hard the star's flares hit the surface — activity × closeness × weak shielding ──
        public static float FlareDose(PlanetData p)
        {
            if (p.flareDose >= 0f) return Mathf.Clamp01(p.flareDose);
            float exposure = p.starFlareActivity * Mathf.Clamp01(p.insolation / 1.5f + 0.1f);
            return Mathf.Clamp01(exposure * (1.2f - Magnetic(p)));
        }

        // ── surface pressure (bar): forced, or retained gas × gravity, minus what flares have stripped ──
        public static bool HasAtmosphereOverride(PlanetData p, out bool has)
        {
            has = p.pressureOverrideBar > 0.005f;
            return p.pressureOverrideBar >= 0f;
        }
        /// Multiplier on the derived pressure: an unshielded world under an active star loses its air (Mars, flare-scoured).
        public static float StrippingFactor(PlanetData p)
        {
            if (!PlanetData.IsRocky(p.type)) return 1f;
            float strip = p.starFlareActivity * Mathf.Clamp01(p.insolation) * (1f - Magnetic(p));
            return Mathf.Clamp(1f - strip * 0.9f, 0.05f, 1f);
        }

        // ── atmospheric composition: forced, or what this world should hold ──
        public static AtmoComposition Composition(PlanetData p)
        {
            if (p.atmo != null) return p.atmo.Normalised();
            if (p.type == PlanetType.GasGiant) return new AtmoComposition { h2 = 0.86f, he = 0.13f, ch4 = 0.004f, nh3 = 0.002f };
            if (p.type == PlanetType.IceGiant) return new AtmoComposition { h2 = 0.8f, he = 0.17f, ch4 = 0.025f };
            float t = p.meanTempC;
            bool life = p.habClass == HabClass.Habitable;
            var c = new AtmoComposition();
            if (t > 300f) { c.co2 = 0.9f; c.so2 = 0.05f + p.volcanism * 0.1f; c.n2 = 0.05f; }               // Venus-like
            else if (t < -150f) { c.n2 = 0.92f; c.ch4 = 0.06f; c.h2 = 0.01f; }                                // Titan / Triton
            else if (life) { c.n2 = 0.76f; c.o2 = 0.2f * Mathf.Clamp01(p.habitabilityIndex * 1.4f); c.co2 = 0.004f; c.h2o = 0.01f + p.waterCoverage * 0.01f; }
            else if (p.volcanism > 0.7f) { c.co2 = 0.55f; c.so2 = 0.25f; c.n2 = 0.2f; }
            else { c.n2 = 0.6f + p.waterCoverage * 0.2f; c.co2 = 0.35f - p.waterCoverage * 0.2f; c.h2o = p.waterCoverage * 0.04f; }
            return c.Normalised();
        }

        /// Sky tint from what scatters and absorbs: Rayleigh blue for light N₂/O₂, pale yellow-white for thick CO₂,
        /// methane haze orange, NH₃ cream, SO₂ yellow, H₂ pale teal, Cl₂ green, steam white.
        public static Color SkyTint(AtmoComposition a, float pressureBar)
        {
            var c = a.Normalised();
            Color col = new Color(0.45f, 0.62f, 1f) * (c.n2 + c.o2 + c.he * 0.8f)
                      + new Color(0.95f, 0.88f, 0.72f) * c.co2 * Mathf.Clamp01(pressureBar / 10f + 0.3f) + new Color(0.75f, 0.62f, 0.5f) * c.co2 * Mathf.Clamp01(1f - pressureBar / 10f)
                      + new Color(0.88f, 0.56f, 0.3f) * c.ch4 * 6f
                      + new Color(0.93f, 0.88f, 0.74f) * c.nh3
                      + new Color(0.9f, 0.82f, 0.38f) * c.so2
                      + new Color(0.55f, 0.82f, 0.88f) * c.h2
                      + new Color(0.55f, 0.85f, 0.35f) * c.cl2 * 2f
                      + new Color(0.92f, 0.93f, 0.95f) * c.h2o * 2f;
            float s = Mathf.Max(col.r, Mathf.Max(col.g, col.b), 0.01f);
            col /= s; col.a = 1f;
            return col;
        }

        // ── liquid on the surface ──
        public static LiquidType Liquid(PlanetData p, float pressureBar)
        {
            if (p.liquidOverride >= 0) return (LiquidType)p.liquidOverride;
            if (!PlanetData.IsRocky(p.type) || p.waterCoverage < 0.02f || pressureBar < 0.006f) return LiquidType.None;
            float t = p.meanTempC;
            if (t > 374f && pressureBar > 220f) return LiquidType.Supercritical;
            if (t > 100f + 30f * Mathf.Log(Mathf.Max(pressureBar, 0.01f) + 1f)) return LiquidType.None;   // boiled off
            if (t < -160f) return LiquidType.Methane;
            if (t < -75f) return LiquidType.Ammonia;
            if (t < -12f) return LiquidType.Brine;                                    // salts keep it liquid below 0 °C
            return LiquidType.Water;
        }

        // ── redox (0 reducing … 1 oxidising): free O₂ from life rusts everything; H₂/CH₄ atmospheres keep crusts grey/tarry ──
        public static float Redox(PlanetData p)
        {
            if (p.redox >= 0f) return Mathf.Clamp01(p.redox);
            var c = Composition(p);
            float ox = c.o2 * 3f + c.co2 * 0.35f + c.so2 * 0.4f + c.cl2 * 0.8f;
            float red = c.h2 * 1.2f + c.ch4 * 1.5f + c.nh3;
            return Mathf.Clamp01(0.45f + ox - red);
        }

        public static string Describe(PlanetData p, float pressureBar)
        {
            var tect = PlanetTexture.Tectonics(p);
            return $"{Composition(p).Dominant()} · B {Magnetic(p):0.00} · flare {FlareDose(p):0.00} · " +
                   $"liquid {Liquid(p, pressureBar)} · redox {Redox(p):0.00} · {tect}{(p.spinResonance32 ? " · 3:2" : "")}";
        }
    }
}
