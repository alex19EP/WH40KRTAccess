using HarmonyLib;
using Kingmaker.UI.InputSystems;
using RTAccess.Input;
using RTAccess.Screens;

namespace RTAccess.Accessibility;

/// <summary>
/// The heart of the "merge, don't own" input model. RT's keyboard hotkeys run through
/// <see cref="KeyboardAccess"/> (itself a raw-<c>UnityEngine.Input</c> poller); each frame it dispatches every
/// bound shortcut via <c>OnCallbackByBinding</c>. This prefix arbitrates that dispatch against the mod's own
/// per-frame claim set:
///
/// <list type="bullet">
/// <item>Focus mode OFF → let the game handle everything (fully vanilla keyboard).</item>
/// <item>A modal (Exclusive) mod screen is up → suppress ALL game keys (the mod owns the keyboard).</item>
/// <item>Otherwise → suppress a game shortcut ONLY if the mod actively claims that exact chord this frame
/// (<see cref="InputManager.ClaimsChord"/>); every un-overridden game key stays live.</item>
/// </list>
///
/// This replaces the old blanket <c>KeyboardAccess.Disabled</c> mute, so the game's own bindings (action bar,
/// save/load, End-turn on Space out in the world, the Ctrl+letter window openers the mod moved there via
/// <see cref="GameKeybinds"/>, in-game tutorials/hints) keep working, and the mod overrides only the subset it
/// genuinely needs. See docs/input-system-architecture-review.md and the <c>rt-input-system-verdict</c> memory.
/// </summary>
[HarmonyPatch(typeof(KeyboardAccess), "OnCallbackByBinding", new[] { typeof(KeyboardAccess.Binding), typeof(bool) })]
internal static class KeyboardArbitration
{
    // Return false to skip the game's callback (suppress this chord this frame).
    private static bool Prefix(KeyboardAccess.Binding binding)
    {
        try
        {
            if (binding == null) return true;
            if (!FocusMode.Active) return true;              // mod dormant → vanilla game keyboard
            if (ScreenManager.ExclusiveActive) return false; // a modal mod screen owns the whole keyboard
            // Per-chord: the mod wins the keys it claims this frame; the game keeps everything else.
            return !InputManager.ClaimsChord(binding.Key, binding.IsCtrlDown, binding.IsAltDown, binding.IsShiftDown);
        }
        catch (System.Exception e)
        {
            // Never brick the game keyboard on a bug in here — fail open (let the game handle the key).
            Main.Log?.Log("KeyboardArbitration prefix error: " + e.Message);
            return true;
        }
    }
}
