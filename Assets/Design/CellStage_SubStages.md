# CLAY — Cell Stage Sub-Stages (the science-to-gameplay map)

> Deep map of the cell stage's evolutionary sub-stages, grounded in origin-of-life and early-
> evolution science. The throughline: **each major biological innovation unlocks a different
> KIND of gameplay.** The most important is the cytoskeleton — phagocytosis (engulfing other
> cells, i.e. agar.io) is *strictly* cytoskeleton-dependent, so it cannot exist early. This
> doc supersedes the simpler 3-tier sketch in `CellStage.md` §2.
>
> Research sources at the bottom. Design intentionally leans on real theory but bends it where
> fun demands; notes call out where.

## Progression at a glance

| # | Sub-stage | Science | Gameplay mode | Unlock gate |
|---|-----------|---------|---------------|-------------|
| S0 | **Protocell** | Abiogenesis, lipid vesicles, prebiotic chemistry | Solo survival / resource gathering | self-replicating polymer |
| S1 | **Replicator** | RNA world (genes-first) | Solo + first evolution choices | self-sustaining cell (LUCA) |
| S2 | **Prokaryote** | Bacteria/archaea, metabolism, HGT | Competitive efficiency, no engulfing | endosymbiosis (needs proto-cytoskeleton) |
| S3 | **Endosymbiosis** | Margulis theory, cytoskeleton, energy-per-gene | Pivotal transformation (branching) | becomes eukaryote |
| S4 | **Eukaryote FFA** | Phagocytosis, large cells, predation | **agar.io predator/prey + active parts** | apex + colony achievement |
| S5 | **Colony** | Volvox, choanoflagellates, multicellularity | Co-op / clonal body, graduation | graduate → creature stage |

The deadline catastrophe (`PlanetaryParameters.md` §5) can fire at any sub-stage; the famous
real one — the **Great Oxidation Event** — is a *player-driven* catastrophe that emerges at S2.

---

## S0 — Protocell (Molecular phase)  · *abiogenesis*
**Science.** Life's precursors: monomers (amino acids, nucleotides, fatty acids, sugars) formed
by prebiotic chemistry (Miller–Urey), delivered by meteorites, or cooked at hydrothermal vents.
Fatty acids spontaneously self-assemble into **lipid vesicles** that can grow and divide
(Szostak). **Wet–dry tidal cycling** in pools concentrates monomers on mineral surfaces — which
is *literally our tide-pool setting*. Two camps: genes-first (RNA) vs metabolism-first
(coacervates/proton gradients at alkaline vents, Lane/Russell); modern trend is hybrid.

**Fantasy.** You are a fragile lipid bubble adrift. Absorb free-floating monomers, grow your
membrane, divide when full.

**Core mechanic.** Resource gathering + **membrane-integrity survival** (no predation):
- Hazards: UV in clear shallows, hydrolysis, pH/temperature swings (read from biome chemistry).
- Wet–dry tension: low tide concentrates food but exposes you to UV/desiccation.
- Reproduce by fission once you bank enough material.

**Gate → S1.** Encapsulate a self-replicating polymer.
**Alien generalization.** `liquidType` (water/ammonia/methane) sets which monomers exist and
which hazards bite (e.g., methane worlds: cold-stable, different solvent chemistry).

## S1 — Replicator (Genetic phase)  · *RNA world* · still solo
**Science.** The **RNA world**: short RNAs act as both genetic information and catalyst
(ribozymes), self-replicating before DNA/protein existed. Real tension we can gamify: the
chemistry that stabilizes fatty-acid membranes tends to *inhibit* RNA replication — survival vs
reproduction pull against each other. Possible pre-RNA polymers (PNA/TNA) = flavor.

**Fantasy.** You now carry a self-copying molecule — a genome. Feed it monomers to replicate;
**mutations are your first evolution currency.**

**Core mechanic.** Osmotrophic gathering continues; introduce the **first editor (passive traits
only)**: tougher membrane, basic metabolism, faster copy. **Fidelity vs speed** tradeoff — copy
fast for growth but accumulate risky mutations. Competition is indirect (you deplete shared
monomers), still **no engulfing**.

**Gate → S2.** Integrate genome + metabolism + membrane into a self-sustaining cell (a LUCA-like
"first true cell").

## S2 — Prokaryote (Metabolic phase)  · *bacteria & archaea* · competitive, NO engulfing
**Science.** The prokaryotic world: metabolic diversity — **phototrophy** (light), **chemotrophy/
chemosynthesis** (vents, sulfur/methane), **lithotrophy** (rock). Reproduction by binary fission.
**Horizontal gene transfer** lets bacteria swap genes laterally. **Biofilms** = early cooperation.
Oxygenic photosynthesis (cyanobacteria) eventually triggers the **Great Oxidation Event**.

**Fantasy.** Pick a metabolism matched to your biome and out-compete by efficiency, not violence.

**Core mechanic.** Still **no phagocytosis** (key constraint). Instead:
- **Metabolism choice** gated by biome (photo in sunlit shallows, chemo at iron seeps/vents).
- **Horizontal gene transfer** — borrow traits from nearby cells (a great low-stakes MMO social
  mechanic; precursor to colonies).
- **Biofilms** — clump for defense/efficiency (proto-cooperation).
- **Player-driven catastrophe:** if enough players run oxygenic photosynthesis, oxygen builds up
  → **Great Oxidation Event**, toxic to anaerobes (the first mass extinction). Emergent, dramatic.

**Gate → S3.** Evolve a primitive cytoskeleton (first engulf) and undergo endosymbiosis.
**Alien generalization.** Available metabolisms come from planet energy/chemistry: geothermal
worlds favor chemosynthesis; bright worlds favor phototrophy.

## S3 — Endosymbiosis Event (the power gate)  · *Margulis* · pivotal, branching
**Science.** **Endosymbiotic theory**: a host (an Asgard-type archaeon that had evolved an
actin/tubulin **cytoskeleton** capable of *primitive phagocytosis*) engulfed a bacterium and,
instead of digesting it, kept it as a permanent **internal power plant** — the mitochondrion
(from an alphaproteobacterium) or, separately, the chloroplast (from a cyanobacterium). This
**energy-per-gene** leap (Lane) is what made large, complex eukaryotic cells possible. *(Camps
differ on whether mitochondria came before or after full complexity; we use the dramatic ordering:
proto-engulf → capture symbiont → energy unlock → full eukaryote.)*

**Fantasy.** You've evolved your first crude engulf. You swallow a microbe — and keep it. It
becomes your engine, and the choice defines your build.

**Core mechanic.** A **one-time transformative acquisition** that massively raises your energy
budget and unlocks the eukaryote tier (size, full cytoskeleton, true phagocytosis). The branch
you take is a lasting identity choice:

| Symbiont branch | Real analog | Favored on planets with |
|-----------------|-------------|--------------------------|
| Mitochondrion (aerobic) | alphaproteobacterium | oxygen (post-GOE), balanced worlds |
| Chloroplast / photosymbiont | cyanobacterium | high insolation / light |
| Chemosymbiont | vent tubeworm/mussel symbioses | volcanism, sulfur/methane vents |
| Radiosymbiont (melanin) | radiotrophic fungi | high stellar flare / radiation |
| Thermosymbiont | (speculative) | strong thermal gradients |

**Gate → S4.** You are now a eukaryote — enter the free-for-all.

## S4 — Eukaryote FFA (the agar.io phase)  · *phagocytosis* · the skill core
**Science.** Full cytoskeleton enables **phagocytosis** (engulfing other cells), large cell size,
a nucleus, endomembrane trafficking, and active predation — the predator/prey arms race begins.
This is the *first point at which eating other cells is biologically possible.*

**Fantasy.** Classic agar.io predator/prey, fused with Spore's **parts as active abilities**.

**Core mechanic.** Size-gated engulfing; active parts (flagella dash, spike/proboscis, toxin sac,
cilia, electroreceptors, photosynthetic membrane); biome traversal risk/reward via currents.
*(This is the gameplay in `CellStage_BuildPlan.md` Phases 1–4 — but it belongs HERE, gated behind
the cytoskeleton, not at the very start.)*

**Gate → S5.** Reach apex + clear the colony-formation achievement gate.

## S5 — Colony (Multicellularity)  · *Volvox / choanoflagellates* · social + graduation
**Science.** Multicellularity arose many times. Models: **Volvox** (clonal green algae) and
**choanoflagellates** (closest living relatives of animals). Two routes: **clonal** (one cell
divides and stays together) vs **aggregative** (independent cells assemble). A 2026 *Nature* study
found a choanoflagellate (*Choanoeca flexa*) that does **either, switched by salinity**, in
ephemeral splash pools undergoing evaporation/refill — environment decides the mode. Cells then
**differentiate** (division of labor).

**Fantasy.** Cells bind into a multicellular body — the unit that graduates to the creature world.

**Core mechanic.** Two colony modes, and **the environment can bias which is favored** (diegetic
answer to the open "commander vs one-cell-per-player" question in `CellStage.md` §5):
- **Clonal** — you grow your own body by division → *solo commander* of a colony.
- **Aggregative** — independent players assemble into one body → *MMO co-op colony*.
- Salinity/biome could push toward one mode → different biomes breed different colony styles.

Then: **cell differentiation** into roles (motility, defense, feeding, reproduction), coordinated
control, and the **graduation challenge** — escape to the creature world before the catastrophe.

**Gate → Creature stage.** Beach / proto-organism handoff (`Architecture.md` §2).

---

## Design payoffs this map unlocks
- **agar.io is correctly gated** behind the cytoskeleton — the early game is a genuinely different
  (calmer, builder/survival) experience, which makes the predation phase land harder.
- **The endosymbiosis branch ties build identity to the planet** — a vent world breeds
  chemosymbionts, a bright world breeds photosynthesizers. Replayability per planet.
- **Two real catastrophe sources**: planet-rolled (PlanetaryParameters §5) AND player-driven (GOE
  at S2) — emergent, narrative.
- **Colony mode is environmental** — clonal vs aggregative tied to salinity/biome resolves an open
  design question and reuses the `salinity` param we already model.

## Open questions carried forward
- Exact **colony-formation achievement gate** (still TBD) — candidate: complete S3 endosymbiosis.
- How long/elaborate are S0–S2? Risk: a long pre-agar.io ramp could bore. Tune to be short,
  tense vignettes, not grinds.
- Does HGT (S2) persist as a mechanic into later stages?

## Sources
- [Origin of life — RNA world vs metabolism-first (wikidoc)](https://www.wikidoc.org/index.php/Origin_of_life)
- [Protocells & non-enzymatic RNA synthesis (iBiology / Szostak)](https://www.ibiology.org/sessions/session-1-origins-life-protocells-non-enzymatic-template-directed-rna-synthesis/)
- [RNA world & ribozymes (Chemistry World)](https://www.chemistryworld.com/features/how-rna-reveals-clues-to-lifes-origins-on-earth/4022833.article)
- [The origins of phagocytosis and eukaryogenesis (PMC)](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC2651865/)
- [Lynn Margulis and the endosymbiont hypothesis, 50 years later (MBoC)](https://www.molbiolcell.org/doi/10.1091/mbc.e16-07-0509)
- [Endosymbiotic theories for eukaryote origin (Phil. Trans. R. Soc. B)](https://royalsocietypublishing.org/doi/10.1098/rstb.2014.0330)
- [Clonal-aggregative multicellularity tuned by salinity in a choanoflagellate (Nature 2026)](https://www.nature.com/articles/s41586-026-10137-y)
- [Volvox, Chlamydomonas & the evolution of multicellularity (Nature Scitable)](https://www.nature.com/scitable/topicpage/volvox-chlamydomonas-and-the-evolution-of-multicellularity-14433403/)
- [The Great Oxidation Event (ASM)](https://asm.org/articles/2022/february/the-great-oxidation-event-how-cyanobacteria-change)
- [Hypothetical types of biochemistry (Wikipedia)](https://en.wikipedia.org/wiki/Hypothetical_types_of_biochemistry)
- [Evaluating alternatives to water as solvents for life (PMC)](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC8145300/)
