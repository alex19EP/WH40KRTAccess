using HarmonyLib;
using Kingmaker;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Puts the game into console (gamepad) UI mode — the mode in which the focus-navigation system, and
/// therefore our <see cref="SetFocusedPatch"/> reader, is active. Mouse mode raises OnHover (not OnFocus),
/// so nothing is announced there.
///
/// Enabled by default at launch via the game's OWN supported lever: <c>Game.ControllerOverride</c>. The
/// startup flow (<c>GameStarter.StartGameCoroutine</c> / <c>Game.Initialize</c>) honours that override
/// before it would otherwise fall back to mouse when no controller is connected, so the entire UI builds
/// in console mode from the start — no UI rebuild, no flash. It also means <c>GamepadConnectDisconnectVM</c>
/// never subscribes to controller connect/disconnect (it skips that when an override is present), so there
/// is no auto-revert to mouse and no "gamepad disconnected" prompt when running without a physical pad,
/// and the "choose controller mode" prompt is skipped entirely.
///
/// F6 still flips the live mode at runtime (see <see cref="Main"/>); that path needs a UI rebuild because
/// the UI is already built by then.
/// </summary>
internal static class ConsoleMode
{
    /// <summary>
    /// Whether the mod forces console UI mode automatically when the game launches. Code-level switch for
    /// now; can be surfaced in the UnityModManager settings UI later. Turning it off restores the game's
    /// stock controller-mode behaviour (F6 still works to toggle manually).
    /// </summary>
    internal static bool EnableByDefault = true;

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
        if (game == null) { Speaker.Speak("Game not ready.", interrupt: false); return; }
        try
        {
            bool toGamepad = game.ControllerMode != Game.ControllerModeType.Gamepad;
            if (SetMode(toGamepad, resetUi: true))
                Speaker.Speak(toGamepad ? "Console UI mode." : "Mouse mode.", interrupt: false);
        }
        catch (Exception e)
        {
            Main.Log?.Error("ConsoleMode.Toggle failed: " + e);
            Speaker.Speak("Mode switch failed.", interrupt: false);
        }
    }

    /// <summary>
    /// Force <c>Game.ControllerOverride</c> to report Gamepad so the startup flow selects console mode
    /// itself, before any UI is built. This is the whole "console UI by default" mechanism.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.ControllerOverride), MethodType.Getter)]
    internal static class ControllerOverridePatch
    {
        private static void Postfix(ref Game.ControllerModeType? __result)
        {
            if (EnableByDefault) __result = Game.ControllerModeType.Gamepad;
        }
    }

    /// <summary>
    /// Belt-and-suspenders: lock the controller mode after <c>Game.Initialize</c> so nothing reverts to
    /// mouse later. (With the override above, the connect/disconnect handler isn't even subscribed, but
    /// this keeps the intent explicit and survives if the override path is ever disabled mid-session.)
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.Initialize))]
    internal static class InitializeLockPatch
    {
        private static void Postfix()
        {
            if (EnableByDefault) Game.DontChangeController = true;
        }
    }
}
