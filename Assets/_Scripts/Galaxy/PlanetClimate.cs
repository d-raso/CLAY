using UnityEngine;

namespace CLAY.Galaxy
{
    /// One climate band of a planet: a latitude band (or, on a tidally locked world, a band of distance from the
    /// point directly under the star). Temperatures are the band's seasonal + day/night extremes.
    [System.Serializable]
    public struct ClimateZone
    {
        public string name;
        public float tMinC, tMeanC, tMaxC;   // coldest night of winter · annual mean · hottest day of summer
        public float precipMm;               // mean annual precipitation
        public float windMs;                 // typical surface wind
        public float windLoad;               // wind dynamic pressure relative to Earth (ρv²; 1 = Earth-typical)
        public float areaFrac;               // share of the planet's surface in this band
        public float key;                    // band centre: |latitude| in degrees, or angle from substellar point
    }

    // Whittaker-style biomes: classified from local mean temperature & annual precipitation (temperature gates
    // whether water supports forest, grassland or tundra), plus water/ice/shore special cases.
    public enum Biome { Ocean, Shore, Ice, Tundra, Boreal, TemperateForest, TemperateRainforest, Grassland, Savanna,
                        TropicalSeasonal, TropicalRainforest, Desert, ColdDesert, Wetland, Barren }

    /// The climate at one spot on the surface.
    public struct LocalClimate
    {
        public float tMeanC, tMinC, tMaxC, precipMm, windLoad, elevM;
        public Biome biome;
    }

    /// <summary>
    /// A planet's WEATHER, derived deterministically from its physical data (mass, radius, insolation, rotation,
    /// tilt, water, volcanism, star). Rules of thumb are calibrated so Earth comes out Earth-like:
    /// • Pressure from atmosphere retention/density and gravity, plus greenhouse (runaway worlds are thick).
    /// • CO₂ rises with volcanic outgassing and greenhouse warming, is drawn down by a biosphere, and freezes
    ///   out of the air below about −78 °C.
    /// • Day/night swings grow with slow rotation and shrink with thick air and oceans; seasons grow with axial
    ///   tilt; the equator-to-pole contrast shrinks as the atmosphere carries more heat.
    /// • Rainfall follows evaporation (≈ 6–7 % more water vapour per °C, Clausius–Clapeyron) × ocean cover,
    ///   distributed like Earth's circulation: wet equator, dry subtropics, wet mid-latitudes, dry poles.
    /// • Surface UV scales steeply with star temperature and is screened by air + ozone (needs free O₂);
    ///   red dwarfs add flare bursts.
    /// • Usable light is split into visible (what ordinary chlorophyll uses) vs far-red (dominant from red dwarfs).
    /// </summary>
    [System.Serializable]
    public class PlanetClimate
    {
        public bool  hasAtmosphere;
        public float pressureBar;
        public float co2ppm, co2kPa;          // mixing ratio · partial pressure (what plants actually "see")
        public float o2Frac;
        public float meanTempC, diurnalRangeC, seasonalRangeC, equatorPoleC;
        public float precipMm, humidity;
        public float windMs, windLoad, storminess;
        public float uvIndex, uvPeak, flareRisk;   // 1 = Earth-surface mean UV
        public float visibleFrac, farRedFrac;      // share of starlight in visible / far-red, relative to the Sun
        public float gravity;
        public float salinity;
        public bool  tidallyLocked;
        public ClimateZone[] zones;

        public static PlanetClimate Derive(PlanetData p)
        {
            var c = new PlanetClimate();
            if (p == null) return c;
            c.gravity = Mathf.Clamp(p.mass / Mathf.Max(p.radiusEarth * p.radiusEarth, 1e-3f), 0.02f, 12f);
            c.hasAtmosphere = PlanetTexture.HasAtmosphere(p);
            c.tidallyLocked = p.tidallyLocked;
            c.meanTempC = p.meanTempC;
            float wc = Mathf.Clamp01(p.waterCoverage);

            // ── air ──
            float dens = PlanetTexture.AtmosphereDensity(p);
            c.pressureBar = c.hasAtmosphere
                ? Mathf.Clamp(dens * 2.1f * Mathf.Sqrt(Mathf.Clamp(c.gravity, 0.1f, 4f)) + Mathf.Max(0f, p.greenhouseK - 25f) / 30f, 0.01f, 95f)
                : 0f;
            bool biosphere = p.habClass == HabClass.Habitable;
            float co2 = 280f * (0.7f + 2.5f * p.volcanism) * (1f + Mathf.Max(0f, p.greenhouseK - 30f) / 15f);
            if (biosphere) co2 *= 0.85f;
            if (p.meanTempC < -78f) co2 *= 0.15f;                          // CO₂ frosts out of the air
            c.co2ppm = Mathf.Clamp(co2, 5f, 500000f);
            c.co2kPa = c.pressureBar * 100f * c.co2ppm / 1e6f;
            c.o2Frac = biosphere ? Mathf.Lerp(0.05f, 0.28f, p.habitabilityIndex) : 0.002f;

            // ── temperature structure ──
            float pAir = Mathf.Clamp(c.pressureBar, 0f, 5f);
            float ocean = 1f - 0.5f * wc;
            float rot = Mathf.Max(p.rotationHours, 1f);
            c.diurnalRangeC = c.tidallyLocked ? 0f
                : c.hasAtmosphere ? Mathf.Clamp(10f * Mathf.Sqrt(rot / 24f) / (0.2f + 0.8f * pAir) * ocean / 0.65f, 1f, 250f)
                                  : Mathf.Clamp(160f * Mathf.Sqrt(rot / 24f), 30f, 300f);
            c.seasonalRangeC = Mathf.Clamp(p.axialTiltDeg * 1.35f * ocean / (0.6f + 0.4f * pAir), 0f, 120f);
            c.equatorPoleC = c.tidallyLocked
                ? Mathf.Clamp(120f / (0.35f + 0.65f * pAir), 15f, 300f)            // day side vs night side
                : Mathf.Clamp(42f * (1f - 0.3f * wc) / (0.5f + 0.5f * pAir), 5f, 200f);

            // ── water cycle ──
            c.precipMm = (!c.hasAtmosphere || wc < 0.01f) ? 0f
                : Mathf.Clamp(1000f * (wc / 0.7f) * Mathf.Exp(0.065f * (p.meanTempC - 15f)) * Mathf.Clamp01(c.pressureBar / 0.3f), 0f, 6000f);
            if (p.meanTempC < -30f) c.precipMm *= 0.15f;                   // frozen worlds: little snowfall
            if (p.meanTempC > 100f) c.precipMm = 0f;                       // too hot for liquid rain
            c.humidity = Mathf.Clamp01(c.precipMm / 2000f);

            // ── wind ── (thin air blows faster but pushes less; fast spin → stronger jets)
            c.windMs = !c.hasAtmosphere ? 0f
                : (3f + c.equatorPoleC * 0.08f + 2f * Mathf.Sqrt(24f / rot)) / Mathf.Sqrt(Mathf.Max(c.pressureBar, 0.05f))
                  + (c.tidallyLocked ? 8f : 0f);
            float rho = c.pressureBar * 288f / Mathf.Max(p.meanTempC + 273f, 40f);   // density relative to Earth
            c.windLoad = rho * (c.windMs / 6f) * (c.windMs / 6f);
            c.storminess = Mathf.Clamp01(c.humidity * Mathf.Clamp01((p.meanTempC + 10f) / 40f) * c.windMs / 10f);

            // ── light & UV ──
            float T = p.hostStarTempK > 0f ? p.hostStarTempK : 5778f;
            float starUV = Mathf.Pow(T / 5778f, 3f);
            c.flareRisk = T < 4000f ? Mathf.Lerp(0.8f, 0.2f, Mathf.InverseLerp(2500f, 4000f, T)) : 0.05f;
            float shield = c.hasAtmosphere ? Mathf.Exp(-pAir * 0.4f - c.o2Frac * 8f) : 1f;
            const float earthShield = 0.125f;                              // exp(-0.4 − 0.21·8)
            c.uvIndex = p.insolation * starUV * shield / earthShield;
            c.uvPeak = c.uvIndex + c.flareRisk * p.insolation * 3f * shield / earthShield;
            c.visibleFrac = Mathf.Exp(-((T - 6000f) / 3200f) * ((T - 6000f) / 3200f));
            c.farRedFrac = Mathf.Clamp01((5200f - T) / 2600f) * 0.85f + 0.15f;

            c.salinity = Mathf.Clamp01(0.25f + (1f - wc) * 0.5f + (p.meanTempC > 30f ? 0.15f : 0f)
                                       + (p.waterChemistry == WaterChemistry.Sulfide ? 0.2f : 0f));

            // ── zones ──
            if (c.tidallyLocked)
            {
                string[] names = { "Substellar", "Dayside", "Terminator", "Nightside", "Antistellar" };
                float[] ang = { 0f, 45f, 90f, 135f, 175f };
                float[] area = { 0.07f, 0.29f, 0.28f, 0.29f, 0.07f };
                float[] rain = { 2.2f, 1.2f, 0.7f, 0.25f, 0.05f };
                float[] wind = { 0.6f, 0.9f, 1.8f, 1.1f, 0.5f };
                c.zones = new ClimateZone[5];
                for (int i = 0; i < 5; i++)
                {
                    float tm = c.meanTempC + c.equatorPoleC * 0.5f * Mathf.Cos(ang[i] * Mathf.Deg2Rad);
                    c.zones[i] = Zone(names[i], tm, 4f, c.precipMm * rain[i], c.windMs * wind[i], rho, area[i]);
                    c.zones[i].key = ang[i];
                }
            }
            else
            {
                string[] names = { "Equatorial", "Subtropical", "Temperate", "Subpolar", "Polar" };
                float[] lat = { 5f, 22f, 42f, 60f, 80f };
                float[] area = { 0.17f, 0.27f, 0.27f, 0.16f, 0.13f };
                float[] rain = { 1.9f, 0.45f, 1.1f, 0.9f, 0.25f };
                float[] wind = { 0.8f, 1.0f, 1.3f, 1.4f, 1.1f };
                c.zones = new ClimateZone[5];
                for (int i = 0; i < 5; i++)
                {
                    float s = Mathf.Sin(lat[i] * Mathf.Deg2Rad);
                    float tm = c.meanTempC + c.equatorPoleC * (0.35f - s * s);
                    float seas = c.seasonalRangeC * (0.15f + s);
                    c.zones[i] = Zone(names[i], tm, seas + c.diurnalRangeC, c.precipMm * rain[i], c.windMs * wind[i], rho, area[i]);
                    c.zones[i].key = lat[i];
                }
            }
            return c;
        }

        /// Local climate at a surface direction: interpolate the bands by latitude (or by angle from the substellar
        /// point, +Z, on a locked world), cool with altitude (≈6.5 °C per km lapse rate), strengthen wind with
        /// altitude, and let the orbital moisture field make wet and dry regions within a band.
        public LocalClimate At(Vector3 dir, float elevM, float moist01, bool underwater, bool frozenCap)
        {
            var l = new LocalClimate { elevM = elevM };
            if (zones == null || zones.Length == 0) { l.biome = Biome.Barren; return l; }
            float key = tidallyLocked ? Mathf.Acos(Mathf.Clamp(dir.z, -1f, 1f)) * Mathf.Rad2Deg
                                      : Mathf.Abs(Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f))) * Mathf.Rad2Deg;
            int i1 = 0;
            while (i1 < zones.Length - 1 && zones[i1 + 1].key < key) i1++;
            int i2 = Mathf.Min(i1 + 1, zones.Length - 1);
            float t = i1 == i2 ? 0f : Mathf.Clamp01(Mathf.InverseLerp(zones[i1].key, zones[i2].key, key));
            if (key < zones[0].key) { i1 = i2 = 0; t = 0f; }
            ClimateZone a = zones[i1], b = zones[i2];
            float lapse = Mathf.Max(0f, elevM) * 0.0065f;
            l.tMeanC = Mathf.Lerp(a.tMeanC, b.tMeanC, t) - lapse;
            l.tMinC = Mathf.Lerp(a.tMinC, b.tMinC, t) - lapse;
            l.tMaxC = Mathf.Lerp(a.tMaxC, b.tMaxC, t) - lapse;
            l.precipMm = Mathf.Lerp(a.precipMm, b.precipMm, t) * Mathf.Lerp(0.3f, 1.8f, Mathf.Clamp01(moist01));
            l.windLoad = Mathf.Lerp(a.windLoad, b.windLoad, t) * (1f + Mathf.Max(0f, elevM) / 3000f);
            l.biome = Classify(l, underwater, frozenCap);
            return l;
        }

        public Biome Classify(in LocalClimate l, bool underwater, bool frozenCap)
        {
            if (!hasAtmosphere || pressureBar < 0.01f) return Biome.Barren;
            if (underwater) return Biome.Ocean;
            if (frozenCap || l.tMaxC < 0f) return Biome.Ice;
            if (l.tMeanC > 60f) return Biome.Barren;
            if (l.tMeanC < -6f) return l.precipMm < 150f ? Biome.ColdDesert : Biome.Tundra;
            if (l.precipMm < 250f) return l.tMeanC < 8f ? Biome.ColdDesert : Biome.Desert;
            if (l.tMeanC < 3f) return Biome.Boreal;
            if (l.precipMm > 2600f && l.tMeanC < 22f && l.elevM < 150f) return Biome.Wetland;
            if (l.tMeanC > 20f) return l.precipMm > 2000f ? Biome.TropicalRainforest
                                     : l.precipMm > 1000f ? Biome.TropicalSeasonal : Biome.Savanna;
            if (l.precipMm < 600f) return Biome.Grassland;
            return l.precipMm > 1800f ? Biome.TemperateRainforest : Biome.TemperateForest;
        }

        static ClimateZone Zone(string n, float mean, float range, float precip, float wind, float rho, float area) => new ClimateZone
        {
            name = n, tMeanC = mean, tMinC = mean - range * 0.5f, tMaxC = mean + range * 0.5f,
            precipMm = precip, windMs = wind, windLoad = rho * (wind / 6f) * (wind / 6f), areaFrac = area,
        };

        public string Summary() =>
            $"{pressureBar:0.00} bar · CO₂ {co2ppm:0} ppm ({co2kPa:0.000} kPa) · O₂ {o2Frac * 100f:0}%\n" +
            $"Mean {meanTempC:0}°C · seasons ±{seasonalRangeC * 0.5f:0}° · day/night ±{diurnalRangeC * 0.5f:0}°\n" +
            $"Rain {precipMm:0} mm/yr · wind {windMs:0.0} m/s (load ×{windLoad:0.0}) · UV ×{uvIndex:0.0} (flares ×{uvPeak:0.0})\n" +
            $"Light: visible ×{visibleFrac:0.00}, far-red ×{farRedFrac:0.00} · gravity {gravity:0.00} g";
    }
}
