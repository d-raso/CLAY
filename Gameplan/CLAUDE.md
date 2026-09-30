# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**CLAY** - A massively multiplayer evolutionary simulation inspired by Spore's cell stage. Players control soft-body physics cells through abiogenesis to multicellular organisms across 2-week server cycles.

**Three Phases:** Abiogenesis (protobiont) → Cellular Era (survival) → Assembly (colony/multicellularity)

## Tech Stack

- Unity 6 (URP 2D)
- New Input System
- Target: DOTS/ECS Hybrid

## Soft-Body Physics System

The cell is a mass-spring system:

```
JellyBodyBuilder → spawns MembraneNode ring → SpringJoint2D connections
                                            ↓
                   JellyMesh (fill) + MembraneRender (outline) via Catmull-Rom splines
```

**Scripts (`Assets/_Scripts/`):**
- `JellyBodyBuilder.cs` - Creates physics nodes with spoke + rim spring joints
- `JellyMesh.cs` - Procedural mesh generation (fan topology)
- `MembraneRender.cs` - LineRenderer outline
- `JellyMovement.cs` - AddForce movement with custom drag
- `CameraFollow.cs` - Smooth camera tracking

## Biome System (Planned)

6-layer parallax with 5 biomes: Tide Pool, Clay Bed, Hydrothermal Vent, Deep Ocean, Bacterial Mat. Chunk-based generation with noise-driven biome selection.

## Current Priorities

1. Maintain soft body volume during collisions
2. Connect C# `Radius`/`Mass` to shader `_Radius`/`_Mass`
3. Procedural seabed background with parallax
4. Implement `CellMitosis.cs`

## Design Documents

| File | Contents |
|------|----------|
| `Gameplan/CellStage.md` | Game design, phases, MMO systems |
| `Gameplan/clay_procedural_biome_generation.md` | Biome system, parallax layers, chunk generation |
| `Gameplan/CellStage_Roadmap.md` | Development milestones and task tracking |

## Key Assets

- `Assets/Materials/Cell/BioSlime.shadergraph` - Cell membrane shader
- `Assets/_Prefabs/MembraneNode.prefab` - Physics node prefab
