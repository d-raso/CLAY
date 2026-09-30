# Planet Surface — Roadmap

Logged 2026-09-29. The on-foot planet surface (L to land, Y for the Terrain Lab) now has: quadtree terrain from the
orbital height field, orbital-map colouring, 21 terrain types with baked PBR texture sets + hex anti-tiling,
physically based sky scattering, binary suns, sky bodies, meteorites, lineage-based flora, filmic post.
All six items below are **committed** — the order is a suggestion, not a priority cut.

---

## 1. Grass & ground cover (3D) — BUILT 2026-09-29 (SurfaceGrass.cs + .shader), awaiting in-game test
Textures can't give grass depth. Add real blades near the camera.
- GPU-instanced blade clumps (a few mesh variants, 3 LODs) within ~40–80 m, density from the Grassy/Mossy palette
  weights at each point (same data the terrain shader uses), fading into the Grassy texture beyond.
- Wind sway in the vertex shader (per-planet wind from `PlanetClimate.windLoad`), colour from the vegetation
  pigment (`PlantGenome` star-temperature pigment) with dry/lush variation from moisture.
- Alien variants: moss cushions, lichen crust tufts, fleshy "turf", crystalline mats — picked from the planet's lineages.
- Interacts with plants: none under tree trunks, thinner in deep canopy shade.

## 2. Water — BUILT 2026-09-29 (Gerstner + refraction/absorption + foam + underwater fog), awaiting in-game test; caustics still to do
- Proper sea shader: planar/SSR-style sky reflection (the sky model already exists), Fresnel, Gerstner waves scaled by
  gravity & wind, depth-based colour/absorption using the planet's water chemistry, shoreline foam, caustics in the shallows.
- Exotic liquids: methane/ethane seas (Titan: glassy, low waves, amber), iron/sulfide seas, brines.
- Underwater view: fog + absorption, light shafts.
- Ties into item 6 (rivers, lakes).

## 3. Ambient occlusion & contact shadows — BUILT 2026-09-29 (SurfaceSSAO feature on GalaxyRenderer3D, inactive by default, toggled by SSAO.cs; baked per-vertex plant AO in uv.w), awaiting in-game test
- Enable URP SSAO renderer feature on the 3D renderer (asset change) — plants/rocks/ground contact.
- Tune intensity per sun elevation; make sure it's surface-only (not the galaxy view).
- Consider baked per-vertex AO on plant meshes (trunk bases, leaf clusters).

## 4. Weather & atmosphere — BUILT 2026-09-29 (SurfaceWeather.cs, Include/Clouds.hlsl raymarched deck + matching ground shadows, rain/snow/methane/ash/dust, wet ground + settled snow), awaiting in-game test; aerial-perspective fog replacement still to do
- Volumetric / raymarched cloud layer (2.5D or low-res volume) lit by the existing scattering model; coverage & type
  from climate (humidity, pressure, temperature): cumulus, stratus, haze decks, Titan smog.
- Precipitation where the climate says so: rain, snow, dust storms (dusty worlds), ash on volcanic worlds.
- Aerial perspective from the scattering model (replace the exponential fog), sun shafts.
- Day-to-day variation (weather seed + time).

## 5. Smart plant generation — BUILT 2026-09-29 (SurfaceEcology: soil fertility from terrain types, species niches xeric/hydric/shade, canopy dominance + shade, site-shaped growth, age classes + dead snags), awaiting in-game test; fallen logs + impostors still to do
Plants should look like they *belong* where they grow.
- Placement driven by resources: water (distance to rivers/sea, moisture, valleys), light (slope aspect, canopy shade),
  soil (terrain type: none on salt/ice/lava, succulents on sand/gravel, moss on cold/wet rock).
- Communities & succession: forest cores with understorey and ground cover, edges/ecotones, pioneer species on fresh
  ground, clearings; species ranges already exist — add competition (dominant species crowd others out).
- Morphology responds to the spot: wind-bent/stunted on ridges, tall and thin in dense forest, spreading in the open,
  smaller at the treeline, riparian species along water.
- Growth variety within a species (age classes: seedlings, adults, dead snags / fallen logs).
- Performance: hierarchical placement + impostors for distant forests.

## 6. Landforms ("Minecraft-style" fun terrain) — PART 1 BUILT 2026-09-29: rivers+lakes (SurfaceHydrology priority-flood drainage, background build, versioned tile rebuild), plateaus, chasms/rifts, volcanoes+calderas, glaciers. STILL TO DO: caves/overhangs/arches (volumetric chunks), waterfalls, deltas, karst, fjords, sea stacks, hoodoos
Big, recognisable features driven by each planet's geology, climate and history:
- **Mountain ranges** along tectonic belts (ridged + erosion-looking drainage), peaks with snow lines.
- **Valleys** carved by rivers and glaciers (U-shaped glacial vs V-shaped fluvial).
- **Rivers & lakes**: flow accumulation from the height field → river channels that actually run downhill to the sea,
  meanders on flats, deltas, waterfalls at escarpments, lakes in basins; exotic-liquid rivers on Titan-likes.
- **Chasms & canyons**: rift valleys, slot canyons, Valles-Marineris-scale trenches on tectonically dead worlds.
- **Plateaus & mesas / buttes**: erosion-resistant caprock (already partial: terraced strata).
- **Glaciers & ice sheets**: flowing tongues in valleys, crevasse fields, moraines, ice cliffs at the sea.
- **Caves**: needs volumetric terrain (the current height field can't overhang) — plan: local marching-cubes / SDF
  chunks near the player for cave mouths, arches, overhangs, lava tubes, sinkholes.
- **Volcanoes & craters**: shield volcanoes, calderas, lava plains, crater rims with central peaks.
- **Dunes** (done: warped fields), **badlands**, **karst** towers, **fjords**, **sea stacks**, **hoodoos**.
- Tech notes: rivers/glaciers need a coarse global drainage pass (on the orbital height grid) sampled at surface
  scale; overhangs/caves need a second volumetric layer blended with the height field near the camera.
