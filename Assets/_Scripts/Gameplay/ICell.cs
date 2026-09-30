using UnityEngine;

/// <summary>
/// Common surface for anything that behaves like a cell (the soft-body player and the
/// lightweight AI cells), so the engulfment rules work on both.
/// </summary>
public interface ICell
{
    Transform Transform { get; }
    float Mass { get; }       // biomass = size = score
    float Radius { get; }     // world-space membrane radius
    float Rigidity { get; }   // 0..1 membrane rigidity
    float EngulfPower { get; }// 1 = full; lower as the membrane stiffens
    bool  IsAlive { get; }

    void GainBiomass(float amount);
    void Consumed(ICell by);  // got engulfed by `by`
}

/// <summary>The physics of who-can-eat-whom. Shared by all cells.</summary>
public static class EngulfMath
{
    /// <summary>Attacker needs this much size advantage (×) over a soft target to engulf it.</summary>
    public const float Ratio = 1.25f;

    /// <summary>
    /// Can `a` engulf `t`? Attacker's reach = mass × engulfPower (rigid membranes wrap worse).
    /// Target's resistance = mass × (rigidity makes it harder to swallow).
    /// </summary>
    public static bool CanEngulf(ICell a, ICell t)
    {
        if (a == null || t == null || !a.IsAlive || !t.IsAlive || a == t) return false;
        // Hard gate: you can only ever engulf something SMALLER than you. Size dominates — a soft
        // membrane makes a target easier to swallow, but never lets you eat a bigger cell.
        if (a.Mass <= t.Mass) return false;
        float reach  = a.Mass * Mathf.Max(a.EngulfPower, 0.05f);
        float resist = t.Mass * (0.9f + 0.7f * Mathf.Clamp01(t.Rigidity)); // floor ≥ 0.9 so size wins
        return reach >= resist * Ratio;
    }
}
