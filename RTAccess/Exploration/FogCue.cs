using RTAccess.Audio;    // Earcons (FogEnter / FogExit)
using RTAccess.Screens;  // InGameScreen.ExplorationActive
using RTAccess.Settings; // BoolSetting
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// A one-shot cue when the exploration cursor crosses the fog-of-war boundary — the edge of the party's CURRENT
/// sight. An "enter fog" tone plays when the cursor moves out of the visible area (into remembered / never-seen
/// fog); an "exit fog" tone when it moves back into the party's live sight. Ported from WrathAccess's
/// <c>FogSystem</c> overlay: RT reads the boundary from <see cref="FogProbe"/> (green = ever-explored, red =
/// currently-visible) instead of WotR's CPU-side <c>FogOfWarController.IsInFogOfWar</c>, and voices it through the
/// synthesized <see cref="Earcons.FogEnter"/> / <see cref="Earcons.FogExit"/> cues, whose pitch and length are
/// matched to WA's <c>fog_enter</c> / <c>fog_exit</c> wavs so it sounds the same while shipping no WAV asset.
///
/// Fog IS the party's live line of sight, so the cue is inherently visual-parity-safe (see the rt-visual-parity
/// note): it never reveals anything a sighted player's fog overlay doesn't already show. "Fogged" here means NOT
/// currently visible — a <see cref="FogProbe.FogState.Explored"/> (remembered, now dark) or
/// <see cref="FogProbe.FogState.NeverSeen"/> tile both count as OUTSIDE the party's sight; only
/// <see cref="FogProbe.FogState.Visible"/> (and the fog-off / off-map <see cref="FogProbe.FogState.NoFow"/>) count
/// as inside it — matching WA, where no fog ⇒ never fogged ⇒ no cue.
///
/// ON by default (<c>exploration.fog_cue</c>): unlike the ambient sonar / wall tones, the boundary cue is a brief
/// discrete event, not a continuous bed, so it doesn't need the maintainer's ear-tuning pass before shipping. No
/// keybind — it is automatic; disable it from the settings screen. Driven directly from <c>Main.OnUpdate</c>.
/// </summary>
internal static class FogCue
{
    // RT's fog read is a synchronous 1×1 GPU readback (FogProbe.Classify) that forces a render-thread sync — unlike
    // WotR's cheap CPU-side query, so we must NOT poll it continuously (a default-on continuous stall would risk
    // periodic micro-stutter, worse for a screen-reader user if it clips in-flight speech). We sample ONLY when the
    // planted cursor has actually MOVED; because the cursor steps tile-by-tile, that makes the read keypress-driven
    // — exactly the usage FogProbe is built for and how its other callers (Scanner / InteractableDescriber) touch
    // it. SampleSec just caps the rate should moves ever land every frame. Trade-off vs WA (whose CPU query samples
    // free every frame): the boundary sweeping over a PARKED cursor as the party advances is not cued — an
    // acceptable loss to keep the readback event-driven.
    private const float SampleSec = 0.1f;

    private static bool? _wasFogged;  // null = no baseline yet (don't fire on the first sample after (re)activation)
    private static Vector3 _lastPos;  // last sampled cursor position — the movement gate
    private static bool _haveLast;
    private static float _timer;

    private static bool Enabled => ModSettings.GetSetting<BoolSetting>("exploration.fog_cue")?.Get() ?? true;

    /// <summary>Per-frame: when the planted cursor moves, sample its fog state (rate-capped) and cue on a crossing.
    /// Gated on exploration control + the toggle; the baseline resets when inactive or unplanted so re-entering
    /// exploration never fires a spurious cue. Never throws out of the update loop.</summary>
    public static void Tick(float dt)
    {
        try
        {
            if (!Enabled || !InGameScreen.ExplorationActive || !ControlState.HasControl)
            { _wasFogged = null; _haveLast = false; return; }

            _timer -= dt;

            // Skip the GPU readback unless the cursor is planted AND moved: unplanted it tracks the party (always in
            // sight → nothing to cue), and a stationary planted cursor cannot have crossed the boundary.
            if (!MapCursor.Has) { _wasFogged = null; _haveLast = false; return; }
            var pos = MapCursor.Position;
            if (_haveLast && (pos - _lastPos).sqrMagnitude < 1e-4f) return; // cursor hasn't moved — no readback
            if (_timer > 0f) return;                                        // moved, but cap the sample rate
            _timer = SampleSec;
            _lastPos = pos; _haveLast = true;

            var state = FogProbe.Classify(pos);
            bool fogged = state == FogProbe.FogState.NeverSeen || state == FogProbe.FogState.Explored;

            if (_wasFogged.HasValue && fogged != _wasFogged.Value)
            {
                if (fogged) Earcons.FogEnter(); // crossed OUT of the party's sight (into fog)
                else Earcons.FogExit();         // crossed INTO the party's sight
            }
            _wasFogged = fogged;
        }
        catch (Exception e) { Main.Log?.Error("FogCue.Tick failed: " + e); }
    }
}
