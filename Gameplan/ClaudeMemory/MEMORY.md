# CLAY — Memory Index

- [CLAY environment architecture](clay-environment-architecture.md) — how biomes/background/caustics/flow are wired; the legacy-vs-new system split and serialization gotchas
- [CLAY game design](clay-game-design.md) — MMO-Spore vision; canonical design docs in Assets/Design/; nested servers, catastrophe deadline, locked decisions & open TBDs
- [CLAY gameplay prototype](clay-gameplay-prototype.md) — the S4 vertical slice scripts (eat/engulf/AI/behaviour/nodes/flagellum) and how they wire together
- [CLAY cell death FX](clay-cell-death-fx.md) — the pop/engulf spill effect + 2D-lighting/material gotchas (avoid repeating the long FX saga)
- [CLAY galaxy generation](clay-galaxy-generation.md) — star/planet/habitability procedural gen module (Assets/_Scripts/Galaxy), real astrophysics
- [CLAY BRG shader gotchas](clay-brg-shader-gotchas.md) — DOTS/Entities Graphics star+dust shaders can't read camera position; use clip-space math; shader hot-reload traps
- [CLAY planet surface](clay-planet-surface.md) — press L to land; runtime scene, quadtree terrain from the orbital height field, climate→biome, lineage flora; key gotchas
- [CLAY nebula rendering](clay-nebula-rendering.md) — volumetric hero nebula system; the working fBm-minus-radius shape trick, 3 classes, N-preview, 2026-09-18 checkpoint, open dark-cloud issue
