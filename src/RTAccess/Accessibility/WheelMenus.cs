using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints.Root.Strings;             // UIStrings (reuse the game's Formation label)
using Kingmaker.Code.UI.MVVM.View.Formation.Console;
using Kingmaker.Code.UI.MVVM.View.IngameMenu.Console;
using Kingmaker.Code.UI.MVVM.View.Party.Console;
using Kingmaker.UI.MVVM.View.GroupChanger.Console;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Announces RT's radial/"wheel" menus when they open (name + entry count).
///
/// Per-entry reading — the highlighted character/option as the selection moves — was previously supplied by
/// the console focus reader, which rode the game's console navigation and was removed with the rest of the
/// console-nav path. It is not currently reimplemented in the parallel UI tree, so only the open announcement
/// is spoken here.
///
/// The two hold-radials (party selector = L2, in-game menu = R2) bind their items in
/// <c>BindViewImplementation</c> BEFORE <c>CreateNavigation</c>, so we announce in a <c>CreateNavigation</c>
/// prefix — that runs after the items exist but before the first focus, so "name + count" is spoken as the
/// wheel opens. The contextual group-changer and formation windows are announced from a
/// <c>BindViewImplementation</c> postfix. All are key-driven, so the announcement interrupts (per
/// [[rt-interrupt-speech-rule]]).
/// </summary>
internal static class WheelMenus
{
    // isCharacter picks the counted noun (character[s] vs option[s]); pluralization is a per-language
    // singular/plural key pair, matching the mod's existing party.selected_all[_one] pattern.
    private static void Open(string name, int count, bool isCharacter)
    {
        string noun = isCharacter
            ? Loc.T(count == 1 ? "wheel.character" : "wheel.characters")
            : Loc.T(count == 1 ? "wheel.option" : "wheel.options");
        Speaker.Speak(Loc.T("wheel.count", new { name, count, noun }), interrupt: true);
    }

    [HarmonyPatch(typeof(PartySelectorConsoleView), "CreateNavigation")]
    private static class PartySelectorPatch
    {
        private static void Prefix(PartySelectorConsoleView __instance)
        {
            try { Open(Loc.T("wheel.party_selector"), __instance.m_Characters.Count(c => c != null && c.IsBinded), isCharacter: true); }
            catch (Exception e) { Main.Log?.Log("party selector announce failed: " + e.Message); }
        }
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
                Open(GameText.Or(() => UIStrings.Instance.CommonTexts.Menu, "hud.menu"), count, isCharacter: false);
            }
            catch (Exception e) { Main.Log?.Log("ingame menu announce failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GroupChangerConsoleView), "BindViewImplementation")]
    private static class GroupChangerPatch
    {
        private static void Postfix(GroupChangerConsoleView __instance)
        {
            try { Open(Loc.T("wheel.group_changer"), __instance.RemoteCharacterViews.Count, isCharacter: true); }
            catch (Exception e) { Main.Log?.Log("group changer announce failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(FormationConsoleView), "BindViewImplementation")]
    private static class FormationPatch
    {
        private static void Postfix(FormationConsoleView __instance)
        {
            try { Open(GameText.Or(() => UIStrings.Instance.FormationTexts.FormationLabel, "hudmenu.formation"), __instance.m_Characters.Count, isCharacter: true); }
            catch (Exception e) { Main.Log?.Log("formation announce failed: " + e.Message); }
        }
    }
}
