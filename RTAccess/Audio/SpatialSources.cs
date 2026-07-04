using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTAccess.Audio
{
    /// <summary>
    /// Keeps positional one-shots alive as scene SOURCES: while a voice is still audible, it's re-spatialised
    /// every frame against the moving listener (the shared cursor), so pan / gain / ITD / front-back filter
    /// follow the cursor instead of freezing at fire time. Ticked on the main thread (where game state + settings
    /// are safe to read); the per-voice updates are smoothed inside <see cref="ISpatialVoice"/> so movement never
    /// clicks. This is the "live-tracked 3D" half of the object sonar — the biggest gap the maintainer's ear-test
    /// flagged in the old frozen synth pings. Ported from WrathAccess's <c>SpatialSources</c>.
    ///
    /// A source is described by three live functions so the caller keeps all the geometry/volume maths:
    /// <c>listener</c> (where it's heard from now), <c>sourceAt</c> (the source world point given the listener —
    /// lets a wall track its nearest point, a unit just return its centre), and <c>gain</c> (distance → volume,
    /// including the system's own falloff + volume setting). Self-cleaning: a drained voice (Finished) is dropped;
    /// a throwing function drops it too (the source went stale/destroyed — let the voice tail out on its own).
    /// </summary>
    internal static class SpatialSources
    {
        private sealed class Src
        {
            public ISpatialVoice Voice;
            public Func<Vector3> Listener;
            public Func<Vector3, Vector3> SourceAt;
            public Func<float, float> Gain;
            public float PanWidth;
        }

        // Main-thread only (Play from the sonar tick, Tick from the frame loop) — no lock needed.
        private static readonly List<Src> _live = new List<Src>();

        /// <summary>Fire a tracked positional one-shot from a WAV path. Returns immediately; the voice is then
        /// re-placed each frame until it finishes. No-op if the file is missing or the voice couldn't start.</summary>
        public static void Play(string path, Func<Vector3> listener, Func<Vector3, Vector3> sourceAt,
            Func<float, float> gain, float panWidth)
        {
            if (listener == null || sourceAt == null || gain == null) return;
            try
            {
                var buf = AudioMixer.Instance.LoadFile(path);
                if (buf == null || buf.Length == 0) return;
                var c = listener();
                var s = sourceAt(c);
                float dx = s.x - c.x, dz = s.z - c.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                var voice = AudioMixer.Instance.PlaySpatial(buf, Spatializer.Cue(dx, dz, panWidth), gain(dist));
                if (voice == null) return;
                _live.Add(new Src { Voice = voice, Listener = listener, SourceAt = sourceAt, Gain = gain, PanWidth = panWidth });
            }
            catch (Exception e) { Main.Log?.Error("[spatial-src] play " + path + " — " + e); }
        }

        /// <summary>Re-spatialise every live source against the current listener. Drops finished voices.</summary>
        public static void Tick()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var src = _live[i];
                if (src.Voice.Finished) { _live.RemoveAt(i); continue; }
                try
                {
                    var c = src.Listener();
                    var s = src.SourceAt(c);
                    float dx = s.x - c.x, dz = s.z - c.z;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    src.Voice.SetPlacement(Spatializer.Cue(dx, dz, src.PanWidth), src.Gain(dist));
                }
                catch { _live.RemoveAt(i); } // a stale/destroyed source — let the voice drain on its own
            }
        }

        /// <summary>Forget all tracked sources (area change): the mixer voices tail out and self-remove; we just
        /// stop chasing points on the old grid.</summary>
        public static void Clear() => _live.Clear();
    }
}
