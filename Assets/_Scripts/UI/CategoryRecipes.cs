using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.UI
{
    /// <summary>
    /// Planet-editor category picker: a RECIPE per documented category (the causal parameters that make a world
    /// that kind of world), then a small solver that tunes the orbit to the recipe's temperature and searches seeds
    /// until PlanetCategories.Classify agrees — so the picker never lies about what you get.
    /// </summary>
    public static class CategoryRecipes
    {
        public static readonly (string family, PlanetCategory[] cats)[] Families =
        {
            ("Molten", new[] { PlanetCategory.LavaOcean, PlanetCategory.SilicateVapor, PlanetCategory.CarbonLava, PlanetCategory.HellMoon, PlanetCategory.Chthonian, PlanetCategory.Obsidian, PlanetCategory.GlassRain, PlanetCategory.PostGiantImpact }),
            ("Rocky", new[] { PlanetCategory.EarthAnalog, PlanetCategory.Continental, PlanetCategory.Pangaea, PlanetCategory.Desert, PlanetCategory.OchreIron, PlanetCategory.BasaltPlains, PlanetCategory.VolcanicHighland, PlanetCategory.CrateredDead, PlanetCategory.RegolithDust, PlanetCategory.Karst, PlanetCategory.ClayShelf, PlanetCategory.Canyon, PlanetCategory.Mesa, PlanetCategory.Puffy, PlanetCategory.Coreless }),
            ("Ocean", new[] { PlanetCategory.GlobalOcean, PlanetCategory.Archipelago, PlanetCategory.ShallowSea, PlanetCategory.EyeballOcean, PlanetCategory.StormOcean, PlanetCategory.IceCappedOcean, PlanetCategory.SupercriticalWater, PlanetCategory.Hycean, PlanetCategory.Brine }),
            ("Ice", new[] { PlanetCategory.Snowball, PlanetCategory.Glacier, PlanetCategory.EuropaType, PlanetCategory.EnceladusType, PlanetCategory.NitrogenIce, PlanetCategory.DryIce, PlanetCategory.Clathrate, PlanetCategory.DirtyIce, PlanetCategory.FrostDesert, PlanetCategory.Cryovolcanic, PlanetCategory.MethaneSea, PlanetCategory.AmmoniaOcean }),
            ("Toxic", new[] { PlanetCategory.VenusGreenhouse, PlanetCategory.SulfuricCloud, PlanetCategory.Sulfur, PlanetCategory.Chlorine, PlanetCategory.Copper, PlanetCategory.Halide, PlanetCategory.SaltFlat, PlanetCategory.Tholin, PlanetCategory.PhotochemicalSmog, PlanetCategory.AmmoniaHaze, PlanetCategory.MercuryVapor, PlanetCategory.WaterVapor, PlanetCategory.HeliumWorld }),
            ("Exotic", new[] { PlanetCategory.Carbon, PlanetCategory.Tar, PlanetCategory.Diamond, PlanetCategory.IronWorld, PlanetCategory.SuperMercury, PlanetCategory.IronSnow, PlanetCategory.Corundum }),
            ("Living", new[] { PlanetCategory.Garden, PlanetCategory.Steppe, PlanetCategory.RedVegetation, PlanetCategory.PurpleRetinal, PlanetCategory.BlackPlant, PlanetCategory.FungalLichen, PlanetCategory.MicrobialMat, PlanetCategory.Oxygenating, PlanetCategory.MethaneBiosphere, PlanetCategory.Bioluminescent, PlanetCategory.TwilightRing }),
            ("Orbit & history", new[] { PlanetCategory.Eyeball, PlanetCategory.Resonance32, PlanetCategory.FlareScoured, PlanetCategory.RogueCaptured }),
            ("Giants", new[] { PlanetCategory.Jovian, PlanetCategory.HotJupiter, PlanetCategory.IceGiant, PlanetCategory.SubNeptune, PlanetCategory.AmmoniaCloudGiant, PlanetCategory.Superstorm, PlanetCategory.HeliumGiant }),
        };

        const int Earth = 0, Venus = 1, Mars = 2, Titan = 3, Hyc = 4, SO2 = 5, Chl = 6, NH3 = 7, Steam = 8, He = 9;
        static int Th(PlanetTexture.ChemTheme t) => (int)t;

        /// Sets the recipe on a fresh Sun-like spec; returns the target mean temperature (NaN = leave the orbit alone).
        public static float Apply(PlanetSpec s, PlanetCategory c)
        {
            s.habMode = 2; s.type = PlanetType.Terrestrial;
            float T = 15f;
            void Air(int preset, float bar) { s.atmoPreset = preset; s.pressureBar = bar; }
            void Airless() { s.pressureBar = 0.0001f; s.atmoPreset = -1; s.waterCoverage = 0f; }
            switch (c)
            {
                // ── molten ──
                case PlanetCategory.LavaOcean: T = 1000f; s.type = PlanetType.Desert; Airless(); s.volcanism = 1f; s.lockMode = 1; break;
                case PlanetCategory.SilicateVapor: T = 2100f; s.type = PlanetType.Desert; Airless(); s.lockMode = 1; break;
                case PlanetCategory.CarbonLava: T = 950f; s.type = PlanetType.Desert; Airless(); s.theme = Th(PlanetTexture.ChemTheme.Carbonaceous); s.lockMode = 1; break;
                case PlanetCategory.GlassRain: T = 1300f; s.type = PlanetType.Desert; Air(SO2, 5f); s.lockMode = 1; break;
                case PlanetCategory.Chthonian: T = 900f; s.type = PlanetType.Desert; s.massEarth = 14f; s.radiusEarth = 1.9f; Airless(); s.lockMode = 1; break;
                case PlanetCategory.HellMoon: T = 120f; s.type = PlanetType.Desert; s.massEarth = 0.015f; Airless(); s.tidalHeat = 1f; s.volcanism = 1f; s.theme = Th(PlanetTexture.ChemTheme.Sulfuric); break;
                case PlanetCategory.Obsidian: T = 400f; s.type = PlanetType.Desert; Airless(); s.volcanism = 0.8f; break;
                case PlanetCategory.PostGiantImpact: T = 400f; s.type = PlanetType.Desert; Air(Venus, 0.5f); s.bombardment = 1f; s.volcanism = 1f; s.waterCoverage = 0f; break;

                // ── rocky ──
                case PlanetCategory.EarthAnalog: T = 14f; Air(Earth, 1f); s.waterCoverage = 0.6f; s.mineralDiversity = 0.4f; s.volcanism = 0.3f; s.tectonics = (int)PlanetTexture.TectonicMode.StagnantLid; break;
                case PlanetCategory.Continental: T = 14f; Air(Earth, 1f); s.waterCoverage = 0.45f; s.mineralDiversity = 0.4f; s.volcanism = 0.4f; s.tectonics = (int)PlanetTexture.TectonicMode.MobileLid; break;
                case PlanetCategory.Pangaea: goto case PlanetCategory.Continental;
                case PlanetCategory.Desert: T = 35f; s.type = PlanetType.Desert; Air(Mars, 0.5f); s.waterCoverage = 0f; s.mineralDiversity = 0.3f; s.redox = 0.5f; s.volcanism = 0.2f; s.tectonics = (int)PlanetTexture.TectonicMode.Dead; s.theme = Th(PlanetTexture.ChemTheme.Silicate); break;
                case PlanetCategory.OchreIron: T = -5f; s.type = PlanetType.Desert; Air(Mars, 0.3f); s.waterCoverage = 0f; s.redox = 0.9f; s.theme = Th(PlanetTexture.ChemTheme.Ferrous); s.volcanism = 0.2f; break;
                case PlanetCategory.BasaltPlains: T = 40f; s.type = PlanetType.Desert; Air(Mars, 0.4f); s.waterCoverage = 0f; s.volcanism = 0.75f; s.tectonics = (int)PlanetTexture.TectonicMode.StagnantLid; break;
                case PlanetCategory.VolcanicHighland: T = 30f; Air(Mars, 0.6f); s.waterCoverage = 0.1f; s.volcanism = 0.8f; s.tectonics = (int)PlanetTexture.TectonicMode.MobileLid; break;
                case PlanetCategory.CrateredDead: T = 0f; s.type = PlanetType.Desert; s.massEarth = 0.1f; Airless(); s.bombardment = 0.8f; s.volcanism = 0f; s.tectonics = (int)PlanetTexture.TectonicMode.Dead; s.flareDose = 0.1f; break;
                case PlanetCategory.RegolithDust: T = 0f; s.type = PlanetType.Desert; s.massEarth = 0.3f; Airless(); s.bombardment = 0.2f; s.volcanism = 0.1f; s.flareDose = 0.1f; s.lockMode = 2; break;
                case PlanetCategory.Karst: T = 22f; Air(Earth, 1f); s.waterCoverage = 0.1f; s.mineralDiversity = 0.6f; s.volcanism = 0.2f; s.redox = 0.5f; s.theme = Th(PlanetTexture.ChemTheme.Silicate); break;
                case PlanetCategory.ClayShelf: T = 22f; Air(Earth, 1f); s.waterCoverage = 0.2f; s.mineralDiversity = 0.85f; s.volcanism = 0.2f; s.redox = 0.5f; s.theme = Th(PlanetTexture.ChemTheme.Silicate); break;
                case PlanetCategory.Canyon: T = 10f; s.type = PlanetType.Desert; Air(Mars, 0.3f); s.waterCoverage = 0.1f; s.redox = 0.85f; s.theme = Th(PlanetTexture.ChemTheme.Ferrous); s.volcanism = 0.3f; break;
                case PlanetCategory.Mesa: T = 30f; s.type = PlanetType.Desert; Air(Mars, 0.5f); s.waterCoverage = 0f; s.mineralDiversity = 0.8f; s.redox = 0.5f; s.volcanism = 0.2f; s.theme = Th(PlanetTexture.ChemTheme.Silicate); break;
                case PlanetCategory.Puffy: T = 30f; s.massEarth = 1.5f; s.radiusEarth = 1.75f; Air(Earth, 1f); s.waterCoverage = 0.1f; s.volcanism = 0.2f; break;
                case PlanetCategory.Coreless: T = 30f; s.massEarth = 1.2f; s.radiusEarth = 1.25f; Air(Earth, 1f); s.waterCoverage = 0.1f; s.volcanism = 0.2f; break;

                // ── ocean ──
                case PlanetCategory.GlobalOcean: T = 18f; s.type = PlanetType.Ocean; Air(Earth, 1f); s.waterCoverage = 0.98f; s.liquid = (int)LiquidType.Water; break;
                case PlanetCategory.Archipelago: T = 20f; s.type = PlanetType.Ocean; Air(Earth, 1f); s.waterCoverage = 0.85f; s.massEarth = 1f; s.liquid = (int)LiquidType.Water; break;
                case PlanetCategory.ShallowSea: T = 20f; s.type = PlanetType.Ocean; Air(Earth, 1.5f); s.waterCoverage = 0.85f; s.massEarth = 3f; s.liquid = (int)LiquidType.Water; break;
                case PlanetCategory.EyeballOcean: T = 5f; s.starClass = SpectralClass.M; s.type = PlanetType.Ocean; Air(Earth, 1f); s.waterCoverage = 0.6f; s.lockMode = 1; s.liquid = (int)LiquidType.Water; break;
                case PlanetCategory.StormOcean: T = 20f; s.type = PlanetType.Ocean; Air(Earth, 1.5f); s.waterCoverage = 0.8f; s.rotationHours = 5f; s.lockMode = 2; s.liquid = (int)LiquidType.Water; break;
                case PlanetCategory.IceCappedOcean: T = 0f; s.type = PlanetType.Ocean; Air(Earth, 1f); s.waterCoverage = 0.8f; s.liquid = (int)LiquidType.Water; break;
                case PlanetCategory.SupercriticalWater: T = 420f; s.type = PlanetType.Ocean; Air(Steam, 250f); s.waterCoverage = 0.9f; s.liquid = (int)LiquidType.Supercritical; break;
                case PlanetCategory.Hycean: T = 40f; s.type = PlanetType.Ocean; s.massEarth = 6f; Air(Hyc, 10f); s.waterCoverage = 0.98f; s.liquid = (int)LiquidType.Water; break;
                case PlanetCategory.Brine: T = -10f; s.type = PlanetType.Ocean; Air(Earth, 1f); s.waterCoverage = 0.6f; s.liquid = (int)LiquidType.Brine; break;

                // ── ice ──
                case PlanetCategory.Snowball: T = -50f; s.type = PlanetType.FrozenRock; Air(Earth, 1f); s.waterCoverage = 0.7f; s.volcanism = 0.1f; s.theme = Th(PlanetTexture.ChemTheme.Snowball); break;
                case PlanetCategory.Glacier: T = -35f; s.type = PlanetType.FrozenRock; Air(Earth, 0.8f); s.waterCoverage = 0.2f; s.volcanism = 0.1f; s.theme = Th(PlanetTexture.ChemTheme.Silicate); break;
                case PlanetCategory.EuropaType: T = -160f; s.type = PlanetType.FrozenRock; s.massEarth = 0.008f; Airless(); s.waterCoverage = 0.9f; s.tidalHeat = 0.4f; s.theme = Th(PlanetTexture.ChemTheme.SalineIce); break;
                case PlanetCategory.EnceladusType: T = -190f; s.type = PlanetType.FrozenRock; s.massEarth = 0.02f; Airless(); s.waterCoverage = 0.9f; s.tidalHeat = 0.8f; s.theme = Th(PlanetTexture.ChemTheme.SalineIce); break;
                case PlanetCategory.NitrogenIce: T = -225f; s.type = PlanetType.FrozenRock; s.massEarth = 0.05f; Air(Earth, 0.01f); s.atmoPreset = Titan; s.waterCoverage = 0f; break;
                case PlanetCategory.DryIce: T = -100f; s.type = PlanetType.FrozenRock; Air(Venus, 0.5f); s.waterCoverage = 0f; s.volcanism = 0.1f; break;
                case PlanetCategory.Clathrate: T = -60f; s.type = PlanetType.FrozenRock; Air(Earth, 1f); s.atmoPreset = Titan; s.pressureBar = 0.1f; s.waterCoverage = 0.4f; s.volcanism = 0.1f; break;
                case PlanetCategory.DirtyIce: T = -120f; s.type = PlanetType.FrozenRock; s.massEarth = 0.05f; Airless(); s.waterCoverage = 0.3f; s.theme = Th(PlanetTexture.ChemTheme.Carbonaceous); break;
                case PlanetCategory.FrostDesert: T = -70f; s.type = PlanetType.FrozenRock; Air(Mars, 0.05f); s.waterCoverage = 0f; s.volcanism = 0.1f; s.theme = Th(PlanetTexture.ChemTheme.Silicate); break;
                case PlanetCategory.Cryovolcanic: T = -150f; s.type = PlanetType.FrozenRock; s.massEarth = 0.02f; Airless(); s.waterCoverage = 0.2f; s.volcanism = 0.8f; s.tidalHeat = 0.4f; break;
                case PlanetCategory.MethaneSea: T = -180f; s.type = PlanetType.FrozenRock; s.massEarth = 0.0225f; Air(Titan, 1.5f); s.waterCoverage = 0.2f; s.liquid = (int)LiquidType.Methane; s.theme = Th(PlanetTexture.ChemTheme.Methanic); break;
                case PlanetCategory.AmmoniaOcean: T = -60f; s.type = PlanetType.Ocean; Air(Earth, 1f); s.waterCoverage = 0.7f; s.liquid = (int)LiquidType.Ammonia; s.theme = Th(PlanetTexture.ChemTheme.AmmoniaIce); break;

                // ── toxic ──
                case PlanetCategory.VenusGreenhouse: T = 460f; s.type = PlanetType.Desert; Air(Venus, 92f); s.waterCoverage = 0f; s.greenhouseK = float.NaN; break;
                case PlanetCategory.SulfuricCloud: T = 250f; s.type = PlanetType.Desert; Air(SO2, 10f); s.waterCoverage = 0f; s.volcanism = 0.6f; break;
                case PlanetCategory.Sulfur: T = 60f; s.type = PlanetType.Desert; Air(SO2, 0.3f); s.waterCoverage = 0f; s.volcanism = 0.6f; s.theme = Th(PlanetTexture.ChemTheme.Sulfuric); break;
                case PlanetCategory.Chlorine: T = 30f; Air(Chl, 1f); s.waterCoverage = 0.3f; break;
                case PlanetCategory.Copper: T = 30f; s.type = PlanetType.Desert; Air(Mars, 0.5f); s.waterCoverage = 0.1f; s.theme = Th(PlanetTexture.ChemTheme.Cupric); break;
                case PlanetCategory.Halide: T = 40f; s.type = PlanetType.Desert; Air(Mars, 0.5f); s.waterCoverage = 0.05f; s.theme = Th(PlanetTexture.ChemTheme.Halide); break;
                case PlanetCategory.SaltFlat: T = 40f; s.type = PlanetType.Desert; Air(Earth, 0.8f); s.waterCoverage = 0.05f; s.theme = Th(PlanetTexture.ChemTheme.Evaporite); break;
                case PlanetCategory.Tholin: T = -170f; s.type = PlanetType.FrozenRock; Air(Titan, 1.5f); s.waterCoverage = 0f; s.theme = Th(PlanetTexture.ChemTheme.Tholin); break;
                case PlanetCategory.PhotochemicalSmog: T = -80f; s.type = PlanetType.FrozenRock; Air(Titan, 1.5f); s.waterCoverage = 0f; break;
                case PlanetCategory.AmmoniaHaze: T = -10f; Air(NH3, 2f); s.waterCoverage = 0.2f; s.liquid = (int)LiquidType.Water; break;
                case PlanetCategory.MercuryVapor: T = 320f; s.type = PlanetType.Desert; Air(Venus, 2f); s.atmoPreset = Mars; s.waterCoverage = 0f; s.theme = Th(PlanetTexture.ChemTheme.Metallic); break;
                case PlanetCategory.WaterVapor: T = 250f; s.type = PlanetType.Ocean; Air(Steam, 30f); s.waterCoverage = 0.5f; s.liquid = (int)LiquidType.None; break;
                case PlanetCategory.HeliumWorld: T = 50f; s.massEarth = 8f; Air(He, 5f); s.waterCoverage = 0f; break;

                // ── exotic composition ──
                case PlanetCategory.Carbon: T = 60f; s.type = PlanetType.Desert; Air(Mars, 0.5f); s.waterCoverage = 0f; s.redox = 0.4f; s.theme = Th(PlanetTexture.ChemTheme.Carbonaceous); break;
                case PlanetCategory.Tar: T = 60f; s.type = PlanetType.Desert; Air(Titan, 1.5f); s.waterCoverage = 0f; s.redox = 0.1f; s.theme = Th(PlanetTexture.ChemTheme.Carbonaceous); break;
                case PlanetCategory.Diamond: T = 150f; s.type = PlanetType.Desert; s.massEarth = 6f; Air(Venus, 10f); s.waterCoverage = 0f; s.theme = Th(PlanetTexture.ChemTheme.Carbonaceous); break;
                case PlanetCategory.IronWorld: T = 60f; s.type = PlanetType.Desert; s.massEarth = 0.6f; Air(Mars, 0.1f); s.waterCoverage = 0f; s.volcanism = 0.2f; s.theme = Th(PlanetTexture.ChemTheme.Metallic); break;
                case PlanetCategory.SuperMercury: T = 200f; s.type = PlanetType.Desert; s.massEarth = 5f; s.radiusEarth = 1.4f; Air(Mars, 0.1f); s.waterCoverage = 0f; s.volcanism = 0.2f; s.theme = Th(PlanetTexture.ChemTheme.Metallic); break;
                case PlanetCategory.IronSnow: T = 180f; s.type = PlanetType.Desert; s.massEarth = 0.8f; Air(Mars, 0.2f); s.waterCoverage = 0f; s.volcanism = 0.7f; s.theme = Th(PlanetTexture.ChemTheme.Metallic); break;
                case PlanetCategory.Corundum: T = 300f; s.type = PlanetType.Desert; Air(Mars, 0.2f); s.waterCoverage = 0f; s.theme = Th(PlanetTexture.ChemTheme.Corundum); break;

                // ── living ──
                case PlanetCategory.Garden: T = 16f; s.habMode = 1; Air(Earth, 1f); s.waterCoverage = 0.6f; s.lockMode = 2; s.rotationHours = 24f; break;
                case PlanetCategory.Steppe: T = 32f; s.habMode = 1; Air(Earth, 1f); s.waterCoverage = 0.25f; s.lockMode = 2; s.rotationHours = 24f; break;
                case PlanetCategory.RedVegetation: T = 15f; s.starClass = SpectralClass.K; s.starMassT = 0.3f; s.habMode = 1; Air(Earth, 1f); s.waterCoverage = 0.55f; s.lockMode = 2; s.rotationHours = 30f; break;
                case PlanetCategory.PurpleRetinal: T = 15f; s.starClass = SpectralClass.A; s.starMassT = 0.2f; s.ageFrac = 0.4f; s.habMode = 1; Air(Earth, 1f); s.waterCoverage = 0.55f; s.lockMode = 2; s.rotationHours = 24f; break;
                case PlanetCategory.BlackPlant: T = 12f; s.starClass = SpectralClass.M; s.starMassT = 0.5f; s.habMode = 1; Air(Earth, 1.2f); s.waterCoverage = 0.55f; s.lockMode = 2; s.rotationHours = 40f; break;
                case PlanetCategory.FungalLichen: T = float.NaN; s.distanceAU = 1.9f; s.greenhouseK = 75f; s.habMode = 1; Air(Earth, 2f); s.greenhouseK = 75f; s.waterCoverage = 0.5f; s.lockMode = 2; s.rotationHours = 24f; break;
                case PlanetCategory.MicrobialMat: T = 20f; s.habMode = 1; s.type = PlanetType.Ocean; Air(Earth, 1f); s.waterCoverage = 0.94f; s.lockMode = 2; s.rotationHours = 24f; break;
                case PlanetCategory.Oxygenating: T = 18f; s.habMode = 1; Air(Mars, 1f); s.redox = 0.3f; s.waterCoverage = 0.6f; s.lockMode = 2; s.rotationHours = 24f; break;
                case PlanetCategory.MethaneBiosphere: T = 15f; s.habMode = 1; Air(Titan, 1f); s.waterCoverage = 0.6f; s.lockMode = 2; s.rotationHours = 24f; break;
                case PlanetCategory.Bioluminescent: T = 16f; s.habMode = 1; Air(Earth, 1f); s.waterCoverage = 0.55f; s.lockMode = 2; s.rotationHours = 400f; break;
                case PlanetCategory.TwilightRing: T = 10f; s.starClass = SpectralClass.M; s.habMode = 1; Air(Earth, 1.5f); s.waterCoverage = 0.5f; s.lockMode = 1; break;

                // ── orbit & history ──
                case PlanetCategory.Eyeball: T = 0f; s.starClass = SpectralClass.M; Air(Mars, 0.6f); s.waterCoverage = 0.1f; s.volcanism = 0.3f; s.lockMode = 1; break;
                case PlanetCategory.Resonance32: T = 20f; Air(Mars, 0.6f); s.waterCoverage = 0.1f; s.volcanism = 0.3f; s.lockMode = 3; break;
                case PlanetCategory.FlareScoured: T = 40f; s.starClass = SpectralClass.M; s.type = PlanetType.Desert; Airless(); s.flareDose = 0.9f; s.lockMode = 1; break;
                case PlanetCategory.RogueCaptured: T = float.NaN; s.distanceAU = 400f; s.type = PlanetType.FrozenRock; Airless(); s.flareDose = 0f; s.lockMode = 2; s.waterCoverage = 0.1f; break;

                // ── giants ──
                case PlanetCategory.Jovian: T = float.NaN; s.type = PlanetType.GasGiant; s.massEarth = 318f; s.distanceAU = 5.2f; s.rotationHours = 10f; break;
                case PlanetCategory.HotJupiter: T = float.NaN; s.type = PlanetType.GasGiant; s.massEarth = 300f; s.distanceAU = 0.05f; s.lockMode = 1; break;
                case PlanetCategory.IceGiant: T = float.NaN; s.type = PlanetType.IceGiant; s.massEarth = 17f; s.distanceAU = 25f; s.rotationHours = 16f; break;
                case PlanetCategory.SubNeptune: T = float.NaN; s.type = PlanetType.IceGiant; s.massEarth = 6f; s.distanceAU = 0.6f; break;
                case PlanetCategory.AmmoniaCloudGiant: T = -100f; s.type = PlanetType.GasGiant; s.massEarth = 250f; s.distanceAU = 3f; s.rotationHours = 10f; break;
                case PlanetCategory.Superstorm: T = float.NaN; s.type = PlanetType.GasGiant; s.massEarth = 600f; s.distanceAU = 2f; s.rotationHours = 8f; break;
                case PlanetCategory.HeliumGiant: T = float.NaN; s.type = PlanetType.GasGiant; s.massEarth = 60f; s.distanceAU = 0.3f; s.ageFrac = 0.95f; break;
            }
            if (!float.IsNaN(s.pressureBar) && s.atmoPreset >= 0 && c != PlanetCategory.FungalLichen) s.greenhouseK = float.NaN;   // warming from the air
            return T;
        }

        public static PlanetCategory CategoryOf(StarSystem sys)
        {
            var p = sys.planets[0];
            ulong ps = DetRng.Hash(sys.seed, 1UL);
            var th = PlanetData.IsRocky(p.type) ? PlanetTexture.Chem(p, ps).theme : PlanetTexture.ChemTheme.Silicate;
            return PlanetCategories.Classify(p, th, ps).cat;
        }

        /// Recipe → tune the distance to the target temperature → search seeds until the classifier agrees.
        public static PlanetSpec Solve(PlanetSpec baseline, PlanetCategory want, out string msg)
        {
            PlanetSpec best = null;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                var s = baseline.Clone();
                s.seed = attempt == 0 ? baseline.seed : DetRng.Hash(baseline.seed, (ulong)(attempt * 7919));
                float T = Apply(s, want);
                StarSystem sys = null;
                for (int it = 0; it < 8; it++)
                {
                    sys = SystemGenerator.GenerateCustom(s);
                    if (float.IsNaN(T) || sys.planets.Count == 0) break;
                    float cur = sys.planets[0].meanTempC;
                    if (Mathf.Abs(cur - T) < Mathf.Max(4f, Mathf.Abs(T + 273f) * 0.03f)) break;
                    float k = Mathf.Pow(Mathf.Max(cur + 273f, 5f) / Mathf.Max(T + 273f, 5f), 2f);   // T ∝ d^-½
                    s.distanceAU = Mathf.Clamp(s.distanceAU * k, 0.004f, 600f);
                }
                if (best == null) best = s;
                if (sys != null && sys.planets.Count > 0 && CategoryOf(sys) == want)
                {
                    msg = $"<color=#9f9>✓ {PlanetCategories.Name(want)}</color>" + (attempt > 0 ? $" (seed search: {attempt + 1} tries)" : "");
                    return s;
                }
            }
            var got = CategoryOf(SystemGenerator.GenerateCustom(best));
            msg = $"<color=#fc8>closest: {PlanetCategories.Name(got)}</color> — the recipe for {PlanetCategories.Name(want)} didn't classify; tweak parameters";
            return best;
        }
    }
}
