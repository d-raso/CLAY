# Clay: Creature Stage Overview
## From Colony to Civilization - Design Analysis & Proposal

---

## The Central Design Question

> **Should each player control an individual creature or an entire species?**

This is the most important decision for the creature stage. Let's analyze both approaches, then propose a hybrid solution.

---

# Part 1: Analysis of Control Models

## Option A: Individual Creature Control

*"You are one creature in a living world"*

### How It Would Work
- Player controls a single organism
- Other members of your species are AI or other players
- Death = respawn as offspring/new individual
- Progression through one creature's life, then its descendants

### Pros

| Advantage | Explanation |
|-----------|-------------|
| **Continuity from cell stage** | You've been controlling one entity the whole time |
| **Emotional connection** | "This is MY creature" - attachment to individual |
| **Immediate gameplay** | Direct control, moment-to-moment decisions |
| **Clear stakes** | Death matters (even if you respawn) |
| **Emergent stories** | "Remember when I escaped that predator?" |
| **Easier to understand** | Same mental model as cell stage |
| **Works for MMO** | Many players in same world, natural |

### Cons

| Disadvantage | Explanation |
|--------------|-------------|
| **Limited scope** | Can't see species-level evolution |
| **Repetitive** | Same creature, same abilities, for hours? |
| **Evolution disconnect** | How do you "evolve" mid-life? |
| **Scale mismatch** | Spore went from cell to galaxy - feels small |
| **Passive evolution** | Changes happen between deaths, not actively |

---

## Option B: Species Control

*"You guide the evolution of an entire species"*

### How It Would Work
- Player controls species-level decisions
- Individual creatures are AI-controlled
- Direct specific creatures or set behaviors
- Evolution happens through breeding/selection choices

### Pros

| Advantage | Explanation |
|-----------|-------------|
| **Epic scale** | Watch your species spread across continents |
| **Active evolution** | Direct control over evolutionary path |
| **God-game appeal** | Popular genre (Spore, Species: ALRE) |
| **Strategic depth** | Population management, niche selection |
| **Time compression** | Can skip boring parts, see millennia |
| **Differentiation** | Very different from cell stage (fresh) |

### Cons

| Disadvantage | Explanation |
|--------------|-------------|
| **Disconnect from cell stage** | Jarring shift from individual to species |
| **Loss of attachment** | "My species" vs "my creature" - less personal |
| **Complexity** | Managing populations is harder than one creature |
| **Multiplayer conflict** | How do multiple players control same species? |
| **Less immediate** | Watching vs doing |
| **Spore comparison** | Directly competing with Spore's model |

---

## Option C: Hybrid Model (Proposed)

*"You are a creature, but your choices shape your species"*

### The Concept

**Individual control with species-level consequences.**

You control one creature at a time, but:
- Your choices influence your species' evolution
- Successful behaviors spread to AI members
- You can possess/switch between your species members
- Major decisions affect the whole species

### The Best of Both Worlds

| From Individual | From Species |
|-----------------|--------------|
| Direct creature control | Watching your species thrive |
| Emotional attachment | Evolutionary trajectory |
| Moment-to-moment gameplay | Long-term strategy |
| Continuity from cell stage | Epic scale over time |
| Clear death stakes | Population persistence |

---

# Part 2: The Hybrid Model - Detailed Design

## Core Philosophy

> **You are the "spirit" of your lineage - currently inhabiting one creature, but your influence extends to all your descendants.**

This mirrors how evolution actually works: individuals live and die, but successful traits propagate through the species.

---

## 2.1 The Lineage System

### What is a Lineage?

Your "lineage" is:
- All creatures descended from your original colony
- They share your base genome (with variations)
- They exist in the world whether you control them or not
- You can inhabit any of them

```
                    [Original Colony]
                          │
              ┌───────────┼───────────┐
              ↓           ↓           ↓
         [Gen 1-A]   [Gen 1-B]   [Gen 1-C]
         (You're      (AI)        (Other
          here)                   Player?)
              │
        ┌─────┼─────┐
        ↓     ↓     ↓
    [Gen 2] [Gen 2] [Gen 2]
      ...
```

### Lineage Stats

```csharp
public class Lineage
{
    public string lineageId;
    public string speciesName;  // Player-named
    
    [Header("Population")]
    public int totalPopulation;
    public int aliveCreatures;
    public int totalGenerations;
    
    [Header("Genetics")]
    public Genome baseGenome;
    public List<Mutation> spreadMutations;  // Mutations that became common
    public float geneticDiversity;
    
    [Header("Territory")]
    public List<Region> occupiedRegions;
    public List<Biome> adaptedBiomes;
    
    [Header("Behavior")]
    public BehaviorProfile dominantBehaviors;
    public DietType primaryDiet;
    public SocialStructure socialStructure;
    
    [Header("Player Stats")]
    public int playerControlledDeaths;
    public int playerControlledKills;
    public float playerInfluenceScore;  // How much player has shaped this species
}
```

---

## 2.2 Creature Control

### Primary Mode: Direct Control

Most of the time, you directly control one creature:

- **Movement**: Direct WASD/analog control
- **Actions**: Attack, eat, mate, flee, etc.
- **Abilities**: Based on body plan and evolutions
- **Senses**: See/hear/smell based on creature's organs

### The Possession System

You can switch which creature you control:

**Switch Triggers:**
- Your current creature dies → Auto-switch to offspring/relative
- Voluntary switch → Enter "spirit view", select new host
- Emergency switch → If separated from lineage, find nearest member

**Switch Costs:**
- Cooldown (can't switch constantly)
- Lineage bond required (can't possess unrelated creatures)
- Distance factor (nearby = instant, far = takes time)

```csharp
public class PossessionSystem : MonoBehaviour
{
    public Creature currentHost;
    public Lineage lineage;
    
    public float switchCooldown = 60f;
    public float lastSwitchTime;
    
    public void EnterSpiritView()
    {
        // Pause direct control
        currentHost.SetAIControlled(true);
        
        // Enable overview camera
        Camera.main.GetComponent<SpiritViewCamera>().Enable();
        
        // Show available hosts
        HighlightPossessableCreatures();
    }
    
    public bool CanPossess(Creature target)
    {
        if (Time.time - lastSwitchTime < switchCooldown)
            return false;
        
        if (target.lineage != this.lineage)
            return false;
        
        if (target.isPlayerControlled)  // Another player
            return false;
        
        return true;
    }
    
    public void Possess(Creature target)
    {
        // Release current host
        currentHost.SetAIControlled(true);
        
        // Take control of new host
        target.SetPlayerControlled(true);
        currentHost = target;
        
        // Snap camera
        Camera.main.GetComponent<CreatureCamera>().SetTarget(target);
        
        lastSwitchTime = Time.time;
    }
}
```

### Why This Works

- **You always have a body** - Never just watching
- **Death isn't the end** - Your lineage continues
- **Can experience variety** - Different individuals have different mutations
- **Strategic switching** - Possess the creature best suited for current challenge

---

## 2.3 Evolution System

### How Evolution Happens

Evolution occurs through three mechanisms:

**1. Mutation on Birth**
- When creatures reproduce, offspring have chance of mutations
- Mutations are random but influenced by environment
- Player can't directly control this

**2. Selection Through Play**
- Creatures you control survive longer (presumably)
- Their traits pass to offspring
- Your playstyle shapes the gene pool

**3. Directed Breeding (Active Choice)**
- In safe zones / nests, choose mates deliberately
- See potential offspring traits
- Guide evolution intentionally

### The Evolution Interface

**Between deaths/generations, player can:**
- View current genome
- See available mutations in the gene pool
- Preview how mutations would change creature
- Choose to express or suppress certain genes

```
┌─────────────────────────────────────────────────────────────────┐
│                    EVOLUTION INTERFACE                          │
│                                                                 │
│  Current Creature: "Swift Hunter" (Gen 47)                      │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                  [CREATURE PREVIEW]                      │   │
│  │                                                          │   │
│  │                       ████                               │   │
│  │                      ██████                              │   │
│  │                     ████████                             │   │
│  │                    ██████████                            │   │
│  │                        ██                                │   │
│  │                       ████                               │   │
│  │                      ██  ██                              │   │
│  │                                                          │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  AVAILABLE MUTATIONS IN GENE POOL:                              │
│                                                                 │
│  [■] Longer Legs (+15% speed, +10% visibility)      [EXPRESS]  │
│  [■] Thicker Hide (+20% defense, -5% speed)         [SUPPRESS] │
│  [□] Venomous Bite (NEW - rare mutation)            [EXPRESS]  │
│  [■] Larger Eyes (+25% night vision)                [EXPRESS]  │
│                                                                 │
│  SPECIES TREND: Your lineage is becoming faster and leaner     │
│                 over the past 10 generations.                   │
│                                                                 │
│  [CONFIRM CHANGES]                    [PREVIEW OFFSPRING]       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Body Plan Editor

**Major body changes happen at specific milestones:**
- First land emergence
- Major niche shifts
- Speciation events

**The editor allows:**
- Limb addition/removal/modification
- Sensory organ placement
- Body segment changes
- Appendage specialization

**Constraints:**
- Must be biologically plausible
- Changes cost "evolution points" (accumulated through play)
- Drastic changes take multiple generations to fully express

---

## 2.4 Species-Level Gameplay

### Watching Your Species

Even when controlling one creature, you're aware of your species:

**Minimap/Overview shows:**
- Territory your species occupies
- Population density
- Threats to the species
- Other species movements

**Notifications for:**
- Population milestones (100, 1000, 10000...)
- New mutations spreading through species
- Territory gains/losses
- Extinction threats

### Species Decisions

Certain decisions affect the whole species:

**Migration**
- As the player, you can "lead" a migration
- AI members follow your general direction
- Establishes new territory

**Behavior Establishment**
- How you play influences AI behavior
- Hunt in packs repeatedly → Species becomes pack hunters
- Flee from X consistently → Species learns to fear X

**Social Structure**
- Your interactions with mates/offspring define structure
- Monogamous play → Monogamous species
- Harem building → Harem species
- Solitary play → Solitary species

```csharp
public class SpeciesBehaviorLearning : MonoBehaviour
{
    public Dictionary<BehaviorType, float> behaviorWeights = new();
    
    public void OnPlayerAction(ActionType action, Context context)
    {
        // Player actions influence species behavior
        
        switch (action)
        {
            case ActionType.HuntInGroup:
                behaviorWeights[BehaviorType.PackHunting] += learningRate;
                break;
            case ActionType.ShareFood:
                behaviorWeights[BehaviorType.Altruism] += learningRate;
                break;
            case ActionType.FleeFromPredator:
                behaviorWeights[BehaviorType.FlightResponse] += learningRate;
                predatorFearLevels[context.predatorSpecies] += learningRate;
                break;
            case ActionType.DefendTerritory:
                behaviorWeights[BehaviorType.Territorial] += learningRate;
                break;
        }
        
        // Propagate to AI members over time
        PropagateToSpecies();
    }
}
```

---

## 2.5 Multiplayer Considerations

### Same Species Multiplayer

Multiple players can be in the same lineage/species:

**Cooperation:**
- Hunt together
- Raise offspring together
- Defend territory together
- Different players in different regions

**Divergence:**
- If players in same species choose different paths...
- Eventually speciation occurs
- Species splits into two lineages
- Players now control different (related) species

### Different Species Interaction

Players controlling different species:

**Competition:**
- Predator/prey relationships
- Territory disputes
- Resource competition

**Coexistence:**
- Different niches
- Mutualism possible
- Ecosystem building

### The Shared World

All players exist in one persistent world:

- Ecosystems shaped by all players' species
- Extinctions affect everyone
- Cooperation or competition - player choice
- Emergent food webs

---

## 2.6 Stage Progression

### Creature Stage Sub-Phases

**Phase 1: Aquatic Creature (First 2-3 days)**
- Emerge from colony as simple aquatic organism
- Learn creature controls
- Establish first territory
- Early evolution choices

**Phase 2: Aquatic Apex (2-3 days)**
- Grow species population
- Compete with other aquatic species
- Develop complex body plan
- Prepare for land (optional path)

**Phase 3: Land Emergence (Major Milestone)**
- Dramatic transition
- New movement, new senses, new challenges
- Many species attempt, few succeed
- Opens terrestrial gameplay

**Phase 4: Terrestrial Creature (3-4 days)**
- Explore land biomes
- Compete with land species
- Develop land adaptations
- Social structures emerge

**Phase 5: Apex Terrestrial (2-3 days)**
- Dominant species gameplay
- Complex social behavior
- Tool use emergence (if applicable)
- Prepare for tribal stage

### Transition to Tribal Stage

**Requirements:**
- Social structure established
- Basic tool use (rocks, sticks)
- Communication system (calls, gestures)
- Population threshold
- Territory stability

**The Transition:**
- Species gains sapience
- Individual control becomes tribal control
- New mechanics unlock

---

# Part 3: Creature Stage Mechanics Overview

## 3.1 Body Plan System

### Body Segments

Creatures are built from segments:

```csharp
public class CreatureBodyPlan
{
    public HeadSegment head;
    public List<TorsoSegment> torsoSegments;
    public TailSegment tail;  // Optional
    
    public List<Limb> limbs;
    public List<Appendage> appendages;
    
    public SkinType skinType;
    public float baseSize;
}

public class Limb
{
    public LimbType type;  // Leg, arm, wing, flipper, tentacle
    public int segmentCount;
    public AttachmentPoint attachPoint;
    public List<LimbAbility> abilities;
}
```

### Body Plan Constraints

| Constraint | Reason |
|------------|--------|
| Bilateral symmetry (default) | Most efficient for movement |
| Max limbs based on torso length | Physics/balance |
| Sensory organs on head (usually) | Concentration benefits |
| Size limits per biome | Energy/physics |
| Internal consistency | Can't have gills AND lungs active simultaneously |

### Evolution Point Costs

| Change Type | Cost | Generations |
|-------------|------|-------------|
| Minor mutation expression | 10 EP | 1 |
| Limb modification | 50 EP | 2-3 |
| New limb | 100 EP | 3-5 |
| Major body restructure | 200 EP | 5-10 |
| Niche shift (aquatic ↔ terrestrial) | 500 EP | 10+ |

---

## 3.2 Creature Abilities

### Movement Abilities

| Ability | Requirements | Function |
|---------|--------------|----------|
| Walk | Legs | Basic land movement |
| Run | Strong legs | Fast land movement |
| Swim | Fins/flippers/tail | Water movement |
| Fly | Wings + light body | Aerial movement |
| Climb | Claws/grip | Vertical surfaces |
| Burrow | Digging limbs | Underground movement |
| Jump | Strong legs | Vertical leap |
| Glide | Membrane/wings | Controlled fall |

### Combat Abilities

| Ability | Requirements | Function |
|---------|--------------|----------|
| Bite | Jaws/teeth | Basic attack |
| Claw | Claws | Slashing attack |
| Tail Whip | Strong tail | Knockback |
| Venom | Venom glands | Poison damage |
| Gore | Horns | Charging attack |
| Constrict | Long body | Grappling |
| Spit | Specialized glands | Ranged attack |

### Survival Abilities

| Ability | Requirements | Function |
|---------|--------------|----------|
| Camouflage | Skin adaptation | Hide from predators |
| Echolocation | Specialized ears | Navigate darkness |
| Heat Sense | Pit organs | Detect warm-blooded prey |
| Regeneration | Genetic trait | Heal over time |
| Hibernation | Fat storage | Survive harsh seasons |
| Venom Resistance | Genetic trait | Resist poison |

---

## 3.3 Survival Systems

### Needs

```csharp
public class CreatureNeeds
{
    [Header("Primary Needs")]
    public float hunger;        // 0-100, death at 0
    public float thirst;        // 0-100, death at 0
    public float stamina;       // 0-100, regenerates
    public float health;        // 0-100, death at 0
    
    [Header("Secondary Needs")]
    public float temperature;   // Comfort range varies by species
    public float oxygen;        // For aquatic/altitude
    public float sleep;         // Affects performance
    
    [Header("Social Needs")]
    public float loneliness;    // For social species
    public float matingUrge;    // Drives reproduction
}
```

### Diet Types

| Diet | Food Sources | Pros | Cons |
|------|--------------|------|------|
| Herbivore | Plants | Abundant food | Low energy density |
| Carnivore | Other creatures | High energy | Must hunt, competition |
| Omnivore | Both | Flexible | Master of neither |
| Insectivore | Insects | Reliable | Small portions |
| Filter Feeder | Plankton | Passive | Limited to water |
| Scavenger | Carrion | No hunting | Unreliable, disease |

### Predator/Prey Dynamics

```csharp
public class PredatorPreySystem : MonoBehaviour
{
    public void EvaluateThreatLevel(Creature self, Creature other)
    {
        float sizeRatio = other.size / self.size;
        float speedRatio = other.speed / self.speed;
        bool isPredatorSpecies = foodWeb.IsPredatorOf(other.species, self.species);
        
        if (isPredatorSpecies && sizeRatio > 0.5f)
        {
            // Threat! Trigger fear response
            self.behaviorAI.SetState(BehaviorState.Flee);
            self.fearLevel += CalculateFear(other);
        }
        else if (foodWeb.IsPreyOf(other.species, self.species) && sizeRatio < 2f)
        {
            // Potential prey
            self.behaviorAI.ConsiderHunting(other);
        }
    }
}
```

---

## 3.4 Reproduction System

### Mating

**Finding Mates:**
- Same species, opposite sex (for sexual species)
- Mating displays (player-controlled minigame?)
- Mate selection based on fitness indicators

**Mating Rituals:**
- Species-specific (evolved through play)
- Can be simple or elaborate
- Success based on display quality + mate receptiveness

### Offspring

**Birth/Hatching:**
- Eggs vs live birth (evolved trait)
- Clutch/litter size varies
- Parental care varies (evolved behavior)

**Offspring Control:**
- Initially AI-controlled
- Player can switch to control offspring
- Offspring inherit player's traits + mutations

**Generational Progression:**
```
You (Gen 1) → Offspring (Gen 2) → Their Offspring (Gen 3) → ...
     │              │                      │
     └── Die ───────┼── Possess ───────────┘
                    │
              [Continue playing]
```

---

## 3.5 Social Systems

### Social Structures

| Structure | Description | Benefits | Costs |
|-----------|-------------|----------|-------|
| Solitary | Live alone | No sharing, full control | No help |
| Pair Bond | Mated pairs | Shared parenting | Limited |
| Family Group | Parents + offspring | Protection | Must feed all |
| Pack/Herd | Multiple families | Safety in numbers | Hierarchy drama |
| Colony | Large organized group | Specialization | Individual = expendable |

### Hierarchy

For social species:
- Dominance determines mating rights
- Player can challenge for dominance
- Or play as subordinate (different gameplay)
- Hierarchy affects AI behavior toward you

### Communication

Creatures can develop communication:

**Basic Calls:**
- Danger warning
- Food discovery
- Mating call
- Territory claim

**Advanced Communication (late game):**
- Specific predator warnings
- Location information
- Individual recognition
- Proto-language (tribal stage prep)

---

## 3.6 Environment Interaction

### Biomes

| Biome | Challenges | Opportunities |
|-------|------------|---------------|
| Ocean | Pressure, predators | 3D movement, abundant food |
| Coastal | Tides, exposure | Two worlds access |
| Freshwater | Limited space | Less competition |
| Rainforest | Competition, disease | Abundant resources |
| Savanna | Exposure, drought | Room to run |
| Desert | Heat, water scarcity | Few competitors |
| Tundra | Cold, scarce food | Few predators |
| Mountains | Altitude, terrain | Defensible territory |

### Seasons

Time passes, seasons change:

| Season | Effects |
|--------|---------|
| Spring | Abundant food, mating season |
| Summer | Peak activity, water important |
| Autumn | Food storage, migration prep |
| Winter | Scarcity, survival mode |

### Natural Events

- Droughts
- Floods
- Fires
- Volcanic activity
- Meteor impacts (rare, major)
- Ice ages (very rare, game-changing)

---

## 3.7 Progression & Objectives

### Creature Stage Objectives

**Survival Tier:**
- [ ] Survive first day
- [ ] First successful hunt/forage
- [ ] Find safe sleeping spot
- [ ] Survive first predator encounter

**Growth Tier:**
- [ ] Reach adulthood
- [ ] First successful mating
- [ ] Raise offspring to adulthood
- [ ] Live to old age

**Species Tier:**
- [ ] Lineage reaches 100 population
- [ ] Occupy 3 different regions
- [ ] Survive a harsh season
- [ ] Outlast a competitor species

**Evolution Tier:**
- [ ] Express 10 different mutations
- [ ] Develop a unique adaptation
- [ ] Speciation event (split into two species)
- [ ] Colonize new biome type

**Apex Tier:**
- [ ] Become apex predator in region
- [ ] Lineage reaches 1000 population
- [ ] Develop basic tool use
- [ ] Establish complex social structure

### Creature Stage Milestones

| Milestone | Trigger | Reward |
|-----------|---------|--------|
| First Steps | Complete aquatic intro | Land unlock |
| Terrestrial | Survive 1 hour on land | Land evolution options |
| Pack Leader | Lead group hunt | Social evolution options |
| Apex Predator | No predators in region | Territory expansion |
| Tool User | Use object as tool | Tribal stage unlock |
| Sapience | Meet all requirements | Tribal stage transition |

---

# Part 4: Technical Considerations

## 4.1 AI Systems

### Individual Creature AI

Each AI creature needs:
- Pathfinding
- Need-based decision making
- Predator/prey responses
- Social behavior
- Species-specific behaviors

**Performance Consideration:** Can't simulate thousands of creatures fully

**Solution: LOD for AI**
- Full AI for nearby creatures
- Simplified AI for distant creatures
- Statistical simulation for very distant
- "Fog of war" - unseen creatures are abstract

```csharp
public class CreatureAILOD : MonoBehaviour
{
    public enum AILevel { Full, Simplified, Statistical, Abstract }
    
    public void UpdateAILevel(float distanceToPlayer)
    {
        if (distanceToPlayer < 50f)
            SetAILevel(AILevel.Full);
        else if (distanceToPlayer < 200f)
            SetAILevel(AILevel.Simplified);
        else if (distanceToPlayer < 1000f)
            SetAILevel(AILevel.Statistical);
        else
            SetAILevel(AILevel.Abstract);
    }
}
```

### Species AI

Species-level behavior patterns:
- Migration routes
- Territory boundaries
- Population dynamics
- Evolution pressures

---

## 4.2 World Scale

### The Challenge

Creature stage implies:
- Large world (continents)
- Long time spans (generations)
- Many creatures (populations)

### Solutions

**Instanced Regions:**
- World divided into regions
- Player's region is fully simulated
- Other regions are abstracted
- Traveling between loads new region

**Time Compression:**
- Player can "sleep" through boring periods
- Generational skip (play as descendant)
- Seasonal fast-forward

**Population Abstraction:**
- Your immediate group: Full simulation
- Your species elsewhere: Statistics
- Other species: Simplified

---

## 4.3 Multiplayer Architecture

### Player Density

- Can't have millions of players in one region
- Shard by region
- Cross-shard species (your species exists in multiple shards)

### Synchronization

- Species data synced globally
- Individual creatures synced locally
- Evolution changes propagate over time

### Conflict Resolution

- Two players in same species, different shards, evolve differently
- Solution: Speciation - they become different subspecies
- Eventually different species
- Adds to biodiversity

---

# Part 5: Summary

## The Hybrid Model Recap

1. **You control individual creatures** - Continuity from cell stage
2. **You can switch between your lineage members** - Variety, persistence
3. **Your actions shape species behavior** - Agency over species
4. **Evolution happens between generations** - Active choices
5. **Multiple players can share a species** - Cooperation
6. **Species can split** - Handles divergence

## Stage Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  COLONY COMPLETE                                                │
│        │                                                        │
│        ↓                                                        │
│  AQUATIC CREATURE (2-3 days)                                   │
│  - Learn creature controls                                      │
│  - Basic survival                                               │
│  - First evolutions                                             │
│        │                                                        │
│        ↓                                                        │
│  AQUATIC APEX (2-3 days)                                       │
│  - Population growth                                            │
│  - Complex body plan                                            │
│  - Competition                                                  │
│        │                                                        │
│        ↓ (optional)                                             │
│  LAND EMERGENCE (milestone)                                     │
│  - Dramatic transition                                          │
│  - New challenges                                               │
│        │                                                        │
│        ↓                                                        │
│  TERRESTRIAL CREATURE (3-4 days)                               │
│  - Land survival                                                │
│  - New biomes                                                   │
│  - Social structures                                            │
│        │                                                        │
│        ↓                                                        │
│  APEX TERRESTRIAL (2-3 days)                                   │
│  - Dominance                                                    │
│  - Tool use                                                     │
│  - Communication                                                │
│        │                                                        │
│        ↓                                                        │
│  TRIBAL STAGE                                                   │
│                                                                 │
│  Total: ~2 weeks                                                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Key Design Pillars

1. **Continuity** - Individual control maintained from cell stage
2. **Consequence** - Your play shapes the species
3. **Persistence** - Death isn't the end, lineage continues
4. **Scale** - Species-level impact while playing as individual
5. **Emergence** - Behaviors and ecosystems emerge from play
6. **Choice** - Multiple valid evolutionary paths

---

## Open Questions for Further Design

1. **How detailed should the body editor be?** (Spore-like vs more constrained)
2. **How long should generations feel?** (Minutes vs hours per creature life)
3. **How much time compression?** (Real-time vs accelerated evolution)
4. **Tribal transition specifics?** (What triggers sapience?)
5. **PvP balance?** (Predator players vs prey players)
6. **Extinction handling?** (What if a player's species dies out?)

---

## Next Steps

1. Prototype the possession system
2. Design the body plan editor in detail
3. Develop the species behavior learning system
4. Plan the land emergence milestone
5. Create detailed objectives list
6. Design tribal stage transition
