using UnityEngine;

/// <summary>
/// Raw building blocks the cell has gathered. For this prototype slice we track a single
/// generic "matter" pool (later splits into proteins/lipids/nucleotides/minerals — see
/// CellStage_Evolution.md §2①). Matter is spent to grow evolution nodes.
/// </summary>
public class CellResources : MonoBehaviour
{
    [SerializeField] float matter = 0f;
    public float Matter => matter;

    public void AddMatter(float amount) => matter = Mathf.Max(0f, matter + amount);

    public bool TrySpend(float amount)
    {
        if (matter < amount) return false;
        matter -= amount;
        return true;
    }
}
