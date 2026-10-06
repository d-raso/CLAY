using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Stage0;

namespace CLAY.CellStage.Stage2
{
    /// <summary>
    /// An emergent biofilm — a cluster of prokaryotes embedded in a visible polymer matrix.
    ///
    /// Design rules (CellStageDecisions §12):
    ///   - No join button. Proximity + time → automatic formation.
    ///   - Benefits: UV/toxin resistance, faster HGT, shared resource pool.
    ///   - Cost: reduced individual mobility (movement multiplied by MobilityPenalty).
    ///   - Mild specialisation (metabolic roles) emerges automatically; not player-chosen.
    ///   - Cells leaving the film incur a brief de-adaptation lag.
    ///
    /// Multiple BiofilmPatches can coexist in the pool.
    /// Stage2Mode.Tick() calls BiofilmPatch.Tick() on all patches, which handles
    ///   membership, resource sharing, and the polymer-matrix visual signal.
    /// </summary>
    public sealed class BiofilmPatch
    {
        // ── Tuning ───────────────────────────────────────────────────────────
        /// Cells within this radius of the film centre get a membership offer.
        public const float JoinRadius = 2.6f;

        /// Cells must stay within this radius continuously for JoinDuration to join.
        public const float JoinDuration = 5f;

        /// A cell that drifts beyond this radius is ejected.
        public const float EjectRadius = 3.6f;

        /// Movement speed multiplier applied to members.
        public const float MobilityPenalty = 0.45f;

        /// Fraction of surplus energy pooled and redistributed per second.
        public const float ResourceShareFraction = 0.25f;

        /// HGT timer reduction fraction for members (faster HGT inside film).
        public const float HGTBoost = HGTContact.BiofilmHGTMultiplier;

        /// O₂ / toxin damage reduction for members.
        public const float HazardResistance = 0.40f;

        // ── State ────────────────────────────────────────────────────────────
        public readonly List<Prokaryote> members = new();

        /// Approximate geometric centre of the film (updated each Tick).
        public Vector2 centre;

        /// How opaque / thick the polymer matrix looks (0..1). Drives the renderer.
        public float matrixDensity;

        /// Seconds each candidate has been near the film (indexed by Prokaryote reference).
        readonly Dictionary<Prokaryote, float> candidateTimers = new();

        // ── Formation ────────────────────────────────────────────────────────

        /// <summary>
        /// Minimum members to be considered an established film.
        /// </summary>
        public bool Established => members.Count >= 3;

        /// <summary>
        /// Call from Stage2Mode.Tick() with all living cells.
        /// Updates membership, resource pooling, and matrix density.
        /// </summary>
        public void Tick(IReadOnlyList<Prokaryote> allCells, float dt)
        {
            if (members.Count == 0) return;

            UpdateCentre();
            UpdateMembership(allCells, dt);
            ShareResources(dt);
            UpdateMatrix(dt);
        }

        void UpdateCentre()
        {
            if (members.Count == 0) return;
            Vector2 sum = Vector2.zero;
            foreach (var m in members) sum += m.pos;
            centre = sum / members.Count;
        }

        void UpdateMembership(IReadOnlyList<Prokaryote> allCells, float dt)
        {
            // Eject members that wandered too far or died
            for (int i = members.Count - 1; i >= 0; i--)
            {
                var m = members[i];
                if (m.dead || Vector2.Distance(m.pos, centre) > EjectRadius)
                    Remove(m);
            }

            // Check candidates for joining
            foreach (var c in allCells)
            {
                if (c.dead || c.biofilm == this) continue;
                float dist = Vector2.Distance(c.pos, centre);
                if (dist <= JoinRadius)
                {
                    candidateTimers.TryGetValue(c, out float t);
                    t += dt;
                    candidateTimers[c] = t;
                    if (t >= JoinDuration) Add(c);
                }
                else
                {
                    candidateTimers.Remove(c);
                }
            }
        }

        void ShareResources(float dt)
        {
            // Pool surplus from producers; distribute to deficit cells
            float pool = 0f;
            foreach (var m in members)
            {
                float share = m.energy.surplus * ResourceShareFraction;      // surplus is already this tick's amount
                pool += share;
                m.energy.surplus = Mathf.Max(0f, m.energy.surplus - share);
            }

            if (pool <= 0f || members.Count == 0) return;
            float perCell = pool / members.Count;
            foreach (var m in members)
                m.energy.deficit = Mathf.Max(0f, m.energy.deficit - perCell);   // the film feeds its starving members
        }

        void UpdateMatrix(float dt)
        {
            float target = Mathf.Clamp01(members.Count / 12f);
            matrixDensity = Mathf.MoveTowards(matrixDensity, target, dt * 0.1f);
        }

        // ── Hazard resistance ────────────────────────────────────────────────

        /// <summary>
        /// Reduces incoming O₂ stress for a member.
        /// Call inside Prokaryote.TickOxygenStress() when biofilm != null.
        /// </summary>
        public float ReducedOxygenStress(float rawStress) =>
            rawStress * (1f - HazardResistance);

        // ── Resource upkeep contribution ─────────────────────────────────────

        public void ContributeUpkeep(Prokaryote cell, float dt)
        {
            // The biofilm's shared pool pays part of each member's upkeep.
            // Actual pool-transfer happens in ShareResources(); this is a marker
            // for the movement penalty so Prokaryote.Tick can read it.
            // Movement penalty applied by Stage2Mode.Steer().
        }

        // ── Membership helpers ───────────────────────────────────────────────

        public void Add(Prokaryote cell)
        {
            if (cell.biofilm != null) cell.biofilm.Remove(cell);
            members.Add(cell);
            cell.biofilm = this;
            candidateTimers.Remove(cell);
        }

        public void Remove(Prokaryote cell)
        {
            members.Remove(cell);
            if (cell.biofilm == this) cell.biofilm = null;
        }

        // ── Render hint ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the bounding radius of the film for renderer use.
        /// </summary>
        public float BoundingRadius()
        {
            float maxDist = 0f;
            foreach (var m in members)
                maxDist = Mathf.Max(maxDist, Vector2.Distance(m.pos, centre));
            return maxDist + 0.3f;
        }

        // ── Static factory ───────────────────────────────────────────────────

        /// <summary>
        /// Seed a new biofilm from a cluster of very-close cells.
        /// Stage2Mode calls this when it detects ≥ MinSeedCount cells within SeedRadius.
        /// </summary>
        public const int   MinSeedCount = 3;
        public const float SeedRadius   = 1.6f;

        public static BiofilmPatch TryForm(IReadOnlyList<Prokaryote> cells, Vector2 anchor)
        {
            var patch = new BiofilmPatch { centre = anchor };
            foreach (var c in cells)
            {
                if (c.dead || c.biofilm != null) continue;
                if (Vector2.Distance(c.pos, anchor) <= SeedRadius)
                    patch.Add(c);
            }
            return patch.members.Count >= MinSeedCount ? patch : null;
        }
    }
}
