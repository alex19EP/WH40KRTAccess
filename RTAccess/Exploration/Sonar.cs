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
    // ---- tuning (metres / seconds); consts for v1, promoted to settings once the ear pass picks the knobs ----
    private const float MaxDist = 25f;    // sense radius: beyond it a thing drops from the sweep (no deceptive floor)
    private const float RefDist = 5f;     // distance→volume reference (vol = refDist/(refDist+dist))
    private const float MinVol = 0.08f;   // floor so a far-but-visible thing stays audible
    private const float PanWidth = 3f;    // lateral pan crossover (~2 tiles)
    private const float SpreadSec = 0.75f; // K: per-ping gap at one thing (then clamped by GapMin/Max)
    private const float GapMin = 0.10f;
    private const float GapMax = 0.20f;
    private const float RestSec = 0.40f;  // pause between sweeps
    private const float MoveGrace = 1.25f; // "moving recently" window for the When-moving mode

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
            if (!play || !InGameScreen.ExplorationActive || !ControlState.HasControl) { ResetSweep(); return; }

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
        Speaker.Speak(next == "when_moving" ? "Sonar when moving" : "Sonar " + next, interrupt: true);
    }

    private static void TrackMotion(float dt)
    {
        var p = MapCursor.Position;
        if (_haveLast && (p - _lastPos).sqrMagnitude > 1e-4f) _sinceMoved = 0f;
        else _sinceMoved += dt;
        _lastPos = p; _haveLast = true;
    }

    private static void ResetSweep() { _sweep.Clear(); _index = 0; _timer = 0f; }

    // Perceivable things within the sense radius of the cursor, ordered left→right by lateral offset so the pan
    // glides across the sweep (two same-type things read as "left … right", not a centred average).
    private static void Snapshot()
    {
        var c = MapCursor.Position;
        _sweep.Clear();
        foreach (var it in WorldModel.Items)
        {
            if (!it.CurrentlySeen || it.IsDead) continue;   // perceivable-now gate (fog/LOS via CurrentlySeen); skip corpses
            if (StemFor(it.Primary) == null) continue;       // no sound configured for this thing
            var np = it.NearestPoint(c);
            float dx = np.x - c.x, dz = np.z - c.z;
            if (dx * dx + dz * dz > MaxDist * MaxDist) continue;
            _sweep.Add(it);
        }
        _sweep.Sort((a, b) => (a.Position.x - c.x).CompareTo(b.Position.x - c.x));
    }

    private static void FirePing(ScanItem item)
    {
        if (!item.CurrentlySeen) return; // went out of perception since the snapshot
        var stem = StemFor(item.Primary);
        if (stem == null) return;
        // A LIVE source: heard from the moving cursor, positioned at the nearest point on the item's actual shape
        // (recomputed as the cursor moves, so a wall reads along its length), re-panned/attenuated every frame by
        // SpatialSources — in real 3D (pan + ITD + front/back filter) — until it drains. No longer freezes at fire.
        SpatialSources.Play(
            AudioAssets.Interactable(stem),
            () => MapCursor.Position,
            c => item.NearestPoint(c),
            d => Spatializer.VolumeFor(d, RefDist, MinVol) * Volume,
            PanWidth);
    }

    // gap = clamp(K/count, GapMin, GapMax): spacious for a few, compressing toward the floor as the crowd grows,
    // so the whole sweep lengthens with count but pings stay individually audible.
    private static float GapSec(int count) => Mathf.Clamp(SpreadSec / Mathf.Max(1, count), GapMin, GapMax);

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
            case ScanTaxonomy.UnitsParty:    return "units-ally";
            case ScanTaxonomy.Hazards:       return "hazard-zone";
            case ScanTaxonomy.BuffZones:     return "buff-zone";
            case ScanTaxonomy.Containers:    return "loot-generic";
            case ScanTaxonomy.Corpses:       return "loot-corpse";
            case ScanTaxonomy.Doors:         return "door";
            case ScanTaxonomy.Exits:         return "transition";
            case ScanTaxonomy.SearchPoints:  return "unknown";
            case ScanTaxonomy.Traps:         return "trap";
            case ScanTaxonomy.Mechanisms:    return "mechanism";
            case ScanTaxonomy.Scenery:       return null; // WA: scenery is silent
            default:                         return null; // unmapped → not pinged
        }
    }
}
