using HarmonyLib;
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Announces a service window by name the moment it actually opens.
///
/// Hook: postfix on <c>ServiceWindowsVM.ShowWindow(ServiceWindowsType)</c> — the single point that runs only
/// after every availability/mode guard has passed and the window's VM is created. Because we announce on the
/// real open (not on a keypress), an unavailable window stays silent, and this covers every open path equally:
/// the keyboard (<see cref="WindowHotkeys"/>), the gamepad in-game-menu radial, and any in-game trigger.
/// The window's own first focused element is read afterwards via <see cref="SetFocusedPatch"/>, so this is the
/// short "which window" confirmation that precedes the content.
/// </summary>
[HarmonyPatch(typeof(ServiceWindowsVM), "ShowWindow")]
internal static class ServiceWindowAnnounce
{
    private static void Postfix(ServiceWindowsType type)
    {
        var label = Label(type);
        if (label != null) Speaker.Speak(label, interrupt: false);
    }

    private static string Label(ServiceWindowsType type) => type switch
    {
        ServiceWindowsType.Inventory => "Inventory",
        ServiceWindowsType.CharacterInfo => "Character",
        ServiceWindowsType.Journal => "Journal",
        ServiceWindowsType.LocalMap => "Map",
        ServiceWindowsType.Encyclopedia => "Encyclopedia",
        ServiceWindowsType.ShipCustomization => "Ship",
        ServiceWindowsType.ColonyManagement => "Colony",
        ServiceWindowsType.CargoManagement => "Cargo",
        _ => null,
    };
}
