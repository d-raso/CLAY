using UnityEngine;

namespace CLAY.CellStage
{
    /// <summary>
    /// PROCEDURAL MUSIC for the cell stage: a small real-time synthesizer (no audio files) playing a score written
    /// by rules of ordinary music theory, steered by the pool.
    ///
    /// FORM: the piece is built from SECTIONS of 4 or 8 bars — Intro, Theme A, Theme B, Development, Breakdown, Rest —
    /// each with its own chord progression and its own LAYERS (pad / bass / melody / arpeggio / bells). Layers come
    /// and go, so there is space and silence; nothing plays all the time.
    /// HARMONY: per-biome functional progressions in the biome's mode (I–V–vi–IV, i–♭II–i–♭VII, Dorian i–IV vamps,
    /// ii–V–i …), voiced with voice leading (each chord takes the inversion nearest the last). Occasional modulation to
    /// the relative key, the subdominant or dominant at section boundaries.
    /// MELODY: a 4-note MOTIF (intervals + rhythm) is invented per theme and developed the classical way across a
    /// 4-bar phrase: statement → repetition on the new chord → sequence (moved up a step on a rising tide, down as it
    /// recedes) → cadence (a long note on the chord root or third). Strong beats snap to chord tones.
    /// BASS: root on beat 1, fifth on beat 3, a stepwise approach note into the next chord.
    ///
    /// Pool inputs: biome (mode, key, tempo, instruments, room) · daylight (night flips to the darker mode, lowers,
    /// thins) · tide (sequence direction, brightness) · water flow (pad brightness, water wash) · food (sparkles) ·
    /// crowd (busier sections) · danger (more Development / tremolo minor second) · events (life, divide, gene, death).
    /// F11 mutes.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class CellMusic : MonoBehaviour
    {
        public static CellMusic Instance;

        // ── inputs (main thread writes, audio thread reads) ──
        public volatile int biome;
        public float day = 1f, tideRate, flow, food, crowd, danger, alive;
        volatile int pendingEvent;
        public bool muted;
        public float master = 0.45f;
        public string NowPlaying = "";
        public volatile int themeSeed;
        int hookSeed = -1, hookStart;
        readonly int[] hookAnsI = new int[4], hookAnsR = new int[4];

        public enum Cue { None, Life, Divide, Gene, Death }
        public void Event(Cue c) => pendingEvent = (int)c;
        volatile int pendingGood, pendingDull;
        /// A small sound for taking something in: an in-key pluck if it's useful, a muted thud if it's of no value.
        public void Absorb(bool useful) { if (useful) pendingGood++; else pendingDull++; }
        volatile int pendingSnap;   // 1 snap · 2 a good snap (bonded / paired)
        /// The click of a molecule snapping into place.
        public void Snap(bool good) { pendingSnap = good ? 2 : 1; }
        int absorbCooldown;

        int sr;
        readonly System.Random rng = new System.Random(1234);

        // ── theory tables ──
        static readonly int[] Lydian = { 0, 2, 4, 6, 7, 9, 11 }, Ionian = { 0, 2, 4, 5, 7, 9, 11 }, Aeolian = { 0, 2, 3, 5, 7, 8, 10 },
            Phrygian = { 0, 1, 3, 5, 7, 8, 10 }, Dorian = { 0, 2, 3, 5, 7, 9, 10 }, Mixolydian = { 0, 2, 4, 5, 7, 9, 10 }, Harmonic = { 0, 2, 3, 5, 7, 8, 11 };
        int[] Scale(int b, bool night) => b switch
        {
            1 => night ? Harmonic : Phrygian,
            2 => night ? Aeolian : Lydian,
            3 => night ? Aeolian : Dorian,
            _ => night ? Aeolian : (sectionAlt ? Mixolydian : Ionian),
        };
        // progressions as scale degrees (0 = I). Several per biome; sections pick among them.
        static readonly int[][][] Progs =
        {
            new[] { new[] { 0, 4, 5, 3 }, new[] { 0, 5, 3, 4 }, new[] { 3, 4, 0, 0 }, new[] { 5, 3, 0, 4 }, new[] { 1, 4, 0, 5 } },   // tidal: pop/classical
            new[] { new[] { 0, 1, 0, 6 }, new[] { 0, 6, 5, 6 }, new[] { 0, 3, 0, 1 }, new[] { 5, 6, 0, 0 } },                     // vent: Phrygian ♭II, Andalusian
            new[] { new[] { 0, 1, 0, 1 }, new[] { 0, 4, 1, 0 }, new[] { 3, 1, 0, 4 }, new[] { 5, 1, 0, 0 } },                     // spring: Lydian I–II shimmer
            new[] { new[] { 0, 3, 0, 3 }, new[] { 0, 6, 3, 0 }, new[] { 1, 4, 0, 0 }, new[] { 0, 2, 3, 4 } },                     // clay: Dorian i–IV, ii–V–i
        };
        static readonly int[] ArpPat = { 0, 2, 4, 7, 4, 2, 0, 2 }, Triad = { 0, 2, 4 };
        static readonly int[][] Rhythms = { new[] { 0, 2, 3, 4 }, new[] { 0, 1, 2, 4 }, new[] { 0, 3, 4, 6 }, new[] { 0, 2, 4, 6 }, new[] { 0, 1, 4, 5 }, new[] { 0, 3, 5, 6 } };
        static readonly int[] RootMidi = { 57, 45, 62, 53 };
        static readonly float[] Bpm = { 84, 58, 50, 66 };
        static readonly float[] RoomMix = { 0.26f, 0.36f, 0.5f, 0.16f };
        static readonly string[] BiomeName = { "tidal", "vent", "spring", "clay" };

        enum Sec { Intro, ThemeA, ThemeB, Development, Breakdown, Rest }
        // STYLES: the ways a section can be built. A planet seeds its own palette of them (its musical character);
        // the biome leans the choice; Theme A always uses the planet's signature style so the theme stays recognisable.
        //   Arpeggio — broken chords, bass, motif        Chorale — held chords, slow singing melody in half notes
        //   Ostinato — a repeating marimba cell that slowly phases against itself (minimalism)
        //   CallResponse — a phrase, then an answer in another voice      Drone — a held low pedal, free sparse melody
        //   Groove — swung eighths, syncopated bass, off-beat stabs       Waltz — 3/4, oom-pah-pah
        //   Canon — the melody is echoed half a bar later in a second voice
        enum Style { Arpeggio, Chorale, Ostinato, CallResponse, Drone, Groove, Waltz, Canon }
        static readonly Style[][] BiomeLean =
        {
            new[] { Style.Arpeggio, Style.Groove, Style.CallResponse, Style.Waltz },   // tidal flat
            new[] { Style.Drone, Style.Ostinato, Style.Chorale },                       // vent field
            new[] { Style.Chorale, Style.Drone, Style.Canon },                          // hot spring
            new[] { Style.Groove, Style.Waltz, Style.Ostinato, Style.CallResponse },    // clay shelf
        };
        readonly Style[] palette = new Style[5]; int paletteFor = -1;
        Style style;
        int barLen = 8;                                   // eighths per bar (6 in a waltz)
        bool swing;
        long gstep;                                       // global step counter (canon echoes)
        readonly int[] echoDeg = new int[8]; readonly long[] echoAt = new long[8];
        readonly int[] ostCell = new int[4]; int ostPhase;
        // layer masks: pad, bass, melody, arp, bells
        const int Pad = 1, Bass = 2, Mel = 4, Arp = 8, Bell = 16;

        // ── score state ──
        Sec sec = Sec.Rest; int secBars, bar, stepInBar; int[] prog; int layers; bool sectionAlt;
        int key;                                   // semitone offset (modulation)
        readonly int[] motifInt = new int[4], motifRhy = new int[4];   // motif: degree steps + onset (eighths in bar)
        readonly int[] motifIntB = new int[4], motifRhyB = new int[4];
        int chordDeg, nextChordDeg, melodyDeg = 7, lastPadVoicing0 = 7;
        readonly int[] voicing = new int[3];
        double stepCounter;
        float sDay = 1, sTide, sFlow, sFood, sCrowd, sDanger;
        bool night;

        // ── synth ──
        const int MaxVoices = 20, KsMax = 4096;
        struct Voice { public bool on; public int type; public float freq, ph, ph2, amp, env, decay, pan, atk; public int ksLen, ksPos; public float damp; }
        readonly Voice[] v = new Voice[MaxVoices];
        readonly float[][] ks = new float[MaxVoices][];
        readonly float[] padF = new float[3], padPh = new float[6];
        float padEnv, padTarget, padLp, padLp2;
        float tensPh, tensF = 220f, thumpF = 55f;
        float washLp1, washLp2, rumbleLp, brown, heartEnv, tremPh;
        readonly float[][] comb = new float[4][]; readonly int[] combPos = new int[4]; readonly float[] combLp = new float[4];
        readonly float[][] ap = new float[2][]; readonly int[] apPos = new int[2];

        void Awake()
        {
            Instance = this;
            sr = AudioSettings.outputSampleRate;
            for (int i = 0; i < MaxVoices; i++) ks[i] = new float[KsMax];
            int[] cl = { 1116, 1188, 1277, 1356 }; int[] al = { 556, 441 };
            float sc = sr / 44100f;
            for (int i = 0; i < 4; i++) comb[i] = new float[Mathf.RoundToInt(cl[i] * sc * 1.6f)];
            for (int i = 0; i < 2; i++) ap[i] = new float[Mathf.RoundToInt(al[i] * sc)];
            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("CellMusicCarrier", sr, 1, sr, false);
            src.loop = true; src.spatialBlend = 0f; src.playOnAwake = false; src.volume = 1f;
            src.Play();
            prog = Progs[0][0];
        }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void Update() { if (Input.GetKeyDown(KeyCode.F11)) muted = !muted; }

        static float Mtof(float m) => 440f * Mathf.Pow(2f, (m - 69f) / 12f);
        float Note(int b, int deg, int octShift = 0)
        {
            var s = Scale(b, night);
            int oct = Mathf.FloorToInt(deg / (float)s.Length);
            int idx = deg - oct * s.Length;
            return Mtof(RootMidi[b] + key + (night ? -5 : 0) + (oct + octShift) * 12 + s[idx]);
        }
        int R(int n) => rng.Next(n);
        bool P(float p) => rng.NextDouble() < p;

        // ── form ──
        void NextSection(int b)
        {
            // choose the next section by simple form logic + the pool's mood
            Sec prev = sec;
            night = sDay < 0.35f;   // the mode changes only at a section boundary, never under a held chord
            float busy = sCrowd + sDanger;
            Sec s;
            switch (prev)
            {
                case Sec.Rest: s = P(0.6f) ? Sec.Intro : Sec.ThemeA; break;
                case Sec.Intro: s = Sec.ThemeA; break;
                case Sec.ThemeA: s = P(0.45f) ? Sec.ThemeB : P(0.5f + busy * 0.3f) ? Sec.Development : Sec.Breakdown; break;
                case Sec.ThemeB: s = P(0.5f) ? Sec.ThemeA : Sec.Development; break;
                case Sec.Development: s = P(0.5f) ? Sec.Breakdown : Sec.ThemeA; break;
                default: s = P(0.35f + (1f - sDay) * 0.3f) ? Sec.Rest : Sec.ThemeA; break;
            }
            if (sDanger > 0.5f && s != Sec.Development && P(0.5f)) s = Sec.Development;
            sec = s; bar = 0;
            secBars = s == Sec.Rest ? 2 + R(3) : s == Sec.Intro || s == Sec.Breakdown ? 4 : 8;
            var set = Progs[b];
            prog = s switch
            {
                Sec.ThemeA => set[0],
                Sec.ThemeB => set[1 + R(set.Length - 1)],
                _ => set[R(set.Length)],
            };
            sectionAlt = P(0.3f);
            if (paletteFor != themeSeed * 7 + b) BuildPalette(b);
            style = s == Sec.ThemeA ? palette[0] : palette[R(palette.Length)];
            barLen = style == Style.Waltz ? 6 : 8;
            swing = style == Style.Groove;
            layers = s switch
            {
                Sec.Rest => 0,
                Sec.Intro => P(0.5f) ? Pad : Arp | Bell,
                Sec.ThemeA => Mel | Bass | (P(0.5f) ? Pad : Arp),
                Sec.ThemeB => Mel | Bass | Arp | (P(0.3f) ? Pad : 0),
                Sec.Development => Mel | Bass | Arp | (P(0.4f) ? Bell : 0),
                _ => P(0.5f) ? Pad | Bell : Arp,
            };
            if (night && P(0.5f)) layers &= ~Arp;                       // night thins the texture
            if (b == 2 && (layers & Mel) != 0) layers |= Bell;          // the spring always rings
            // themes get their own motif; Theme A keeps its motif when it returns (that's what makes it a theme)
            if (hookSeed != themeSeed) MakeHook();
            if (s == Sec.ThemeB || s == Sec.Development) MakeMotif(motifIntB, motifRhyB);
            // modulation at a section boundary: to the subdominant / dominant / relative, and back home later
            if (s == Sec.ThemeB && P(0.25f)) key = P(0.5f) ? 5 : -5;
            else if (s == Sec.ThemeA) key = 0;
            if (style == Style.Chorale || style == Style.Drone) layers |= Pad;
            if (style == Style.Ostinato) { for (int i = 0; i < 4; i++) ostCell[i] = new[] { 0, 2, 4, 7, 5, 9 }[R(6)]; ostPhase = 0; }
            NowPlaying = $"{BiomeName[b]} · {s} · {style} · {(night ? "night" : "day")}{(key != 0 ? $" · key {key:+0;-0}" : "")}";
        }

        void BuildPalette(int b)
        {
            paletteFor = themeSeed * 7 + b;
            var pr = new System.Random(themeSeed ^ 0x5EED);
            var lean = BiomeLean[b];
            palette[0] = lean[(themeSeed & 0x7fff) % lean.Length];        // the planet's signature style in this biome
            palette[1] = lean[pr.Next(lean.Length)];
            for (int i = 2; i < palette.Length; i++) palette[i] = (Style)pr.Next(8);   // and some of its own surprises
        }

        /// THE HOOK: this planet's theme — a 2-bar question / answer, fixed by the planet seed, so it returns every time
        /// Theme A plays (and broadened when you come alive). Catchy by construction: a syncopated rhythm, stepwise
        /// motion with one characteristic leap, and an answer that echoes the question then resolves home.
        void MakeHook()
        {
            hookSeed = themeSeed;
            var hr = new System.Random(themeSeed);
            int[][] catchy = { new[] { 0, 2, 3, 6 }, new[] { 0, 1, 2, 4 }, new[] { 0, 3, 4, 6 }, new[] { 0, 2, 4, 5 }, new[] { 0, 1, 3, 6 } };
            var rr = catchy[hr.Next(catchy.Length)];
            for (int i = 0; i < 4; i++) { motifRhy[i] = rr[i]; hookAnsR[i] = rr[i]; }
            int leapAt = 1 + hr.Next(3), leap = hr.Next(2) == 0 ? 3 : -2;
            motifInt[0] = 0;
            for (int i = 1; i < 4; i++) motifInt[i] = i == leapAt ? leap : (hr.Next(2) == 0 ? 1 : -1);
            // the answer: same opening, then turns back toward where it began (a cadence in miniature)
            hookAnsI[0] = 0; hookAnsI[1] = motifInt[1]; hookAnsI[2] = -motifInt[2];
            hookAnsI[3] = -(hookAnsI[1] + hookAnsI[2]);
            hookStart = hr.Next(3) * 2;
        }

        void MakeMotif(int[] iv, int[] rh)
        {
            // a short cell: 4 notes, mostly steps with one leap, rhythm on eighths of a bar (first note on the downbeat)
            var r = Rhythms[R(Rhythms.Length)];
            for (int i = 0; i < 4; i++) rh[i] = r[i];
            iv[0] = 0;
            int leapAt = 1 + R(3);
            for (int i = 1; i < 4; i++) iv[i] = i == leapAt ? (P(0.5f) ? 3 : -2) : (P(0.5f) ? 1 : -1);
        }

        // ── the step sequencer (eighth notes, 4/4) ──
        void Step(int b)
        {
            if (stepInBar == 0)
            {
                if (pendingAlter == 1) { pendingAlter = 0; Bloom(b); }
                else if (bar >= secBars) NextSection(b);
                if (pendingAlter == 2) { pendingAlter = 0; layers |= Arp | Bell; }
                chordDeg = prog[bar % prog.Length];
                nextChordDeg = prog[(bar + 1) % prog.Length];
                if ((layers & Pad) != 0) VoicePad(b);
                padTarget = (layers & Pad) != 0 ? 1f : 0f;
                if (b == 1 && sec != Sec.Rest) heartEnv = 1f;
                thumpF = Note(b, chordDeg, -3);
                tensF = Note(b, chordDeg + 1, 0);
            }
            if (b == 1 && stepInBar == 3 && sec != Sec.Rest) heartEnv = 0.6f;
            float vel = 0.85f + (stepInBar == 0 ? 0.15f : stepInBar == 4 ? 0.08f : 0f);

            Accompany(b, vel);
            // melody: motif developed over a 4-bar phrase
            if ((layers & Mel) != 0 && style == Style.Drone)
            {
                if ((stepInBar == 0 || stepInBar == 5) && P(0.45f))
                {
                    melodyDeg = Mathf.Clamp(melodyDeg + (P(0.5f) ? 1 : -1) * (P(0.2f) ? 2 : 1) + (sTide > 0.3f ? 1 : sTide < -0.3f ? -1 : 0), 6, 15);
                    Lead(b, Note(b, melodyDeg), 0.15f, 2.2f);
                }
            }
            else if ((layers & Mel) != 0)
            {
                bool useB = sec == Sec.ThemeB || sec == Sec.Development;
                int phraseBar = bar % 4;
                var iv = useB ? motifIntB : (phraseBar == 1 ? hookAnsI : motifInt); var rh = useB ? motifRhyB : (phraseBar == 1 ? hookAnsR : motifRhy);
                if (phraseBar < 3)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (MapOnset(rh[i]) != stepInBar) continue;
                        if (i == 0)
                        {
                            // statement on the chord; repetition on the next chord; sequence shifted with the tide
                            int seqShift = phraseBar == 2 ? (sTide >= 0f ? 1 : -1) : 0;
                            melodyDeg = useB ? chordDeg + 7 + (P(0.5f) ? 2 : 0) + seqShift : 7 + hookStart + seqShift;   // the hook always starts on the same note
                            if (sec == Sec.Development && P(0.4f)) melodyDeg += P(0.5f) ? 2 : -2;   // fragmentation
                        }
                        else melodyDeg += sec == Sec.Development && phraseBar == 1 ? -iv[i] : iv[i];   // inversion when developing
                        if (useB && (stepInBar == 0 || stepInBar == 4)) melodyDeg = SnapToChord(melodyDeg);
                        melodyDeg = Mathf.Clamp(melodyDeg, 5, 16);
                        float len = style == Style.Chorale ? 2.5f : i == 3 ? 1.4f : 1f;
                        bool answer = style == Style.CallResponse && phraseBar == 1;
                        if (answer) Trigger(2, Note(b, melodyDeg, 1), 0.12f * vel, 2f, 0.5f);   // the response, higher, in bells
                        else Lead(b, Note(b, melodyDeg), 0.18f * vel, len);
                        if (style == Style.Canon) Echo(melodyDeg - 7, 4);
                    }
                }
                else if (stepInBar == 0)
                {
                    // cadence: a long note on the chord's root or third
                    melodyDeg = chordDeg + 7 + (P(0.6f) ? 0 : 2);
                    Lead(b, Note(b, melodyDeg), 0.17f, 2.6f);
                    if (style == Style.Canon) Echo(melodyDeg - 7, 4);
                }
            }
            // canon echoes falling due
            for (int e = 0; e < echoAt.Length; e++)
                if (echoAt[e] > 0 && echoAt[e] == gstep) { echoAt[e] = 0; Trigger(b == 0 ? 3 : 2, Note(b, echoDeg[e]), 0.12f, 1.4f, -0.5f); }
            // bells: rare high answers to the melody
            if ((layers & Bell) != 0 && (stepInBar == 3 || stepInBar == 7) && P(0.3f))
                Trigger(2, Note(b, chordDeg + 7 + 2 * R(3), 1), 0.05f, 3.5f, P(0.5f) ? -0.6f : 0.6f);
            // food sparkles: a pentatonic shimmer over whatever plays
            if (P(sFood * 0.18f)) Trigger(2, Note(b, chordDeg + 14 + 2 * R(3)), 0.03f, 1.2f, (float)(rng.NextDouble() * 2 - 1));

            stepInBar++;
            gstep++;
            if (stepInBar >= barLen) { stepInBar = 0; bar++; }
        }

        // the melody's rhythm, reshaped by the style
        int MapOnset(int o)
        {
            switch (style)
            {
                case Style.Chorale: return o == 0 ? 0 : o <= 3 && o >= 2 ? 4 : -1;          // half notes
                case Style.Waltz: return o * 6 / 8;                                          // squeezed into 3/4
                case Style.Drone: return o == 0 || o == 4 ? o : -1;                          // sparse
                default: return o;
            }
        }

        void Echo(int deg, int stepsLater)
        {
            for (int e = 0; e < echoAt.Length; e++) if (echoAt[e] == 0) { echoAt[e] = gstep + stepsLater; echoDeg[e] = deg; return; }
        }

        void Accompany(int b, float vel)
        {
            bool bass = (layers & Bass) != 0, arp = (layers & Arp) != 0;
            int s8 = stepInBar;
            switch (style)
            {
                case Style.Chorale:
                    if (bass && s8 == 0) Trigger(4, Note(b, chordDeg, -2), 0.2f, 3f, 0f);
                    break;
                case Style.Drone:
                    if (s8 == 0 && (bar % 2 == 0)) Trigger(4, Note(b, Progressions0(b), -2), 0.2f, 6f, 0f);   // a pedal on the home note
                    if (arp && s8 == 4 && P(0.25f)) Trigger(2, Note(b, chordDeg + 4, 1), 0.04f, 4f, 0.4f);
                    break;
                case Style.Ostinato:
                    // the cell repeats on every eighth; every 4 bars a second copy slips one step out of phase
                    if (arp || bass)
                    {
                        int root = (bar / 2) % 2 == 0 ? chordDeg : prog[0];
                        Trigger(3, Note(b, root + ostCell[s8 % 4], 0), 0.07f, 0.5f, -0.3f);
                        if (bar >= 4) Trigger(3, Note(b, root + ostCell[(s8 + 1 + ostPhase) % 4], 1), 0.04f, 0.4f, 0.4f);
                        if (s8 == 0 && bar % 4 == 3) ostPhase++;
                    }
                    if (bass && s8 == 0) Trigger(4, Note(b, chordDeg, -2), 0.18f, 2f, 0f);
                    break;
                case Style.Groove:
                    if (bass)
                    {
                        if (s8 == 0) Trigger(4, Note(b, chordDeg, -2), 0.22f * vel, 0.6f, 0f);
                        else if (s8 == 3) Trigger(4, Note(b, chordDeg + 4, -2), 0.16f, 0.4f, 0f);
                        else if (s8 == 5) Trigger(4, Note(b, chordDeg + 7, -2), 0.14f, 0.3f, 0f);
                        else if (s8 == 6 && P(0.6f)) Trigger(4, Note(b, chordDeg + 6, -2), 0.12f, 0.3f, 0f);
                    }
                    if (arp && (s8 == 2 || s8 == 6))   // off-beat chord stabs
                        for (int k = 0; k < 3; k++) Trigger(0, Note(b, chordDeg + Triad[k], 0), 0.05f, 0.25f, -0.3f + k * 0.3f);
                    break;
                case Style.Waltz:
                    if (bass && s8 == 0) Trigger(4, Note(b, chordDeg, -2), 0.22f, 1.2f, 0f);
                    if (arp && (s8 == 2 || s8 == 4))
                        for (int k = 1; k < 3; k++) Trigger(b == 1 ? 1 : 3, Note(b, chordDeg + Triad[k], 0), 0.05f, 0.5f, k == 1 ? -0.3f : 0.3f);
                    break;
                default:   // Arpeggio, CallResponse, Canon
                    if (bass)
                    {
                        if (s8 == 0) Trigger(4, Note(b, chordDeg, -2), 0.22f * vel, 1.6f, 0f);
                        else if (s8 == 4 && P(0.75f)) Trigger(4, Note(b, chordDeg + 4, -2), 0.16f, 1.1f, 0f);
                        else if (s8 == 7 && nextChordDeg != chordDeg && P(0.6f)) Trigger(4, Note(b, nextChordDeg + (nextChordDeg > chordDeg ? -1 : 1), -2), 0.12f, 0.4f, 0f);
                    }
                    if (arp && style == Style.Arpeggio && (b != 2 || s8 % 2 == 0))
                        Trigger(b == 1 ? 1 : 2, Note(b, chordDeg + ArpPat[s8], 1), 0.05f, b == 2 ? 2.5f : 0.9f, (s8 % 2 == 0 ? -0.35f : 0.35f));
                    if (arp && style != Style.Arpeggio && s8 % 4 == 2)   // lighter: a chord tone on the off-beats
                        Trigger(2, Note(b, chordDeg + Triad[R(3)], 1), 0.04f, 1.2f, P(0.5f) ? -0.4f : 0.4f);
                    break;
            }
        }
        int Progressions0(int b) => prog != null && prog.Length > 0 ? prog[0] : 0;

        int SnapToChord(int deg)
        {
            int best = deg, bd = 99;
            for (int o = -1; o <= 2; o++)
                foreach (int t in Triad)
                {
                    int c = chordDeg + t + o * 7;
                    if (Mathf.Abs(c - deg) < bd) { bd = Mathf.Abs(c - deg); best = c; }
                }
            return best;
        }

        void Lead(int b, float f, float amp, float len)
        {
            float pan = (float)(rng.NextDouble() * 0.6 - 0.3);
            switch (b)
            {
                case 1: Trigger(1, f * 0.5f, amp * 0.9f, 2.6f * len, pan); break;
                case 2: Trigger(2, f, amp * 0.8f, 4f * len, pan); break;
                case 3: Trigger(3, f, amp, 0.9f * len, pan); break;
                default: Trigger(0, f, amp * 1.1f, 1.5f * len, pan); break;
            }
        }

        /// Pad voicing with voice leading: each chord tone goes to the octave closest to where the last voicing was.
        void VoicePad(int b)
        {
            for (int i = 0; i < 3; i++)
            {
                int t = chordDeg + Triad[i];
                while (t < lastPadVoicing0 - 3) t += 7;
                while (t > lastPadVoicing0 + 4) t -= 7;
                voicing[i] = t;
            }
            lastPadVoicing0 = voicing[0];
            for (int i = 0; i < 3; i++) padF[i] = Note(b, voicing[i], -1);
        }

        void Trigger(int type, float freq, float amp, float decaySec, float pan)
        {
            int slot = -1; float quiet = 9f;
            for (int i = 0; i < MaxVoices; i++) { if (!v[i].on) { slot = i; break; } if (v[i].env < quiet) { quiet = v[i].env; slot = i; } }
            ref var x = ref v[slot];
            x.on = true; x.type = type; x.freq = freq; x.ph = 0; x.ph2 = 0; x.amp = amp; x.env = 1f; x.atk = 0f; x.pan = pan;
            x.decay = Mathf.Exp(-1f / (decaySec * sr));
            if (type == 0)
            {
                x.ksLen = Mathf.Clamp(Mathf.RoundToInt(sr / freq), 2, KsMax - 1); x.ksPos = 0;
                for (int i = 0; i < x.ksLen; i++) ks[slot][i] = (float)(rng.NextDouble() * 2.0 - 1.0);
                x.damp = 0.996f;
            }
        }

        int pendingAlter;   // applied on the next downbeat: 1 bloom (life) · 2 lift (gene)
        void Bloom(int b)
        {
            // becoming alive changes the music itself: a lift to a brighter key, the full texture, the theme broadened
            sec = Sec.ThemeB; bar = 0; secBars = 8; night = false;
            key = key == 0 ? 5 : 0;
            sectionAlt = true;
            prog = Progs[b][0];
            layers = Mel | Bass | Pad | Arp;
            if (hookSeed != themeSeed) MakeHook();
            for (int i = 0; i < 4; i++) { motifIntB[i] = motifInt[i]; motifRhyB[i] = i * 2; }
            NowPlaying = $"{BiomeName[b]} · BLOOM (alive) · key {key:+0;-0}";
        }

        void Cue_(int b, int cue)
        {
            switch (cue)
            {
                case 1: pendingAlter = 1; break;
                case 3: pendingAlter = 2; break;
                case 2: Trigger(b == 0 ? 0 : 3, Note(b, chordDeg + 7), 0.14f, 1.5f, -0.5f); Trigger(b == 0 ? 0 : 3, Note(b, chordDeg + 11), 0.12f, 1.5f, 0.5f); break;
                case 4: Trigger(4, Note(b, chordDeg, -2), 0.22f, 4f, 0f); sec = Sec.Breakdown; bar = 0; secBars = 2; layers = 0; padTarget = 0f; break;
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            int b = Mathf.Clamp(biome, 0, 3);
            int frames = data.Length / channels;
            float k = 1f - Mathf.Exp(-frames / (sr * 1.5f));
            sDay += (day - sDay) * k; sTide += (tideRate - sTide) * k; sFlow += (flow - sFlow) * k;
            sFood += (food - sFood) * k; sCrowd += (crowd - sCrowd) * k; sDanger += (danger - sDanger) * k;
            int cue = pendingEvent; if (cue != 0) { pendingEvent = 0; Cue_(b, cue); }
            int sn = pendingSnap;
            if (sn != 0)
            {
                pendingSnap = 0;
                Trigger(5, Note(b, chordDeg + 7 + (sn == 2 ? 4 : 0), 1), sn == 2 ? 0.16f : 0.1f, 0.06f, (float)(rng.NextDouble() - 0.5) * 0.4f);
                if (sn == 2) Trigger(2, Note(b, chordDeg + 14), 0.05f, 0.5f, 0f);   // a little ring when it pairs / bonds
            }
            absorbCooldown -= frames;
            if (absorbCooldown <= 0 && (pendingGood > 0 || pendingDull > 0))
            {
                absorbCooldown = sr / 14;                                            // at most ~14 per second
                if (pendingGood > 0) { pendingGood = 0; Trigger(2, Note(b, chordDeg + 7 + 2 * R(3), 1), 0.05f, 0.35f, (float)(rng.NextDouble() - 0.5)); }
                else { pendingDull = 0; Trigger(4, Note(b, chordDeg, -1), 0.07f, 0.09f, 0f); }   // short, low, muted
            }
            double samplesPerStep = sr * 60.0 / (Bpm[b] * (night ? 0.9f : 1f) * (1f + sDanger * 0.1f)) / 2.0;
            float room = RoomMix[b] + (night ? 0.12f : 0f);
            float padCut = Mathf.Clamp01(0.02f + sFlow * 0.03f + sDay * 0.02f + Mathf.Max(0f, sTide) * 0.01f);
            float padLevel = (b == 3 ? 0.06f : 0.075f);
            float washAmp = 0.01f + Mathf.Min(sFlow, 3f) * 0.01f + Mathf.Abs(sTide) * (b == 0 ? 0.018f : 0.004f);
            float twoPi = 2f * Mathf.PI, invSr = 1f / sr;
            float vol = muted ? 0f : master;
            float padRate = 1f / (sr * 1.8f);

            for (int n = 0; n < frames; n++)
            {
                stepCounter += 1.0;
                double thr = swing ? samplesPerStep * (stepInBar % 2 == 0 ? 1.34 : 0.66) : samplesPerStep;
                if (stepCounter >= thr) { stepCounter -= thr; Step(b); }

                // pad: held chord (no glides), swelling in and out with its layer
                padEnv += (padTarget > padEnv ? 1f : -1f) * padRate; padEnv = Mathf.Clamp01(padEnv);
                float pad = 0f;
                if (padEnv > 0.0001f)
                    for (int i = 0; i < 3; i++)
                    {
                        padPh[i * 2] += padF[i] * invSr; if (padPh[i * 2] > 1f) padPh[i * 2] -= 1f;
                        padPh[i * 2 + 1] += padF[i] * 1.003f * invSr; if (padPh[i * 2 + 1] > 1f) padPh[i * 2 + 1] -= 1f;
                        pad += padPh[i * 2] * 2f - 1f + padPh[i * 2 + 1] * 2f - 1f;
                    }
                padLp += (pad - padLp) * padCut; padLp2 += (padLp - padLp2) * padCut;
                float padOut = padLp2 * padLevel * 0.33f * padEnv * padEnv;

                tremPh += 6f * invSr; if (tremPh > 1f) tremPh -= 1f;
                tensPh += tensF * invSr; if (tensPh > 1f) tensPh -= 1f;
                float tension = sDanger > 0.05f ? Mathf.Sin(twoPi * tensPh) * sDanger * 0.025f * (0.5f + 0.5f * Mathf.Sin(twoPi * tremPh)) : 0f;

                float L = 0f, R2 = 0f;
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (!v[i].on) continue;
                    ref var x = ref v[i];
                    float s;
                    if (x.type == 0)
                    {
                        var buf = ks[i]; int p0 = x.ksPos, p1 = p0 + 1 >= x.ksLen ? 0 : p0 + 1;
                        s = buf[p0]; buf[p0] = (buf[p0] + buf[p1]) * 0.5f * x.damp; x.ksPos = p1;
                    }
                    else
                    {
                        x.ph += x.freq * invSr; if (x.ph > 1f) x.ph -= 1f;
                        if (x.type == 1) { x.ph2 += x.freq * 2f * invSr; if (x.ph2 > 1f) x.ph2 -= 1f; s = Mathf.Sin(twoPi * x.ph + 1.4f * x.env * Mathf.Sin(twoPi * x.ph2)); }
                        else if (x.type == 2) { x.ph2 += x.freq * 3f * invSr; if (x.ph2 > 1f) x.ph2 -= 1f; s = Mathf.Sin(twoPi * x.ph) + 0.25f * x.env * x.env * Mathf.Sin(twoPi * x.ph2); }
                        else if (x.type == 3) { x.ph2 += x.freq * 4f * invSr; if (x.ph2 > 1f) x.ph2 -= 1f; s = Mathf.Sin(twoPi * x.ph) + 0.3f * x.env * x.env * x.env * Mathf.Sin(twoPi * x.ph2); }
                        else if (x.type == 5) { s = Mathf.Sin(twoPi * x.ph) * 0.8f + (float)(rng.NextDouble() * 2.0 - 1.0) * x.env * x.env * x.env * 0.6f; }   // snap: a click + tick
                        else { x.ph2 += x.freq * 2f * invSr; if (x.ph2 > 1f) x.ph2 -= 1f; s = Mathf.Sin(twoPi * x.ph) * 0.9f + 0.25f * x.env * Mathf.Sin(twoPi * x.ph2); }   // round bass
                    }
                    x.atk = Mathf.Min(1f, x.atk + invSr / (x.type == 4 ? 0.01f : x.type == 5 ? 0.0005f : 0.004f));
                    x.env *= x.decay;
                    float o = s * x.env * x.atk * x.amp;
                    L += o * (0.5f - x.pan * 0.5f); R2 += o * (0.5f + x.pan * 0.5f);
                    if (x.env < 0.0005f) x.on = false;
                }

                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                washLp1 += (white - washLp1) * 0.08f; washLp2 += (washLp1 - washLp2) * 0.02f;
                float wash = (washLp1 - washLp2) * washAmp;
                brown = Mathf.Clamp(brown + white * 0.02f, -1f, 1f) * 0.999f;
                rumbleLp += (brown - rumbleLp) * 0.01f;
                heartEnv *= 0.99985f;
                float thump = heartEnv * Mathf.Sin(twoPi * thumpF * (float)(stepCounter * invSr)) * 0.1f;
                float low = b == 1 ? rumbleLp * 0.18f + thump : 0f;

                float dry = (L + R2) * 0.5f + padOut;
                float rev = 0f;
                for (int c = 0; c < 4; c++)
                {
                    var cb = comb[c]; int cp = combPos[c];
                    float y = cb[cp];
                    combLp[c] = y * 0.7f + combLp[c] * 0.3f;
                    cb[cp] = dry + combLp[c] * (0.8f + room * 0.15f);
                    combPos[c] = cp + 1 >= cb.Length ? 0 : cp + 1;
                    rev += y;
                }
                rev *= 0.25f;
                for (int a = 0; a < 2; a++)
                {
                    var ab = ap[a]; int pp = apPos[a];
                    float bo = ab[pp]; float y = -rev + bo;
                    ab[pp] = rev + bo * 0.5f;
                    apPos[a] = pp + 1 >= ab.Length ? 0 : pp + 1;
                    rev = y;
                }

                float outL = L + padOut + rev * room + wash + low + tension;
                float outR = R2 + padOut + rev * room * 0.95f + wash * 0.9f + low + tension;
                outL = outL / (1f + Mathf.Abs(outL)) * vol; outR = outR / (1f + Mathf.Abs(outR)) * vol;
                int i0 = n * channels;
                data[i0] = outL;
                if (channels > 1) { data[i0 + 1] = outR; for (int ch = 2; ch < channels; ch++) data[i0 + ch] = 0f; }
            }
        }
    }
}
