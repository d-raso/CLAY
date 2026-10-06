using System;
using System.Collections.Generic;
using UnityEngine;
using CLAY.Galaxy;
using CLAY.CellStage.Genetics;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// S0 VIROID PATH  (CellStage_Decisions §5, CellStage_SubStages S0 virus path)
    ///
    /// A player who sheds their membrane (by choice, or after repeated bursts) becomes a naked
    /// replicating RNA strand — a viroid (real: the smallest known infectious agents). Semi-living:
    /// it cannot eat or make energy.
    ///
    /// Lifecycle:  Drift → passive absorption into a host → internal replication → Lyse or lie Dormant.
    ///  • Drifting: carried by the pool's currents and the ebbing film, with only a weak steer of your own.
    ///    Open water slowly degrades RNA; sunlit shallows (UV) degrade it fast. Hosts don't let you in —
    ///    you slip in when a feeding protocell takes you up along with its molecules.
    ///  • Inside: your strand copies itself from the host's loose bases (template copying, like any chain),
    ///    competing with the host's own chemistry. The host's membrane shimmers as you load it up.
    ///    In random SPURTS you get control of the host's movement (Merges.HijackSpurt — the more of the
    ///    pool you've infected, the more often and the longer).
    ///  • Lyse: burst the host and scatter your copies (one per finished copy). You continue as one of them.
    ///  • Dormant: stop copying and wait. If you're still dormant inside a living host when the pool's
    ///    founder collapse happens, you integrate into its genome and enter S1 as part of that lineage.
    ///
    /// The viroid is a sub-mode of Stage0Replication (not a full IStageMode): Stage0Replication owns the
    /// list of viroids (the player's one plus AI copies), ticks them, and routes player input.
    /// </summary>
    public sealed class S0Viroid
    {
        // ── state ──────────────────────────────────────────────────────────────────────────
        public Vector2 pos;
        public Vector2 vel;
        public bool alive = true;
        public float digestTimer;            // inside a living cell: seconds until it's digested
        public bool player;                  // the player's viroid (else an AI copy)
        public bool insideHost;              // true while inside a host cell
        public Protocell host;               // null if drifting freely
        public List<int> strand;             // the viroid's RNA sequence
        public float rnaIntegrity = 1f;      // 0 = degraded and dead
        public float replicationProgress;    // 0..1 how full the host is with copies (CopiesToFill)
        public int copiesBuilt;              // finished copies of the strand inside the host
        public bool dormant;                 // waiting inside the host for the founder collapse
        public float spin;                   // visual tumble
        public string causeOfDeath;
        public VirusBody body;               // the player's physical shell (null for wild viroids)
        public System.Func<Protocell, bool> entryCheck;   // may this viroid get into that cell? (null = always)
        public bool interior;                // the player is inside a LIVING cell: the mode drives it (no copying)

        // in-progress template copy (complementary strand, then a copy of that → the original)
        readonly List<int> building = new();
        bool buildingComplement = true;
        float copyTimer;
        Vector2 steer;

        // ── tuning ─────────────────────────────────────────────────────────────────────────
        const float SteerSpeed = 0.55f;      // your own pull — far weaker than a protocell's swimming (~2)
        const float BrownianSpeed = 0.35f;
        const float FlowScale = 0.7f;        // how strongly the pool's currents carry you
        const float AbsorptionRate = 0.6f;   // per second, scaled by the host's uptake gene, while touching a host
        const float DegradationBase = 0.012f;// RNA integrity loss per second in open water
        const float UVDegradation = 0.6f;    // extra loss per unit UV dose
        const float CopyRate = 2.2f;         // template steps per second inside a host (× reaction rate)
        public const int CopiesToFill = 4;   // copies that fill a host (AI viroids lyse at this point)

        public static float DecayMul = 1f;   // the biome's free-RNA degradation (vent heat 2×, cool springs/clay less)
        public bool HijackActive(float poolTime) => insideHost && host != null && !host.dead && host.hijackUntil > poolTime;
        public bool ReadyToLyse => insideHost && !dormant && copiesBuilt >= CopiesToFill;

        // ── init ───────────────────────────────────────────────────────────────────────────
        public S0Viroid(Vector2 startPos, List<int> replicatorStrand, bool isPlayer)
        {
            pos = startPos; player = isPlayer;
            strand = new List<int>(replicatorStrand);
        }

        // ── public API ─────────────────────────────────────────────────────────────────────

        /// One tick. `infectedShare` = share of the living pool currently carrying one of this player's
        /// viroids (drives hijack spurts). `flowAt` samples the environment's currents (may return zero).
        public void Tick(float dt, float poolTime, TidePool pool, IReadOnlyList<Protocell> cells, float reactionRate,
                         float infectedShare, Func<Vector2, Vector2> flowAt, ref DetRng r)
        {
            if (!alive) return;
            spin += dt * (insideHost ? 2.5f : 0.8f);
            if (insideHost && interior) { if (host == null || host.dead) { if (host != null) pos = host.pos; interior = false; ReleaseHost(); } return; }
            if (insideHost) TickInsideHost(dt, poolTime, reactionRate, infectedShare, ref r);
            else TickDrifting(dt, pool, cells, flowAt, ref r);
        }

        /// Player input while drifting: a weak bias on top of the currents. Being mostly out of control is the point.
        public void ApplyPlayerInput(Vector2 inputDir) { steer = Vector2.ClampMagnitude(inputDir, 1f); }

        /// Burst the host and scatter one viroid per finished copy. The caller kills `lysedHost` (spilling its contents)
        /// and adds the copies to its viroid list. This viroid continues, free, at the host's position.
        public List<S0Viroid> Lyse(ref DetRng r, float copyFidelity, out Protocell lysedHost)
        {
            var copies = new List<S0Viroid>();
            lysedHost = null;
            if (!insideHost || host == null) return copies;
            lysedHost = host;
            for (int i = 0; i < copiesBuilt; i++)
            {
                var s = new List<int>(strand);
                for (int k = 0; k < s.Count; k++) if (r.Value > copyFidelity) s[k] = r.RangeInt(0, 4);   // copying errors
                copies.Add(new S0Viroid(host.pos + r.InsideUnitCircle() * host.Radius, s, false) { vel = r.InsideUnitCircle() * 2.5f });
            }
            pos = host.pos; vel = r.InsideUnitCircle() * 2f;
            ReleaseHost();
            return copies;
        }

        /// Stop copying and wait inside the host for the founder collapse.
        public void GoDormant() { if (insideHost) { dormant = true; if (host != null) host.viroidLoad = Mathf.Min(host.viroidLoad, 0.15f); } }
        public void WakeUp() { dormant = false; }

        /// Permanent integration at the founder collapse: the strand joins the host's genome (an endogenous
        /// viroid) and leaves a permanent visible mark. Returns the host genome the player will descend from.
        public Genome Integrate()
        {
            if (!insideHost || host == null || host.dead) return null;
            var g = host.genome;
            g.replicator.AddRange(strand);                          // the viroid becomes part of the heritable sequence
            g.hue = Mathf.Repeat(g.hue + 0.12f, 1f);                // a lasting shift in the lineage's look
            g.pattern = (g.pattern + 1) % 4;
            alive = false; causeOfDeath = "integrated";
            ReleaseHost();
            return g;
        }

        // ── private ────────────────────────────────────────────────────────────────────────

        void TickDrifting(float dt, TidePool pool, IReadOnlyList<Protocell> cells, Func<Vector2, Vector2> flowAt, ref DetRng r)
        {
            // RNA decays in open water, fast under sunlit UV; stranded on dry rock it decays slower
            float depth = pool.Depth(pos);
            rnaIntegrity -= dt * (DegradationBase * DecayMul + pool.UV(pos) * UVDegradation) * (depth <= 0.003f ? 0.5f : 1f) * (body != null ? 1f - 0.75f * body.Cohesion : 1f);   // a free particle decays — slowly, if it was built well
            if (rnaIntegrity <= 0f) { alive = false; causeOfDeath = "degraded"; return; }

            // carried: currents + Brownian jostle + the ebbing film draining into hollows + your weak steer
            Vector2 target = (flowAt != null ? flowAt(pos) * FlowScale : Vector2.zero)
                           + r.InsideUnitCircle() * BrownianSpeed
                           + (player ? steer * SteerSpeed : Vector2.zero);
            if (depth <= 0.003f) target *= 0.05f;
            else if (depth < 0.05f) target += pool.Downhill(pos) * (0.05f - depth) * 50f;
            vel = Vector2.Lerp(vel, target, 1f - Mathf.Exp(-dt * 3f));
            pos += vel * dt;
            pos.x = Mathf.Clamp(pos.x, 0.5f, TidePool.W - 0.5f);
            pos.y = Mathf.Clamp(pos.y, 0.5f, TidePool.H - 0.5f);

            // passive absorption: a protocell touching you may take you up with its molecules
            foreach (var c in cells)
            {
                if (c.dead) continue;
                if ((c.pos - pos).sqrMagnitude > c.Radius * c.Radius) continue;
                if (entryCheck != null && !entryCheck(c)) continue;
                float p = 1f - Mathf.Exp(-dt * AbsorptionRate * (0.5f + c.genome.uptake) * (c.life != null && player ? 4f : 1f));
                if (r.Value < p) { Enter(c); return; }
            }
        }

        void Enter(Protocell target)
        {
            insideHost = true; host = target;
            replicationProgress = 0f; copiesBuilt = 0; dormant = false;
            building.Clear(); buildingComplement = true; copyTimer = 0f;
            target.absorbFlash = 1f;
            rnaIntegrity = Mathf.Min(1f, rnaIntegrity + 0.3f);   // the host's interior shelters you
        }

        /// Leave the host (the player bursting out, or after a takeover).
        public void Exit(Vector2 at, Vector2 v) { ReleaseHost(); interior = false; pos = at; vel = v; }

        void ReleaseHost()
        {
            if (host != null) { host.viroidLoad = 0f; host.hijackUntil = 0f; }
            insideHost = false; host = null; dormant = false;
            building.Clear(); copyTimer = 0f;
        }

        void TickInsideHost(float dt, float poolTime, float reactionRate, float infectedShare, ref DetRng r)
        {
            if (host == null || host.dead)
            {
                // the host died around you (burst / dried): you're spilled back into the water
                if (host != null) pos = host.pos;
                ReleaseHost();
                return;
            }
            pos = host.pos;
            if (dormant) return;

            // template copying from the host's loose bases: build the complement, then a copy of the complement
            // (= the original strand). Each finished original-sense copy is one future viroid.
            copyTimer += dt * CopyRate * reactionRate;
            var template = buildingComplement ? strand : ComplementOf(strand);
            while (copyTimer >= 1f && building.Count < template.Count)
            {
                copyTimer -= 1f;
                int want = MoteChem.Complement(template[building.Count]);
                if (host.free[want] <= 0) { copyTimer = 0f; break; }               // stalls until the host takes one in
                host.free[want]--; building.Add(want);
            }
            if (building.Count >= template.Count && template.Count > 0)
            {
                building.Clear();
                if (buildingComplement) buildingComplement = false;
                else { buildingComplement = true; copiesBuilt++; }
            }
            replicationProgress = Mathf.Clamp01((copiesBuilt + building.Count / (float)Mathf.Max(template.Count, 1) * 0.5f) / CopiesToFill);
            host.viroidLoad = replicationProgress;                                   // the membrane shimmers as you fill it

            // hijack spurts: random moments of control over the host's movement
            if (player && host.hijackUntil <= poolTime)
            {
                Merges.HijackSpurt(infectedShare, out float perSecond, out float duration);
                if (r.Value < perSecond * dt) host.hijackUntil = poolTime + duration;
            }
        }

        static List<int> ComplementOf(List<int> s)
        {
            var c = new List<int>(s.Count);
            foreach (int b in s) c.Add(MoteChem.Complement(b));
            return c;
        }
    }
}
