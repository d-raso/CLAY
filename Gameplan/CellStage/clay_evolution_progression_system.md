# Clay: Evolution & Progression System
## Technical Design Document for Implementation

---

## Overview

This document describes how players evolve their cells through a combination of safe zone editing, achievement-based progression, and death-driven adaptation. The goal is to make every moment of gameplay - including death - feel like meaningful progress.

**Core Philosophy:** Death is not failure. Death is your lineage learning.

---

## The Gameplay Loop

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│    ┌─────────────┐                                              │
│    │ SPAWN       │                                              │
│    │ (new cell)  │                                              │
│    └──────┬──────┘                                              │
│           ↓                                                     │
│    ┌─────────────────────────────────────────┐                  │
│    │ SURVIVE & HUNT                          │                  │
│    │ - Agar.io style gameplay                │                  │
│    │ - Eat smaller, avoid larger             │←──────────┐      │
│    │ - Complete achievements                 │           │      │
│    │ - Gather resources                      │           │      │
│    └──────┬──────────────────────────────────┘           │      │
│           ↓                                              │      │
│    ┌─────────────────────────────────────────┐           │      │
│    │ FIND SAFE ZONE                          │           │      │
│    │ - Clay beds, crevices, bacterial mats   │           │      │
│    │ - Strategic locations on map            │           │      │
│    └──────┬──────────────────────────────────┘           │      │
│           ↓                                              │      │
│    ┌─────────────────────────────────────────┐           │      │
│    │ EDIT YOUR CELL                          │           │      │
│    │ - Sculpt membrane                       │           │      │
│    │ - Place proteins                        │           │      │
│    │ - Spend evolution points                │           │      │
│    │ - Unlock new genes                      │           │      │
│    └──────┬──────────────────────────────────┘           │      │
│           ↓                                              │      │
│    ┌─────────────────────────────────────────┐           │      │
│    │ LEAVE SAFE ZONE                         ├───────────┘      │
│    └──────┬──────────────────────────────────┘                  │
│           ↓                                                     │
│    ┌─────────────────────────────────────────┐                  │
│    │ DEATH                                   │                  │
│    │ - Analyze cause                         │                  │
│    │ - Grant adaptation XP                   │                  │
│    │ - Show achievement progress             │                  │
│    └──────┬──────────────────────────────────┘                  │
│           ↓                                                     │
│    ┌─────────────┐                                              │
│    │ RESPAWN     │                                              │
│    │ (lineage    │                                              │
│    │ continues)  ├──────────────────────────────────────────────┘
│    └─────────────┘                                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Safe Zone System

### Safe Zone Types

```csharp
public enum SafeZoneType
{
    ClayBed,          // Primary evolution spots, mineral-rich
    Crevice,          // Small, hidden, limited capacity
    BacterialMat,     // Moderate safety, resource regeneration
    HydrothermalVent, // Dangerous approach, powerful bonuses
    TidePoolEdge,     // Surface access, light for photosynthesis
    DeadCell,         // Temporary, inside large dead cell husk
    Colony            // Player-created, late game
}

[System.Serializable]
public class SafeZone : MonoBehaviour
{
    [Header("Identity")]
    public SafeZoneType type;
    public string zoneName;
    
    [Header("Capacity")]
    public int maxOccupants = 3;
    public float editRadius = 5f;
    
    [Header("Properties")]
    public float protectionStrength = 1f;
    public float editSpeedBonus = 0f;
    public float resourceRegen = 0f;
    public GeneType[] geneAffinities;
    
    [Header("Access")]
    public float approachDanger = 0f;
    public bool requiresAdhesion = false;
    public float minSizeToUse = 0f;
    public float maxSizeToUse = 999f;
    
    [Header("State")]
    public List<Cell> currentOccupants;
    public bool isHidden = true;
}
```

### Safe Zone Properties by Type

| Type | Capacity | Protection | Bonus | Risk |
|------|----------|------------|-------|------|
| Clay Bed | 5-8 | Full | +Gene synthesis, minerals | Low |
| Crevice | 1-2 | Full | +Hiding | Very Low |
| Bacterial Mat | 3-4 | 80% | +Resource regen | Low |
| Hydrothermal Vent | 2-3 | Full | +Energy, +rare genes | High |
| Tide Pool Edge | 4-6 | 70% | +Light, +photosynthesis | Medium |
| Dead Cell Husk | 1 | Full | +Nutrients | Temporary |
| Colony | 10+ | Variable | Customizable | Varies |

### Safe Zone Interaction

```csharp
public class SafeZoneInteraction : MonoBehaviour
{
    private SafeZone currentZone;
    private bool isEditing = false;
    
    void OnTriggerEnter2D(Collider2D other)
    {
        SafeZone zone = other.GetComponent<SafeZone>();
        if (zone == null) return;
        
        if (!CanUseZone(zone))
        {
            ShowZoneRequirements(zone);
            return;
        }
        
        if (zone.currentOccupants.Count >= zone.maxOccupants)
        {
            ShowZoneFullNotification(zone);
            return;
        }
        
        EnterSafeZone(zone);
    }
    
    bool CanUseZone(SafeZone zone)
    {
        float mySize = cell.membrane.GetArea();
        
        if (mySize < zone.minSizeToUse || mySize > zone.maxSizeToUse)
            return false;
        
        if (zone.requiresAdhesion)
        {
            float adhesion = cell.GetTotalProtein(ProteinType.Integrin);
            if (adhesion < minAdhesionRequired)
                return false;
        }
        
        return true;
    }
    
    void EnterSafeZone(SafeZone zone)
    {
        currentZone = zone;
        zone.currentOccupants.Add(cell);
        cell.damageReduction = zone.protectionStrength;
        editingUI.SetActive(true);
        
        if (zone.resourceRegen > 0)
            StartCoroutine(ResourceRegeneration(zone.resourceRegen));
    }
    
    public void BeginEditing()
    {
        if (currentZone == null) return;
        
        isEditing = true;
        cell.movementEnabled = false;
        cameraController.ZoomToEditMode();
        cellEditor.Enable(cell, currentZone);
    }
    
    public void FinishEditing()
    {
        isEditing = false;
        cell.movementEnabled = true;
        cameraController.ZoomToGameplay();
        cellEditor.Disable();
    }
    
    void LeaveSafeZone()
    {
        if (currentZone == null) return;
        
        currentZone.currentOccupants.Remove(cell);
        cell.damageReduction = 0f;
        if (isEditing) FinishEditing();
        editingUI.SetActive(false);
        currentZone = null;
    }
}
```

---

## Achievement System

### Achievement Structure

```csharp
public enum AchievementCategory
{
    Survival,
    Hunting,
    Exploration,
    Evolution,
    Mastery,
    Social,
    Environmental,
    Milestones
}

[System.Serializable]
public class Achievement
{
    public string id;
    public string name;
    public string description;
    public Sprite icon;
    public AchievementCategory category;
    
    [Header("Requirements")]
    public AchievementCondition[] conditions;
    
    [Header("Rewards")]
    public int evolutionPoints;
    public string unlockedGeneId;
    public float adaptationBonus;
    public AdaptationType adaptationCategory;
    
    [Header("Progression")]
    public bool repeatable = false;
    public float repeatCooldown = 300f;
    public int maxRepeats = 10;
    public float repeatPointDecay = 0.8f;
    
    [Header("Tiers")]
    public bool hasTiers = false;
    public int[] tierThresholds;
    public int[] tierRewards;
}
```

### Achievement Examples

```csharp
public static class AchievementDefinitions
{
    // === SURVIVAL ===
    
    public static Achievement FirstMinute = new Achievement
    {
        id = "SURVIVE_60",
        name = "First Steps",
        description = "Survive for 60 seconds",
        category = AchievementCategory.Survival,
        evolutionPoints = 10
    };
    
    public static Achievement CloseCall = new Achievement
    {
        id = "ESCAPE_ENGULF",
        name = "Close Call",
        description = "Escape after being partially engulfed",
        category = AchievementCategory.Survival,
        evolutionPoints = 25,
        repeatable = true,
        repeatCooldown = 60f
    };
    
    // === HUNTING ===
    
    public static Achievement FirstBlood = new Achievement
    {
        id = "FIRST_KILL",
        name = "First Blood",
        description = "Kill your first cell",
        category = AchievementCategory.Hunting,
        evolutionPoints = 15
    };
    
    public static Achievement GiantSlayer = new Achievement
    {
        id = "KILL_LARGER",
        name = "Giant Slayer",
        description = "Kill a cell larger than yourself",
        category = AchievementCategory.Hunting,
        evolutionPoints = 50,
        unlockedGeneId = "GENE_TOXIN",
        repeatable = true,
        repeatCooldown = 180f
    };
    
    public static Achievement Impaler = new Achievement
    {
        id = "KILL_WITH_SPIKE",
        name = "Impaler",
        description = "Kill a cell using spike damage",
        category = AchievementCategory.Hunting,
        evolutionPoints = 30,
        repeatable = true
    };
    
    public static Achievement Rampage = new Achievement
    {
        id = "KILL_SPREE",
        name = "Rampage",
        description = "Kill 5 cells within 60 seconds",
        category = AchievementCategory.Hunting,
        evolutionPoints = 75,
        repeatable = true,
        repeatCooldown = 300f
    };
    
    // === EXPLORATION ===
    
    public static Achievement Explorer = new Achievement
    {
        id = "DISCOVER_ZONE",
        name = "Explorer",
        description = "Discover a safe zone",
        category = AchievementCategory.Exploration,
        evolutionPoints = 10,
        repeatable = true
    };
    
    public static Achievement BiomeDiscovery = new Achievement
    {
        id = "DISCOVER_BIOME",
        name = "New Frontiers",
        description = "Discover a new biome",
        category = AchievementCategory.Exploration,
        evolutionPoints = 30,
        repeatable = true
    };
    
    public static Achievement SurfaceWorld = new Achievement
    {
        id = "REACH_SURFACE",
        name = "Surface World",
        description = "Reach the water's surface",
        category = AchievementCategory.Exploration,
        evolutionPoints = 40,
        unlockedGeneId = "GENE_CHLOROPHYLL"
    };
    
    // === EVOLUTION ===
    
    public static Achievement FirstFlagellum = new Achievement
    {
        id = "CREATE_FLAGELLUM",
        name = "Mobility",
        description = "Create your first flagellum",
        category = AchievementCategory.Evolution,
        evolutionPoints = 20
    };
    
    public static Achievement Innovator = new Achievement
    {
        id = "NOVEL_STRUCTURE",
        name = "Innovator",
        description = "Create a structure the system hasn't categorized",
        category = AchievementCategory.Evolution,
        evolutionPoints = 100,
        repeatable = true,
        repeatCooldown = 600f
    };
    
    public static Achievement Mitosis = new Achievement
    {
        id = "FIRST_DIVISION",
        name = "Mitosis",
        description = "Successfully divide for the first time",
        category = AchievementCategory.Evolution,
        evolutionPoints = 50
    };
    
    // === SOCIAL ===
    
    public static Achievement FirstContact = new Achievement
    {
        id = "CONTACT_PLAYER",
        name = "First Contact",
        description = "Make peaceful contact with another player",
        category = AchievementCategory.Social,
        evolutionPoints = 25,
        unlockedGeneId = "GENE_CADHERIN"
    };
    
    public static Achievement Symbiosis = new Achievement
    {
        id = "COOPERATE_PLAYER",
        name = "Symbiosis",
        description = "Stay attached to another player for 60 seconds",
        category = AchievementCategory.Social,
        evolutionPoints = 50
    };
    
    // === ENVIRONMENTAL ===
    
    public static Achievement AcidSurvivor = new Achievement
    {
        id = "SURVIVE_PH",
        name = "Acid Survivor",
        description = "Survive a sudden pH shift",
        category = AchievementCategory.Environmental,
        evolutionPoints = 30,
        adaptationBonus = 0.1f,
        adaptationCategory = AdaptationType.PHResistance
    };
    
    public static Achievement Thermophile = new Achievement
    {
        id = "SURVIVE_HEAT",
        name = "Thermophile",
        description = "Survive near a hydrothermal vent",
        category = AchievementCategory.Environmental,
        evolutionPoints = 40,
        adaptationBonus = 0.1f,
        adaptationCategory = AdaptationType.HeatResistance
    };
    
    // === MILESTONES ===
    
    public static Achievement Eukaryote = new Achievement
    {
        id = "BECOME_EUKARYOTE",
        name = "Eukaryote",
        description = "Successfully acquire mitochondria",
        category = AchievementCategory.Milestones,
        evolutionPoints = 200
    };
    
    public static Achievement ColonyFounder = new Achievement
    {
        id = "FOUND_COLONY",
        name = "Colony Founder",
        description = "Form a colony with other cells",
        category = AchievementCategory.Milestones,
        evolutionPoints = 150
    };
}
```

### Achievement Tracking

```csharp
public class AchievementSystem : MonoBehaviour
{
    public Dictionary<string, AchievementProgress> progress = new();
    public UnityEvent<Achievement> OnAchievementUnlocked;
    
    void Start()
    {
        GameEvents.OnCellKilled += HandleKill;
        GameEvents.OnZoneDiscovered += HandleZoneDiscovered;
        GameEvents.OnStructureCreated += HandleStructureCreated;
    }
    
    void HandleKill(Cell killer, Cell victim, DamageSource source)
    {
        if (killer != playerCell) return;
        
        float sizeRatio = victim.GetSize() / killer.GetSize();
        
        CheckAchievement("FIRST_KILL");
        
        if (sizeRatio > 0.9f) CheckAchievement("KILL_SAME_SIZE");
        if (sizeRatio > 1.2f) CheckAchievement("KILL_LARGER");
        if (source.structureType == "Spike") CheckAchievement("KILL_WITH_SPIKE");
        if (killer.damageTakenThisFight == 0) CheckAchievement("PERFECT_KILL");
        
        TrackKillSpree();
    }
    
    void CompleteAchievement(Achievement achievement)
    {
        var prog = progress[achievement.id];
        prog.completed = true;
        prog.timesCompleted++;
        
        int points = achievement.evolutionPoints;
        if (achievement.repeatable && prog.timesCompleted > 1)
        {
            points = Mathf.RoundToInt(points * Mathf.Pow(achievement.repeatPointDecay, prog.timesCompleted - 1));
        }
        
        evolutionPoints.Add(points);
        
        if (!string.IsNullOrEmpty(achievement.unlockedGeneId) && prog.timesCompleted == 1)
        {
            geneSystem.UnlockGene(achievement.unlockedGeneId);
        }
        
        if (achievement.adaptationBonus > 0)
        {
            adaptationSystem.AddBonus(achievement.adaptationCategory, achievement.adaptationBonus);
        }
        
        OnAchievementUnlocked?.Invoke(achievement);
        ShowAchievementNotification(achievement, points);
    }
}
```

---

## Death and Adaptation System

### Death Analysis

```csharp
public class DeathAnalyzer : MonoBehaviour
{
    public DeathReport AnalyzeDeath(Cell cell, DamageSource finalBlow, List<DamageEvent> damageHistory)
    {
        var report = new DeathReport();
        
        report.primaryCause = ClassifyDamageSource(finalBlow);
        report.killerInfo = finalBlow.source?.GetCellInfo();
        
        report.contributingFactors = damageHistory
            .GroupBy(d => ClassifyDamageSource(d))
            .OrderByDescending(g => g.Sum(d => d.amount))
            .Take(3)
            .Select(g => new DamageFactor { cause = g.Key, totalDamage = g.Sum(d => d.amount) })
            .ToList();
        
        report.suggestedAdaptations = GetSuggestedAdaptations(report);
        report.survivalTime = cell.aliveTime;
        report.killsThisLife = cell.killCount;
        report.distanceTraveled = cell.distanceTraveled;
        
        return report;
    }
    
    DeathCause ClassifyDamageSource(DamageSource source)
    {
        if (source == null) return DeathCause.Unknown;
        
        return source.type switch
        {
            DamageType.Engulfment => DeathCause.Eaten,
            DamageType.Spike => DeathCause.Pierced,
            DamageType.Toxin => DeathCause.Poisoned,
            DamageType.PHShock => DeathCause.PHShock,
            DamageType.HeatDamage => DeathCause.Overheated,
            DamageType.ColdDamage => DeathCause.Frozen,
            DamageType.Starvation => DeathCause.Starved,
            _ => DeathCause.Unknown
        };
    }
    
    List<AdaptationType> GetSuggestedAdaptations(DeathReport report)
    {
        return report.primaryCause switch
        {
            DeathCause.Eaten => new List<AdaptationType> { AdaptationType.Speed, AdaptationType.Size, AdaptationType.Spikes },
            DeathCause.Pierced => new List<AdaptationType> { AdaptationType.Toughness, AdaptationType.Evasion },
            DeathCause.Poisoned => new List<AdaptationType> { AdaptationType.ToxinResistance, AdaptationType.Detoxification },
            DeathCause.PHShock => new List<AdaptationType> { AdaptationType.PHResistance, AdaptationType.BufferCapacity },
            DeathCause.Overheated => new List<AdaptationType> { AdaptationType.HeatResistance },
            DeathCause.Frozen => new List<AdaptationType> { AdaptationType.ColdResistance },
            DeathCause.Starved => new List<AdaptationType> { AdaptationType.MetabolicEfficiency, AdaptationType.StorageCapacity },
            _ => new List<AdaptationType>()
        };
    }
}

public enum DeathCause
{
    Unknown, Eaten, Pierced, Poisoned, Crushed,
    PHShock, Overheated, Frozen, Starved, Irradiated
}

[System.Serializable]
public class DeathReport
{
    public DeathCause primaryCause;
    public CellInfo killerInfo;
    public List<DamageFactor> contributingFactors;
    public List<AdaptationType> suggestedAdaptations;
    public float survivalTime;
    public int killsThisLife;
    public float distanceTraveled;
}
```

### Adaptation System

```csharp
public enum AdaptationType
{
    // Defensive
    Toughness, Evasion, Spikes, Size,
    
    // Resistances
    PHResistance, BufferCapacity,
    HeatResistance, ColdResistance,
    ToxinResistance, Detoxification,
    PressureResistance,
    
    // Survival
    MetabolicEfficiency, StorageCapacity, Regeneration,
    
    // Offense
    EngulfmentSpeed, DigestiveEfficiency, ToxinPotency, SpikeDamage,
    
    // Mobility
    Speed, Acceleration, TurnRate,
    
    // Special
    Photosynthesis, Chemosynthesis, Sensing
}

[System.Serializable]
public class Adaptation
{
    public AdaptationType type;
    public float currentValue = 0f;
    public float maxValue = 0.5f;
    public float diminishingFactor = 0.9f;
    public int xpInvested = 0;
    public int xpToNextLevel = 100;
    public float xpScaling = 1.5f;
}

public class AdaptationSystem : MonoBehaviour
{
    public Dictionary<AdaptationType, Adaptation> adaptations = new();
    
    public int baseXPPerDeath = 50;
    public float relevanceMultiplier = 2f;
    
    public UnityEvent<Adaptation> OnAdaptationLevelUp;
    
    void Start()
    {
        foreach (AdaptationType type in Enum.GetValues(typeof(AdaptationType)))
        {
            adaptations[type] = new Adaptation { type = type };
        }
    }
    
    public Dictionary<AdaptationType, int> ProcessDeath(DeathReport report)
    {
        var xpGains = new Dictionary<AdaptationType, int>();
        
        // Bonus XP for relevant adaptations
        foreach (var suggested in report.suggestedAdaptations)
        {
            int xp = Mathf.RoundToInt(baseXPPerDeath * relevanceMultiplier);
            AddAdaptationXP(suggested, xp);
            xpGains[suggested] = xp;
        }
        
        // Small XP to all adaptations
        int passiveXP = baseXPPerDeath / 10;
        foreach (var adaptation in adaptations.Values)
        {
            AddAdaptationXP(adaptation.type, passiveXP);
        }
        
        return xpGains;
    }
    
    public void AddAdaptationXP(AdaptationType type, int amount)
    {
        var adaptation = adaptations[type];
        adaptation.xpInvested += amount;
        
        while (adaptation.xpInvested >= adaptation.xpToNextLevel)
        {
            adaptation.xpInvested -= adaptation.xpToNextLevel;
            LevelUpAdaptation(adaptation);
        }
    }
    
    void LevelUpAdaptation(Adaptation adaptation)
    {
        float gain = 0.05f * Mathf.Pow(adaptation.diminishingFactor, adaptation.currentValue / 0.05f);
        adaptation.currentValue = Mathf.Min(adaptation.currentValue + gain, adaptation.maxValue);
        adaptation.xpToNextLevel = Mathf.RoundToInt(adaptation.xpToNextLevel * adaptation.xpScaling);
        
        OnAdaptationLevelUp?.Invoke(adaptation);
    }
    
    public void ApplyToCell(Cell cell)
    {
        cell.damageReduction += GetValue(AdaptationType.Toughness);
        cell.evasionChance += GetValue(AdaptationType.Evasion);
        cell.phResistance += GetValue(AdaptationType.PHResistance);
        cell.heatResistance += GetValue(AdaptationType.HeatResistance);
        cell.coldResistance += GetValue(AdaptationType.ColdResistance);
        cell.toxinResistance += GetValue(AdaptationType.ToxinResistance);
        cell.metabolicEfficiency *= (1 + GetValue(AdaptationType.MetabolicEfficiency));
        cell.regenRate *= (1 + GetValue(AdaptationType.Regeneration));
        cell.maxSpeed *= (1 + GetValue(AdaptationType.Speed));
        cell.acceleration *= (1 + GetValue(AdaptationType.Acceleration));
        cell.engulfSpeed *= (1 + GetValue(AdaptationType.EngulfmentSpeed));
    }
    
    float GetValue(AdaptationType type) => adaptations[type].currentValue;
}
```

### Death Screen UI

```csharp
public class DeathScreenUI : MonoBehaviour
{
    public GameObject deathScreenPanel;
    public TextMeshProUGUI causeOfDeathText;
    public TextMeshProUGUI survivalTimeText;
    public TextMeshProUGUI statsText;
    public Transform adaptationGainsParent;
    public GameObject adaptationGainPrefab;
    public Button respawnButton;
    
    public void Show(DeathReport report, Dictionary<AdaptationType, int> xpGains)
    {
        deathScreenPanel.SetActive(true);
        StartCoroutine(AnimateDeathScreen(report, xpGains));
    }
    
    IEnumerator AnimateDeathScreen(DeathReport report, Dictionary<AdaptationType, int> xpGains)
    {
        // Fade in
        CanvasGroup cg = deathScreenPanel.GetComponent<CanvasGroup>();
        cg.alpha = 0;
        while (cg.alpha < 1)
        {
            cg.alpha += Time.deltaTime * 2f;
            yield return null;
        }
        
        // Type out cause of death
        causeOfDeathText.text = GetDeathMessage(report.primaryCause, report.killerInfo);
        
        yield return new WaitForSeconds(0.5f);
        
        survivalTimeText.text = $"Survived: {FormatTime(report.survivalTime)}";
        statsText.text = $"Kills: {report.killsThisLife}  |  Distance: {report.distanceTraveled:F0}m";
        
        yield return new WaitForSeconds(0.3f);
        
        // Show adaptation gains
        foreach (var kvp in xpGains.Where(k => k.Value > 0).OrderByDescending(k => k.Value).Take(3))
        {
            var gainUI = Instantiate(adaptationGainPrefab, adaptationGainsParent);
            gainUI.GetComponent<AdaptationGainUI>().Show(kvp.Key, kvp.Value);
            yield return new WaitForSeconds(0.2f);
        }
        
        respawnButton.interactable = true;
    }
    
    string GetDeathMessage(DeathCause cause, CellInfo killer)
    {
        return cause switch
        {
            DeathCause.Eaten => $"You were consumed by {killer?.GetDescription() ?? "a larger cell"}.\n\n<i>Your descendants remember the terror.</i>",
            DeathCause.Pierced => "You were impaled by a spike.\n\n<i>Your descendants remember the pain.</i>",
            DeathCause.Poisoned => "You succumbed to toxins.\n\n<i>Your descendants remember the sickness.</i>",
            DeathCause.PHShock => "A sudden pH shift overwhelmed you.\n\n<i>Your descendants remember the burn.</i>",
            DeathCause.Overheated => "The heat was too intense.\n\n<i>Your descendants remember the flames.</i>",
            DeathCause.Frozen => "The cold claimed you.\n\n<i>Your descendants remember the chill.</i>",
            DeathCause.Starved => "You ran out of energy.\n\n<i>Your descendants remember the hunger.</i>",
            _ => "You died.\n\n<i>Your descendants will learn from this.</i>"
        };
    }
    
    string FormatTime(float seconds)
    {
        int mins = Mathf.FloorToInt(seconds / 60);
        int secs = Mathf.FloorToInt(seconds % 60);
        return $"{mins}:{secs:D2}";
    }
}
```

---

## Evolution Points Economy

### Point Sources

```csharp
public class EvolutionPointsManager : MonoBehaviour
{
    public int currentPoints { get; private set; }
    public int lifetimePoints { get; private set; }
    
    public UnityEvent<int, string> OnPointsGained;
    public UnityEvent<int, string> OnPointsSpent;
    
    public void AddFromAchievement(Achievement achievement, int amount)
    {
        Add(amount, $"Achievement: {achievement.name}");
    }
    
    public void AddFromKill(Cell victim)
    {
        int basePoints = 5;
        float sizeBonus = victim.GetSize() / playerCell.GetSize();
        int points = Mathf.RoundToInt(basePoints * Mathf.Max(1, sizeBonus));
        Add(points, "Kill");
    }
    
    public void AddFromSurvival(float duration)
    {
        int points = Mathf.FloorToInt(duration / 30f);
        if (points > 0) Add(points, "Survival");
    }
    
    public void AddFromExploration(SafeZone zone)
    {
        int points = zone.type switch
        {
            SafeZoneType.ClayBed => 10,
            SafeZoneType.HydrothermalVent => 25,
            SafeZoneType.BacterialMat => 15,
            _ => 10
        };
        Add(points, $"Discovered: {zone.zoneName}");
    }
    
    void Add(int amount, string source)
    {
        currentPoints += amount;
        lifetimePoints += amount;
        OnPointsGained?.Invoke(amount, source);
    }
    
    public bool CanAfford(int amount) => currentPoints >= amount;
    
    public bool TrySpend(int amount, string reason)
    {
        if (!CanAfford(amount)) return false;
        currentPoints -= amount;
        OnPointsSpent?.Invoke(amount, reason);
        return true;
    }
}
```

### Point Costs

```csharp
public static class EvolutionCosts
{
    // Gene Unlocks (one-time)
    public static Dictionary<string, int> GeneCosts = new()
    {
        // Tier 1 - Basic
        { "GENE_FLAGELLIN", 100 },
        { "GENE_ACTIN", 80 },
        { "GENE_MYOSIN", 80 },
        { "GENE_RECEPTOR", 100 },
        
        // Tier 2 - Advanced
        { "GENE_LECTIN", 120 },
        { "GENE_KERATIN", 150 },
        { "GENE_CAPSULE", 180 },
        { "GENE_HEAT_SHOCK", 150 },
        { "GENE_ANTIFREEZE", 150 },
        { "GENE_BUFFER", 120 },
        { "GENE_PROTEASE_ADVANCED", 150 },
        
        // Tier 3 - Specialized
        { "GENE_TOXIN", 200 },
        { "GENE_CHLOROPHYLL", 250 },
        { "GENE_CADHERIN", 200 },
        { "GENE_TUBULIN", 200 },
    };
    
    // Adaptation Boosts (repeatable, escalating)
    public static int GetAdaptationCost(Adaptation adaptation)
    {
        float level = adaptation.currentValue / 0.05f;
        return Mathf.RoundToInt(50 * Mathf.Pow(1.5f, level));
    }
    
    // Capacity Upgrades
    public static Dictionary<string, int> CapacityCosts = new()
    {
        { "PROTEIN_SLOT_4", 100 },
        { "PROTEIN_SLOT_5", 200 },
        { "PROTEIN_SLOT_6", 400 },
        { "GENE_CAPACITY_1", 250 },
        { "GENE_CAPACITY_2", 500 },
        { "MEMBRANE_COMPLEXITY_1", 200 },
        { "MEMBRANE_COMPLEXITY_2", 400 },
    };
}
```

---

## Lineage Persistence

### What Persists

```csharp
[System.Serializable]
public class LineageData
{
    // === PERSISTS FOREVER ===
    public int generation = 1;
    public int lifetimePoints = 0;
    public float totalPlayTime = 0f;
    public int totalKills = 0;
    public int totalDeaths = 0;
    
    public HashSet<string> unlockedGenes = new();
    public HashSet<string> unlockedCapacities = new();
    public Dictionary<AdaptationType, Adaptation> adaptations = new();
    
    public HashSet<string> completedAchievements = new();
    public HashSet<string> discoveredZones = new();
    public HashSet<string> discoveredBiomes = new();
    
    // Records
    public float longestLife = 0f;
    public int mostKillsInOneLife = 0;
    public Dictionary<DeathCause, int> deathsByType = new();
    
    // === PERSISTS UNTIL SPENT ===
    public int currentPoints = 0;
    
    // === RESETS ON DEATH (on Cell object) ===
    // - Current size, position, protein placements
    // - Membrane shape, resources, damage
}
```

### Lineage Manager

```csharp
public class LineageManager : MonoBehaviour
{
    public static LineageManager Instance;
    public LineageData data;
    
    void Awake()
    {
        Instance = this;
        LoadLineage();
    }
    
    public void OnPlayerDeath(Cell cell, DeathReport report)
    {
        data.generation++;
        data.totalDeaths++;
        data.totalPlayTime += cell.aliveTime;
        data.totalKills += cell.killCount;
        
        if (cell.aliveTime > data.longestLife)
            data.longestLife = cell.aliveTime;
        if (cell.killCount > data.mostKillsInOneLife)
            data.mostKillsInOneLife = cell.killCount;
        
        if (!data.deathsByType.ContainsKey(report.primaryCause))
            data.deathsByType[report.primaryCause] = 0;
        data.deathsByType[report.primaryCause]++;
        
        var xpGains = adaptationSystem.ProcessDeath(report);
        evolutionPoints.AddFromSurvival(cell.aliveTime);
        
        deathScreenUI.Show(report, xpGains);
        SaveLineage();
    }
    
    public void OnPlayerRespawn(Cell newCell)
    {
        // Apply genes
        foreach (string geneId in data.unlockedGenes)
        {
            newCell.genome.AddGene(GeneDatabase.Get(geneId));
        }
        
        // Apply adaptations
        adaptationSystem.ApplyToCell(newCell);
        
        // Apply capacities
        int extraSlots = data.unlockedCapacities.Count(c => c.StartsWith("PROTEIN_SLOT"));
        newCell.maxProteinTypes = 3 + extraSlots;
        
        int membraneLevels = data.unlockedCapacities.Count(c => c.StartsWith("MEMBRANE"));
        newCell.membrane.vertexCount = 32 + (membraneLevels * 16);
        
        // Restore map
        minimapSystem.RestoreDiscoveries(data.discoveredZones);
    }
    
    void SaveLineage()
    {
        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString("LineageData", json);
        PlayerPrefs.Save();
    }
    
    void LoadLineage()
    {
        if (PlayerPrefs.HasKey("LineageData"))
        {
            data = JsonUtility.FromJson<LineageData>(PlayerPrefs.GetString("LineageData"));
        }
        else
        {
            data = new LineageData();
            // Grant starter genes
            data.unlockedGenes.Add("GENE_ATPASE_BASIC");
            data.unlockedGenes.Add("GENE_SPECTRIN_BASIC");
            data.unlockedGenes.Add("GENE_PROTEASE_BASIC");
        }
    }
}
```

---

## Implementation Priority

| Phase | Weeks | Focus |
|-------|-------|-------|
| 1 | 1-2 | Safe zone detection, entry, basic editing UI |
| 2 | 3-4 | Achievement system, event tracking, notifications |
| 3 | 5-6 | Death analysis, death screen, respawn flow |
| 4 | 7-8 | Adaptation XP, stat effects, visual feedback |
| 5 | 9-10 | Evolution shop UI, purchasing |
| 6 | 11-12 | Lineage persistence, stats tracking |

---

## Balance Targets

| Metric | Target |
|--------|--------|
| Average life (new player) | 2-4 minutes |
| Average life (experienced) | 8-15 minutes |
| Points per life | 50-200 |
| Gene unlock rate | 1 every 3-5 lives |
| Adaptation improvement | Noticeable every 5-10 deaths |
| Time to colony stage | ~2 weeks |

---

## Testing Checklist

- [ ] Does death feel like progress, not punishment?
- [ ] Is "your descendants remember" compelling?
- [ ] Are safe zones worth seeking out?
- [ ] Do achievements feel achievable but not trivial?
- [ ] Is there meaningful choice in spending points?
- [ ] Do adaptations make noticeable differences?
- [ ] Does respawn feel satisfying?
- [ ] Can players recover from bad purchases?
- [ ] Does long-term progression feel rewarding?
