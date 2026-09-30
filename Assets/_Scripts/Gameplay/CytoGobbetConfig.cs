using UnityEngine;

/// <summary>
/// Tuning for the edible cytoplasm gobbets a cell leaves when it POPS. Placeholder water-condition
/// sliders drive how fast gobbets dissolve (to be wired to real per-region chemistry later). Put one on
/// a scene manager (e.g. GameplaySystems); if none exists, a default is created automatically.
/// </summary>
public class CytoGobbetConfig : MonoBehaviour
{
    public static CytoGobbetConfig Instance;
    static CytoGobbetConfig _fallback;
    public static CytoGobbetConfig Active =>
        Instance != null ? Instance : (_fallback != null ? _fallback : (_fallback = MakeFallback()));

    [Header("Edible cytoplasm")]
    [Tooltip("Fraction of a popped cell's mass that becomes edible cytoplasm (before the organelle cut).")]
    [Range(0f, 1f)] public float baseCytoFraction = 0.7f;
    [Tooltip("Each organelle the cell had reduces the edible cytoplasm fraction by this much " +
             "(organelles lock mass away that the cytoplasm doesn't).")]
    [Range(0f, 0.2f)] public float organelleMassCut = 0.08f;
    [Tooltip("Most fragments a single pop can produce.")]
    public int maxGobbets = 20;
    [Tooltip("Matter gained per unit of gobbet mass eaten (biomass is 1:1 with the gobbet's mass).")]
    public float matterPerMass = 0.4f;

    [Header("Dissolve time (seconds)")]
    [Tooltip("Average seconds a gobbet lingers before dissolving, in neutral water.")]
    public float baseDissolveTime = 6f;
    [Tooltip("Per-gobbet random spread on the dissolve time (±fraction).")]
    [Range(0f, 0.8f)] public float dissolveRandomness = 0.3f;

    [Header("Water conditions — PLACEHOLDER (0..1, tune/replace later)")]
    [Tooltip("Warmer water dissolves gobbets faster.")]
    [Range(0f, 1f)] public float temperature = 0.5f;
    [Tooltip("Saltier water dissolves gobbets faster.")]
    [Range(0f, 1f)] public float salinity = 0.5f;
    [Tooltip("More acidic water dissolves gobbets faster.")]
    [Range(0f, 1f)] public float acidity = 0.5f;

    [Header("...how strongly each condition matters (placeholder)")]
    public float temperatureInfluence = 0.6f;
    public float salinityInfluence = 0.25f;
    public float acidityInfluence = 0.8f;

    void Awake() { if (Instance == null) Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Edible cytoplasm fraction for a cell with the given organelle count.</summary>
    public float CytoFraction(int organelles) =>
        Mathf.Clamp01(baseCytoFraction - Mathf.Max(0, organelles) * organelleMassCut);

    /// <summary>Seconds this gobbet should linger before dissolving, given current water conditions.</summary>
    public float DissolveTime()
    {
        float harsh = temperature * temperatureInfluence
                    + salinity    * salinityInfluence
                    + acidity     * acidityInfluence;
        float t = baseDissolveTime / (1f + Mathf.Max(0f, harsh));   // harsher water → shorter life
        return Mathf.Max(0.5f, t * Random.Range(1f - dissolveRandomness, 1f + dissolveRandomness));
    }

    static CytoGobbetConfig MakeFallback()
    {
        var go = new GameObject("CytoGobbetConfig (auto)");
        Object.DontDestroyOnLoad(go);
        return go.AddComponent<CytoGobbetConfig>();
    }
}
