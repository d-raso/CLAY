using System.Collections.Generic;
using UnityEngine;

namespace CLAY.CellStage.Genetics
{
    /// <summary>
    /// Two players in one organism (CellStage_Decisions §4–5).
    /// • Before takeover: a virus player gets RANDOM SPURTS of keyboard control over the bodies it infects; how
    ///   often/strong scales with the share of the species it currently infects.
    /// • At ≥ 40 % of a species' living bodies the virus integrates permanently and the virus player becomes an
    ///   independent member of that species (own cell, full control).
    /// • Endosymbiont candidates (small, high-energy player cells) glow; engulfed, they may join (merge rules TBD).
    /// </summary>
    public static class Merges
    {
        public const float TakeoverFraction = 0.40f;

        /// Probability per second that the virus seizes a host's controls, and how long a spurt lasts.
        public static void HijackSpurt(float infectedFraction, out float perSecond, out float duration)
        {
            float f = Mathf.Clamp01(infectedFraction / TakeoverFraction);
            perSecond = Mathf.Lerp(0.03f, 0.35f, f);
            duration = Mathf.Lerp(0.3f, 1.6f, f);
        }

        public static bool TakeoverReached(float infectedFraction) => infectedFraction >= TakeoverFraction;

        /// Glow strength marking a player cell as an endosymbiont candidate (small + energetic).
        public static float CandidateGlow(float relativeSize, float energyRate)
            => Mathf.Clamp01((1f - relativeSize * 2f) * 1.5f) * Mathf.Clamp01(energyRate * 1.2f - 0.3f);
    }

    /// Per-host infection state (virus path). Stub: wired when the Virus mode is built.
    public sealed class Infection
    {
        public int virusPlayerId;
        public float load;          // 0..1 viral load in this host
        public bool dormant;        // integrated, waiting
        public float spurtUntil;    // time until which the virus holds the controls
    }
}
