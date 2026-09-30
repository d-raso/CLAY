# CLAY — Cell Stage Build Plan

> **Purpose:** the implementation roadmap for fully building the cell stage. This is the
> "what do we build next" tracker. Design rationale lives in `CellStage.md`,
> `Architecture.md`, and `PlanetaryParameters.md` — this doc is about *order of work*.
>
> **How to use:** phases are roughly sequential and de-risk in this order: prove the
> single-shard gameplay loop is *fun* first, then layer networking, then MMO scaffolding.
> Check boxes as tasks land. Each phase has a **Definition of Done (DoD)** — don't advance
> until it's met. Sizes: **S** ≈ hours, **M** ≈ a few sessions, **L** ≈ multi-week.

## Sub-stage content vs. build phases (read this)
The phases below are about **systems** (eat, grow, predate, parts, colony). The **content**
those systems serve is the evolutionary sub-stage arc in `CellStage_SubStages.md` (S0 Protocell
→ S1 Replicator → S2 Prokaryote → S3 Endosymbiosis → S4 Eukaryote FFA → S5 Colony). Important
ordering consequence: **predation/engulfing (Phase 2) is gated to S4** — it requires the
cytoskeleton (endosymbiosis). The early "eat → grow" loop (Phase 1) is **osmotrophic** (absorbing
free molecules, S0–S2), NOT eating other cells. Build Phase 1 as molecular gathering; reskin/gate
the agar.io predation as the S4 unlock.

## Networking strategy (read first — it shapes everything)
The cell stage is ultimately an MMO, but we validate the loop **locally first**. To avoid a
painful retrofit, build gameplay with an **authoritative-simulation split from the start**:
keep game logic (movement, eating, growth, combat) free of direct `Input`/UI calls so it can
later be driven by a server tick instead of local input. Phases 1–6 run "offline" (single
client = local authority). Phase 8 swaps local authority for a networked server with **no
gameplay rewrite** if we hold this discipline. Decision point in Phase 8: which netcode stack.

---

## Phase 0 — Foundations & environment  🟡 ~40%
Core systems are *in place* but the environment still needs significant art/feel iteration
before it's "done." The boxes below mark systems that exist and function — not that they're
polished.
- [x] Soft-body cell avatar (`JellyMesh`, `MembraneRender`, `VolumePreservation`, `JellyBodyBuilder`) — *functional*
- [x] Player movement + flow response (`JellyMovement`) — *functional*
- [x] Biome-driven ocean (`PlanetaryEnvironment` + `BiomeLibrary`, `WorldBackground`/`WaterCaustics` shaders) — *functional, needs art polish*
- [x] Dynamic currents (`FlowFieldManager`) — *functional*
- [x] Atmosphere: particles, organic matter, caustics, screen distortion w/ cell mask — *functional, needs art polish*

### Remaining environment work (the other ~60%)
- [ ] Background/biome visual quality pass — color, murkiness layering, blob feel
- [ ] Caustics & lighting refinement
- [ ] Particle & organic-matter variety/believability
- [ ] Legacy environment system cleanup (disable/remove `Environment` children duplicating the new system)
- [ ] *(add specific environment issues here as we identify them)*
- **DoD:** the ocean reads as a believable, beautiful, biome-varied alien tide pool across all 6 biomes — not just functional.

---

## Phase 1 — Edible ecosystem & growth  (the core loop MVP)  **[M]**  🟡 prototype working
Serves the **S4 Eukaryote FFA** stage (detailed design: `CellStage_S4_Eukaryote.md`).
Goal: *eat → grow* feels good. The smallest thing that proves the game.
- [x] `EdibleEntity` + `FoodSpawner` — pooled self-spawning food motes (matter + biomass). **[done]**
- [x] Ingestion via `Engulfment` overlap → consume → grows the soft body (`CellBiomass`). **[done]**
- [x] `CellResources` — matter pool gathered from food (building blocks for nodes). **[done]**
- [ ] `BiomeFoodSpawner` — spawn rates/types from biome `foodDensity` + diet availability. **[S]**
- [ ] `CellMetabolism` — energy income/upkeep; starvation. **[M]**
- [ ] `DietProfile` (herbivore/carnivore/omnivore). **[S]**
- [ ] Minimal HUD: size/matter/potential readout (currently console + inspector only). **[S]**
- **DoD:** a player can roam, eat, visibly grow, and starve if they don't eat. *(growth + eating done; metabolism/diet/HUD remain)*
- ⚠ **Balance TODO:** engulfing AI cells yields big biomass → player grows explosively & pops constantly.
  Tune `Engulfment.cellBiomassYield`, food values, AI mass range. Camera auto-zoom (added) keeps it viewable.

## Phase 2 — Predation, death, respawn (single shard)  **[M]**  🟡 prototype working
- [x] Size-gated predation via `EngulfMath` (size × rigidity); shared by player + AI (`ICell`). **[done]**
- [x] AI cells: `SimpleCell` + `AIController` (flee/hunt/graze/wander) + `AICellSpawner`. **[done]**
- [x] Death → respawn small (player `Consumed`/pop resets to mass 1; AI destroyed & respawned). **[done]**
- [ ] Drops meat chunks on death (currently biomass transfers directly to engulfer). **[S]**
- [ ] `CellHealth`/damage model (or keep pure size-engulf — decide). **[S]**
- [ ] Threat readability pass (silhouette/color cues). **[S]**
- **DoD:** the "you are food and predator" tension exists vs. AI cells. *(core works; needs balance + chunks)*

## Phase 3 (partial) — Behaviour tracking & the node/flagellum slice  🟡 prototype working
First taste of the evolution model (`CellStage_Evolution.md`):
- [x] `BehaviorObserver` — tracks locomotion/currents/predators/escapes → flagellum potential; logs outcomes. **[done]**
- [x] `EvolutionNodes` — spend matter to grow blank nodes (key N); develop a node into a **flagellum**
      once potential unlocks (key F); flagellum gives speed boost + dash (Space). **[done]**
- [ ] Real evolution UI (replace debug keys); node potential per-node; more organelles. **[L]**

## Phase 3 — Parts & evolution editor  **[L]**  (design: `CellStage_Evolution.md`)
- [ ] `CellPart` system — attach points on the soft-body; parts deform with it. **[M]**
- [ ] Active abilities w/ real-time aim/timing: flagella dash, spike/proboscis, toxin sac, cilia. **[L]**
- [ ] Passive parts: electroreceptors (see through murk), photosynthetic membrane (light→energy). **[M]**
- [ ] Genetic-material currency (banked from eating). **[S]**
- [ ] Editor UI — spend genes on parts/morphology; **async, no world-pause** (vulnerable while editing). **[L]**
- **DoD:** players express builds; a part meaningfully changes how you play.

## Phase 4 — Mini-stage progression  **[M]**
- [ ] Tier model: Microbe → Protist (size thresholds, more part slots, deeper-biome access). **[M]**
- [ ] Gate access to murkier/deeper/harsher biomes by tier. **[S]**
- [ ] Progression feedback (tier-up moment, UI). **[S]**
- **DoD:** the cell stage has a sense of advancement beyond "get bigger."

## Phase 5 — Colony stage (the social hinge)  **[L]**
Depends on Phase 8 for the *multiplayer* version; can prototype with AI first.
- [ ] Define & implement the **colony-formation achievement gate** (currently TBD — pick it). **[M]**
- [ ] Multicellular binding: cells join into one body (data model + soft-body composition). **[L]**
- [ ] Colony identity/membership; recruit colony-less players. **[M]**
- [ ] Colony control model decision: one commander vs. each player = one cell. **[design]**
- [ ] In-colony death → respawn into same colony (rejoin friction) or leave to form/join another. **[M]**
- **DoD:** colonies form emergently and act as the unit that will graduate.

## Phase 6 — Catastrophe & graduation gate  **[M]**
- [ ] `CatastropheController` — type/ETA/occurrence from planet params (`PlanetaryParameters.md` §5). **[M]**
- [ ] Telegraphing: ocean degrades toward the event (temp/chem/food shifts via `PlanetaryEnvironment`). **[M]**
- [ ] Graduation challenge for apex colonies → mark species as "graduated". **[M]**
- [ ] Offline stub for "enter creature world" handoff (real handoff = Phase 9). **[S]**
- **DoD:** a colony can win and escape before a deadline; stragglers are wiped (lineage persists).

## Phase 7 — Planet generation  **[M]**
- [ ] `StarSystemSO`, `PlanetSO` ScriptableObjects (schema in `PlanetaryParameters.md` §1–3). **[M]**
- [ ] `PlanetGenerator` — derive gameplay params (§4) + catastrophe profile (§5); deterministic seed. **[M]**
- [ ] Wire generator output into `PlanetaryEnvironment` + `BiomeFoodSpawner` + `CatastropheController`. **[S]**
- **DoD:** a tide pool's entire character (biomes, food, deadline) is generated from a planet seed.

## Phase 8 — Networking foundation (make a tide pool multiplayer)  **[L]**
- [ ] **Decision:** netcode stack (Unity Netcode for GameObjects / Fish-Net / Mirror / custom). **[design]**
- [ ] Server-authoritative simulation; clients send intent, receive state. **[L]**
- [ ] Replicate cells, food, predation, growth; client-side interpolation/prediction for movement. **[L]**
- [ ] Interest management / AOI (only replicate nearby entities — the ocean is big). **[L]**
- [ ] Port colony formation to real multiplayer. **[M]**
- **DoD:** multiple real players share one tide-pool server and the loop holds under latency.

## Phase 9 — MMO scaffolding (nested servers)  **[L]**
- [ ] Tide-pool server lifecycle: spawn, accept players, close on catastrophe (`Architecture.md` §5). **[L]**
- [ ] Planet shard owns its tide pools; graduation **arrival buffer** + wave/rolling policy. **[L]**
- [ ] Account-level lineage/genome persistence across deaths & stages. **[M]**
- [ ] Matchmaking/routing: new players → open tide pools → fresh planet when full. **[L]**
- [ ] Tune the numbers: players/pool, pools/planet, species/planet (`Architecture.md` §6). **[M]**
- **DoD:** players continuously flow through tide pools; colonies graduate to a planet creature server.

## Phase 10 — Polish, balance, telemetry  **[ongoing]**
- [ ] Economy/balance pass (food density, growth curves, predation fairness, catastrophe ETAs). **[M]**
- [ ] Anti-griefing / power-gap mitigations (size bands, molt vulnerability, spawn protection). **[M]**
- [ ] UX: onboarding/tutorial pool, readability, audio. **[M]**
- [ ] Telemetry to drive balance (session length, death causes, graduation rates). **[M]**
- **DoD:** the cell stage is fun, fair, and instrumented.

---

## Critical path
Phase 1 → 2 → 3 → 4 prove the loop solo. Phase 7 (planet gen) can run in parallel after Phase 1.
Phase 5 (colony) and 6 (catastrophe) need the loop; their *multiplayer* forms need Phase 8.
Phase 8 → 9 turn it into the MMO. Phase 0 environment polish runs **in parallel** with early
gameplay phases — it doesn't block them. **Current focus: finish Phase 0 environment, then Phase 1.**

## Decision log (resolve as we reach them)
- [ ] Colony-formation achievement gate (Phase 5)
- [ ] Colony control model: commander vs. one-cell-per-player (Phase 5)
- [ ] Netcode stack (Phase 8)
- [ ] Graduation arrival policy: rolling vs. wave, per-planet (Phase 9)
- [ ] Health/damage vs. pure size-engulf predation (Phase 2)
