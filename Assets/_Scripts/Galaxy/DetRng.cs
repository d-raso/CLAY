using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>
    /// Deterministic RNG (SplitMix64). Same seed → same star system, forever — so a server can reload a
    /// world, or regenerate an entire galaxy, and reproduce it exactly (design doc §7: deterministic seeds).
    /// A struct so it's cheap to pass by ref through the generators.
    /// </summary>
    public struct DetRng
    {
        ulong s;

        public DetRng(ulong seed) { s = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed; }

        public ulong NextU64()
        {
            s += 0x9E3779B97F4A7C15UL;
            ulong z = s;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);
        public float Value => (float)NextDouble();
        public float Range(float a, float b) => a + (b - a) * Value;
        public float LogRange(float a, float b) => Mathf.Exp(Range(Mathf.Log(a), Mathf.Log(b)));
        public int RangeInt(int aInclusive, int bExclusive) => aInclusive + (int)(Value * (bExclusive - aInclusive));
        public float NextFloat() => Value;                                         // [0,1)
        public int NextInt(int aInclusive, int bExclusive) => RangeInt(aInclusive, bExclusive);
        public Vector2 InsideUnitCircle()
        {
            float a = Range(0f, Mathf.PI * 2f), d = Mathf.Sqrt(Value);
            return new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
        }

        /// <summary>Combine two seeds into a well-mixed third (e.g. galaxy seed + system index).</summary>
        public static ulong Hash(ulong a, ulong b)
        {
            ulong z = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
