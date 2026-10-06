# CLAY — S4: Eukaryote FFA (detailed stage design)

> The skill core of the cell stage. Cytoskeleton + phagocytosis unlock eating other cells, so
> this is the agar.io predator/prey free-for-all fused with Spore's parts-as-active-abilities.
> Entry from S3 (you just acquired an organelle engine); exit to S5 (colony) at apex + gate.
> Context: `CellStage_SubStages.md` (S4), `CellStage.md`, `CellStage_BuildPlan.md` Phases 1–4.
>
> Numbers below are **indicative starting points**, all tunable.

## 0. Science groundwork — the fossil record of early eukaryotes

The eukaryotic FFA is not fiction: the fossil record shows exactly this phase.

- **Grypania spiralis (~2.1 Ga)** — a coiled, ribbon-like fossil from the Negaunee Iron
  Formation, Michigan. Possibly the earliest eukaryote fossil. Its consistent large size and
  helical form are difficult to explain as a bacterium. *(In CLAY: the cell forms and sizes
  visible at S4 entry are grounded in what Grypania-sized organisms looked like — a few
  hundred micrometres, visible to the naked eye for the first time.)*

- **Chuanlinggou multicellular eukaryotes (~1.63 Ga)** — a 2024 *Science Advances* paper
  described differentiated multicellular eukaryotes from North China at 1.63 Ga, with cells
  performing different roles. This is the earliest confirmed complex multicellular eukaryote,
  pushing back the S4→S5 transition timeline significantly. *(In CLAY: the colony-adhesion
  gate that ends S4 is calibrated against this — multicellularity as an emergent possibility
  much earlier than the Ediacaran.)*

- **Bangiomorpha pubescens (~1.047 Ga)** — from Baffin Island, Arctic Canada. The oldest
  fossil confidently assigned to a modern lineage (red alga, *Bangia* genus). Critically, it
  shows **morphological evidence of sexual reproduction** — differentiated cells that likely
  produced gametes. *(In CLAY: the sexual recombination mechanic in §13 is directly modelled
  on Bangiomorpha. It shows that meiosis was in play long before the Ediacaran.)*

- **The billion-year stasis.** Eukaryotes existed for roughly **one billion years** as mostly
  small, single-celled organisms before multicellularity took off in the Ediacaran (~635 Ma).
  This "boring billion" was dominated by S4-equivalent predator/prey competition. In CLAY, S4
  is the longest sub-stage by design — the billion-year stasis is the justification for it
  being the deep arcade phase.

## 1. Fantasy & the 10-second loop
You are a hunting eukaryote. **Sense → close → engulf (or flee) → grow → spend genes → repeat.**
Every few seconds you make a predator/prey read on everyone nearby and act on it. Builds make
those reads asymmetric (a fast glass-cannon vs a slow tank vs a stealth ambusher).

## 2. The biomass economy (central currency)
- **Biomass = size = score.** Eating adds biomass; you visibly grow.
- **Mass ↔ speed tradeoff (the agar.io balance):** base speed ∝ 1/√mass. Bigger eats more but
  is slower and clumsier — a small fast cell can outrun a big one.
- **Membrane rigidity → max size, or you POP (the size risk):** every cell has a `maxMass`
  ceiling set by an evolvable **membrane rigidity** trait. Grow past it and the cell **bursts**
  (death, drops biomass). This is the hard cap that keeps the FFA from snowballing.
  - **Rigidity tradeoff (a core evolution choice):**
    - ⬆ **Upside:** more rigid membrane → higher `maxMass`, so you can safely get bigger.
    - ⬇ **Downside:** a stiff membrane can't wrap around prey → **reduced engulf power**
      (`EngulfPower` falls; the engulf size-gate gets stricter / slower). *(Interpretation: rigidity
      hurts YOUR phagocytosis. If instead it should make you harder for others to engulf, flip it.)*
  - So builds fork: **rigid tank** (huge, safe from popping, poor hunter) vs **soft predator**
    (smaller ceiling, must stay lean or pop, but engulfs aggressively).
  - `maxMass = baseMaxMass + rigidity × rigidityMassBonus`; `EngulfPower = lerp(1, min, rigidity)`.
- **Soft cap + molt risk (optional, layered on top):** near the ceiling, growth can slow and a
  brief vulnerable **molt** may be required — secondary to the pop cap above.
- **Death:** when engulfed you drop most of your biomass as **meat chunks**; respawn small;
  lineage/account progress persists. Killers get a meal, not a deletion of your progress.

## 3. Engulf mechanics (the core verb)
- **Size gate:** you can engulf a target only if `yourMass ≥ target.mass × 1.25` (the 25% rule —
  tunable). Below that, you can still *bite* (partial damage) with offensive parts.
- **Engulf is not instant:** closing your membrane around prey takes ~0.4–0.8s scaled by size
  difference; during it you're committed and **vulnerable** (a bigger cell can punish you). This
  creates counterplay rather than instant deletion.
- **Escape window:** prey can break a partial engulf with a dash/jet part before it closes.
- **Bite vs engulf:** offensive parts (spike, proboscis) let a smaller cell chip a larger one —
  so size isn't the *only* axis; a fast biter can whittle a tank.

## 4. Energy & metabolism (separate from biomass)
- **Energy** is the spend for active abilities; **biomass** is size. Keeps "use abilities" and
  "grow" as distinct resources so a big cell can still be ability-starved.
- **Passive income comes from your S3 organelle branch** — this is what binds build to biome:
  - **Mitochondrion (aerobic):** steady energy anywhere, best all-rounder; burst bonus (sprint-engulf).
  - **Chloroplast (photo):** strong energy *only in lit biomes* (Sunlit Shallows); useless in deep/murk;
    can photosynthesize while fully stationary (stealth-graze playstyle).
  - **Chemosymbiont:** strong energy *only near vents* (Iron Seep / Mineral Springs); passive toxin
    resistance in sulfur/methane biomes.
  - **Radiosymbiont (melanin):** converts ambient radiation to energy; strongest in high-radiation
    biome bands; immune to rad-pulse hazards that damage other cells. Rare worlds only.
  - **Thermosymbiont:** energy from thermal gradients; fastest passive income near hot-vent edges;
    sluggish energy income in cold/stable zones; scales with biome temperature delta.
  - → Your engine choice dictates *where you're strong*, pushing players into different biomes.
- Eating also tops up energy; starvation slowly drains biomass.
- **No-symbiont penalty:** players who skipped S3 endosymbiosis enter S4 with base energy income
  only (fermentation-rate, equivalent to S2 Mitochondrion). Viable but outpaced in late S4 by
  branched players near their home biome.

## 5. Parts catalog (active + passive + metabolic)
Parts attach to the soft-body and deform with it (legible silhouettes). Slots scale with size/tier.

**Active (aim & time, cost energy):**
| Part | Effect | Counter |
|------|--------|---------|
| Flagellum | Directional **dash** burst | predict & intercept |
| Cilia ring | Sustained tighter turning | — (mobility) |
| Spike / ram | Contact **damage**, knockback | range/kiting |
| Proboscis | Ranged **latch + biomass drain** | dash out of range |
| Toxin sac | **AoE** deter/damage cloud | wait it out / armor |
| Contractile vacuole (jet) | Panic **escape** burst | bait it, then commit |
| Pseudopod | Extend reach to **grab** prey | — (utility) |

**Passive / defensive:**
| Part | Effect |
|------|--------|
| Thick membrane | Armor (reduces bite damage; slows you) |
| Defensive spikes | Retaliation damage to engulfers |
| Transparency / camo | Harder to see (esp. in murk) |
| Electroreceptors | **Sense cells through murk** (counters camo) |
| Eyespot | Larger vision radius in clear water |

**Metabolic:** photosynthetic membrane, chemosynthetic patch, storage vacuole (energy buffer).

> Diet identity (herbivore/carnivore/mixotroph) is **emergent** from part + organelle choices, not
> a hard class pick. A photosynthetic, toxin-armed cell plays like a grazer; a fast proboscis
> build plays like a hunter.

## 6. Evolution / editor (in S4)
- Eating banks **genetic material**; spend in the editor on parts/morphology.
- **Async, no world-pause** — you stay vulnerable while editing, or duck into a safe pocket.
- Trait slots unlock with size sub-tiers (§7). Respec allowed at a gene cost (encourages
  adaptation to the biome you've drifted into).

## 7. Progression within S4 (sub-tiers)
| Tier | ~Size | Unlocks |
|------|-------|---------|
| Small protist | entry | 2 part slots, shallow/clear biomes |
| Mid protist | 2× | +1 slot, murkier biomes (need electroreceptors to hunt there) |
| Large protist | 4× | +1 slot, deep/harsh biomes, molt risk begins |
| **Apex** | soft cap | colony-formation gate becomes available (→ S5) |

Deeper/harsher biomes = more food & fewer rivals but more environmental danger (chemistry,
currents). Risk/reward traversal using the `FlowFieldManager` currents.

## 8. Threat readability (must-have for a fair FFA)
- **Size** read instantly from silhouette scale.
- **Offensive parts** visible on the silhouette (spikes/proboscis telegraph danger).
- **Organelle branch = body color/tint** (e.g., greenish = photosynthetic) so you can guess where
  a cell is strong/weak.
- Your own **edible/engulfable targets subtly highlighted** (size-gate feedback) so the predator/
  prey read is honest.

## 9. Biome interplay (reuses built systems)
- **Currents** (`FlowFieldManager`): fast risky traversal; getting swept toward a bigger cell = death.
- **Murk** (`BiomeLibrary`): ambush cover; camo strong, electroreceptors counter.
- **Chemistry/light**: gates where each organelle branch thrives (§4).
- **Food density**: biome-driven spawn of plant motes (graze) + meat chunks (from kills).

## 10. MMO / netcode notes (hold from the start)
Server-authoritative engulf/biomass/energy; clients send intent, receive state; interest
management (only replicate nearby cells); client prediction/interpolation for movement. Engulf
resolution must be server-side to prevent cheating. See `CellStage_BuildPlan.md` Phase 8.

## 11. Build order for S4 (maps to BuildPlan Phases 1–4)
1. **Biomass + movement + mass/speed curve** (no eating yet) — prove the feel.
2. **Edible motes + growth** (osmotrophic, the Phase-1 MVP entities).
3. **Engulf vs other cells** (size gate, engulf-time vulnerability, death → chunks).
4. **First active part (flagellum dash)** — prove "active ability" feel.
5. **Energy + one organelle branch's passive income** — prove build-to-biome binding.
6. Expand parts catalog + editor + sub-tiers.

## 12. Living gene pool respawn in S4

Same mechanic as S0–S3 (`CellStage_Decisions.md` §3) but the pool is richer:

On death the player sees a **picker** — a list of living variants of their species currently alive
in the pool (AI and other players). Each is shown as its actual body with visible traits, at its
current size. You spawn *into* that body at its current mass; you don't restart small unless no
surviving variants exist.

**S4-specific rules:**
- Variants diverge faster in S4 (sexual recombination + more mutation per generation).
  The picker may show cells with meaningfully different part loadouts or even different
  symbiont branch expression from mutations. This is intentional — you're reading the live
  state of your lineage.
- Picking a large-mass variant is high-risk (it may be hunted immediately) but high-reward
  (you skip early growth). Picking a small variant is safer but slower.
- If a variant was the one that killed you (e.g., a predator in your own species), it does
  not appear in the picker.
- The species' symbiont branch is locked (it was set in S3); parts can differ per variant.

## 13. Sexual recombination (meiosis-lite)

Science: eukaryotes can reproduce sexually via meiosis — halving and then combining genomes.
This scrambles traits in offspring and dramatically accelerates adaptation.

**Mechanic.** Two cells of the same species (compatible genome) can opt into a brief **merge**:
- Both cells slow to a near-stop and their membranes touch (amoeboid contact, ~2 seconds).
- Offspring genomes are recombined: each child gets roughly half the parts/traits from each parent.
- Two offspring emerge at half the combined mass of both parents.
- The original two cells are gone. This is voluntary and symmetric — both players agree by
  holding the input simultaneously (no prompting UI, just the held input).

**Why do it?** If one parent has a fast dash and the other has thick membrane armor, offspring
might get both — a faster path to a strong build than mutation alone.

**Risk:** you're both stationary and fused for ~2 seconds. Any predator can interrupt by engulfing
one of you; the merge collapses and both cells take area damage.

**AI cells** recombine automatically when genome distance from the player exceeds a drift threshold,
keeping the species pool diverse without player input.

**No sex → slower adaptation.** Players who never recombine still evolve via mutation but more
slowly. Biomes with high environmental pressure (GOE aftermath, radiation belts) will visibly
punish static genomes over time.

## 14. Colony-formation gate (S4 → S5)

Resolved (was TBD in §7 and §12). Gate to S5 requires **all three**:

1. **Apex tier reached** — cell size in the top sub-tier for the current pool.
2. **First division adhesion** — one division event where the offspring cell fails to separate
   and the two remain connected for > 5 seconds. This is a mutation, not a player action;
   it occurs randomly at Apex tier with a rate proportional to a new `adhesionMutationRate`
   genome field. The player sees two cells moving together. No label.
3. **Species population threshold** — ≥ 10 living bodies of the player's species in the pool
   (ensures the pool is healthy enough to seed an S5 colony).

When all three fire, `Milestone.ColonyAdhesion` triggers and the S5 gate opens.

The adhesion mutation being involuntary is intentional: it mirrors how real multicellularity
arose — not chosen, but selected for when it accidentally happened and proved advantageous.

## 15. Open questions
- Exact 25% engulf ratio & engulf-time curve (playtest).
- Is energy worth the extra system, or fold abilities into a biomass cost? (Lean: keep separate.)
- How hard should molt punish the biggest cells?
- Sexual recombination opt-in signal: held input on both players simultaneously is clean but
  may be accidentally triggered. Alternative: a brief proximity glow that either player can
  break by moving away.
