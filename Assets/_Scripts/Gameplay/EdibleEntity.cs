using UnityEngine;

/// <summary>
/// A free-floating morsel of food. Engulfed by any cell that reaches it; yields matter
/// (building blocks) and a little biomass. Spawned/recycled by FoodSpawner.
/// </summary>
public class EdibleEntity : MonoBehaviour
{
    public float matterValue  = 1.0f;
    public float biomassValue = 0.15f;
    [HideInInspector] public FoodSpawner pool;

    bool eaten;

    /// <summary>Called by an Engulfment when this morsel is consumed.</summary>
    public void Consume(CellResources eaterResources, ICell eaterCell)
    {
        if (eaten) return;
        eaten = true;
        if (eaterResources != null) eaterResources.AddMatter(matterValue);
        if (eaterCell != null)      eaterCell.GainBiomass(biomassValue);
        if (pool != null) pool.Recycle(this);
        else Destroy(gameObject);
    }

    public bool Eaten => eaten;
    public void ResetEaten() => eaten = false;
}
