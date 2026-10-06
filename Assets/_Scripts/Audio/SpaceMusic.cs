using UnityEngine;

namespace CLAY.Audio
{
    /// <summary>
    /// PROCEDURAL SPACE MUSIC — grand, dramatic, orchestral (a synthesized orchestra; no audio files).
    ///
    /// The orchestra: STRINGS (detuned saw ensembles, slow bows, vibrato) · BRASS (bright, filter-swelling horns and
    /// trumpets) · CHOIR (additive "aah" voices shaped by vowel formants) · TIMPANI (pitched drums with a falling
    /// pitch and a thump; rolls) · HARP (plucked arpeggios) · CONTRABASS · CYMBAL swells and crashes · a sub BOOM.
    /// All in a large hall reverb.
    ///
    /// The score: minor-key epic harmony (i–VI–III–VII, the Andalusian i–♭VII–♭VI–V, i–iv–VI–V) with Lydian "wonder"
    /// passages; a heroic THEME seeded per universe (rising fifths and octave leaps, a long held peak, a falling
    /// answer) that returns — whispered on a horn, then blazing in the brass. The form breathes:
    ///   DAWN (strings + choir from silence) → RISE (string ostinato, timpani roll, cymbal swell) → ANTHEM (brass theme
    ///   fortissimo, full choir, timpani) → REFLECTION (solo horn and harp over quiet strings) → STILLNESS → …
    ///
    /// Plays wherever you're in space (it fades out in the cell stage and on a planet's surface). F11 mutes.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class SpaceMusic : MonoBehaviour
    {
        public static SpaceMusic Instance;
        public int seed = 1977;
        public float master = 0.5f;
        public bool muted;
        public string NowPlaying = "";
        float targetVol = 1f, vol, checkT;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("SpaceMusic");
            DontDestroyOnLoad(go);
            go.AddComponent<AudioSource>();
            go.AddComponent<SpaceMusic>();
        }

        // ── theory ──
        static readonly int[] Aeolian = { 0, 2, 3, 5, 7, 8, 10 }, Harmonic = { 0, 2, 3, 5, 7, 8, 11 }, Dorian = { 0, 2, 3, 5, 7, 9, 10 }, Lydian = { 0, 2, 4, 6, 7, 9, 11 };
        static readonly int[][] EpicProgs =
        {
            new[] { 0, 5, 2, 6 },   // i – VI – III – VII
            new[] { 0, 6, 5, 4 },   // i – ♭VII – ♭VI – V   (Andalusian, harmonic minor V)
            new[] { 0, 3, 5, 4 },   // i – iv – VI – V
            new[] { 5, 6, 0, 0 },   // VI – VII – i
            new[] { 0, 5, 3, 4 },
        };
        static readonly int[][] WonderProgs = { new[] { 0, 1, 0, 1 }, new[] { 0, 4, 5, 1 }, new[] { 3, 4, 0, 0 } };

        static readonly int[] Ost = { 0, 4, 7, 4, 0, 4, 7, 9 }, Arp = { 0, 2, 4, 7, 9, 7, 4, 2 }, Triad = { 0, 2, 4 };
        enum Sec { Stillness, Dawn, Rise, Anthem, Reflection }
        Sec sec = Sec.Stillness; int bar, secBars = 2, step; int[] prog = EpicProgs[0]; int[] scale = Aeolian; bool wonder;
        int tonic = 50, chord, nextChord;
        float dyn, dynTarget = 0.3f;
        double clock; int sr;
        System.Random rng;

        // theme: 8 notes over 4 bars (durations in eighths, degrees)
        readonly int[] thDeg = new int[8], thDur = new int[8]; int thIdx, thLeft; bool themeOn; float themeVel; int themeVoice;

        // ── synth ──
        sealed class Sus { public bool on; public float f, tf, amp, env, atk, rel, ph1, ph2, ph3, lp, lp2, vib; public bool releasing; public int kind; public float[] form; }   // strings / choir / bass
        readonly Sus[] sus = new Sus[16];
        struct Hit { public bool on; public int type; public float f, ph, ph2, env, decay, amp, pan, bright, lp, drop, hold; public int ksLen, ksPos; }   // brass / timpani / harp / boom
        readonly Hit[] hits = new Hit[24];
        readonly float[][] ks = new float[24][];
        float cymEnv, cymTarget, cymLp, cymLp2, crash;
        readonly float[][] comb = new float[6][]; readonly int[] combPos = new int[6]; readonly float[] combLp = new float[6];
        readonly float[][] ap = new float[3][]; readonly int[] apPos = new int[3];

        void Awake()
        {
            Instance = this;
            sr = AudioSettings.outputSampleRate;
            rng = new System.Random(seed);
            for (int i = 0; i < sus.Length; i++) sus[i] = new Sus { form = new float[16] };
            for (int i = 0; i < ks.Length; i++) ks[i] = new float[4096];
            int[] cl = { 1557, 1617, 1491, 1422, 1277, 1356 }; int[] al = { 225, 556, 441 };
            float sc = sr / 44100f;
            for (int i = 0; i < 6; i++) comb[i] = new float[Mathf.RoundToInt(cl[i] * sc * 2.2f)];
            for (int i = 0; i < 3; i++) ap[i] = new float[Mathf.RoundToInt(al[i] * sc)];
            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("SpaceMusicCarrier", sr, 1, sr, false);
            src.loop = true; src.spatialBlend = 0f; src.playOnAwake = false; src.volume = 1f;
            src.Play();
            MakeTheme();
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F11)) muted = !muted;
            checkT -= Time.unscaledDeltaTime;
            if (checkT > 0f) return;
            checkT = 0.5f;
            // only in space: not in the cell stage, not on a planet's surface
            bool cell = CLAY.CellStage.CellStageWorld.Current != null && CLAY.CellStage.CellStageWorld.Current.isActiveAndEnabled;
            bool surface = Object.FindFirstObjectByType<CLAY.Surface.SurfaceWorld>() != null;
            targetVol = (cell || surface || muted) ? 0f : 1f;
            if (targetVol > 0f && Object.FindFirstObjectByType<AudioListener>() == null && Camera.main != null) Camera.main.gameObject.AddComponent<AudioListener>();
        }

        static float Mtof(float m) => 440f * Mathf.Pow(2f, (m - 69f) / 12f);
        float Note(int deg, int oct = 0)
        {
            int o = Mathf.FloorToInt(deg / 7f), idx = deg - o * 7;
            return Mtof(tonic + (o + oct) * 12 + scale[idx]);
        }
        bool P(float p) => rng.NextDouble() < p;

        void MakeTheme()
        {
            var r = new System.Random(seed * 31 + 7);
            // heroic contour templates (degrees from the tonic): rise by a fifth, climb to the octave, hold, fall home
            int[][] contours =
            {
                new[] { 0, 4, 5, 7, 6, 4, 3, 4 },
                new[] { 0, 4, 7, 6, 4, 5, 2, 0 },
                new[] { 4, 7, 9, 7, 5, 4, 2, 4 },
                new[] { 0, 2, 4, 7, 9, 7, 4, 5 },
            };
            int[][] rhythms =
            {
                new[] { 4, 2, 2, 8, 4, 2, 2, 8 },
                new[] { 6, 2, 4, 4, 6, 2, 4, 4 },
                new[] { 2, 2, 4, 8, 2, 2, 4, 8 },
            };
            var c = contours[r.Next(contours.Length)]; var d = rhythms[r.Next(rhythms.Length)];
            for (int i = 0; i < 8; i++) { thDeg[i] = c[i]; thDur[i] = d[i]; }
            tonic = 45 + r.Next(8);   // A2 … E3
        }

        // ── form ──
        void NextSection()
        {
            sec = sec switch
            {
                Sec.Stillness => Sec.Dawn,
                Sec.Dawn => Sec.Rise,
                Sec.Rise => Sec.Anthem,
                Sec.Anthem => P(0.5f) ? Sec.Reflection : Sec.Rise,
                _ => P(0.6f) ? Sec.Stillness : Sec.Dawn,
            };
            bar = 0;
            secBars = sec == Sec.Stillness ? 2 + rng.Next(3) : 8;
            wonder = sec == Sec.Dawn ? P(0.4f) : sec == Sec.Reflection && P(0.35f);
            scale = wonder ? Lydian : sec == Sec.Anthem ? (P(0.5f) ? Harmonic : Aeolian) : (P(0.3f) ? Dorian : Aeolian);
            prog = wonder ? WonderProgs[rng.Next(WonderProgs.Length)] : EpicProgs[rng.Next(EpicProgs.Length)];
            dynTarget = sec switch { Sec.Stillness => 0.12f, Sec.Dawn => 0.35f, Sec.Rise => 0.6f, Sec.Anthem => 1f, _ => 0.4f };
            themeOn = sec == Sec.Anthem || sec == Sec.Reflection;
            themeVoice = sec == Sec.Anthem ? 0 : 1;   // 0 full brass · 1 solo horn
            themeVel = sec == Sec.Anthem ? 0.22f : 0.13f;
            thIdx = 0; thLeft = 0;
            if (sec == Sec.Anthem) { crash = 1f; Boom(); }
            if (sec == Sec.Rise) cymTarget = 0f;
            NowPlaying = $"space · {sec}{(wonder ? " (wonder)" : "")}";
        }

        int Slow => sec == Sec.Dawn || sec == Sec.Stillness ? 2 : 1;   // chords change every 2 bars when it's slow

        void Step()
        {
            int s8 = step % 8;
            if (s8 == 0)
            {
                if (bar >= secBars) NextSection();
                chord = prog[(bar / Slow) % prog.Length];
                nextChord = prog[((bar + 1) / Slow) % prog.Length];
                if (bar % Slow == 0) Voice();
                // a cymbal swell into the next big moment
                if (sec == Sec.Rise && bar == secBars - 2) cymTarget = 1f;
                if (sec == Sec.Rise && bar == 0) cymTarget = 0f;
            }
            // RISE: strings drive an eighth-note ostinato; timpani roll builds in the last bar
            if (sec == Sec.Rise)
            {
                Pluck(2, Note(chord + Ost[s8], -1), 0.07f + 0.05f * (bar / (float)secBars), 0.28f, (s8 % 2 == 0) ? -0.4f : 0.4f, 0.55f);
                if (bar == secBars - 1) Timp(Note(0, -2), 0.08f + 0.12f * (s8 / 8f));
                if (s8 == 0 && bar % 2 == 0) Timp(Note(0, -2), 0.12f);
            }
            // ANTHEM: timpani on 1 and 3 (tonic / fifth)
            if (sec == Sec.Anthem && (s8 == 0 || s8 == 4)) Timp(Note(s8 == 0 ? 0 : 4, -2), 0.2f);
            // REFLECTION / DAWN: harp arpeggios
            if ((sec == Sec.Reflection || (sec == Sec.Dawn && bar >= 4)) && (sec == Sec.Reflection || s8 % 2 == 0))
            {
                Pluck(1, Note(chord + Arp[s8], 1), 0.06f, 1.6f, (s8 - 3.5f) / 5f, 0.4f);
            }
            // the THEME
            if (themeOn)
            {
                if (thLeft <= 0)
                {
                    int deg = thDeg[thIdx];
                    // strong beats sit on the chord: nudge the theme note to the nearest chord tone
                    if (s8 == 0) deg = NearestChordTone(deg);
                    int len = thDur[thIdx] * (themeVoice == 1 ? 2 : 1);
                    float secs = len * (float)(60.0 / Bpm / 2.0);
                    Brass(Note(deg, 1), themeVel * (0.85f + 0.15f * dyn), secs, themeVoice);
                    if (themeVoice == 0) Brass(Note(deg, 0), themeVel * 0.7f, secs, 2);   // doubled an octave down (horns)
                    thLeft = len; thIdx = (thIdx + 1) % 8;
                }
                thLeft--;
            }
            step++;
            if (step % 8 == 0) bar++;
        }

        int NearestChordTone(int deg)
        {
            int best = deg, bd = 99;
            for (int o = -1; o <= 2; o++)
                foreach (int t in Triad)
                {
                    int c = chord + t + o * 7;
                    if (Mathf.Abs(c - deg) < bd) { bd = Mathf.Abs(c - deg); best = c; }
                }
            return best;
        }

        float Bpm => sec == Sec.Anthem ? 84f : sec == Sec.Rise ? 88f : 66f;

        /// Strings, choir and bass take the new chord (voice-led: each part moves to the nearest chord tone).
        void Voice()
        {
            bool strings = sec != Sec.Stillness, choir = sec == Sec.Dawn || sec == Sec.Anthem || (sec == Sec.Reflection && wonder);
            int[] tones = { chord, chord + 2, chord + 4, chord + 7 };
            for (int v = 0; v < 4; v++) SetSus(v, 0, strings ? Note(tones[v], 0) : 0f, 0.05f, 1.8f, 1.2f);          // strings
            for (int v = 0; v < 4; v++) SetSus(4 + v, 1, choir ? Note(tones[v], 1) : 0f, 0.035f, 2.5f, 2f);       // choir
            SetSus(8, 2, Note(chord, -2), sec == Sec.Stillness ? 0.07f : 0.1f, 1.2f, 1.5f);                       // contrabass (always: the floor of space)
            SetSus(9, 2, sec == Sec.Anthem ? Note(chord, -1) : 0f, 0.06f, 0.8f, 1.2f);
        }

        void SetSus(int i, int kind, float f, float amp, float atk, float rel)
        {
            var s = sus[i];
            if (f <= 0f) { s.releasing = true; s.rel = rel; return; }
            if (!s.on || s.releasing) { s.f = f; s.env = s.on ? s.env : 0f; }
            s.on = true; s.releasing = false; s.kind = kind; s.tf = f; s.amp = amp; s.atk = atk; s.rel = rel;
            if (kind == 1)
            {
                // choir: harmonic weights from vowel formants ("aah": ~730 Hz and ~1090 Hz, a little ~2440 Hz)
                for (int h = 1; h <= 16; h++)
                {
                    float hf = f * h;
                    s.form[h - 1] = (Formant(hf, 730f, 90f) + 0.6f * Formant(hf, 1090f, 110f) + 0.25f * Formant(hf, 2440f, 160f)) / Mathf.Sqrt(h);
                }
            }
        }
        static float Formant(float f, float c, float bw) => 1f / (1f + Mathf.Pow((f - c) / bw, 2f));

        int FreeHit() { int slot = 0; float q = 9f; for (int i = 0; i < hits.Length; i++) { if (!hits[i].on) return i; if (hits[i].env < q) { q = hits[i].env; slot = i; } } return slot; }
        void Brass(float f, float amp, float secs, int voice)
        {
            int i = FreeHit(); ref var h = ref hits[i];
            h = new Hit { on = true, type = 0, f = f, amp = amp, env = 0f, hold = secs, decay = 0f, pan = voice == 2 ? -0.25f : 0.2f, bright = voice == 1 ? 0.35f : 0.8f };
        }
        void Timp(float f, float amp)
        {
            int i = FreeHit(); ref var h = ref hits[i];
            h = new Hit { on = true, type = 1, f = f * 1.15f, drop = f, amp = amp, env = 1f, decay = Mathf.Exp(-1f / (1.3f * sr)), pan = 0.1f };
        }
        void Pluck(int kind, float f, float amp, float decaySec, float pan, float bright)
        {
            int i = FreeHit(); ref var h = ref hits[i];
            h = new Hit { on = true, type = 2, f = f, amp = amp, env = 1f, decay = Mathf.Exp(-1f / (decaySec * sr)), pan = pan, bright = bright };
            h.ksLen = Mathf.Clamp(Mathf.RoundToInt(sr / f), 2, 4095); h.ksPos = 0;
            for (int k = 0; k < h.ksLen; k++) ks[i][k] = (float)(rng.NextDouble() * 2 - 1);
        }
        void Boom()
        {
            int i = FreeHit(); ref var h = ref hits[i];
            h = new Hit { on = true, type = 3, f = 55f, drop = 32f, amp = 0.35f, env = 1f, decay = Mathf.Exp(-1f / (2.5f * sr)) };
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            float invSr = 1f / sr, twoPi = 2f * Mathf.PI;
            double spStep = sr * 60.0 / Bpm / 2.0;
            for (int n = 0; n < frames; n++)
            {
                clock += 1.0;
                if (clock >= spStep) { clock -= spStep; Step(); spStep = sr * 60.0 / Bpm / 2.0; }
                vol += ((muted ? 0f : targetVol) - vol) * 0.00003f;
                dyn += (dynTarget - dyn) * 0.000008f;

                float L = 0f, R = 0f;
                // sustained sections
                for (int i = 0; i < sus.Length; i++)
                {
                    var s = sus[i]; if (!s.on) continue;
                    s.f += (s.tf - s.f) * 0.0004f;
                    if (s.releasing) { s.env -= invSr / s.rel; if (s.env <= 0f) { s.on = false; s.env = 0f; continue; } }
                    else s.env = Mathf.Min(1f, s.env + invSr / s.atk);
                    s.vib += 5.2f * invSr; if (s.vib > 1f) s.vib -= 1f;
                    float f = s.f * (1f + 0.003f * Mathf.Sin(twoPi * s.vib));
                    float o;
                    if (s.kind == 1)
                    {
                        // choir: additive, formant-weighted harmonics
                        s.ph1 += f * invSr; if (s.ph1 > 1f) s.ph1 -= 1f;
                        o = 0f;
                        for (int h = 1; h <= 16; h++) o += s.form[h - 1] * Mathf.Sin(twoPi * s.ph1 * h);
                        o *= 0.5f;
                    }
                    else
                    {
                        // strings / bass: three detuned saws, low-passed (brighter with the dynamics)
                        s.ph1 += f * invSr; if (s.ph1 > 1f) s.ph1 -= 1f;
                        s.ph2 += f * 1.0035f * invSr; if (s.ph2 > 1f) s.ph2 -= 1f;
                        s.ph3 += f * 0.9968f * invSr; if (s.ph3 > 1f) s.ph3 -= 1f;
                        float saw = (s.ph1 + s.ph2 + s.ph3) * (2f / 3f) - 1f;
                        float cut = s.kind == 2 ? 0.02f : 0.025f + dyn * 0.06f;
                        s.lp += (saw - s.lp) * cut; s.lp2 += (s.lp - s.lp2) * cut;
                        o = s.lp2 * 1.4f;
                    }
                    float a = o * s.env * s.env * s.amp * (0.4f + 0.6f * dyn);
                    float pan = (i % 4 - 1.5f) * 0.2f;
                    L += a * (0.5f - pan * 0.5f); R += a * (0.5f + pan * 0.5f);
                }
                // struck / blown / plucked
                for (int i = 0; i < hits.Length; i++)
                {
                    ref var h = ref hits[i]; if (!h.on) continue;
                    float o;
                    switch (h.type)
                    {
                        case 0:   // brass: bright saw/square, the filter opens as the note swells, held then released
                        {
                            if (h.hold > 0f) { h.hold -= invSr; h.env = Mathf.Min(1f, h.env + invSr / 0.07f); }
                            else { h.env -= invSr / 0.35f; if (h.env <= 0f) { h.on = false; continue; } }
                            h.ph += h.f * invSr; if (h.ph > 1f) h.ph -= 1f;
                            float sw = h.ph * 2f - 1f, sq = h.ph < 0.5f ? 1f : -1f;
                            float raw = sw * 0.7f + sq * 0.3f;
                            float cut = 0.03f + h.bright * (0.05f + 0.15f * h.env) * (0.5f + 0.5f * dyn);
                            h.lp += (raw - h.lp) * cut; h.ph2 += (h.lp - h.ph2) * cut;
                            o = h.ph2 * h.env * h.amp * 1.6f;
                            break;
                        }
                        case 1:   // timpani: falling pitch, thump
                        {
                            h.f += (h.drop - h.f) * 0.0006f;
                            h.ph += h.f * invSr; if (h.ph > 1f) h.ph -= 1f;
                            h.env *= h.decay;
                            float thump = (float)(rng.NextDouble() * 2 - 1) * h.env * h.env * h.env * h.env * 0.5f;
                            o = (Mathf.Sin(twoPi * h.ph) + 0.4f * Mathf.Sin(twoPi * h.ph * 1.5f) * h.env + thump) * h.env * h.amp;
                            if (h.env < 0.0005f) h.on = false;
                            break;
                        }
                        case 2:   // plucked (Karplus–Strong): harp / pizzicato
                        {
                            var buf = ks[i]; int p0 = h.ksPos, p1 = p0 + 1 >= h.ksLen ? 0 : p0 + 1;
                            float s0 = buf[p0];
                            buf[p0] = (buf[p0] * h.bright + buf[p1] * (1f - h.bright) + buf[p1]) * 0.5f * 0.996f;
                            h.ksPos = p1; h.env *= h.decay;
                            o = s0 * h.env * h.amp;
                            if (h.env < 0.0005f) h.on = false;
                            break;
                        }
                        default:  // boom: a sub drop under the big moments
                        {
                            h.f += (h.drop - h.f) * 0.0002f;
                            h.ph += h.f * invSr; if (h.ph > 1f) h.ph -= 1f;
                            h.env *= h.decay;
                            o = Mathf.Sin(twoPi * h.ph) * h.env * h.amp;
                            if (h.env < 0.0005f) h.on = false;
                            break;
                        }
                    }
                    L += o * (0.5f - h.pan * 0.5f); R += o * (0.5f + h.pan * 0.5f);
                }
                // cymbal: a swelling wash, then a crash
                cymEnv += (cymTarget * 0.6f - cymEnv) * 0.00002f;
                crash *= 0.99996f;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                cymLp += (noise - cymLp) * 0.6f; cymLp2 += (cymLp - cymLp2) * 0.05f;
                float cym = (cymLp - cymLp2) * (cymEnv * 0.05f + crash * 0.12f);
                L += cym; R += cym * 0.9f;

                // a big hall
                float dry = (L + R) * 0.5f, rev = 0f;
                for (int c = 0; c < 6; c++)
                {
                    var cb = comb[c]; int cp = combPos[c];
                    float y = cb[cp];
                    combLp[c] = y * 0.6f + combLp[c] * 0.4f;
                    cb[cp] = dry + combLp[c] * 0.86f;
                    combPos[c] = cp + 1 >= cb.Length ? 0 : cp + 1;
                    rev += y;
                }
                rev /= 6f;
                for (int a2 = 0; a2 < 3; a2++)
                {
                    var ab = ap[a2]; int pp = apPos[a2];
                    float bo = ab[pp]; float y = -rev + bo;
                    ab[pp] = rev + bo * 0.5f;
                    apPos[a2] = pp + 1 >= ab.Length ? 0 : pp + 1;
                    rev = y;
                }
                float outL = (L * 0.7f + rev * 0.55f), outR = (R * 0.7f + rev * 0.5f);
                outL = outL / (1f + Mathf.Abs(outL)) * master * vol;
                outR = outR / (1f + Mathf.Abs(outR)) * master * vol;
                int i0 = n * channels;
                data[i0] = outL;
                if (channels > 1) { data[i0 + 1] = outR; for (int ch = 2; ch < channels; ch++) data[i0 + ch] = 0f; }
            }
        }
    }
}
