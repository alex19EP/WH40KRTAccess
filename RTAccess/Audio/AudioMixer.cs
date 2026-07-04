using System.Collections.Generic;
using System.IO;
using NAudio.Dsp;   // BiQuadFilter (front/back low-pass)
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;  // Mathf (spatial voice clamps)

namespace RTAccess.Audio
{
    /// <summary>
    /// A minimal shared stereo mixer (the "classic" NAudio backend, ported lean from WrathAccess's
    /// NAudioEngine): ONE <see cref="MixingSampleProvider"/> feeding ONE <see cref="WaveOutEvent"/>; every
    /// sound is a voice on that single mixer. It is created lazily on first play, so NAudio is never touched
    /// unless an audio cue actually fires (the whole feature is off by default).
    ///
    /// Two voice kinds ride it: fire-and-forget <see cref="OneShot"/>s (UI earcons; positional sonar/object
    /// cues via <see cref="Spatializer"/>) that self-remove at their end, and resident <see cref="LoopVoice"/>s
    /// (a held sonar tone, later the directional wall tones) that loop until <see cref="Remove(object)"/>d and
    /// can be re-panned / re-gained live. Buffers are decoded once and cached (<see cref="LoadFile"/>), so a
    /// stem plays with no per-fire I/O. This is the Phase F audio-engine capability fill for the map-viewer
    /// soundscape (docs/plans/echoing-charting-lovelace.md).
    /// </summary>
    internal sealed class AudioMixer : IDisposable
    {
        public const int Rate = 44100;

        public static AudioMixer Instance { get; } = new AudioMixer();

        private MixingSampleProvider _mixer;
        private IWavePlayer _out;
        private readonly object _gate = new object();
        // Decoded WAVs, keyed by path (a null entry means "tried, failed" — don't retry a missing file each frame).
        private readonly Dictionary<string, float[]> _fileCache = new Dictionary<string, float[]>();

        private void EnsureStarted()
        {
            if (_out != null) return;
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
            // 100 ms buffer rides through managed-thread (GC/CPU) pauses without underrunning.
            _out = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 4 };
            _out.Init(_mixer);
            _out.Play();
        }

        // ---- one-shots ----

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

        /// <summary>Play a WAV file once at (volume, pan). Decoded + cached on first use (44.1 kHz stereo).</summary>
        public void PlayFile(string path, float volume, float pan = 0f)
        {
            var buf = LoadFile(path);
            if (buf != null) Play(buf, volume, pan);
        }

        /// <summary>Decode a WAV to the mixer format (44.1 kHz interleaved-stereo float), cached by path.
        /// Returns null (logged once, cached) when the file is missing/undecodable, so callers can pass a
        /// stem that isn't shipped yet without spamming the log or re-hitting the disk every frame.</summary>
        public float[] LoadFile(string path)
        {
            lock (_gate) { if (_fileCache.TryGetValue(path, out var cached)) return cached; }
            float[] buf = null;
            try { buf = Decode(path); }
            catch (Exception e) { Main.Log?.Log("[audio] decode " + path + " — " + e.Message); }
            lock (_gate) { _fileCache[path] = buf; }
            return buf;
        }

        /// <summary>Like <see cref="LoadFile"/> but MONO-summed (both lanes = the L+R average), so a hard pan keeps
        /// the whole signal instead of dropping a channel. Needed for STEREO stems played through a fixed pan (the
        /// wall tones); mono stems don't need it (their two lanes are already identical). Mirrors WA's ReadMono.</summary>
        public float[] LoadFileMono(string path)
        {
            var st = LoadFile(path);
            if (st == null || st.Length == 0) return st;
            var mono = new float[st.Length];
            for (int i = 0; i + 1 < st.Length; i += 2)
            {
                float m = 0.5f * (st[i] + st[i + 1]);
                mono[i] = m; mono[i + 1] = m;
            }
            return mono;
        }

        // Decode a WAV normalised to the mixer format (44.1 kHz stereo float). Ported from WA NAudioEngine.Decode.
        private static float[] Decode(string path)
        {
            using (var reader = new AudioFileReader(path))
            {
                ISampleProvider sp = reader;
                if (sp.WaveFormat.SampleRate != Rate) sp = new WdlResamplingSampleProvider(sp, Rate);
                if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
                var all = new List<float>(Rate);
                var tmp = new float[Rate * 2];
                int n;
                while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(tmp[i]);
                return all.ToArray();
            }
        }

        // ---- resident (looping, live-mutable) voices ----

        internal void Add(ISampleProvider p) { lock (_gate) { EnsureStarted(); _mixer.AddMixerInput(p); } }
        internal void Remove(ISampleProvider p) { lock (_gate) { try { _mixer?.RemoveMixerInput(p); } catch { } } }

        /// <summary>Start a seamlessly-looping voice on a cached interleaved-stereo buffer at (volume, pan);
        /// returns an opaque handle. Re-pan / re-gain it live with <see cref="SetVoice"/>, stop + release with
        /// <see cref="Remove(object)"/>. This is what a held sonar tone and (Phase H) the wall tones ride.</summary>
        public object PlayLoop(float[] interleavedStereo, float volume, float pan = 0f)
        {
            if (interleavedStereo == null || interleavedStereo.Length == 0) return null;
            var v = new LoopVoice(interleavedStereo, Rate, volume, pan);
            Add(v);
            return v;
        }

        /// <summary>Re-pan / re-gain a live loop voice (main-thread safe; the audio thread ramps to it).</summary>
        public void SetVoice(object handle, float volume, float pan)
        {
            if (handle is LoopVoice v) v.Set(volume, pan);
        }

        /// <summary>Stop + remove a resident voice handle (from <see cref="PlayLoop"/>).</summary>
        public void Remove(object handle)
        {
            if (handle is ISampleProvider p) Remove(p);
        }

        /// <summary>Fire a LIVE, self-draining positional one-shot on a cached interleaved-stereo buffer (its LEFT
        /// lane is the mono source): constant-power pan + interaural delay + front/back low-pass from
        /// <paramref name="cue"/>, at <paramref name="volume"/>. Returns an <see cref="ISpatialVoice"/> so
        /// <see cref="SpatialSources"/> can re-place it every frame as the cursor moves; it removes itself from the
        /// mixer when the buffer (plus the ITD tail) drains. This is the real-3D object-sonar ping.</summary>
        public ISpatialVoice PlaySpatial(float[] interleavedStereo, SpatialCue cue, float volume)
        {
            if (interleavedStereo == null || interleavedStereo.Length == 0) return null;
            try
            {
                var v = new PositionalEmitter(interleavedStereo, Rate);
                v.SetPlacement(cue, volume);
                Add(v);
                return v;
            }
            catch (Exception e) { Main.Log?.Log("[audio] spatial play failed: " + e.Message); return null; }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _out?.Stop(); _out?.Dispose(); } catch { }
                _out = null; _mixer = null;
                _fileCache.Clear();
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

        // A resident, live-mutable looping voice. The cached interleaved-stereo buffer loops seamlessly
        // (wrap at the end); constant-power L/R gains are set on the main thread (volatile) and RAMPED toward
        // across each read block on the audio thread, so a re-pan / volume change is click-free. Never
        // self-removes — it plays (silently when gain 0) until Remove()d. Mirrors WA NAudioEngine's resident
        // voice discipline; the wall-tone channel wraparound and the target/current ramp are the same shape.
        private sealed class LoopVoice : ISampleProvider
        {
            private readonly float[] _buf;      // interleaved stereo; looped
            private readonly int _frames;
            private int _pos;                   // frame index into _buf
            private volatile float _tGainL, _tGainR; // targets (main thread)
            private float _cGainL, _cGainR;     // current smoothed (audio thread)
            private bool _primed;

            public LoopVoice(float[] buf, int rate, float vol, float pan)
            {
                _buf = buf;
                _frames = buf.Length / 2;
                Set(vol, pan);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public void Set(float vol, float pan)
            {
                float t = (pan + 1f) * 0.5f * (float)(Math.PI / 2.0);
                _tGainL = vol * (float)Math.Cos(t);
                _tGainR = vol * (float)Math.Sin(t);
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                if (frames == 0 || _frames == 0) { for (int i = 0; i < count; i++) buffer[offset + i] = 0f; return count; }

                float tL = _tGainL, tR = _tGainR;
                if (!_primed) { _cGainL = tL; _cGainR = tR; _primed = true; }
                float dL = (tL - _cGainL) / frames, dR = (tR - _cGainR) / frames;

                for (int f = 0; f < frames; f++)
                {
                    _cGainL += dL; _cGainR += dR;
                    buffer[offset + f * 2] = _buf[_pos * 2] * _cGainL;
                    buffer[offset + f * 2 + 1] = _buf[_pos * 2 + 1] * _cGainR;
                    if (++_pos >= _frames) _pos = 0; // seamless wrap
                }
                return count; // ReadFully mixer: always full (silence when gains are 0)
            }
        }

        // A spatialised, LIVE one-shot. The cached buffer is treated as MONO (left lane — our stems are mono,
        // duplicated to stereo on decode), low-passed for the front/back cue, then split L/R with a constant-power
        // pan and a fractional ITD delay on the FAR channel (a small ring of recent FILTERED samples). Crucially
        // the placement is re-settable while it plays: SetPlacement (main thread) writes target gains/ITD/cutoff
        // and Read (audio thread) ramps the current values toward them across each block, so a source tracks the
        // moving cursor without clicks. Goes silent past the buffer end, draining the delay tail, then returns 0
        // so the mixer auto-removes it. Ported verbatim from WrathAccess NAudioEngine.PositionalEmitter.
        private sealed class PositionalEmitter : ISampleProvider, ISpatialVoice
        {
            private const int RingSize = 64;          // >= max ITD (~29 frames) + margin; power of two
            private const int RingMask = RingSize - 1;
            private const int TailFrames = RingSize;  // drain the delay line after the source ends
            private const float OpenHz = 20000f;      // "no filter" cutoff (effectively transparent)
            private const float Q = 0.707f;

            private readonly float[] _buf;            // interleaved stereo; left lane sampled as mono
            private readonly int _srcFrames;
            private readonly int _rate;
            private readonly float[] _ring = new float[RingSize];
            private readonly BiQuadFilter _lp;        // always present; cutoff ramped (OpenHz ≈ bypass)

            // Targets — written by SetPlacement (main thread), read by Read (audio thread).
            private volatile float _tGainL, _tGainR, _tItd, _tCutoff, _tWet;
            // Current smoothed values — audio thread only.
            private float _cGainL, _cGainR, _cItd, _cCutoff, _cWet;
            private bool _primed;
            private int _frame;
            private volatile bool _finished;

            public PositionalEmitter(float[] buf, int rate)
            {
                _buf = buf;
                _srcFrames = buf.Length / 2;
                _rate = rate;
                _tCutoff = _cCutoff = OpenHz;
                _lp = BiQuadFilter.LowPassFilter(rate, OpenHz, Q);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
            }

            public WaveFormat WaveFormat { get; }
            public bool Finished => _finished;

            public void SetPlacement(SpatialCue cue, float volume)
            {
                float t = (cue.Pan + 1f) * 0.5f * (float)(Math.PI / 2.0);
                _tGainL = volume * (float)Math.Cos(t);
                _tGainR = volume * (float)Math.Sin(t);
                _tItd = cue.ItdSamples;
                _tCutoff = Mathf.Clamp(cue.LowpassHz, 20f, _rate * 0.49f);
                _tWet = cue.WetMix < 0f ? 0f : (cue.WetMix > 1f ? 1f : cue.WetMix);
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                if (frames == 0) return 0;

                float tGainL = _tGainL, tGainR = _tGainR, tItd = _tItd, tCutoff = _tCutoff, tWet = _tWet;
                if (!_primed)
                {
                    _cGainL = tGainL; _cGainR = tGainR; _cItd = tItd; _cCutoff = tCutoff; _cWet = tWet;
                    _lp.SetLowPassFilter(_rate, Mathf.Clamp(_cCutoff, 20f, _rate * 0.49f), Q);
                    _primed = true;
                }

                // Cutoff lerps once per block (retuning per sample is too costly; filter state is preserved).
                if (Mathf.Abs(tCutoff - _cCutoff) > 1f)
                {
                    _cCutoff += (tCutoff - _cCutoff) * 0.5f;
                    _lp.SetLowPassFilter(_rate, Mathf.Clamp(_cCutoff, 20f, _rate * 0.49f), Q);
                }

                // Gains + ITD + wet-mix ramp linearly to target across the block — click-free moving source.
                float dGainL = (tGainL - _cGainL) / frames;
                float dGainR = (tGainR - _cGainR) / frames;
                float dItd = (tItd - _cItd) / frames;
                float dWet = (tWet - _cWet) / frames;

                int produced = 0;
                for (int f = 0; f < frames; f++)
                {
                    if (_frame >= _srcFrames + TailFrames) break;
                    _cGainL += dGainL; _cGainR += dGainR; _cItd += dItd; _cWet += dWet;

                    // Blend the dry source with its low-passed copy by the rear wet-mix. Dry ahead/at the side
                    // (wet ≈ 0); behind, the filtered copy fades in — keeping bright cues audible.
                    float dry = _frame < _srcFrames ? _buf[_frame * 2] : 0f;
                    float wet = _lp.Transform(dry);
                    float m = dry + _cWet * (wet - dry);
                    _ring[_frame & RingMask] = m;

                    float itdMag = _cItd < 0f ? -_cItd : _cItd;
                    int itdInt = (int)itdMag; if (itdInt > RingSize - 2) itdInt = RingSize - 2;
                    float frac = itdMag - (int)itdMag;
                    int d0 = _frame - itdInt, d1 = d0 - 1;
                    float s0 = d0 >= 0 ? _ring[d0 & RingMask] : 0f;
                    float s1 = d1 >= 0 ? _ring[d1 & RingMask] : 0f;
                    float far = s0 + (s1 - s0) * frac;
                    float near = m;

                    bool delayLeft = _cItd >= 0f; // +ve = source east = right ear leads, left ear lags
                    buffer[offset + produced++] = (delayLeft ? far : near) * _cGainL;
                    buffer[offset + produced++] = (delayLeft ? near : far) * _cGainR;
                    _frame++;
                }
                if (_frame >= _srcFrames + TailFrames) _finished = true;
                return produced;
            }
        }
    }
}
