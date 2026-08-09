using System.Collections.Generic;
using RTAccess.Audio;   // Spatializer, SpatialSources, AudioAssets
using RTAccess.Screens; // InGameScreen.ExplorationActive
using RTAccess.Settings;
using RTAccess.Speech;  // Speaker (toggle announce)
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The ambient <b>sonar</b>: a staggered sweep that pings the things around the shared <see cref="MapCursor"/>
/// one at a time, ordered left→right, each positioned by distance (volume) and bearing (pan via
/// <see cref="Spatializer"/>) so a blind player can "feel the room" instead of reading one tile per keypress.
/// Rather than sounding everything at once (which phantom-centres two same-type sources into one averaged
/// blob), it fires one ping, waits a gap that shrinks as the crowd grows, and repeats — a nearby handful feel
/// spacious, a crowd compresses toward the audible floor, nothing is dropped. Ported from WrathAccess's
/// <c>SonarSystem</c> sweep discipline.
///
/// This is the first real consumer of the Phase F audio primitives (docs/plans/echoing-charting-lovelace.md).
/// It is deliberately LEAN — driven directly from <c>Main.OnUpdate</c> (like <see cref="WorldModel"/> and
/// <see cref="Targeting"/>), NOT yet wrapped in the WA overlay framework (Phase E): that framework's mode-
/// composition machinery earns its keep only once several audio systems (walls/fog/object cues, Phases G–I)
/// coexist, per the plan's own rationale. When those land, this folds into the framework as one
/// <c>OverlaySystem</c>.
///
/// Each ping is now a <b>recorded per-type WAV stem</b> (WrathAccess's <c>assets/audio/interactables/*.wav</c>,
/// mapped from the thing's <see cref="ScanItem.Primary"/> taxonomy node by <see cref="StemFor"/>) fired as a
/// <b>live-tracked 3D source</b> via <see cref="Audio.SpatialSources"/> — re-panned / re-attenuated every frame
/// against the moving cursor and the item's nearest edge, in real 3D (constant-power pan + interaural delay +
/// front/back low-pass, <see cref="Audio.Spatializer.Cue"/>). This replaces the earlier frozen synth pings the
/// maintainer's ear-test rejected: identity (recorded timbre), motion (it follows you), and depth (front/back).
///
/// GATED OFF by default (<c>exploration.sonar = off</c>): audio quality is un-self-verifiable, so the maintainer
/// flips it on and tunes by ear (Off / When moving / Continuous).
/// </summary>
internal static class Sonar
{
    // ---- tuning ---- FIXED knobs (WA keeps these const too):
    private const float MinVol = 0.08f;    // floor so a far-but-visible thing stays audible
    private const float PanWidth = 3f;     // lateral pan crossover (~2 tiles)
    private const float SpreadSec = 0.75f; // K: per-ping gap at one thing (then clamped by GapMin/Max)
    private const float MoveGrace = 1.25f; // "moving recently" window for the When-moving mode

    // Cadence + range knobs — now user SETTINGS (exploration.sonar_*), read live each frame, matching WrathAccess's
    // tunables so the sweep is shapeable by ear. RestSec is the silence BETWEEN sweeps (set 0 → continuous); the
    // gaps bound the per-ping spacing; RefDist/MaxDist set the volume rolloff + sense radius. Defaults match WA
    // (rest 400 ms, gaps 100–200 ms) with the radius tightened to WA's ~12 m / 3 m (was 25 m / 5 m — a sparser,
    // gappier sweep). See Settings/Defaults.cs for the ranges.
    private static int IntSet(string path, int fallback) => ModSettings.GetSetting<IntSetting>(path)?.Get() ?? fallback;
    private static float MaxDist => IntSet("exploration.sonar_max_dist", 12);   // sense radius (m); drop beyond it
    private static float RefDist => IntSet("exploration.sonar_ref_dist", 3);    // distance→volume reference (m)
    private static float GapMin  => IntSet("exploration.sonar_gap_min", 100) / 1000f;
    private static float GapMax  => IntSet("exploration.sonar_gap_max", 200) / 1000f;
    private static float RestSec => IntSet("exploration.sonar_rest", 400) / 1000f; // pause between sweeps

    private static readonly List<ScanItem> _sweep = new List<ScanItem>();
    private static int _index;
    private static float _timer;

    // Motion tracking for the When-moving mode: seconds since the cursor frame last changed.
    private static Vector3 _lastPos;
    private static bool _haveLast;
    private static float _sinceMoved = MoveGrace;

    private enum Playback { Off, WhenMoving, Continuous }

    private static Playback Mode
    {
        get
        {
            var id = ModSettings.GetSetting<ChoiceSetting>("exploration.sonar")?.Current?.Id;
            return id == "continuous" ? Playback.Continuous : id == "when_moving" ? Playback.WhenMoving : Playback.Off;
        }
    }

    private static float Volume => (ModSettings.GetSetting<IntSetting>("exploration.sonar_volume")?.Get() ?? 60) / 100f;
    private static string ReviewSound => ModSettings.GetSetting<ChoiceSetting>("exploration.sonar_review_sound")?.Current?.Id ?? "silent";

    /// <summary>Per-frame sweep step. Gated on exploration control + the play mode; silent (and reset) otherwise so
    /// a fresh sweep starts when control/movement returns. Never throws out of the update loop.</summary>
    public static void Tick(float dt)
    {
        try
        {
            TrackMotion(dt);

            var mode = Mode;
            bool play = mode == Playback.Continuous
                || (mode == Playback.WhenMoving && _sinceMoved < MoveGrace);
            if (!play || !InGameScreen.SoundscapeActive || !ControlState.HasControl) { ResetSweep(); return; }

            _timer -= dt;
            if (_timer > 0f) return;

            if (_index >= _sweep.Count)   // whole sweep fired (or none yet) → snapshot a fresh one
            {
                Snapshot();
                _index = 0;
                if (_sweep.Count == 0) { _timer = RestSec; return; } // nothing in range — idle, recheck after a rest
            }

            FirePing(_sweep[_index++]);   // positioned live, in case the cursor moved during the sweep
            _timer = _index >= _sweep.Count ? RestSec : GapSec(_sweep.Count);
        }
        catch (Exception e) { Main.Log?.Error("Sonar.Tick failed: " + e); }
    }

    /// <summary>Cycle the sonar playback mode Off → When moving → Continuous → Off, speak the new state, and
    /// persist it. Bound to Ctrl+F2 — the same chord WrathAccess uses for its sonar-mode toggle.</summary>
    public static void ToggleMode()
    {
        var s = ModSettings.GetSetting<ChoiceSetting>("exploration.sonar");
        if (s == null) return;
        string next = s.Current?.Id switch
        {
            "off" => "when_moving",
            "when_moving" => "continuous",
            _ => "off",
        };
        s.Set(next);
        Speaker.Speak(Loc.T("sonar.mode." + next), interrupt: true);
    }

    private static void TrackMotion(float dt)
    {
        var p = MapCursor.ListenPosition;
        if (_haveLast && (p - _lastPos).sqrMagnitude > 1e-4f) _sinceMoved = 0f;
        else _sinceMoved += dt;
        _lastPos = p; _haveLast = true;
    }

    private static void ResetSweep() { _sweep.Clear(); _index = 0; _timer = 0f; }

    // Perceivable things within the sense radius of the cursor, ordered left→right by lateral offset so the pan
    // glides across the sweep (two same-type things read as "left … right", not a centred average).
    private static void Snapshot()
    {
        var c = MapCursor.ListenPosition;
        float maxDist = MaxDist; // read the setting once per sweep
        _sweep.Clear();
        foreach (var it in WorldModel.Items)
        {
            // Detectable-from-cursor gate (matches WA's SonarSystem + RT's scanner review cycles): currently seen,
            // OR a remembered thing under fog with a CLEAR line of sight from the cursor. Fogged persistent OBJECTS
            // (chests/doors/mechanisms — IsVisible stays reveal-latched) come back in; fogged CREATURES do NOT
            // (ProxyUnit.IsVisible follows IsVisibleForPlayer, which the game clears under fog) — the visual-parity
            // law holds automatically. Skip dead units.
            if (!it.DetectableFrom(c) || it.IsDead) continue;
            if (StemFor(it.Primary) == null) continue;       // no sound configured for this thing
            var np = it.NearestPoint(c);
            float dx = np.x - c.x, dz = np.z - c.z;
            if (dx * dx + dz * dz > maxDist * maxDist) continue;
            _sweep.Add(it);
        }
        _sweep.Sort((a, b) => (a.Position.x - c.x).CompareTo(b.Position.x - c.x));
    }

    private static void FirePing(ScanItem item)
    {
        if (!item.IsVisible) return; // still known/exists since the snapshot (a remembered fogged object keeps pinging)
        var stem = StemFor(item.Primary);
        if (stem == null) return;
        // A LIVE source: heard from the moving cursor, positioned at the nearest point on the item's actual shape
        // (recomputed as the cursor moves, so a wall reads along its length), re-panned/attenuated every frame by
        // SpatialSources — in real 3D (pan + ITD + front/back filter) — until it drains. No longer freezes at fire.
        SpatialSources.Play(
            AudioAssets.Interactable(stem),
            () => MapCursor.ListenPosition,
            c => item.NearestPoint(c),
            d => Spatializer.VolumeFor(d, RefDist, MinVol) * Volume,
            PanWidth);
    }

    // gap = clamp(K/count, GapMin, GapMax): spacious for a few, compressing toward the floor as the crowd grows,
    // so the whole sweep lengthens with count but pings stay individually audible.
    private static float GapSec(int count) => Mathf.Clamp(SpreadSec / Mathf.Max(1, count), GapMin, GapMax);

    /// <summary>The REVIEW-CURSOR ping: a one-shot positional sound at the just-selected item, heard from the review
    /// origin <paramref name="from"/> (FROZEN — it does NOT chase the movement cursor like the tracked sweep pings;
    /// a deliberate "this thing, relative to where you looked" cue). Selection feedback for the scanner's browse /
    /// review cycles (fired from <c>Scanner.Select</c>), SEPARATE from the ambient sweep and NOT gated on the sonar
    /// mode — it's controlled only by <c>exploration.sonar_review_sound</c> (Silent = off, the default, so the whole
    /// soundscape still ships silent). Uses a root-level cue wav (review.wav / tracking.wav) with the sonar volume +
    /// spatial model. Ported from WrathAccess <c>SonarSystem.PlayReview</c>.</summary>
    public static void PlayReview(ScanItem item, Vector3 from)
    {
        try
        {
            if (item == null) return;
            var stem = ReviewSound;
            if (string.IsNullOrEmpty(stem) || stem == "silent") return;
            var buf = AudioMixer.Instance.LoadFile(AudioAssets.Cue(stem)); // root-level wav, decoded + cached
            if (buf == null || buf.Length == 0) return;
            var np = item.NearestPoint(from);
            float dx = np.x - from.x, dz = np.z - from.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            // Fire-and-forget: a frozen positional one-shot (ignore the returned voice so it isn't cursor-tracked).
            AudioMixer.Instance.PlaySpatial(buf, Spatializer.Cue(dx, dz, PanWidth), Spatializer.VolumeFor(dist, RefDist, MinVol) * Volume);
        }
        catch (Exception e) { Main.Log?.Error("Sonar.PlayReview failed: " + e); }
    }

    // ---- per-type recorded stems (WA's assets/audio/interactables/*.wav) ----
    // Each taxonomy node maps to WrathAccess's own default stem for that thing, so types are told apart by their
    // recorded timbre. Scenery is silent (matches WA); anything unmapped isn't pinged. RT's taxonomy is flatter
    // than WA's (flat Containers, no door/loot sub-splits), so those collapse to the parent's default stem.
    private static string StemFor(string primary)
    {
        switch (primary)
        {
            case ScanTaxonomy.UnitsEnemies:  return "units-enemy";
            case ScanTaxonomy.UnitsNeutrals: return "units-neutral";
            // Party members and non-commandable allies share the friendly timbre (WA ships one ally stem).
            case ScanTaxonomy.UnitsParty:    return "units-ally";
            case ScanTaxonomy.UnitsAllies:   return "units-ally";
            case ScanTaxonomy.Hazards:       return "hazard-zone";
            case ScanTaxonomy.BuffZones:     return "buff-zone";
            case ScanTaxonomy.Containers:    return "loot-generic";
            case ScanTaxonomy.Corpses:       return "loot-corpse";
            case ScanTaxonomy.Doors:         return "door";
            case ScanTaxonomy.Exits:         return "transition";
            // A way between floors is an exit from this level, so it shares the transition timbre. Without a case
            // here the ladders and climbs that just moved out of SearchPoints would fall to the default and stop
            // pinging altogether — silently losing the one thing worth hearing in a stacked area.
            case ScanTaxonomy.LevelChanges:  return "transition";
            case ScanTaxonomy.SearchPoints:  return "unknown";
            case ScanTaxonomy.Traps:         return "trap";
            case ScanTaxonomy.Mechanisms:    return "mechanism";
            case ScanTaxonomy.Scenery:       return null; // WA: scenery is silent
            default:                         return null; // unmapped → not pinged
        }
    }
}
