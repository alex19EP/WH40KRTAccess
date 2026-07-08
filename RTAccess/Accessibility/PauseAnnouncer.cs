using System;
using Kingmaker;                       // Game
using Kingmaker.Blueprints.Root.Strings; // UIStrings (CommonTexts.Paused)
using RTAccess.Localization;           // GameText
using RTAccess.Speech;                 // Speaker
using UnityEngine;                     // Time

namespace RTAccess.Accessibility;

/// <summary>
/// Narrates every pause / unpause EDGE the sighted PAUSED banner shows (main-HUD audit #5) — previously the
/// mod voiced only its own HUD Pause button. The state mirrored is the banner's own gate,
/// <c>Game.IsPaused &amp;&amp; !PauseController.IsPausedByPlayers</c> (<c>PauseNotificationVM.IsPaused</c>):
/// that covers the Space pause, the trap/hidden-object, lost-focus and area-load autopauses, scripted
/// pauses, and the silent force-UNpause on turn-based-combat entry — while excluding the per-window UI
/// pauses (inventory / character sheet / settings / formation all <c>RequestPauseUi</c>, which routes into
/// the player-group pause the banner deliberately does NOT show; announcing those would say "Paused" on
/// every window open — review finding). Unpausing is a silent banner fade even for sighted players, so the
/// spoken "unpaused" is the parity floor, not extra. Passive state change → queued speech, never
/// interrupting. The mod's own <c>TogglePause</c> speaks its own keypress-provenance line and marks the
/// specific edge it caused as already spoken (<see cref="SuppressNext"/>).
/// </summary>
internal static class PauseAnnouncer
{
    private static bool? _last;          // null = no baseline yet (the first tick after a load never announces)
    private static float _suppressUntil; // one matching edge before this time was already spoken by its cause
    private static bool _suppressState;  // ...and only an edge INTO this state is the one to swallow

    /// <summary>The caller is flipping the pause state to <paramref name="pausedState"/> AND speaking its
    /// own confirmation — swallow that one matching edge (time-bounded, since a queued game command can
    /// apply a frame or two later; direction-checked and consumed on use, so it can never eat a different
    /// later edge — review finding).</summary>
    public static void SuppressNext(bool pausedState)
    {
        _suppressUntil = Time.unscaledTime + 0.5f;
        _suppressState = pausedState;
    }

    public static void Tick()
    {
        try
        {
            var g = Game.Instance;
            if (g == null || g.CurrentlyLoadedArea == null) { _last = null; return; }
            // The banner's exact state (PauseNotificationVM.IsPaused): paused, but not by the player-group
            // channel every UI window's RequestPauseUi rides.
            bool paused = g.IsPaused && !(g.PauseController?.IsPausedByPlayers ?? false);
            if (_last == null) { _last = paused; return; }
            if (paused == _last.Value) return;
            _last = paused;
            if (Time.unscaledTime < _suppressUntil && paused == _suppressState)
            {
                _suppressUntil = 0f; // consumed — a later genuine edge is never swallowed
                return;
            }
            // The paused word is the game's own banner text when available; unpause has no sighted string.
            Speaker.Speak(paused
                ? GameText.Or(() => UIStrings.Instance.CommonTexts.Paused, "pause.paused")
                : Loc.T("pause.unpaused"), interrupt: false);
        }
        catch (Exception e) { Main.Log?.Error("PauseAnnouncer.Tick failed: " + e); }
    }
}
