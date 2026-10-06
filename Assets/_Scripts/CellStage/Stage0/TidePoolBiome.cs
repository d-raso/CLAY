using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// The tide pool's BIOME (CellStage_SubStages.md, "S0 Biomes"): chosen from the planet's causal parameters plus a
    /// little per-pool chance, and the ONE place every pool system reads its character from — basin shape, tide,
    /// water and floor colour, formations (vent chimneys / carbonate towers), suspended matter, monomer mix and
    /// density, the seabed surface for hands-on assembly, hazards, UV, reaction speed and viroid RNA stability.
    ///
    /// First pass (4 of the 10 documented biomes): Sunlit Tidal Flat, Warm Hydrothermal Vent Field, Alkaline Hot
    /// Spring (Lost City type), Clay Mineral Shelf. Non-water liquids fall back to the tidal flat (tinted by the
    /// solvent) until the second pass adds brine / pyrite / ammonia / methane / cryovolcanic / UV-crevice.
    /// </summary>
    public sealed class TidePoolBiome
    {
        public enum Kind { SunlitTidalFlat, HydrothermalVentField, AlkalineHotSpring, ClayMineralShelf }

        public Kind kind;
        public string displayName, tagline;
        public static int Forced = -1;      // DEV (F10): force this Kind for the next pool
        public const int KindCount = 4;

        // basin & water
        public float tideMul = 1f;          // tidal amplitude
        public float bowlDepth = 1f;        // basin depth (deeper = more permanent water)
        public float rockiness = 1f;        // outcrops
        public float uvMul = 1f;            // UV reaching the water (turbid / dark water shields)
        public float rateMul = 1f;          // chemistry speed
        public float rnaDecayMul = 1f;      // free RNA (viroid) degradation
        public Color waterShallow, waterDeep, rock, rockHigh, accent, crust;
        public float lookBlend;
        public Vector4 groundMix;           // sand, mud, rock, crust: patch weights on the floor
        public float groundSeed;             // how strongly the biome colours override the planet palette

        // water movement: how this pool's water behaves
        public float flowMul = 1f;          // the environment's background currents
        public float tidalCurrent;          // ebb / flood sweeping across the pool (strongest in the shallows)
        public float turbulence;            // eddies
        public float convection;            // rising, swirling plumes around the vents
        public float waveEnergy = 1f;       // surface chop → caustic strength (calm water = faint caustics)
        public Vector2 tideAxis;

        // formations
        public Vents.Style ventStyle;
        public int ventCount;
        public float ventStrength;

        // soup
        public float densityMul = 1f;
        public readonly float[] kindMul = { 1, 1, 1, 1, 1, 1, 1 };   // per MoteKind abundance
        public Suspended.Matter matter;
        public float suspendedMul = 1f;
        public SeabedAssembly.Surface surface;

        // hazards
        public bool thermalPulses;          // vent field: periodic surges widen the hot core
        public bool phGradient;             // alkaline spring: membranes destabilise away from the towers

        public static TidePoolBiome For(CellStageContext ctx)
        {
            var r = new DetRng(DetRng.Hash(ctx.poolSeed, 0xB10E5UL));
            if (Forced >= 0) return Make((Kind)Forced, ctx, ref r);
            float vent = ctx.ventActivity, min = ctx.minerals;
            bool water = ctx.solvent == LiquidType.Water || ctx.solvent == LiquidType.Brine;

            // weights from the planet + this spot; then a weighted pick (so neighbouring pools can differ)
            float wFlat = 1.0f + ctx.tidalRange * 0.8f + ctx.uv * 0.4f;
            float wVent = water ? Mathf.Clamp01((vent - 0.35f) * 2.5f) * 2.2f : 0f;
            float wSpring = water ? Mathf.Clamp01(1f - Mathf.Abs(vent - 0.38f) * 3f) * (0.4f + min) * 1.4f : 0f;
            float wClay = water ? Mathf.Clamp01((min - 0.45f) * 2.5f) * (1.2f - vent) * 1.6f : 0f;
            float total = wFlat + wVent + wSpring + wClay, pick = r.Value * total;
            Kind k = pick < wVent ? Kind.HydrothermalVentField
                   : pick < wVent + wSpring ? Kind.AlkalineHotSpring
                   : pick < wVent + wSpring + wClay ? Kind.ClayMineralShelf
                   : Kind.SunlitTidalFlat;
            return Make(k, ctx, ref r);
        }

        static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        public static TidePoolBiome Make(Kind k, CellStageContext ctx, ref DetRng r)
        {
            var b = new TidePoolBiome { kind = k };
            switch (k)
            {
                case Kind.HydrothermalVentField:
                    b.displayName = "Warm Hydrothermal Vent Field"; b.tagline = "black smokers, sulfide plumes, rock — chemosynthesis pays here";
                    b.tideMul = 0.35f; b.bowlDepth = 1.35f; b.rockiness = 1.3f;
                    b.uvMul = 0.4f; b.rateMul = 1.3f; b.rnaDecayMul = 2f;
                    b.waterShallow = C(0.2f, 0.32f, 0.36f); b.waterDeep = C(0.03f, 0.06f, 0.09f);
                    b.rock = C(0.16f, 0.14f, 0.13f); b.rockHigh = C(0.32f, 0.28f, 0.24f); b.accent = C(0.55f, 0.36f, 0.16f); b.crust = C(0.82f, 0.74f, 0.45f);
                    b.lookBlend = 0.65f;
                    b.ventStyle = Vents.Style.BlackSmoker; b.ventCount = 4 + r.RangeInt(0, 4); b.ventStrength = 0.75f;
                    b.densityMul = 1.0f; b.kindMul[(int)MoteKind.Clay] = 0.5f; b.kindMul[(int)MoteKind.Organic] = 1.3f;
                    b.matter = Suspended.Matter.SulfurGrains; b.suspendedMul = 1.4f;
                    b.surface = SeabedAssembly.Surface.Basalt;
                    b.thermalPulses = true;
                    b.flowMul = 0.7f; b.tidalCurrent = 0.2f; b.turbulence = 1.0f; b.convection = 1.6f; b.waveEnergy = 0.75f;
                    break;
                case Kind.AlkalineHotSpring:
                    b.displayName = "Alkaline Hot Spring"; b.tagline = "carbonate towers, white crusts, fatty acids — membranes form easily";
                    b.tideMul = 0.45f; b.bowlDepth = 1.15f; b.rockiness = 0.7f;
                    b.uvMul = 0.85f; b.rateMul = 1.1f; b.rnaDecayMul = 0.75f;
                    b.waterShallow = C(0.62f, 0.72f, 0.7f); b.waterDeep = C(0.2f, 0.32f, 0.34f);
                    b.rock = C(0.62f, 0.6f, 0.55f); b.rockHigh = C(0.86f, 0.84f, 0.78f); b.accent = C(0.78f, 0.74f, 0.62f); b.crust = C(0.95f, 0.94f, 0.9f);
                    b.lookBlend = 0.6f;
                    b.ventStyle = Vents.Style.CarbonateTower; b.ventCount = 3 + r.RangeInt(0, 3); b.ventStrength = 0.45f;
                    b.densityMul = 1.1f; b.kindMul[(int)MoteKind.Lipid] = 1.8f;          // fatty acids in abundance
                    b.matter = Suspended.Matter.CarbonateFlocs; b.suspendedMul = 1.2f;
                    b.surface = SeabedAssembly.Surface.Carbonate;
                    b.phGradient = true;
                    b.flowMul = 0.35f; b.tidalCurrent = 0.25f; b.turbulence = 0.15f; b.convection = 0.6f; b.waveEnergy = 0.4f;   // still, steaming
                    break;
                case Kind.ClayMineralShelf:
                    b.displayName = "Clay Mineral Shelf"; b.tagline = "amber silty water, mud flats — clay catalyses copying";
                    b.tideMul = 0.8f; b.bowlDepth = 0.75f; b.rockiness = 0.35f;
                    b.uvMul = 0.5f; b.rateMul = 1f; b.rnaDecayMul = 0.8f;
                    b.waterShallow = C(0.6f, 0.52f, 0.36f); b.waterDeep = C(0.26f, 0.2f, 0.12f);      // amber-tinted, silty
                    b.rock = C(0.5f, 0.44f, 0.37f); b.rockHigh = C(0.66f, 0.6f, 0.52f); b.accent = C(0.58f, 0.5f, 0.42f); b.crust = C(0.82f, 0.78f, 0.7f);
                    b.lookBlend = 0.6f;
                    b.ventStyle = Vents.Style.BlackSmoker; b.ventCount = 1; b.ventStrength = 0.35f;
                    b.densityMul = 1.35f; b.kindMul[(int)MoteKind.Clay] = 2.5f;           // the easy biome: everything, densest
                    b.matter = Suspended.Matter.ClayFlocs; b.suspendedMul = 1.8f;
                    b.surface = SeabedAssembly.Surface.Clay;
                    b.flowMul = 0.2f; b.tidalCurrent = 0.45f; b.turbulence = 0.05f; b.convection = 0f; b.waveEnergy = 0.25f;     // glassy, sluggish, silty
                    break;
                default:
                    b.displayName = "Sunlit Tidal Flat"; b.tagline = "clear water, big tides, strong light and UV";
                    b.tideMul = 1.25f; b.bowlDepth = 0.85f; b.rockiness = 1.1f;
                    b.uvMul = 1.25f; b.rateMul = 1f; b.rnaDecayMul = 1f;
                    b.waterShallow = C(0.42f, 0.66f, 0.64f); b.waterDeep = C(0.08f, 0.2f, 0.26f);     // clear
                    b.rock = C(0.5f, 0.45f, 0.38f); b.rockHigh = C(0.72f, 0.66f, 0.56f); b.accent = C(0.6f, 0.52f, 0.4f); b.crust = C(0.9f, 0.88f, 0.82f);
                    b.lookBlend = 0.35f;
                    b.ventStyle = Vents.Style.BlackSmoker; b.ventCount = 1 + (ctx.ventActivity > 0.3f ? 1 : 0); b.ventStrength = 0.5f;
                    b.densityMul = 1f;
                    b.matter = Suspended.Matter.SilicaDebris; b.suspendedMul = 0.8f;
                    b.flowMul = 1.3f; b.tidalCurrent = 1.6f; b.turbulence = 0.6f; b.convection = 0f; b.waveEnergy = 1.35f;      // the tide pours in and drains out
                    b.surface = SeabedAssembly.Surface.Carbonate;
                    break;
            }
            float ta = r.Range(0f, 6.283f); b.tideAxis = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta));
            b.groundMix = k switch
            {
                Kind.HydrothermalVentField => new Vector4(0.2f, 0.15f, 0.55f, 0.1f),
                Kind.AlkalineHotSpring => new Vector4(0.2f, 0.1f, 0.2f, 0.5f),
                Kind.ClayMineralShelf => new Vector4(0.2f, 0.55f, 0.15f, 0.1f),
                _ => new Vector4(0.45f, 0.2f, 0.25f, 0.1f),
            };
            for (int i = 0; i < 4; i++) b.groundMix[i] *= r.Range(0.6f, 1.4f);
            b.groundSeed = r.Range(0f, 100f);
            // non-water liquids keep their own colours (methane amber, ammonia violet) — the biome only shapes the basin
            if (ctx.solvent == LiquidType.Methane) b.matter = Suspended.Matter.TholinHaze;
            if (ctx.solvent != LiquidType.Water && ctx.solvent != LiquidType.Brine) b.lookBlend *= 0.3f;
            return b;
        }

        /// Pool colours: the biome's own palette blended over the planet's (the pool still agrees with its planet).
        public void ApplyLook(Material m)
        {
            void Blend(string prop, Color c) { m.SetColor(prop, Color.Lerp(m.GetColor(prop), c, lookBlend)); }
            Blend("_WaterShallow", waterShallow); Blend("_WaterDeep", waterDeep);
            Blend("_Rock", rock); Blend("_RockHigh", rockHigh); Blend("_Accent", accent); Blend("_Crust", crust);
            var sh = m.GetColor("_WaterShallow");
            m.SetColor("_Glow", Color.Lerp(sh, Color.white, 0.45f) * 1.1f);
            m.SetVector("_GroundMix", groundMix);
            m.SetFloat("_GroundSeed", groundSeed);
        }
    }
}
