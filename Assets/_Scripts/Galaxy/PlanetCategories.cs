using System.Collections.Generic;
using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>The ~100 planet categories of PlanetTypes.md §2 (families A–K).</summary>
    public enum PlanetCategory
    {
        // A molten / scorched
        LavaOcean, SilicateVapor, CarbonLava, HellMoon, Chthonian, Obsidian, Sublimation, GlassRain,
        // B rocky / temperate
        EarthAnalog, Continental, Archipelago, Pangaea, Desert, OchreIron, BasaltPlains, VolcanicHighland, CrateredDead,
        RegolithDust, Karst, ClayShelf, Canyon, Mesa, Steppe, SaltFlat,
        // C ocean / hydrosphere
        GlobalOcean, ShallowSea, EyeballOcean, StormOcean, IceCappedOcean, SupercriticalWater, Hycean, MethaneSea, AmmoniaOcean, Brine,
        // D ice / cryo
        Snowball, Glacier, EuropaType, EnceladusType, NitrogenIce, DryIce, Clathrate, DirtyIce, FrostDesert, Cryovolcanic,
        // E toxic / chemistry
        VenusGreenhouse, SulfuricCloud, Sulfur, Chlorine, Copper, Halide, Tholin, PhotochemicalSmog, AmmoniaHaze, MercuryVapor,
        // F composition extremes
        Carbon, Tar, IronWorld, SuperMercury, Diamond, Corundum, Coreless, HeliumWorld, WaterVapor, IronSnow,
        // G mass / gravity
        SuperEarth, MegaEarth, SubEarth, DwarfRocky, Puffy,
        // H biosphere
        Garden, RedVegetation, PurpleRetinal, BlackPlant, FungalLichen, MicrobialMat, Oxygenating, MethaneBiosphere, Bioluminescent, PostBiotic,
        // I dynamics
        Eyeball, TwilightRing, Resonance32,
        // J history
        PostGiantImpact, FlareScoured, RogueCaptured, EjectaDusted,
        // K giants
        Jovian, HotJupiter, IceGiant, SubNeptune, AmmoniaCloudGiant, Superstorm, HeliumGiant,
    }

    /// <summary>
    /// Decides which documented category a world IS, from its causal parameters (PlanetPhysics) and composition —
    /// most distinctive first (vapour worlds before deserts, chlorine skies before generic rock) — plus secondary
    /// TAGS (spin, history, mass). Then gives categories the chemistry themes can't express their own look
    /// (palette adjustments applied at the end of PlanetTexture.Chem).
    /// </summary>
    public static class PlanetCategories
    {
        public struct Result { public PlanetCategory cat; public List<string> tags; }

        static float H(ulong seed, ulong salt) => (DetRng.Hash(seed, salt) & 0xFFFFFF) / 16777216f;

        public static Result Classify(PlanetData p, PlanetTexture.ChemTheme theme, ulong seed)
        {
            var r = new Result { tags = new List<string>() };
            var cl = PlanetClimate.Derive(p);
            float T = p.meanTempC, P = cl.pressureBar, wc = p.waterCoverage, volc = p.volcanism, mass = p.mass;
            float rho = mass / Mathf.Max(p.radiusEarth * p.radiusEarth * p.radiusEarth, 1e-3f);   // density vs Earth
            var gas = PlanetPhysics.Composition(p);
            var liq = PlanetPhysics.Liquid(p, P);
            float redox = PlanetPhysics.Redox(p), flare = PlanetPhysics.FlareDose(p);
            var tect = PlanetTexture.Tectonics(p);
            bool air = cl.hasAtmosphere && P > 0.01f;
            bool life = p.habClass == HabClass.Habitable;

            // ── tags: spin, orbit, history, mass ──
            if (p.tidallyLocked) r.tags.Add("tidally locked");
            if (p.spinResonance32) r.tags.Add("3:2 resonance");
            if (p.axialTiltDeg > 90f) r.tags.Add("retrograde"); else if (p.axialTiltDeg > 45f) r.tags.Add("high obliquity");
            if (p.rotationHours < 6f) r.tags.Add("fast rotator"); else if (p.rotationHours > 250f && !p.tidallyLocked) r.tags.Add("slow rotator");
            if (p.orbit.eccentricity > 0.3f) r.tags.Add("eccentric seasons");
            if (flare > 0.6f && PlanetData.IsRocky(p.type)) r.tags.Add("flare-scoured");
            if (p.bombardment > 0.8f) r.tags.Add("ejecta-dusted");
            if (PlanetData.IsRocky(p.type))
            {
                if (mass > 10f) r.tags.Add("mega-Earth"); else if (mass > 2.5f) r.tags.Add("super-Earth");
                else if (mass < 0.1f) r.tags.Add("dwarf"); else if (mass < 0.4f) r.tags.Add("sub-Earth");
            }

            // ── giants ──
            if (!PlanetData.IsRocky(p.type))
            {
                var g = PlanetTexture.GiantSubtype(p, seed);
                r.cat = g switch
                {
                    PlanetTexture.GiantType.HotJupiter => PlanetCategory.HotJupiter,
                    PlanetTexture.GiantType.SubNeptune => PlanetCategory.SubNeptune,
                    PlanetTexture.GiantType.HeliumGiant => PlanetCategory.HeliumGiant,
                    PlanetTexture.GiantType.Superstorm => PlanetCategory.Superstorm,
                    PlanetTexture.GiantType.IceGiant => PlanetCategory.IceGiant,
                    _ => T > -140f && T < -40f ? PlanetCategory.AmmoniaCloudGiant : PlanetCategory.Jovian,
                };
                return r;
            }

            // ── A: molten ──
            if (T > 1700f) { r.cat = PlanetCategory.SilicateVapor; return r; }
            if (T > 1100f && air) { r.cat = PlanetCategory.GlassRain; return r; }
            if (rho > 1.5f && mass > 8f && T > 400f) { r.cat = PlanetCategory.Chthonian; return r; }   // stripped giant core
            if (T > 700f) { r.cat = theme == PlanetTexture.ChemTheme.Carbonaceous ? PlanetCategory.CarbonLava : PlanetCategory.LavaOcean; return r; }
            if (p.tidalHeat > 0.7f && volc > 0.7f) { r.cat = PlanetCategory.HellMoon; return r; }

            // ── E: chemistry-dominated skies ──
            if (gas.cl2 > 0.1f) { r.cat = PlanetCategory.Chlorine; return r; }
            if (P > 30f && gas.co2 > 0.5f) { r.cat = PlanetCategory.VenusGreenhouse; return r; }
            if (gas.h2 > 0.4f && liq == LiquidType.Water) { r.cat = PlanetCategory.Hycean; return r; }
            if (liq == LiquidType.Supercritical) { r.cat = PlanetCategory.SupercriticalWater; return r; }
            if (gas.h2o > 0.5f && T > 100f) { r.cat = PlanetCategory.WaterVapor; return r; }
            if (gas.he > 0.5f) { r.cat = PlanetCategory.HeliumWorld; return r; }
            if (gas.so2 > 0.2f && air) { r.cat = T > 150f ? PlanetCategory.SulfuricCloud : PlanetCategory.Sulfur; return r; }
            if (gas.nh3 > 0.3f && air && liq != LiquidType.Ammonia) { r.cat = PlanetCategory.AmmoniaHaze; return r; }
            if (T > 250f && theme == PlanetTexture.ChemTheme.Metallic && air) { r.cat = PlanetCategory.MercuryVapor; return r; }

            // ── H: living worlds — named by what life does here ──
            if (life)
            {
                float st = p.hostStarTempK;
                if (p.tidallyLocked) r.cat = PlanetCategory.TwilightRing;
                else if (gas.ch4 > 0.01f && gas.o2 < 0.02f) r.cat = PlanetCategory.MethaneBiosphere;
                else if (gas.o2 < 0.05f && redox < 0.5f) r.cat = PlanetCategory.Oxygenating;
                else if (wc > 0.92f) r.cat = PlanetCategory.MicrobialMat;
                else if (st < 3200f) r.cat = PlanetCategory.BlackPlant;
                else if (st < 4100f) r.cat = PlanetCategory.RedVegetation;
                else if (st > 7000f) r.cat = PlanetCategory.PurpleRetinal;
                else if (p.insolation < 0.35f) r.cat = PlanetCategory.FungalLichen;
                else if (p.rotationHours > 200f) r.cat = PlanetCategory.Bioluminescent;
                else if (T > 28f && wc < 0.35f) r.cat = PlanetCategory.Steppe;
                else r.cat = PlanetCategory.Garden;
                return r;
            }

            // ── D: ice / cryo ──
            if (T < -20f)
            {
                if (liq == LiquidType.Methane && wc > 0.05f) r.cat = PlanetCategory.MethaneSea;
                else if (liq == LiquidType.Ammonia && wc > 0.3f) r.cat = PlanetCategory.AmmoniaOcean;
                else if (T < -190f && gas.n2 > 0.5f) r.cat = PlanetCategory.NitrogenIce;
                else if (gas.ch4 > 0.03f && P > 0.3f) r.cat = T < -150f ? PlanetCategory.Tholin : PlanetCategory.PhotochemicalSmog;
                else if (T > -140f && T < -70f && gas.co2 > 0.5f) r.cat = PlanetCategory.DryIce;
                else if (theme == PlanetTexture.ChemTheme.SalineIce)
                    r.cat = p.tidalHeat > 0.5f && mass < 0.05f ? PlanetCategory.EnceladusType : PlanetCategory.EuropaType;
                else if (volc > 0.55f) r.cat = PlanetCategory.Cryovolcanic;
                else if (theme == PlanetTexture.ChemTheme.Tholin || theme == PlanetTexture.ChemTheme.Carbonaceous) r.cat = PlanetCategory.DirtyIce;
                else if (gas.ch4 > 0.01f && wc > 0.2f) r.cat = PlanetCategory.Clathrate;
                else if (wc > 0.35f) r.cat = PlanetCategory.Snowball;
                else if (!air || wc < 0.05f) r.cat = PlanetCategory.FrostDesert;
                else r.cat = PlanetCategory.Glacier;
                if (p.insolation < 0.0005f) r.tags.Add("captured rogue");
                return r;
            }

            // ── C: water worlds ──
            if (liq == LiquidType.Brine && wc > 0.3f) { r.cat = PlanetCategory.Brine; return r; }
            if (liq == LiquidType.Water || wc > 0.3f)
            {
                if (wc > 0.95f) { r.cat = PlanetCategory.GlobalOcean; return r; }
                if (p.tidallyLocked && wc > 0.3f) { r.cat = PlanetCategory.EyeballOcean; return r; }
                if (p.rotationHours < 8f && wc > 0.5f) { r.cat = PlanetCategory.StormOcean; return r; }
                if (wc > 0.6f && T < 8f) { r.cat = PlanetCategory.IceCappedOcean; return r; }
                if (wc > 0.75f) { r.cat = p.mass > 2f ? PlanetCategory.ShallowSea : PlanetCategory.Archipelago; return r; }
            }

            // ── F: composition extremes ──
            switch (theme)
            {
                case PlanetTexture.ChemTheme.Carbonaceous:
                    r.cat = mass > 3f && P > 5f ? PlanetCategory.Diamond : redox < 0.25f && air ? PlanetCategory.Tar : PlanetCategory.Carbon; return r;
                case PlanetTexture.ChemTheme.Metallic:
                    r.cat = rho > 1.4f && mass > 2f ? PlanetCategory.SuperMercury : volc > 0.5f && T > 100f ? PlanetCategory.IronSnow : PlanetCategory.IronWorld; return r;
                case PlanetTexture.ChemTheme.Corundum: r.cat = PlanetCategory.Corundum; return r;
                case PlanetTexture.ChemTheme.Cupric: r.cat = PlanetCategory.Copper; return r;
                case PlanetTexture.ChemTheme.Halide: r.cat = PlanetCategory.Halide; return r;
                case PlanetTexture.ChemTheme.Evaporite: r.cat = PlanetCategory.SaltFlat; return r;
                case PlanetTexture.ChemTheme.Sulfuric: r.cat = PlanetCategory.Sulfur; return r;
                case PlanetTexture.ChemTheme.Tholin: r.cat = PlanetCategory.Tholin; return r;
            }
            if (rho < 0.55f) { r.cat = PlanetCategory.Puffy; return r; }
            if (rho < 0.75f && mass > 0.5f) { r.cat = PlanetCategory.Coreless; return r; }

            // ── J/A: violent histories ──
            if (p.bombardment > 0.9f && volc > 0.7f && T > 200f) { r.cat = PlanetCategory.PostGiantImpact; return r; }
            if (volc > 0.5f && T > 300f && !air) { r.cat = PlanetCategory.Obsidian; return r; }

            // ── B: rocky, by geology & water ──
            if (!air)
            {
                r.cat = flare > 0.65f ? PlanetCategory.FlareScoured
                      : p.insolation < 0.0005f ? PlanetCategory.RogueCaptured
                      : tect == PlanetTexture.TectonicMode.Dead && p.bombardment > 0.35f ? PlanetCategory.CrateredDead
                      : p.tidallyLocked ? PlanetCategory.Eyeball : PlanetCategory.RegolithDust;
                return r;
            }
            if (p.tidallyLocked) { r.cat = PlanetCategory.Eyeball; return r; }
            if (p.spinResonance32) { r.cat = PlanetCategory.Resonance32; return r; }
            if (volc > 0.65f) { r.cat = tect == PlanetTexture.TectonicMode.MobileLid ? PlanetCategory.VolcanicHighland : PlanetCategory.BasaltPlains; return r; }
            if (theme == PlanetTexture.ChemTheme.Basaltic) { r.cat = PlanetCategory.BasaltPlains; return r; }
            if (redox > 0.7f || theme == PlanetTexture.ChemTheme.Ferrous) { r.cat = wc < 0.05f ? PlanetCategory.OchreIron : PlanetCategory.Canyon; return r; }
            if (wc < 0.05f)
            {
                r.cat = p.mineralDiversity > 0.6f ? PlanetCategory.Mesa
                      : tect == PlanetTexture.TectonicMode.StagnantLid ? PlanetCategory.Canyon : PlanetCategory.Desert;
                return r;
            }
            if (p.mineralDiversity > 0.7f && wc < 0.3f) { r.cat = PlanetCategory.ClayShelf; return r; }
            if (wc < 0.15f && p.mineralDiversity > 0.5f) { r.cat = PlanetCategory.Karst; return r; }
            if (tect == PlanetTexture.TectonicMode.MobileLid) { r.cat = H(seed, 0x9A9EAUL) < 0.3f ? PlanetCategory.Pangaea : PlanetCategory.Continental; return r; }
            r.cat = T > -5f && T < 35f && wc > 0.25f ? PlanetCategory.EarthAnalog : PlanetCategory.Continental;
            return r;
        }

        public static string Name(PlanetCategory c) => c switch
        {
            PlanetCategory.LavaOcean => "Lava-Ocean World", PlanetCategory.SilicateVapor => "Silicate-Vapor World", PlanetCategory.CarbonLava => "Carbon-Lava World",
            PlanetCategory.HellMoon => "Hell World (Io-type)", PlanetCategory.Chthonian => "Chthonian World", PlanetCategory.Obsidian => "Obsidian World",
            PlanetCategory.Sublimation => "Sublimation World", PlanetCategory.GlassRain => "Glass-Rain World",
            PlanetCategory.EarthAnalog => "Earth Analog", PlanetCategory.Continental => "Continental World", PlanetCategory.Archipelago => "Archipelago World",
            PlanetCategory.Pangaea => "Supercontinent World", PlanetCategory.Desert => "Desert World", PlanetCategory.OchreIron => "Ochre Iron-Oxide World",
            PlanetCategory.BasaltPlains => "Basalt-Plains World", PlanetCategory.VolcanicHighland => "Volcanic-Highland World", PlanetCategory.CrateredDead => "Cratered Dead World",
            PlanetCategory.RegolithDust => "Regolith World", PlanetCategory.Karst => "Karst World", PlanetCategory.ClayShelf => "Clay-Shelf World",
            PlanetCategory.Canyon => "Canyon World", PlanetCategory.Mesa => "Mesa World", PlanetCategory.Steppe => "Steppe World", PlanetCategory.SaltFlat => "Salt-Flat World",
            PlanetCategory.GlobalOcean => "Global Ocean World", PlanetCategory.ShallowSea => "Shallow-Sea World", PlanetCategory.EyeballOcean => "Eyeball Ocean",
            PlanetCategory.StormOcean => "Storm-Ocean World", PlanetCategory.IceCappedOcean => "Ice-Capped Ocean World", PlanetCategory.SupercriticalWater => "Supercritical-Water World",
            PlanetCategory.Hycean => "Hycean World", PlanetCategory.MethaneSea => "Methane-Sea World", PlanetCategory.AmmoniaOcean => "Ammonia-Ocean World", PlanetCategory.Brine => "Brine World",
            PlanetCategory.Snowball => "Snowball World", PlanetCategory.Glacier => "Glacier World", PlanetCategory.EuropaType => "Europa-type Ice Moon",
            PlanetCategory.EnceladusType => "Enceladus-type Plume World", PlanetCategory.NitrogenIce => "Nitrogen-Ice World", PlanetCategory.DryIce => "Dry-Ice World",
            PlanetCategory.Clathrate => "Clathrate World", PlanetCategory.DirtyIce => "Dirty-Ice World", PlanetCategory.FrostDesert => "Frost-Desert World", PlanetCategory.Cryovolcanic => "Cryovolcanic World",
            PlanetCategory.VenusGreenhouse => "Runaway Greenhouse World", PlanetCategory.SulfuricCloud => "Sulfuric-Cloud World", PlanetCategory.Sulfur => "Sulfur World",
            PlanetCategory.Chlorine => "Chlorine World", PlanetCategory.Copper => "Copper World", PlanetCategory.Halide => "Halide-Salt World", PlanetCategory.Tholin => "Tholin World",
            PlanetCategory.PhotochemicalSmog => "Smog World", PlanetCategory.AmmoniaHaze => "Ammonia-Haze World", PlanetCategory.MercuryVapor => "Mercury-Vapor World",
            PlanetCategory.Carbon => "Carbon World", PlanetCategory.Tar => "Tar World", PlanetCategory.IronWorld => "Iron World", PlanetCategory.SuperMercury => "Super-Mercury",
            PlanetCategory.Diamond => "Diamond World", PlanetCategory.Corundum => "Corundum (Ruby) World", PlanetCategory.Coreless => "Coreless World", PlanetCategory.HeliumWorld => "Helium World",
            PlanetCategory.WaterVapor => "Steam World", PlanetCategory.IronSnow => "Iron-Snow World",
            PlanetCategory.SuperEarth => "Super-Earth", PlanetCategory.MegaEarth => "Mega-Earth", PlanetCategory.SubEarth => "Sub-Earth", PlanetCategory.DwarfRocky => "Dwarf World", PlanetCategory.Puffy => "Puffy World",
            PlanetCategory.Garden => "Garden World", PlanetCategory.RedVegetation => "Red-Vegetation World", PlanetCategory.PurpleRetinal => "Purple (Retinal) World",
            PlanetCategory.BlackPlant => "Black-Plant World", PlanetCategory.FungalLichen => "Fungal/Lichen World", PlanetCategory.MicrobialMat => "Microbial-Mat World",
            PlanetCategory.Oxygenating => "Oxygenating World", PlanetCategory.MethaneBiosphere => "Methane-Biosphere World", PlanetCategory.Bioluminescent => "Bioluminescent World", PlanetCategory.PostBiotic => "Post-Biotic World",
            PlanetCategory.Eyeball => "Eyeball World", PlanetCategory.TwilightRing => "Twilight-Ring World", PlanetCategory.Resonance32 => "3:2-Resonant World",
            PlanetCategory.PostGiantImpact => "Post-Impact Magma World", PlanetCategory.FlareScoured => "Flare-Scoured World", PlanetCategory.RogueCaptured => "Captured Rogue", PlanetCategory.EjectaDusted => "Ejecta-Dusted World",
            PlanetCategory.Jovian => "Jovian Giant", PlanetCategory.HotJupiter => "Hot Jupiter", PlanetCategory.IceGiant => "Ice Giant", PlanetCategory.SubNeptune => "Sub-Neptune",
            PlanetCategory.AmmoniaCloudGiant => "Ammonia-Cloud Giant", PlanetCategory.Superstorm => "Superstorm Giant", PlanetCategory.HeliumGiant => "Helium Giant",
            _ => c.ToString(),
        };

        static Color Mix(Color a, Color b, float t) { var c = Color.Lerp(a, b, t); c.a = 1f; return c; }

        /// Palette adjustments for categories the chemistry themes alone can't express.
        public static void ApplyLook(PlanetCategory c, ref PlanetTexture.ChemPalette cp, PlanetData p)
        {
            switch (c)
            {
                case PlanetCategory.Chlorine:
                    cp.landLow = Mix(cp.landLow, new Color(0.45f, 0.48f, 0.2f), 0.55f); cp.landMid = Mix(cp.landMid, new Color(0.62f, 0.64f, 0.32f), 0.55f);
                    cp.oceanShallow = new Color(0.45f, 0.6f, 0.22f); cp.oceanDeep = new Color(0.16f, 0.26f, 0.08f); cp.atm = new Color(0.55f, 0.85f, 0.35f); break;
                case PlanetCategory.Hycean:
                    cp.hasLiquid = true; cp.oceanShallow = new Color(0.2f, 0.55f, 0.58f); cp.oceanDeep = new Color(0.04f, 0.18f, 0.24f); cp.atm = new Color(0.6f, 0.85f, 0.9f); break;
                case PlanetCategory.VenusGreenhouse:
                    cp.landLow = new Color(0.42f, 0.3f, 0.2f); cp.landMid = new Color(0.58f, 0.42f, 0.28f); cp.landHigh = new Color(0.7f, 0.56f, 0.4f); cp.atm = new Color(0.95f, 0.86f, 0.62f); break;
                case PlanetCategory.NitrogenIce:
                    cp.ice = new Color(0.96f, 0.86f, 0.82f); cp.landLow = new Color(0.55f, 0.38f, 0.32f); cp.accent = new Color(0.45f, 0.25f, 0.18f); cp.accentAmt = Mathf.Max(cp.accentAmt, 0.4f); break;
                case PlanetCategory.DryIce:
                    cp.ice = new Color(0.97f, 0.97f, 0.98f); cp.landMid = Mix(cp.landMid, new Color(0.6f, 0.58f, 0.56f), 0.5f); break;
                case PlanetCategory.DirtyIce:
                    cp.ice = new Color(0.62f, 0.56f, 0.5f); break;
                case PlanetCategory.EuropaType:
                    cp.hasLineae = true; cp.ice = new Color(0.94f, 0.9f, 0.82f); cp.accent = new Color(0.62f, 0.32f, 0.2f); cp.accentAmt = Mathf.Max(cp.accentAmt, 0.5f); break;
                case PlanetCategory.EnceladusType:
                    cp.ice = new Color(0.99f, 0.99f, 1f); cp.hasLineae = true; cp.accent = new Color(0.55f, 0.7f, 0.9f); break;
                case PlanetCategory.Cryovolcanic:
                    cp.accent = new Color(0.6f, 0.75f, 0.92f); cp.accentAmt = Mathf.Max(cp.accentAmt, 0.45f); break;
                case PlanetCategory.Obsidian:
                    cp.landLow = new Color(0.03f, 0.03f, 0.04f); cp.landMid = new Color(0.06f, 0.06f, 0.07f); cp.landHigh = new Color(0.12f, 0.11f, 0.12f); break;
                case PlanetCategory.GlassRain: case PlanetCategory.SilicateVapor: case PlanetCategory.PostGiantImpact:
                    cp.hasLiquid = true; cp.oceanShallow = new Color(1f, 0.55f, 0.12f); cp.oceanDeep = new Color(0.4f, 0.06f, 0.02f);
                    cp.landLow = new Color(0.14f, 0.06f, 0.05f); cp.landMid = new Color(0.22f, 0.1f, 0.08f); break;
                case PlanetCategory.Diamond:
                    cp.landLow = new Color(0.08f, 0.09f, 0.11f); cp.landMid = new Color(0.16f, 0.17f, 0.2f); cp.landHigh = new Color(0.55f, 0.6f, 0.68f); break;
                case PlanetCategory.Tar:
                    cp.landLow = new Color(0.07f, 0.05f, 0.03f); cp.landMid = new Color(0.12f, 0.09f, 0.05f); cp.oceanShallow = new Color(0.1f, 0.07f, 0.03f); cp.oceanDeep = new Color(0.02f, 0.02f, 0.01f); break;
                case PlanetCategory.IronSnow: case PlanetCategory.IronWorld: case PlanetCategory.SuperMercury:
                    cp.landMid = Mix(cp.landMid, new Color(0.5f, 0.5f, 0.52f), 0.5f); break;
                case PlanetCategory.Karst:
                    cp.landMid = Mix(cp.landMid, new Color(0.72f, 0.7f, 0.64f), 0.5f); cp.landHigh = Mix(cp.landHigh, new Color(0.82f, 0.8f, 0.74f), 0.5f); break;
                case PlanetCategory.FlareScoured:
                    cp.landMid = Mix(cp.landMid, Color.white * 0.75f, 0.3f); break;
                case PlanetCategory.WaterVapor:
                    cp.atm = new Color(0.94f, 0.95f, 0.97f); break;
                // living worlds: what the pigment and the biosphere's stage look like
                case PlanetCategory.BlackPlant:     cp.veg = new Color(0.05f, 0.05f, 0.04f); break;
                case PlanetCategory.RedVegetation:  cp.veg = new Color(0.45f, 0.1f, 0.08f); break;
                case PlanetCategory.PurpleRetinal:  cp.veg = new Color(0.36f, 0.16f, 0.42f); break;
                case PlanetCategory.FungalLichen:   cp.veg = new Color(0.62f, 0.6f, 0.5f); break;
                case PlanetCategory.Steppe:         cp.veg = new Color(0.62f, 0.55f, 0.28f); break;
                case PlanetCategory.Bioluminescent: cp.veg = new Color(0.1f, 0.32f, 0.36f); break;
                case PlanetCategory.PostBiotic:     cp.veg = new Color(0.36f, 0.32f, 0.26f); cp.vegAmt *= 0.5f; break;
                case PlanetCategory.MicrobialMat:   cp.oceanShallow = new Color(0.62f, 0.38f, 0.42f); break;
                case PlanetCategory.Oxygenating:    cp.oceanShallow = new Color(0.55f, 0.36f, 0.22f); cp.oceanDeep = new Color(0.25f, 0.14f, 0.1f); break;   // banded-iron seas
            }
        }
    }
}
