using Kingmaker;
using Kingmaker.GameModes;

namespace RTAccess
{
    /// <summary>
    /// Single source of truth for whether the player can act in the world. We use the GAME's own input
    /// gate: <c>Game.ClickEventsController</c> (the world-click <c>PointerController</c>) is assigned per
    /// game mode — set in Default/Pause, GlobalMap/Kingdom/Settlement and TacticalCombat, and left null in
    /// everything else (Cutscene, Dialog incl. storybook/interchapter, Rest, FullScreenUi menus, loading /
    /// None). So "the click controller exists" == "the player has control". One field read the game
    /// maintains on every mode change — no scene lookup, no flag conjunction to flicker or get stuck.
    /// </summary>
    internal static class ControlState
    {
        public static bool HasControl => Game.Instance?.ClickEventsController != null;

        /// <summary>
        /// The mode the player is actually IN, seeing through a pause. Pause is not a flag in this engine — it is
        /// a <see cref="GameModeType.Pause"/> game mode PUSHED onto <c>Game.m_GameModes</c> (a stack) while the
        /// mode underneath stays on it, so <c>Game.CurrentMode</c> (the stack's <c>Peek().Type</c>) reports
        /// <c>Pause</c> and every <c>CurrentMode == Default/StarSystem</c> test silently flips false the moment
        /// the player hits pause. That killed exploration on foot (the scanner, the tile cursor, party
        /// selection — everything riding <see cref="Screens.InGameScreen.ExplorationActive"/>) and the
        /// star-system verbs, even though the engine happily ACCEPTS orders while paused: the
        /// <c>UnitCommandBuffer</c> ticks in every mode and buffers the command, only
        /// <c>UnitCommandController</c> / <c>UnitMoveController</c> sit out Pause, so the order executes on
        /// unpause. Real-time-with-pause is the game's own contract — issuing orders while paused is the point.
        ///
        /// So: walk down from the top of the stack past any Pause layers and report the first real mode.
        /// Deliberately NOT <c>IsModeActive(x)</c>, which only asks "is x anywhere on the stack" and would call
        /// a dialogue layered over exploration "Default".
        /// </summary>
        public static GameModeType EffectiveMode
        {
            get
            {
                var game = Game.Instance;
                if (game == null) return GameModeType.None;
                try
                {
                    // Stack<T> enumerates top → bottom, which is exactly the order we want.
                    foreach (var mode in game.m_GameModes)
                        if (mode != null && mode.Type != GameModeType.Pause) return mode.Type;
                }
                catch { /* fall through to the plain read */ }
                return game.CurrentMode;
            }
        }

        /// <summary>True when <see cref="EffectiveMode"/> is <paramref name="type"/> — i.e. the player is in that
        /// mode, whether or not the game is paused on top of it.</summary>
        public static bool InMode(GameModeType type) => EffectiveMode == type;
    }
}
