using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.Flora
{
    // Growth habit (Raunkiær-ish life-forms), not "tree vs bush". Each drives a distinct geometry generator.
    public enum PlantArchetype
    {
        Tree,            // single bole, branching canopy (a bare trunk + frond crown = "palm", from params alone)
        Shrub,           // multi-stem bushy
        Fern,            // ground rosette of arching compound fronds
        GrassClump,      // tuft of arching blades
        Vine,            // long winding/sprawling stem with leaves
        FloweringHerb,   // slender stems topped with flowers
        Cactus,          // ribbed succulent column(s) + spines, few leaves
        SucculentRosette,// rosette of thick pointed pads (agave/aloe)
        Globe,           // spherical body (barrel cactus / puffball / alien orb) on the ground
        MatAlgae,        // low spreading mat of small fronds/blobs
        Mushroom,        // stalk + cap (fungal / lichen analogue)
        Tendril,         // bulb head trailing many dangling tentacle-fronds (medusa)
        OrbCluster,      // stalk hung with clusters of gas-bladder spheres (grapes)
        Tube,            // clusters of open funnels / pitchers that catch light
        Ribbon,          // tall wide waving ribbons / kelp-blades
        Umbrella,        // single tall stalk under a broad radial parasol canopy
        Crystal,         // radiating clusters of angular crystalline photosynthetic spikes
        Spire,           // spiky ground rosette + a towering floret-studded column (Puya / silversword / giant lobelia)
        Segmented,       // jointed hollow stalks with sheathed nodes, whorls of needles & a spore cone (horsetail)
        LivingStone      // clustered pairs of fleshy, fissured half-domes flush with the ground (lithops)
    }

    // Foliage morphology — deliberately NOT limited to Earth leaves. Flat blades, funnels, bladder-orbs,
    // dangling filaments, and algal sheaths that coat the stem are all "leaves" (photosynthetic organs) here.
    public enum LeafShape
    {
        Ovate, Lanceolate, Palmate, Cordate, Needle, Blade, Pad, Scale, Frond,   // familiar
        Strap,      // long ribbon-strap (seagrass / hart's-tongue)
        Disc,       // flat round pad on a short petiole (lily-pad / pennywort)
        Fan,        // ginkgo / kelp fan wedge with a ruffled far edge
        Orb,        // clusters of small photosynthetic bladder-spheres
        Cup,        // upward funnel that catches light (bromeliad / pitcher-ish)
        Trumpet,    // flared outward funnel
        Filament,   // thin dangling strings (air-plant / string-of-hearts)
        Bilobe,     // notched two-lobed blade
        Reniform,   // kidney / rounded broad leaf
        Sheath,     // algal coat of tiny overlapping scales hugging the stem
        Kelp,       // long wavy broad blade (kelp / seaweed)
        Tongue,     // broad blunt recurved strap (aloe / bromeliad tongue)
        Spatulate,  // paddle: narrow base widening to a rounded tip
        Antler,     // flat forked staghorn blade
        Membrane,   // big volumetric photosynthetic sail/membrane — forces few branches (its own morphologies)
        None
    }

    // A single BIG organ carried at the branch tips, independent of leaf shape — the payload that makes a plant read
    // as a giant flower, a bladder-orb bush, a pitcher colony, a berry spike, etc.
    public enum TerminalOrgan { None, Bloom, Orb, Pitcher, Membrane, Berry, Bulb, Plume, Cone, Anemone }
    // Cone: an ovoid shingled with spiralling scales (pinecone / cycad cone). Anemone: an organ head ringed by
    // curling glandular tentacles with bead tips (sundew / sea-anemone).

    // Surface RELIEF of fleshy organs (orbs/berries/bulbs) — from a smooth ellipsoid to convoluted brain-like folds.
    public enum OrganShape { Smooth, Brain, Lobed, Warty, Ridged, Veined }

    // Two-tone coloration PATTERN on organs (blends the organ's base ↔ accent colour across the surface).
    public enum OrganColor { Solid, Varied, Banded, Blotched, Marbled, Speckled }

    // Leaf surface finish (albedo/normal/roughness look), independent of leaf SHAPE.
    // 0 matte, 1 glossy/waxy-smooth, 2 veined (midrib + laterals), 3 mottled (blotchy), 4 waxy (high spec + veins).
    // 5 cellular (Voronoi cells, sunken walls), 6 nebulous (warped swirls + wisps + third tone), 7 ocellate (eye-spots),
    // 8 striate (warped pinstripes), 9 iridescent (angle-dependent thin-film sheen). Indices must match the shader.
    public enum LeafStyle { Matte, Glossy, Veined, Mottled, Waxy, Cellular, Nebulous, Ocellate, Striate, Iridescent }

    // How the woody scaffold is arranged — the silhouette of the tree, independent of leaf shape.
    public enum BranchStyle
    {
        Spreading,   // decurrent oak: wide angles, no dominant leader, rounded crown
        Excurrent,   // conifer/pine: strong central leader, near-horizontal drooping whorls, conical
        Columnar,    // fastigiate poplar/cypress: steep upswept branches, narrow
        Weeping,     // willow: branches arch then hang straight down
        Whorled,     // araucaria/spruce: branches in distinct rings at nodes
        Sparse       // open, few long limbs (savanna / alien)
    }

    // Discrete bark archetypes (indices must match CLAY/Flora StemSurface):
    // 0 furrowed, 1 plated, 2 papery, 3 stringy, 4 smooth, 5 scaly, 6 ropey.
    public enum BarkType { Furrowed, Plated, Papery, Stringy, Smooth, Scaly, Ropey }

    // The "paint job": how the two pigments are distributed over the surface.
    public enum PaintJob { Solid, TipTinted, Veined, Margin, Variegated, Spotted, GradientVertical }

    [System.Serializable]
    public class PlantGenome
    {
        public PlantArchetype archetype = PlantArchetype.Tree;
        public LeafShape leaf = LeafShape.Ovate;
        public BranchStyle branchStyle = BranchStyle.Spreading;
        public PaintJob paint = PaintJob.Solid;

        public float heightM = 6f;
        public float trunkTaper = 0.72f;
        public int   branchDepth = 4;
        public float branchAngle = 38f;
        public float branchDensity = 0.6f;
        public int   stemCount = 1;
        public bool  trunkSplits = false;  // dichotomous: trunk forks into equal limbs instead of a tapering leader
        public float trunkBulge = 0f;      // 0 straight taper … 1 bulbous / pot-bellied (baobab / bottle tree)
        public float stemMaterial = 1f;    // 0 spongy · 0.33 herbaceous · 0.66 fibrous · 1 woody
        public BarkType barkType = BarkType.Furrowed;   // discrete bark archetype (used when woody)
        public float barkScale = 1f;       // grain size of the bark/fiber texture
        public float barkRelief = 1f;      // 0 flat … 1 default … up to deep bump relief
        public float barkWarp = 0.3f;      // 0 regular pattern … 1 heavily domain-warped (organic irregularity)
        public float barkNoise = 0.15f;    // 0 clean … 1 gritty high-frequency roughness on the ridges
        public float lean = 0f;            // 0 upright … 1 sprawling/creeping
        public float bareTrunkFrac = 0f;   // 0 branches all the way down … 1 clear bole, crown only (palm / umbrella)
        public float stiltRoots = 0f;      // 0 trunk meets the ground … 1 trunk raised on arching prop roots (mangrove / walking tree)

        public float leafSize = 1f;
        public float leafDensity = 1f;
        public float leafTranslucency = 0.4f;   // 0 opaque · 1 backlit-glowing thin membrane (light passes through)
        public float leafRuffle = 0f;           // 0 flat edge · 1 wavy/frilled margin
        public LeafStyle leafStyle = LeafStyle.Matte;   // surface finish / texture of the leaf
        public float leafColorVar = 0.35f;      // 0 uniform · 1 every leaf a different tint (variegation/autumn flecks)
        public float leafJitter = 0.35f;        // 0 machined-perfect · 1 heavily distorted mesh (organic imperfection)
        // Frond (fern) anatomy
        public float frondPinnae = 1f;          // number of leaflets along the rachis (0.4 sparse … 2 dense)
        public float frondWidth = 1f;           // leaflet breadth (0.3 needle-thin … 2 broad)
        public float frondDivision = 0.3f;      // 0 once-divided (pinnate) … 1 twice-divided (bipinnate, lacy)
        public float frondCurl = 0.2f;          // 0 straight tip … 1 tightly coiled fiddlehead
        public float ribbing = 0f;         // 0 smooth … 1 deeply ribbed (cacti)
        public float spininess = 0f;       // spines/hairs
        public float bodyGirth = 1f;       // fatness of succulent/globe bodies

        public TerminalOrgan terminalOrgan = TerminalOrgan.None;   // big organ at branch tips (giant flower/orb/pitcher/…)
        public float terminalSize = 1f;
        public OrganShape organShape = OrganShape.Smooth;          // relief of fleshy organs (orbs/berries/bulbs)
        public OrganColor organColor = OrganColor.Solid;           // two-tone pattern across the organ surface
        public float organRelief = 0.6f;                           // 0 smooth … 1 deeply convoluted
        public LeafStyle organTexture = LeafStyle.Nebulous;        // surface texture of flowers & flower-mesh organs
        public float pendant = 0f;                                 // 0 upright … 1 limbs & organs hang / dangle

        public bool  hasFlowers = false;
        public float flowerAmount = 0.4f;
        public int   petalCount = 5;
        public float flowerSize = 1f;
        public float glow = 0f;            // bioluminescence (self-illumination), common on dim worlds

        public Color pigment = new Color(0.24f, 0.46f, 0.24f);   // primary foliage
        public Color accent = new Color(0.5f, 0.6f, 0.25f);      // secondary (pattern)
        public Color underside = new Color(0.2f, 0.34f, 0.22f);  // leaf back
        public Color woodColor = new Color(0.28f, 0.20f, 0.13f);
        public Color flowerColor = new Color(0.9f, 0.5f, 0.6f);

        public int seed = 12345;

        public static PlantGenome Random(PlanetData p, int seed) => Random(p, seed, null);

        /// Random genome; `force` pins the body plan (used by the surface ecology to fill a specific role).
        public static PlantGenome Random(PlanetData p, int seed, PlantArchetype? force)
        {
            var r = new DetRng((ulong)(uint)seed ^ 0xF10A5EEDUL);
            var g = new PlantGenome { seed = seed };

            float grav = Gravity(p);
            float wet = p != null ? Mathf.Clamp01(p.waterCoverage) : 0.5f;
            float warmth = p != null ? Mathf.Clamp01((p.meanTempC + 10f) / 45f) : 0.5f;
            bool arid = wet < 0.3f;
            bool hot = warmth > 0.7f;
            bool cold = warmth < 0.3f;

            // Colour: star-tuned primary, a related accent (hue-shifted), a darker underside, and a contrasting flower.
            g.pigment = PlantColor.FromStar(p != null ? p.hostStarTempK : 5800f, r.Value);
            g.accent = PlantColor.Vary(g.pigment, r.Range(-0.08f, 0.10f), r.Range(0.7f, 1.2f), r.Range(0.9f, 1.4f));
            g.underside = PlantColor.Vary(g.pigment, r.Range(-0.03f, 0.03f), r.Range(0.6f, 0.9f), r.Range(0.6f, 0.85f));
            // Trunk colour: mostly brown wood, but a good fraction are photosynthetic (green/red bark), some grey/pale.
            float wroll = r.Value;
            if (wroll < 0.34f)        // photosynthetic bark — follows the star like the foliage does (green ↔ red)
                g.woodColor = PlantColor.Vary(g.pigment, r.Range(-0.05f, 0.05f), r.Range(0.65f, 1.05f), r.Range(0.45f, 0.8f));
            else if (wroll < 0.5f)    // pale / grey / birch-like
                g.woodColor = new Color(r.Range(0.45f, 0.72f), r.Range(0.42f, 0.68f), r.Range(0.38f, 0.6f));
            else                       // ordinary brown wood
                g.woodColor = new Color(r.Range(0.18f, 0.36f), r.Range(0.12f, 0.22f), r.Range(0.08f, 0.16f));
            g.flowerColor = PlantColor.Complement(g.pigment, ref r);

            // Pick a habit weighted by the environment.
            g.archetype = PickArchetype(ref r, grav, wet, warmth);   // always drawn, so RNG sequence is stable
            if (force.HasValue) g.archetype = force.Value;

            g.heightM = Mathf.Clamp(r.Range(1.5f, 10f) * (1f + (1f - grav) * 1.4f) * (0.6f + 0.8f * wet * warmth), 0.1f, 90f);
            g.trunkTaper = Mathf.Clamp01(r.Range(0.6f, 0.85f) - (1f - grav) * 0.1f);
            g.branchDepth = Mathf.Clamp(Mathf.RoundToInt(r.Range(2f, 5f) + wet), 1, 6);
            g.branchAngle = r.Range(22f, 55f);
            g.branchDensity = Mathf.Clamp01(r.Range(0.25f, 0.9f) * (0.6f + 0.8f * wet));
            g.stemCount = r.Value < 0.35f ? r.RangeInt(2, 6) : 1;
            g.trunkSplits = r.Value < 0.4f;   // ~40% fork dichotomously; the rest keep a tapering leader
            g.trunkBulge = r.Value < 0.3f ? r.Range(0.3f, 1f) : 0f;   // occasional bulbous trunk
            // Stem tissue keyed to the growth form: succulents spongy, trees woody, herbs plant-like.
            bool woody = g.archetype == PlantArchetype.Tree || g.archetype == PlantArchetype.Shrub;
            bool fleshy = g.archetype == PlantArchetype.Cactus || g.archetype == PlantArchetype.SucculentRosette || g.archetype == PlantArchetype.Globe || g.archetype == PlantArchetype.MatAlgae || g.archetype == PlantArchetype.Mushroom;
            g.stemMaterial = woody ? r.Range(0.6f, 1f) : fleshy ? r.Range(0f, 0.35f) : r.Range(0.2f, 0.75f);
            g.barkType = (BarkType)r.RangeInt(0, 7);
            g.barkScale = r.Range(0.6f, 1.8f);
            g.barkRelief = r.Range(0.6f, 1.4f);
            g.barkWarp = r.Range(0.15f, 0.7f);
            g.barkNoise = r.Range(0.05f, 0.4f);
            g.lean = g.archetype == PlantArchetype.Vine || g.archetype == PlantArchetype.MatAlgae ? r.Range(0.6f, 1f) : r.Range(0f, 0.25f);
            // Bare bole (palm/umbrella): sometimes clear the lower trunk so the crown sits on a naked stem.
            g.bareTrunkFrac = (g.archetype == PlantArchetype.Tree && r.Value < 0.3f) ? r.Range(0.5f, 0.85f) : 0f;

            g.leaf = PickLeaf(ref r, g.archetype, wet, cold);
            g.leafSize = Mathf.Clamp(r.Range(0.7f, 1.8f) * (0.6f + wet), 0.2f, 2.8f);
            g.leafDensity = r.Range(0.6f, 1.5f) * (0.6f + 0.9f * wet);
            g.leafTranslucency = Mathf.Clamp01(r.Range(0.1f, 0.85f) + (cold ? -0.15f : 0.1f));   // thin membranes glow when backlit
            g.leafRuffle = r.Value < 0.4f ? r.Range(0.2f, 1f) : 0f;
            g.leafStyle = (LeafStyle)r.RangeInt(0, 10);
            g.organTexture = (LeafStyle)r.RangeInt(0, 10);
            g.leafColorVar = r.Range(0.1f, 0.8f);
            g.leafJitter = r.Range(0.2f, 0.6f);
            g.frondPinnae = r.Range(0.6f, 1.8f);
            g.frondWidth = r.Range(0.5f, 1.6f);
            g.frondDivision = r.Value < 0.45f ? r.Range(0.55f, 1f) : r.Range(0f, 0.4f);
            g.frondCurl = r.Value < 0.3f ? r.Range(0.4f, 1f) : r.Range(0f, 0.25f);
            // Crown silhouette: conifers favour cold worlds, weeping favours wet, columnar anywhere.
            g.branchStyle = PickBranchStyle(ref r, cold, wet);
            g.ribbing = g.archetype == PlantArchetype.Cactus || g.archetype == PlantArchetype.Globe ? r.Range(0.4f, 1f) : 0f;
            bool spiny = g.archetype == PlantArchetype.Cactus || g.archetype == PlantArchetype.Globe || g.archetype == PlantArchetype.SucculentRosette;
            g.spininess = (arid ? r.Range(0.3f, 1f) : r.Range(0f, 0.3f)) * (spiny ? 1f : 0.12f);   // trees rarely spiny
            g.bodyGirth = r.Range(0.7f, 1.6f);

            // Terminal organ: a big tip payload on ~35% of plants. Big blooms/orbs pair well with a bare stalk & few branches.
            g.terminalOrgan = r.Value < 0.35f ? (TerminalOrgan)r.RangeInt(1, 10) : TerminalOrgan.None;
            bool treeLike = g.archetype == PlantArchetype.Tree || g.archetype == PlantArchetype.Shrub;
            g.stiltRoots = treeLike && r.Value < (0.12f + wet * 0.2f) ? r.Range(0.35f, 1f) : 0f;   // swamps favour stilts
            g.terminalSize = r.Range(0.7f, 1.8f);
            g.organShape = r.Value < 0.15f ? OrganShape.Smooth : (OrganShape)r.RangeInt(1, 6);   // mostly textured, rarely smooth
            g.organColor = r.Value < 0.2f ? OrganColor.Solid : (OrganColor)r.RangeInt(1, 6);     // mostly patterned
            g.organRelief = r.Range(0.45f, 1f);
            g.pendant = r.Value < 0.35f ? r.Range(0.3f, 1f) : 0f;
            if (g.terminalOrgan == TerminalOrgan.Bloom || g.terminalOrgan == TerminalOrgan.Membrane)
            {
                g.branchDensity *= 0.5f;                                   // fewer, so the big organ dominates
                if (g.archetype == PlantArchetype.Tree && g.bareTrunkFrac < 0.3f) g.bareTrunkFrac = r.Range(0.4f, 0.7f);
            }

            g.hasFlowers = g.archetype == PlantArchetype.FloweringHerb || r.Value < (warmth * 0.5f);
            g.flowerAmount = r.Range(0.2f, 0.9f);
            g.petalCount = r.RangeInt(3, 9);
            g.flowerSize = r.Range(0.6f, 1.6f);

            g.paint = (PaintJob)r.RangeInt(0, 7);
            // Bioluminescence: likelier under dim red-dwarf light or on dark plants.
            bool dim = (p != null && p.hostStarTempK < 3900f);
            g.glow = r.Value < (dim ? 0.4f : 0.15f) ? r.Range(0.4f, 1.5f) : 0f;
            return g;
        }

        static PlantArchetype PickArchetype(ref DetRng r, float grav, float wet, float warmth)
        {
            (PlantArchetype a, float w)[] opts =
            {
                (PlantArchetype.Tree,           grav < 1.6f ? 1.3f * wet + 0.3f : 0.1f),
                (PlantArchetype.Shrub,          1f),
                (PlantArchetype.Fern,           wet * (1f - Mathf.Abs(warmth - 0.6f))),
                (PlantArchetype.GrassClump,     0.8f),
                (PlantArchetype.Vine,           wet * 0.8f),
                (PlantArchetype.FloweringHerb,  0.7f + warmth * 0.5f),
                (PlantArchetype.Cactus,         (1f - wet) * 1.4f + (warmth > 0.6f ? 0.4f : 0f)),
                (PlantArchetype.SucculentRosette,(1f - wet) * 1.0f),
                (PlantArchetype.Globe,          (1f - wet) * 0.6f + 0.2f),
                (PlantArchetype.MatAlgae,       wet * 0.7f + (grav > 2f ? 0.5f : 0f)),
                (PlantArchetype.Mushroom,       wet * 0.5f + (warmth < 0.4f ? 0.3f : 0f)),
                // wilder / more alien forms — always a fair chance so worlds feel exotic
                (PlantArchetype.Tendril,        0.5f + wet * 0.4f),
                (PlantArchetype.OrbCluster,     0.5f),
                (PlantArchetype.Tube,           0.4f + wet * 0.3f),
                (PlantArchetype.Ribbon,         0.5f + wet * 0.5f),
                (PlantArchetype.Umbrella,       0.5f + warmth * 0.3f),
                (PlantArchetype.Crystal,        0.4f + (1f - wet) * 0.4f),
                (PlantArchetype.Spire,          0.4f + (1f - warmth) * 0.4f),     // alpine giants
                (PlantArchetype.Segmented,      0.3f + wet * 0.5f),               // wetland horsetails
                (PlantArchetype.LivingStone,    (1f - wet) * 0.9f),               // desert stone-mimics
            };
            float tot = 0f; foreach (var o in opts) tot += Mathf.Max(0f, o.w);
            float x = r.Value * tot;
            foreach (var o in opts) { x -= Mathf.Max(0f, o.w); if (x <= 0f) return o.a; }
            return PlantArchetype.Shrub;
        }

        static LeafShape PickLeaf(ref DetRng r, PlantArchetype a, float wet, bool cold)
        {
            switch (a)
            {
                case PlantArchetype.Fern: return LeafShape.Frond;
                case PlantArchetype.GrassClump: return LeafShape.Blade;
                case PlantArchetype.Cactus:
                case PlantArchetype.Globe: return r.Value < 0.7f ? LeafShape.None : LeafShape.Scale;
                case PlantArchetype.SucculentRosette: return LeafShape.Pad;
                case PlantArchetype.MatAlgae: return r.Value < 0.5f ? LeafShape.Blade : LeafShape.None;
                case PlantArchetype.Ribbon: return LeafShape.Blade;
                case PlantArchetype.Mushroom:
                case PlantArchetype.Tendril:
                case PlantArchetype.OrbCluster:
                case PlantArchetype.Tube:
                case PlantArchetype.Umbrella:
                case PlantArchetype.Crystal:
                case PlantArchetype.Spire:
                case PlantArchetype.Segmented:
                case PlantArchetype.LivingStone: return LeafShape.None;   // these build their own organs
                default:
                    if (cold) return r.Value < 0.5f ? LeafShape.Needle : (r.Value < 0.5f ? LeafShape.Scale : LeafShape.Sheath);
                    // A wide, deliberately alien palette — familiar broad leaves plus discs, fans, funnels, orbs, strings.
                    LeafShape[] wetPalette =
                    {
                        LeafShape.Ovate, LeafShape.Lanceolate, LeafShape.Palmate, LeafShape.Cordate, LeafShape.Reniform,
                        LeafShape.Bilobe, LeafShape.Disc, LeafShape.Fan, LeafShape.Strap, LeafShape.Cup, LeafShape.Trumpet,
                        LeafShape.Orb, LeafShape.Filament, LeafShape.Sheath, LeafShape.Membrane,
                        LeafShape.Kelp, LeafShape.Tongue, LeafShape.Spatulate, LeafShape.Antler,
                    };
                    LeafShape[] dryPalette =
                    {
                        LeafShape.Needle, LeafShape.Lanceolate, LeafShape.Scale, LeafShape.Strap, LeafShape.Orb,
                        LeafShape.Filament, LeafShape.Bilobe, LeafShape.Fan, LeafShape.Tongue, LeafShape.Spatulate, LeafShape.Antler,
                    };
                    var pal = wet > 0.4f ? wetPalette : dryPalette;
                    return pal[r.RangeInt(0, pal.Length)];
            }
        }

        static BranchStyle PickBranchStyle(ref DetRng r, bool cold, float wet)
        {
            (BranchStyle s, float w)[] opts =
            {
                (BranchStyle.Spreading, 1.2f),
                (BranchStyle.Excurrent, cold ? 1.4f : 0.6f),   // conifers on cold worlds
                (BranchStyle.Columnar,  0.7f),
                (BranchStyle.Weeping,   0.5f + wet * 0.6f),
                (BranchStyle.Whorled,   0.6f),
                (BranchStyle.Sparse,    0.6f),
            };
            float tot = 0f; foreach (var o in opts) tot += o.w;
            float x = r.Value * tot;
            foreach (var o in opts) { x -= o.w; if (x <= 0f) return o.s; }
            return BranchStyle.Spreading;
        }

        public static float Gravity(PlanetData p)
            => p == null ? 1f : Mathf.Clamp(p.mass / Mathf.Max(p.radiusEarth * p.radiusEarth, 1e-3f), 0.02f, 12f);
    }

    public static class PlantColor
    {
        public static Color FromStar(float starTempK, float roll)
        {
            if (starTempK <= 0f) starTempK = 5800f;
            Color green = new Color(0.24f, 0.46f, 0.24f), teal = new Color(0.12f, 0.42f, 0.40f), olive = new Color(0.30f, 0.42f, 0.14f),
                  yellow = new Color(0.60f, 0.55f, 0.16f), orange = new Color(0.60f, 0.38f, 0.14f), red = new Color(0.52f, 0.17f, 0.15f),
                  darkred = new Color(0.32f, 0.10f, 0.10f), purple = new Color(0.34f, 0.16f, 0.42f), black = new Color(0.11f, 0.12f, 0.15f);
            if (starTempK > 6500f) return Pick(roll, green, teal, olive);
            if (starTempK > 5300f) return Pick(roll, green, green, olive, teal);
            if (starTempK > 3900f) return Pick(roll, olive, yellow, orange, red);
            return Pick(roll, red, darkred, purple, black);
        }
        public static Color Vary(Color c, float dH, float sMul, float vMul)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            var o = Color.HSVToRGB(Mathf.Repeat(h + dH, 1f), Mathf.Clamp01(s * sMul), Mathf.Clamp01(v * vMul));
            o.a = 1f; return o;
        }
        public static Color Complement(Color c, ref DetRng r)
        {
            Color.RGBToHSV(c, out float h, out float _, out float _);
            var o = Color.HSVToRGB(Mathf.Repeat(h + r.Range(0.35f, 0.6f), 1f), r.Range(0.6f, 0.95f), r.Range(0.7f, 1f));
            o.a = 1f; return o;
        }
        static Color Pick(float roll, params Color[] c) => c[Mathf.Clamp(Mathf.FloorToInt(roll * c.Length), 0, c.Length - 1)];
    }
}
