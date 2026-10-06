using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Stage0;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage.Stage2
{
    /// <summary>
    /// Horizontal Gene Transfer (HGT) — the core S2 social mechanic.
    ///
    /// Science: prokaryotes exchange genes laterally by sustained physical contact
    /// (conjugation), phage-mediated transfer (transduction), or environmental DNA
    /// uptake (transformation). In S2 we simplify to contact-based exchange because:
    ///   - It maps naturally to the cells bumping mechanic.
    ///   - It makes the player's spatial choices meaningful (who to bump).
    ///   - Viroids provide the transduction flavour without a separate system.
    ///
    /// Design rules (CellStageDecisions §12):
    ///   - No dialog, no confirmation, no UI — purely silent contact.
    ///   - The player biases which traits drift in by where they spend time.
    ///   - HGT can shift Genome.metabolism, .domain maturity, .oxygenTolerance.
    ///   - Rate scales with biofilm membership (faster in-film HGT).
    /// </summary>
    public static class HGTContact
    {
        // ── Tuning ───────────────────────────────────────────────────────────
        /// Cells within this world-unit distance trigger a contact check.
        public const float ContactRadius = 0.25f;

        /// Seconds of sustained overlap required before HGT fires.
        public const float SustainedContactThreshold = 2.5f;

        /// Base probability of a gene transfer event per threshold crossing.
        public const float BaseTransferChance = 0.35f;

        /// Biofilm membership multiplier on transfer chance.
        public const float BiofilmHGTMultiplier = 2.2f;

        /// Maximum metabolic "steps" the recipient's metabolism can shift per HGT event.
        /// Prevents instant leaps across the whole Metabolism enum.
        const int MaxMetabolicShift = 1;

        // ── Per-frame contact accumulator ────────────────────────────────────

        /// <summary>
        /// Accumulate contact time between two overlapping cells.
        /// Call every physics frame for every close pair.
        /// Returns the contact duration so callers can cache it on the Prokaryote.
        /// </summary>
        public static float AccumulateContact(Prokaryote a, Prokaryote b, float dt)
        {
            float dist = Vector2.Distance(a.pos, b.pos);
            float combinedRadius = Mathf.Sqrt(a.area / Mathf.PI) + Mathf.Sqrt(b.area / Mathf.PI);
            if (dist < combinedRadius + ContactRadius)
                return dt;
            return 0f;
        }

        // ── Transfer event ───────────────────────────────────────────────────

        /// <summary>
        /// Try to fire a gene transfer from <paramref name="donor"/> to <paramref name="recipient"/>.
        /// Caller should invoke this once the accumulated contact time crosses
        /// <see cref="SustainedContactThreshold"/> and then reset the timer.
        /// </summary>
        public static bool TryTransfer(
            Prokaryote donor,
            Prokaryote recipient,
            ref DetRng r,
            CellStageProgress prog)
        {
            float chance = BaseTransferChance;
            if (recipient.biofilm != null) chance *= BiofilmHGTMultiplier;

            // Player proximity bias: player actively seeks certain cells → skewed chance
            if (recipient.player)
                chance = Mathf.Lerp(chance, chance * 1.8f, recipient.hgtProximityBias);

            if (r.NextFloat() > chance) return false;

            // Pick what to transfer
            int roll = r.NextInt(0, 3);
            switch (roll)
            {
                case 0: TransferMetabolismGene(donor, recipient, ref r); break;
                case 1: TransferDomainGene(donor, recipient, ref r);     break;
                case 2: TransferToleranceGene(donor, recipient, ref r);  break;
            }

            prog.RaiseHGT(donor.genome, recipient.genome);
            return true;
        }

        // ── Gene-specific transfer helpers ───────────────────────────────────

        static void TransferMetabolismGene(Prokaryote donor, Prokaryote recipient, ref DetRng r)
        {
            // Shift recipient metabolism one step toward donor's metabolism
            int donorIdx     = (int)donor.genome.metabolism;
            int recipientIdx = (int)recipient.genome.metabolism;
            int delta        = (int)Mathf.Sign(donorIdx - recipientIdx);
            if (delta == 0) return;

            // Cap shift
            int newIdx = Mathf.Clamp(recipientIdx + delta, 0, (int)Metabolism.Mixotroph);
            recipient.genome.metabolism = (Metabolism)newIdx;
        }

        static void TransferDomainGene(Prokaryote donor, Prokaryote recipient, ref DetRng r)
        {
            // Domain is sticky but can nudge maturity if the same domain
            if (donor.genome.domain == recipient.genome.domain)
            {
                // Accelerate recipient's maturity
                recipient.domainMaturity = Mathf.Min(1f, recipient.domainMaturity + 0.05f);
            }
            else if (recipient.genome.domain == Domain.Undecided)
            {
                // Uncommitted cells can be nudged to donor's domain
                if (r.NextFloat() < 0.4f)
                    recipient.genome.domain = donor.genome.domain;
            }
        }

        static void TransferToleranceGene(Prokaryote donor, Prokaryote recipient, ref DetRng r)
        {
            // O₂ tolerance can propagate laterally — key for surviving the GOE
            float diff = donor.genome.oxygenTolerance - recipient.genome.oxygenTolerance;
            if (diff > 0.05f)
                recipient.genome.oxygenTolerance =
                    Mathf.Min(1f, recipient.genome.oxygenTolerance + diff * 0.3f);
        }

        // ── Batch scan helper ────────────────────────────────────────────────

        /// <summary>
        /// Scans all cell pairs closer than <see cref="ContactRadius"/> and
        /// updates their <see cref="Prokaryote.contactTimer"/>.
        /// Call once per frame from Stage2Mode.Tick().
        /// </summary>
        public static void ScanContacts(
            IReadOnlyList<Prokaryote> cells,
            float dt,
            ref DetRng r,
            CellStageProgress prog)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var a = cells[i];
                if (a.dead) continue;
                for (int j = i + 1; j < cells.Count; j++)
                {
                    var b = cells[j];
                    if (b.dead) continue;

                    float accumulated = AccumulateContact(a, b, dt);
                    if (accumulated <= 0f) continue;
                    a.contactTimer += accumulated;
                    b.contactTimer += accumulated;

                    // sustained contact between THIS pair → a transfer (either direction)
                    if (a.contactTimer >= SustainedContactThreshold && b.contactTimer >= SustainedContactThreshold)
                    {
                        bool aGives = r.NextFloat() < 0.5f;
                        var donor = aGives ? a : b; var recipient = aGives ? b : a;
                        if (TryTransfer(donor, recipient, ref r, prog)) { recipient.hgtFlash = 1f; donor.hgtFlash = 0.5f; }
                        a.contactTimer = 0f;
                        b.contactTimer = 0f;
                    }
                }
            }
        }
    }
}
