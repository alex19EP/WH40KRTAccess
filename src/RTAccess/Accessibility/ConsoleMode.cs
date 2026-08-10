using HarmonyLib;
using Kingmaker;

namespace RTAccess.Accessibility;

/// <summary>
/// Controller-mode policy. RTAccess no longer forces CONSOLE (gamepad) UI mode — the mod drives its own
/// parallel accessible UI tree in MOUSE mode (see [[ui-paradigm-pivot]] /
/// docs/plans/mirrored-surfacing-engelbart.md). But we keep using the game's own
/// <c>Game.ControllerOverride</c> lever, now forcing <b>Mouse</b>: that makes the startup flow select
/// mouse mode itself and, crucially, <b>skips the "press A for gamepad / Enter for keyboard" boot prompt
/// entirely</b> (an override being present suppresses both that prompt and the gamepad connect/disconnect
/// handler) — that prompt is inaccessible and would otherwise block a blind player at every launch.
///
/// The in-game helpers all run in mouse mode, no longer gated on <c>ControllerMode == Gamepad</c>:
/// <c>PartyHotkeys</c> gates on <c>InGameScreen.ExplorationActive</c>
/// (<c>CurrentMode == Default</c>), while the always-active tile cursor and scanner ride the Exploration
/// input category (live whenever <c>ControlState.HasControl</c>, so also during real-time Pause — move-to
/// is separately pause-guarded). The old F6 runtime console/mouse A/B toggle was removed with the
/// console-focus ride; forcing mouse at launch is the whole mechanism now.
/// </summary>
internal static class ConsoleMode
{
    /// <summary>
    /// Force mouse mode at launch (and skip the inaccessible controller-choice prompt). Code-level switch;
    /// can be surfaced in settings later. Off → stock behaviour (the boot prompt returns).
    /// </summary>
    internal static bool ForceMouseAtLaunch = true;

    /// <summary>
    /// Force <c>Game.ControllerOverride</c> to report Mouse so the startup flow selects mouse mode itself,
    /// before any UI is built, and the controller-choice boot prompt is skipped. The whole "mouse by
    /// default, no prompt" mechanism.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.ControllerOverride), MethodType.Getter)]
    internal static class ControllerOverridePatch
    {
        private static void Postfix(ref Game.ControllerModeType? __result)
        {
            if (ForceMouseAtLaunch) __result = Game.ControllerModeType.Mouse;
        }
    }

    /// <summary>
    /// Belt-and-suspenders: lock the controller mode after <c>Game.Initialize</c> so nothing reverts later.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.Initialize))]
    internal static class InitializeLockPatch
    {
        private static void Postfix()
        {
            if (ForceMouseAtLaunch) Game.DontChangeController = true;
        }
    }
}
