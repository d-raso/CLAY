# Cell Stage — Play-test Guide

How to reach and test every cell-stage feature currently built.

## Getting in
- **Main menu → "Cell Stage (sandbox)"**, or land on a planet and click a tide pool with the play cursor.
- A banner names the biome you're in.

## Controls
| Key / input | What it does |
|---|---|
| WASD / arrows, or hold left mouse | Swim |
| Mouse wheel | Zoom (S0: zooming in fully on a stranded shore enters hands-on assembly) |
| X (hold) | Shed your membrane → the viroid path (S0) |
| SPACE / A / D | Fission minigame (S1, once you have the ring) |
| Left-drag a gene knot | Regulate / cut genes (S1) |
| **F3** | Debug overlay: biome, music section, and in S1 every energy/genome number |
| F2 | Dev genome panel (numbers) |
| F10 | Rebuild the pool as the next biome |
| F11 | Mute music |
| F4 | Caustics on/off |
| F6 | Microscope illumination (bright / dark / phase / DIC) |
| M | Test recipe AAA (S0) |
| "DEV: become S1" button (top right, S0) | Become alive instantly and spawn 16 living cells |

## S0 — the protocell
1. **Absorbing:** swim through molecules.
   - Bases (notched/tabbed shapes) and lipids give a bright little pluck.
   - Clay hexagons and sugar rings give a dull thud: no use to you.
2. **Replicating:** bases inside you link into chains. A chain that exactly matches a planet recipe is a replicator and glows.
   - Hot/cold glow hints at how close you are. M makes the recipe AAA for testing.
3. **The pool turns:** once all 3 planet recipes have been assembled somewhere in the pool, a "1/3, 2/3, 3/3" banner appears for your finds. On the last, 90% of the other protocells come alive (the rest become viruses). Before that, only *you* can come alive early.
4. **Becoming alive:** carry a replicator through 2 divisions (grow by absorbing lipids until you split) → you become alive (S1).
   - The camera dives in and the music lifts to a brighter key (the "bloom").
   - Shortcut: the DEV button.

## S1 — the living cell
What you see inside your cell:
- **Genome loop:** a pale filament in the middle.
- **Gene bays** along the loop: coloured knots (filled) or pale pockets (empty). A knot pulses while that gene earns.
- **Proteins:** coloured specks around the membrane, bright when their energy source is present.
- **Brown granules:** raw energy waiting to be processed. Lots of them means your enzymes can't keep up (clogged).
- **Gold beads:** your ribozymes (enzymes). More and brighter = better; they flare while converting.

### Energy (test it)
- **Food:** swim into food (droplets, globule clusters, flocs, membrane scraps) → pluck sound, brown granules appear, gold beads flare.
- **Rest vs swim:** while you rest, surplus builds better enzymes (gold beads multiply). While you swim, it grows the membrane.
  - Check F3 → `activity`, `making /s`.
- **Starvation:** stay away from food → the cell wrinkles and shimmers, the music gets tense, and eventually you die.

### S1 is the RNA world
No proteins and no light yet. Your genes are RNA strands whose **fold is the catalyst** (ribozymes).

**Energy comes from three places:**
1. **The vent's proton gradient.** Near a vent (dark chimneys, or the white towers of the hot spring), protons stream in through your membrane: bright specks crossing inward.
   - **Leakiness** is the key trait.
   - **A leaky membrane** (thin-looking) earns a lot at the vent, but bleeds energy away from it.
   - **Eating fatty acids** (lipids) makes the membrane leakier.
   - **Each new generation's membrane** is a little tighter (thicker-looking).
2. **CO₂-fixing ribozyme** (cyan fold). It makes energy from vent H₂ (the cyan fizz bubbles), but only as well as the **iron–sulfur grains** you hold.
   - FeS grains are dark specks near vents and seeps; every living cell picks them up.
   - They show as dark flecks inside you and slowly wear out.
3. **Thioester ribozyme** (amber fold). Breaks down the food you swallow; no vacuoles.

### What's inside your living cell
| You see | It is |
|---|---|
| Pale ribbon loop | Your **genome** (RNA). Twisted double strand once it's DNA. |
| Folded glossy strands on the loop | **Genes**: their shape is their 4 bases, their colour their job, a clean fold = a good gene |
| Same folds drifting in the cytoplasm | **Working copies** of your ribozymes, doing the chemistry. More = more of your effort on that gene. |
| Pale empty pocket on the loop | Empty gene bay |
| Dull brown tangle | A misfolded gene (useless, costs energy) |
| Dark purple knot | The viral gene that will turn RNA into DNA |
| Dark metallic flecks | Iron–sulfur grains (catalysts) |
| Bright specks crossing the membrane inward | Protons from the vent's gradient |
| Little flashes streaming to the centre | Energy being made. More flashes from a gene's folds = it earns more. |
| Pearl beads of 4 shapes | Nucleotides (bases) you've collected, the material genes are written in |
| Brown granules / gold beads | Raw feedstock / overall ribozyme quality |

### The Ribosome (milestone between THE RING and DNA)
You get the ribosome automatically once **both** are true:
- you've divided at least **5 times** as a living cell;
- you carry genes for **at least 3 different jobs**, and **every** gene's meter is past the threshold tick.

Better genes don't just unlock it: they make you more stable and better fed, so you survive the divisions.

Amino acids and peptides still appear (pink knobs and chains); a better peptide-maker gene makes more of them, which steadies your cell.

Shortcut: **DEV: give ribosome**.

**Light (rhodopsin, photosynthesis)** isn't in S1; it comes in S2.

### The gene editor (zoom into your cell)
A gene is a **chain of bases** (coloured beads on a strand). Its shape is never set by hand: it **folds by itself**:
- **Stem:** the two ends zip together wherever their bases pair (A↔B, C↔D).
- **Bulge:** one unpaired base can kink the stem.
- **Loop:** whatever hangs in the middle is the pocket that grips a molecule.
- **Floppy:** if the very ends don't pair, it's a floppy strand that barely works.

**Handling it**, in the gene view: zoom all the way in, scroll once more, click a gene.
- **Grab any bead and pull.** It moves like a limb, and its neighbours follow on the strand.
- **Pull a bead far away and let go** → it comes out. The gene refolds, or falls apart if that bead held it together. A red glow warns you it's about to come out.
- **Drop a gene bead on another bead** → they swap.
- **Your free bases** float around the gene:
  - drop one **on a bead** to replace it;
  - drop one **between two beads** to insert it;
  - drop one **past either end** to extend the chain.
- **Tap a bead** to select it; then **1–4** set its base, and **← →** move the selection.

**What it shows you:**
- **The pocket:** the molecule the folded loop grips (its job). The better the gene, the tighter it's held.
- **Nearby jobs:** faint molecules around it brighten the closer you are to them.
- **The efficiency meter** below; the tick is the ribosome threshold.

**Costs:** the first change per visit to a gene uses one complexity point. Each change costs a little energy, and placing a base uses that base.

**F3** shows the editor's state: what you're holding, the selection, and complexity points.

### Seeing energy
Each source has its own particles:
- **Light:** twinkling pale glints in sunny shallows.
- **Vent H₂:** tiny cyan fizz bubbles.
- **Sulfide:** faint yellow wisps near black smokers.
- **Iron:** rust flecks near the seep.
- **Food:** the drifting organic particles.

If you have the gene, its machines pull those particles in. A better gene pulls harder and sends more flashes to the centre.

### Genes
Gene colours: amber = fermentation, purple = light pump, red = light + sulfide, cyan = vent H₂, yellow = sulfur, rust = iron, green = lysis.
- **Regulate:** grab a knot (left-drag). Pull away from the loop → bigger, more effort to that gene. Push in → smaller.
- **Cut out:** drag a knot out through the membrane and release. It drifts off as a scrap.
- **Pick up:** swim through a coloured scrap with an empty bay → it snaps in (bell sound). With no empty bay → it bounces off.
- **Find scraps:** dead living cells spill them. Use the DEV button for 16 neighbours, wait for some to die, or eat smaller ones once you have the green Lysis gene.
- **Drift:** each division can turn a gene into a related one, likelier where its source is strong.
  - Live near the vent (shimmering plumes) to drift into cyan or yellow vent genes.
- **Loss:** a gene with nothing to feed on for ~2½ minutes unravels out of the cell. While still RNA, UV in shallow water can break genes.
- **Where sources are:** light = shallow water by day; H₂ / sulfide = around the vent (dark chimneys); iron = the seep.
  - F3 shows the numbers, F2 shows source strength per gene.

### Body shape
When THE RING appears, your lineage takes a form: a rod, a curved rod (vibrio), a round coccus, or a lobed cell. It then drifts a little each division, so lineages look different. Rods point where they swim.

### Division and milestones
1. **Passive division (no ring yet):** grow (swim and eat) until about 1.8× your birth size and your copy store fills. Watch F3 `copy store`.
   - The cell tears in two on its own: unequal halves, and the smaller may lose genes.
2. **LINEAGE banner:** your first division. From now on dying respawns you as a living descendant, never back to S0.
3. **THE RING banner:** controlled division unlocks. When the cell pulses, press **SPACE** (the hint shows once). The genome copies itself; then:
   - the two copies drift toward the ends of the cell on their own, but jostle, and now and then one slips back to the middle (it turns red when it's in the ring's way);
   - **hold SPACE** to tighten the ring (it burns energy); **let go** to wait for a better moment;
   - close the ring on a copy → torn membrane, maybe a lost gene;
   - close it with both copies on the same side → one daughter is an empty bag and dies.
4. **A STRANGE GENE banner:** viruses (small RNA strands) drift toward you every ~35–70 s. Let one in: your membrane shimmers for ~8 s while you digest it.
   - Half the time you keep its gene: a dark knot on your loop, and the cell glows faintly.
   - It can also appear on its own after many generations.
5. **DNA banner:** with the strange gene, each division twists the genome into a double strand (3 divisions). DNA = LUCA → S2.

### Dying in S1
- **Before LINEAGE:** back to S0, but reborn carrying your recipe, one division from life again.
- **After LINEAGE:** you continue as a living relative, or a new living cell carrying your genome.

## Biomes (F10 to cycle)
| Biome | Water | Floor & features | Music |
|---|---|---|---|
| Sunlit Tidal Flat | clear, big tidal sweeps | sand ripples, pebbles, boulders, pumice/foam overhead | bright major plucks |
| Warm Hydrothermal Vent Field | dark, churning vortex around vents | basalt columns, rust/black mineral tubes, sulfur crusts | low Phrygian bells + heartbeat |
| Alkaline Hot Spring | pale, still | rimstone terraces, white lace spires, foam | slow Lydian glass bells, big room |
| Clay Mineral Shelf | amber, glassy, sluggish | mud volcanoes, trapped bubbles, cracked mud patches | warm Dorian marimba |

- **Caustics** follow wave energy: strong on the tidal flat, nearly gone on the clay shelf.

## Music
- **Sections:** Intro / Theme A / Theme B / Development / Breakdown / Rest. F3 shows the current one.
- **Night** switches to the darker mode at the next section.
- **Tide:** a rising tide makes melodies climb; a receding one makes them fall.
- **Surroundings:** food nearby adds sparkles; a predator nearby adds a tense trembling note.
- **Events** change the music itself:
  - life: brighter key, full texture
  - gene: arpeggio and bells join
  - death: drops out


## Playing as a virus
**How you become one:**
- choose "No (virus)" when the tide turns;
- hold **X** to shed your membrane;
- press **V** after repeated deaths;
- or use **DEV: become virus**.

**Outside a cell** (as real virus particles are): you're inert.
- You take nothing in. You drift with the current and slowly decay (the thin bar).
- A soundly built virus decays much more slowly.
- Find a host.

**Entering:**
- A protocell lets you in easily; that's the old copying game.
- A **living cell** lets you in only if your build is sound enough for its defences. Fibres help you latch on.

**Inside a living cell, you assemble your next generation** (this is where the Flow-style pattern game happens):
- **Stealing:** the cell's parts you steal (bases, iron–sulfur, amino acids, its genes) are your building material.
- **Order matters.** The order you take them in is your assembly pattern:
  - a consistent repeating pattern, the more distinct compounds the better, builds sound copies and fills your tier progress fast;
  - random stealing builds defective copies.
- **Coherence:** the icon by the bars, red to violet, trembling when low. It's how regular this assembly is. A low one risks **unravelling**, worse in a well-defended cell.
- **The pink bar** is the takeover. A tail makes it faster.
- **HIJACKED:** the cell bursts with your copies, built as soundly as this assembly was. You continue as one, and the next cell starts a fresh assembly.

**Tiers are your lineage's evolution** (how well it builds itself). Each tier changes your form:

| Tier | Form |
|---|---|
| 0 | a lopsided scrap |
| 1 | round |
| 2 | a shell with fibres |
| 3 | a fuller capsid |
| 4 | a head and tail |
| 5 | a larger phage |
| 6 | a complex form |

- **Colours:** your form takes the colours of the compounds you assemble with.
- **Dying** costs a tier.

**Taking over species:** at least 3 hijacks and 35% of a species → **INFECTED**; at least 6 and 75% → **CLAIMED**.

**Become life** (bottom right) opts out.
