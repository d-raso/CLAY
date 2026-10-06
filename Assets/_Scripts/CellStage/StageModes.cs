using UnityEngine;
using CLAY.CellStage.Stage1;
using CLAY.CellStage.Stage2;

namespace CLAY.CellStage
{
    /// <summary>
    /// One sub-stage's gameplay. CellStageWorld owns exactly one active mode and swaps it when the progress
    /// graduates. Modes build their own objects under `root` and tear them down in End().
    /// </summary>
    public interface IStageMode
    {
        void Begin(CellStageWorld world, Transform root);
        void Tick(float dt);
        void End();
        /// Optional immediate-mode overlay (dev/debug only — the game itself shows no HUD).
        void DebugGUI();
    }

    /// A mode that needs an on-screen choice (rare — e.g. the pool's "Become life?" moment).
    public interface IStageGUI { void StageGUI(); }

    /// Placeholder for a sub-stage not built yet: keeps the arc navigable end-to-end.
    public abstract class StubMode : IStageMode
    {
        protected CellStageWorld world;
        protected abstract string Blurb { get; }
        public virtual void Begin(CellStageWorld w, Transform root) { world = w; Debug.Log($"[CellStage] {GetType().Name} (stub): {Blurb}"); }
        public virtual void Tick(float dt) { }
        public virtual void End() { }
        public virtual void DebugGUI() { GUILayout.Label($"<b>{GetType().Name}</b> — not built yet.\n{Blurb}"); }
    }

    /// S2 "Little Engines": thin adapter so CellStageWorld.SwapMode can construct Stage2Mode
    /// without knowing the Stage2 namespace. Delegates everything to Stage2Mode.
    public sealed class Stage2Prokaryote : IStageMode
    {
        readonly Stage2Mode impl = new();
        public void Begin(CellStageWorld w, Transform root) => impl.Begin(w, root);
        public void Tick(float dt)                          => impl.Tick(dt);
        public void End()                                   => impl.End();
        public void DebugGUI()                              => impl.DebugGUI();
    }

    /// S3: cytoskeleton mutation → first crude engulf → keep a symbiont (mitochondrion / chloroplast / chemo…);
    /// glowing player candidates (Merges.CandidateGlow). → Milestone.Endosymbiont.
    public sealed class Stage3Endosymbiosis : StubMode { protected override string Blurb => "cytoskeleton, first engulf, keep a symbiont, player endosymbiont candidates"; }

    /// S4: the agar.io free-for-all — bridges to the existing prototype in _Scripts/Gameplay (CellBiomass,
    /// Engulfment, AIController, EvolutionNodes) + division, living-gene-pool respawn picker. → PopulationThreshold.
    public sealed class Stage4Eukaryote : StubMode { protected override string Blurb => "agar.io FFA on the Gameplay prototype + division + respawn into living variants"; }

    /// S5: clonal or aggregative colonies; stalk/spore by morphology & chance; cell types. → MulticellularBody.
    public sealed class Stage5Colony : StubMode { protected override string Blurb => "clonal / aggregative colonies, cell differentiation, graduation"; }

    /// The virus path: drift, attach, inject, hijack spurts (Merges.HijackSpurt), dormancy, 40 % takeover.
    public sealed class VirusMode : StubMode { protected override string Blurb => "attach, inject, hijack spurts, dormancy, 40% species takeover"; }
}
