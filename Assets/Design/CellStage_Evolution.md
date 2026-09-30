# CLAY — Cell Stage Evolution Plan

> How players change their cell over time. This is the system behind the "parts & editor" work
> (BuildPlan Phase 3) and the trait choices in `CellStage_S4_Eukaryote.md`. Grounded in real
> population genetics and in what shipped evolution games actually do (Thrive, Spore, Niche).
> Sources at the bottom.

## 0. References we're building on (what works elsewhere)
- **Thrive (closest cousin):** a per-session **Mutation Point (MP)** budget; you spend MP to
  add/move/remove **organelles**; *every organelle has upkeep* (ATP/osmoregulation) and **shuts
  down if you can't feed it** — so builds must stay energy-positive. Membrane has a **fluid↔rigid
  slider** and discrete **material types** (lipid, cellulose, chitin, calcium carbonate, silica)
  with tradeoffs. The editor is **gated behind reproduction** (gather resources → reproduce → edit).
- **Spore:** **DNA points** earned by eating; edit any time you have enough; editor entered by
  **reproducing ("call mate")**; a **size limit** gates advancing to the next stage.
- **Niche:** built on the **five pillars of population genetics** — mutation, natural selection,
  gene flow, genetic drift, sexual selection — with real inheritance (dominant/recessive) and
  offspring that mix parents' genes.

We already shipped a piece of this: `CellBiomass` has the **membrane rigidity** trait (maxSize vs
engulf-power tradeoff) — that IS Thrive's fluid/rigid slider. Evolution is the system that lets
players *choose* it and everything like it.

## 1. Pillars
1. **You become how you live** — evolution is driven by your *behaviour and stresses*, not by
   spending an abstract score. (Replaces "eat food → DNA → buy parts," which felt too gamey.)
2. **Develop, don't buy** — you *cultivate* structures from real building blocks and *acquire*
   organelles by action; nothing comes from a store menu.
3. **Expressive & constrained** — many options, but **upkeep costs** stop you stacking everything.
4. **Tied to the world** — diet and habitat decide what you *can* build; an organelle is useless
   where its fuel is absent.
5. **Persistent (MMO)** — your genome is your lineage's identity; it survives death and carries on.

> Surface simplicity is mandatory: the player sees a few resource types and clear feedback
> ("you've been fleeing a lot — a flagellum is developing"). All the ATP/methylation/osmoregulation
> math stays under the hood as flavour, never a spreadsheet.

## 2. The model: Live → Develop → Cement → Inherit  (the core redesign)
Replaces both the "DNA currency" and the "buy organelles" shop. Grounded in **phenotypic
plasticity**, the **Baldwin effect**, **stress-induced mutagenesis**, and **endosymbiosis**.

**① Gather building blocks (not "DNA").** Eating and habitat yield real **compounds**:
amino acids/proteins, lipids (membrane), nucleotides, minerals (silica/calcium = armor), vent
chemicals (sulfur…). *What you eat and where you live* set your material palette — a vent-dweller
and a sunlit grazer can build different things. This replaces the single DNA number.

**② Your playstyle creates adaptive pressure.** The game quietly tracks lifestyle + near-death
**stresses** (flee / engulf / bask in light / survive toxins / where you live). Sustained pressure
makes the *aligned* traits **emerge and cheapen** — flee a lot → a flagellum starts developing;
bask → photosynthesis pressure builds; keep barely surviving acid → acid resistance. Scientifically
this is **trait-environment mismatch driving stress-induced mutation toward solutions**. *You evolve
toward how you survive.*

**③ Develop traits that mature with use (and atrophy without).** Instead of buying a finished part,
you **invest building blocks** into a **nascent structure** that starts **plastic** (weak,
reversible) and **strengthens the more you use it**, eventually **fixing into your genome** (Baldwin
effect) so offspring inherit it. Stop using it → it withers (**use it or lose it**). Tending a
garden, not a checkout.

**④ Acquire organelles by action — never bought.** Real organelles were **engulfed and kept**
(endosymbiosis). So you get a power organelle by **hunting a free-living microbe of that type and
retaining it instead of digesting it** — a dramatic moment, not a menu pick. Genes you **steal from
prey** (HGT). This is the answer to "working towards developing them rather than buying."

**When does this happen?** Continuously while you play (plastic development), with the permanent
**cementing** resolved at **reproduction** — both while thriving (bank enough to spawn an offspring)
and on **death → respawn as offspring** (roguelike inheritance, + a drift mutation). The editor/
review is **async and never pauses the shared world** (`CellStage.md` §4).

## 2b. Nodes — undifferentiated potential (the "what will this become?" hook)
A core mechanic on top of §2: **all evolutions begin as a generic NODE.** You spend matter to grow a
node on your membrane **without knowing what it will become.** As you play, your accumulated
behaviour/stress pressure (`AdaptivePressure`) gives each node **latent potential** toward specific
organelles. Only later — when you evolve — do you **choose** which unlocked potential a node
realises.

Why this is good: it makes evolution feel like **discovery, not shopping.** You commit material to
raw potential, live your life, and your habits *reveal* what that potential could become. Flee
through predator-infested currents for a while and your nodes start glowing with **flagellum**
potential; bask in light and they lean **photosynthetic**.

Flow: `spend matter → blank Node → (play; pressure accrues) → Node shows 1+ organelle potentials →
at reproduction, choose one → Node differentiates → organelle develops (plastic→fixed, §2③)`.

Example (flagellum): `matter gathered + observed locomotion behaviour (fast/sustained movement,
strong currents, nearby predators) → a Node gains flagellum potential → you choose it → flagellum
grows and strengthens with use.`

## 3. The genome (data model — the spine)
A **Genome** = an ordered list of **Genes**. Each Gene = a typed trait with parameters and (for
parts) a placement on the membrane. Everything the cell is, is its genome.

```
Genome
 ├─ Morphology: sizeCeiling(membraneRigidity), membraneMaterial, symmetry
 ├─ Metabolism: organelle branch (mito/chloroplast/chemo…), storage
 ├─ Locomotion (periphery, limited slots): flagellum | cilia | jet
 ├─ Offense:  spike | proboscis | toxin sac …
 ├─ Defense:  armor | retaliation spikes | camo …
 └─ Sensory:  electroreceptors | eyespot …
```

- `GeneDefinition` = ScriptableObject catalog entry (build-cost in compounds, upkeep, tradeoffs,
  prefab/visual, which **adaptive pressure** develops it).
- Each gene carries a **development state** `0→1`: **plastic** (low, reversible, weak) →
  **fixed** (genetic, permanent, inherited). Use raises it; disuse lowers it (atrophy).
- `Genome` = runtime component holding the genes + their development states; on change it
  **re-applies** to the cell (drives `CellBiomass`, spawns/removes parts, sets metabolism).
- **Operations are not "buy":** *Develop* (invest compounds + pressure into a nascent trait),
  *Strengthen* (use it), *Atrophy* (disuse), *Acquire* (engulf-and-keep an organelle / HGT a gene),
  *Cement* (fix a plastic trait into the genome at reproduction).

## 4. The constraint that makes it a game: UPKEEP
Borrowed from Thrive and non-negotiable for balance: **every gene has an energy upkeep.** Sum of
upkeep must stay ≤ your metabolic income or the cell **starves** (or parts auto-shut-down). This is
what forces choices — you cannot be fast AND armored AND toxic AND photosynthetic. Income comes
from your **metabolism organelle** (and eating); upkeep comes from everything you bolt on.

| Category | Example gene | Build cost (compounds) | Developed by (pressure) | Upkeep | Tradeoff |
|----------|-------------|-----------------------|-------------------------|-------:|----------|
| Morphology | Rigid membrane (↑) | lipids | growing big / getting bitten | tiny | bigger maxSize, worse engulf *(built)* |
| Morphology | Membrane material: chitin | proteins + minerals | taking damage | low | armor + hard to engulf you, but heavy/slow |
| Metabolism | Mitochondrion | *acquired* (engulf & keep) | — | — (produces) | steady energy anywhere |
| Metabolism | Chloroplast | *acquired* (engulf & keep) | basking in light | — in light | strong energy only in lit biomes |
| Locomotion | Flagellum (1 slot) | lipids + proteins | moving / fleeing a lot | med | dash burst; drains energy |
| Offense | Toxin sac | proteins | hunting / fighting | high | AoE deterrent; expensive to run |
| Defense | Armor plating | minerals | surviving attacks | med | survive bites; slower |
| Sensory | Electroreceptors | proteins | living in murk | low | see through murk |

## 5. The five mechanisms of evolution → as mechanics (Niche-grounded)
1. **Mutation** = **stress-induced & directed**: your near-death stresses raise mutation pressure
   *toward* solutions (real: stress-induced mutagenesis), surfacing as developable traits. Plus a
   small **random drift mutation on respawn** (the roguelike spice).
2. **Natural selection + the Baldwin effect** = the FFA is the fitness test (survival validates
   your build), and *sustained behaviour cements plastic traits into the genome* — what you
   repeatedly do becomes inherited. No explicit "selection system"; it's emergent.
3. **Gene flow / horizontal gene transfer** = **steal a gene** from a cell you engulf, or swap
   with biofilm neighbours (S2). A standout MMO-social mechanic — you can acquire a rival's trait.
4. **Genetic drift** = the random respawn mutation, **stronger in small/isolated tide-pool
   populations** (ties drift to server population — emergent per-pool flavor).
5. **Sexual recombination** = **colony formation / conjugation** (S5): merging genomes when cells
   join. Optional but a natural fit for the co-op colony.

## 6. Inheritance & respawn (roguelike lineage)
- Death → respawn as **offspring**: same genome, optionally **one random mutation** (drift).
- Your **lineage = species identity**; it persists across deaths and toward the creature stage.
- A colony (S5) that graduates carries its (possibly recombined) genome forward as a **species**.

## 7. The editor grows with you (per sub-stage scope)
Mirrors Spore's complexity gating — you can't edit what you haven't biologically unlocked
(`CellStage_SubStages.md`):
| Sub-stage | What the editor exposes |
|-----------|-------------------------|
| S1 Replicator | passive traits only (membrane toughness, basic metabolism) |
| S2 Prokaryote | metabolism choice + **horizontal gene transfer** |
| S3 Endosymbiosis | the **organelle branch** (one big pivotal choice) |
| S4 Eukaryote | full part catalog (locomotion/offense/defense/sensory), membrane materials |
| S5 Colony | **cell differentiation** — assign roles to cells in the body |

## 8. Implementation sketch (for when we build Phase 3)
- `GeneDefinition` (ScriptableObject): category, build-cost (per compound), upkeep, slot rules,
  prefab, tradeoffs, and **which adaptive pressure develops it**.
- `GeneLibrary` (catalog of all GeneDefinitions).
- `Compounds` (resource pool): amino acids/proteins, lipids, nucleotides, minerals, vent chemicals —
  gained by eating/habitat. Replaces the single "DNA" number.
- `AdaptivePressure` tracker: accrues per behaviour/stress domain (locomotion, light, combat, toxins,
  murk…) from what the player actually does; gates/cheapens aligned gene development.
- `Genome` (MonoBehaviour on the cell): active genes + **development state (plastic→fixed)**;
  `Apply()` reconfigures the cell. Plastic traits **strengthen with use, atrophy with disuse**.
- `Metabolism`: sums organelle income vs gene upkeep each tick → feeds energy; starvation if negative.
- `Reproduction`: **cements** sufficiently-developed plastic traits into the inherited genome (+ drift).
- Deterministic genome serialization so a lineage/species can persist & replicate server-side.

## 9. Balancing knobs (tune for fun, not realism)
Per the design literature, the fun/accuracy dials are: **mutation rate** (respawn randomness),
**GM income** (how fast you evolve), **upkeep curve** (how punishing complexity is), **respawn
cost**, and **HGT availability**. Start generous, instrument, tighten.

## 10. Worked example genome — "Shallows grazer"
`Chloroplast` (income in light) · `Fluid membrane, low rigidity` (agile, small ceiling) ·
`Cilia` (maneuver) · `Toxin sac` (deter predators) · `Eyespot`. Upkeep is light, income is solar →
energy-positive **only in Sunlit Shallows**; wander into the deep and it starves. A predator build
would instead run `Mitochondrion + rigid chitin membrane + flagellum + proboscis` — bigger, tankier,
energy-hungry, biome-agnostic.

## 11. Open decisions
- **Plasticity → fixation pace:** how long/much use before a trait cements? Too fast = no commitment;
  too slow = unsatisfying. Tune.
- **Atrophy:** do unused traits really wither, and how punishing? (Risk: feels bad to lose progress.)
- **Compound granularity:** how many resource types stay fun without becoming inventory management?
  (Lean: ~4–5: proteins, lipids, nucleotides, minerals, + a biome-special.)
- **Directed vs random:** how much evolution is steered by playstyle vs left to drift/chance?
- **HGT:** from engulfing only, or also proximity/biofilms? Permanent or temporary?
- **Recombination in colonies:** full genome merge, or pick-and-mix?
- **Does upkeep use the same Energy resource as active abilities** (`S4 §4`)? (Lean: yes, one Energy pool.)

## Sources
- [Thrive — Microbe Editor (Developer Wiki)](https://wiki.revolutionarygamesstudio.com/wiki/Microbe_Editor)
- [Thrive — Microbe Stage GDD](https://wiki.revolutionarygamesstudio.com/wiki/Microbe_Stage_GDD)
- [Thrive — Microbe Editor (Fandom)](https://thrive.fandom.com/wiki/Microbe_Editor)
- [Spore — DNA Points (SporeWiki)](https://spore.fandom.com/wiki/DNA_Points)
- [Spore — Cell Stage (StrategyWiki)](https://strategywiki.org/wiki/Spore/Cell_Stage)
- [Niche — a genetics survival game (Steam)](https://store.steampowered.com/app/440650/Niche__a_genetics_survival_game/)
- [The Design and Implementation of Biological Evolution as a Video Game Mechanic (Springer)](https://link.springer.com/chapter/10.1007/978-3-031-49065-1_7)
- [Speciation (Georgia Tech Biological Principles)](https://bioprinciples.biosci.gatech.edu/module-1-evolution/speciation/)
- [Emergence of phenotypic plasticity through epigenetic mechanisms (Evolution Letters)](https://academic.oup.com/evlett/article/8/4/561/7635911)
- [Phenotypic plasticity as a facilitator of microbial evolution (Environmental Epigenetics)](https://academic.oup.com/eep/article/8/1/dvac020/6833161)
- [Stress-induced mutagenesis in bacteria (Nature Reviews Genetics)](https://www.nature.com/articles/nrg3415)
