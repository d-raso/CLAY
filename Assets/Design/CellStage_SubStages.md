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
| S3 | **Proto-Eukaryote** | Cytoskeleton, primitive phagocytosis, endosymbiosis, nucleus formation | Proto-agar.io + endosymbiosis climax | nucleus formed |
| S4 | **Eukaryote FFA** | Full phagocytosis, nucleus, active parts, meiosis | **Main agar.io arcade (long)** | apex + colony achievement |
| S5 | **Colony** | Volvox, choanoflagellates, multicellularity | Co-op / clonal body, graduation | graduate → creature stage |

The deadline catastrophe (`PlanetaryParameters.md` §5) can fire at any sub-stage; the famous
real one — the **Great Oxidation Event** — is a *player-driven* catastrophe that emerges at S2.

---

## S0 — Protocell (Molecular phase)  · *abiogenesis*
**Science.** Life's precursors: monomers (amino acids, nucleotides, fatty acids, sugars) formed
by prebiotic chemistry (Miller–Urey 1953), delivered by carbonaceous meteorites, or synthesised
at hydrothermal vents. Earliest physical evidence of life: **3.8 billion-year-old biogenic
hematite** in the Nuvvuagittuq Greenstone Belt, Canada — iron chemistry driven by early microbes,
preserved in banded iron formations. Fatty acids spontaneously self-assemble into **lipid vesicles**
that can grow and divide (Szostak). **Wet–dry tidal cycling** concentrates monomers on mineral
surfaces — literally our tide-pool setting. **Montmorillonite clay** catalyses RNA strand
formation up to 50 nucleotides long (Ferris) — mapped to the Clay Mineral Shelf biome seabed.
**Pyrite surfaces** catalyse peptide and RNA precursor chemistry (Wächtershäuser iron-sulfur
world) — mapped to the Pyrite/Iron Seep seabed. **Alkaline hydrothermal vents** (Lost City type):
hydrogen-rich fluid, 40–90 °C, porous mineral walls provide natural compartments and proton
gradients (Lane/Russell) — the metabolism-first counterpart to the gene-first RNA world.
A 2026 *Science* paper demonstrated a **45-nucleotide self-synthesising ribozyme (QT45)** that
can catalyse its own replication and produce its complement — direct experimental support for
the RNA-world S0→S1 gate. Modern consensus: a **hybrid multi-stage pathway** combining clay
catalysis, wet–dry cycling, vesicle formation, and RNA emergence is more likely than either
genes-first or metabolism-first alone.

**Fantasy.** You are a fragile lipid bubble adrift. Absorb free-floating monomers, grow your
membrane, divide when full.

**Core mechanic.** Resource gathering + **membrane-integrity survival** (no predation):
- Hazards: UV in clear shallows, hydrolysis, pH/temperature swings (read from biome chemistry).
- Wet–dry tension: low tide concentrates food but exposes you to UV/desiccation.
- Reproduce by fission once you bank enough material.

**Gate → S1.** Encapsulate a self-replicating polymer.
**Alien generalization.** `liquidType` (water/ammonia/methane) sets which monomers exist and
which hazards bite (e.g., methane worlds: cold-stable, different solvent chemistry).

### S0 Gameplay additions (2026-10-01)

**Seabed manual assembly.** The mineral surface of the tide pool floor is where prebiotic
chemistry actually concentrates and catalyzes base pairing — clay, pyrite, and carbonate
surfaces in real origin-of-life theory. The camera can zoom down into your protocell's
immediate seabed and the player can **physically drag and place monomers** onto the mineral
surface to attempt chains. A viable chain glows and pulses; an invalid one does nothing. This
is still discovery-only (no labels, no objectives) — you learn what works by doing it. The
seabed surface type varies by biome and changes how assembly feels (see biomes below).

**Collaborative S0 → S1 transition (3 victors).** S0 does not graduate individually. When a
threshold number of protocells in the pool successfully encapsulate a self-replicating polymer,
the pool collapses: the **3 most genetically distinct surviving lineage templates** become the
founders of S1, and every player in the pool respawns as a member of one of those 3 lineages
(living gene pool — same mechanic as `CellStage_Decisions.md` §3). This makes S0 a collective
bootstrap — every player is working to prime the same primordial soup. Players who do not want
to transition can opt into the **virus path** instead (see below).

**S0 virus path.** At any point in S0, a player whose membrane has burst repeatedly, or who
simply chooses it, can shed their lipid membrane and become a **naked replicating polymer** — a
viroid. This is a full alternative gameplay mode, not a punishment:
- *Drift* — extremely small, carried by currents, no energy management but UV and harsh
  chemistry damage you in open water. You must reach a host quickly.
- *Absorption* — you cannot enter a protocell actively; position yourself near feeding
  protocells and ride currents to be absorbed passively alongside monomers.
- *Internal replication* — once inside, copy yourself using the host's monomer supply. The
  host's membrane shimmers and behaves erratically (visible state change, cause unknown to host).
- *Hijack slider* — as you infect more of the species' living bodies, you get increasingly
  strong random spurts of control over infected protocells (see `CellStage_Decisions.md` §4).
- *Lyse or lie dormant* — burst the host to scatter copies, or stay integrated through the
  S0→S1 transition to become a permanent part of the founder lineage (endogenous viroid).
Biome effects on viral play are covered in the biome table below.

---

### S0 Biomes

Each tide pool's biome is derived from the same planetary parameters that drive `PlanetTexture`
and `FloraGeneration`. **The generation principle is the same:** causal factors (liquidType,
volcanism, temperature, salinity, acidity, star spectrum) produce a `TidePoolBiomeGenome` that
drives mineral formation shapes, seabed texture, water color, and particle chemistry — just as
`GenerateGenome(env, rng)` produces a plant genome from the same environment vector. The
consistency contract from `FloraGeneration.md` §7 applies here too: **the inside of a tide pool
must visually agree with its planet's surface color and chemistry.** A rust-red iron world has
iron-seep tide pools; a methane world has orange-haze pools; a carbonate world has white mineral
towers.

Mineral formations and seabed structure reuse the same noise seed as `PlanetTexture.ComputeSurface`
at a microscale (different frequency, same base seed), so the geology of the pool floor is
continuous with the planet's terrain — it literally is a small piece of that terrain.

| Biome | Planetary drivers | Look & atmosphere | Seabed surface | Monomer profile | Hazards | Viral play |
|---|---|---|---|---|---|---|
| **Warm Hydrothermal Vent Field** | high `volcanism`, deep zone, iron/sulfur crust | dark water, glowing white/black chimneys, mineral plumes, bioluminescent tint | porous iron-sulfide crust; monomers trapped in crevices, pulled out and assembled on the surface | sulfur amino acids, iron-rich nucleotides | thermal pulses from vent surges, acidity spikes | RNA degrades fast in heat — must find a host immediately after lysing |
| **Alkaline Hot Spring Pool** *(Lost City type)* | moderate `volcanism`, carbonate-rich crust, mid temp | white/cream mineral towers, milky upwelling water, gentle convection currents | smooth layered carbonate — the ideal flat workbench for base assembly | fatty acids in abundance (easy membrane formation), RNA precursors | pH gradient — stray from the vent source and chemistry destabilizes your membrane | stable RNA; many hosts clustered near vents — prime hunting ground |
| **Sunlit Tidal Flat** | moderate `insolation`, shallow `liquidCoverage`, wet-dry cycling | clear shallow water, visible sandy/rocky bottom, strong caustics, visibly exposed rock during low tide | sandy mineral bed with patches of feldspar/quartz — monomer concentration spikes during dry phase | broad monomer mix; richest during low-tide dry phase | UV during low tide, desiccation if your membrane is small | moderate; hosts abundant but dispersed; UV damages free viroid between lysings |
| **Cold Brine Pool** | low temp, high `salinity`, outer HZ or brine `liquidType` | dense pink/orange liquid (halophile-colored), slow particle movement, sharp halocline boundary | salt crystal formations — monomers crystallize onto faces, chipped off and assembled | sparse but extremely stable — long half-life, slow chemistry | osmotic stress: too much brine contact shrinks your membrane | slow drift, long host search times, but viroid RNA very stable — patient play |
| **Clay Mineral Shelf** | sediment-rich runoff, moderate temp, shallow, low `volcanism` | soft grey-brown flat floor, fine silt suspension, amber-tinted water, almost calm | smooth clay — richest catalytic surface, all monomer types adsorbed; the most legible assembly environment | all types, highest density | almost none (the "easy" biome) — silt clouds reduce visibility for larger organisms | clay adsorbs viroid RNA — slightly harder to drift off surfaces once absorbed by a host |
| **Pyrite / Iron Seep** | high iron/sulfur in crust, reducing atmosphere, low O₂ | yellowish sediment, rusty water tint, metallic bubble streams, iron-crystal formations | pyrite crystal clusters — monomers bind directionally to crystal faces; assembly orientation matters | iron-catalyzed peptides, sulfur-heavy bases | redox chemistry — if player-driven GOE is underway, O₂ pockets here become lethal | average conditions; hosts concentrated near mineral seeps |
| **Ammonia Tide Pool** | cold planet, ammonia `liquidType` | blue-violet liquid, slow sluggish movement, ammonium carbonate crystal formations, cold-haze aesthetic | ammonium carbonate flats — slow and slippery | nitrogen-rich, different base set; ammonia-stable membranes only | temperature swings hit harder; ammonia ice phase is lethal | very slow drift; viroid chemistry is different — ammonia-stabilized RNA analogs |
| **Methane Shallows** | methane `liquidType`, very cold, reducing atmosphere (Titan-type) | orange-haze liquid, slow drifting hydrocarbon droplets, dark sky above, haze particles in suspension | hydrocarbon sand — soft, shifting; monomers embedded in tar-like substrate | azotosome-compatible membranes; very sparse but extremely stable; slowest chemistry | no UV (protective), but extremely slow processes — your membrane forms in slow motion | ultra-slow drift; hosts rare and spread out; long infection cycles |
| **Cryovolcanic Splash Pool** | tidal heating + icy moon type (`Europa`/`Enceladus`), subsurface ocean | icy crater walls, periodic geyser eruptions reshaping the pool, ice crystals in suspension, steamy upwellings | fresh ice with embedded mineral grains from the interior — constantly resurfaced by eruptions | monomers arrive in pulses during geyser events; scarce between | eruptions themselves dangerous; sudden dilution of pool chemistry between pulses | geyser pulses flush free viroids and scatter them; time lysing to ride the pulse |
| **UV-Bleached Rock Crevice** | high `flareActivity`, low `magneticField` (M-dwarf / flare world) | fractured pale rock, harsh direct light that pulses with flares, safe dark crevices in rock shadow | fractured basalt — assembly happens inside crevices, cramped but shielded from radiation | photochemically generated — burst of monomers after a flare, but the flare damages open membranes | radiation pulses; safe zones only in rock shadow | UV destroys free viroid in open water; must be inside a host or in shadow during flares |

**Generation note.** The `TidePoolBiomeGenome` struct (analogous to `PlantGenome`) is built
by `GenerateTidePoolBiome(planetData, tidePoolLocation, rng)` and drives: water color/tint,
particle type and density, seabed mesh parameters (noise frequency, rock/crystal/clay/sand
blend), mineral formation archetype (chimney / tower / crystal cluster / flat / crevice), and
chemistry values passed to `PlanetaryEnvironment` (temperature, acidity, salinity viscosity).
The same `ShiftHSV` + noise seed used by `PlanetTexture` should be reused so pool and surface
are visually coherent.

## S1 — Replicator (Genetic phase)  · *RNA world* · still solo

**Science.** The **RNA world**: short RNAs act as both genetic information and catalyst
(ribozymes), self-replicating before DNA/protein existed. Real tension we can gamify: the
chemistry that stabilizes fatty-acid membranes tends to *inhibit* RNA replication (Szostak) —
survival vs reproduction pull against each other. Possible pre-RNA polymers (PNA/TNA) = flavour.
**LUCA** (Last Universal Common Ancestor) lived ~**4.2 billion years ago** (Moody et al. 2024,
*Nature Ecology & Evolution*) — earlier than previously thought, implying life took hold fast
after the Late Heavy Bombardment subsided. LUCA was no simple molecule: it had a genome of at
least **2.5 megabases (~2,600 proteins)**, comparable to many modern bacteria. It was an
**anaerobic acetogen** (lived without oxygen, made acetate as a waste product), and critically,
it already had an **early immune system engaged in an arms race with viruses** — meaning the
virus path (`CellStage_Decisions.md` §5) is not a late-game novelty but was a genuine pressure
from life's very first days. LUCA was cellular, with a lipid bilayer, DNA, RNA, and proteins
all functioning together — the state S1 graduates toward.

**Fantasy.** You now carry a self-copying molecule — a genome. Feed it monomers to replicate;
**mutations are your first evolution currency.**

**Goal.** Evolve a complete, self-sustaining cell: stable membrane + working metabolism +
integrated ribozyme genome all functioning together simultaneously. That integration event — the
LUCA moment — is the gate to S2. The body visibly looks complete and coherent for the first
time when it fires.

**Core loop: Gather → Process → Allocate → Copy → Select**

1. *Gather* — chemotaxis toward your energy source (chemical gradient signals, light patches,
   organic-rich silt). Your cell drifts toward the signal passively at first; motility genes
   improve it over time.
2. *Process* — ribozymes convert raw molecules from your source into stored energy. Efficiency
   depends on ribozyme quality, which is genome-encoded and mutation-sensitive.
3. *Allocate* — stored energy must be split across three competing needs:
   - **Membrane upkeep** — without it the membrane thins and eventually bursts. Visible as
     membrane shimmer and wrinkling.
   - **Ribozyme synthesis** — improve your metabolic efficiency over time by building better
     ribozyme chains. Visible as interior glow intensifying.
   - **Genome copying** — spend energy to replicate. The only way to spread your variants.
   You cannot max all three simultaneously. This allocation is the core strategic tension of S1.
4. *Copy* — spend energy to replicate your genome. Choose speed: fast = more offspring + more
   mutations; slow = fewer offspring + stable genome. No labeled slider — you feel it as
   resource drain and offspring frequency.
5. *Select* — mutations manifest as visible body changes in offspring (colour, shimmer,
   interior pattern). Bad variants starve or burst and leave the gene pool; good ones spread.
   You can respawn into thriving variants (living gene pool, `CellStage_Decisions.md` §3).

**Energy sourcing** (biome-dependent, genome-encoded via `Genome.metabolism`):

| Source | Biomes | Science | Gameplay |
|--------|--------|---------|----------|
| Organic fermentation | all biomes | Break down prebiotic S0-era organic molecules | Universal, low efficiency. The fallback. |
| Proton gradient / chemiosmosis | hydrothermal vent, alkaline hot spring | Lane/Russell theory: chemical gradient between vent fluid and pool water | High yield, vent-proximity dependent. Likely the first real metabolism. |
| Primitive phototrophy | sunlit tidal flat, ammonia/methane shallows (if enough light) | Early light-harvesting retinal proteins | Biome-locked but passive income once established. Foreshadows S2's metabolism choice. |
| Mineral redox | pyrite/iron seep | Use iron/sulfur chemistry as electron donor | Iron seep biome only; high ceiling if your ribozyme is tuned right. |

Your ribozyme chains (genome-encoded) determine how efficiently you convert each source. Ribozyme
quality mutates — for better or worse — every time your genome copies.

**The membrane vs ribozyme tension (Szostak's constraint).** The fatty acids that stabilize
your membrane compete with genome copying for the same mineral surface catalysts and monomer
supply. Investing in membrane integrity literally draws resources away from replication, and
vice versa. Players who neglect this will have a great genome in a fragile bubble, or a solid
membrane with evolutionary stagnation.

**Interaction with S0 and viroids.** S1 cells can chemically dissolve S0 protocells — their
lipid bilayers are an energy source at S1. No engulfing yet; it's osmotic dissolution at
contact. S0-era viroids can still infect S1 cells. Repeated infection triggers the first
**CRISPR-like immunity** — the real mechanism: viral RNA sequences are cut out and stored as
*spacers* between palindromic repeats in the host genome; future infections with the same
sequence are recognised and destroyed. Visible in CLAY as structural surface markers on the
membrane (the spacer library). LUCA already had this system (Moody et al. 2024), meaning
virus-vs-cell arms races predate the divergence of bacteria and archaea.

**First editor (passive traits only).** Still no meters or labels. The player sees their cell
body and can adjust it — touching the membrane changes membrane genes; dwelling near a vent
activates chemotrophy in offspring; copying slowly visibly raises offspring fidelity. The
effect manifests in offspring bodies, never as numbers. Passive traits only: membrane
toughness, copy speed/fidelity, basic metabolic efficiency, ribozyme length.

**Gate → S2.** Lineage achieves stable membrane + reliable metabolic output from one source
+ at least one working ribozyme, all simultaneously. The LUCA moment fires; the cell visibly
looks integrated and complete.

## S2 — Prokaryote (Metabolic phase)  · *bacteria & archaea* · competitive, NO engulfing

**Science.** The prokaryotic world spans ~2 billion years of Earth's history and is the longest
phase of life. Metabolic diversity: **phototrophy** (light), **chemotrophy/chemosynthesis**
(vents, sulfur/methane), **lithotrophy** (rock). Reproduction by binary fission.
**Horizontal gene transfer** (HGT) — the dominant driver of prokaryotic evolution — lets
bacteria swap genes laterally, including **CRISPR-Cas immune loci**, which are themselves
transferred by HGT (making the arms race between cells and viruses a self-spreading system).
**CRISPR-Cas** is the real name for the repeated-infection immunity that appears in our S1
→ S2 transition: viral RNA sequences are cut and stored as spacers; future infections of the
same sequence are destroyed. Anti-CRISPR proteins evolved by phages counter it — a true
co-evolutionary arms race. **Biofilms** = early cooperation; the oldest definitive biofilm
fossils are **3.5 billion years old** (Dresser Formation, Western Australia).
**Cyanobacteria** evolved oxygenic photosynthesis by ~**3.0 billion years ago** and began
outputting oxygen as metabolic waste. For ~600 million years, dissolved iron in the oceans
absorbed this oxygen — precipitating as **Banded Iron Formations (BIFs)**, visible today as
the red and grey striped rock formations in the Pilbara and Lake Superior regions.
When the iron buffer was exhausted, atmospheric O₂ rose sharply: the **Great Oxidation
Event** (~**2.4–2.0 Ga**) — the first planetary-scale mass extinction, now player-driven.
The resulting atmospheric methane destruction triggered the **Huronian Glaciation**
(~**2.29–2.25 Ga**) — a near-total snowball Earth. In CLAY this can follow the GOE as a
second catastrophe wave on high-methane planets (see PlanetaryParameters).

**Fantasy.** Out-compete by efficiency, not violence. Find your niche, become the best version of
it, and reshape the world's chemistry in the process.

**Goal.** Evolve a stable, dominant metabolism; develop a domain identity (Bacteria or Archaea);
and grow large and complex enough to evolve the first primitive cytoskeleton. That cytoskeleton
is the key to S3 — it enables the crude proto-engulf that makes endosymbiosis possible.

**The key constraint: NO phagocytosis.** The agar.io predation game does not exist yet and
cannot be triggered early. Competition is purely metabolic — who extracts energy fastest from
the shared environment. This makes S2 feel fundamentally different from S4.

---

### S2 gameplay loop: Compete → Specialize → Transfer → Cooperate → Trigger → Adapt

**1. Compete** — efficiency race in your metabolic niche. The pool is a shared resource; the
cells that extract energy fastest from their zone reproduce faster and crowd others out. This
is the first stage where other players are direct competitors for the same food.

**2. Specialize** — your metabolism drifts toward what works where you live. Offspring of cells
that spend time in sunlit water trend toward phototrophy; offspring near vents trend toward
chemosynthesis. No menu: the environment selects. The player biases it by where they spend
time and what genes they pick up via HGT. The available metabolisms:

| Metabolism | Biome gate | Energy yield | Notes |
|------------|-----------|--------------|-------|
| Fermentation | All biomes | Low | Holdover from S1; universal fallback |
| Anoxygenic photosynthesis | Sunlit, low O₂ | Medium | Doesn't produce O₂; safe for anaerobes |
| **Oxygenic photosynthesis** | Sunlit, high insolation | High | **Produces O₂ → drives the GOE** |
| Chemosynthesis | Vent, seep, sulfur | Medium–high | Biome-gated; stable niche, no O₂ risk |
| Lithotrophy | Iron seep, mineral surfaces | Medium | Rock-feeding; unlocks post-GOE iron redox |
| **Aerobic respiration** | Requires O₂ (post-GOE only) | **Very high** | Locked until GOE fires; the big reward |
| Mixotrophy | Flexible | Variable | Generalist; lower ceiling but not O₂-locked |

Alien generalization: available metabolisms come from planetary params — geothermal worlds favor
chemosynthesis; bright/close-orbit worlds favor phototrophy; carbon-rich worlds may unlock novel
pathways not listed here.

**3. Transfer** — **Horizontal Gene Transfer (HGT).** When two cells make sustained contact
(bump and hold), genes can be exchanged laterally. No dialog, no menu:
- Your offspring start showing traits of the cells you've been near (visible body changes).
- The player CAN bias this: seek out high-performing cells with desirable visible traits and
  repeatedly bump them. This is the first active social mechanic.
- Risk: you can also pick up viroid sequences (transduction) — introducing an infection vector.
- Rate varies by biome and domain: bacteria ↔ bacteria HGT is fast; archaea HGT is slower but
  more stable (less chance of disruptive insertions).
- The `PlanetHistoryLedger` tracks cumulative HGT activity per planet for emergent effects.

**4. Cooperate** — **Biofilms.** Cells that remain in proximity long enough secrete a polymer
matrix and stick together visibly. Within a biofilm:
- Resources are shared between members (a member with surplus feeds adjacent starving members).
- Chemical hazards (UV, acid, temperature swings) are damped — the matrix absorbs them.
- HGT is dramatically faster (constant proximity = constant bumping).
- You move slower and are larger — a liability when S3 engulfers eventually appear.
- Mild cell specialization emerges: some biofilm members trend toward reproduction, others
  toward matrix secretion. Visible as slight hue and form divergence within the cluster.
- Biofilm is the proto-colony mechanic. It foreshadows S5 directly.

**Bacteria vs Archaea domain fork.** Genome.domain drifts under selection pressure:
- **Bacteria** (`Domain.Bacteria`): faster binary fission, higher HGT rate, more metabolic
  versatility, thinner membrane. Better at dominating open, resource-rich zones.
- **Archaea** (`Domain.Archaea`): tougher ether-linked membrane (better extreme tolerance),
  slower but more precise copying (lower mutation rate), proto-cytoskeleton genes accumulate
  faster (Asgard archaea lineage). **Better positioned for S3** — endosymbiosis requires an
  archaeon host. Archaea are also more resistant to the GOE oxygen toxicity.
- Domain is not chosen. It drifts via mutation and environment. Players who seek out extreme
  biomes (vents, brines, cryovolcanic pools) tend toward Archaea; players in open sunlit water
  trend Bacteria. Both can reach S3; the Archaea path to S3 is shorter.

**5. Trigger — the Great Oxidation Event (GOE).**
The `PlanetHistoryLedger` tallies O₂ output from all cells running oxygenic photosynthesis.
When cumulative O₂ crosses the planet's chemistry threshold:
- **Visible telegraphing** (gradual, not instant): water colour shifts toward blue-clear (iron
  rusts out of suspension), orange/rust mineral staining appears on the seabed, particle
  chemistry changes, some cells visibly struggle and die.
- **Anaerobe die-off**: cells with low `oxygenTolerance` take increasing damage. Cells running
  oxygenic photosynthesis without tolerance self-poison (Decisions §6 setback).
- **Sky and biome shift**: on the planet surface, sky colour changes. New biome zones open
  (oxygenated open water = new habitat).
- **Aerobic respiration unlocks** planet-wide — the highest-yield metabolism in the game becomes
  available for the first time. Cells that evolved O₂ tolerance early can immediately exploit it.
- The GOE is the defining event of S2. On some planets it fires early (aggressive players); on
  others it fires late or not at all (vent-dominated, low-insolation worlds).

**6. Adapt — survival and opportunity.** After the GOE:
- Players who pre-evolved O₂ tolerance switch to aerobic respiration and dominate.
- Players who didn't must rapidly evolve tolerance or migrate to anaerobic niches (vents, deep
  zones where O₂ hasn't reached).
- The pool's competitive landscape resets — a new dominant niche opens, and whoever fills it
  first has the reproduction advantage going into S3.

---

### S2 biomes (same pool, new competitive dynamics)

The S0/S1 biomes persist into S2 but their role shifts: they are now **metabolic niches** that
cells compete to dominate, not just hazard environments.

| Biome | S2 dominant metabolism | GOE effect |
|-------|----------------------|------------|
| Sunlit tidal flat | Oxygenic photosynthesis (primary O₂ source) | Becomes the most changed: O₂ bubbles, rust staining, anaerobe die-off most visible here |
| Warm hydrothermal vent field | Chemosynthesis | Mostly unaffected (anoxic near vent); becomes a refuge for anaerobes post-GOE |
| Alkaline hot spring | Chemosynthesis / anoxygenic photo | Minor effect; O₂ penetrates slowly |
| Cold brine pool | Fermentation / lithotrophy | Brine slows O₂ penetration; late GOE impact |
| Clay mineral shelf | Fermentation (most diverse HGT zone) | Strongly affected; rusty clay color shift |
| Pyrite/iron seep | Lithotrophy → iron oxidation post-GOE | GOE transforms this biome: FeS₂ → Fe₂O₃, new energy landscape, color from yellow to rust |
| Cryovolcanic splash pool | Chemosynthesis | Eruption pulses may dilute O₂ locally; partial refuge |
| UV-bleached rock crevice | Anoxygenic photo (sheltered) | UV + O₂ doubly hostile after GOE; strongest selection pressure for O₂ tolerance |

---

### Gate → S3

Three conditions, all visible on the body:
1. **Domain maturity**: `Domain.Archaea` (or convergently similar complex genome in Bacteria —
   harder path, but possible). The cell visibly looks more complex: thicker membrane, more
   internal structure.
2. **Cell size**: binary fission has grown the lineage to a large enough cell that primitive
   cytoskeletal proteins can be supported energetically.
3. **First proto-engulf**: a mutation causes the membrane to briefly indent and wrap around a
   small adjacent cell — not a full engulf, just a momentary dimple. The player sees this happen
   once. That is the S3 gate opening.

**Alien generalization.** Available metabolisms, GOE threshold, and domain-fork pressure all
derive from planetary params: geothermal worlds favor chemosynthesis + archaea; bright worlds
favor oxygenic photosynthesis + fast GOE; low-insolation worlds may never trigger a full GOE
(phototrophy underrepresented); methane/ammonia worlds have entirely different metabolic trees.

## S3 — Proto-Eukaryote  · *Margulis + cytoskeleton* · first predation, endosymbiosis climax

**Science.** The eukaryotic cell is a **biological chimera** — its nucleus and information
machinery derive from an archaeon; its mitochondria and (separately) chloroplasts from bacteria.
The archaeal host was almost certainly an **Asgard archaeon** close to **Heimdallarchaeia** —
the lineage phylogenomically most related to eukaryotes. A 2025 *mBio* paper describes cultured
Asgard cells with **irregular protrusions and extended membrane processes** — physical features
close to the prokaryote-eukaryote boundary — making the S3 crawling/amoeboid movement style
directly grounded in what real Asgard cells look like. **Eukaryotes emerged ~3.0–2.25 Ga**;
the **LECA** (Last Eukaryotic Common Ancestor) already had a mitochondrion, meaning the
endosymbiosis event happened **exactly once** in Earth's history — every living eukaryote
descends from that single capture. The endosymbiosis may not have begun through phagocytosis:
the **syntrophic (E³) model** proposes the host and proto-mitochondrion first formed a
**metabolic partnership** (H₂ and CO₂ exchange) before becoming irreversibly interdependent.
The **stillness-to-retain** mechanic honours this: chemical interdependence, not violent capture,
is what made the partnership permanent. The nucleus formed slightly later, likely as the genome
ballooned from endosymbiotic gene transfer — a nuclear envelope protecting and organising
increasingly unwieldy DNA. *(We treat nucleus formation as the S3→S4 gate.)*

**Fantasy.** You enter S3 as an amoeboid crawler — slow, irregular, but finally able to eat
other cells. You hunt, you engulf, you grow. Then one engulf is different: the prey glows
inside you, and if you hold still, it stays. Your genome bloats as the symbiont's genes
migrate inward. A membrane forms around your DNA. You are becoming something new.

**Goal (graduation milestone).** `Milestone.NucleusFormed` — the nuclear envelope closes
around the host's genome. This happens naturally once EGT has added enough symbiont genes
that the genome reaches `NuclearThreshold`. It is not triggered manually.

**Duration target.** 10–20 minutes — a moderate arcade phase. S4 is the long one.

### S3 Gameplay loop

S3 has a **sustained predation loop** with one transformative event inside it.

**Phase A — Crawl and hunt (proto-agar.io).**
You enter with the proto-engulf dimple active. Cytoskeleton is sparse but functional.
Movement is **amoeboid crawl** — slower than S2 chemotaxis, but more directional and
responsive to input. You can eat cells smaller than your engulf radius; they give raw energy
and grow your area. This is the first time the game feels like agar.io, but constrained:
- No active parts yet (flagella dash, toxin sac, spike — those are S4).
- Engulf is slow (2–4 second membrane deformation per prey).
- You can be engulfed by larger proto-eukaryotes in the pool.
- HGT from S2 continues at low rate; more actin/tubulin genes from bacterial neighbours
  slowly increase cytoskeletal density and engulf speed over the course of the phase.

Energy from eating goes into growth (area) and cytoskeletal development. The more you eat,
the faster your engulf becomes. This is the arcade incentive to hunt actively.

**Phase B — The endosymbiosis (the climax engulf).**
Among the prokaryotes in the pool, a small number are **symbiont candidates** — energy-dense
cells that emit a faint glow (glow = high internal energy density). Player cells that are
small and high-energy also glow (co-pilot path, see below). These are not labelled.

When you engulf a symbiont candidate, the phagosome behaves differently: the prey keeps
glowing inside you rather than going dark. The default behaviour is digestion (prey dims
quickly). To *retain* it, the player must **stop moving and hold position** — stillness
suppresses the lysosome pathway. This is grounded in how real intracellular bacteria
(Legionella, Chlamydia) exploit host signalling to survive inside phagosomes.

Once retained, **endosymbiotic gene transfer (EGT)** begins: a brightening pulse radiates
outward from the symbiont. Energy budget doubles over ~30 seconds. The symbiont shrinks
as it loses autonomy but remains a permanent internal glow — the proto-organelle. The host
grows visibly. This is a one-time event per cell; you cannot retain a second symbiont.

**Phase C — Genome bloat and nucleus formation.**
EGT adds the symbiont's genes to the host genome. Combined with HGT from S2 and ongoing
mutation, the genome reaches a density where free-floating DNA becomes unstable — offspring
start showing higher mutation rates, and the cell's central region brightens as DNA condenses.
The nuclear envelope then forms spontaneously: a visible membrane closes around the bright
central region in a slow pulse. This is not a reward screen or a cutscene — it just happens,
like the proto-engulf dimple mutation did at the end of S2. The cell's interior now has a
distinct lit nucleus. `Milestone.NucleusFormed` fires. Gate to S4 opens.

**What if you never find a symbiont candidate?**
You can stay in Phase A indefinitely, eating prokaryotes for energy. The genome still grows
from HGT and mutation. At sufficient genome complexity, the nuclear envelope forms anyway —
but without the symbiont's energy boost, you enter S4 with a smaller energy budget and no
organelle branch. Valid but harder. The S4 prototype's CellBiomass system handles this gracefully
(no symbiont = no branch-specific bonus, but otherwise full agar.io continues).

### Symbiont branches (expanded)

| Symbiont branch | Real analog | Biome pressure | S4 gameplay identity |
|-----------------|-------------|----------------|----------------------|
| **Mitochondrion** (aerobic) | alphaproteobacterium | post-GOE O₂-rich worlds | Fastest energy burst; can sprint-engulf; needs ambient O₂ |
| **Chloroplast** (photosymbiont) | cyanobacterium | high insolation, low organic density | Passive energy in light zones; slower in dark/deep; can photosynthesize while stationary |
| **Chemosymbiont** | vent mussel/tubeworm bacteria | volcanic, sulfur/methane-rich, low O₂ | Thrives near vents, toxic to most; vent proximity = energy bonus |
| **Radiosymbiont** (melanin) | *Cladosporium sphaerospermum* (Chernobyl) | high stellar flare, radiation belts | Radiation converts to energy; immune to rad setbacks; rare worlds only |
| **Thermosymbiont** | (speculative) | strong thermal gradient, early planets | Energy from heat differentials; fastest in hot biomes, sluggish in cold |

Branch is set at integration step — the identity of the retained symbiont determines it.
There is no menu. You acquire the branch by engulfing the right kind of cell in the right
biome. A player can end up with a mismatched branch (e.g. mitochondrion on a low-O₂ world)
and play through S4 at a permanent disadvantage — valid but harder.

### Co-pilot mechanic (player endosymbiont)

When a **player cell** is engulfed by another player and chooses to integrate rather than
fight free, a unique co-pilot relationship begins (see `CellStage_Decisions.md` §4).

**Host player experience:**
- A new input action unlocks: **Organelle Burst** (e.g. hold Q). Triggers the symbiont
  player's primary ability on demand.
- A subtle pulse from inside the cell signals the symbiont player's activity.
- If the host dies, the symbiont is freed as a small prokaryote (their lineage continues).

**Symbiont player experience:**
- View shifts to a soft interior perspective — the cell's membrane as the horizon, organelles
  as landscape features, the nuclear region glowing at centre.
- One primary action: **Energy Burst** — charge and release ATP to the host. Holding the
  button builds charge; releasing fires it. The host visually accelerates or regenerates area.
- One directional nudge: tiny movement inputs create a bias on the host's steering (they
  feel it as a gentle pull). Communication is emergent; there are no chat bubbles.
- When the host stabilises and gates to S4, the symbiont player's score/lineage merges
  with the host's; they become a full member of the host species (Decisions §4, 40% rule).
  They can continue co-piloting in S4 or respawn into an independent cell of the merged species.

**AI symbiont:** integration is automatic; no co-pilot. The energy budget simply doubles.

**Fight-free:** if the engulfed player chooses to fight free, the host's membrane shimmers
and ruptures locally. The escaping player takes 30% area damage; the host's phagosome
collapses, wasting the engulf attempt. Both recover. No lasting penalty — the design
makes integration attractive but never forces it.

### S3 biomes — which symbiont the environment pressures you toward

| S2 biome inherited | GOE outcome | Most available candidate | Pressure toward |
|--------------------|-------------|--------------------------|-----------------|
| Alkaline hydrothermal vent | Low O₂ (pre-GOE common) | sulfur-reducing bacteria | Chemosymbiont |
| Black smoker | Low O₂ | thermophilic archaea | Thermosymbiont |
| Cryovolcanic vent | Variable | cold-tolerant chemoautotrophs | Chemosymbiont or Thermosymbiont |
| Photic shallow | Post-GOE | cyanobacteria abundant | Chloroplast |
| Iron-rich deep ocean | Post-GOE | alphaproteobacteria (O₂ users) | Mitochondrion |
| High-radiation | Variable | melanin-rich bacteria | Radiosymbiont |
| Low-insolation tide pool | Low light | chemotrophs | Chemosymbiont |
| Evaporative hypersaline | Pre-GOE | halophile bacteria | Chemosymbiont or Mitochondrion |
| Sulfuric acid aerosol | Alien | acid-stable chemotrophs | Chemosymbiont |
| Methane-ammonia | Alien | cold-chemistry microbes | Thermosymbiont or Chemosymbiont |

Biome does not *force* a branch — if the player hunts the right candidate it is always
theoretically available. Biome just controls population density, making the
world-appropriate branch easy to find and the alien branch rare.

### S3 body visual language (discovery-only rule maintained)

| Internal state | Visible signal |
|----------------|----------------|
| Cytoskeleton sparse (Step 1–2) | Near-circular shape, smooth membrane |
| Cytoskeleton developing | Irregular outline, internal shimmer deepens |
| Cytoskeleton functional | Amoeboid crawl movement, clear dimple visible |
| Symbiont integrating | Brightening pulse radiating outward from interior |
| Integration complete / stable | Distinct internal glow at organelle site; cell permanently larger |
| Energy burst (co-pilot firing) | Brief flare from organelle site, speed spike |
| Organelle stress (poor biome match) | Organelle glow flickers, cell shrinks slightly |

### Gate → S4

`Milestone.NucleusFormed` fires when genome complexity crosses `NuclearThreshold` (EGT genes
+ HGT genes + mutation accumulation). No UI announcement. The gate signal is the nuclear
membrane closing visibly around the cell's bright central region — players who see it know
something permanent just happened. The cell then enters S4 with whatever symbiont branch it
acquired (or none, if it skipped Phase B).

Secondary milestone still tracked: `Milestone.Endosymbiont` fires at the moment of successful
symbiont retention in Phase B, separately from the nucleus gate. This drives the co-pilot
merge and the branch assignment, and is recorded in `CellStageProgress` even if the player
later dies before reaching nucleus formation.

**Alien generalization.** All five symbiont branches exist on every `liquidType` world
but at different base abundances driven by the planet's insolation, volcanism, radiation
flux, and O₂ history. On ammonia worlds, the mitochondrion analogue uses a different
electron acceptor (nitrogen compounds); the gameplay loop is identical, the visual palette
and chemistry labels differ. The branch table's "favored on planets with" column is the
only thing that changes per-planet.

## S4 — Eukaryote FFA  · *full phagocytosis* · **the long arcade core**

**Science.** With nucleus sealed, cytoskeleton mature, and symbiont supplying energy, the
eukaryotic cell unlocks true phagocytosis — fast, fluid engulfing of cells up to ~80% of
your own size. Endomembrane trafficking enables **active parts**: flagella, cilia, secreted
toxins, membrane spikes. Cell size can now scale far beyond any prokaryote. Sexual
recombination (meiosis) appears, scrambling genomes and accelerating evolution. The
predator/prey arms race properly begins.

**Fantasy.** This is the main event. Full agar.io predator/prey fused with Spore's
**parts as active abilities** — but earned, not given at the start. The symbiont branch
from S3 defines your build identity here. Duration target: **30–60 minutes**.

**Core loop.** Size-gated engulfing is the engine. Everything else is a modifier:
- **Active parts** — flagella (speed burst), membrane spike (damage on contact), toxin sac
  (AoE slow), cilia (current generation), electroreceptors (sense nearby cells through
  obstacles), photosynthetic membrane (passive light energy while stationary).
- **Branch bonuses** — mitochondrion = sprint-engulf burst; chloroplast = light-zone regen;
  chemosymbiont = vent-proximity bonus; radiosymbiont = radiation immunity; thermosymbiont
  = heat-gradient speed boost.
- **Living gene pool respawn** — same mechanic as S0–S3: on death, pick a living variant
  of your species. Drift and selection continue through S4 deaths.
- **Sexual recombination** — two cells of compatible genome can merge briefly and split into
  offspring with recombined genomes (meiosis-lite). Opt-in mechanic; offspring carry traits
  from both parents.
- **Biome traversal** — the pool has zones (deep/shallow, vent proximity, light/dark,
  radiation band) with different resource density and hazard profiles. Moving between zones
  is risk/reward.

*(This is the gameplay in `CellStage_BuildPlan.md` Phases 1–4 and the existing `_Scripts/Gameplay`
prototype — CellBiomass, Engulfment, AIController, EvolutionNodes.)*

**Gate → S5.** Reach size apex AND trigger the colony-formation achievement gate (TBD:
candidate = complete one proto-adhesion event, where two of your division offspring fail to
separate and remain attached).

## S5 — Colony (Multicellularity)  · *Volvox / choanoflagellates / Dictyostelium* · social + graduation

**Science.** Multicellularity arose **independently at least 25 times** in eukaryotes
(Grosberg & Strathmann) — it is not a rare accident but a convergent solution to the limits
of single-cell life. Real model organisms:

- **Volvox** (clonal): up to 50,000 cells, strictly divided into sterile somatic cells
  (flagella, motility) and reproductive gonidia. Somatic cells are genetically identical to
  the germ cells but will never reproduce — a true altruistic sacrifice. The evolutionary
  series from *Chlamydomonas* (unicellular) → *Gonium* (8 cells) → *Eudorina* → *Pleodorina*
  → *Volvox* shows gradual steps, each adding more cell-type specialisation.
- **Choanoflagellates** (aggregative): closest living relatives of animals. *Choanoeca flexa*
  (2026 *Nature*) can switch between clonal and aggregative modes **based on salinity** in
  ephemeral splash pools — the environment directly decides the reproductive strategy.
  This is the direct science basis for salinity biasing colony mode in CLAY.
- ***Dictyostelium discoideum*** (aggregative): independent amoebae aggregate when starved into
  a fruiting body. ~20% of cells become a **stalk** — they die so the other 80% (spores) can
  disperse. No cell chooses to be stalk; position in the aggregate decides it. Classic cheater
  problem: cells that avoid stalk formation pass genes at others' expense (Strassmann/Queller).
- **Single-cell bottleneck** (Grosberg & Strathmann): multicellular organisms that develop from
  a single cell dramatically reduce within-body genetic conflict — all cells share the same
  genome, so they have perfectly aligned evolutionary interests. Organisms that lack this
  bottleneck suffer high cheater rates (cancer analogue). In CLAY this is the design basis for
  the cheater setback in `CellStage_Decisions.md` §6.

**Fantasy.** You are no longer just a cell — you are a body in formation. Cells bind, stay,
and specialise. The thing that walks out of the tide pool to become a creature is the sum of
every choice made from S0 onward.

**Goal (graduation milestone).** `Milestone.MulticellularBody` — the colony reaches a
threshold of differentiated cell types performing distinct roles simultaneously, and has
survived for `ColonyStabilisationPeriod` without catastrophic cheater collapse or total
predation loss.

**Duration target.** 10–20 minutes — a moderate climax phase. The graduation challenge
creates urgency.

---

### S5 Colony modes

The biome's current salinity determines which mode is more accessible. Both can be achieved in
any biome, but high-salinity environments push toward clonal (Volvox-like) and
low-salinity/evaporative environments push toward aggregative (Dictyostelium-like).

#### Mode A — Clonal (solo commander)

You grow your own multicellular body by division. Daughter cells remain attached; over time
they differentiate based on position and genome expression.

**Loop:**
1. *Divide and hold* — when you divide in S5, instead of daughters drifting apart, they
   remain attached via an adhesion protein (encoded after the `ColonyAdhesion` milestone).
   Holding position during division increases adhesion success rate.
2. *Differentiate* — daughter cells automatically specialise based on position in the growing
   body. No player choice. Outer cells → motility/defense (flagella, membrane spikes). Inner
   cells → feeding/metabolism. Tip cells → eventual reproductive gonidia.
3. *Coordinate* — the player controls the whole colony as a unit (commander view). Movement
   is slower; the colony can rotate. Active parts from S4 are distributed across the body
   (flagella ring = sustained propulsion; spike ring = contact defense).
4. *Maintain* — inner metabolic cells must be fed. If the colony outgrows its energy supply,
   peripheral cells starve and detach. Size has a hard metabolic cap.
5. *Graduate* — once the body achieves the required differentiation mix and survives the
   graduation challenge, it is a viable proto-organism and hands off to the creature stage.

**Cheater mechanic in clonal mode:** since all cells are genetically identical, cheating is
suppressed but not eliminated — somatic cells that undergo additional mutations can diverge
and begin reproducing rather than maintaining their role (cancer analogue). The setback from
`CellStage_Decisions.md` §6 applies: multicellular without a single-cell bottleneck → cheater
cells spread. Visible as an internal colour divergence and erratic movement in affected sectors.

#### Mode B — Aggregative (MMO co-op colony)

Independent player cells assemble into one body when a starvation signal is broadcast from
a critical-mass cluster.

**Loop:**
1. *Starvation signal* — when a dense cluster of S5 cells can no longer sustain individual
   energy needs, a chemical gradient is emitted. All nearby players receive it (visible as a
   slow pulse spreading outward from the cluster centre). No text.
2. *Stream* — players who respond move toward the signal source. Streaming itself is visible
   and beautiful — dozens of individual cells flowing into a central point like tributaries.
   Players can resist (stay individual) at the cost of starving.
3. *Aggregate* — cells arrive and stack into a slug-like mass. Movement is slow but the
   combined body is large enough to push through obstacles individual cells could not.
4. *Differentiate by position* — cells at the front of the slug trend toward tip/stalk fate;
   cells at the rear and interior trend toward spore fate. Position is partly driven by
   arrival order and partly by cell size and symbiont branch.
5. *Fruiting body* — the slug rises: front cells extend upward as stalk, rear cells climb
   to the top as spores. Stalk cells die (their player respawns as a free cell immediately —
   in the living gene pool). Spore cells disperse as the graduation challenge fires.

**Stalk vs spore:** who becomes stalk is decided by position, morphology, and chance —
never by player choice (`CellStage_Decisions.md` §4b). A player who consistently arrives
early, is large, or has a mitochondrion branch tends to be pushed toward stalk. Players
who consistently cheat (resist streaming) lose nothing mechanically this session but are
remembered socially by other players into civilisation.

---

### Cell differentiation types (both modes)

| Role | Visible form | Function |
|------|-------------|---------|
| **Somatic / motility** | flagella-ringed exterior, streamlined | Propels the colony body |
| **Defense** | membrane spikes, thick outer wall | Blocks engulf attempts; retaliation damage |
| **Metabolic / feeding** | large interior, high internal glow | Sustains colony energy; engulfs food particles |
| **Reproductive (gonidia)** | brightest glow, slightly separate | Eventually generates next-generation colony |
| **Stalk** (aggregative only) | elongated, pale, dimming | Structural scaffold; sacrificed on graduation |

Differentiation is **never chosen** — it emerges from position, genome, and biome. The player
may bias it by where they sit in the colony body (outer vs inner) and which branch bonus they
carry, but cannot directly assign a role.

---

### The graduation challenge

The planet's countdown catastrophe (`PlanetaryParameters.md` §5) reaches its conclusion at S5.
The tide pool begins to dry, freeze, acidify, or stratify (biome-dependent). The colony has a
limited window to:

1. Reach sufficient size and differentiation (the body must be viable, not just assembled).
2. Survive a final environmental hazard (the biome's terminal event — drying in a tidal flat,
   a methane eruption in a volcanic pool, a UV burst in a thin-atmosphere world).
3. The body is then handed to the creature stage as a proto-organism on the beach.
   (`Architecture.md` §2 — the tide-pool-to-beach handoff.)

If the catastrophe fires before the colony is ready, the colony is destroyed and players
respawn in S4 from their living gene pool variants. The catastrophe deadline is what prevents
S5 from being a low-stakes idle phase.

---

### S5 visual language (discovery-only rule maintained)

| State | Visible signal |
|-------|----------------|
| First adhesion (ColonyAdhesion milestone) | Two daughter cells shimmer in contact, fail to separate |
| Growing clonal body | Cells accumulate in a brightening cluster; outer ring differentiates in colour |
| Starvation signal (aggregative) | Slow chemical pulse radiates outward from dense cluster |
| Streaming | Rivers of individual cells converging on a point |
| Slug formation | Cells merge into a single slow-moving mass |
| Stalk differentiation | Front cells elongate and pale; rear cells brighten and compact |
| Cheater outbreak | Internal colour divergence, erratic local movement in the affected sector |
| Graduation threshold reached | The body pulses once, strongly — visible to all players in the pool |

---

### Gate → Creature stage

`Milestone.MulticellularBody` fires when:
1. Colony body contains ≥ 3 distinct differentiated cell types active simultaneously.
2. Colony has survived `ColonyStabilisationPeriod` (default 90 seconds) without cheater
   collapse or full dissolution.
3. Graduation challenge survived (terminal biome event weathered).

The handoff to the creature stage is the body physically crossing the pool boundary onto
the exposed beach/shore — a literal spatial threshold, not a menu transition.

**Alien generalization.** Salinity, temperature, and organic density bias clonal vs
aggregative. On ammonia worlds, the aggregation signal is a thermal rather than chemical
gradient. On high-radiation worlds, stalk cells have melanin-derived protection (Radiosymbiont
branch players are disproportionately pulled toward stalk — a branch-specific sacrifice).
Methane/ethane worlds may produce floating colonial bodies rather than sessile fruiting
structures (buoyancy rather than structural stalk).

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
- Does HGT persist as a low-rate background mechanic into S3/S4, or switch off at nucleus
  formation? (Suspected: very low rate persists; eukaryotes do still take up environmental DNA
  but far less than prokaryotes.)
- Exact `ColonyStabilisationPeriod` and `NuclearThreshold` values — needs playtesting.
- Creature-stage handoff format: does the proto-organism body carry its cell-type layout into
  the creature editor, or does the creature stage start fresh with the lineage's genome?
- How many simultaneous colony bodies can co-exist in the pool in aggregative mode? Need a
  population cap to prevent coordination collapse.

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
