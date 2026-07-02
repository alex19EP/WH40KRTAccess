using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using RTAccess.Speech;

namespace RTAccess.Accessibility
{
    /// <summary>
    /// Announces the CURRENTLY-CONTROLLED unit whenever the game's primary selection changes — covering the
    /// changes the explicit keyboard paths don't already speak: a mouse click on a portrait/unit, or the game
    /// re-selecting a unit on its own (after a command, or when the selected unit becomes uncontrollable). A blind
    /// player otherwise has no cue that "who my next command targets" changed under them.
    ///
    /// <para>ONE announce path for the whole mod. The explicit keyboard/HUD selectors route their confirmation
    /// through <see cref="Announce"/> with force=true; <see cref="Tick"/> polls
    /// <c>SelectionCharacter.SelectedUnit</c> once per frame with force=false. A shared last-unit guard dedupes the
    /// two (the poll sees the unit the keyboard just set and stays silent). Polled (force=false) announces are
    /// SUPPRESSED in turn-based combat: the engine unselects then reselects the acting unit every turn
    /// (<c>SelectionManagerBase.HandleUnitStartTurn</c>), which would echo the turn cue CombatEvents already
    /// speaks. Explicit selects (force=true) always speak, in or out of combat, so a key/click never goes silent
    /// even when it re-picks the already-selected unit.</para>
    /// </summary>
    internal static class SelectionAnnouncer
    {
        private static BaseUnitEntity _last;

        /// <summary>Poll the live primary selection once per frame (from Main.OnUpdate); a no-op when unchanged.</summary>
        public static void Tick() => Announce(Game.Instance?.SelectionCharacter?.SelectedUnit?.Value, force: false);

        /// <summary>
        /// Speak the newly-selected unit's name. force=true is an explicit keyboard/HUD selection: always spoken
        /// (even re-selecting the same unit, even in turn-based combat). force=false is the poll / a game-driven
        /// change: deduped against the last announced unit, and silenced in turn-based combat (tracked so it isn't
        /// spoken later either). A null unit (deselect) resets the guard silently.
        /// </summary>
        public static void Announce(BaseUnitEntity unit, bool force)
        {
            if (unit == null) { _last = null; return; }
            if (!force)
            {
                if (ReferenceEquals(unit, _last)) return;                                   // already announced
                if (Game.Instance?.TurnController?.TurnBasedModeActive ?? false) { _last = unit; return; }
            }
            _last = unit;
            // Interrupt ONLY for an explicit selector press (force) — that's a keypress the player expects instant
            // feedback for. A polled/game-driven change (force=false: unit death, area load, combat auto-reselect)
            // is automatic, so it QUEUES behind passive lines rather than purging them ([[rt-interrupt-speech-rule]]).
            Speaker.Speak(unit.CharacterName, interrupt: force);
        }
    }
}
