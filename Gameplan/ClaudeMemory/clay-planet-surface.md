---
name: clay-planet-surface
description: "On-foot planet surface (press L in system view) — runtime scene, quadtree terrain from orbital height field, climate→biome, lineage-based flora ecology"
metadata:
  node_type: memory
  type: project
  originSessionId: acbd9b6d-8db3-4552-b515-aaccdad95992
  modified: 2026-09-26T06:40:23.578Z
---

Built 2026-09-25. Code in `Assets/_Scripts/Surface/` (SurfaceWorld, SurfaceGeo, SurfaceTerrain, SurfaceEcology) + `Galaxy/PlanetClimate.cs` + shaders `SurfaceTerrain.shader`, `SurfaceWater.shader`.

- Entry: SystemViewer `L` → `SurfaceWorld.Enter(viewer, planet, sysSeed, pseed, landingDir)`; pseed = `DetRng.Hash(sys.seed, index+1)` (must match orbital bake); landingDir = `b.tf.InverseTransformPoint(cam)` normalized.
- 2026-09-26: L now arms a landing TARGET (ray-sphere marker in SystemViewer.LateUpdate), click lands there; Enter(..., landingDir, sunLocal, viewerSpin) drives the day cycle (1 local hour per real minute). Terrain mesh uv0 = material weights (rock,sand,snow,soil), color.a = wetness; albedo derived from the ORBITAL colour (SurfaceGeo.Ground) so surface matches space view.
- LandingContext (SystemViewer.BuildLandingContext): copies the viewer's painted `_SurfaceTex` pixels (geo.SetOrbitalMap → Orbital() samples it; recomputed Albedo did NOT match), other bodies as sky spheres, and a RenderToCubemap star background (SystemRoot hidden during capture; `CaptureStarBackground` flag to disable if it misbehaves). Terrain colour.a: ≥.5 wetness, <.5 playa mud-crack amount (user loves the cracks — keep them, but only on dry flats).
- Terrain orphan-tile bug: a stale task result for a re-created chunk key must be dropped (ReferenceEquals check) or it leaves an unpositioned tile glued to the camera.
- Never disable the surface sun Light (e.g. at night): URP promotes another directional light (system-view star) to main light → region floodlit. Use intensity 0.
- Surface look (2026-09-26): own post Volume on layer 31 (cam volumeLayerMask = surface layer only, so galaxy bloom never leaks): ACES + mild bloom/grade. Sky = single-scattering Rayleigh+Mie+absorption (SurfaceSky Scatter/AirMass) mirrored in C# SkyScatter → fog + ambient derive from the same model. User goal: realistic (not cartoony) Spore-like game.
- Terrain types (2026-09-26): 21 procedural PBR types (SurfaceGeo.TerrainType ↔ EvalType ids in SurfaceTerrain.shader). Per-planet palette of 8 (geo.BuildPalette survey; slot 0 always Rocky) → globals _TypeIdA/B; vertices carry 8 weights in uv0+uv1 (interpolate seamlessly — per-vertex type IDs would seam). colour.a = wetness. No fwidth inside the type loop (gPix set outside).
- Ground textures (2026-09-26): procedural in-shader types looked geometric (voronoi cells, sine stripes, derivative-bump pixelation, aliasing "hairs") → replaced by TerrainTextures.cs, a CPU bake of tileable 512² sets per type into Texture2DArrays (_TerrA normal.xz/height/AO, _TerrB tone/rough/emit/metal, _TerrScale m per tile), sampled with SAMPLE_TEXTURE2D_ARRAY_GRAD (derivatives taken outside the type loop) + rotated second sample for anti-tiling. Bouldery = rock-mesh density (geo.Boulderiness), not a texture. Terrain Lab = Y key.
- ROADMAP (logged 2026-09-29, all committed): Assets/Design/Surface_Roadmap.md — 1 3D grass, 2 water, 3 SSAO, 4 weather/clouds, 5 smart plant placement, 6 landforms (rivers, canyons, glaciers, caves via volumetric chunks, etc.). Check it when picking the next surface task.
- SSAO: "SurfaceSSAO" renderer feature added (m_Active 0) to Assets/Materials/Cell/GalaxyRenderer3D.asset; SSAO.cs Find/SetActive/SetIntensity(reflection). Source = Depth (custom shaders have DepthOnly but NO DepthNormals pass). Plant AO baked per vertex into uv.w (PlantBuilder.BakeAO). Backup of the pre-change renderer asset in the session scratchpad.
- Weather: SurfaceWeather.cs drives globals _CloudShape/_CloudOffset (Assets/Materials/Include/Clouds.hlsl shared by sky raymarch + terrain/grass/flora cloud shadows), _RainWet, _SnowCover; SunFactor/FogFactor applied in ApplySkyAndSun. Dispose zeroes the globals (the lab/galaxy must not inherit clouds).
- Landforms (2026-09-29): SurfaceGeo.At adds plateaus/chasms/volcanoes/glaciers (masks in Sample), then hydro.Apply (SurfaceHydrology: priority-flood drainage on a 320² × 3.2 km grid, built in a background Task at landing). When it lands: terrain.Version++ (tiles rebuild in place, mesh swap) + ecology/rocks/grass Invalidate(). Terrain uv2 = (water, flowX, flowZ) → river/lake surface in SurfaceTerrain.shader; _RiverColor global. Caves NOT done (need volumetric chunks).
- Uses a RUNTIME scene (SceneManager.CreateScene) so RenderSettings fog/ambient/sun are its own; galaxy/system stay loaded + `viewer.suspended`. Camera borrowed like FloraLab: renderer index 1 (3D), culling layer 30, restored via reflection on `m_RendererIndex`. StarSystemEntry Escape guard checks `SurfaceWorld.Active`.
- Coordinates: local tangent frame (x east, z north, metres), doubles + floating origin (rebase at 1500 m). East = Cross(up, north) (Unity left-handed — the reverse mirrors the map).
- Height = `PlanetTexture.Sampler(...).Height01` (the orbital field, via new public SurfaceSampler; PP/Exotic made internal) × ~7000 m + local detail noise.
- Horizon curvature is a vertex-shader drop via global `_CurvK = 1/2R` in Flora/SurfaceTerrain/SurfaceWater shaders (0 in the Flora Lab).
- Flora: species = mutated descendants of 3–5 planet lineages, re-rolled until `PlantBiology.FitsLocal` for the biome; rendered with RenderMeshInstanced (Flora.shader now supports instancing + fog), 3 LODs via `PlantBuilder.Detail`.

Gotchas learned the hard way (2026-09-25/26):
- A separate RUNTIME scene (SceneManager.CreateScene) rendered NOTHING with this camera setup → surface lives in the current scene on layer 31 (same as FloraLab), RenderSettings saved/restored manually.
- SystemViewer.Update calls the L-key handler mid-update, then keeps running its orbit-camera code → overwrites camera pos/farClipPlane AFTER Init (far=0 → solid colour, far=1000 → "sky wall"). Surface must re-apply camera settings EVERY frame. StarSystemEntry LateUpdate/OnGUI also need the SurfaceWorld.Active guard.
- Toggling other cameras made the galaxy re-upload its 2M-star buffer (~290 MB) → D3D12 upload overflow → editor-wide flashing. Don't touch other cameras.
- Mathf.SmoothStep(a,b,t) INTERPOLATES a→b; it is not HLSL smoothstep. Use an SS() helper for masks (bug made every landform apply everywhere).
- Diagnose from `%LOCALAPPDATA%\Unity\Editor\Editor.log` ([Surface] status lines every 2 s) and auto-screenshots in `<project>/SurfaceShots/` — no need to ask the user to copy errors.

Related: [[clay-galaxy-generation]], [[clay-environment-architecture]]
