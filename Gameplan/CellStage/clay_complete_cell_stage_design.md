# Clay: Complete Cell Stage Design Document
## From Chemistry to Multicellularity

---

## Document Overview

This document provides complete mechanical, objective, and progression specifications for every substage of Clay's cell stage. Each section includes:

- Scientific basis
- Core gameplay loop
- Detailed mechanics
- Objectives and milestones
- Transition requirements
- UI/UX considerations
- Balance targets

**Total Target Playtime:** ~2 weeks for semi-avid player (2-3 hours/day)

---

# STAGE 0: CHEMICAL GENESIS

## Scientific Basis

Before life existed, chemistry happened. Simple molecules formed through:
- Lightning in early atmosphere
- Reactions at hydrothermal vents
- Delivery via meteorites

These molecules self-assembled into increasingly complex structures until the first protocell emerged - a lipid membrane containing self-replicating RNA.

## Overview

| Aspect | Details |
|--------|---------|
| Duration | 30-60 minutes (tutorial) |
| Style | Puzzle/Assembly |
| Perspective | Abstract/Microscopic |
| Multiplayer | No (solo tutorial) |
| Death | Restart section |

## Core Loop

```
┌─────────────────────────────────────────────────────┐
│                                                     │
│   OBSERVE → COLLECT → ASSEMBLE → PROTECT → BIRTH   │
│      ↑                                     │        │
│      └─────────────────────────────────────┘        │
│            (fail = partial restart)                 │
│                                                     │
└─────────────────────────────────────────────────────┘
```

## Detailed Mechanics

### 0.1 The Environment

**Setting:** Primordial ocean / hydrothermal vent field

**Visual Style:**
- Abstract, almost cosmic
- Molecules represented as glowing geometric shapes
- Dark background with occasional light flashes (lightning)
- Particle effects for chemical reactions

**Environmental Features:**
- **Open water**: Molecules drift randomly
- **Vent plumes**: High molecule density, dangerous heat
- **Clay surfaces**: Catalyze reactions, safe zones
- **Lightning zones**: Create new molecules, destroy unprotected structures

### 0.2 Molecular Collection

**Molecule Types:**

| Molecule | Visual | Function | Rarity |
|----------|--------|----------|--------|
| Amino Acids | Small colored circles | Build proteins | Common |
| Nucleotides | Pentagon shapes | Build RNA/DNA | Uncommon |
| Lipids | Elongated ovals | Build membrane | Common |
| Phosphates | Triangles | Energy carriers | Uncommon |
| Metal Ions | Bright dots | Catalysts | Rare |

**Collection Mechanic:**
- Player controls a "focal point" (not yet a cell)
- Move near molecules to attract them
- Molecules orbit your focal point
- Limited capacity (10-15 molecules initially)

```csharp
public class MoleculeCollector : MonoBehaviour
{
    public int maxCapacity = 12;
    public float attractionRadius = 3f;
    public float attractionForce = 5f;
    
    public List<Molecule> collectedMolecules = new();
    
    void FixedUpdate()
    {
        // Attract nearby molecules
        var nearby = Physics2D.OverlapCircleAll(transform.position, attractionRadius);
        foreach (var col in nearby)
        {
            Molecule mol = col.GetComponent<Molecule>();
            if (mol != null && !mol.isCollected && collectedMolecules.Count < maxCapacity)
            {
                Vector2 direction = (transform.position - mol.transform.position).normalized;
                mol.GetComponent<Rigidbody2D>().AddForce(direction * attractionForce);
                
                if (Vector2.Distance(transform.position, mol.transform.position) < 0.5f)
                {
                    CollectMolecule(mol);
                }
            }
        }
    }
}
```

### 0.3 Assembly Phases

**Phase 1: Lipid Membrane (First 10-15 minutes)**

*Objective: Build a closed membrane around your focal point*

**Mechanic: Ring Assembly**
- Lipids must be placed in a ring formation
- Player drags lipids to positions around focal point
- Lipids snap to valid positions
- Need 8-12 lipids to complete a basic membrane
- Incomplete membrane = molecules can escape

**Puzzle Element:**
- Lipids have hydrophobic (tail) and hydrophilic (head) ends
- Must orient correctly (heads out, tails in)
- Wrong orientation = unstable, will drift away

**Completion:** Closed ring of correctly-oriented lipids = Membrane Complete ✓

---

**Phase 2: RNA Core (Next 15-20 minutes)**

*Objective: Build a self-replicating RNA strand inside your membrane*

**Mechanic: Sequence Building**
- Four nucleotide types: A, U, G, C
- Must build a strand of 8+ nucleotides
- Strand automatically tries to replicate
- Replication success based on:
  - Correct base pairing (A-U, G-C)
  - Available free nucleotides
  - Stability (certain sequences more stable)

**Puzzle Element:**
- Player chooses which nucleotides to incorporate
- Some sequences are "ribozymes" (catalytic)
- Discover ribozyme = bonus ability

**Mini-game: Replication**
- Watch your RNA replicate
- Guide daughter strand formation
- Avoid errors (mutations)
- Successful replication = RNA established ✓

---

**Phase 3: First Enzymes (Final 10-15 minutes)**

*Objective: Build protein enzymes to stabilize your protocell*

**Mechanic: Protein Folding**
- String amino acids together (4-8 per protein)
- Sequence determines fold shape
- Fold shape determines function

**Functions to build:**
1. **Membrane stabilizer**: Reduces membrane leakage
2. **Replication helper**: Speeds RNA copying
3. **Energy processor**: Converts phosphates to usable energy

**Puzzle Element:**
- Limited amino acids available
- Must prioritize which enzyme to build first
- Wrong folds = non-functional (wasted resources)

**Simplified Folding UI:**
- Not full protein-folding simulation
- More like "match the shape" puzzle
- 3-4 possible fold outcomes per sequence
- Player learns patterns

### 0.4 Hazards

| Hazard | Effect | Avoidance |
|--------|--------|-----------|
| UV Radiation | Damages exposed RNA | Complete membrane |
| Heat Spike | Denatures proteins | Avoid vent proximity |
| pH Shift | Destabilizes membrane | Find neutral zones |
| Dilution | Molecules drift away | Complete membrane |

**Hazard Timing:**
- First 5 min: No hazards (learning)
- 5-15 min: Occasional UV (teaches membrane importance)
- 15-30 min: All hazards active but infrequent
- 30+ min: Regular hazard cycles

### 0.5 Birth Sequence

**Trigger:** All three components complete (Membrane + RNA + Enzyme)

**Cinematic Moment:**
1. Camera pulls back
2. Your protocell pulses with light
3. First successful replication shown
4. "Life has begun" message
5. Protocell begins moving on its own
6. Transition to Stage 1

**Player Emotion Goal:** Wonder, accomplishment, "I created life"

### 0.6 UI Layout

```
┌─────────────────────────────────────────────────────────────┐
│  [Progress: Membrane ████████░░ RNA ████░░░░ Enzyme ░░░░░]  │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│                                                             │
│                    [MAIN GAME VIEW]                         │
│                                                             │
│                    Molecules floating                       │
│                    Your focal point center                  │
│                    Assembly UI overlaid                     │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│  Inventory: [A][A][U][G][Lipid][Lipid][Fe++]              │
│  Hint: "Collect more lipids to complete your membrane"      │
└─────────────────────────────────────────────────────────────┘
```

### 0.7 Objectives Checklist

**Required for Completion:**
- [ ] Collect 10+ lipids
- [ ] Build closed membrane ring
- [ ] Collect 8+ nucleotides
- [ ] Build functional RNA strand
- [ ] Successfully replicate RNA once
- [ ] Build at least 1 functional enzyme

**Optional Achievements:**
- [ ] "Perfect Membrane" - First try, no gaps
- [ ] "Lucky Sequence" - Build a ribozyme
- [ ] "Efficient" - Complete in under 30 minutes
- [ ] "Survivor" - Complete without losing molecules to hazards

---

# STAGE 1: PROTOCELL

## Scientific Basis

Early protocells were fragile, leaky, and dependent on external chemistry. They couldn't truly metabolize - just accumulate and occasionally replicate. The transition to true cells required:
- RNA → DNA (more stable)
- Better membranes
- Primitive metabolism
- Environmental adaptation

## Overview

| Aspect | Details |
|--------|---------|
| Duration | 2-3 days (6-9 hours) |
| Style | Survival/Gathering |
| Perspective | Top-down 2D |
| Multiplayer | Shared world, minimal interaction |
| Death | Respawn with adaptations |

## Core Loop

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  DRIFT ──→ GATHER ──→ FIND CLAY BED ──→ SYNTHESIZE         │
│    ↑                                          │             │
│    │         ┌────────────────────────────────┘             │
│    │         ↓                                              │
│    │      SURVIVE ──→ GROW ──→ STABILIZE                   │
│    │         │                    │                         │
│    │         ↓                    ↓                         │
│    └──── [DEATH] ←───────── [THRESHOLD MET]                │
│              │                    │                         │
│              ↓                    ↓                         │
│         ADAPTATION           STAGE 2                        │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Detailed Mechanics

### 1.1 Movement

**Base Movement:**
- Protocells drift with currents (passive)
- Weak self-propulsion via membrane undulation
- Slow, floaty, vulnerable feeling

```csharp
public class ProtocellMovement : MonoBehaviour
{
    [Header("Base Stats")]
    public float maxSelfPropulsion = 1f;      // Very slow
    public float driftInfluence = 0.8f;        // Heavily affected by currents
    
    [Header("Current State")]
    public Vector2 currentDirection;
    public float currentStrength;
    
    void FixedUpdate()
    {
        // Environmental drift (major factor)
        Vector2 drift = currentDirection * currentStrength * driftInfluence;
        
        // Player input (minor factor)
        Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        Vector2 selfPropulsion = input * maxSelfPropulsion * (1 - driftInfluence);
        
        // Apply
        rb.velocity = drift + selfPropulsion;
    }
}
```

**Movement Upgrades (via evolution):**
1. **Membrane fluidity** - Slightly faster undulation
2. **Cilia precursors** - Protein tufts that improve steering
3. **Streamlining** - Shape optimization reduces drag

### 1.2 Resource Gathering

**Resource Types:**

| Resource | Use | Abundance | Visual |
|----------|-----|-----------|--------|
| Nucleotides | RNA/DNA synthesis | Medium | Colored dots |
| Amino Acids | Protein building | High | Small circles |
| Lipids | Membrane repair/growth | High | Ovals |
| Phosphates | Energy (ATP precursor) | Medium | Yellow triangles |
| Metal Ions | Enzyme cofactors | Low | Bright specks |
| Organic Fragments | General nutrition | High | Brown blobs |

**Gathering Mechanic:**
- Resources absorbed on contact through membrane
- Absorption rate based on membrane permeability
- Some resources require specific transport proteins
- Internal storage limited (prevents hoarding)

```csharp
public class ProtocellResources : MonoBehaviour
{
    [Header("Storage")]
    public float maxStorage = 100f;
    public float currentStorage = 50f;
    
    [Header("Resource Pools")]
    public float nucleotides;
    public float aminoAcids;
    public float lipids;
    public float phosphates;
    public float metalIons;
    
    [Header("Absorption Rates")]
    public float baseAbsorptionRate = 1f;
    public float membranePermeability = 0.5f;
    
    public void OnResourceContact(Resource resource)
    {
        if (currentStorage >= maxStorage) return;
        
        float absorptionRate = baseAbsorptionRate * membranePermeability;
        
        // Some resources need transporters
        if (resource.requiresTransporter)
        {
            if (!HasTransporter(resource.type))
            {
                absorptionRate *= 0.1f; // Very slow without transporter
            }
        }
        
        float absorbed = Mathf.Min(resource.amount, absorptionRate * Time.deltaTime);
        AddResource(resource.type, absorbed);
        resource.amount -= absorbed;
    }
}
```

### 1.3 Clay Bed Mechanics (Safe Zones)

**Clay beds are critical to Stage 1.** They provide:
- Catalytic surfaces for synthesis
- Protection from hazards
- Anchoring point (stop drifting)

**Finding Clay Beds:**
- Scattered across the map
- Visual: Brownish textured regions
- Minimap reveals discovered beds
- Some beds have special properties

**Clay Bed Types:**

| Type | Property | Bonus |
|------|----------|-------|
| Common Clay | Basic catalysis | 1x synthesis speed |
| Mineral-Rich | Metal ion deposits | Metal ions regen |
| Volcanic Clay | Near vents, warm | Faster reactions, risky |
| Ancient Clay | Rare, deep | 2x synthesis speed |

**Using Clay Beds:**

```csharp
public class ClayBedInteraction : MonoBehaviour
{
    public ClayBed currentBed;
    public bool isAnchored = false;
    
    public void AnchorToBed(ClayBed bed)
    {
        currentBed = bed;
        isAnchored = true;
        
        // Stop movement
        protocellMovement.enabled = false;
        rb.velocity = Vector2.zero;
        
        // Enable synthesis UI
        synthesisUI.Show(bed);
        
        // Apply protection
        protocell.hazardResistance += bed.protectionBonus;
    }
    
    public void Detach()
    {
        protocell.hazardResistance -= currentBed.protectionBonus;
        currentBed = null;
        isAnchored = false;
        protocellMovement.enabled = true;
        synthesisUI.Hide();
    }
}
```

### 1.4 Synthesis System

**Performed only at clay beds.** This is how you upgrade your protocell.

**Synthesis Options:**

**1. Membrane Repair/Growth**
- Cost: Lipids
- Effect: Heal damage, increase size slightly
- Always available

**2. RNA Maintenance**
- Cost: Nucleotides
- Effect: Repair mutations, maintain replication
- Required periodically or you degrade

**3. DNA Synthesis (Major Milestone)**
- Cost: High nucleotides + specific enzymes
- Prerequisites: Build reverse transcriptase enzyme first
- Effect: Convert RNA genome to DNA
- Benefits:
  - More stable (less maintenance)
  - Can store more genes
  - Required for Stage 2

**4. Protein Synthesis**
- Cost: Amino acids
- Options: Build various enzymes/structural proteins
- Examples:
  - Membrane stabilizers
  - Transport proteins
  - Primitive metabolic enzymes
  - UV-repair enzymes

**5. Primitive Metabolism**
- Build enzyme chains that:
  - Convert resources more efficiently
  - Generate internal energy
  - Process waste

**Synthesis UI:**

```
┌─────────────────────────────────────────────────────────────┐
│  CLAY BED SYNTHESIS                         [DETACH]        │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Your Resources:                                            │
│  ■■■■■■░░░░ Nucleotides (62)                               │
│  ■■■■■■■■░░ Amino Acids (84)                               │
│  ■■■■░░░░░░ Lipids (41)                                    │
│  ■■░░░░░░░░ Phosphates (23)                                │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│  AVAILABLE SYNTHESES:                                       │
│                                                             │
│  [■] Repair Membrane          (20 Lipids)         [BUILD]  │
│  [■] Maintain RNA             (15 Nucleotides)    [BUILD]  │
│  [□] DNA Conversion           (80 Nuc + Enzyme)   [LOCKED] │
│  [■] Build: Transport Protein (30 Amino Acids)    [BUILD]  │
│  [■] Build: UV Shield Enzyme  (25 Amino + 5 Metal)[BUILD]  │
│                                                             │
│  Progress to DNA: ████████░░░░░░░░ 52%                     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 1.5 Hazards

**Environmental Hazards:**

| Hazard | Damage Type | Avoidance | Adaptation |
|--------|-------------|-----------|------------|
| UV Radiation | RNA/protein damage | Clay beds, depth | UV repair enzyme |
| Temperature Spike | Protein denaturation | Avoid vents | Heat shock proteins |
| pH Shift | Membrane destabilization | Move away | Buffer proteins |
| Osmotic Shock | Cell swelling/shrinking | Avoid salinity zones | Ion pumps |
| Oxidation | General damage | Avoid O2 pockets | Catalase enzyme |

**Hazard Implementation:**

```csharp
public class HazardZone : MonoBehaviour
{
    public HazardType type;
    public float damagePerSecond;
    public float warningRadius;  // Shows visual warning before damage zone
    
    void OnTriggerStay2D(Collider2D other)
    {
        Protocell cell = other.GetComponent<Protocell>();
        if (cell == null) return;
        
        float resistance = cell.GetResistance(type);
        float actualDamage = damagePerSecond * (1 - resistance) * Time.deltaTime;
        
        cell.TakeDamage(actualDamage, type);
    }
}
```

### 1.6 Death and Adaptation

**Death occurs when:**
- Membrane integrity reaches 0
- RNA completely degrades
- Starvation (resources depleted)

**On Death:**
1. Analyze cause (see Evolution doc)
2. Award adaptation XP to relevant resistances
3. Show death screen with "Your descendants remember..."
4. Respawn at a random clay bed

**What Carries Over:**
- Discovered clay bed locations
- Adaptation bonuses
- Achievement progress
- Evolution points earned

**What Resets:**
- Size
- Resource stores
- Position
- Current synthesis progress

### 1.7 Objectives and Milestones

**Stage 1 Main Objectives:**

| Objective | Requirement | Reward |
|-----------|-------------|--------|
| First Protein | Build any enzyme | 20 EP |
| Membrane Mastery | Reach 90% stability | 15 EP |
| RNA Guardian | 30 min without RNA damage | 25 EP |
| Clay Hopper | Discover 5 clay beds | 30 EP |
| DNA Pioneer | Complete DNA conversion | 100 EP + Stage 2 unlock |

**Milestone: DNA Conversion**

This is the major gate to Stage 2.

Requirements:
1. Build reverse transcriptase enzyme
2. Accumulate 80+ nucleotides
3. Maintain stable RNA for 10 minutes
4. Complete conversion at clay bed (takes ~2 minutes of safety)

Upon completion:
- RNA genome converts to DNA
- Stability massively increases
- New gene slots unlock
- Visual change (nucleus-like region appears)

### 1.8 Competition (Light)

Other protocells exist but interaction is minimal:

- **Resource competition**: Finite resources in areas
- **Clay bed capacity**: Limited slots per bed
- **No direct combat**: Can't damage each other yet
- **Accidental disruption**: Bumping can knock off clay beds

**NPC Protocells:**
- AI-controlled
- Compete for resources
- Can "succeed" and transition (environmental pressure)
- Successful ones become Stage 2 NPCs later

### 1.9 Transition to Stage 2

**Automatic Transition Triggers:**
1. DNA conversion complete
2. At least 3 functional enzymes built
3. Membrane stability above 70%
4. Survived for cumulative 2+ hours

**Transition Sequence:**
1. "Your lineage stabilizes..."
2. Cell visibly transforms:
   - Membrane thickens
   - Internal structure appears
   - Slight size increase
3. "You have become a true cell."
4. Tutorial for new mechanics
5. Spawn in Prokaryote zone

---

# STAGE 2: PROKARYOTE

## Scientific Basis

Prokaryotes (bacteria and archaea) dominated Earth for over 2 billion years. They:
- Have rigid cell walls (can't engulf)
- Compete through chemical warfare
- Exchange genes horizontally
- Form cooperative biofilms
- Diversified into countless metabolic strategies

## Overview

| Aspect | Details |
|--------|---------|
| Duration | 4-5 days (12-15 hours) |
| Style | Territory Control / Chemical Warfare |
| Perspective | Top-down 2D |
| Multiplayer | Full PvP, cooperation possible |
| Death | Respawn with adaptations |

## Core Loop

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  ESTABLISH ──→ PRODUCE ──→ COMPETE ──→ ACQUIRE              │
│      ↑                                    │                 │
│      │    ┌───────────────────────────────┘                 │
│      │    ↓                                                 │
│      │  GROW ──→ SPECIALIZE ──→ COOPERATE?                 │
│      │    │            │              │                     │
│      │    ↓            ↓              ↓                     │
│      └─[DEATH]    [BIOFILM]    [MITOCHONDRIA HUNT]         │
│           │                           │                     │
│           ↓                           ↓                     │
│       ADAPTATION                  STAGE 3                   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Detailed Mechanics

### 2.1 The Rigid Cell Wall

**This is the defining limitation of Stage 2.**

Your cell has a rigid peptidoglycan wall that:
- Prevents engulfment (NO agar.io mechanics yet)
- Provides protection from osmotic stress
- Maintains shape
- Can be weaponized (spikes, etc.)

**Implications:**
- You cannot eat other cells whole
- Combat is chemical, not physical
- You kill by lysing (bursting) enemies, then absorb released contents

```csharp
public class ProkaryoteCell : Cell
{
    [Header("Cell Wall")]
    public float wallIntegrity = 100f;
    public float wallThickness = 1f;
    public bool hasCapusle = false;  // Extra protection layer
    
    public override bool CanEngulf()
    {
        return false;  // NEVER - rigid wall prevents this
    }
    
    public override void OnCollisionWithCell(Cell other)
    {
        // No engulfment - just bounce off
        // Unless you have penetrating weapons (spikes/toxins)
        
        if (HasWeapon(WeaponType.Spike))
        {
            other.TakeDamage(spikeDamage, DamageType.Piercing);
        }
    }
}
```

### 2.2 Chemical Warfare

**Primary Combat Mechanic**

Instead of eating enemies, you:
1. Produce and secrete chemical weapons
2. These damage nearby cells
3. Damaged cells eventually lyse (burst)
4. You absorb the released nutrients/genes

**Weapon Types:**

| Weapon | Range | Effect | Counter |
|--------|-------|--------|---------|
| Antibiotics | Medium | Cell wall damage | Resistance genes |
| Toxins | Short | Protein damage | Detox enzymes |
| Lysozymes | Contact | Direct wall lysis | Thick wall |
| Acid Secretion | Area | pH damage | Buffer capacity |
| Oxidants | Medium | General damage | Antioxidant enzymes |

**Production System:**

```csharp
public class ChemicalProduction : MonoBehaviour
{
    [Header("Production Capacity")]
    public float maxProductionRate = 10f;
    public float currentEnergy;
    
    [Header("Active Productions")]
    public List<ChemicalWeapon> activeWeapons = new();
    
    public void ProduceWeapon(WeaponType type, float intensity)
    {
        float energyCost = GetEnergyCost(type) * intensity;
        if (currentEnergy < energyCost) return;
        
        currentEnergy -= energyCost;
        
        // Create chemical cloud/projectile
        ChemicalWeapon weapon = Instantiate(GetWeaponPrefab(type));
        weapon.intensity = intensity;
        weapon.source = this.cell;
        weapon.transform.position = GetSecretionPoint();
        
        activeWeapons.Add(weapon);
    }
}

public class ChemicalWeapon : MonoBehaviour
{
    public WeaponType type;
    public float intensity;
    public float lifetime;
    public float radius;
    public Cell source;
    
    void Update()
    {
        lifetime -= Time.deltaTime;
        if (lifetime <= 0) Destroy(gameObject);
        
        // Expand radius over time (diffusion)
        radius += diffusionRate * Time.deltaTime;
        
        // Intensity decreases as it spreads
        intensity *= (1 - decayRate * Time.deltaTime);
    }
    
    void OnTriggerStay2D(Collider2D other)
    {
        Cell target = other.GetComponent<Cell>();
        if (target == null || target == source) return;
        
        float resistance = target.GetResistance(type);
        float damage = intensity * (1 - resistance) * Time.deltaTime;
        target.TakeDamage(damage, GetDamageType());
    }
}
```

**Visual Representation:**
- Chemicals appear as colored clouds/waves
- Antibiotics: Blue/purple
- Toxins: Green
- Acid: Yellow/orange
- Each has distinct particle effects

### 2.3 Gene Acquisition

**Horizontal Gene Transfer (HGT)**

Prokaryotes can exchange genes without reproduction:

**Method 1: Conjugation (Direct Contact)**
- Touch another cell for 5+ seconds
- Chance to exchange genes
- Works with players and NPCs
- Both parties can gain genes

```csharp
public class Conjugation : MonoBehaviour
{
    public float contactTimeRequired = 5f;
    private Dictionary<Cell, float> contactTimes = new();
    
    void OnCollisionStay2D(Collision2D collision)
    {
        Cell other = collision.gameObject.GetComponent<Cell>();
        if (other == null) return;
        
        if (!contactTimes.ContainsKey(other))
            contactTimes[other] = 0f;
        
        contactTimes[other] += Time.deltaTime;
        
        if (contactTimes[other] >= contactTimeRequired)
        {
            AttemptGeneTransfer(other);
            contactTimes[other] = 0f;
        }
    }
    
    void AttemptGeneTransfer(Cell other)
    {
        // Each cell offers a random gene
        Gene myOffer = GetRandomTransferableGene();
        Gene theirOffer = other.GetRandomTransferableGene();
        
        // Chance-based acquisition
        if (Random.value < transferSuccessChance)
        {
            if (theirOffer != null && !HasGene(theirOffer.id))
            {
                AcquireGene(theirOffer);
                ShowGeneAcquisitionEffect(theirOffer);
            }
        }
        
        // They might get yours too (handled on their side)
    }
}
```

**Method 2: Transformation (Environmental DNA)**
- Dead/lysed cells release DNA fragments
- Swim through debris to absorb
- Random genes from defeated enemies

**Method 3: Transduction (Viral Transfer)**
- Rare "bacteriophage" events
- Virus transfers genes between cells
- Can be beneficial or harmful
- Creates interesting moments

### 2.4 Biofilm Formation

**Early Cooperation Mechanic**

Players can form biofilms - sticky communities attached to surfaces:

**Benefits:**
- Shared defense (outer cells protect inner)
- Resource pooling
- Gene exchange opportunities
- Resistance to hazards
- Safe zone properties

**Formation:**
1. Produce adhesion proteins (extracellular matrix)
2. Attach to surface
3. Other cells can attach to you
4. Network grows

```csharp
public class BiofilmSystem : MonoBehaviour
{
    public bool isInBiofilm = false;
    public Biofilm currentBiofilm;
    public List<Cell> connectedCells = new();
    
    public float adhesionStrength = 0f;
    public bool canInitiateBiofilm = false;
    
    public void AttemptJoinBiofilm(Biofilm biofilm)
    {
        if (adhesionStrength < minAdhesionRequired) return;
        
        biofilm.AddMember(this.cell);
        currentBiofilm = biofilm;
        isInBiofilm = true;
        
        // Reduce movement
        cell.movementSystem.maxSpeed *= 0.3f;
        
        // Gain protection
        cell.damageReduction += biofilm.GetProtectionBonus();
    }
    
    public void LeaveBiofilm()
    {
        currentBiofilm.RemoveMember(this.cell);
        cell.movementSystem.maxSpeed /= 0.3f;
        cell.damageReduction -= currentBiofilm.GetProtectionBonus();
        currentBiofilm = null;
        isInBiofilm = false;
    }
}

public class Biofilm : MonoBehaviour
{
    public List<Cell> members = new();
    public float totalBiomass;
    public float sharedResources;
    
    public float GetProtectionBonus()
    {
        // More members = more protection
        return Mathf.Min(0.5f, members.Count * 0.05f);
    }
    
    public void ShareResources()
    {
        // Pool and redistribute resources
        float total = members.Sum(m => m.resources.currentStorage);
        float perMember = total / members.Count;
        
        foreach (var member in members)
        {
            member.resources.currentStorage = perMember;
        }
    }
}
```

### 2.5 Metabolism Specialization

**Players choose a metabolic strategy:**

| Strategy | Energy Source | Pros | Cons |
|----------|--------------|------|------|
| Chemotroph | Chemical reactions (vents) | Steady energy, independent | Limited locations |
| Fermenter | Organic compounds | Works anywhere | Low efficiency |
| Phototroph | Light | Unlimited energy | Must stay near surface |
| Parasite | Other cells | High reward | High risk |

**Metabolic Upgrades:**

```csharp
public enum MetabolismType
{
    Basic,          // Starting - fermentation only
    Chemosynthesis, // Vent energy
    Photosynthesis, // Light energy
    Respiration,    // Oxygen-based (efficient)
    Parasitic       // Steal from others
}

public class Metabolism : MonoBehaviour
{
    public MetabolismType primaryMetabolism = MetabolismType.Basic;
    public List<MetabolismType> secondaryMetabolisms = new();
    
    public float CalculateEnergyProduction()
    {
        float total = 0f;
        
        switch (primaryMetabolism)
        {
            case MetabolismType.Basic:
                total = organicResources * 0.5f;  // Low efficiency
                break;
            case MetabolismType.Chemosynthesis:
                total = GetVentProximity() * chemosynthesisRate;
                break;
            case MetabolismType.Photosynthesis:
                total = GetLightLevel() * photosynthesisRate;
                break;
            case MetabolismType.Respiration:
                if (HasOxygen())
                    total = organicResources * 2f;  // High efficiency
                break;
        }
        
        return total;
    }
}
```

### 2.6 The Great Oxygenation Event

**Mid-Stage Event (Environmental)**

As more cells develop photosynthesis, oxygen accumulates:

1. **Early Stage 2**: No oxygen, anaerobic life
2. **Mid Stage 2**: Oxygen pockets form near phototrophs
3. **Late Stage 2**: Oxygen spreads, becomes hazard to anaerobes

**Effects:**
- Anaerobic cells take damage in oxygen
- Aerobic respiration becomes possible (big energy boost)
- Forces adaptation or retreat to anaerobic zones
- Creates new niches

**This is a server-wide event that changes the meta.**

### 2.7 Objectives and Milestones

**Stage 2 Main Objectives:**

| Objective | Requirement | Reward |
|-----------|-------------|--------|
| First Kill | Lyse another cell | 20 EP |
| Chemical Master | Produce 3 weapon types | 30 EP |
| Gene Collector | Acquire 10 genes via HGT | 50 EP |
| Biofilm Founder | Start biofilm with 3+ members | 40 EP |
| Metabolism Specialist | Max out one metabolism type | 35 EP |
| Oxygen Adapter | Survive GOE | 50 EP + Respiration unlock |
| Mitochondria Hunter | Find alpha-proteobacterium | Stage 3 unlock |

### 2.8 Finding the Mitochondria Candidate

**This is the MAJOR GATE to Stage 3.**

**The Alpha-Proteobacterium:**
- Rare NPC cell type
- Small, fast, very energy-efficient
- Found near oxygen-rich areas (they're aerobic)
- Can't be killed normally - must be "captured"

**Discovery:**
- Random spawns after certain conditions met
- Player must have:
  - Aerobic respiration capability
  - Certain engulfment-precursor genes
  - Large enough size

**The Hunt:**
- Locate alpha-proteobacterium
- Chase and corner (they're fast)
- Use special "primitive engulfment" ability
- This is a skill challenge

```csharp
public class MitochondriaCandidate : MonoBehaviour
{
    public float fleeSpeed = 8f;  // Faster than most prokaryotes
    public float detectionRange = 10f;
    public bool isBeingChased = false;
    
    void Update()
    {
        // Flee from large cells
        var threats = FindThreatsInRange(detectionRange);
        if (threats.Count > 0)
        {
            isBeingChased = true;
            Vector2 fleeDirection = CalculateFleeDirection(threats);
            rb.velocity = fleeDirection * fleeSpeed;
        }
        else
        {
            isBeingChased = false;
            Wander();
        }
    }
    
    public bool CanBeCapturedBy(Cell cell)
    {
        return cell.HasGene("GENE_PROTO_ENGULF") &&
               cell.size >= minSizeToCapture &&
               cell.HasAerobicRespiration();
    }
}
```

---

# STAGE 3: THE MITOCHONDRIA EVENT

## Scientific Basis

Endosymbiosis: ~2 billion years ago, a large archaeon engulfed an alpha-proteobacterium. Instead of digesting it, they formed a symbiosis. The bacterium became the mitochondrion. This happened ONCE - all eukaryotes descend from this event.

## Overview

| Aspect | Details |
|--------|---------|
| Duration | 1 session (30-60 min) |
| Style | Quest / Boss Encounter |
| Multiplayer | Solo instance |
| Death | Restart with major adaptation bonus |

## The Event Sequence

### 3.1 Pre-Event Requirements

**To trigger the event, player must have:**
- [ ] Aerobic respiration capability
- [ ] "Proto-engulfment" gene (rare, from HGT or mutation)
- [ ] Size threshold met (top 20% of prokaryotes)
- [ ] Located an alpha-proteobacterium
- [ ] Sufficient energy reserves

### 3.2 The Chase Phase

**Objective:** Corner the alpha-proteobacterium

**Mechanics:**
- The candidate flees at high speed
- Player must use environment to trap it
- Can't damage it (or it dies and you fail)
- Other hazards still active

**Strategy Elements:**
- Herd toward walls/corners
- Use biofilm allies to block escape routes
- Anticipate movement patterns
- Manage your energy (chasing is expensive)

**Duration:** Variable (2-10 minutes typically)

### 3.3 The Capture Phase

**Objective:** Engulf without killing

**Mechanic: Primitive Engulfment**

Unlike later agar.io engulfment, this is:
- Slow and difficult
- Requires precise positioning
- Cell wall must temporarily soften (energy cost)
- Risk of candidate escaping

```csharp
public class PrimitiveEngulfment : MonoBehaviour
{
    public float engulfmentProgress = 0f;
    public float requiredProgress = 100f;
    public MitochondriaCandidate target;
    
    public void AttemptEngulf(MitochondriaCandidate candidate)
    {
        if (!CanEngulf(candidate)) return;
        
        target = candidate;
        StartCoroutine(EngulfmentProcess());
    }
    
    IEnumerator EngulfmentProcess()
    {
        // Soften cell wall (temporary vulnerability)
        cell.wallIntegrity *= 0.5f;
        cell.movementSpeed *= 0.2f;
        
        while (engulfmentProgress < requiredProgress)
        {
            // Must maintain contact
            if (Vector2.Distance(transform.position, target.transform.position) > contactRange)
            {
                // Target escaping!
                engulfmentProgress -= escapeRate * Time.deltaTime;
                
                if (engulfmentProgress <= 0)
                {
                    FailEngulfment("Target escaped");
                    yield break;
                }
            }
            else
            {
                engulfmentProgress += engulfRate * Time.deltaTime;
                
                // Energy cost
                cell.ConsumeEnergy(engulfEnergyCost * Time.deltaTime);
                
                if (cell.currentEnergy <= 0)
                {
                    FailEngulfment("Ran out of energy");
                    yield break;
                }
            }
            
            // Visual progress
            UpdateEngulfmentVisual(engulfmentProgress / requiredProgress);
            
            yield return null;
        }
        
        // Success!
        CompleteEngulfment();
    }
    
    void CompleteEngulfment()
    {
        target.gameObject.SetActive(false);
        cell.hasInternalizedCandidate = true;
        
        // Begin integration phase
        integrationSystem.BeginIntegration(target);
    }
}
```

**UI During Capture:**

```
┌─────────────────────────────────────────────────────────────┐
│                    ENGULFMENT IN PROGRESS                   │
│                                                             │
│    ████████████████████░░░░░░░░░░░░░░░░░░░░  67%           │
│                                                             │
│    Energy: ████████░░░░░░░░                                │
│    Wall Integrity: ████░░░░░░░░ (VULNERABLE)               │
│                                                             │
│    [!] Maintain contact! Target is struggling!              │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 3.4 The Integration Phase

**Objective:** Stabilize the symbiosis without killing or being killed

**This is a minigame balancing two dangers:**

1. **Immune Response** (killing the symbiont)
   - Your cell naturally tries to digest foreign material
   - Must suppress this response
   - Too much suppression = other risks

2. **Symbiont Overgrowth** (being overwhelmed)
   - The bacterium reproduces
   - Too many = they burst your cell
   - Must control their population

**The Balance Mechanic:**

```csharp
public class IntegrationMinigame : MonoBehaviour
{
    [Header("Balance Meters")]
    public float immuneResponse = 50f;      // 0 = no response, 100 = full attack
    public float symbiontPopulation = 1f;   // Starting with 1 symbiont
    
    [Header("Thresholds")]
    public float deathByDigestion = 0f;     // Immune response kills symbiont
    public float deathByOvergrowth = 10f;   // Too many symbionts
    public float stabilizationThreshold = 3f; // Need 3+ for stable mitochondria
    
    [Header("Controls")]
    public float suppressionRate = 0.5f;    // How fast player can suppress immune
    public float growthRate = 0.1f;         // Symbiont reproduction
    
    public float integrationTime = 0f;
    public float requiredIntegrationTime = 60f;  // 1 minute of stability
    
    void Update()
    {
        // Symbiont population changes
        float growthModifier = (100f - immuneResponse) / 100f;
        symbiontPopulation += growthRate * growthModifier * Time.deltaTime;
        
        // Immune response naturally rises
        immuneResponse += naturalImmuneRise * Time.deltaTime;
        
        // Player input: suppress immune response
        if (Input.GetKey(KeyCode.Space))
        {
            immuneResponse -= suppressionRate * Time.deltaTime;
            // But this has energy cost
            cell.ConsumeEnergy(suppressionEnergyCost * Time.deltaTime);
        }
        
        // Clamp values
        immuneResponse = Mathf.Clamp(immuneResponse, 0f, 100f);
        
        // Check failure conditions
        if (symbiontPopulation <= deathByDigestion)
        {
            Fail("Your immune response destroyed the symbiont");
        }
        else if (symbiontPopulation >= deathByOvergrowth)
        {
            Fail("The symbionts overwhelmed your cell");
        }
        
        // Check stability
        if (symbiontPopulation >= stabilizationThreshold && 
            immuneResponse > 20f && immuneResponse < 80f)
        {
            // In stable range
            integrationTime += Time.deltaTime;
            
            if (integrationTime >= requiredIntegrationTime)
            {
                Success();
            }
        }
        else
        {
            // Lost stability
            integrationTime = Mathf.Max(0, integrationTime - Time.deltaTime * 0.5f);
        }
    }
}
```

**UI During Integration:**

```
┌─────────────────────────────────────────────────────────────┐
│                  SYMBIOSIS INTEGRATION                      │
│                                                             │
│   IMMUNE RESPONSE                    SYMBIONT POPULATION    │
│   ████████████░░░░░░░░░░░░░░░        ●●●●○○○○○○            │
│   [DANGER: Too high!]                 4 symbionts           │
│                                                             │
│                 ┌─────────────┐                             │
│   KILL ←       │  STABLE     │       → OVERWHELM           │
│   SYMBIONT     │   ZONE      │          YOU                │
│                └─────────────┘                             │
│                      ▲                                      │
│                   YOU ARE HERE                              │
│                                                             │
│   STABILITY TIME: ████████████░░░░░░░░  34/60 seconds      │
│                                                             │
│   [HOLD SPACE] to suppress immune response (costs energy)   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 3.5 Success: Transformation

**Upon successful integration:**

1. **Immediate Effects:**
   - Symbiont becomes permanent mitochondrion
   - Energy production: +1000%
   - Cell wall begins dissolving
   - Size increases significantly

2. **Cinematic Sequence:**
   - Camera zoom out
   - Cell pulses with new energy
   - Internal structures reorganize
   - "A new kind of life emerges..."

3. **Mechanical Changes:**
   - Movement speed doubles
   - Can now fully engulf (agar.io unlocks!)
   - New organelle system unlocks
   - Enter Eukaryote stage

### 3.6 Failure Handling

**If you fail:**
- Cell dies (digestion or bursting)
- BUT: Massive adaptation XP toward mitochondria-related stats
- Easier on next attempt (hidden assistance scaling)
- Can retry as soon as you find another candidate

**Design Intent:** This should be hard but not gatekeeping. 2-4 attempts expected for average player.

---

# STAGE 4: EUKARYOTE

## Scientific Basis

Eukaryotes are fundamentally different from prokaryotes:
- 10-100x larger
- Internal membrane system (nucleus, ER, Golgi)
- Flexible membrane (no rigid wall)
- Mitochondria for energy
- Later: chloroplasts, sexual reproduction

## Overview

| Aspect | Details |
|--------|---------|
| Duration | 4-5 days (12-15 hours) |
| Style | Agar.io / Predation |
| Perspective | Top-down 2D |
| Multiplayer | Full PvP + Co-op |
| Death | Respawn with adaptations |

## Core Loop

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  HUNT ──→ ENGULF ──→ DIGEST ──→ GROW ──→ EVOLVE            │
│    ↑                                          │             │
│    │         ┌────────────────────────────────┘             │
│    │         ↓                                              │
│    │      DIVIDE ──→ OFFSPRING ──→ [PLAY AS OFFSPRING]     │
│    │         │                                              │
│    │         ↓                                              │
│    └──── [DEATH]                                            │
│              │                                              │
│              ↓                                              │
│         ADAPTATION                                          │
│                                                             │
│  OPTIONAL PATHS:                                            │
│  ─→ CHLOROPLAST ACQUISITION (become autotroph)             │
│  ─→ SEXUAL REPRODUCTION (gene mixing)                      │
│  ─→ COLONY PREPARATION (adhesion proteins)                 │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Detailed Mechanics

### 4.1 The Flexible Membrane

**This is the core change from Stage 2.**

Your membrane is now:
- Deformable (soft-body physics)
- Player-sculptable
- Capable of engulfment (phagocytosis)
- Vulnerable to piercing

**Membrane Sculpting:**
(As detailed in the Cell Evolution System document)
- Push, pull, pinch, stretch
- Proteins determine what shapes hold
- Emergent structures (flagella, pseudopods, etc.)

### 4.2 Engulfment (Agar.io Core)

**Size Rules:**
- Must be 20%+ larger to engulf
- Equal size = stalemate (bounce off)
- Much smaller = instant engulf
- Slightly larger = extended engulfment process

```csharp
public class Engulfment : MonoBehaviour
{
    public float minSizeRatioToEngulf = 1.2f;
    
    void OnCollisionStay2D(Collision2D collision)
    {
        Cell prey = collision.gameObject.GetComponent<Cell>();
        if (prey == null) return;
        
        float sizeRatio = cell.GetSize() / prey.GetSize();
        
        if (sizeRatio >= minSizeRatioToEngulf)
        {
            StartEngulfment(prey);
        }
    }
    
    void StartEngulfment(Cell prey)
    {
        // Check membrane flexibility at contact point
        int[] contactVertices = GetContactVertices();
        float flexibility = cell.membrane.GetAverageFlexibility(contactVertices);
        
        if (flexibility < minFlexibilityToEngulf)
        {
            // Too rigid here - can't wrap around prey
            return;
        }
        
        StartCoroutine(EngulfmentProcess(prey));
    }
    
    IEnumerator EngulfmentProcess(Cell prey)
    {
        float progress = 0f;
        
        while (progress < 1f)
        {
            // Wrap membrane around prey
            WrapMembraneAround(prey, progress);
            
            // Progress based on size difference and flexibility
            float sizeAdvantage = (cell.GetSize() / prey.GetSize()) - 1f;
            float flexAdvantage = cell.membrane.averageFlexibility;
            
            progress += (sizeAdvantage * flexAdvantage) * engulfSpeed * Time.deltaTime;
            
            // Prey can resist/escape
            if (prey.AttemptEscape())
            {
                progress -= escapeStrength * Time.deltaTime;
            }
            
            // Check if prey escaped
            if (progress <= 0f)
            {
                CancelEngulfment(prey);
                yield break;
            }
            
            yield return null;
        }
        
        // Fully engulfed
        CompleteEngulfment(prey);
    }
    
    void CompleteEngulfment(Cell prey)
    {
        // Create food vacuole
        Vacuole vacuole = CreateVacuoleAround(prey);
        
        // Begin digestion
        digestionSystem.BeginDigestion(vacuole, prey);
        
        // Chance to acquire genes
        geneAcquisition.OnCellConsumed(prey);
        
        // Size increase
        float nutrients = prey.GetNutrientValue();
        cell.Grow(nutrients);
    }
}
```

### 4.3 Emergent Structures

**Fully implemented as per Cell Evolution System document:**

| Structure | Requirements | Function |
|-----------|--------------|----------|
| Flagellum | Thin protrusion + flagellin protein | Fast directional movement |
| Cilia | Multiple small flagella | Precise movement, filter feeding |
| Pseudopod | Broad protrusion + actin | Crawling, engulfment |
| Spike | Rigid protrusion + keratin | Defense, damage |
| Vacuole | Internal pocket | Storage, digestion |
| Photosynthetic Region | Chloroplast + surface area | Energy from light |

### 4.4 Division (Mitosis)

**Reproduction mechanic:**

**Requirements to Divide:**
- Reach size threshold (2x starting size)
- Sufficient resources stored
- No active engulfments
- Find safe location (or risk dying mid-division)

**The Division Process:**

```csharp
public class Mitosis : MonoBehaviour
{
    public float divisionProgress = 0f;
    public float divisionTime = 30f;  // 30 seconds to divide
    
    public void BeginDivision()
    {
        if (!CanDivide()) return;
        
        // Commit to division
        isDividing = true;
        cell.movementEnabled = false;  // Vulnerable!
        
        StartCoroutine(DivisionProcess());
    }
    
    IEnumerator DivisionProcess()
    {
        // Phase 1: DNA replication (internal, visual only)
        yield return ShowDNAReplication(5f);
        
        // Phase 2: Organelle duplication
        yield return ShowOrganelleDuplication(5f);
        
        // Phase 3: Membrane pinching (visible constriction)
        while (divisionProgress < 1f)
        {
            divisionProgress += Time.deltaTime / divisionTime;
            
            // Visual: cell pinches in middle
            cell.membrane.ApplyDivisionConstriction(divisionProgress);
            
            // Can be killed during this!
            if (cell.health <= 0)
            {
                FailDivision();
                yield break;
            }
            
            yield return null;
        }
        
        // Phase 4: Separation
        CompleteDivision();
    }
    
    void CompleteDivision()
    {
        // Create offspring cell
        Cell offspring = Instantiate(cellPrefab);
        
        // Split resources
        float halfResources = cell.resources.currentStorage / 2f;
        cell.resources.currentStorage = halfResources;
        offspring.resources.currentStorage = halfResources;
        
        // Split size
        float halfSize = cell.GetSize() / 2f;
        cell.SetSize(halfSize);
        offspring.SetSize(halfSize);
        
        // Copy genes (with possible mutation)
        offspring.genome = cell.genome.CopyWithMutation(mutationChance);
        
        // Copy protein layout (roughly)
        offspring.membrane.CopyLayoutFrom(cell.membrane);
        
        // Position offspring nearby
        offspring.transform.position = cell.transform.position + GetSeparationOffset();
        
        // Player choice: continue as parent or switch to offspring
        ShowOffspringChoice(cell, offspring);
    }
}
```

**Post-Division Choice:**
- Continue as parent (keep current position/situation)
- Switch to offspring (fresh start, possibly different mutations)
- This adds strategic layer

### 4.5 Sexual Reproduction (Optional)

**Gene mixing mechanic for players who want variety:**

**Requirements:**
- Both cells must have "mating type" genes (compatibility)
- Similar size (within 30%)
- Both consent (multiplayer handshake)
- Sufficient energy

**Process:**
1. Cells approach and signal compatibility
2. Temporary fusion (membranes merge briefly)
3. Gene shuffling occurs
4. Split into 2-4 offspring with mixed genes
5. Players each control one offspring

**Benefits:**
- Novel gene combinations
- Can get genes you couldn't otherwise access
- Social/emergent gameplay

**Risks:**
- Vulnerable during mating
- Might get bad gene combinations
- Energy expensive

### 4.6 Chloroplast Acquisition

**Second Endosymbiosis Event (Optional Path)**

Similar to mitochondria event but:
- Target: Cyanobacterium (photosynthetic prokaryote)
- Location: Near surface, high light areas
- Easier than mitochondria (you have experience now)

**Benefits:**
- Photosynthesis capability
- Energy independence
- Unlocks autotroph playstyle
- Green coloration (visual)

**Trade-offs:**
- Must stay near light
- Slower movement (maintaining chloroplasts)
- Different build options

### 4.7 Safe Zones

**Eukaryote-specific safe zones:**

| Zone Type | Properties |
|-----------|------------|
| Debris Field | Dead organic matter, easy feeding |
| Algae Forest | Light + cover, good for phototrophs |
| Deep Crevice | Dark, safe, limited resources |
| Bacterial Mat | Can harvest prokaryotes safely |
| Colony | Player-made, requires adhesion |

### 4.8 Objectives and Milestones

**Stage 4 Main Objectives:**

| Objective | Requirement | Reward |
|-----------|-------------|--------|
| First Engulfment | Successfully engulf a cell | 25 EP |
| Predator | Engulf 10 cells in one life | 40 EP |
| Apex Predator | Reach top 10% size | 60 EP |
| First Division | Successfully divide | 50 EP |
| Lineage | 5 successful divisions | 100 EP |
| Photosynthesizer | Acquire chloroplast | 80 EP |
| Gene Mixer | Sexual reproduction | 50 EP |
| Colony Ready | Max adhesion proteins | Stage 5 unlock |

### 4.9 Transition to Colony Stage

**Requirements:**
- [ ] Adhesion protein genes (cadherin, etc.)
- [ ] Successful divisions (proves viability)
- [ ] Contact with other ready players
- [ ] Choose to commit to colony

---

# STAGE 5: COLONY FORMATION

## Scientific Basis

Multicellularity evolved independently 25+ times. The transition involves:
1. Cells staying attached after division
2. Specialization (different roles)
3. Interdependence (can't survive alone)
4. Coordinated behavior

## Overview

| Aspect | Details |
|--------|---------|
| Duration | 3-4 days (9-12 hours) |
| Style | Cooperative / Team-based |
| Multiplayer | Required (3+ players) |
| Death | Individual or colony death |

## Core Loop

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  FIND PARTNERS ──→ ATTACH ──→ SPECIALIZE ──→ COORDINATE    │
│        ↑                                         │          │
│        │         ┌───────────────────────────────┘          │
│        │         ↓                                          │
│        │      SURVIVE ──→ GROW COLONY ──→ DIFFERENTIATE    │
│        │         │                              │           │
│        │         ↓                              ↓           │
│        └──── [COLONY DEATH]              [CREATURE STAGE]  │
│                  │                                          │
│                  ↓                                          │
│             INDIVIDUAL RESPAWN                              │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Detailed Mechanics

### 5.1 Colony Formation

**Finding Partners:**
- Matchmaking system for colony-ready players
- Can also form with friends directly
- Minimum 3 players to start
- Maximum ~20 players per colony (performance)

**Physical Attachment:**

```csharp
public class ColonyAttachment : MonoBehaviour
{
    public bool isInColony = false;
    public Colony colony;
    public List<Cell> directlyAttached = new();  // Cells I'm touching
    
    public void AttachTo(Cell other)
    {
        if (!other.isInColony)
        {
            // Start new colony
            colony = CreateNewColony(this.cell, other);
        }
        else
        {
            // Join existing colony
            other.colony.AddMember(this.cell);
            colony = other.colony;
        }
        
        directlyAttached.Add(other);
        other.directlyAttached.Add(this.cell);
        
        // Create physical joint
        SpringJoint2D joint = gameObject.AddComponent<SpringJoint2D>();
        joint.connectedBody = other.GetComponent<Rigidbody2D>();
        joint.distance = GetOptimalDistance();
        joint.frequency = 2f;
        
        isInColony = true;
    }
    
    public void Detach()
    {
        // Can only detach if on edge of colony
        if (!IsOnColonyEdge())
        {
            ShowMessage("Can't detach - you're in the middle of the colony!");
            return;
        }
        
        colony.RemoveMember(this.cell);
        
        foreach (var attached in directlyAttached)
        {
            attached.directlyAttached.Remove(this.cell);
        }
        
        // Remove joints
        foreach (var joint in GetComponents<SpringJoint2D>())
        {
            Destroy(joint);
        }
        
        directlyAttached.Clear();
        colony = null;
        isInColony = false;
    }
}
```

### 5.2 Role Specialization

**Players choose/vote on roles. Once chosen, cells transform:**

| Role | Function | Abilities | Limitations |
|------|----------|-----------|-------------|
| Motility Cell | Movement | Flagella, steering | Can't feed or reproduce |
| Feeding Cell | Nutrition | Engulfment, digestion | Slow, vulnerable |
| Structural Cell | Shape/Defense | Rigid, protective | Immobile |
| Sensory Cell | Detection | Extended range, signaling | Fragile |
| Reproductive Cell | Colony reproduction | Division capability | Protected, passive |

**Role Selection:**

```csharp
public class ColonyRoleSystem : MonoBehaviour
{
    public Dictionary<ColonyRole, int> roleLimits = new()
    {
        { ColonyRole.Motility, -1 },      // No limit
        { ColonyRole.Feeding, -1 },       // No limit
        { ColonyRole.Structural, -1 },    // No limit
        { ColonyRole.Sensory, 4 },        // Max 4
        { ColonyRole.Reproductive, 2 }    // Max 2
    };
    
    public void RequestRole(Cell cell, ColonyRole role)
    {
        // Check if role is available
        int currentCount = colony.members.Count(m => m.role == role);
        int limit = roleLimits[role];
        
        if (limit != -1 && currentCount >= limit)
        {
            cell.ShowMessage($"Colony already has maximum {role} cells");
            return;
        }
        
        // Colony votes (for important roles)
        if (role == ColonyRole.Reproductive)
        {
            StartVote(cell, role);
        }
        else
        {
            AssignRole(cell, role);
        }
    }
    
    void AssignRole(Cell cell, ColonyRole role)
    {
        cell.role = role;
        
        // Transform cell based on role
        switch (role)
        {
            case ColonyRole.Motility:
                cell.EnableFlagellaMode();
                cell.DisableEngulfment();
                cell.DisableDivision();
                break;
            case ColonyRole.Feeding:
                cell.EnableEnhancedEngulfment();
                cell.DisableMovement();
                break;
            case ColonyRole.Structural:
                cell.EnableRigidMode();
                cell.IncreaseDefense(2f);
                cell.DisableMovement();
                cell.DisableEngulfment();
                break;
            case ColonyRole.Sensory:
                cell.EnableSensingMode();
                cell.ExtendDetectionRange(3f);
                break;
            case ColonyRole.Reproductive:
                cell.EnableReproductiveMode();
                // This cell's division will spawn new colony members
                break;
        }
    }
}
```

### 5.3 Coordinated Movement

**Colony movement requires cooperation:**

**Motility Cell Controls:**
- Each motility cell player controls their flagella direction
- Colony moves in averaged direction
- More agreement = faster movement
- Disagreement = spinning or stalling

```csharp
public class ColonyMovement : MonoBehaviour
{
    public void CalculateColonyMovement()
    {
        var motilityCells = colony.members.Where(m => m.role == ColonyRole.Motility);
        
        if (motilityCells.Count() == 0)
        {
            // Colony can't move!
            return;
        }
        
        // Average direction from all motility cells
        Vector2 averageDirection = Vector2.zero;
        foreach (var cell in motilityCells)
        {
            averageDirection += cell.inputDirection;
        }
        averageDirection /= motilityCells.Count();
        
        // Agreement bonus (dot product of all directions)
        float agreement = CalculateAgreement(motilityCells);
        float speedMultiplier = 0.5f + agreement * 0.5f;  // 50-100% speed based on agreement
        
        // Apply to colony center of mass
        Vector2 force = averageDirection * colonyBaseSpeed * speedMultiplier;
        colony.ApplyForce(force);
    }
    
    float CalculateAgreement(IEnumerable<Cell> motilityCells)
    {
        var directions = motilityCells.Select(m => m.inputDirection.normalized).ToList();
        
        float totalAgreement = 0f;
        int comparisons = 0;
        
        for (int i = 0; i < directions.Count; i++)
        {
            for (int j = i + 1; j < directions.Count; j++)
            {
                totalAgreement += Vector2.Dot(directions[i], directions[j]);
                comparisons++;
            }
        }
        
        return comparisons > 0 ? (totalAgreement / comparisons + 1f) / 2f : 1f;
    }
}
```

### 5.4 Resource Sharing

**Feeding cells gather, all cells benefit:**

```csharp
public class ColonyResources : MonoBehaviour
{
    public float sharedPool = 0f;
    public float distributionInterval = 5f;
    
    public void OnFeedingCellGathered(Cell feeder, float amount)
    {
        // Feeding cells contribute to shared pool
        sharedPool += amount;
    }
    
    void DistributeResources()
    {
        if (sharedPool <= 0) return;
        
        // Distribute based on role needs
        float perCell = sharedPool / colony.members.Count;
        
        foreach (var cell in colony.members)
        {
            float need = GetRoleNeed(cell.role);
            float share = perCell * need;
            cell.resources.Add(share);
            sharedPool -= share;
        }
    }
    
    float GetRoleNeed(ColonyRole role)
    {
        return role switch
        {
            ColonyRole.Motility => 1.5f,      // High energy for movement
            ColonyRole.Feeding => 1.0f,       // Normal
            ColonyRole.Structural => 0.5f,    // Low metabolism
            ColonyRole.Sensory => 0.8f,       // Moderate
            ColonyRole.Reproductive => 2.0f,  // Preparing for division
            _ => 1.0f
        };
    }
}
```

### 5.5 Tissue Layers

**As colony grows, tissue differentiation emerges:**

**Layer 1: Ectoderm (Outer)**
- Structural and sensory cells
- First contact with environment
- Protection function

**Layer 2: Endoderm (Inner)**
- Feeding and reproductive cells
- Protected position
- Metabolic function

```csharp
public class TissueLayer : MonoBehaviour
{
    public void OrganizeLayers()
    {
        // Outer layer: structural and sensory
        var outerRoles = new[] { ColonyRole.Structural, ColonyRole.Sensory, ColonyRole.Motility };
        
        // Inner layer: feeding and reproductive
        var innerRoles = new[] { ColonyRole.Feeding, ColonyRole.Reproductive };
        
        // Calculate colony center
        Vector2 center = colony.GetCenterOfMass();
        
        // Sort members by role
        var outerCells = colony.members.Where(m => outerRoles.Contains(m.role));
        var innerCells = colony.members.Where(m => innerRoles.Contains(m.role));
        
        // Encourage proper positioning through attachment preferences
        foreach (var cell in innerCells)
        {
            cell.preferredDistance = 0f;  // Want to be near center
        }
        
        foreach (var cell in outerCells)
        {
            cell.preferredDistance = colony.radius;  // Want to be on edge
        }
    }
}
```

### 5.6 Colony Challenges

**Coordinated challenges that require teamwork:**

| Challenge | Requirements | Reward |
|-----------|--------------|--------|
| Predator Attack | Coordinate defense + escape | Survival + EP |
| Food Scarcity | Efficient gathering + sharing | Resources + EP |
| Environmental Hazard | Protection formation | Resistance + EP |
| Rival Colony | Competition or merger | Territory + EP |

### 5.7 Colony Reproduction

**When reproductive cells divide:**
- New cell joins colony (if space)
- OR buds off as new colony seed
- Colony can split into two

**Colony Growth Stages:**
1. **Initial** (3-5 cells): Fragile, forming
2. **Stable** (6-10 cells): Functional, roles established
3. **Mature** (11-20 cells): Tissue layers, specialized
4. **Ready** (20+ cells): Can transition to creature

### 5.8 Transition to Creature Stage

**Requirements:**
- [ ] Colony size: 15+ cells
- [ ] All role types present
- [ ] Tissue layers formed
- [ ] Survived for 30+ minutes as mature colony
- [ ] Colony-wide vote to transition

**The Transition:**
1. Colony enters "pupation" state
2. Cells reorganize into body plan
3. Individual control merges into creature control
4. Players collectively control one organism

**Post-Transition:**
- One player becomes "primary" controller
- Others become organ systems
- Or: Players rotate control
- Or: Players control different aspects (movement, feeding, etc.)

---

# SUMMARY: Complete Cell Stage Flow

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  STAGE 0: CHEMICAL GENESIS (30-60 min)                     │
│  "Build life from chemistry"                                │
│  Style: Puzzle                                              │
│                           │                                 │
│                           ↓                                 │
│  STAGE 1: PROTOCELL (2-3 days)                             │
│  "Survive as fragile proto-life"                           │
│  Style: Survival/Gathering                                  │
│                           │                                 │
│                           ↓                                 │
│  STAGE 2: PROKARYOTE (4-5 days)                            │
│  "Compete through chemical warfare"                         │
│  Style: Territory Control                                   │
│                           │                                 │
│                           ↓                                 │
│  STAGE 3: MITOCHONDRIA EVENT (30-60 min)                   │
│  "Capture the symbiont"                                     │
│  Style: Quest/Boss                                          │
│                           │                                 │
│                           ↓                                 │
│  STAGE 4: EUKARYOTE (4-5 days)                             │
│  "Hunt, engulf, evolve"                                     │
│  Style: Agar.io                                             │
│                           │                                 │
│                           ↓                                 │
│  STAGE 5: COLONY (3-4 days)                                │
│  "Cooperate, specialize, become one"                        │
│  Style: Team Cooperation                                    │
│                           │                                 │
│                           ↓                                 │
│  CREATURE STAGE (Future development)                        │
│                                                             │
└─────────────────────────────────────────────────────────────┘

Total: ~2 weeks for semi-avid player (2-3 hours/day)
```

---

# APPENDIX: Balance Targets

| Stage | Avg Life | Deaths Expected | EP per Life | Key Metric |
|-------|----------|-----------------|-------------|------------|
| 0 | N/A | 0-2 | N/A | Completion rate 95%+ |
| 1 | 10-20 min | 10-20 | 30-50 | DNA conversion by death 15 |
| 2 | 5-15 min | 20-30 | 40-80 | Mito event by death 30 |
| 3 | N/A | 1-4 attempts | 50-200 | Success by attempt 4 |
| 4 | 8-20 min | 15-25 | 50-100 | Colony ready by death 25 |
| 5 | 30-60 min | 5-10 colony deaths | 100-200 | Creature ready by death 10 |

---

# APPENDIX: Accessibility Options

- **Assist Mode**: Reduced difficulty, more checkpoints
- **Solo Colony**: AI partners for players without friends online
- **Pause**: Available in solo instances
- **Colorblind Modes**: Distinct patterns not just colors
- **Speed Options**: Slower gameplay for some stages
