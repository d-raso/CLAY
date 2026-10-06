# CLAY — Cell Stage Decisions (2026-09-30 design round)

> Outcome of the 2026-09-30 proposal exchange (research summary → Claude proposal → designer notes →
> answers). This doc **layers on top of** `CellStage_SubStages.md` (S0–S5 map), `CellStage_Evolution.md`
> (Live → Develop → Cement → Inherit, nodes, upkeep) and `CellStage_S4_Eukaryote.md`. Where it
> conflicts with them, **this doc wins**; each override is marked **[supersedes …]**.

## Stage naming (designer's names → existing map)
| Designer stage | Sub-stages | Theme |
|---|---|---|
| **Stage 0: Replication** | S0 Protocell + S1 Replicator | build a self-copying pattern without bursting |
| **Stage 1: Little Engines** | S2 Prokaryote | become energetically smart; bacteria vs archaea; gene transfer by bumping |
| **(the big jump)** | S3 Proto-Eukaryote + S4 Eukaryote FFA | cytoskeleton → proto-agar.io → endosymbiosis → nucleus gate → **long agar.io** → nucleus, mitochondria, chloroplasts, sex |
| **Colonies** | S5 | clonal or aggregative multicellularity → graduation |

**Everyone plays Stage 0** (no skipping, even on repeat playthroughs). Keep it short and tense.

## 1. Discovery-only information (hard rule)
- **No meters, no named resources, no objectives** ("collect sulfur", "acquire a mitochondrion").
  State is shown **on the body**: shrinking/wrinkling when starved, visible granules for reserves,
  membrane shimmer under toxins, bulging before division, glow for high-energy small cells.
- **No planet information is revealed** to cell-stage players — not the planet's category, its
  catastrophe, its age, or whether a civilization exists elsewhere on it. Everything is discovered.
- Stage 0 replication: monomers are shaped/coloured motes that **fit together**; a viable chain
  glows, pulses, lengthens and makes the bubble bud. Never explained, only shown.

## 2. Function evolves, appearance is chosen
- **Function** (metabolism, membrane chemistry, motility type, tolerances) comes from mutation +
  behaviour pressure (`CellStage_Evolution.md` §2 stays the model).
- **Appearance** (shape, colour, pattern, membrane texture, part placement/style) is under full
  player creative control in the editor — the Spore appeal, without designing biochemistry.

## 3. Lineage as a living gene pool  **[supersedes Evolution §6 "respawn as offspring"]**
- A species is the set of **living bodies** carrying its genome variants — players and AI alike.
- On death you **choose where to respawn**: a list of the **viable variants of your species
  currently alive** (players' and AI cells'), each shown as its living body with its visible traits.
  You spawn into that body. (Mutations are therefore *seen*: variants differ visibly; stats stay
  hidden.)
- **Death is selection:** the mutations carried only by the dead body are **removed from the pool**;
  the surviving variants' share grows ("reinforces the progress of others"). Successful variants
  spread because more players and offspring end up in them — population genetics as the respawn UI.
- Small/isolated pools drift more (Evolution §5.4 still applies).

## 4. Merges: hijack slider and co-pilots  (new)
Shared mechanic for **viruses** and **endosymbiosis** — two players in one organism.
- **Hijack (before takeover):** the infected player keeps control, but the virus player gets
  **random spurts of keyboard control** over infected bodies. How often and how strong those spurts
  are scales with the **share of the species' population the virus currently infects**.
- **Species-level takeover at 40 %:** if a virus player infects **≥ 40 % of a species' living
  bodies**, the virus integrates permanently (endogenous retrovirus — real: syncytin). The virus
  player **joins that species as an independent member**: from then on they play their own cell of
  that species with full control, like any other player in it. *They took a shortcut in evolution.*
- **Endosymbiont candidates:** small, high-energy player cells **glow**. If a larger cell engulfs one,
  the small player chooses: fight free, or **become the organelle** → merged lineage, the former
  player continues as a co-pilot (e.g. controls an on-demand energy burst).

## 4b. Aggregative colonies and freeloaders  (decided)
- Who becomes the sacrificial **stalk** (vs. reproductive spores) is decided by **morphology and
  chance** — position, size, biology — never chosen by the player.
- **No mechanical punishment for cheating.** By the colony stage players implicitly know the colony
  will become one multicellular organism, so the incentive is to behave fairly. The consequence of
  defecting is **social**: other players remember who the cheaters were and can work to keep them
  from rising to power when evolution reaches civilization.

## 5. The virus path  (new)
- Entry: chosen at any point in Stages 0–1, or fallen into after failing (repeated bursts /
  starving lineage). Never a game over.
- Semi-living: cannot eat or make energy. Drift → attach → inject → hijack (slider) → burst out as
  copies, or lie **dormant** in the host genome.
- Hosts fight back with evolved immunity (CRISPR-like) → arms race.
- Dormant at a host milestone + species threshold reached → permanent integration (§4).

**S0-specific virus experience (viroid phase).** In S0, before capsid proteins exist, the
player's "virus" is a **naked replicating polymer** (a viroid — real; viroids are the smallest
known infectious agents, just RNA strands). The S0 virus arc:
1. *Shed* — membrane dissolves; player becomes viroid-scale, nearly invisible.
2. *Drift* — carried by currents with no energy management; UV and harsh chemistry damage you
   in open water (biome-dependent; see `CellStage_SubStages.md` S0 Biomes table).
3. *Passive absorption* — cannot enter a host actively; must position near feeding protocells
   and be absorbed passively alongside monomers. Biome determines host density and drift patterns.
4. *Internal replication* — copy yourself using the host's monomer supply. Host membrane shimmers
   and behaves erratically — visible state, unknown cause (discovery-only rule maintained).
5. *Lyse or lie dormant* — lyse to scatter copies (risky; depends on biome currents), or stay
   integrated through the S0→S1 founder collapse to become part of a founding lineage permanently.

## 6. Milestones, order and setbacks  (new)
Each sub-stage has a **graduation milestone** plus supporting milestones. Reaching the graduation
milestone without the supporting ones is allowed but brings **setbacks** (slowdowns, never blocks):
- big genome without nucleus → mutation meltdown (most offspring broken);
- O₂ producer without O₂ tolerance → self-poisoning, kin die-off;
- mitochondria without enough membrane surface → energy capped, starvation when large;
- multicellular without a single-cell bottleneck → cheaters/cancer spread inside the colony.

## 7. Planet history and catastrophes
- Player + AI metabolism is tallied per planet. Oxygenic dominance → O₂ rise → **Great Oxidation
  Event, planet-wide** when chemistry allows (rust-out, anaerobe die-off, possible Snowball,
  sky colour change). Other emergent catastrophes may arise the same way.
- Planet-rolled catastrophe deadline (`CellStage.md` §6) stays: tide-pool play is **time-limited**.
- **Open (designer leaning yes):** late-starting cell play on planets that already host advanced
  life — e.g. betting on a civilization's collapse and seeding life anew in a niche. Must not leak
  the planet's circumstances to newcomers (§1). Decide when the MMO layer is designed.

## 8. Alien chemistry
Same mechanics, different chemistry per planet `liquidType`: water (baseline), ammonia (colder,
slower), methane/ethane (azotosome membranes, very slow, long-lived), sulfuric-acid cloud droplets
(airborne arena — leave the layer and die), silicon (speculative, hot, crystalline, slowest).

## 9. Arcade feel
5–15 minute sessions; respawn choice on every death; always a bigger and a smaller fish in view
(agar.io density tuning); resource patches drift and pulse; responsive low-Reynolds movement
(instant stop, no coasting); every unlock is a **new verb** (absorb, secrete, engulf, keep, merge,
infect), not a stat bump.

## Build order (proposed)
1. S4 agar.io core + living-gene-pool respawn (§3), on the existing S4 prototype — prove fun first.
2. S2 metabolism + gene transfer by bumping + planet O₂ ledger.
3. S0/S1 replication prologue (monomer fitting, bursting, wet–dry cycles).
4. S3 endosymbiosis incl. player candidates (§4).
5. S5 colonies (clonal / aggregative, cheaters).
6. Virus path + hijack slider.
7. Multiplayer layering (merges, species thresholds).

## 10. S0 seabed manual assembly  (new, 2026-10-01)
The original discovery-only rule (§1) is preserved but made tactile: the camera can zoom into
the player's immediate seabed, and the player can **physically drag and place monomers onto the
mineral surface** to attempt chain assembly. No labels, no objectives — a viable chain glows and
pulses; an invalid arrangement does nothing. You learn what works by experimenting.

The seabed surface type varies per biome (see `CellStage_SubStages.md` S0 Biomes) and changes
how assembly feels: clay is a smooth workbench, pyrite crystals require directional placement,
vent crust requires digging monomers out of crevices first, cryovolcanic ice is constantly
resurfaced by eruptions. The surface is a gameplay mechanic as well as an aesthetic one.

## 11. S0 → S1 collaborative transition: 3 founders  (new, 2026-10-01)
S0 does not graduate individually. When a threshold number of protocells in the pool
successfully encapsulate a self-replicating polymer, the pool collapses into **3 founding
lineage templates** — the 3 most genetically distinct surviving lines — and all S0 players
respawn as a member of one of those 3 lineages (living gene pool mechanic, §3). Every player
in S0 is therefore collectively priming the same primordial soup; individual success raises
the threshold faster. The 3 founder lineages then diverge through S1 and beyond, seeding
biodiversity in the pool.

Players who do not want to transition at the collapse moment can instead opt into the virus
path (§5) — they stay in S0 as viroids while S1 bootstraps above them, and can still infect
S1 cells. This is never presented as a failure state; it is an explicit choice.

**Everyone plays Stage 0** (no skipping, per the original rule). Keep it short and tense.

## 12. S2 design decisions  (2026-10-01)

**Metabolism is never chosen from a menu.** `Genome.metabolism` drifts under selection pressure
from biome exposure and HGT. The player biases it by where they spend time and who they bump.
Offspring show the drift visibly (colour, glow character, particle emissions change with
metabolism type). The GOE is never announced — the player reads it from the world chemistry.

**HGT is contact-based and low-stakes.** Bumping another cell for sustained contact triggers
gene exchange — silent, no UI. The player can intentionally seek out high-performing cells to
trade with. Viroids can also transfer via HGT (transduction), introducing an infection risk.
This is the first social mechanic and the direct precursor to colony formation.

**Biofilms form emergently from proximity.** No join-biofilm button. Cells that stay close
enough long enough secrete a visible polymer matrix. The biofilm confers hazard resistance and
faster HGT at the cost of mobility. Mild internal specialization is automatic (not player-chosen).

**The Bacteria/Archaea fork is environment-driven.** `Domain.Bacteria` vs `Domain.Archaea`
drifts based on environment and selection. Archaea path is shorter to S3 (Asgard lineage
naturally accumulates proto-cytoskeleton genes). Both paths are valid but feel distinct.
Bacteria are faster and more versatile; Archaea are tougher and better positioned for extremes.

**GOE fires at a planet-wide ledger threshold.** `PlanetHistoryLedger` tallies O₂ from all
oxygenic photosynthesizers. At threshold: pool chemistry shifts visibly (rust staining, water
clarity, particle change), anaerobes without tolerance take damage, aerobic respiration unlocks.
Setback from Decisions §6 applies: O₂ producers without O₂ tolerance self-poison.

**Aerobic respiration is the S2 reward.** It has the highest energy yield of any metabolism
but is strictly post-GOE. Players who pre-evolved O₂ tolerance claim it immediately after the
GOE fires; latecomers must race to adapt. This creates a natural competitive spike at the
GOE boundary.

**S3 gate is three visible body conditions** (domain maturity, cell size, proto-engulf dimple
mutation), none of which are labeled. The player sees the dimple happen once — that is the
only signal that S3 has unlocked.

## Open questions
- Late-start life on advanced planets (§7) — **deferred until much later**.
- Endosymbiont merge: does the absorbed player also become an independent member of the host
  species (same as virus takeover), or stay inside as the organelle?
- Does HGT persist as a mechanic into S3/S4? (Suspected yes, but rate drops dramatically once
  cytoskeleton and nucleus appear — eukaryotes mostly lost lateral gene transfer.)
- Exact GOE O₂ threshold per planet type — needs playtesting once S2 is built.
