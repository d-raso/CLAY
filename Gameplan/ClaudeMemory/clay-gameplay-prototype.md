---
name: clay-gameplay-prototype
description: The S4 gameplay vertical-slice scripts (eat/engulf/AI/behaviour/nodes) and how they wire together
metadata: 
  node_type: memory
  type: project
  originSessionId: f2bceb4a-0ee1-44fc-9820-0eaac5494694
---

CLAY's first gameplay slice lives in `Assets/_Scripts/Gameplay/` (+ `CellMechanics/CellBiomass.cs`).
Built for the S4 Eukaryote FFA stage. See [[clay-game-design]] design docs.

**Shared spine:** `ICell` interface (Transform/Mass/Radius/Rigidity/EngulfPower/IsAlive/GainBiomass/
Consumed) + `EngulfMath.CanEngulf` (reach = mass×engulfPower vs resist = mass×rigidity, needs 1.25×).
Implemented by `CellBiomass` (player soft-body) and `SimpleCell` (lightweight AI cells).

**Components on PlayerCell:** CellBiomass (size/rigidity/maxMass/pop — built earlier), CellResources
(matter pool), Engulfment (overlap-eats food + smaller cells), BehaviorObserver (tracks movement/
currents/predators/escapes → flagellaPotential; logs deaths/peakMass), EvolutionNodes (spend matter →
blank node [N key]; develop node → flagellum [F] once potential unlocked; flagellum = speed boost +
dash [Space]). Movement: JellyMovement has `externalSpeedMultiplier` the flagellum sets.

**World systems** on a `GameplaySystems` GameObject: `FoodSpawner` (self-spawning pooled motes,
give matter+biomass, uses `GfxUtil` procedural circle sprite) and `AICellSpawner`. AI cells are now
**full soft-body cells** — clones of the `Assets/_Prefabs/PlayerCell.prefab` (same membrane/bioslime
shader, same `ICell` engulf rules), with player-only scripts (JellyMovement/BehaviorObserver/
EvolutionNodes/CellRippleSystem) stripped on spawn and an `AIController` added. `CellBiomass` has
`destroyOnConsumed` (AI=true → destroyed when eaten; player=false → respawns) + `onConsumed` event.
`AIController` drives any `ICell`. (`SimpleCell.cs` is now unused legacy.)

Follow-ups not yet done: AI cells are all green (no per-instance membrane color tint yet — the user
wanted them colorful/distinct); BehaviorObserver/Engulfment overlap counts membrane-node colliders
so a soft-body cell can be counted multiple times (inflates `predators`); needs distinct-ICell dedup.

`CameraFollow` now has **auto-zoom** (orthographic size scales with the player's `CellBiomass.Radius`)
so the world stays visible at any cell size; scroll wheel = manual offset.

**Known balance gap (next tuning pass):** engulfing AI cells yields big biomass → player grows
explosively and pops (maxMass) every few seconds. Tune `Engulfment.cellBiomassYield`, FoodSpawner
`biomassValue`, and AICellSpawner `massRange`. Components were added fresh in-scene, so changing a
script's default field value needs a component **Reset** to take effect (Unity serialization gotcha,
see [[clay-environment-architecture]]).

Debug keys: `=`/`-` grow/shrink (CellBiomass), `[`/`]` rigidity, `N` node, `F` develop flagellum,
`Space` dash. These are scaffolding until eating/evolution UI replace them.
