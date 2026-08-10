#if DEBUG
using UnityEngine;

namespace RTAccess.Audio
{
    /// <summary>
    /// Dev-only ear-test bench for the Phase F audio primitives (docs/plans/echoing-charting-lovelace.md).
    /// Audio quality is un-self-verifiable by the harness, so these exist to be driven from the dev REPL
    /// (<c>/eval RTAccess.Audio.AudioProbe.Sweep()</c>) and judged by the maintainer's ear. Everything here
    /// synthesizes its own tones (no WAV assets needed) and bypasses every gate, so it's audible even with the
    /// soundscape off. Compiled out of Release entirely.
    ///
    /// Recipe:
    ///   AudioProbe.Left()  / .Right() / .Centre()  — a beep hard-left / hard-right / centred (verify panning).
    ///   AudioProbe.Sweep()                         — one tone that travels left → right (verify pan direction).
    ///   AudioProbe.LoopStart(); .LoopPan(-1); .LoopPan(1); .LoopStop();  — a sustained voice, re-panned live.
    ///   AudioProbe.Bearing(dxEast, dzNorth)        — the Spatializer pan for a source at that offset (metres).
    /// </summary>
    public static class AudioProbe
    {
        private static object _loop;

        /// <summary>Play a ~250 ms tone at (pan in -1..1). -1 = hard west (left), +1 = hard east (right).</summary>
        public static void Beep(float pan = 0f)
            => AudioMixer.Instance.Play(Tone(660f, 250), 0.5f, Mathf.Clamp(pan, -1f, 1f));

        public static void Left() => Beep(-1f);
        public static void Right() => Beep(1f);
        public static void Centre() => Beep(0f);

        /// <summary>One tone whose pan travels from hard-left to hard-right over its duration — the
        /// unmistakable "does the stereo image move the right way" test.</summary>
        public static void Sweep(int ms = 1400, float freq = 440f)
            => AudioMixer.Instance.Play(PanSweep(freq, ms), 0.5f, 0f);

        /// <summary>Start a sustained looping voice (default 220 Hz), centred. Re-pan with <see cref="LoopPan"/>,
        /// stop with <see cref="LoopStop"/>.</summary>
        public static void LoopStart(float freq = 220f)
        {
            LoopStop();
            _loop = AudioMixer.Instance.PlayLoop(SineLoop(freq, 500), 0.35f, 0f);
            Main.Log?.Log("[audioprobe] loop started (" + freq + " Hz)");
        }

        /// <summary>Re-pan the running loop voice (-1 = left, +1 = right) — should glide, click-free.</summary>
        public static void LoopPan(float pan)
        {
            if (_loop == null) { Main.Log?.Log("[audioprobe] no loop running"); return; }
            AudioMixer.Instance.SetVoice(_loop, 0.35f, Mathf.Clamp(pan, -1f, 1f));
        }

        public static void LoopStop()
        {
            if (_loop == null) return;
            AudioMixer.Instance.Remove(_loop);
            _loop = null;
            Main.Log?.Log("[audioprobe] loop stopped");
        }

        /// <summary>Compute + audition the <see cref="Spatializer"/> pan for a source at (east, north) metres
        /// from the listener, then play a beep there. Logs the pan value for a numeric check under the harness.</summary>
        public static void Bearing(float dxEast, float dzNorth)
        {
            float pan = Spatializer.Pan(dxEast, dzNorth);
            Main.Log?.Log("[audioprobe] pan(" + dxEast + " E, " + dzNorth + " N) = " + pan);
            Beep(pan);
        }

        // ---- synthesis (no assets) ----

        // A steady sine with short fade in/out (no click), interleaved stereo, centred (equal L/R).
        private static float[] Tone(float freq, int ms)
        {
            int frames = Mathf.Max(1, AudioMixer.Rate * ms / 1000);
            var buf = new float[frames * 2];
            float fade = AudioMixer.Rate * 0.008f; // 8 ms
            double twoPi = 2.0 * Math.PI;
            for (int i = 0; i < frames; i++)
            {
                float env = 1f;
                if (i < fade) env = i / fade;
                else if (i > frames - fade) env = (frames - i) / fade;
                float s = (float)Math.Sin(twoPi * freq * i / AudioMixer.Rate) * env * 0.8f;
                buf[i * 2] = s; buf[i * 2 + 1] = s;
            }
            return buf;
        }

        // A whole number of periods so the buffer loops seamlessly (end phase wraps back to the start).
        private static float[] SineLoop(float freq, int ms)
        {
            int want = Mathf.Max(1, AudioMixer.Rate * ms / 1000);
            int periods = Mathf.Max(1, Mathf.RoundToInt(want * freq / AudioMixer.Rate));
            int frames = Mathf.Max(1, Mathf.RoundToInt(periods * AudioMixer.Rate / freq));
            var buf = new float[frames * 2];
            double twoPi = 2.0 * Math.PI;
            for (int i = 0; i < frames; i++)
            {
                float s = (float)Math.Sin(twoPi * freq * i / AudioMixer.Rate) * 0.8f;
                buf[i * 2] = s; buf[i * 2 + 1] = s;
            }
            return buf;
        }

        // A tone with a constant-power pan baked per-sample sweeping -1 (L) → +1 (R). Played at OneShot pan 0
        // (a harmless uniform -3 dB), so the baked L/R ratio is what's heard.
        private static float[] PanSweep(float freq, int ms)
        {
            int frames = Mathf.Max(1, AudioMixer.Rate * ms / 1000);
            var buf = new float[frames * 2];
            float fade = AudioMixer.Rate * 0.008f;
            double twoPi = 2.0 * Math.PI;
            for (int i = 0; i < frames; i++)
            {
                float env = 1f;
                if (i < fade) env = i / fade;
                else if (i > frames - fade) env = (frames - i) / fade;
                float s = (float)Math.Sin(twoPi * freq * i / AudioMixer.Rate) * env * 0.8f;
                float pan = -1f + 2f * i / frames;
                float t = (pan + 1f) * 0.5f * (float)(Math.PI / 2.0);
                buf[i * 2] = s * (float)Math.Cos(t);
                buf[i * 2 + 1] = s * (float)Math.Sin(t);
            }
            return buf;
        }
    }
}
#endif
