using RTAccess.Exploration; // MapCursor (the audio-frame origin)
using RTAccess.Settings;    // BoolSetting (ITD / front-back / head-shadow A/B toggles)
using UnityEngine;

namespace RTAccess.Audio
{
    /// <summary>The stereo placement cues for one source, heard from the shared cursor (our virtual listener).</summary>
    internal struct SpatialCue
    {
        public float Pan;         // -1 (hard west/left) .. +1 (hard east/right) lateral fraction (centre-expanded)
        public float ItdSamples;  // interaural delay; magnitude = samples, sign = +east / -west (far ear delayed)
        public float RearShelfDb; // high-shelf gain on the WHOLE source: 0 ahead/at the side .. negative behind
        public float FarShadowDb; // high-shelf gain on the FAR EAR only (head shadow): 0 centred .. negative at the side
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
    /// The compass-stable audio frame. All spatial panning (sonar, object cues) is computed HERE in code from the
    /// shared <see cref="MapCursor"/> position and a FIXED north (+Z), never from the game's Wwise listener — so a
    /// source panned hard-left always means west, whatever the camera is doing (see the plan §3.4: the engine
    /// <c>DefaultListener</c> is unrelated to this and stays deferred). RT map space is +Z = north, +X = east
    /// (matching <see cref="Geo"/> / InteractableDescriber's compass).
    ///
    /// The current WrathAccess perceptual model, three independent channels:
    ///  - <b>east/west → capped-ILD pan + ITD (+ far-ear shadow).</b> A constant-power pan whose interaural LEVEL
    ///    difference is CAPPED (<see cref="MaxIldDb"/> ≈ 12 dB) instead of driving the far ear to silence — a real
    ///    head shadows the far ear by ~8–20 dB, never −∞, and the interaural TIME difference (<see cref="Cue"/>'s
    ///    Woodworth curve) needs a far-ear signal to exist at all. Below ~1.5 kHz ITD is the dominant cue and the
    ///    brain resolves it far finer than a sample, so together they externalise left/right — especially on
    ///    headphones. The far ear also gets a mild laterality-scaled high-shelf cut (frequency-dependent head
    ///    shadow), which reads as a head between two ears rather than a mixer pan.
    ///  - <b>distance → gain.</b> The caller's job (<see cref="VolumeFor"/> / each system's own falloff); this only
    ///    does direction. A magnitude can't tell front from back — that's the next channel.
    ///  - <b>north/south → timbre.</b> Stereo can't pan front/back, so sources BEHIND the listener get a high-shelf
    ///    CUT ramping to −<see cref="RearMaxCutDb"/> at due-south (darker/quieter = behind, the audiogame
    ///    convention). A shelf, not a lowpass mix: our cues are bright and narrowband, so a lowpass erases them and
    ///    a parallel dry/wet blend comb-filters; a shelf darkens broadband sounds and merely quietens bright ones,
    ///    minimum-phase, nothing ever disappears.
    ///
    /// The extra cues are each A/B-toggleable by ear (audio.itd / audio.front_back_filter / audio.head_shadow).
    /// The pan/volume helpers stay pure; <see cref="Cue"/> composes the direction cues into a <see cref="SpatialCue"/>
    /// a live <see cref="ISpatialVoice"/> renders. The one game read (<see cref="PlayAt"/>) touches only
    /// <see cref="MapCursor"/>.
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
        private const float WoodworthMax = Mathf.PI / 2f + 1f;      // (θ + sin θ) at θ = 90°

        // Interaural level difference at hard side, in dB. Finite on purpose: the far ear must keep signal for
        // the ITD to be audible, and a 100%/0% pan reads as "inside one ear" — the opposite of external.
        private const float MaxIldDb = 12f;

        // Perceptual expansion of the lateral fraction: |lat|^exp with exp < 1 steepens the response near the
        // centre (a small x offset pans noticeably) and leaves the extremes unchanged — lining the cursor up on
        // a source by ear needs the sharp null at zero, not linear geometry. All lateral cues (ILD, ITD, ear
        // shadow) share the expanded fraction so they keep agreeing on the angle.
        private const float PanExponent = 0.82f;

        // Far-ear head shadow: extra high-shelf cut on the far ear only, scaled by laterality.
        public const float ShadowCornerHz = 1500f; // shadow acts above ~1–2 kHz (long waves diffract around the head)
        private const float ShadowMaxDb = 8f;       // at hard side, far-ear highs sit ~MaxIld+8 dB below the near ear

        // Rear (front/back) cue: high-shelf cut ramping in over the rear hemisphere.
        public const float RearCornerHz = 3000f;
        private const float RearMaxCutDb = 10f;     // due-south: −10 dB above the corner (bright cues ≈ −10 dB overall)

        public static bool ItdEnabled => ModSettings.GetSetting<BoolSetting>("audio.itd")?.Get() ?? true;
        public static bool FilterEnabled => ModSettings.GetSetting<BoolSetting>("audio.front_back_filter")?.Get() ?? true;
        public static bool ShadowEnabled => ModSettings.GetSetting<BoolSetting>("audio.head_shadow")?.Get() ?? true;

        /// <summary>Constant-power pan (-1 = hard west, +1 = hard east) for a source offset from the listener,
        /// in metres. <paramref name="dxEast"/> is +X (east), <paramref name="dzNorth"/> is +Z (north).
        /// Within <paramref name="panWidth"/> the pan opens toward the side; past it, it tracks bearing. Used by
        /// the non-tracked one-shot path (<see cref="PlayAt"/> / AudioProbe); the tracked sonar uses <see cref="Cue"/>.</summary>
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

        /// <summary>Full direction cues for a source offset from the listener (metres): a centre-expanded lateral
        /// fraction, a Woodworth spherical-head interaural time delay, a far-ear head-shadow shelf, and a
        /// rear-hemisphere high-shelf cut (darker = behind). <paramref name="panWidth"/> is the lateral crossover.
        /// Distance→volume stays the caller's job (pass the gain separately). The capped-ILD level split itself is
        /// applied in the voice via <see cref="PanGains"/>. Ported from WrathAccess's <c>Spatializer.Cue</c>.</summary>
        public static SpatialCue Cue(float dxEast, float dzNorth, float panWidth = DefaultPanWidth)
        {
            float dist = Mathf.Sqrt(dxEast * dxEast + dzNorth * dzNorth);
            float lat = dist > 1e-4f ? Mathf.Clamp(dxEast / Mathf.Max(dist, panWidth), -1f, 1f) : 0f;
            if (lat != 0f) lat = Mathf.Sign(lat) * Mathf.Pow(Mathf.Abs(lat), PanExponent); // centre expansion

            var cue = new SpatialCue { Pan = lat };

            if (ItdEnabled)
            {
                // Woodworth spherical-head model: ITD ∝ θ + sin θ (lat ≈ sin θ), normalised to 1 at the side —
                // a touch more delay at mid angles than the plain sin taper.
                float s = Mathf.Abs(lat);
                float theta = Mathf.Asin(Mathf.Clamp01(s));
                cue.ItdSamples = MaxItdSamples * ((theta + s) / WoodworthMax) * Mathf.Sign(lat);
            }

            if (ShadowEnabled) cue.FarShadowDb = -ShadowMaxDb * Mathf.Abs(lat);

            // Front/back: only the rear hemisphere is darkened (matches "south of the listener" exactly), the shelf
            // cut ramping in linearly from the due-side line to its maximum at due-south.
            if (FilterEnabled && dist > 1e-4f)
            {
                float northFrac = Mathf.Clamp(dzNorth / dist, -1f, 1f); // +1 ahead .. -1 behind
                if (northFrac < 0f) cue.RearShelfDb = RearMaxCutDb * northFrac; // negative → a cut
            }
            return cue;
        }

        /// <summary>Stereo gains for a lateral pan fraction: constant-power taper with the interaural level
        /// difference capped at <see cref="MaxIldDb"/> (linear in dB with |pan|), power-normalised so loudness
        /// stays constant across the arc. Centre = 0.707/0.707, matching the old constant-power law.</summary>
        public static void PanGains(float pan, out float gainL, out float gainR)
        {
            float mag = Mathf.Clamp01(Mathf.Abs(pan));
            float far = Mathf.Pow(10f, -MaxIldDb * mag / 20f);
            float norm = 1f / Mathf.Sqrt(1f + far * far);
            float near = norm; far *= norm;
            if (pan >= 0f) { gainR = near; gainL = far; }
            else { gainL = near; gainR = far; }
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
