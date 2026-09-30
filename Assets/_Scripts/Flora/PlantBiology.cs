using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.Flora
{
    // Carbon-fixation strategy, inferred from anatomy (never picked):
    // C3 = ordinary leaves (needs ~150–200 ppm CO₂; suffers photorespiration in heat),
    // C4 = grass/blade-type anatomy (concentrates CO₂; copes with ~50 ppm, hot bright conditions),
    // CAM = succulents (opens stomata at night; very water-thrifty, copes with low CO₂, slow-growing).
    public enum CarbonPathway { C3, C4, CAM }

    // The survivable envelope, DERIVED from structure/biology — editing the genome re-derives it live, so
    // "where it lives" is always a consequence of what the plant is. Thresholds are anchored to real plant
    // physiology (see the comments on each factor).
    public struct PlantSurvivability
    {
        public CarbonPathway pathway;
        public float tempMinC, tempMaxC;       // coldest / hottest the tissue survives
        public float lightMin, lightMax;       // usable-light range, in Earth-sunlight units
        public float gMax;                     // strongest gravity it can stand up in
        public float precipMinMm, precipMaxMm; // annual rainfall it needs / tolerates
        public float minCO2kPa;                // CO₂ partial-pressure floor for net photosynthesis
        public float minPressureBar;           // below this, leaves desiccate / gas exchange fails
        public float maxWindLoad;              // wind dynamic pressure (×Earth) before it snaps or is uprooted
        public float maxUV;                    // UV (×Earth surface) it can screen
        public float farRedUse;                // 0 … 1 how much far-red light its pigments can harvest
        public float saltTolerance;            // 0 … 1
    }

    public struct Verdict
    {
        public bool viable;
        public string limiting;                // the binding constraint when not viable
        public List<string> goodZones;         // climate zones where it can grow
        public float habitableArea;            // fraction of the planet's surface it could occupy
    }

    public static class PlantBiology
    {
        static bool IsSucculent(PlantArchetype a) => a == PlantArchetype.Cactus || a == PlantArchetype.SucculentRosette || a == PlantArchetype.Globe
                                                     || a == PlantArchetype.LivingStone || a == PlantArchetype.Spire;
        static bool IsBroadLeaf(LeafShape l) => l == LeafShape.Ovate || l == LeafShape.Lanceolate || l == LeafShape.Palmate || l == LeafShape.Cordate
                                               || l == LeafShape.Reniform || l == LeafShape.Disc || l == LeafShape.Kelp || l == LeafShape.Membrane;
        static bool IsSquat(PlantArchetype a) => a == PlantArchetype.Globe || a == PlantArchetype.GrassClump || a == PlantArchetype.MatAlgae
                                                || a == PlantArchetype.SucculentRosette || a == PlantArchetype.LivingStone;

        public static PlantSurvivability Derive(PlantGenome g)
        {
            var s = new PlantSurvivability();
            float dark = 1f - Luminance(g.pigment);
            Color.RGBToHSV(g.pigment, out float hue, out float sat, out float _);
            // anthocyanin-type (red/purple/magenta) pigmentation — the UV-screening, far-red-leaning pigments
            float antho = sat * ((hue < 0.06f || hue > 0.75f) ? 1f : 0f);
            bool succ = IsSucculent(g.archetype);
            bool aquatic = g.archetype == PlantArchetype.MatAlgae;
            bool broad = IsBroadLeaf(g.leaf);
            bool needleScale = g.leaf == LeafShape.Needle || g.leaf == LeafShape.Scale || g.leaf == LeafShape.None || g.leaf == LeafShape.Sheath;
            bool waxy = g.leafStyle == LeafStyle.Waxy || g.leafStyle == LeafStyle.Glossy;
            float xeric = Mathf.Clamp01((succ ? 0.7f : 0f) + (needleScale ? 0.3f : 0f) + g.spininess * 0.3f
                                        + Mathf.Clamp01(g.bodyGirth - 1f) * 0.3f + (waxy ? 0.15f : 0f));

            // ── Carbon pathway ──
            bool bladey = g.archetype == PlantArchetype.GrassClump || g.leaf == LeafShape.Blade || g.leaf == LeafShape.Strap;
            s.pathway = succ || xeric > 0.65f ? CarbonPathway.CAM : (bladey && xeric > 0.15f) ? CarbonPathway.C4 : CarbonPathway.C3;
            // CO₂ floor as a partial pressure (at 1 bar, 0.015 kPa ≈ 150 ppm): C3 plants need ~150–200 ppm,
            // C4 and CAM survive near 50 ppm (their compensation points are far lower).
            s.minCO2kPa = s.pathway == CarbonPathway.C3 ? 0.015f : s.pathway == CarbonPathway.C4 ? 0.005f : 0.004f;

            // ── Temperature ── Plant tissue survives roughly −60 °C to +60 °C; the most heat-tolerant known plant
            // (Tidestromia) photosynthesises to ~63 °C. Cold-hardiness from small/needle leaves and dark (heat-
            // absorbing) pigment; heat tolerance from water storage, wax and reduced leaf area.
            s.tempMinC = Mathf.Lerp(-5f, -60f, Mathf.Clamp01((needleScale ? 0.5f : 0f) + dark * 0.35f + (succ ? 0.1f : 0f)));
            s.tempMaxC = Mathf.Lerp(38f, 63f, xeric);
            if (broad) { s.tempMinC = Mathf.Max(s.tempMinC, -12f); s.tempMaxC = Mathf.Min(s.tempMaxC, 47f); }
            if (s.pathway == CarbonPathway.C3) s.tempMaxC = Mathf.Min(s.tempMaxC, 50f);   // photorespiration ceiling

            // ── Light ── dark pigment + big leaves harvest dim light; xeric/spiny/waxy tolerate glare.
            s.lightMin = Mathf.Lerp(0.35f, 0.02f, Mathf.Clamp01(dark * 0.8f + Mathf.Clamp01(g.leafSize - 0.5f) * 0.3f));
            s.lightMax = Mathf.Lerp(1.4f, 3.6f, Mathf.Clamp01(xeric * 0.8f + g.spininess * 0.3f));
            // Red dwarfs emit mostly far-red; pigments that look dark/red/purple are the kind that can be shifted to
            // use it (chlorophyll d/f analogues).
            s.farRedUse = Mathf.Clamp01(dark * 0.8f + antho * 0.5f);

            // ── Gravity ── tall + thin only stands in low g; squat forms handle high g.
            float slender = Mathf.Clamp01(g.heightM / 30f) * (1.2f - g.trunkTaper);
            float squat = (IsSquat(g.archetype) ? 1.3f : 0f) + (1f - Mathf.Clamp01(g.heightM / 6f));
            s.gMax = Mathf.Clamp(3.5f - slender * 3.2f + squat * 1.2f + g.stiltRoots * 0.4f, 0.15f, 6f);

            // ── Water ── deserts get < 250 mm/yr, rainforests 2000–3000 mm/yr. Succulents/CAM live on a few tens of
            // mm; thin broad leaves need forest-level rain; too much rain rots succulents.
            s.precipMinMm = aquatic ? 1500f : Mathf.Lerp(900f, 30f, xeric) * (broad ? 1.2f : 1f);
            s.precipMaxMm = succ ? 1300f : 7000f;

            // ── Air pressure ── photosynthesis continues down to ~10 kPa (0.1 bar) when CO₂ is adequate; below
            // that, and for thin big leaves even earlier, water loss runs away (diffusion speeds up in thin air).
            s.minPressureBar = broad ? 0.3f : xeric > 0.6f || waxy ? 0.08f : 0.12f;
            if (g.leaf == LeafShape.Membrane) s.minPressureBar = 0.45f;

            // ── Wind ── height (lever arm) × canopy sail area is what breaks trees; squat forms shrug it off,
            // flexible herbaceous stems bend instead of snapping, stilt roots brace.
            float sail = Mathf.Max(0.2f, g.leafSize * Mathf.Max(g.leafDensity, 0.3f)) *
                         (g.leaf == LeafShape.Membrane ? 3f : broad ? 1.3f : needleScale ? 0.6f : 1f);
            float lever = 1f + g.heightM / 8f;
            s.maxWindLoad = 6f / (lever * sail) * (IsSquat(g.archetype) ? 4f : 1f)
                            * (g.stemMaterial < 0.4f ? 1.5f : 1f) * (1f + g.stiltRoots * 0.3f);
            s.maxWindLoad = Mathf.Clamp(s.maxWindLoad, 0.3f, 40f);

            // ── UV ── anthocyanins/flavonoids in the epidermis absorb UV-B; wax, hairs/spines and succulence help.
            s.maxUV = 1.5f + dark * 1.5f + antho * 2f + (waxy ? 1f : 0f) + g.spininess * 1f + (succ ? 1f : 0f);

            // ── Salt ── succulence and waxy cuticles correlate with halophyte (salt-tolerant) lifestyles.
            s.saltTolerance = Mathf.Clamp01(0.25f + xeric * 0.45f + (aquatic ? 0.3f : 0f));
            return s;
        }

        /// Full evaluation against a planet's climate, band by band.
        public static Verdict Evaluate(PlantGenome g, PlanetData p)
        {
            var v = new Verdict { goodZones = new List<string>() };
            var s = Derive(g);
            var c = PlanetClimate.Derive(p);

            // planet-wide hard limits first (they apply everywhere)
            if (!PlanetWideOK(s, p, c, out string pw)) return Fail(v, pw);
            float usable = UsableLight(s, p, c);

            // then zone by zone
            string lastWhy = "no climate band suits it";
            foreach (var z in c.zones)
            {
                if (FitsLocal(s, z.tMinC, z.tMaxC, z.precipMm, z.windLoad, usable, out string why))
                { v.goodZones.Add(z.name); v.habitableArea += z.areaFrac; }
                else lastWhy = $"{z.name}: {why}";
            }
            v.viable = v.goodZones.Count > 0;
            v.limiting = v.viable ? "viable" : lastWhy;
            return v;
        }

        static Verdict Fail(Verdict v, string why) { v.viable = false; v.limiting = why; return v; }

        /// Conditions that apply everywhere on the planet (air, CO₂, gravity, UV, the star's light, salt).
        public static bool PlanetWideOK(in PlantSurvivability s, PlanetData p, PlanetClimate c, out string why)
        {
            why = null;
            if (!c.hasAtmosphere) why = "no atmosphere (no CO₂ to fix, water boils off)";
            else if (c.pressureBar < s.minPressureBar) why = $"air too thin ({c.pressureBar:0.00} bar < {s.minPressureBar:0.00}): it would dry out";
            else if (c.co2kPa < s.minCO2kPa) why = $"too little CO₂ for {s.pathway} photosynthesis ({c.co2ppm:0} ppm)";
            else if (c.gravity > s.gMax) why = $"gravity too strong ({c.gravity:0.0} g > {s.gMax:0.0} g)";
            else if (c.uvPeak > s.maxUV * 1.6f) why = $"UV too harsh (×{c.uvPeak:0.0} Earth; screens ×{s.maxUV:0.0})";
            else if (UsableLight(s, p, c) < s.lightMin * 0.5f) why = "too little usable light (pigments can't use this star's far-red)";
            else if (c.salinity > 0.85f && s.saltTolerance < 0.5f) why = "soils too saline";
            return why == null;
        }

        public static float UsableLight(in PlantSurvivability s, PlanetData p, PlanetClimate c)
            => p.insolation * Mathf.Clamp01(c.visibleFrac + c.farRedFrac * s.farRedUse);

        /// Conditions at one place (a climate band, or a single spot on the surface).
        public static bool FitsLocal(in PlantSurvivability s, float tMin, float tMax, float precip, float windLoad, float usable, out string why)
        {
            why = null;
            if (tMin < s.tempMinC)             why = $"winters too cold ({tMin:0}°C < {s.tempMinC:0}°C)";
            else if (tMax > s.tempMaxC)        why = $"too hot ({tMax:0}°C > {s.tempMaxC:0}°C)";
            else if (precip < s.precipMinMm)   why = $"too dry ({precip:0} mm/yr < {s.precipMinMm:0})";
            else if (precip > s.precipMaxMm)   why = "too wet — it would rot";
            else if (windLoad > s.maxWindLoad) why = $"winds would snap it (load ×{windLoad:0.0} > ×{s.maxWindLoad:0.0})";
            else if (usable > s.lightMax * 1.5f) why = "light too intense";
            return why == null;
        }

        // Back-compat wrapper.
        public static bool CanLiveOn(PlantGenome g, PlanetData p, out string why)
        {
            var v = Evaluate(g, p);
            why = v.limiting;
            return v.viable;
        }

        public static string Habitat(PlantGenome g)
        {
            var s = Derive(g);
            string temp = s.tempMaxC > 55f ? "hot" : s.tempMinC < -25f ? "cold-hardy" : "temperate";
            string wat = s.precipMinMm > 700f ? "wet" : s.precipMinMm < 150f ? "arid" : "moderate-rain";
            string light = s.lightMin < 0.1f ? "dim/shaded" : s.lightMax > 2.5f ? "high-glare" : "moderate-light";
            string wind = s.maxWindLoad < 1.2f ? "sheltered" : s.maxWindLoad > 6f ? "wind-hardy" : "moderate-wind";
            return $"{s.pathway} · {temp}, {wat}, {light}, {wind}";
        }

        static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
    }
}
