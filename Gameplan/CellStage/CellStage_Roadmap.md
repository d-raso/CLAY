# Cell Stage Development Roadmap

## Current State (Foundation Complete)

- [x] JellyBody physics skeleton (SpringJoint2D spoke + rim system)
- [x] Procedural mesh generation (JellyMesh with Catmull-Rom splines)
- [x] Membrane outline rendering (MembraneRender)
- [x] Physics-based movement with drag (JellyMovement)
- [x] Camera follow system
- [x] BioSlime shader (base implementation)
- [x] Background shader (base implementation)

---

## Milestone 1: Core Cell Polish

*Goal: Make the single cell feel complete and satisfying before adding systems*

### 1.1 Soft Body Stability
- [ ] Add volume preservation to JellyBodyBuilder (prevent collapse on collision)
- [ ] Implement pressure simulation (internal force pushing outward)
- [ ] Tune spring parameters for "squishy but stable" feel
- [ ] Add collision response dampening

### 1.2 Shader Integration
- [ ] Create `CellVisuals.cs` component to sync C# → Shader
- [ ] Pass `_Radius` to BioSlime shader (normalized transparency)
- [ ] Pass `_Mass` for visual density changes
- [ ] Implement growth/shrink visual feedback

### 1.3 Procedural Background
- [ ] Finish Seabed parallax shader with multiple depth layers
- [ ] Add subtle animated caustics
- [ ] Implement depth-based color grading

---

## Milestone 2: Cell Lifecycle

*Goal: Cells can eat, grow, and reproduce*

### 2.1 Resource System
- [ ] Create `Resource.cs` base class (Glucose, Iron, Lipids, ACGT bases)
- [ ] Implement Compound Clouds (fog volumes with resource types)
- [ ] Add absorption mechanic (overlap detection → resource gain)
- [ ] Create visual feedback for absorption

### 2.2 Growth System
- [ ] Create `CellStats.cs` (mass, energy, health)
- [ ] Implement mass gain from resources
- [ ] Scale JellyBody radius based on mass
- [ ] Add/remove membrane nodes dynamically during growth

### 2.3 Mitosis
- [ ] Create `CellMitosis.cs`
- [ ] Implement "Pop and Push" splitting animation
- [ ] Divide mass between parent and daughter
- [ ] Spawn daughter cell with AI controller
- [ ] Add cooldown/energy cost

---

## Milestone 3: Phase 1 - Abiogenesis

*Goal: Playable proto-life tutorial phase*

### 3.1 Protobiont Mode
- [ ] Create `Protobiont.cs` (simplified JellyBody, no nucleus)
- [ ] Implement fragile membrane (lower spring stiffness)
- [ ] Add surface tension health mechanic
- [ ] Create "pop" and "collapse" death states

### 3.2 DNA Synthesis
- [ ] Track ACGT collection (Adenine, Cytosine, Guanine, Thymine)
- [ ] Create synthesis UI showing required ratios
- [ ] Implement "gene lock" trigger
- [ ] Transition cutscene: Protobiont → Cell

### 3.3 Abiogenesis Environment
- [ ] Primordial soup biome with amino acid clouds
- [ ] Gentle tutorial currents
- [ ] Safe zone boundaries

---

## Milestone 4: Phase 2 - Cellular Era

*Goal: Core survival gameplay loop*

### 4.1 Organelle System
- [ ] Create `Organelle.cs` base class
- [ ] Define attachment points on JellyBody skeleton nodes
- [ ] Implement Flagellum (thrust boost)
- [ ] Implement Mitochondria (energy efficiency)
- [ ] Implement Cilia (passive movement/sensing)
- [ ] Create organelle attachment/detachment mechanics
- [ ] Visual integration (organelles visible in mesh)

### 4.2 Combat & Predation
- [ ] Implement cell-to-cell collision damage
- [ ] Size-based predation (bigger eats smaller)
- [ ] Create `Spike.cs` organelle (offensive)
- [ ] Create `Shell.cs` organelle (defensive)
- [ ] Death and respawn system

### 4.3 AI Cells
- [ ] Create `CellAI.cs` behavior tree
- [ ] Wander state
- [ ] Seek food state
- [ ] Flee predator state
- [ ] Hunt prey state
- [ ] Populate world with NPC cells

### 4.4 Evolution/Mutation
- [ ] Create `Genome.cs` (trait storage)
- [ ] Mutation points earned from survival/eating
- [ ] Mutation menu UI
- [ ] Trait unlocks (speed, size, organelle slots)

---

## Milestone 5: World Generation

*Goal: Procedural environment worth exploring*

*Reference: `Gameplan/clay_procedural_biome_generation.md` for full technical spec*

### 5.1 Parallax Layer System
- [ ] Implement `ParallaxLayer.cs` with 6 layers (0.1x to 1.5x movement)
- [ ] Configure blur/brightness per layer
- [ ] Infinite horizontal scrolling
- [ ] `BiomeLayerManager.cs` for layer configuration

### 5.2 Chunk System
- [ ] Implement `WorldGenerator.cs` with chunk loading/unloading
- [ ] Chunk size ~50 units, load radius 3-4 chunks
- [ ] Noise-based biome selection (depth + temperature)
- [ ] Biome blend zones at transitions

### 5.3 Single Biome Polish (Tide Pool first)
- [ ] Create `BiomeDefinition` ScriptableObject structure
- [ ] Terrain prefabs (Sand, Pebbles, Algae, SeaGrass)
- [ ] Particle systems (Debris, Plankton, Bubbles, LightShafts)
- [ ] Post-processing profile (color grading, vignette, bloom)
- [ ] Caustics effect for surface lighting

### 5.4 Additional Biomes
- [ ] **Clay Bed** - Earthy, safe zones, diffuse light
- [ ] **Hydrothermal Vent** - Volcanic lighting, heat/acid hazards
- [ ] **Deep Ocean** - Bioluminescent, near-black, sparse
- [ ] **Bacterial Mat** - Purple/green/gold, spores, slime
- [ ] Terrain generation with Poisson disk sampling per biome

### 5.5 Biome Transitions
- [ ] `BiomeTransitionManager.cs` for smooth crossfades
- [ ] Interpolate lighting, particles, audio, post-processing
- [ ] Blend terrain colors in transition zones

### 5.6 Flow Fields
- [ ] Create static vector field textures for currents
- [ ] Apply current forces to all Rigidbody2D objects
- [ ] Jet streams (fast travel corridors)
- [ ] Eddies (circular traps)
- [ ] Vent updrafts

### 5.7 Environmental Hazards
- [ ] Temperature damage zones
- [ ] Toxic compound clouds (resource fog volumes)
- [ ] Predator spawn zones
- [ ] Biome-specific hazard configs

---

## Milestone 6: Phase 3 - Colony & Multicellularity

*Goal: Cooperative organism building*

### 6.1 Cadherin Binding
- [ ] Create `Cadherin.cs` (cell adhesion component)
- [ ] Implement sticky collision (FixedJoint2D on contact)
- [ ] Bind to other players OR own daughter cells
- [ ] Detachment mechanics

### 6.2 Cell Specialization
- [ ] **Epidermis** - armor cell type (high defense, no movement)
- [ ] **Myocyte** - muscle cell (thrust contributor to colony)
- [ ] **Cnidocyte** - weapon cell (stinger/spike attacks)
- [ ] **Photocyte** - energy cell (passive resource generation)
- [ ] Specialization locks out other paths

### 6.3 Colony Physics
- [ ] Compound mass calculation
- [ ] Distributed thrust from Myocytes
- [ ] Colony-wide damage distribution
- [ ] Formation shape affects hydrodynamics

---

## Milestone 7: MMO Systems

*Goal: Persistent multiplayer world*

### 7.1 Networking Foundation
- [ ] Choose networking solution (Netcode for GameObjects / Mirror / custom)
- [ ] Server-authoritative cell physics
- [ ] Client prediction for responsive feel
- [ ] Interest management (only sync nearby cells)

### 7.2 Clade System
- [ ] Assign `CladeID` on spawn
- [ ] Visual differentiation (color tinting per clade)
- [ ] No friendly fire within clade
- [ ] Gene sharing: unlocks reduce cost for clade members

### 7.3 Server Cycle
- [ ] 14-day server timer
- [ ] Phase progression triggers (Day 1-3: Abiogenesis, Day 4-10: Cellular, Day 11-14: Filter)
- [ ] Great Filter events:
  - [ ] Oxygenation Crisis (oxygen-dependent cells survive)
  - [ ] Ice Age (cold zones expand)
  - [ ] Meteor Impact (destruction wave)
- [ ] End-of-cycle scoring and persistence to next stage

### 7.4 Persistence
- [ ] Player genome saves across sessions
- [ ] Colony membership persistence
- [ ] Server state snapshots
- [ ] Reconnection handling

---

## Development Order Recommendation

**Phase A: Single-Player Vertical Slice**
1. Milestone 1 (Cell Polish)
2. Milestone 2 (Lifecycle)
3. Milestone 4.1-4.2 (Organelles + Combat)
4. Milestone 4.3 (AI Cells)

**Phase B: World Building**
5. Milestone 5 (World Gen)
6. Milestone 3 (Abiogenesis as tutorial)
7. Milestone 4.4 (Evolution)

**Phase C: Multiplayer**
8. Milestone 7.1-7.2 (Networking + Clades)
9. Milestone 6 (Colony)
10. Milestone 7.3-7.4 (Server Cycle + Persistence)

---

## Immediate Next Steps

From CellStage.md priorities:
1. **Volume preservation** in JellyBodyBuilder
2. **Shader property sync** (C# → `_Radius`, `_Mass`)
3. **Parallax layer system** (see biome doc for 6-layer setup)
4. **CellMitosis.cs** implementation

Then: Polish one biome (Tide Pool) end-to-end before expanding.
