using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// S4 core economy: biomass = size = score. Eating raises biomass; biomass makes the
/// cell bigger but slower (speed ∝ mass^-massSpeedExponent — the agar.io tradeoff).
///
/// Growth physically inflates the soft body by scaling the membrane springs, the
/// VolumePreservation target area, and the collider — so getting bigger is visible and felt.
///
/// No eating yet (Phase 1, step 1): use the debug keys to add/remove biomass and feel the curve.
/// Attach to the same GameObject as JellyBodyBuilder / VolumePreservation (the cell center).
/// See Assets/Design/CellStage_S4_Eukaryote.md §2.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CellBiomass : MonoBehaviour, ICell
{
    [Header("Biomass")]
    [Tooltip("Current biomass. 1 = starting size. Area/size scale with mass; speed with mass^-exp.")]
    [Min(0.05f)] public float mass = 1f;

    [Header("Speed curve")]
    [Tooltip("speedMultiplier = mass^(-exponent). 0.5 = 1/sqrt(mass) (classic agar.io feel).")]
    [Range(0.1f, 1f)] public float massSpeedExponent = 0.5f;
    [Tooltip("Floor so the biggest cells are slow but not frozen.")]
    [Range(0.05f, 1f)] public float minSpeedMultiplier = 0.35f;

    [Header("Membrane rigidity & max size")]
    [Tooltip("Evolvable trait. Rigid membrane = bigger maxMass (upside) but weaker engulf (downside).")]
    [Range(0f, 1f)] public float membraneRigidity = 0.3f;
    [Tooltip("Max biomass at rigidity 0 — grow past maxMass and the cell POPS.")]
    public float baseMaxMass = 4f;
    [Tooltip("Extra max biomass granted at rigidity 1.")]
    public float rigidityMassBonus = 16f;
    [Tooltip("Engulf power at full rigidity (1 = no penalty, lower = stiffer membrane engulfs worse).")]
    [Range(0.1f, 1f)] public float minEngulfAtMaxRigidity = 0.4f;
    [Tooltip("Fires when the cell exceeds maxMass and bursts. Wire death/respawn/chunk-drop here later.")]
    public UnityEngine.Events.UnityEvent onPop;

    /// <summary>Biomass ceiling for the current membrane rigidity. Exceed it → pop.</summary>
    public float MaxMass => baseMaxMass + membraneRigidity * rigidityMassBonus;

    /// <summary>How well this cell can engulf prey (1 = full; drops as the membrane stiffens).</summary>
    public float EngulfPower => Mathf.Lerp(1f, minEngulfAtMaxRigidity, membraneRigidity);

    [Header("Debug rigidity controls (temporary)")]
    public KeyCode rigidityUpKey   = KeyCode.RightBracket; // ']'
    public KeyCode rigidityDownKey = KeyCode.LeftBracket;  // '['

    [Header("Debug grow controls (temporary — until eating exists)")]
    public bool debugGrowKeys = true;
    [Tooltip("Biomass added/removed per second while holding the keys")]
    public float debugGrowRate = 1.5f;
    public KeyCode growKey = KeyCode.Equals;   // '=' / '+'
    public KeyCode shrinkKey = KeyCode.Minus;  // '-'

    /// <summary>Movement speed scalar for the current mass (1 at mass 1, lower when bigger).</summary>
    public float SpeedMultiplier =>
        Mathf.Clamp(Mathf.Pow(Mathf.Max(mass, 0.05f), -massSpeedExponent), minSpeedMultiplier, 1f);

    /// <summary>Approximate membrane radius for the current mass (∝ sqrt(mass)).</summary>
    public float Radius => baseRadius * Mathf.Sqrt(mass);

    [Header("On consumed")]
    [Tooltip("AI cells set this true → destroyed when engulfed. Player leaves false → respawns small.")]
    public bool destroyOnConsumed = false;
    public System.Action<CellBiomass> onConsumed;

    bool consumedFlag;

    // ── ICell ──────────────────────────────────────────────────────────────
    public Transform Transform => transform;
    public float Mass     => mass;
    public float Rigidity => membraneRigidity;
    public bool  IsAlive  => isActiveAndEnabled && !consumedFlag;
    public void GainBiomass(float amount) => AddBiomass(amount);
    public void Consumed(ICell by)
    {
        if (!IsAlive) return;
        // Getting EATEN is an absorption, not a rupture: the cell is drawn into the predator and the
        // predator "gulps". Visually the opposite of a pop (inward + directional, vs outward + explosive).
        StartCoroutine(EatenDeathRoutine(by != null ? by.Transform : null));
    }

    /// <summary>Floor on biomass. Below this the volume solver's emergency expansion goes unstable
    /// (near-zero target area → huge corrective forces → the cell flails). Keep cells above it.</summary>
    public const float MinMass = 0.3f;

    public void AddBiomass(float amount) => SetMass(mass + amount);

    public void SetMass(float m)
    {
        mass = Mathf.Max(MinMass, m);
        ApplyGrowth();
        CheckPop();
    }

    public void SetRigidity(float r)
    {
        membraneRigidity = Mathf.Clamp01(r);
        ApplyRigidity();
        CheckPop(); // lowering rigidity can drop maxMass below current mass
    }

    /// <summary>Burst if biomass exceeds the rigidity-defined ceiling.</summary>
    void CheckPop()
    {
        if (cached && mass > MaxMass) Pop();
    }

    [Header("Death (burst)")]
    [Tooltip("Seconds the cytoplasm/membrane burst flies before cleanup (player is hidden this long).")]
    public float deathTime = 0.6f;

    bool rupturing;

    void Pop()
    {
        if (rupturing) return;
        Debug.Log($"[CellBiomass] POP! mass {mass:F1} exceeded maxMass {MaxMass:F1} " +
                  $"(rigidity {membraneRigidity:F2}).");
        onPop?.Invoke();
        StartCoroutine(PopDeathRoutine());
    }

    /// <summary>
    /// A PHYSICAL death: the membrane springs are severed and every node is kicked outward with a real
    /// impulse, so the cell physically bursts apart (the JellyMesh/MembraneRender draw the shredding
    /// membrane). Cytoplasm sprays as physics particles + leaves a lingering remnant. High node drag
    /// and a frozen centre keep the physics from launching anything to infinity. AI cells are destroyed;
    /// the player keeps its joints (only shoved) so it can spring back and respawn.
    /// </summary>
    /// <summary>POP — an internal-pressure failure: the cell strains (swell + jitter wind-up), then
    /// ruptures, flinging cytoplasm and torn membrane OUTWARD with a shockwave + pressure shove. Loud,
    /// symmetric, leaves a mess.</summary>
    IEnumerator PopDeathRoutine()
    {
        if (rupturing) yield break;
        rupturing = true;
        bool prevGrowKeys = debugGrowKeys;
        consumedFlag = true;
        debugGrowKeys = false;
        onConsumed?.Invoke(this);

        // ── WIND-UP TELL: the over-full cell strains — it swells and vibrates for a beat before it goes.
        // Driven through the soft body (spring rest-lengths + volume target) so it physically bulges.
        float windup = 0.22f, tW = 0f;
        while (tW < windup)
        {
            tW += Time.deltaTime;
            float k = tW / windup;
            float swell = 1f + 0.22f * k + Mathf.Sin(tW * 90f) * 0.05f * k;   // grow + fast jitter
            InflateSoftBody(swell);
            yield return null;
        }

        // ── RUPTURE INSTANT: the membrane fails suddenly.
        Color memColor = membraneLine != null ? membraneLine.startColor : FxColor();
        Vector3 popPos = transform.position;
        if (volume != null) volume.enabled = false;

        CellFx.Hitstop(0.12f, 0.06f);
        CellFx.Shockwave(popPos, Radius, memColor);
        CellFx.PressureWave(popPos, Radius, Radius * 6f, transform);
        // The membrane tears into arcs that snap outward and curl away (balloon rubber recoiling) —
        // captured BEFORE we hide the outline so it uses the intact ring.
        CellFx.RuptureMembrane(membraneLine, popPos, Radius, memColor);
        CellFx.CytoplasmCloud(popPos, Radius, FxColor());   // diffuse haze left behind

        if (meshRenderer != null) meshRenderer.enabled = false;
        if (membraneLine != null) membraneLine.enabled = false;

        // The cytoplasm blows open as an EXPANDING METABALL SHELL (one fused surface, visible only where
        // the mass-points cluster into arcs) that breaks apart as it spreads. Those same points carry the
        // edible mass — cells nibble the shell away; the rest dissolves after a water-condition lifetime.
        int organelles = 0;
        var evo = GetComponent<EvolutionNodes>();
        if (evo != null) organelles = evo.OrganelleCount;
        PopShell.Burst(popPos, Radius, mass, organelles, FxColor());

        yield return new WaitForSeconds(Mathf.Max(deathTime, 1.0f));
        FinishDeath(prevGrowKeys);
    }

    /// <summary>EATEN — an absorption: the cell is drawn INTO the predator (streaming/shrinking toward
    /// the mouth), its membrane peels inward, a cytoplasm strand siphons across, and the predator gulps
    /// (bulges + flashes the prey's colour). Quiet, directional, no outward spray.</summary>
    IEnumerator EatenDeathRoutine(Transform predator)
    {
        if (rupturing) yield break;
        rupturing = true;
        bool prevGrowKeys = debugGrowKeys;
        consumedFlag = true;
        debugGrowKeys = false;
        onConsumed?.Invoke(this);

        if (volume != null) volume.enabled = false;
        Color col = FxColor();
        Color memColor = membraneLine != null ? membraneLine.startColor : col;

        // Hide the real body; organic metaball goo gets sucked into the predator, drawn UNDER it.
        if (meshRenderer != null) meshRenderer.enabled = false;
        if (membraneLine != null) membraneLine.enabled = false;

        // Render the absorbed goo just beneath the predator's body so it reads as going inside/under it.
        int siphonOrder = -5;
        if (predator != null)
        {
            var pmr = predator.GetComponentInChildren<MeshRenderer>();
            if (pmr != null) siphonOrder = pmr.sortingOrder - 1;
            var pred = predator.GetComponentInParent<CellBiomass>();
            if (pred != null) pred.PlayEatFeedback(col);                     // the predator "gulps"
        }

        CellFx.MetaballSiphon(transform.position, Radius, col, predator, siphonOrder); // shards streaming in, unravelling
        CellFx.CytoplasmSiphon(transform.position, predator, col);                     // extra droplets drawn across

        yield return new WaitForSeconds(0.5f);   // let the goo finish streaming in (still quicker than a pop)
        FinishDeath(prevGrowKeys);
    }

    /// <summary>Shared tail: destroy an AI cell, or un-hide + reform the player as a small cell.</summary>
    void FinishDeath(bool prevGrowKeys)
    {
        if (destroyOnConsumed) { Destroy(gameObject); return; }
        if (meshRenderer != null) meshRenderer.enabled = true;
        if (membraneLine != null) membraneLine.enabled = true;
        if (volume != null) volume.enabled = true;
        consumedFlag = false;
        rupturing = false;
        debugGrowKeys = prevGrowKeys;
        SetMass(1f);           // ApplyGrowth resets spring rest-lengths (undoes any wind-up inflation)
        ApplyRigidity();
    }

    /// <summary>Temporarily inflate the soft body to <paramref name="swell"/>× its current size (spring
    /// rest-lengths + volume target) — used for the pop wind-up. SetMass()/ApplyGrowth() restores it.</summary>
    void InflateSoftBody(float swell)
    {
        if (!cached) return;
        float sc = Mathf.Sqrt(mass) * swell;
        if (spokes != null) for (int i = 0; i < spokes.Length; i++) if (spokes[i] != null) spokes[i].distance = spokeBase[i] * sc;
        if (rims != null)   for (int i = 0; i < rims.Length; i++)   if (rims[i] != null)   rims[i].distance   = rimBase[i]   * sc;
        if (volume != null) volume.SetTargetArea(baseTargetArea * mass * swell * swell);
    }

    /// <summary>Predator feedback when it swallows a cell: a quick "gulp" — the body bulges and settles,
    /// with a brief flash of the prey's colour. Call on the eater.</summary>
    public void PlayEatFeedback(Color preyColor)
    {
        if (isActiveAndEnabled) StartCoroutine(EatFeedbackRoutine(preyColor));
    }

    IEnumerator EatFeedbackRoutine(Color preyColor)
    {
        // Colour flash on the body material (if it exposes _Tint), plus a soft absorb-pulse ring.
        Material bodyMat = meshRenderer != null ? meshRenderer.material : null;
        bool hasTint = bodyMat != null && bodyMat.HasProperty("_Tint");
        Color tint0 = hasTint ? bodyMat.GetColor("_Tint") : Color.white;
        CellFx.AbsorbPulse(transform.position, Radius, preyColor);

        float dur = 0.28f, t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            // Bulge out then settle (gulp), strongest early.
            float pulse = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);   // 0 → 1 → 0
            InflateSoftBody(1f + 0.12f * pulse);
            if (hasTint) bodyMat.SetColor("_Tint", Color.Lerp(tint0, preyColor, 0.5f * pulse));
            yield return null;
        }
        if (hasTint) bodyMat.SetColor("_Tint", tint0);
        ApplyGrowth();   // restore exact rest-lengths
    }

    /// <summary>
    /// Tint for the death/pop/engulf FX. The cytoplasm colour is baked into the BioSlime shader graph
    /// (no exposed material property to read at runtime), so this is an editable approximation —
    /// defaults to the slime green. Tweak per-cell in the Inspector if you recolour the membrane.
    /// </summary>
    [Header("Death FX")]
    [Tooltip("Colour of the spill/suck-in blobs when this cell is eaten or pops.")]
    public Color fxColor = new Color(0.24f, 1f, 0.52f);

    Color FxColor() => fxColor;

    // ── cached soft-body refs ──────────────────────────────────────────────
    Rigidbody2D       centerRB;
    JellyBodyBuilder  builder;
    VolumePreservation volume;
    CircleCollider2D  centerCollider;
    MeshRenderer      meshRenderer; // the JellyMesh cytoplasm renderer (for opacity fade on death)
    LineRenderer      membraneLine; // the outline (faded on death)
    SpringJoint2D[]   spokes;   // node → center
    float[]           spokeBase;
    SpringJoint2D[]   rims;     // node → neighbour
    float[]           rimBase;
    float baseRadius        = 2f;
    float baseTargetArea    = 0f;
    float baseColliderRadius = 0f;
    float baseStiffness     = 5f;   // membrane spring frequency at neutral rigidity
    bool  cached;

    void Update()
    {
        if (!cached) Cache();

        if (debugGrowKeys)
        {
            if (Input.GetKey(growKey))   AddBiomass( debugGrowRate * Time.deltaTime);
            if (Input.GetKey(shrinkKey)) AddBiomass(-debugGrowRate * Time.deltaTime);
            if (Input.GetKey(rigidityUpKey))   SetRigidity(membraneRigidity + 0.5f * Time.deltaTime);
            if (Input.GetKey(rigidityDownKey)) SetRigidity(membraneRigidity - 0.5f * Time.deltaTime);
        }
    }

    /// <summary>
    /// Cache the soft-body pieces. Runs on the first Update — guaranteed after
    /// JellyBodyBuilder.Start() has created the nodes, joints, and target area.
    /// </summary>
    void Cache()
    {
        centerRB       = GetComponent<Rigidbody2D>();
        builder        = GetComponent<JellyBodyBuilder>();
        volume         = GetComponent<VolumePreservation>();
        centerCollider = GetComponent<CircleCollider2D>();
        var jelly      = GetComponentInChildren<JellyMesh>();
        if (jelly != null) meshRenderer = jelly.GetComponent<MeshRenderer>();
        membraneLine   = GetComponentInChildren<LineRenderer>();

        if (builder != null) { baseRadius = builder.radius; baseStiffness = builder.stiffness; }
        if (volume  != null) baseTargetArea = volume.TargetArea;
        if (centerCollider != null) baseColliderRadius = centerCollider.radius;

        // Cells must be able to OVERLAP for engulfing to work (otherwise solid colliders
        // bounce them apart and nobody can ever get close enough to eat). Make all of this
        // cell's colliders triggers — agar.io-style pass-through.
        foreach (var col in GetComponentsInChildren<Collider2D>())
            if (col != null) col.isTrigger = true;

        // Partition springs into spokes (to centre) and rims (to neighbours)
        var allJoints = GetComponentsInChildren<SpringJoint2D>();
        var spokeList = new System.Collections.Generic.List<SpringJoint2D>();
        var rimList   = new System.Collections.Generic.List<SpringJoint2D>();
        foreach (var j in allJoints)
        {
            if (j == null) continue;
            if (j.connectedBody == centerRB) spokeList.Add(j);
            else                              rimList.Add(j);
        }
        spokes = spokeList.ToArray();
        rims   = rimList.ToArray();

        spokeBase = new float[spokes.Length];
        for (int i = 0; i < spokes.Length; i++)
            spokeBase[i] = spokes[i].distance;   // builder set this to `radius`

        rimBase = new float[rims.Length];
        for (int i = 0; i < rims.Length; i++)
        {
            rims[i].autoConfigureDistance = false; // lock so we can scale it
            rimBase[i] = rims[i].distance;
        }

        cached = true;
        ApplyGrowth();   // sync to current mass
        ApplyRigidity(); // sync membrane firmness
        CheckPop();
    }

    /// <summary>Stiffer membrane = firmer (higher spring frequency) = less wobble. Readable feedback.</summary>
    void ApplyRigidity()
    {
        if (!cached) return;
        float freq = baseStiffness * Mathf.Lerp(0.7f, 1.6f, membraneRigidity);
        if (spokes != null) foreach (var j in spokes) if (j != null) j.frequency = freq;
        if (rims   != null) foreach (var j in rims)   if (j != null) j.frequency = freq;
    }

    /// <summary>Push the current mass into the soft-body size targets.</summary>
    void ApplyGrowth()
    {
        if (!cached) return;
        float scale = Mathf.Sqrt(mass); // radius ∝ sqrt(area) ∝ sqrt(mass)

        if (spokes != null)
            for (int i = 0; i < spokes.Length; i++)
                if (spokes[i] != null) spokes[i].distance = spokeBase[i] * scale;

        if (rims != null)
            for (int i = 0; i < rims.Length; i++)
                if (rims[i] != null) rims[i].distance = rimBase[i] * scale;

        if (volume != null)         volume.SetTargetArea(baseTargetArea * mass); // area ∝ mass
        if (centerCollider != null) centerCollider.radius = baseColliderRadius * scale;
    }

    void OnValidate()
    {
        // Live-tune from the Inspector during play.
        if (Application.isPlaying && cached) { ApplyGrowth(); ApplyRigidity(); CheckPop(); }
    }
}
