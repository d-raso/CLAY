# Project Clay: Technical & Design Overview

**Working Title:** Clay
**Genre:** Massively Multiplayer Evolutionary Simulation
**Engine:** Unity 6 (URP)
**Architecture:** DOTS/ECS (Hybrid)
**Vision:** A seamless, scientifically-grounded yet accessible journey from the first assembly of lipids to complex multicellular civilizations. The game emphasizes tactile physics ("Clay"), procedural ecosystems, and large-scale cooperative survival.

---

## Part I: The Game Loop & Progression Stages

The "Cell Stage" is not a single mode. It is a three-act structure spanning roughly 2 weeks of real-time server persistence.

### Phase 1: The Spark (Abiogenesis & The Lipid Layer)
**Concept:** Before there was life, there was chemistry. The player does not control a cell yet; they control a drifting **Protobiont** (a primitive bubble of fat).
* **Goal:** Stabilize the membrane and synthesize the first strand of DNA.
* **Mechanics:**
    * **The Bilipid Layer:** The player is a fragile ring of hydrophobic lipids. Physics-based currents threaten to tear the ring apart.
    * **Amino Acid Collection:** The player must passively drift into clouds of amino acids.
    * **Surface Tension:** A unique "health" mechanic. If the bubble expands too fast without enough lipids, it pops. If it shrinks too much, it collapses.
    * **Synthesis:** Upon collecting the correct ratio of Adenine, Cytosine, Guanine, and Thymine (ACGT), the player "locks" their first gene, triggering the transition to Phase 2.

### Phase 2: The Cellular Era (The "Agar.io" Stage)
**Concept:** The core gameplay loop. A physics-driven survival game where players control a single cell, hunt, evolve, and compete.
* **Control Style:** "Heavy Arcade." Physics-based movement (add force) with drag, distinct from the twitchy movement of *Spore* or the sluggishness of *Thrive*.
* **Key Mechanics:**
    * **Soft Body Physics:** The cell is a mass-spring system (JellyBody). It squishes against rocks and deforms when squeezing through gaps.
    * **The "Clay" Aesthetic:** The cell isn't just a sprite; it's a 3D-generated mesh with a "Bioslime" shader that looks tactile, semi-transparent, and wet.
    * **Organelle Slots:** Players don't just "upgrade stats." They physically attach organelles (Mitochondria, Flagella, Cilia) to their internal skeleton nodes.
    * **Mitosis:** A manual reproduction mechanic. Players split their mass to create a "Daughter Cell" (AI controlled) or to respawn.

### Phase 3: The Assembly (Colony & Multicellularity)
**Concept:** The transition from individual survival to collective engineering.
* **The Binding Agent:** Players unlock "Cadherin" (cell adhesion proteins). This allows them to stick to other players or their own clones.
* **Specialization:** A colony isn't just a blob.
    * *Player A* evolves into a hard **Epidermis** (Armor).
    * *Player B* evolves into a **Myocyte** (Muscle/Thruster).
    * *Player C* evolves into a **Cnidocyte** (Weapon/Stinger).
* **The Goal:** Build a macroscopic organism capable of surviving the "Great Filter" (an environmental cataclysm that ends the 2-week server cycle).

---

## Part II: Technical Implementation Details

### 1. The "Bioslime" Visuals (Shader Graph)
**Objective:** A procedural material that simulates a 3D cell membrane with variable thickness, transparency, and internal complexity.

* **The "Ghost" Logic (Transparency):**
    * Calculates `Distance(UV)`. Uses `Smoothstep` to define a "Hole" in the center (transparent) and a solid ring at the edge (opaque).
    * **Normalization:** Input distance is divided by `_Radius` (sent from C#) to ensure the gradient stays consistent whether the cell is 1 micron or 100 microns wide.
* **Surface Detail:**
    * **Object Space Noise:** Uses `Position` (not UV) to generate noise. This prevents texture stretching as the soft-body mesh deforms.
    * **Parallax Organelles:** Two layers of noise.
        1.  **Body Layer:** Multiplied by `_BodyOpacity` (Ghostly).
        2.  **Spec Layer:** Masked by `Max()` logic to ensure "floating dots" remain solid even inside the transparent regions.
* **PBR Integration:**
    * **Normal Maps:** Procedural bumps generated via Noise -> `Normal From Height` -> **Fragment Block**.
    * **Transmission:** Simulates light scattering through the jelly volume.

### 2. Physics Architecture (JellyBody)
**Objective:** A soft, squishy feel that reacts to the environment without collapsing.
* **Skeleton:**
    * **Nucleus:** Central `Rigidbody2D`.
    * **Membrane:** Ring of `Rigidbody2D` nodes connected to the Nucleus via `SpringJoint2D` (Spokes) and to neighbors via `SpringJoint2D` (Rim).
* **Mesh Generation:**
    * Procedurally generates a mesh every frame based on the positions of the physics nodes.
    * **Fan Topology:** Center Vertex -> Edge Vertex A -> Edge Vertex B.
* **Collision:** The membrane nodes handle collision with rocks/enemies. The internal pressure is simulated by the stiffness of the "Spoke" springs.

### 3. Environment & World Generation (The "Micro-Atlas")
**Objective:** A full-scale procedural planet capable of hosting 100+ players for weeks.

* **Grid System:** The world is divided into a Voronoi Grid based on **Temperature** and **Depth** noise maps.
* **Biomes:**
    1.  **Stromatolite Reef:** Safe, sticky floors, high oxygen. (Start Zone).
    2.  **Banded Iron Ocean:** Low visibility, heavy iron particulates (Resource rich, visual obstruction).
    3.  **Silica Glass Garden:** Sharp geometric obstacles. Hazardous currents.
    4.  **Alkaline Vents:** White smokers. Upward convection currents. High energy, high danger.
* **Fluid Dynamics:**
    * **Flow Fields:** Static vector textures dictate current direction (Jet Streams vs. Eddies).
    * **Compound Clouds:** Instead of distinct food pellets, resources exist as "Fog Volumes." Being inside a green fog grants Glucose; red fog grants Iron.

---

## Part III: MMO Systems & Network Logic

### 1. The Clade System
To manage 100 players, we do not use "Teams" in the traditional sense. We use **Clades** (Genetic Ancestry).
* **Spawn:** Players spawn as a generic archetype (e.g., "The Red Clade").
* **Divergence:** As players evolve, they look different, but they share a `CladeID`.
* **Rules:**
    * **No Cannibalism:** You cannot damage members of your own Clade.
    * **Gene Sharing:** Unlocking a mutation (e.g., "Spike Protein") makes it cheaper for other members of your Clade to unlock.

### 2. The Server Cycle (The Great Filter)
* **Duration:** ~14 Days.
* **Progression:**
    * **Days 1-3:** Abiogenesis & Single Cell survival.
    * **Days 4-10:** Colony formation & Specialization.
    * **Days 11-14:** The "Filter" Event (e.g., Oxygenation Crisis, Ice Age). Only the most efficient Colonies survive to the next server reset/stage.

---

## Part IV: Mission for the AI Coder

**Current Task Priority:**
1.  **Refine the Soft Body:** Ensure `JellyBodyBuilder` maintains volume (prevents collapse) during high-velocity collisions.
2.  **Shader Integration:** Connect the C# `Radius` and `Mass` variables to the Shader Graph properties `_Radius` and `_Mass` to ensure visual consistency during growth.
3.  **Procedural Background:** Implement the "Seabed" shader with Parallax layers to replace the static background.
4.  **Mitosis Script:** Implement `CellMitosis.cs` using the "Pop and Push" method described in the documentation.

*Refer to the chat logs for specific code snippets regarding the `Smoothstep` implementation in Shader Graph and the `SpringJoint2D` configuration.*
