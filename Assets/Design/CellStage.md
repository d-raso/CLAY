# CLAY — Cell Stage Design

> The opening stage. A persistent, server-simulated microbial ocean: agar.io's predator/prey
> readability + Spore's editor depth + an ecosystem you can actually deplete. See
> `Architecture.md` for the server model and `PlanetaryParameters.md` for the world inputs.

## 1. Fantasy & pillars
- **You are simultaneously food and predator.** Threat is readable at a glance from a cell's silhouette.
- **The environment is a mechanic** — currents, murk, and biome chemistry are tools and hazards, not backdrop.
- **Evolution is earned and expressive** — parts are active abilities you aim and time, not stat sticks.
- **The world is alive** — food/populations are simulated; patches deplete; territory is emergent.

## 2. Sub-stages (the cell-stage arc)

The arc follows real evolutionary innovations, and **each unlocks a different kind of gameplay.**
Full science-to-gameplay mapping is in **`CellStage_SubStages.md`**; summary:

1. **S0 Protocell** (abiogenesis) — solo survival: gather monomers in a lipid vesicle, manage membrane integrity. No predation.
2. **S1 Replicator** (RNA world) — solo: feed a self-copying genome; first (passive) evolution choices.
3. **S2 Prokaryote** (bacteria/archaea) — competitive efficiency: metabolism choice, horizontal gene transfer, biofilms. **Still no engulfing.** Player-driven Great Oxidation Event possible here.
4. **S3 Endosymbiosis Event** — pivotal, branching: acquire an internal power-plant organelle (mitochondrion / chloroplast / chemosymbiont, chosen by planet). Unlocks the eukaryote tier.
5. **S4 Eukaryote FFA** — **the agar.io phase**: cytoskeleton enables phagocytosis, so now you engulf other cells. Parts as active abilities. The skill core.
6. **S5 Colony** (multicellularity) — clonal vs aggregative (environment-biased); cell differentiation; **the unit that graduates** to creature stage.

> Key constraint: **phagocytosis (engulfing) is cytoskeleton-dependent**, so the agar.io
> predator/prey game is gated to S4 — it cannot appear in S0–S2. That makes the early game a
> distinct builder/survival experience.

## 3. Core loop (moment-to-moment)
**Sense → eat → avoid → grow → (bank genes) → evolve.**

- **Diet fork:** herbivore (graze plant motes / organic matter) vs. carnivore (hunt cells, eat meat chunks). Shapes build and food-web role.
- **Environment as mechanic** (all already rendered in-engine):
  - **Currents** — fast risk/reward traversal; lose control near predators.
  - **Murkiness** — your sensory range; silt = ambush, clear shallows = mutual exposure.
  - **Biome chemistry** (temp/acidity/salinity) — gates who lives where; players self-sort into niches.
- **Parts as active abilities:** flagella (dash), spike/proboscis (directional attack), toxin sac (area deterrent), cilia (fine maneuver), electroreceptors (see through murk), photosynthetic membrane (passive food in light).

## 4. Evolution (the editor)
- Eating banks **genetic material**.
- Spend it in the editor on parts/morphology. **Asynchronous** — does not pause the shared
  world. You either edit while still vulnerable, or duck into a safe pocket. (No world-pause in an MMO.)
- The soft-body cell deforms around attached parts so builds stay legible to other players.

## 5. Colony formation (the social hinge)
- **Colonies coalesce emergently** from nearby players (NOT pre-formed teams).
- **Gated by an achievement** you must reach before you can form/join a colony. *(Exact gate TBD —
  candidates: reach Protist tier, survive N time, bank X genes, win a predation milestone.)*
- The grouped colony is the unit that pursues graduation (Architecture.md §2).
- **Death in colony:** respawn into the **same colony** if possible (rejoining may be hard);
  otherwise **form or join another colony**, including **recruiting colony-less players**.
- Open: solo player commanding a colony vs. each player driving one cell of a shared body.

## 6. The catastrophe deadline
A planetary ecosystem event imposes a **hard time limit** on colonies still in the cell stage —
the reason "many winners" is bounded and the pools eventually close. Type/timing/occurrence are
**derived per planet** from habitability params (`PlanetaryParameters.md` §4). The ocean visibly
degrades toward the event (chemistry/temperature shift, food collapse), telegraphing urgency.
Colonies that graduate before it escape to the creature world; the rest are wiped (lineage persists).

## 7. Hooks into systems already built
- `PlanetaryEnvironment` + `BiomeLibrary` — climate→biome blend already drives water color, murk,
  caustics, particle/organic density. Diet availability & danger should read from the same biome data.
- `FlowFieldManager` — currents already push the cell; becomes traversal risk/reward.
- Soft-body cell (`JellyMesh`/`MembraneRender`/`VolumePreservation`) — the avatar parts attach to.

## 8. MVP slice to build first
**Eat → grow → diet** on top of the existing cell + biome ocean:
1. Edible entities (plant motes vs. meat chunks) + ingestion + size/energy growth.
2. Predation between cells (size-gated) + death/respawn.
3. One active part (flagella dash) to prove the "active ability" feel.
Then layer colony formation, which feeds the whole graduation system.
