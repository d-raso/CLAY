using System;
using System.Collections.Generic;
using UnityEngine;

namespace CLAY.CellStage.Genetics
{
    /// One living body of a species — a player's cell or an AI cell. The gene pool is the set of these.
    public interface ILivingBody
    {
        Genome Genome { get; }
        Vector2 Position { get; }
        bool IsAlive { get; }
        bool IsPlayerControlled { get; }
        int SpeciesId { get; set; }
    }

    public sealed class Species
    {
        public int id;
        public string debugName;            // never shown to players
        public Genome founder;
        public readonly List<ILivingBody> living = new();
        public readonly List<int> coPlayers = new();   // extra players who joined (virus takeover / merges) — CellStage_Decisions §4
        public bool Extinct => living.Count == 0;
    }

    /// <summary>
    /// The living gene pool (CellStage_Decisions §3). Tracks which bodies belong to which species; supplies the
    /// respawn picker with the species' currently-alive VARIANTS; removes a dead body's unique variant from the
    /// pool (death is selection — survivors' share grows automatically because they're what's left).
    /// </summary>
    public sealed class SpeciesRegistry
    {
        public const float SpeciationDistance = 0.9f;   // genome distance past which offspring found a new species
        readonly Dictionary<int, Species> species = new();
        int nextId = 1;

        public IReadOnlyDictionary<int, Species> All => species;
        public event Action<Species> OnExtinct;

        public Species Found(Genome g, string debugName = null)
        {
            var s = new Species { id = nextId++, founder = g.Clone(), debugName = debugName ?? $"sp{nextId - 1}" };
            species[s.id] = s; return s;
        }

        public Species Get(int id) => species.TryGetValue(id, out var s) ? s : null;

        /// Register a newborn: joins its parent's species unless it has drifted far from the founder.
        public void Born(ILivingBody b, int parentSpecies)
        {
            var sp = Get(parentSpecies);
            if (sp == null || b.Genome.Distance(sp.founder) > SpeciationDistance) sp = Found(b.Genome);
            b.SpeciesId = sp.id;
            sp.living.Add(b);
        }

        public void Died(ILivingBody b)
        {
            var sp = Get(b.SpeciesId); if (sp == null) return;
            sp.living.Remove(b);
            if (sp.Extinct) OnExtinct?.Invoke(sp);
        }

        /// Respawn options: living, NOT player-controlled bodies of the species, most distinct first (so the list
        /// shows real variety, not twenty copies of one variant).
        public List<ILivingBody> RespawnOptions(int speciesId, int max = 8)
        {
            var res = new List<ILivingBody>();
            var sp = Get(speciesId); if (sp == null) return res;
            var pool = new List<ILivingBody>();
            foreach (var b in sp.living) if (b.IsAlive && !b.IsPlayerControlled) pool.Add(b);
            while (res.Count < max && pool.Count > 0)
            {
                int best = 0; float bestD = -1f;
                for (int i = 0; i < pool.Count; i++)
                {
                    float d = float.MaxValue;
                    foreach (var c in res) d = Mathf.Min(d, pool[i].Genome.Distance(c.Genome));
                    if (res.Count == 0) d = 0f;
                    if (d > bestD) { bestD = d; best = i; }
                }
                res.Add(pool[best]); pool.RemoveAt(best);
            }
            return res;
        }

        /// Share of a species' living bodies that satisfy a predicate (e.g. infected by a given virus).
        public float Fraction(int speciesId, Predicate<ILivingBody> pred)
        {
            var sp = Get(speciesId); if (sp == null || sp.living.Count == 0) return 0f;
            int n = 0; foreach (var b in sp.living) if (pred(b)) n++;
            return n / (float)sp.living.Count;
        }
    }
}
