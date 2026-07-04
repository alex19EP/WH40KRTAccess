using RTAccess.Exploration; // MapCursor (the audio-frame origin)
using RTAccess.Settings;    // BoolSetting (ITD / front-back A/B toggles)
using UnityEngine;

namespace RTAccess.Audio
{
    /// <summary>The stereo placement cues for one source, heard from the shared cursor (our virtual listener).</summary>
    internal struct SpatialCue
    {
        public float Pan;        // -1 (hard west/left) .. +1 (hard east/right), constant-power
        public float ItdSamples; // interaural delay; magnitude = samples, sign = +east / -west (far ear delayed)
        public float LowpassHz;  // the wet (rear) path's BiQuad cutoff; lower = more muffled
        public float WetMix;     // 0 = fully dry (ahead/side) .. up to MaxWet behind; how much of the filtered
                                 // signal replaces the dry one. The dry remainder keeps bright, narrowband cues
                                 // (which a lowpass would erase) audible — behind then reads as quieter/darker.
    }

    /// <summary>A playing positional voice whose placement can be re-set live (from the main thread) as the
    /// listener (cursor) moves — so a one-shot still tracks pan / gain / ITD / filter while it's audible, instead
    /// of freezing at fire time. Updates are smoothed inside the voice so a moving source never clicks. Ported
    /// from WrathAccess's <c>ISpatialVoice</c>; driven by <see cref="SpatialSources"/>.</summary>
    internal interface ISpatialVoice
    {
        bool Finished { get; }                        // drained — safe to drop from tracking
        void SetPlacement(SpatialCue cue, float volume);
    }
    /// <summary>
    /// The compass-stable audio frame. All spatial panning (sonar, wall tones, object cues) is computed HERE
    /// in code from the shared <see cref="MapCursor"/> position and a FIXED north (+Z), never from the game's
    /// Wwise listener — so a source panned hard-left always means west, whatever the camera is doing (see the
    /// plan §3.4: the engine <c>DefaultListener</c> is unrelated to this and stays deferred). RT map space is
    /// +Z = north, +X = east (matching <see cref="Geo"/> / InteractableDescriber's compass).
    ///
    /// The full WrathAccess model: a constant-power lateral pan PLUS an interaural time difference (the far ear
    /// hears the sound a few samples later — the dominant low-frequency localisation cue, sharper than pan alone
    /// on headphones) PLUS a front/back low-pass (stereo can't pan front-to-back, so sources BEHIND the listener
    /// are progressively muffled — the audiogame convention for resolving the pan's front/back ambiguity). The
    /// pan/volume helpers stay pure; <see cref="Cue"/> composes all three into a <see cref="SpatialCue"/> a live
    /// <see cref="ISpatialVoice"/> renders. ITD + filter are each A/B-toggleable by ear (audio.itd /
    /// audio.front_back_filter). The one game read (<see cref="PlayAt"/>) touches only <see cref="MapCursor"/>.
    /// </summary>
    internal static class Spatializer
    {
        public const int Rate = AudioMixer.Rate;

        /// <summary>Default lateral crossover in metres (~2 tiles): inside it a source pans toward hard L/R,
        /// beyond it the pan reflects bearing. WA used 10 ft (~3 m); RT is metric.</summary>
        public const float DefaultPanWidth = 3f;

        // Max interaural delay ≈ head width / speed of sound ≈ 0.22 m / 343 m/s ≈ 0.66 ms (~29 frames @ 44.1 kHz).
        private const float MaxItdSeconds = 0.00066f;
        private static float MaxItdSamples => MaxItdSeconds * Rate;

        // Front/back cue: the wet path lowpass closes from open (due-side) to muffled (due-south), its MIX rising
        // in step. Because our stems are bright, a pure lowpass would silence them behind you — so the dry
        // remainder (1 − WetMix) is always kept: broadband sounds darken, bright ones just get quieter.
        private const float OpenHz = 20000f;   // due-side: wet path effectively transparent (and WetMix ≈ 0)
        private const float MuffledHz = 500f;  // due-south: the wet path is heavily muffled
        private const float MaxWet = 0.5f;     // due-south: 50% filtered / 50% dry (a bright cue → ~−6 dB)

        public static bool ItdEnabled => ModSettings.GetSetting<BoolSetting>("audio.itd")?.Get() ?? true;
        public static bool FilterEnabled => ModSettings.GetSetting<BoolSetting>("audio.front_back_filter")?.Get() ?? true;

        /// <summary>Constant-power pan (-1 = hard west, +1 = hard east) for a source offset from the listener,
        /// in metres. <paramref name="dxEast"/> is +X (east), <paramref name="dzNorth"/> is +Z (north).
        /// Within <paramref name="panWidth"/> the pan opens toward the side; past it, it tracks bearing.</summary>
        public static float Pan(float dxEast, float dzNorth, float panWidth = DefaultPanWidth)
        {
            float dist = Mathf.Sqrt(dxEast * dxEast + dzNorth * dzNorth);
            return dist > 1e-4f ? Mathf.Clamp(dxEast / Mathf.Max(dist, panWidth), -1f, 1f) : 0f;
        }

        /// <summary>Distance → volume: 1 at the listener, falling as <c>refDist / (refDist + dist)</c>, floored
        /// at <paramref name="minVol"/> so a far-but-visible source stays audible. Ported from WA Sonar; the
        /// caller multiplies by its own system volume.</summary>
        public static float VolumeFor(float dist, float refDist, float minVol = 0.08f)
            => Mathf.Clamp(refDist / (refDist + Mathf.Max(0f, dist)), minVol, 1f);

        /// <summary>Full direction cues for a source offset from the listener (metres): constant-power pan, an
        /// interaural time delay sharing the pan's lateral fraction, and a rear-hemisphere low-pass (muffled =
        /// behind). <paramref name="panWidth"/> is the lateral crossover. Distance→volume stays the caller's job
        /// (pass the gain separately). Ported from WrathAccess's <c>Spatializer.Cue</c>.</summary>
        public static SpatialCue Cue(float dxEast, float dzNorth, float panWidth = DefaultPanWidth)
        {
            float dist = Mathf.Sqrt(dxEast * dxEast + dzNorth * dzNorth);
            float lat = dist > 1e-4f ? Mathf.Clamp(dxEast / Mathf.Max(dist, panWidth), -1f, 1f) : 0f;

            var cue = new SpatialCue { Pan = lat, LowpassHz = OpenHz, WetMix = 0f };

            // ITD shares the pan's lateral fraction so the time and level cues move together.
            if (ItdEnabled) cue.ItdSamples = MaxItdSamples * lat;

            // Front/back: only the rear hemisphere is processed (south of the listener), ramping from dry at the
            // due-side line to muffled-and-mixed-in at due-south.
            if (FilterEnabled && dist > 1e-4f)
            {
                float northFrac = Mathf.Clamp(dzNorth / dist, -1f, 1f); // +1 ahead .. -1 behind
                if (northFrac < 0f)
                {
                    float back = -northFrac; // 0 at the side line .. 1 at due-south
                    cue.LowpassHz = OpenHz * Mathf.Pow(MuffledHz / OpenHz, back); // log interp, open → muffled
                    cue.WetMix = back * MaxWet;
                }
            }
            return cue;
        }

        /// <summary>Fire a one-shot cached buffer at <paramref name="worldPos"/>, panned relative to the shared
        /// cursor + fixed north. The caller owns the distance→volume curve (pass the final <paramref name="volume"/>);
        /// this only resolves the pan from the current frame. The compass-stable in-code frame the plan calls for.</summary>
        public static void PlayAt(Vector3 worldPos, float[] buffer, float volume, float panWidth = DefaultPanWidth)
        {
            var c = MapCursor.Position;
            AudioMixer.Instance.Play(buffer, volume, Pan(worldPos.x - c.x, worldPos.z - c.z, panWidth));
        }
    }
}
