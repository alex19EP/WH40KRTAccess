using System;
using Kingmaker;
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Opens service windows by keyboard while in console (gamepad) UI mode, by REUSING the game's own keyboard
/// handlers so every engine guard stays in effect.
///
/// The game already binds these window keys in the mode-agnostic <c>ServiceWindowsVM.BindKeys</c> (via
/// <c>KeyboardAccess</c>, which is ticked every frame regardless of UI mode), and those callbacks carry the
/// availability guards — e.g. "OpenShipCustomization" is only bound when <c>CanAccessStarshipInventory</c>,
/// and the colony callback checks <c>!ForbidColonization</c>. So instead of raising the raw EventBus open
/// (which bypasses those guards and would "open" an unavailable window), we invoke the game's bound callback
/// for the matching binding name. When a window is unavailable its callback isn't bound (or no-ops), so the
/// keypress does nothing — and nothing is announced (announcement is on the real open, see
/// <see cref="ServiceWindowAnnounce"/>).
///
/// Re-invoking is safe even though the game also ticks the same binding: the first open sets the window as
/// current, and a second open of the already-current window early-returns without re-showing or toggling.
///
/// Level-up has no game keyboard binding to reuse, so it is intentionally not bound here — it is reachable
/// through the Character screen (C). Gated to console mode so it never doubles stock hotkeys in mouse mode.
/// </summary>
internal static class WindowHotkeys
{
    public static void Update()
    {
        var game = Game.Instance;
        if (game == null || game.ControllerMode != Game.ControllerModeType.Gamepad) return;

        // Ctrl+<letter> is reserved for other features (e.g. Ctrl+I = read details) — don't open windows on it.
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;

        if (Input.GetKeyDown(KeyCode.I)) Fire("OpenInventory");
        else if (Input.GetKeyDown(KeyCode.C)) Fire("OpenCharacterScreen");
        else if (Input.GetKeyDown(KeyCode.J)) Fire("OpenJournal");
        else if (Input.GetKeyDown(KeyCode.M)) Fire("OpenMap");
        else if (Input.GetKeyDown(KeyCode.L)) Fire("OpenEncyclopedia");
        else if (Input.GetKeyDown(KeyCode.Y)) Fire("OpenColonyManagement");
        else if (Input.GetKeyDown(KeyCode.V)) Fire("OpenShipCustomization");
        else if (Input.GetKeyDown(KeyCode.B)) Fire("OpenCargoManagement");
    }

    /// <summary>Invoke the game's own bound keyboard callback(s) for a binding name (the guarded open path).</summary>
    private static void Fire(string bindingName)
    {
        try
        {
            var keyboard = Game.Instance?.Keyboard;
            // m_BindingCallbacks only contains an entry when the game bound that action — i.e. when the
            // window is available. Absent/empty => the engine's guard says "no" => do nothing.
            if (keyboard != null && keyboard.m_BindingCallbacks.TryGetValue(bindingName, out var callbacks))
            {
                foreach (var callback in callbacks.ToArray()) callback();
            }
        }
        catch (Exception e)
        {
            Main.Log?.Error("WindowHotkeys.Fire(" + bindingName + ") failed: " + e);
        }
    }
}
