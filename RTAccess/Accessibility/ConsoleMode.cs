using HarmonyLib;
using Kingmaker;
using RTAccess.Speech;

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
/// <c>F6</c> still flips the live mode at runtime (see <see cref="Main"/>) — for A/B-testing the parallel
/// tree against the game's console focus ring, and to reach the console-era in-game helpers (PartyHotkeys,
/// ExplorationNav, …) still gated on <c>ControllerMode == Gamepad</c> until rebuilt on the new paradigm.
/// </summary>
internal static class ConsoleMode
{
    /// <summary>
    /// Force mouse mode at launch (and skip the inaccessible controller-choice prompt). Code-level switch;
    /// can be surfaced in settings later. Off → stock behaviour (the boot prompt returns). F6 still toggles.
    /// </summary>
    internal static bool ForceMouseAtLaunch = true;

    /// <summary>
    /// Set the live controller mode at runtime, rebuilding the UI so it re-skins to the new mode. Used by
    /// the F6 toggle. Returns false if the game isn't ready yet.
    /// </summary>
    internal static bool SetMode(bool gamepad, bool resetUi)
    {
        var game = Game.Instance;
        if (game == null) return false;
        game.ControllerMode = gamepad ? Game.ControllerModeType.Gamepad : Game.ControllerModeType.Mouse;
        Game.DontChangeController = true; // don't let the game auto-switch back when no pad is attached
        FocusLog.Mark(gamepad ? "CONSOLE MODE ON" : "MOUSE MODE");
        if (resetUi) Game.ResetUI();
        return true;
    }

    /// <summary>F6 handler: flip between console and mouse mode at runtime.</summary>
    internal static void Toggle()
    {
        var game = Game.Instance;
        // F6 is key-driven, so interrupt ([[rt-interrupt-speech-rule]]). SetMode rebuilds the UI.
        if (game == null) { Speaker.Speak("Game not ready.", interrupt: true); return; }
        try
        {
            bool toGamepad = game.ControllerMode != Game.ControllerModeType.Gamepad;
            if (SetMode(toGamepad, resetUi: true))
                Speaker.Speak(toGamepad ? "Console UI mode." : "Mouse mode.", interrupt: true);
        }
        catch (Exception e)
        {
            Main.Log?.Error("ConsoleMode.Toggle failed: " + e);
            Speaker.Speak("Mode switch failed.", interrupt: true);
        }
    }

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
