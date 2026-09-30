# CLAY — Planet Taxonomy, Causal Factors, Moons & Belts

Design brainstorm for driving planet generation from **causes**, not fixed buckets. Each category below lists the
factors that produce it. Factors marked **[NEW]** don't exist in the sim yet and should be added (see §3). The
generator should: roll the causal factors → the archetype (and its variations) *emerges* from them.

---

## 1. CAUSAL FACTORS

### Already in the sim
- Stellar: spectral class, luminosity, age, flare activity, metallicity ([Fe/H]).
- Orbit: semi-major axis, eccentricity, inclination.
- Body: mass, radius, axial tilt, rotation period, albedo.
- Derived: insolation, greenhouse, mean temp, habitability.
- Composition: volcanism, mineral diversity, water coverage, water chemistry.

### [NEW] factors to add
- **Tidal locking / spin state** — synchronous (1:1), resonant (3:2), fast, slow, retrograde. From mass, distance,
  star mass, age, eccentricity. → eyeball worlds, banding, extreme day/night.
- **Tidal heating** — from eccentricity + close orbit + resonance with siblings/moons. → Io-like volcanism, subsurface oceans.
- **Tectonic mode** — mobile-lid (plate tectonics), stagnant-lid, heat-pipe (Io), or dead. From mass, age, water, heat. → mountains, resurfacing, crust age.
- **Magnetic field / dynamo** — from mass, spin, molten core. → auroras, atmosphere protection vs. stripping.
- **Atmospheric pressure** (distinct from composition) — trace / thin / Earth-like / thick / supercritical.
- **Volatile inventory** — separate abundances of H₂O, CO₂, N₂, CH₄, NH₃, SO₂, H₂. Drives which ices/oceans/atmos form at a given temp.
- **Redox / oxygenation state** — reducing vs oxidizing. → grey basalt vs red rust vs organic tar; banded-iron transition.
- **Impact / bombardment history** — crater density, giant-impact basins, late heavy bombardment, synestia.
- **Migration history** — formed-cold-moved-hot (Chthonian, thawed ocean) or formed-hot-moved-cold.
- **UV / flare dosage** — atmosphere stripping, surface sterilization, photochemical haze.
- **Rings & moon retinue** — presence/mass of rings, number/type of moons (see §4).
- **Weather / circulation** — superrotation, storm bands, cloud fraction, dust storms.
- **Biosphere** — presence, complexity, photosynthetic pigment (tuned to star spectrum), post-biotic collapse.
- **Land/ocean fraction & ocean depth**, **salinity**, **cloud albedo feedback**.

---

## 2. ~100 PLANET CATEGORIES (by family) → causes

### A. Molten / scorched
1. Lava-ocean world — very high insolation / tidal heat.
2. Silicate-vapor world — extreme heat; rock-vapor atmosphere; day-side magma, night-side rock rain.
3. Carbon-lava world — carbon-rich + hot.
4. Hell-moon (Io-type) — strong tidal heating from resonance; sulfur volcanism.
5. Chthonian world — gas giant whose envelope was stripped by its star; exposed metal core.
6. Obsidian world — cooled lava; glassy black, high specular.
7. Sublimation world — ices boiling off on approach; comet-like tails.
8. Glass-rain world — hot, silicate clouds; molten day side (HD 189733b-ish).

### B. Rocky / temperate (terrestrial)
9. Earth-analog. 10. Continental world. 11. Archipelago world. 12. Pangaea supercontinent.
13. Desert world (dry, warm). 14. Ochre iron-oxide (Mars-type). 15. Basalt-plains world. 16. Volcanic-highland world.
17. Cratered dead world (old, airless, stagnant-lid). 18. Regolith/dust world. 19. Karst world (eroded, wet history).
20. Clay-shelf world (mineral-rich, mudstone flats). 21. Canyon world (tectonic rifting + water erosion).
22. Mesa/plateau world. 23. Steppe/savanna world (marginal biosphere). 24. Salt-flat / evaporite world (dried ocean).

### C. Ocean / hydrosphere
25. Global ocean world (deep, no land). 26. Shallow-sea world. 27. Archipelago ocean.
28. Eyeball ocean (tidally locked; substellar sea, terminator ring, night ice-cap). 29. Storm-ocean world (fast spin, endless storms).
30. Ice-capped ocean (polar caps, temperate belt). 31. Supercritical-water world. 32. Hycean world (H₂ atmosphere + warm ocean).
33. Methane-sea world (Titan-type, cold). 34. Ammonia-ocean world. 35. Brine world (hypersaline, colored seas).

### D. Ice / cryo
36. Snowball world (globally frozen). 37. Glacier world. 38. Europa-type (ice shell + subsurface ocean, red lineae).
39. Enceladus-type (cryovolcanic plumes). 40. Triton/Pluto nitrogen-ice world. 41. CO₂-ice (dry-ice) world.
42. Clathrate world. 43. Dirty-ice world (ice + tholin/dust). 44. Frost-desert world. 45. Cryovolcanic resurfaced world.

### E. Toxic / chemistry-dominated
46. Venusian greenhouse (thick CO₂, obscured surface). 47. Sulfuric-cloud world. 48. Sulfur world (Io yellow crust).
49. Chlorine world (green skies/seas). 50. Copper/verdigris world (teal oxidized minerals). 51. Halide-salt world.
52. Tholin/organic-haze world (deep red-orange). 53. Photochemical-smog world. 54. Ammonia-haze world. 55. Mercury-vapor world.

### F. Composition extremes
56. Carbon planet (graphite crust, diamond mantle). 57. Tar/bitumen world. 58. Iron world (Mercury-type, stripped mantle).
59. Super-Mercury (huge iron core). 60. Diamond world. 61. Corundum/ruby world (Al-rich, red gems). 62. Coreless silicate world.
63. Helium-dominated world. 64. Water-vapor world. 65. Iron-snow world (metal precipitation).

### G. Mass / gravity extremes
66. Super-Earth (high g, flat relief, thick atmosphere). 67. Mega-Earth. 68. Sub-Earth (thin/no atmosphere).
69. Dwarf rocky world. 70. Puffy low-density world.

### H. Biosphere
71. Garden world (green chlorophyll). 72. Red-vegetation world (M-dwarf light). 73. Purple/retinal world (early photosynthesis).
74. Black-plant world (dim red-dwarf light). 75. Fungal/lichen world. 76. Microbial-mat world (colored shallow seas).
77. Oxygenating world (banded-iron seas, early O₂). 78. Methane-biosphere world. 79. Bioluminescent night world. 80. Post-biotic world (collapsed biosphere, relic oxygen).

### I. Rotation / tidal / orbital dynamics — **[NEW] factors**
81. Tidally-locked eyeball (hot substellar / frozen antistellar). 82. Twilight-habitable locked world (life on the terminator ring).
83. 3:2 resonance world (Mercury-like, slow scorching spin). 84. High-obliquity world (extreme seasons, wandering caps).
85. Retrograde world. 86. Fast-rotator (oblate, strong banding). 87. Slow-rotator (huge day/night temperature swings).
88. Eccentric world (seasonal melt/freeze cycles). 89. Trojan world (shares an orbit). 90. Binary planet (mutual tidal locking).

### J. History / catastrophe
91. Post-giant-impact world (magma ocean / synestia). 92. Tidally-disrupted remnant. 93. Captured rogue world (odd orbit, cold).
94. Migrated thawed-ocean world. 95. Flare-scoured world (stripped atmosphere, irradiated). 96. Ejecta-dusted world (ring/impact debris fallout).

### K. Gas / ice giants
97. Jupiter-type (bands, storms). 98. Hot Jupiter (puffy, close). 99. Ice giant (Neptune blue).
100. Sub-Neptune / gas dwarf. 101. Ammonia-/water-cloud giant. 102. Superstorm giant. 103. Ringed giant. 104. Helium giant.

---

## 3. MAPPING FACTORS → LOOK (implementation notes)
- **Tidal locking** (81–83): if locked, generate a *substellar hemisphere* (hot: ocean/desert/lava) and an
  *antistellar* cold cap, with a terminator ring — a per-longitude gradient in the surface bake keyed to the
  substellar point, not latitude. Big visual identity, currently missing.
- **Tectonic mode**: mobile-lid → linear mountain belts + young crust (few craters); stagnant-lid → old, cratered,
  fewer ranges; heat-pipe → resurfaced, volcanic, no craters.
- **Redox/oxygenation**: interpolate rock hue grey↔rust by oxidation; banded-iron for the oxygenating stage.
- **Atmospheric pressure**: drives haze thickness (already partly done via mass), and whether liquid can exist.
- **Obliquity/rotation**: cap size/placement, banding strength, day-night contrast.
- **Magnetic field**: aurora ring near poles on night side; correlates with atmosphere retention.

## 4. MOONS & BELTS (next code edit)
**Moons** (per planet; count from mass, distance, formation):
- Regular co-formed moon; impact-debris moon (large, like Luna); captured irregular (small, odd orbit/inclination);
  tidally-heated volcanic moon (resonance → Io); icy moon w/ subsurface ocean (Europa); cryovolcanic (Enceladus);
  atmosphere moon (Titan, cold + massive); ring-shepherd moonlet; co-orbital/trojan; binary companion.
- Factors: host mass (more/bigger moons), distance vs frost line (icy vs rocky), resonances (tidal heating), age.

**Belts & rings** (per system / per planet):
- Asteroid belt — a "failed planet" gap, usually shepherded by a nearby giant; rocky/metallic/carbonaceous mix.
- Kuiper/ice belt — beyond the frost line; icy bodies, comets.
- Debris disk — young systems; dusty.
- Planetary rings — ice/rock from a disrupted moon inside the Roche limit; shepherd moonlets; giants especially.
- Trojan swarms — at a giant's L4/L5.
- Factors: system age (young = more debris/dust), giant presence (clearing/shepherding), collisions, frost line.

**Data model additions:** `StarSystem.belts` (inner/outer AU, density, composition, tilt); `PlanetData.moons`
(list of a lightweight MoonData), `PlanetData.hasRings` + ring params, plus the [NEW] factor fields in §1 so the
generator and the renderer can both read them.
