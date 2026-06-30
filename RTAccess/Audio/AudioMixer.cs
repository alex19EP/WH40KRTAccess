using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RTAccess.Audio
{
    /// <summary>
    /// A minimal shared stereo mixer (the "classic" NAudio backend, ported lean from WrathAccess's
    /// NAudioEngine): ONE <see cref="MixingSampleProvider"/> feeding ONE <see cref="WaveOutEvent"/>; each
    /// sound is a self-removing <see cref="OneShot"/> voice on that single mixer. This is the foundation
    /// for non-speech audio cues (UI earcons now; sonar / wall tones later). It is created lazily on first
    /// play, so NAudio is never touched unless an audio cue actually fires (the feature is off by default).
    ///
    /// DEFERRED status: the full spatial soundscape (sonar sweep, directional wall tones, positional
    /// speech) from the steal report is NOT ported here — only the mixer + panned one-shot, which the
    /// earcon layer rides. See docs/overnight-port-report.md.
    /// </summary>
    internal sealed class AudioMixer : IDisposable
    {
        public const int Rate = 44100;

        public static AudioMixer Instance { get; } = new AudioMixer();

        private MixingSampleProvider _mixer;
        private IWavePlayer _out;
        private readonly object _gate = new object();

        private void EnsureStarted()
        {
            if (_out != null) return;
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
            // 100 ms buffer rides through managed-thread (GC/CPU) pauses without underrunning.
            _out = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 4 };
            _out.Init(_mixer);
            _out.Play();
        }

        /// <summary>Play a cached interleaved-stereo float buffer once at (volume, pan in -1..1).</summary>
        public void Play(float[] interleavedStereo, float volume, float pan = 0f)
        {
            if (interleavedStereo == null || interleavedStereo.Length == 0) return;
            try
            {
                lock (_gate)
                {
                    EnsureStarted();
                    _mixer.AddMixerInput(new OneShot(interleavedStereo, Rate, volume, pan));
                }
            }
            catch (Exception e) { Main.Log?.Log("[audio] play failed: " + e.Message); }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _out?.Stop(); _out?.Dispose(); } catch { }
                _out = null; _mixer = null;
            }
        }

        // Plays a cached interleaved-stereo buffer once with a constant-power pan; returns < count at the
        // end so the mixer auto-removes it. Ported from WrathAccess SfxPlayer.OneShot.
        private sealed class OneShot : ISampleProvider
        {
            private readonly float[] _buf;
            private readonly float _gainL, _gainR;
            private int _pos;

            public OneShot(float[] buf, int rate, float vol, float pan)
            {
                _buf = buf;
                float t = (pan + 1f) * 0.5f * (float)(Math.PI / 2.0);
                _gainL = vol * (float)Math.Cos(t);
                _gainR = vol * (float)Math.Sin(t);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int remaining = _buf.Length - _pos;
                int n = Math.Min(count, remaining);
                for (int i = 0; i < n; i++)
                    buffer[offset + i] = _buf[_pos + i] * (((_pos + i) & 1) == 0 ? _gainL : _gainR);
                _pos += n;
                return n;
            }
        }
    }
}
