# Cell Stage — Metabolism & Genome Plan (S1 → S2)

Overrides the single `Genome.metabolism` model in older docs. Code: `Genetics/Pathways.cs`, `Stage0/S1Life.cs`, genome panel in `Stage0Replication.GenomePanel`.

## Core idea
Every cell needs **energy**. Real microbes get it many ways; each way is a small set of **pathway genes**. A cell carries a few (gene slots), splits its protein budget between them by **priority** (the player regulates expression), and pays **upkeep** for every gene whether it earns or not.
- **Generalists** earn a little almost anywhere.
- **Specialists** earn a lot in one place and starve elsewhere.
- **Location matters:** where you live decides what pays.

## Pathways (built)
| Pathway | Source | Yield | Upkeep | Where |
|---|---|---|---|---|
| Fermentation | organic matter (food particles + dissolved) | low | low | everywhere |
| Rhodopsin pump | light | modest | low | shallows, daytime |
| Anoxygenic photosynthesis | light **and** an electron donor (H₂S / Fe²⁺) | high | high | shallows near vents/seeps |
| Hydrogenotrophy (chemosynthesis) | H₂ + CO₂ | highest | medium | vents |
| Sulfur oxidation | sulfide | high | medium | vent plumes |
| Iron oxidation | dissolved Fe²⁺ | low-medium | medium | iron seeps |
| Lysis enzymes | other cells | (predation) | medium | digest protocells faster; smaller living cells at all |

Pigments show on the cell (rhodopsin purple, bacteriochlorophyll red-purple, iron rust, sulfur pale yellow), weighted by expression share.

## How genes are gained (never from a menu)
1. **Duplication + divergence at fission.** A copied gene can drift into a *neighbouring* pathway. The chance rises steeply with the lineage's exposure to that pathway's source. Living by the vents makes chemosynthesis likely.
   - Neighbour map: Fermentation ↔ Lysis / Hydrogenotrophy; Rhodopsin ↔ Anoxygenic; Anoxygenic ↔ Sulfur ox.; Hydrogenotrophy ↔ Sulfur ox.; Sulfur ox. ↔ Iron ox.
2. **Transformation (horizontal gene transfer).** Dead living cells spill DNA fragments that drift. Swimming over one takes it up: into a free slot, or the panel offers a swap.
3. **Predation.** With Lysis, digesting a living cell spills its genes right where you are.

**Gene slots:** start at 3, max 6; a division sometimes lengthens the genome (+1). Copying cost at fission grows with gene count.

**Player controls (TAB):**
- priority sliders
- silence a gene (save upkeep)
- swap offers
- "untapped here" hints: what would pay at this spot that you can't use

## Starting state
- **The player** starts S1 with Fermentation only. The goal is to find and earn better pathways.
- **AI cells** graduate with the pathway that pays best where they are (75%), sometimes rhodopsin or lysis, so pools grow populations of vent-dwellers, phototrophs and predators whose DNA is worth taking.

## Phase 2: leaving the pool (next)
Tide-pool vents are weak (warm springs). Real chemosynthesis country is the **deep vent field** beyond the pool.
- **Escape channels:** fissures / outflow channels at the pool's deep edge. Only living (S1+) cells survive the passage: strong flow, chemical shock. S0 never needs to deal with the shore's clay beds once it can leave.
- **The passage:** a short current-run transition, then a new, larger zone with its own floor, flow and gradient fields.
- **Zones:**
  - Deep vent field: strong H₂ and sulfide, hot cores, no light.
  - Open shallows: strong light, few organics, UV.
  - Iron sediment: steady Fe²⁺, dim.
  - Anoxic mud: organics and fermentation; predators.
- **Scale:** the camera pulls out (cells bigger relative to the view) and the old pool becomes a backdrop.
- **Return:** zones link back, so lineages can spread between them; species distribution feeds S2.

## Phase 3+
- **Oxygenic photosynthesis** (cyanobacteria) unlocks from Anoxygenic in S2 → feeds the GOE tracker (`Stage2/GoeTracker`). Oxygen then poisons cells without oxygen-tolerance genes.
- **Aerobic respiration** becomes possible after the GOE.
- **Chloroplasts / mitochondria** are *endosymbionts* (S3/S4 eukaryote stage): you engulf a bacterium of a lineage that exists on your planet (possibly one you played) and keep it. Your S1/S2 history decides what's available.
- **Other S1/S2 systems:** biofilms / mats with relatives, phage + CRISPR-like immunity, spores / dormancy, competition between lineages for zones.

## S1 milestones (built)
1. **Lineage**: the first division of a living player cell. From then on, death respawns you as a living descendant carrying your last genome; you never fall back to S0.
2. **The Ring** (FtsZ): S1 starts with *passive* division. The membrane outgrows itself and tears: unequal halves, and the smaller may lose genes (30% each, never the first). Each clumsy split makes the ring likelier (12% + 10% per split). With the ring, the player divides with the controlled fission minigame: equal halves, genes kept.
3. **DNA**: RNA genomes cap at 3 gene slots and lose genes to UV.
   - **Getting the RT gene:** a reverse-transcriptase (RT) gene arrives from a virus. Viruses drift into S1 pools; a living cell digests an infecting virus in about 8 s, and keeps its RT gene 50% of the time. Rarely it arises on its own (8% per division after generation 8).
   - **Conversion:** with RT, each division advances the RNA→DNA conversion (3 divisions). The genome visibly becomes a double strand.
   - **Payoff:** DNA raises the slot cap to 6 and ends UV damage. When the player's genome is DNA, that's LUCA → S2.

## Visible genome (built; replaces the TAB panel, which is now F2 dev-only)
- **Bays:** the genome loop carries gene bays. A filled bay is a coloured knot, which pulses while it earns; an empty bay is a pale pocket.
- **Proteins:** each gene's proteins sit in the membrane as coloured specks, bright when their source is present.
- **Drag a knot:** away from the loop → more priority (bigger); toward it → less; out through the membrane → cut the gene out (it drifts off as a scrap).
- **Free RNA scraps** are coloured like their gene. They snap into an empty bay on contact, or bounce off a full genome.
- **Lost genes:** a gene unused for about 150 s, or broken by UV, frays out of the cell as a scrap.
