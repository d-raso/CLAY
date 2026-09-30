using System.Text;

namespace CLAY.Galaxy
{
    // Deterministic pronounceable-name generator for stars and worlds. Star names are catalogue-style proper
    // nouns (e.g. "Delfor", "Vessanor"); colloquial names are the softer native names the system's own
    // inhabitants would use for a world (e.g. "Belininian"), built from a root plus a demonymic suffix.
    public static class NameGen
    {
        static readonly string[] Onset =
            { "b", "br", "d", "dr", "f", "g", "gr", "k", "kr", "l", "m", "n", "p", "pr", "r",
              "s", "st", "str", "t", "tr", "v", "vr", "z", "th", "sh", "kh", "ph", "cl", "gl", "sy" };
        static readonly string[] Vowel =
            { "a", "e", "i", "o", "u", "ae", "ei", "ia", "ou", "io", "y", "ae", "ea" };
        static readonly string[] Coda =
            { "", "", "n", "r", "s", "l", "th", "rn", "ss", "ld", "nd", "sk", "x", "m", "st" };
        static readonly string[] Suffix =
            { "ia", "ian", "inian", "or", "eth", "une", "ara", "oth", "is", "a", "yr", "ex", "essa", "une", "ora" };

        // A star's proper name: 2–3 syllables, capitalised.
        public static string Star(ulong seed)
        {
            var r = new DetRng(seed ^ 0x5741A2C0FFEEUL);
            return Cap(Root(ref r, r.RangeInt(2, 4)));
        }

        // A world's native (colloquial) name: a root + a soft demonymic suffix, capitalised.
        public static string Colloquial(ulong seed)
        {
            var r = new DetRng(seed ^ 0xC0110C1A11DEUL);
            string root = Root(ref r, r.RangeInt(1, 3));
            // Avoid an awkward vowel pile-up between the root and the suffix.
            string suf = Suffix[r.RangeInt(0, Suffix.Length)];
            if (IsVowel(root[root.Length - 1]) && IsVowel(suf[0])) root = root.Substring(0, root.Length - 1);
            return Cap(root + suf);
        }

        static string Root(ref DetRng r, int syllables)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < syllables; i++)
            {
                sb.Append(Onset[r.RangeInt(0, Onset.Length)]);
                sb.Append(Vowel[r.RangeInt(0, Vowel.Length)]);
                if (i == syllables - 1 || r.Value < 0.4f) sb.Append(Coda[r.RangeInt(0, Coda.Length)]);
            }
            return sb.ToString();
        }

        static bool IsVowel(char c) => "aeiouy".IndexOf(char.ToLower(c)) >= 0;
        static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1);
    }
}
