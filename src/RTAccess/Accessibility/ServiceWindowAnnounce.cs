using HarmonyLib;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;
using RTAccess.Screens;   // ServiceWindowInfo (shared label/gate)
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Announces a service window by name the moment it actually opens.
///
/// Hook: postfix on <c>ServiceWindowsVM.ShowWindow(ServiceWindowsType)</c> — the single point that runs only
/// after every availability/mode guard has passed and the window's VM is created. Because we announce on the
/// real open (not on a keypress), an unavailable window stays silent, and this covers every open path equally:
/// the keyboard (<see cref="WindowHotkeys"/>), the gamepad in-game-menu radial, and any in-game trigger.
/// This is the short "which window" confirmation; the window's content is then read by its own screen in the
/// mod's parallel UI tree.
/// </summary>
[HarmonyPatch(typeof(ServiceWindowsVM), "ShowWindow")]
internal static class ServiceWindowAnnounce
{
    private static void Postfix(ServiceWindowsType type)
    {
        // Shared label with the HUD windows list / star-system openers (ServiceWindowInfo) — localized, and
        // covering Augmentations (which this announcer used to drop, opening it silently). Null for None/unknown
        // → stay silent.
        var label = ServiceWindowInfo.Label(type);
        if (label == null) return;
        // The user opened this window (a keypress), so interrupt to say which one it is. The window's own
        // screen then reads its content — giving "Inventory. <first item>." in order. Per [[rt-interrupt-speech-rule]].
        Speaker.Speak(label, interrupt: true);
    }
}
