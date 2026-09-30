# CLAY — Planetary & System Parameters

> The data model for procedurally generating solar systems and planets, grounded in real
> exoplanet-habitability science. These params drive *everything* downstream: which biomes a
> planet has, how many tide pools it supports, its carrying capacity, and what catastrophe
> (if any) ends its cell stage. Designed to translate directly into Unity ScriptableObjects.

Generation flows top-down, each layer deriving from the one above:

```
STAR  →  ORBIT  →  PLANET PHYSICAL  →  CLIMATE/SURFACE  →  BIOMES  →  GAMEPLAY TUNING + CATASTROPHE
```

---

## 1. Star / system parameters  (`StarSystemSO`)

| Param | Type / range | Notes & downstream effect |
|-------|--------------|---------------------------|
| `spectralClass` | enum O,B,A,F,G,K,M | Drives luminosity, color, lifetime, flare activity. G/K = "Goldilocks" long-lived. M = common, flares. O/B = die too fast for life. |
| `stellarMass` | 0.08–20 M☉ | Sets luminosity & lifetime. |
| `luminosity` | derived (L☉) | ∝ mass^~3.5. Sets habitable-zone radii & insolation. |
| `effectiveTemp` | 2,400–40,000 K | Star color + UV output. |
| `stellarAge` | 0–13 Gyr | Young = volatile/flares; old = stable but nearing end. Hard ceiling on planet life. |
| `metallicity` | -1.0–+0.5 [Fe/H] | Higher → more rocky planets/resources. |
| `flareActivity` | 0–1 | High (esp. M dwarfs) → **radiation-burst catastrophes**. |
| `starCount` | 1–3 | Binary/trinary → unstable orbits, exotic light. |
| `hzInner`,`hzOuter` | derived (AU) | Habitable zone = √(L/1.1) to √(L/0.53). |
| `planets[]` | list | Orbital architecture. |

---

## 2. Planet — orbital parameters  (`PlanetSO.orbit`)

| Param | Type / range | Downstream effect |
|-------|--------------|-------------------|
| `semiMajorAxis` | AU | Distance from star → insolation. Position vs. HZ = baseline climate. |
| `eccentricity` | 0–0.4 | Seasonal extremes; high e → **freeze/heat-swing catastrophes**. |
| `orbitalPeriod` | derived (days) | Year length. |
| `axialTilt` | 0–90° | Obliquity → seasons & latitude bands (already modeled in `BiomeLibrary` via latitude). |
| `rotationPeriod` | hours | Day length → day/night temperature swing; tidally-locked = extreme. |

---

## 3. Planet — physical & surface parameters  (`PlanetSO`)

| Param | Type / range | Downstream effect |
|-------|--------------|-------------------|
| `mass` | 0.1–10 M⊕ | → gravity, escape velocity (atmosphere retention). |
| `radius` | 0.5–3 R⊕ | → surface gravity. |
| `surfaceGravity` | derived (g) | Affects movement feel, atmosphere. |
| `bulkComposition` | enum rocky / ocean / icy / carbon | Sets liquid type & geology. |
| `albedo` | 0–1 | Reflectivity → cooling (high albedo → snowball risk). |
| `atmoPressure` | 0–100 bar | Too thin = no liquid; too thick = greenhouse. |
| `atmoComposition` | %{N2,O2,CO2,CH4,…} | CO2/CH4 → greenhouse; O2 → later stages. |
| `greenhouseStrength` | derived (K) | Warms surface above blackbody temp. |
| `magneticField` | 0–1 | Shields from flares. Low + active star → **radiation catastrophe**. |
| `volcanism` | 0–1 | Vent biomes + nutrient cycling; high → **supervolcanic/anoxia catastrophe**. |
| `tectonics` | 0–1 | Crust renewal; affects long-term stability. |
| `liquidType` | enum water / ammonia / methane / brine | Cold worlds use alt solvents. |
| `liquidCoverage` | 0–1 | Fraction of surface with liquid → **# and size of tide pools, carrying capacity, lifecycle length**. |
| `meanSurfaceTemp` | derived (°C) | From insolation + albedo + greenhouse. Feeds `PlanetaryEnvironment.baseTemperature`. |

---

## 4. Derived gameplay parameters  (the science → stage mapping)

These are computed from §1–3 and consumed by the live game:

| Gameplay param | Derived from | Consumes / feeds |
|----------------|--------------|------------------|
| `habitabilityIndex` 0–1 | temp in liquid range × pressure × magnetic × stellar stability | master quality knob |
| `tidePoolCount` | `liquidCoverage` × `habitabilityIndex` | how many cell-stage servers spawn |
| `carryingCapacity` | `liquidCoverage` × habitability | players/colonies before planet closes (Architecture §5) |
| `foodDensity` | volcanism (nutrients) + insolation (light) | biome edible spawn rates |
| `dominantBiomes[]` | temp + chemistry + latitude | which of the 6 `BiomeLibrary` biomes appear & where |
| `catastropheProfile` | see §5 | the cell-stage deadline |
| `baseTemperature`, `baseAcidity`, `baseSalinity`, `baseViscosity` | climate/chemistry | **already exist on `PlanetaryEnvironment`** |

## 5. Catastrophe profile  (the deadline generator)

Each planet rolls a catastrophe **type, ETA, and probability of occurring** from its params.
A high-quality, stable world may roll *none* (open-ended cell stage); a marginal world fires
early.

| Catastrophe | Triggered by (high) | In-world telegraph |
|-------------|--------------------|--------------------|
| Snowball glaciation | albedo, eccentricity, low insolation | water cools, caustics dim, biomes → Cold Brine |
| Runaway greenhouse | CO2, insolation, close orbit | water heats/evaporates, acidity spikes |
| Stellar-flare burst | star flareActivity, low magneticField | periodic radiation pulses, then a lethal one |
| Supervolcanic anoxia | volcanism, tectonics | vent activity surges, oxygen/food collapse |
| Bolide impact | orbital debris / architecture | fixed-date countdown, then global shock |
| Orbital instability | binary star, high eccentricity | escalating temperature swings |

ETA distribution is also param-driven (young/volatile star → sooner). See `Architecture.md` §3, §6.

---

## 6. Worked example system — **Corvane** (G2V, Sun-like, 4.5 Gyr)

`luminosity 1.0 L☉ · HZ ≈ 0.95–1.37 AU · flareActivity 0.1 · stable`

### Planet I — **Sirin**  (inner edge, the crucible)
- orbit 0.93 AU (just inside HZ), ecc 0.05 · mass 0.9 M⊕, gravity 0.95 g
- albedo 0.2, CO2-rich, greenhouse high → **meanTemp ~46 °C**, liquidCoverage 0.35 (evaporating)
- magneticField 0.6, volcanism 0.7
- **Derived:** habitability 0.45 · ~3 tide pools · foodDensity high · biomes: Iron Seep, Mineral Springs, Sunlit Shallows
- **Catastrophe:** Runaway greenhouse, ETA short (~heat death). Carnivore-heavy, frantic.

### Planet II — **Vael**  (mid-HZ, the cradle)
- orbit 1.15 AU, ecc 0.02 · mass 1.0 M⊕, gravity 1.0 g · tilt 23°
- albedo 0.3, balanced N2/CO2 → **meanTemp ~16 °C**, liquidCoverage 0.71 (water)
- magneticField 0.9, volcanism 0.4
- **Derived:** habitability 0.92 · ~12 tide pools · biomes: Sunlit Shallows, Algal Bloom, Mineral Springs
- **Catastrophe:** none likely / very late. The "easy", long-lived, biodiverse world.

### Planet III — **Hethe**  (outer edge, the brine)
- orbit 1.34 AU, ecc 0.22 (eccentric) · mass 1.3 M⊕, gravity 1.1 g
- albedo 0.55, thin atmo → **meanTemp ~-4 °C**, liquidType brine, liquidCoverage 0.5
- magneticField 0.7, volcanism 0.3
- **Derived:** habitability 0.55 · ~6 tide pools · biomes: Cold Brine, Murky Silt Flats
- **Catastrophe:** Snowball glaciation, ETA medium (eccentric cold snaps). Sparse, harsh, ambush play.

### Archetype not in Corvane — **Flare world** (M-dwarf)
Common red-dwarf world: tidally locked, magneticField low, star flareActivity 0.8 →
recurring **radiation bursts** culminating in a lethal flare. High-tension, short lifecycle.

---

## 7. Implementation sketch
- `StarSystemSO`, `PlanetSO` ScriptableObjects holding §1–3 raw params.
- A `PlanetGenerator` derives §4–5 and writes into `PlanetaryEnvironment` (already consumes
  baseTemperature/Acidity/Salinity/Viscosity) + a new `CatastropheController`.
- Seeds are deterministic per planet so a server reload reproduces the same world.
