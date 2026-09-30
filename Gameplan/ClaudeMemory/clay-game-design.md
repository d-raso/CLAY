---
name: clay-game-design
description: "Where CLAY's canonical game/stage design lives and the core MMO-Spore vision"
metadata: 
  node_type: memory
  type: project
  originSessionId: f2bceb4a-0ee1-44fc-9820-0eaac5494694
---

CLAY is an **MMO reimagining of Spore** (Cell → Creature → Tribal → Civ → Space). Canonical
design docs live in `Assets/Design/` — read them before doing mechanics/design work:
- `Architecture.md` — nested-server model (tide pool ⊂ planet ⊂ galaxy), graduation gates, catastrophe deadline, lifecycle.
- `CellStage.md` — cell-stage mechanics, mini-stages (Microbe→Protist→Colony), core loop, colony formation, MVP slice.
- `PlanetaryParameters.md` — star/planet param schema + science→gameplay derivation + catastrophe generator + worked examples (Corvane system).

Key locked decisions: nested servers (scope = server = stage); **many colonies graduate** (not
battle-royale), bounded by a per-planet **ecosystem catastrophe** whose type/timing/occurrence
derive from exoplanet-habitability params; async graduation; death respawns into a tide pool (or
same colony); colonies form **emergently** behind an achievement gate (gate TBD).

The design intentionally reuses systems already built: [[clay-environment-architecture]]
(`PlanetaryEnvironment` already exposes baseTemperature/Acidity/Salinity/Viscosity that the
planet schema feeds; `BiomeLibrary`'s 6 biomes are the `dominantBiomes` output; `FlowFieldManager`
currents become traversal risk/reward).

Open TBDs: colony-formation achievement gate; per-planet arrival policy (rolling vs wave);
colony control model (one player commands vs. each player drives one cell of a shared body).
