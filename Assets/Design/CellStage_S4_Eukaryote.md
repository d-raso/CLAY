# CLAY — S4: Eukaryote FFA (detailed stage design)

> The skill core of the cell stage. Cytoskeleton + phagocytosis unlock eating other cells, so
> this is the agar.io predator/prey free-for-all fused with Spore's parts-as-active-abilities.
> Entry from S3 (you just acquired an organelle engine); exit to S5 (colony) at apex + gate.
> Context: `CellStage_SubStages.md` (S4), `CellStage.md`, `CellStage_BuildPlan.md` Phases 1–4.
>
> Numbers below are **indicative starting points**, all tunable.

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
  - **Mitochondrion (aerobic):** steady energy anywhere; best all-rounder.
  - **Chloroplast (photo):** strong energy *only in lit biomes* (Sunlit Shallows); useless in deep/murk.
  - **Chemosymbiont:** strong energy *only near vents* (Iron Seep / Mineral Springs).
  - → Your engine choice dictates *where you're strong*, pushing players into different biomes.
- Eating also tops up energy; starvation slowly drains biomass.

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

## 12. Open questions
- Exact 25% engulf ratio & engulf-time curve (playtest).
- Is energy worth the extra system, or fold abilities into a biomass cost? (Lean: keep separate.)
- Colony-formation gate trigger (TBD) — candidate: reach Apex tier.
- How hard should molt punish the biggest cells?
