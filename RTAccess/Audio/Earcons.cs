namespace RTAccess.Audio
{
    /// <summary>
    /// Short non-positional UI earcons (chimes) — focus moved, screen changed, action activated, boundary
    /// hit, turn started, error. Trims serial-reader verbosity: a blind player learns the cue faster than a
    /// spoken word. Tones are SYNTHESIZED here (sine partials + an exponential envelope), so no WAV assets
    /// ship; pitch/length/volume are easy to tune. Routed through <see cref="AudioMixer"/>.
    ///
    /// DEFERRED / off by default: <see cref="Enabled"/> is false, so nothing here makes a sound until the
    /// maintainer flips it on (via /eval <c>RTAccess.Audio.Earcons.Enabled = true</c> or a future settings
    /// toggle) and auditions/tunes the set. <see cref="ScreenChange"/> is the only cue wired into the live
    /// framework (one gated call in ScreenManager); the rest are ready for wiring once the palette is liked.
    /// Audition the whole palette from the dev REPL with <see cref="Test"/>.
    /// </summary>
    public static class Earcons
    {
        /// <summary>Master on/off. Off by default — this whole subsystem is silent until enabled + tuned.</summary>
        public static bool Enabled = false;

        /// <summary>Master earcon volume (0..1). Earcons sit under speech, so keep this modest.</summary>
        public static float Volume = 0.30f;

        private static readonly Dictionary<string, float[]> _cache = new Dictionary<string, float[]>();

        // ---- the palette (each gated on Enabled) ----

        /// <summary>Focus moved to a new element — a soft mid blip.</summary>
        public static void Focus() => Play("focus", 660, 70, 0.7f);

        /// <summary>A new screen/window became active — a quick rising two-note.</summary>
        public static void ScreenChange() => PlaySeq("screen", new[] { (523, 60), (784, 90) }, 0.8f);

        /// <summary>An action was activated (button press confirm) — a short bright click.</summary>
        public static void Activate() => Play("activate", 880, 55, 0.8f);

        /// <summary>Navigation hit a boundary (top/bottom of a list, edge of the grid) — a muted low thud.</summary>
        public static void Boundary() => Play("boundary", 220, 90, 0.7f);

        /// <summary>A unit's turn began (turn-based combat) — a clear ascending pair.</summary>
        public static void TurnStart() => PlaySeq("turn", new[] { (587, 70), (988, 110) }, 0.9f);

        /// <summary>An error / refusal (not enough AP, out of range) — a low descending pair.</summary>
        public static void Error() => PlaySeq("error", new[] { (392, 80), (294, 120) }, 0.8f);

        /// <summary>The exploration cursor crossed OUT of the party's sight, into fog — a higher, short decaying tone.</summary>
        public static void FogEnter() => PlayUngated("fog_enter", 2080, 349, 0.8f);

        /// <summary>The exploration cursor crossed back INTO the party's sight — a lower, longer decaying tone.</summary>
        public static void FogExit() => PlayUngated("fog_exit", 1590, 458, 0.8f);
        // FogEnter/FogExit pitch+length are matched to WrathAccess's fog_enter/fog_exit wavs (measured: exponentially-
        // decaying tones ≈2080 Hz/0.35 s and ≈1590 Hz/0.46 s), so RT sounds the same while shipping no WAV. They are
        // driven by Exploration/FogCue on its own default-on toggle, so — unlike the rest of this deferred palette —
        // they bypass the master Enabled gate (see PlayUngated).

        /// <summary>Formation editor: the glide cursor crossed ONTO a party member — a short bright blip.
        /// Feature-owned (the editor is explicitly opened), so it bypasses the master gate like the fog cues.</summary>
        public static void FormationEnter() => PlayUngated("form_enter", 1320, 60, 0.8f);

        /// <summary>Formation editor: the glide cursor left a party member — the lower twin blip.</summary>
        public static void FormationExit() => PlayUngated("form_exit", 990, 60, 0.8f);

        /// <summary>Formation editor: the review cue at a member's layout offset — panned by lateral
        /// position, attenuated by distance (both computed by the caller). Feature-owned, bypasses the gate.</summary>
        public static void FormationReview(float pan, float gain)
            => AudioMixer.Instance?.Play(Get("form_review", () => Chime(784, 90)), Volume * gain,
                Math.Max(-1f, Math.Min(1f, pan)));

        /// <summary>Audition the whole palette in sequence (call from /eval to tune). Ignores
        /// <see cref="Enabled"/> so it can be heard even with the feature off.</summary>
        public static void Test()
        {
            bool was = Enabled;
            Enabled = true;
            try { Focus(); ScreenChange(); Activate(); Boundary(); TurnStart(); Error(); }
            finally { Enabled = was; }
        }

        // ---- synthesis ----

        private static void Play(string key, float freq, int ms, float gain)
        {
            if (!Enabled) return;
            AudioMixer.Instance.Play(Get(key, () => Chime(freq, ms)), Volume * gain);
        }

        // As Play, but bypasses the master Enabled gate — for cues owned by a feature that carries its own on/off
        // (FogCue's exploration.fog_cue), so they can ship live while the general palette stays deferred.
        private static void PlayUngated(string key, float freq, int ms, float gain)
            => AudioMixer.Instance?.Play(Get(key, () => Chime(freq, ms)), Volume * gain);

        private static void PlaySeq(string key, (int freq, int ms)[] notes, float gain)
        {
            if (!Enabled) return;
            AudioMixer.Instance.Play(Get(key, () => Sequence(notes)), Volume * gain);
        }

        private static float[] Get(string key, Func<float[]> make)
        {
            if (_cache.TryGetValue(key, out var c)) return c;
            var buf = make();
            _cache[key] = buf;
            return buf;
        }

        // One note: a sine fundamental + a quiet octave, with a fast attack and exponential decay so it
        // doesn't click. Returns interleaved stereo (centred) at the mixer rate.
        private static float[] Chime(float freq, int ms)
        {
            int rate = AudioMixer.Rate;
            int frames = Math.Max(1, rate * ms / 1000);
            var buf = new float[frames * 2];
            double twoPi = 2.0 * Math.PI;
            float attack = rate * 0.005f; // 5 ms attack
            for (int i = 0; i < frames; i++)
            {
                double t = (double)i / rate;
                float env = (float)Math.Exp(-3.5 * i / frames);          // exponential decay
                if (i < attack) env *= i / attack;                       // soft attack (no click)
                float s = (float)(Math.Sin(twoPi * freq * t) + 0.25 * Math.Sin(twoPi * freq * 2 * t)) * env * 0.8f;
                buf[i * 2] = s;
                buf[i * 2 + 1] = s;
            }
            return buf;
        }

        // Concatenate several chimes into one buffer (a little run-on overlap is fine; we just append).
        private static float[] Sequence((int freq, int ms)[] notes)
        {
            var parts = new List<float[]>();
            int total = 0;
            foreach (var (freq, ms) in notes) { var c = Chime(freq, ms); parts.Add(c); total += c.Length; }
            var buf = new float[total];
            int o = 0;
            foreach (var p in parts) { Array.Copy(p, 0, buf, o, p.Length); o += p.Length; }
            return buf;
        }
    }
}
