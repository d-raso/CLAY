using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.CellStage
{
    /// <summary>
    /// Everything the cell stage knows about WHERE it is: the planet and the exact tide pool the player clicked.
    /// Derived once on entry from the planet's physics (PlanetPhysics / PlanetClimate) and the local surface
    /// sample. Nothing here is ever shown to the player (CellStage_Decisions §1) — it only shapes the chemistry,
    /// hazards and pace of play.
    /// </summary>
    public sealed class CellStageContext
    {
        public PlanetData planet;
        public ulong planetSeed;
        public ulong poolSeed;               // this particular pool (planet seed × location)

        // ── the pool's environment ──
        public LiquidType solvent = LiquidType.Water;
        public float tempC = 20f;            // local mean temperature
        public float tempSwingC = 8f;        // day/night swing
        public float uv = 0.5f;              // 0..1 surface UV (thin air / no ozone / flare-heavy star → high)
        public float tidalRange = 0.6f;      // 0..1 how hard the wet–dry cycle bites (moons, sea, eccentricity)
        public float minerals = 0.5f;        // 0..1 dissolved mineral richness (catalytic surfaces)
        public float ventActivity = 0.2f;    // 0..1 geothermal energy nearby
        public float redox = 0.3f;           // 0 reducing … 1 oxidising (early worlds are reducing)
        public float organicRichness = 0.5f; // 0..1 how much prebiotic soup there is (lightning, impacts, vents)

        /// Reaction-rate multiplier from temperature and solvent (Arrhenius-ish): cold methane seas are SLOW.
        public float ReactionRate
        {
            get
            {
                float k = Mathf.Exp((tempC - 20f) / 25f);
                if (solvent == LiquidType.Methane) k *= 0.25f;
                else if (solvent == LiquidType.Ammonia) k *= 0.6f;
                else if (solvent == LiquidType.Brine) k *= 0.85f;
                return Mathf.Clamp(k, 0.08f, 4f);
            }
        }

        /// A neutral context so the cell stage can be launched standalone (tests, main-menu shortcut).
        public static CellStageContext Default(ulong seed = 1) => new CellStageContext { planetSeed = seed, poolSeed = DetRng.Hash(seed, 0x7001UL) };

        /// Build from a planet + the surface spot the player chose.
        public static CellStageContext FromPlanet(PlanetData p, ulong planetSeed, LocalClimate lc, double worldX, double worldZ)
        {
            var c = new CellStageContext { planet = p, planetSeed = planetSeed };
            c.poolSeed = DetRng.Hash(planetSeed, (ulong)((long)(worldX / 10.0) * 73856093L ^ (long)(worldZ / 10.0) * 19349663L));
            var cl = PlanetClimate.Derive(p);
            c.solvent = PlanetPhysics.Liquid(p, cl.pressureBar);
            if (c.solvent == LiquidType.None) c.solvent = LiquidType.Water;
            c.tempC = lc.tMeanC;
            c.tempSwingC = Mathf.Max(1f, (lc.tMaxC - lc.tMinC) * 0.5f);
            float air = Mathf.Clamp01(cl.pressureBar);
            c.uv = Mathf.Clamp01(0.35f + (1f - air) * 0.4f + PlanetPhysics.FlareDose(p) * 0.4f - PlanetPhysics.Composition(p).o2 * 0.8f);
            c.tidalRange = Mathf.Clamp01(0.3f + p.waterCoverage * 0.3f + p.tidalHeat * 0.4f + (p.moons != null ? p.moons.Count * 0.08f : 0f));
            c.minerals = Mathf.Clamp01(0.3f + p.mineralDiversity * 0.6f);
            c.ventActivity = Mathf.Clamp01(p.volcanism * 0.8f + p.tidalHeat * 0.3f);
            c.redox = PlanetPhysics.Redox(p);
            c.organicRichness = Mathf.Clamp01(0.3f + p.volcanism * 0.25f + p.bombardment * 0.25f + (1f - c.redox) * 0.3f);
            return c;
        }
    }
}
