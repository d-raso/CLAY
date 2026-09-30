# CLAY — Procedural Flora & Explorable Surfaces

Design for generating **plant life from the planet it grows on**, and for the surface-exploration layer that will
place that flora on the ground. Like the planet generator, flora is driven from **causes, not fixed species**: roll
the environment → a *plant genome* of parameters → the morphology and colour *emerge*. Two same-star garden worlds
should grow visibly different forests; one plant should look different at the equator vs the pole vs a valley.

This bridges the orbital view (already built) to a walkable/flyable surface. It assumes the existing planet model:
`PlanetData` (mass, radiusEarth, meanTempC, waterCoverage, tidallyLocked, axialTiltDeg, rotationHours, volcanism,
habClass, hostStarTempK, bombardment, tidalHeat), the surface bake (`PlanetTexture`), and the biosphere pigment work
(`VegPigment`, star-tuned). **[NEW]** marks things not in the sim yet.

---

## 1. CAUSAL INPUTS → PLANT TRAITS

Every plant trait is a function of an environment vector sampled **at the plant's location** on the surface, plus a
per-plant genome seed for individual variety. The environment vector is:

| Input | Source | Primarily drives |
|---|---|---|
| **Star spectrum** (`hostStarTempK`, luminosity) | star | pigment / foliage colour, canopy strategy |
| **Surface gravity** `g` | `mass / radiusEarth²` (Earth units) | max height, trunk taper, branching |
| **Atmospheric pressure / density** | mass + temp (see `HasAtmosphere`, `AtmosphereDensity`) | height support, leaf size, wind resistance |
| **Local insolation** | latitude, tidal-lock angle, cloud cover, axial tilt | height (light competition), leaf area |
| **Precipitation / humidity** | `waterCoverage`, temp, distance-to-water, orographic | leaf form (broad↔needle↔succulent), density |
| **Local temperature** | mean temp × latitude / substellar angle / elevation | hardiness, deciduousness, size |
| **Soil chemistry / redox** | `PlanetTexture` theme, oxidation, volcanism | accent pigments, tolerance, exotic biochemistries |
| **UV / flare dosage** | star flare activity, atmosphere thickness | protective pigments (dark/red/waxy), low habit |
| **Wind** | `rotationHours` (fast rotator), pressure | streamlined/low forms, flagging |
| **Seasonality** | `axialTiltDeg`, orbital eccentricity | deciduous vs evergreen, annual vs perennial |
| **Terrane / biome province** | `OldTerrane`, elevation, slope (from the bake) | which community grows where |

The surface is partitioned into **biome cells** (see §5); each plant reads the environment of its cell + a fine
local jitter, so a single world hosts many communities.

---

## 2. FOLIAGE COLOUR FROM THE STAR

Photosynthesis reflects the wavelengths the star does **not** supply strongly; the leftover light is the colour we
see. This already exists for the orbital view in `VegPigment(hostStarTempK)` and **the surface flora must reuse the
exact same function** so a world's forests match its rendered vegetation from space.

- **Hot A/F/G stars (blue-rich, >5300 K):** abundant high-energy light → efficient **green / blue-green** canopy;
  plants can afford to reflect green.
- **K dwarfs (3900–5300 K):** redder light → broaden absorption → **olive, ochre, orange, occasional red**.
- **M dwarfs (<3900 K, the majority of stars):** dim red/IR → life harvests everything it can → **deep red, retinal
  purple, near-black**. Under a red dwarf, expect dark forests that look almost black in visible light.

Layered refinements **[NEW]**:
- **Understorey vs canopy:** understorey plants live in filtered light → shift *darker / more retinal* than the
  canopy above them (secondary pigment adaptation).
- **Seasonal/stress colour:** cold, drought, or high UV pushes anthocyanin-like **reds/purples** into otherwise-green
  flora (autumn colour, sunburn reds at altitude/UV).
- **Non-green biochemistries** on exotic worlds: sulfur-metabolisers → yellow; iron/anaerobic → rust; halophiles in
  brine flats → pink/orange mats. Gate these on the planet's `PlanetTexture` theme so surface and space agree.
- **Flower/repro accents:** a small chance of a contrasting accent hue (complementary to the canopy) for visual pop.

Colour is authored in **HSV** (reuse `ShiftHSV`): pick a base pigment from the star, then per-plant jitter hue a
little, and modulate saturation/value by health (water, light).

---

## 3. HEIGHT & FORM FROM GRAVITY, AIR, AND LIGHT

**Surface gravity** `g = mass / radiusEarth²` (Earth = 1) is the dominant control on size and silhouette:

- **Low g (<0.5):** tall, spindly, thin-trunked; plants can grow enormously tall cheaply. Long drooping fronds,
  minimal taper. Think 100 m needles on a 0.3 g world.
- **Earth-ish g (~1):** familiar proportions.
- **High g (>2):** short, squat, thick-trunked, low domes and mats; structural cost of height is punishing. Broad
  bracing bases, little vertical growth.

`maxHeight ≈ baseHeight × (lightBudget) × pressureSupport / gᵏ` where:
- **lightBudget** rises with insolation and with **competition** (dense, well-lit, wet stands grow tall to shade
  rivals; sparse or dim stands stay low).
- **pressureSupport**: thin air (low pressure) → shorter, tougher, smaller-leaved (desiccation + weak CO₂/turgor);
  thick air → larger leaves, softer tissue, taller possible.
- **k ≈ 0.5–1.0** — tune so the g effect reads clearly without being absurd.

Derived form parameters (the "genome"):
- **Trunk taper & count** (single bole vs multi-stem), **branching angle & recursion depth**, **branch density**.
- **Leaf archetype:** broadleaf / needle / frond / pad/succulent / bladed-grass / mat/cushion / non-vascular crust.
- **Habit:** tree / shrub / groundcover / vine / floating / cushion.
- **Canopy shape:** columnar, spreading, weeping, umbrella, spherical, prostrate.

Wind (fast rotators, high pressure) biases toward **streamlined, low, flagged** forms; still thick air toward broad
sails.

---

## 4. TIDALLY-LOCKED WORLDS — LIGHT BY LONGITUDE, NOT LATITUDE

A tidally-locked (eyeball) world has **no day/night cycle**; climate is set by the **angle from the substellar
point** (the "meridian" the user means — the sub-solar point at +Z in the bake). Flora must vary along that gradient:

- **Substellar zone (0–30° from sub-solar):** intense fixed overhead light + heat. Either lush (if not too hot) or
  heat-stressed (small, waxy, reflective, ground-hugging to avoid the glare). Plants **do not track the sun** (it
  never moves) — instead they present a fixed optimal angle: canopies flattened toward the star, thick sun-side
  cuticles, shaded undersides.
- **Temperate ring / terminator (60–90°):** the "twilight band" — perpetual low-angle reddened light (like an eternal
  sunset). Expect the **richest, tallest forests**, all **leaning/oriented toward the star** (permanent phototropism →
  asymmetric, one-sided canopies, "solar sails"). This band is the world's habitable belt.
- **Antistellar / night side (>110°):** no light → no photosynthesis. Bare, frozen, or at most chemotrophic mats near
  geothermal vents. Any "plants" here are non-photosynthetic (fungal/detritivore analogues).

Implementation: the placement/genome samples `substellarAngle = acos(dir·toStar)`; it maps to light, temperature, and a
**fixed lean vector** (toward the star) that orients every plant in the terminator band. This reuses the eyeball
thermal logic already in `Colorize` (`heat = dir.z`).

On **fast rotators / normal worlds**, light is by latitude and plants track the sun (heliotropism) or present
horizontal canopies; caps of vegetation thin toward the poles.

---

## 5. PRECIPITATION, HUMIDITY, AND BIOME ZONING

Leaf strategy is mostly a **water** story:

- **Wet / humid (high precip):** large **broadleaves**, dense multi-layer canopy, epiphytes, vines — rainforest.
- **Seasonal / moderate:** deciduous broadleaf or mixed, grassland with scattered trees (savanna) under strong dry
  seasons (obliquity/eccentricity).
- **Dry:** **needles, waxy scales, succulents/pads, thick cuticles**, sparse spacing, deep-root low shrubs, desert
  mats. Small leaf area to limit water loss.
- **Cold-dry (tundra):** cushion plants, low mats, dark pigments for warmth.
- **Coastal / wetland:** reeds, mangrove-analogues, floating mats on shallow seas (ties to `waterCoverage`, distance
  to coastline from the height bake).

**Biome zoning [NEW]:** derive a per-location climate from
`(insolation/substellar-angle, temperature, precipitation, elevation, slope, terrane)` and classify into a biome
(rainforest, forest, savanna, steppe, desert, tundra, wetland, alpine, barren). The biome sets the **community**
(which archetypes, at what density and height), and each individual is a jittered draw from that community's genome
distribution. Precipitation itself should come from a simple model: humidity high near seas / windward slopes /
temperate bands, low in rain shadows, interiors, and hot zones — this also feeds the surface **precipitation/erosion**
feature planned for the terrain pass, so vegetation and terrain weathering agree.

---

## 6. PROCEDURAL GENERATION APPROACH

Two tiers, chosen by view distance:

1. **Parametric archetype meshes (near view):** a small set of GPU-friendly generators (trunk-and-branch L-system or
   recursive parametric skeleton + procedurally placed leaf cards) parameterised by the §3 genome. One generator with
   ~20 parameters can span tree/shrub/frond/succulent by parameter alone. Leaves are instanced cards or simple meshes
   coloured by the §2 pigment. Deterministic from `(planetSeed, biomeSeed, instanceSeed)`.
2. **Impostor billboards / scatter (far view & density):** LOD to cross-fade cards, then to a coloured ground-cover
   shader / density texture at the biome scale, so a whole continent can be "green→red" from altitude without meshing
   billions of plants. The orbital-view vegetation colour is the limit of this LOD chain — it must match.

**The plant genome** (serializable, deterministic):
```
struct PlantGenome {
  archetype (tree/shrub/frond/succulent/grass/mat/vine/floating)
  height, trunkTaper, stemCount
  branchAngle, branchDepth, branchDensity, canopyShape
  leafType, leafSize, leafDensity
  pigmentBaseHSV, accentHSV, health   // from star + water + light
  leanVector, phototropism            // sun-facing (tidal lock / low-angle light)
  seasonalState                       // evergreen/deciduous phase
}
```
`GenerateGenome(env, rng)` maps the §1 environment vector to these; `BuildPlantMesh(genome, lod)` realises it. Keep it
data-only/thread-safe like `PlanetTexture.ComputeSurface` so it can bake off the main thread.

---

## 7. SURFACE EXPLORATION — HOW FLORA GETS PLACED

The surface layer (the next milestone) needs, per planet, a way to stand on the ground and see it populated:

1. **Local terrain patch:** reuse `PlanetTexture.Height` / the same noise fields to build a high-detail terrain tile
   around the viewer (chunked, LOD, floating-origin like the orbital view). The tile inherits the world's provinces
   (maria, highlands, basins), sea level, and biome from the exact same functions, so the ground matches the globe.
2. **Biome sample:** at the tile, evaluate §5 zoning → biome + climate.
3. **Scatter:** Poisson-disk / density-map placement of plants over the tile, density and community from the biome,
   each instance drawing a jittered genome; slope and water mask exclude bare rock, cliffs, and open water.
4. **Render:** archetype meshes near → billboards → ground-cover shader far, all pigment-consistent with orbit.
5. **Only on habitable/living worlds** (`habClass == Habitable`, `ChemTheme.Biosphere`) do photosynthetic plants
   appear; marginal worlds get sparse extremophile mats; hostile/airless worlds get none (bare terrain, or exotic
   non-bio features like lava, dunes, ice).

---

## 8. WORKED EXAMPLES (sanity checks)

- **Earth-analog, G star, g≈1, wet, tilted:** green broadleaf forests, deciduous in temperate zones, grasslands in
  savanna belts, needles in cold-dry, deserts sparse — familiar.
- **Red-dwarf terminator world (tidally locked):** near-black/deep-red forests in the twilight ring, every plant
  leaning toward the fixed star as one-sided "solar sails"; scorched sparse succulents at the substellar point; dead
  night side.
- **Low-gravity ocean-margin world (g≈0.35), F star:** absurdly tall, spindly blue-green fronds and floating mats;
  towering thin canopies.
- **High-gravity super-Earth (g≈2.5), K star:** squat olive/ochre cushions and thick-boled low domes hugging the
  ground; nothing tall.
- **High-UV thin-air world:** low, dark-red waxy mats and crusts; protective pigmentation, minimal leaf area.

---

## 9. PHASING

1. **Genome + one archetype generator** (parametric tree/shrub) driven by star pigment, gravity, water — validate the
   causal chain on a flat test tile.
2. **Biome zoning** from climate (§5) + density scatter on a terrain patch.
3. **Tidal-lock light-by-longitude** + lean/phototropism.
4. **LOD chain** to billboards + ground-cover, matched to the orbital vegetation colour.
5. **Full archetype set** (frond/succulent/grass/mat/vine/floating), seasonal states, exotic biochemistries.
6. **Hook into the surface-exploration terrain** so any habitable planet is walkable and populated.

## 10. OPEN QUESTIONS / TBD

- Exact `g` exponent and pressure-support curve (tune for readability, not realism).
- Precipitation model fidelity (simple humidity field vs orographic simulation).
- How much animation (wind sway, growth) at each LOD.
- Reproductive/animal life is out of scope here (flora only) but the biome map should be reusable for fauna later.
- Consistency contract: **the surface pigment/biome MUST reduce, at the LOD limit, to the orbital-view vegetation** so
  a planet looks the same from space and from the ground.
