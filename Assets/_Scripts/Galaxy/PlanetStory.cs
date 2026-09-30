using System.Text;
using UnityEngine;

namespace CLAY.Galaxy
{
    // Procedurally writes a short "story" for a planet — how it formed and what it's like now — derived entirely
    // from its generated physics/chemistry. Deterministic. The same parameters that drive the visuals drive the
    // narrative, so the story EXPLAINS what you see (why the seas are rust-red, why it's airless, etc.).
    public static class PlanetStory
    {
        public static string Generate(StarData star, PlanetData p, ulong systemSeed)
        {
            var rng = new DetRng(DetRng.Hash(systemSeed, (ulong)(uint)(p.index * 2654435761u + 17u)));
            var sb = new StringBuilder();
            // The visual archetype (same seed the bake uses) so the story matches what's rendered.
            ulong pseed = DetRng.Hash(systemSeed, (ulong)(uint)(p.index + 1));
            var cp = PlanetTexture.Chem(p, pseed);

            // ── Formation ─────────────────────────────────────────────────────────────────
            float frost = 2.7f * Mathf.Sqrt(Mathf.Max(star.luminosity, 1e-3f));
            bool beyondFrost = p.semiMajorAxisAU > frost;
            string starAge = star.stellarAgeGyr > 9f ? "ancient" : star.stellarAgeGyr > 4f ? "mature" : "young";
            string cls = star.ClassLabel.ToLower();

            if (!p.Rocky)
            {
                sb.Append(p.type == PlanetType.GasGiant
                    ? $"A gas giant that accreted a vast hydrogen–helium envelope beyond the {starAge} {cls} star's snow line"
                    : $"An ice giant of water, ammonia and methane ices, grown in the cold outer system");
                sb.Append($", {AU(p.semiMajorAxisAU)} from its sun. ");

                switch (PlanetTexture.GiantSubtype(p, pseed))
                {
                    case PlanetTexture.GiantType.HotJupiter:
                        sb.Append("Migrated in perilously close, it is scorched into a puffed, glowing Hot Jupiter, its atmosphere racing in fierce dark bands. "); break;
                    case PlanetTexture.GiantType.Superstorm:
                        sb.Append("Colossal storm systems churn across it, a single great vortex dominating a whole hemisphere. "); break;
                    case PlanetTexture.GiantType.HeliumGiant:
                        sb.Append("Hydrogen has rained out of its upper layers over the aeons, leaving a pale, almost featureless helium haze. "); break;
                    case PlanetTexture.GiantType.SubNeptune:
                        sb.Append("Only a gas dwarf, it is swaddled in a thick, muted photochemical haze that smooths away any banding. "); break;
                }
            }
            else
            {
                string birth = beyondFrost
                    ? Pick(rng, "condensed as an icy world in the frigid outer disk",
                                "coalesced from volatile-rich planetesimals far from its star")
                    : Pick(rng, "assembled from rock and metal in the warm inner disk",
                                "forged close to its star from refractory dust");
                sb.Append($"This world {birth}, {AU(p.semiMajorAxisAU)} from a {starAge} {cls} star. ");

                if (beyondFrost && p.meanTempC > 0f)
                    sb.Append(Pick(rng, "It later migrated inward, its ices thawing into seas. ",
                                        "Orbital migration carried it sunward, warming its frozen mantle. "));
                if (p.volcanism > 0.6f)
                    sb.Append(Pick(rng, "Tidal flexing and radiogenic heat keep its interior molten and its crust young. ",
                                        "Relentless volcanism resurfaces it faster than craters can accumulate. "));
            }

            // ── Cataclysm / character ─────────────────────────────────────────────────────
            if (p.Rocky && rng.Value < 0.35f)
                sb.Append(Pick(rng, "A giant impact early in its history stripped much of its crust. ",
                                    "It bears a great basin from an ancient collision. ",
                                    "A late bombardment left it scarred and metal-enriched. "));

            // ── Tectonic regime (matches the rendered relief/cratering) ───────────────────
            if (p.Rocky)
                switch (PlanetTexture.Tectonics(p))
                {
                    case PlanetTexture.TectonicMode.MobileLid:
                        sb.Append(Pick(rng, "Active plate tectonics fold long mountain belts and keep its crust young. ",
                                            "Drifting plates raise linear ranges and recycle the surface, so few craters survive. ")); break;
                    case PlanetTexture.TectonicMode.HeatPipe:
                        sb.Append("Heat-pipe volcanism repaves it continuously — a young, crater-free surface of fresh lava. "); break;
                    case PlanetTexture.TectonicMode.StagnantLid:
                        sb.Append(Pick(rng, "A single stagnant lid caps its interior; the ancient crust is pocked with impact craters. ",
                                            "With no plate motion, its old crust preserves the scars of eons. ")); break;
                    case PlanetTexture.TectonicMode.Dead:
                        sb.Append("Geologically dead, its saturation-cratered surface has scarcely changed in billions of years. "); break;
                }

            // ── Cross-body history (matches the rendered ejecta / fissures) ───────────────
            if (p.Rocky && p.bombardment > 0.5f)
                sb.Append(Pick(rng, "Sitting beside a debris belt, it is pelted by frequent impacts — its crust is saturated with craters and bright ejecta. ",
                                    "A neighbouring belt rains rock onto it; fresh craters and ejecta rays cover the ground. "));
            if (p.Rocky && p.tidalHeat > 0.5f)
                sb.Append(Pick(rng, "Tidal flexing from its neighbours keeps the interior molten, cracking the surface with glowing fissures. ",
                                    "Gravitational kneading by nearby worlds heats it from within, fracturing the crust into incandescent rifts. "));

            // ── Surface character (from the actual rendered archetype) ────────────────────
            string surf = cp.theme switch
            {
                PlanetTexture.ChemTheme.Metallic => "Its surface is bare, cratered metallic iron. ",
                PlanetTexture.ChemTheme.Snowball => "It is frozen over entirely — a cracked white shell of ice. ",
                PlanetTexture.ChemTheme.Corundum => "Aluminium-rich crust has crystallised into deep-red corundum — a ruby world. ",
                PlanetTexture.ChemTheme.Carbonaceous => "Graphite and tar darken it to near-black. ",
                PlanetTexture.ChemTheme.Cupric => "Oxidised copper minerals wash it in verdigris green. ",
                PlanetTexture.ChemTheme.Halide => "Pale halogen salt crusts sheathe the ground. ",
                PlanetTexture.ChemTheme.Evaporite => "Vast white salt flats mark where ancient seas dried away. ",
                PlanetTexture.ChemTheme.Pelagic => "A single ocean spans the whole globe, broken by almost no land. ",
                PlanetTexture.ChemTheme.Sulfuric => "Sulfur deposits streak it yellow and orange. ",
                _ => "",
            };
            if (surf.Length > 0) sb.Append(surf);

            // ── Tidal locking (eyeball world) ─────────────────────────────────────────────
            if (p.tidallyLocked)
                sb.Append(Pick(rng,
                    "It is tidally locked: one hemisphere bakes in eternal daylight while the far side lies in perpetual frozen night, a ring of habitable twilight between them. ",
                    "Locked to its star, it keeps one scorched face always sunward and one frozen in endless dark. "));
            else if (p.Rocky)
            {
                if (p.axialTiltDeg > 90f)
                    sb.Append(Pick(rng, "It spins retrograde, turning against the direction of its orbit. ",
                                        "Its rotation runs backwards relative to its path around the star. "));
                else if (p.axialTiltDeg > 45f)
                    sb.Append(Pick(rng, "A steep axial tilt throws it into violent seasons, its ice caps wandering with the year. ",
                                        "Tipped far on its axis, whole hemispheres swing from glare to darkness each orbit. "));
                if (p.rotationHours < 6f)
                    sb.Append(Pick(rng, "It spins fast enough to bulge at the equator and shear its weather into tight bands. ",
                                        "A short day flattens it into an oblate spheroid streaked by racing winds. "));
                else if (p.rotationHours > 250f)
                    sb.Append("Its day drags on so slowly that the sunlit face roasts while the far side deep-freezes. ");
            }

            // ── Atmosphere ────────────────────────────────────────────────────────────────
            if (p.Rocky)
            {
                if (!PlanetTexture.HasAtmosphere(p))
                    sb.Append(p.mass < 0.4f
                        ? "Too small to hold onto gas, it is an airless, exposed rock. "
                        : "Stellar heating long ago boiled away its air, leaving it airless. ");
                else
                {
                    string atmo = p.meanTempC > 200f ? "a crushing, hazy greenhouse atmosphere"
                                : p.meanTempC < -90f ? "a cold methane–nitrogen haze"
                                : p.habClass == HabClass.Habitable ? "a clear nitrogen–oxygen atmosphere"
                                : p.volcanism > 0.6f ? "a sulfurous, volcanic atmosphere"
                                : "a thin, tenuous atmosphere";
                    sb.Append($"It retains {atmo}. ");
                }
            }

            // ── Seas (described from the ACTUAL rendered archetype so the text matches the colour) ──
            if (p.Rocky && cp.hasLiquid && p.waterCoverage > 0.05f)
            {
                string sea = cp.theme switch
                {
                    PlanetTexture.ChemTheme.Ferrous => "rust-red seas, stained by dissolved iron",
                    PlanetTexture.ChemTheme.Cupric => "copper-green mineral seas",
                    PlanetTexture.ChemTheme.Carbonaceous => "inky, carbon-black seas",
                    PlanetTexture.ChemTheme.Tholin => "dark tarry seas of liquid hydrocarbons",
                    PlanetTexture.ChemTheme.Methanic => "black methane–ethane seas",
                    PlanetTexture.ChemTheme.AmmoniaIce => "pale ammonia–water seas",
                    PlanetTexture.ChemTheme.Biosphere => p.waterChemistry switch
                    {
                        WaterChemistry.Iron => "rust-tinged water seas",
                        WaterChemistry.Sulfide => "murky sulfur-yellow water seas",
                        WaterChemistry.Phosphate => "vivid teal water seas",
                        _ => "clear blue water seas",
                    },
                    _ => "liquid-water seas",
                };
                string cover = p.waterCoverage > 0.7f ? "Global" : p.waterCoverage > 0.35f ? "Broad" : "Scattered";
                sb.Append($"{cover} {sea} cover {(int)(p.waterCoverage * 100)}% of the surface. ");
            }
            else if (cp.theme == PlanetTexture.ChemTheme.Lava || cp.theme == PlanetTexture.ChemTheme.Sulfuric)
                sb.Append("Seas of glowing molten rock spread across its surface. ");
            else if (p.Rocky && p.meanTempC > 60f && p.volcanism > 0.5f)
                sb.Append("Lakes of molten rock pool in its volcanic lowlands. ");

            // ── Biosphere pigment (tuned to the star's spectrum, matches the rendered vegetation) ──
            if (cp.theme == PlanetTexture.ChemTheme.Biosphere)
            {
                string pig = star.effectiveTemp > 6500f ? "blue-tinged green"
                           : star.effectiveTemp > 5300f ? "chlorophyll green"
                           : star.effectiveTemp > 3900f ? "olive, ochre and orange"
                           : "deep red, violet and near-black";
                sb.Append($"Its photosynthetic life is {pig}, evolved to harvest the {cls} star's light. ");
            }

            // ── Habitability verdict ──────────────────────────────────────────────────────
            sb.Append(p.habClass switch
            {
                HabClass.Habitable => "Remarkably, conditions here sit within the range life could take hold.",
                HabClass.Marginal => "It sits at the ragged edge of habitability — harsh, but not wholly hostile.",
                HabClass.Hostile => "It is a hostile world, lethal to life as we know it.",
                _ => "No known biology could survive here.",
            });

            return sb.ToString();
        }

        static string AU(float au) => au < 0.1f ? $"{au:0.00} AU" : $"{au:0.0} AU";
        static string Pick(DetRng rng, params string[] opts) => opts[Mathf.Min(opts.Length - 1, (int)(rng.Value * opts.Length))];
    }
}
