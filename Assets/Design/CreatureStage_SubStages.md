# CLAY — Creature Stage Sub-Stages (the science-to-gameplay map)

> The creature stage picks up from the S5 colony handoff: a proto-organism on the beach, descended
> from a single tide-pool lineage. It follows the real evolutionary sequence from basal metazoan
> to complex socially-capable animal. Like the cell stage, each sub-stage unlocks a fundamentally
> different kind of gameplay. The key gate: **a centralised brain is required for true social
> behaviour**, so the tribal stage cannot exist early. The creature stage earns it.
>
> Science sources at the bottom. Numbers are indicative starting points, all tunable.

---

## Progression at a glance

| # | Sub-stage | Science model | Gameplay mode | Unlock gate |
|---|-----------|--------------|---------------|-------------|
| C0 | **Basal Metazoan** | Sponge / Placozoa / Trichoplax | Passive survival, body plan assembly | First contractile cell / proto-nerve |
| C1 | **Cnidarian** | Jellyfish / Hydra / coral | First active predation, radial locomotion | Bilateral symmetry mutation |
| C2 | **Bilaterian Worm** | Flatworm / annelid / early Cambrian worm | Directed hunting, cephalization, first brain | First appendage-like structure |
| C3 | **Cambrian Metazoan** | Anomalocaris / trilobite / Pikaia | Full creature gameplay — Spore + social | Complex brain + social milestone |
| — | **→ Tribal stage** | Early tool use / group behaviour | — | Social complexity gate |

Earliest animals diverged **613–593 Ma** (Ediacaran); the Cambrian explosion (~**530 Ma**)
produced most animal body plans in ~10 million years. The creature stage spans this range.
The planet catastrophe clock (`PlanetaryParameters.md` §5) continues running throughout.

---

## What carries over from the cell stage

- **Living gene pool respawn** — unchanged. Death shows living variants of your species; you
  spawn into one at its current body state.
- **Appearance vs function split** (`CellStage_Decisions.md` §2) — function (nervous system
  complexity, appendage type, diet, sensory organs) evolves via mutation and selection. Appearance
  (colour, texture, shape style, decorative features) is under full player creative control in
  the editor.
- **Symbiont branch identity** — your S3 organelle branch sets your metabolic baseline into the
  creature stage. Mitochondrion = best all-round energy; Chloroplast = light-dependent but
  passive regen; Chemosymbiont = vent-adjacent bonus; Radiosymbiont = radiation immunity.
- **Discovery-only UI** — no HP bars, no stamina meters, no XP numbers. State shown on the body:
  colour loss = starvation, posture = aggression/fear, glow/shimmer = health/energy.
- **Planet environment** drives biome, resource density, hazard type, and which body plans are
  advantaged (cold planets → slower metabolism → different optimum creature morphology).

---

## C0 — Basal Metazoan  · *sponge / Trichoplax* · passive, body-plan assembly

**Science.** The simplest known living animal is *Trichoplax adhaerens* — a flat plate of
~2,000 cells with no nervous system, no muscles, no gut, no symmetry. It crawls over food
and digests it extracellularly by lying on top of it. **Sponges (Porifera)** are barely more
complex: no nervous system, no muscle, but have specialised cell types — **choanocytes**
(collar cells that beat flagella to pull water through pores, filtering out bacteria and organic
particles), **archaeocytes** (totipotent cells that can differentiate into any other type),
and **spicules** (silica or calcium carbonate structural spines). The earliest animal fossils
are sponge-like — **Doushantuo embryo fossils** (~600 Ma, South China) and probable sponge
biomarker steroids in ~650 Ma rocks. **No nervous system, no directed movement, no predation.**
Competition at this stage is entirely about efficient positioning and resource capture rate.

**Fantasy.** You are a body in assembly. You arrived on the beach as a mass of differentiated
cells. Now you have to become *something* — a shape that can hold together, feed itself, and
not get eaten. The creature stage editor opens here for the first time in its full form, but
your creature doesn't move yet. You build it; the world selects.

**Goal.** `Milestone.ProtoNerve` — a contractile cell appears in the body (the precursor to
muscle and neuron), enabling the first physical response to a contact stimulus. This is the
C0→C1 gate: your body can *react* for the first time.

**Duration target.** 5–10 minutes — shorter than S0, enough to establish the body plan and
feel the passive-survival tension. C1 is where locomotion begins.

### C0 Core mechanic — filter feeding and body geometry

C0 creatures are **sessile or near-sessile**. Movement, if any, is extremely slow (crawling
like Trichoplax). The primary resource acquisition mechanic is **filter feeding**: organic
particles and microbes drift through the water; your body's geometry determines how efficiently
you capture them.

**Body geometry matters directly:**
- Pore arrangement (for sponge-like forms) determines water flow rate through the body.
  More pores = more food but structurally weaker. Tight pore clusters = strong flow channels.
- Surface area exposed to current = primary food intake rate.
- Height off the substrate = better access to cleaner, particle-rich water; but tall structures
  topple in strong currents.
- Shape drives passive feeding without any movement from the player. The editor IS the mechanic.

**Currents.** The beach/tidal zone has visible water currents driven by the same
`FlowFieldManager` as the cell stage. Positioning your creature to intercept the current
is the key spatial decision of C0. Current direction shifts with tide. High-current zones
have more food but more risk of toppling.

**Substrate anchoring.** Your creature is anchored to the substrate. The anchor point is
chosen when you first place (settle from the S5 handoff). Repositioning is slow and costly
(detach → drift → re-anchor). Anchor substrate type (rock / sand / coral-like mineral /
organic mat) affects growth rate and available spicule types.

**Predation pressure.** You cannot fight back in C0. Larger organisms can partially eat you
(graze from your mass, shrinking your area). Chemical deterrents (encoded in genome) are
your only defense — bitter compounds visible as a colour pattern on the body surface. Learnt
avoidance by AI predators follows after repeated punishments (classical conditioning, grounded
in real cnidarian/sponge chemical ecology).

**Budding.** Once sufficient mass is banked, the body produces a **bud** — a smaller copy
that detaches and settles nearby (vegetative reproduction). Buds carry your genome with low
mutation rate. This is the C0 reproduction mechanic — no sexual recombination yet at this
stage.

### C0 body plan editor (first full creature editor)

The creature editor opens in its full form here, but with **C0-appropriate constraints**:
- Parts available: body volume shapes, pore density, spicule type (structural spines),
  surface texture (smooth vs fibrous vs branched), colour and chemical pattern.
- No appendages, no eyes, no mouth with directed intake — these unlock progressively.
- Function is prescribed by mutation/evolution; appearance within the functional constraints
  is the player's creative domain.
- The editor is async and non-pausing (same rule as cell stage S4): you are vulnerable
  while editing. The game continues.

### C0 Biome variants

| Environment | C0 body plan pressure | Hazards |
|------------|----------------------|---------|
| Tidal flat (wet/dry cycling) | Compact, low-profile, desiccation-resistant cuticle | Periodic drying — tall structures die |
| Subtidal rock | Tall branching structure (max surface area) | Wave surge topples tall forms |
| Warm shallow reef | Calcified spicule skeleton (most stable) | Herbivore grazing |
| Cold deep shelf | Spread flat (no current lift) | Near-zero food flux |
| Volcanic seep | Chemosymbiont-fed (no filter feeding needed) | Acid pulses |
| Sandy substrate | Burrow-adjacent, anchored below surface | Sand mobility |

### Gate → C1

`Milestone.ProtoNerve` fires when a contractile cell mutation appears in the body (visible as
a brief local twitch in response to contact). No UI announcement — the body just *moves*
for a tiny instant when touched. Players near you will see it and not immediately understand it.
That moment of first reaction is the signal.

---

## C1 — Cnidarian  · *jellyfish / hydra / coral polyp* · first locomotion, first predation

**Science.** Cnidarians (~**600–560 Ma**) are the earliest animals with a **nervous system** —
a **nerve net**: neurons distributed across the whole body with no central brain, no head.
Signals propagate in all directions from the stimulus point; the entire organism responds.
Two body forms: **polyp** (sessile, tube-shaped, oral end up, like hydra or anemones) and
**medusa** (swimming, bell-shaped, like jellyfish). Many cnidarians cycle between both.
**Nematocysts** — explosive stinging cells that fire on touch contact, injecting toxin —
are the primary predation tool, still in use today (box jellyfish venom can kill a human).
Earliest cnidarian-like fossil trails (~**555 Ma**) *meander and crisscross haphazardly*,
indicating a poorly developed nervous system unable to track nearby prey or predators.
By ~530 Ma fossil evidence shows the nerve net already enabling meaningful directed response.

**Fantasy.** You twitch. Then you pulse. Then you sting something. C1 is the first time
movement and predation coexist in the same body.

**Goal.** `Milestone.BilateralSymmetry` — a mutation causes one axis of the body to differ
from the opposite (head/tail axis begins forming). This is the C1→C2 gate.

**Duration target.** 10–20 minutes — first real locomotion arcade.

### C1 Core loop

**Polyp phase** (default entry): sessile like C0 but with tentacles that extend into the
water column and actively capture prey on contact (nematocyst fire). The player can retract
and extend tentacles (first active input). Feeding is now partly active.

**Medusa unlock**: a mutation causes the body to develop bell geometry and pulsing muscles.
The player can now swim — slow, pulsing jet propulsion. This is the first free locomotion in
the creature stage. Movement is still radially symmetric (no preferred forward direction).

**Nerve net gameplay:** reactions are decentralised — if a stimulus hits one side, that side
responds first, then the signal propagates. The player's input is slightly sluggish and
whole-body (no fine motor control). This will feel meaningfully different from C2 where the
brain gives instant directed response.

**First active predation:** nematocysts fire on contact. Prey captured by tentacles is drawn
to the central mouth. The player can steer tentacle placement (radially) but cannot aim
precisely. Ambush positioning matters more than pursuit.

**Predator/prey:** C1 is the first stage where the player is simultaneously predator (to
smaller organisms) and prey (to C2+ creatures). Larger bilaterians can actively hunt jellyfish.
The radial body has no "facing" — you can be attacked from any direction equally.

**Chemical deterrents** from C0 carry over and can be enhanced (more nematocysts = more
deterrent + more capture power, but more energy cost to maintain).

### Gate → C2

`Milestone.BilateralSymmetry` — a mutation creates asymmetry along one body axis. Visible as
one side of the body slightly flattening, elongating, or darkening relative to the other.
Movement immediately becomes slightly directional. No label; the player feels it as drift
toward a preferred direction of motion.

---

## C2 — Bilaterian Worm  · *flatworm / annelid / early Cambrian worm* · directed, hunting

**Science.** Bilateral symmetry enabled **cephalization** — concentration of sensory organs
and nervous tissue at the head end. The **cerebral ganglion** (proto-brain) evolved as a
processing centre for stimuli arriving from the direction of travel. The first **eyes** were
simple photoreceptor patches (eyespots) detecting light direction only — no image formation.
The first **complete digestive tract** (mouth + anus, separate openings) enabled continuous
feeding without interrupting digestion. Cambrian annelids and polychaetes show clear
segmented body plans with differentiated head structures by ~530 Ma.

**Fantasy.** You have a face now. You go toward things and away from things on purpose.
The world resolves into *in front of you* and *behind you* for the first time.

**Goal.** `Milestone.FirstAppendage` — a lateral outgrowth mutation produces a parapodia-like
or lobe-fin-like structure. This is the C2→C3 gate and the foundation of all limb evolution.

**Duration target.** 15–25 minutes.

### C2 Core loop

- **Directed locomotion** — peristaltic crawling or undulating swimming. Speed proportional
  to body length and segmentation. The player has a clear forward direction for the first time.
- **Active hunting** — chase smaller organisms, intercept fleeing prey. The ganglion enables
  tracking (gradual turning toward stimulus source, not whole-body response).
- **Sensory organs** — eyespots (detect light direction, used for shadow-avoidance and
  approaching food), chemoreceptors (smell gradients, detect food patches and predator scent).
  Both are genome-encoded and visible on the head.
- **Burrowing** — soft-body bilaterians can burrow into substrate for defense. Burrowing
  is the primary evasion mechanic against C3 predators.
- **Segmentation editor** — body can now be segmented; segment count and shape affect
  locomotion style, turning radius, and which parts can be added in C3.

### Gate → C3

`Milestone.FirstAppendage` — a parapodia or proto-limb mutation appears on a body segment.
Visible as a small lateral lobe. Immediately improves swimming speed and turning. The player
can now access the appendage part category in the editor.

---

## C3 — Cambrian Metazoan  · *anomalocaris / trilobite / Pikaia* · **the Spore phase**

**Science.** The **Cambrian explosion** (~530 Ma) produced virtually every major animal body
plan within ~10 million years. **Compound eyes** (trilobites — first detailed image-forming
vision in animals), **jointed appendages** (arthropods — speed, dexterity, grasping),
**exoskeletons** (mineralised defence), **active jaws/radulae** (directed feeding). Fossil
Cambrian brains (Chengjiang fauna, China) show clear regional specialisation — not just a
ganglion but a proto-cortex. **Pikaia gracilens** (Burgess Shale, ~508 Ma) — a chordate with
a notochord — is the likely ancestor of all vertebrates, appearing in the same Cambrian
ecosystem as trilobites and anomalocaris.

**Fantasy.** Full creature — you have limbs, eyes, jaws, and the beginnings of a social life.
This is the Spore creature stage, earned. The arms race is real: everyone is hunting,
everyone is being hunted, and the fastest-evolving lineages set the pace.

**Goal.** `Milestone.SocialCapability` — the creature's brain complexity crosses a threshold
enabling learned social behaviour: recognition of individuals, rudimentary communication,
and coordinated group action. This fires the gate to the tribal stage.

**Duration target.** 30–60 minutes — the longest creature sub-stage.

### C3 Core loop — Spore fused with arms-race evolution

**Sense → approach or flee → eat / fight / befriend → evolve → repeat.**

Every encounter is a read: size comparison (can I eat this?), parts read (does it have spikes?
toxin sac? speed parts?), behaviour read (is it fleeing or stalking?). Build identity shapes
which reads you make and how you answer them.

**Active parts (C3 editor unlocks):**
| Part category | Examples | Gameplay function |
|--------------|---------|-------------------|
| **Locomotion** | Jointed legs (walk/run), fins (swim), wing buds (glide) | Speed, terrain access |
| **Sensory** | Compound eye (wide FOV), eyespot cluster (night vision), antennae (chemosense range) | Detection, tracking |
| **Feeding** | Mandibles (fast bite), filter fan (passive), proboscis (fluid drain), radula (scrape) | Diet determines biome food access |
| **Offense** | Claws, spine strike, toxin gland, ram head | Combat, prey capture |
| **Defense** | Exoskeleton plate, spines, ink cloud, mimicry pattern | Predator deterrence |
| **Social** | Colour-change chromatophores, sound organ, bioluminescent patch | Communication, recognition |

**Social behaviour (new in C3):**
- **Befriend** — repeated non-aggressive contact with another species builds an association.
  No dialog box; you approach, mirror body posture, perform a display (colour flash, sound).
  The other creature's reaction (approach / flee / ignore / reciprocate) is the feedback.
- **Pack hunting** — befriended creatures follow and assist in hunts. Share kills.
- **Territory** — larger creatures claim feeding zones and will confront intruders.
- **Mating display** — sexual recombination from S4 continues; the display mechanic is the
  social wrapper. Two compatible creatures perform a display and offspring inherit recombined
  traits.

**Predation arms race:** C3 has the first true ecological pressure loop — herbivore bodies
evolve alongside plant-equivalents; predator bodies evolve against prey bodies; prey bodies
evolve against predators. The `SpeciesRegistry` tracks which builds are dominant at any
time, biasing which AI creatures the pool spawns.

### Gate → Tribal stage

`Milestone.SocialCapability` fires when:
1. Brain complexity threshold reached (visible as pronounced head structure with cerebral
   region visible through translucent skull skin).
2. At least one learned social bond formed with another species (AI or player) that has
   persisted for > 3 minutes.
3. Species population ≥ 5 living individuals in the world simultaneously.

The gate signal: a new behaviour emerges spontaneously — the player's creature uses an object
in the environment as a rudimentary tool (drags a stone, uses a stick to probe). No UI prompt.
The first time it happens it just happens. That's the signal.

**Alien generalization.** The C0–C3 sub-stage sequence adapts to `liquidType` and planet
parameters. Ammonia worlds may lack cnidarians entirely (different chemistry for nerve signal
propagation) and jump from C0 directly to a bilateral form. Methane worlds produce entirely
different body plan archetypes — slow, buoyant, antifreeze-chemistry based.

---

## Design payoffs this map unlocks

- **C0 earns C1's locomotion.** The passive sponge phase makes the first pulse of a jellyfish
  bell feel like a breakthrough, not a tutorial.
- **The nerve net earns the brain.** C1's sluggish, radial reactions make C2's directed
  cephalized movement feel like a revolution. The player experiences what a brain *does*.
- **Cambrian explosion is a player-driven event.** The diversity of C3 body plans is partly
  the accumulated result of S0–C2 evolution across all players on the planet. No two planets'
  C3 ecosystems look the same.
- **Social behaviour is earned, not given.** The befriend/fight mechanic only exists in C3
  because the brain complexity milestone gates it. Social play feels meaningful because it
  wasn't always possible.

---

## Open questions

- Does C0 have any locomotion at all, or is it fully sessile? (Placozoa crawl at ~1 mm/min —
  could include a near-zero-speed crawl as the only C0 movement.)
- How does the creature stage editor handle parts that are evolutionarily unlocked (function)
  vs cosmetically placed (appearance)? Needs a clear UX model.
- What is the creature stage's equivalent of the living gene pool picker UI? (Same principle —
  pick a living variant — but the variants are full creature bodies, not cells.)
- Does the tribal gate require land access, or can C3 creatures become tribal in the water?
  (Real answer: tribal behaviour appears in marine creatures — octopus problem-solving, dolphin
  culture — so water-tribal is valid.)

---

## Sources
- [Ediacaran origin and diversification of Metazoa (ResearchGate)](https://www.researchgate.net/publication/385813355_Ediacaran_origin_and_Ediacaran-Cambrian_diversification_of_Metazoa)
- [The Cambrian explosion (UC Berkeley)](https://evolution.berkeley.edu/the-cambrian-explosion/)
- [Early metazoan life: divergence, environment and ecology (Phil. Trans. R. Soc. B)](https://royalsocietypublishing.org/rstb/article/370/1684/20150036/22702/Early-metazoan-life-divergence-environment-and)
- [Early animal evolution and the origins of nervous systems (Phil. Trans. R. Soc. B)](https://royalsocietypublishing.org/rstb/article/370/1684/20150037/22712/Early-animal-evolution-and-the-origins-of-nervous)
- [Evolution of nervous systems (Wikipedia)](https://en.wikipedia.org/wiki/Evolution_of_nervous_systems)
- [Evolution of centralized nervous systems (PNAS)](https://www.pnas.org/doi/10.1073/pnas.1201889109)
- [What sparked the Cambrian explosion? (Scientific American)](https://www.scientificamerican.com/article/what-sparked-the-cambrian-explosion1/)
- [Bangiomorpha and the origin of eukaryotic photosynthesis (ResearchGate)](https://www.researchgate.net/publication/321692487_Precise_age_of_Bangiomorpha_pubescens_dates_the_origin_of_eukaryotic_photosynthesis)
