using System.Linq;
using HarmonyLib;
using Kingmaker.Code.UI.MVVM.View.Formation.Console;
using Kingmaker.Code.UI.MVVM.View.IngameMenu.Console;
using Kingmaker.Code.UI.MVVM.View.Party.Console;
using Kingmaker.UI.MVVM.View.GroupChanger.Console;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Announces RT's radial/"wheel" menus when they open (name + entry count), and marks one as active so the
/// focus reader can suppress the in-game menu's unlabeled centre-placeholder.
///
/// Per-entry reading needs NO code here: every wheel moves focus through
/// <c>ConsoleNavigationBehaviour.FocusOnEntity → entity.SetFocused(true)</c>, the same
/// <c>ConsoleEntityExtensions.SetFocused</c> choke point <see cref="SetFocusedPatch"/> already hooks, so each
/// highlighted character/option is spoken via <see cref="UiTextReader"/> as the gamepad stick moves.
///
/// The two hold-radials (party selector = L2, in-game menu = R2) bind their items in
/// <c>BindViewImplementation</c> BEFORE <c>CreateNavigation</c>, so we announce in a <c>CreateNavigation</c>
/// prefix — that runs after the items exist but before the first focus, so "name + count" is spoken ahead of
/// the first entry; the label interrupts (the user opened the wheel) and the first entry, being automatic focus
/// rather than a directional move, is queued behind it by <see cref="SetFocusedPatch"/>, so the label is heard
/// in full. The contextual group-changer and formation windows are announced from a <c>BindViewImplementation</c> postfix.
/// </summary>
internal static class WheelMenus
{
    /// <summary>Friendly name of the currently-open wheel, or null. Read by <see cref="SetFocusedPatch"/>.</summary>
    internal static string ActiveWheel;

    private static void Open(string name, int count, string noun)
    {
        ActiveWheel = name;
        var counted = count == 1 ? noun : noun + "s"; // "1 character" / "5 characters"
        // The user opened this wheel (a keypress), so interrupt to announce it. The first entry's focus readout
        // follows and SetFocusedPatch queues it (opening isn't a directional nav, so it's automatic focus) —
        // giving "Party selector, 5 characters. <first entry>." in order. Per [[rt-interrupt-speech-rule]].
        Speaker.Speak($"{name}, {count} {counted}", interrupt: true);
    }

    private static void Close(string name)
    {
        // Only clear if this wheel is the one we marked (wheels are mutually exclusive, but be defensive).
        if (ActiveWheel == name) ActiveWheel = null;
    }

    private const string PartyName = "Party selector";
    private const string MenuName = "Menu";
    private const string GroupName = "Group changer";
    private const string FormationName = "Formation";

    [HarmonyPatch(typeof(PartySelectorConsoleView), "CreateNavigation")]
    private static class PartySelectorPatch
    {
        private static void Prefix(PartySelectorConsoleView __instance)
        {
            try { Open(PartyName, __instance.m_Characters.Count(c => c != null && c.IsBinded), "character"); }
            catch (Exception e) { Main.Log?.Log("party selector announce failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(PartySelectorConsoleView), "DestroyViewImplementation")]
    private static class PartySelectorClosePatch
    {
        private static void Postfix() => Close(PartyName);
    }

    [HarmonyPatch(typeof(IngameMenuConsoleView), "CreateNavigation")]
    private static class IngameMenuPatch
    {
        private static void Prefix(IngameMenuConsoleView __instance)
        {
            try
            {
                // The same active-only query the method itself uses to build the navigable options.
                var count = __instance.m_Content.GetComponentsInChildren<IngameMenuItemConsoleView>(includeInactive: false).Length;
                Open(MenuName, count, "option");
            }
            catch (Exception e) { Main.Log?.Log("ingame menu announce failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(IngameMenuConsoleView), "DestroyViewImplementation")]
    private static class IngameMenuClosePatch
    {
        private static void Postfix() => Close(MenuName);
    }

    [HarmonyPatch(typeof(GroupChangerConsoleView), "BindViewImplementation")]
    private static class GroupChangerPatch
    {
        private static void Postfix(GroupChangerConsoleView __instance)
        {
            try { Open(GroupName, __instance.RemoteCharacterViews.Count, "character"); }
            catch (Exception e) { Main.Log?.Log("group changer announce failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GroupChangerConsoleView), "DestroyViewImplementation")]
    private static class GroupChangerClosePatch
    {
        private static void Postfix() => Close(GroupName);
    }

    [HarmonyPatch(typeof(FormationConsoleView), "BindViewImplementation")]
    private static class FormationPatch
    {
        private static void Postfix(FormationConsoleView __instance)
        {
            try { Open(FormationName, __instance.m_Characters.Count, "character"); }
            catch (Exception e) { Main.Log?.Log("formation announce failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(FormationConsoleView), "DestroyViewImplementation")]
    private static class FormationClosePatch
    {
        private static void Postfix() => Close(FormationName);
    }
}
