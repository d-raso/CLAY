# CLAY — Game & Server Architecture

> Status: living design doc. CLAY is an MMO reimagining of Spore: **Cell → Creature →
> Tribal → Civ → Space**, played across many players on shared, persistent worlds.

## 1. The nested-server model

Scope = server = stage. Each server simulates exactly one stage's rules. Servers nest:

```
GALAXY  (one server, Space stage)
  └── PLANET  (one server each, Creature → Tribal → Civ stages)
        └── TIDE POOL  (many per planet, Cell stage)
```

- **Tide pool** — a cell-stage server. Many run per planet. Players start here.
- **Planet** — a creature/tribal/civ server. Receives species that graduate from *its* tide pools.
- **Galaxy** — the space-stage server. Receives civilizations that graduate from planets.

**Why nesting:** a higher-scope player acting on a lower scope (a space player seeding/
invading a planet) is just their server issuing calls into a live lower server. Cross-scope
permissions are the endgame design space.

## 2. Graduation gates (promotion between scopes)

| Gate | Trigger | Result |
|------|---------|--------|
| ① Cell → Creature | A cell **colony** reaches apex + clears a graduation challenge | That colony becomes a **species** and enters the planet's creature world |
| ② Planet → Space | Enough species mature through civ stage | The planet **ascends** into the galaxy/space stage |

Design rules:
- **Many winners, gated** — not last-colony-standing. Several colonies graduate per pool so
  the creature world is seeded with biodiversity. (Losing shouldn't feel like wasted hours.)
- **Asynchronous graduation** — pools resolve at different times. Graduates enter via an
  **arrival buffer**; the creature world opens in waves once enough species qualify (fairness)
  — exact rolling-vs-wave policy TBD per planet.

## 3. The catastrophe deadline (the straggler mechanic)

"Many winners" is bounded by a **planetary ecosystem catastrophe** that imposes a hard time
limit on colonies still in the cell stage. Catastrophes are **randomized per planet** —
their *type, timing, and even whether they occur* derive from that planet's habitability
parameters (see `PlanetaryParameters.md`). Examples: snowball glaciation, runaway greenhouse,
stellar-flare radiation burst, supervolcanic anoxia, bolide impact.

This makes each planet's cell stage feel distinct and gives a natural, diegetic reason the
tide pools eventually close. See `CellStage.md` §6.

## 4. Player lifecycle & persistence

- **Account-level lineage/genome** persists across all stages and deaths.
- **Death (pre-colony):** respawn as a fresh cell in a tide pool, keeping lineage progress.
- **Death (in colony):** respawn back into the *same colony* if possible (rejoining may be
  hard), or leave to **form/join another colony** — including recruiting players who haven't
  found a colony yet. See `CellStage.md` §5.
- **Washed-out veterans + newbies both feed the low tier**, keeping tide pools populated.

## 5. Planet & shard lifecycle (matchmaking / autoscaling)

A planet has a finite **carrying capacity** (driven by liquid coverage & habitability):

1. **Born** → tide pools spawn, accept new cell players.
2. **Filling** → colonies graduate into the creature world (waves).
3. **Closed to intake** → once seeded (or catastrophe fires), new cell players route to fresh
   tide pools on a **newly spun-up planet**.
4. **Maturing** → creature → tribal → civ play continues.
5. **Ascends** → planet enters space stage.

Planets are continuously born/closed/ascended → a steady churn of server shards at every tier.

## 6. The numbers (must be tuned early — drive feel AND cost)

| Knob | Affects |
|------|---------|
| players per tide pool | how lonely/crowded the ocean feels |
| tide pools per planet | species diversity on the creature world |
| species per planet before close | creature-world crowding, server count |
| catastrophe ETA distribution | cell-stage session length / pressure |

These cascade into server budget and world feel. Start with rough guesses, instrument, tune.

## 7. Open questions
- Arrival policy: rolling entry (always-on action) vs. wave (fairness)? Possibly per-planet.
- Exact colony-formation achievement gate (see `CellStage.md` §5).
- Cross-scope permissions (what a space player may do to a planet).
