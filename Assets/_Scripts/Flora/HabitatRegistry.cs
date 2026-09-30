using System.Collections.Generic;

namespace CLAY.Flora
{
    // Runtime store of the flora the player has generated and SAVED for each planet's habitat. Keyed by
    // (systemSeed, planetIndex) so it survives regenerating the same system deterministically. In-memory for now;
    // a later pass can serialize it to disk.
    public static class HabitatRegistry
    {
        static readonly Dictionary<(ulong, int), List<PlantGenome>> _byPlanet = new();

        public static void Add(ulong systemSeed, int planetIndex, PlantGenome g)
        {
            var key = (systemSeed, planetIndex);
            if (!_byPlanet.TryGetValue(key, out var list)) { list = new List<PlantGenome>(); _byPlanet[key] = list; }
            list.Add(g);
        }

        public static IReadOnlyList<PlantGenome> Get(ulong systemSeed, int planetIndex)
            => _byPlanet.TryGetValue((systemSeed, planetIndex), out var list) ? list : System.Array.Empty<PlantGenome>();

        public static int Count(ulong systemSeed, int planetIndex)
            => _byPlanet.TryGetValue((systemSeed, planetIndex), out var list) ? list.Count : 0;
    }
}
