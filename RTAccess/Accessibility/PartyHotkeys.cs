using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.Common;
using Kingmaker.UI.Selection;      // SelectionManagerBase (SelectAll / Hold / Stop)
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Keyboard party control while the in-game screen owns the world — the keyboard equivalents of HUD/gamepad party
/// functions that our parallel UI tree leaves without a live handler.
///
/// <para>All keyboard party functions are REGISTERED <see cref="RTAccess.Input.InputCategory.Exploration"/>
/// actions (see <see cref="RTAccess.Input.InputBindings"/>), not raw-polled. Member selection —
/// <see cref="MemberNext"/> (Shift+D) / <see cref="MemberPrev"/> (Shift+A) / <see cref="SelectMember"/> (Alt+1..6)
/// — reuses the game's own documented combos and speaks the picked name; registering it (rather than raw-polling)
/// is what lets the keyboard-arbitration patch claim those chords and suppress the game's Prev/NextCharacter +
/// SelectCharacter so they don't double-fire. Party orders — <see cref="SelectAll"/> (Ctrl+A), <see cref="Hold"/>
/// (H), <see cref="Stop"/> (G) — and the <see cref="CombatStatus"/> (R) readout drive
/// <see cref="SelectionManagerBase"/> / the HUD status directly, the same calls the in-game menu buttons make.
/// The mod claims all these chords, so its handler is the sole responder.</para>
/// </summary>
internal static class PartyHotkeys
{
    // ---- registered member selector (InputCategory.Exploration; see InputBindings) ----
    // The radial itself stays gamepad-only (per the user's decision), but the keyboard player gets the same
    // outcome — pick the active character and hear their name.

    // The same chords also serve the service windows: Inventory / Character Info sit non-Exclusive over
    // InGameScreen, so these Exploration bindings stay live there (and CLAIMED — the game's own
    // Prev/NextCharacter binds are suppressed); the window branch drives the game's own viewed-unit
    // switch instead, and ViewedCharacter.Tick speaks the result.

    /// <summary>Shift+D — the next party member: the world selection out in exploration, the VIEWED
    /// character inside a service window.</summary>
    public static void MemberNext()
    {
        if (RTAccess.Screens.InGameScreen.ExplorationActive) Step(next: true);
        else if (ViewedCharacter.WindowActive) ViewedCharacter.SwitchMember(next: true);
    }

    /// <summary>Shift+A — the previous party member (see <see cref="MemberNext"/>).</summary>
    public static void MemberPrev()
    {
        if (RTAccess.Screens.InGameScreen.ExplorationActive) Step(next: false);
        else if (ViewedCharacter.WindowActive) ViewedCharacter.SwitchMember(next: false);
    }

    /// <summary>Alt+1..6 — select that party slot directly (index is 0-based); in a service window,
    /// shows that roster slot instead.</summary>
    public static void SelectMember(int index)
    {
        if (RTAccess.Screens.InGameScreen.ExplorationActive) SelectIndex(index);
        else if (ViewedCharacter.WindowActive) ViewedCharacter.SwitchTo(index);
    }

    /// <summary>The directly-controllable party members, in party order — the selectable set.</summary>
    private static List<BaseUnitEntity> Controllable()
    {
        var party = Game.Instance?.Player?.Party;
        if (party == null) return null;
        return party.Where(u => u != null && u.IsDirectlyControllable()).ToList();
    }

    private static BaseUnitEntity Current()
    {
        var sel = Game.Instance.SelectionCharacter;
        return sel.SelectedUnit.Value ?? sel.SelectedUnitInUI.Value ?? sel.FirstSelectedUnit;
    }

    private static void Step(bool next)
    {
        var list = Controllable();
        if (list == null || list.Count == 0) return;
        int i = list.IndexOf(Current());
        if (i < 0) i = next ? -1 : 0; // unknown current → start at the first/last on next/prev
        int target = ((i + (next ? 1 : -1)) % list.Count + list.Count) % list.Count;
        Select(list[target]);
    }

    private static void SelectIndex(int index)
    {
        var list = Controllable();
        if (list == null || index < 0 || index >= list.Count) return;
        Select(list[index]);
    }

    private static void Select(BaseUnitEntity unit)
    {
        if (unit == null) return;
        try
        {
            Game.Instance.SelectionCharacter.SetSelected(unit);
            // Route the confirmation through the one selection-announce path (force → always speaks, and marks the
            // unit so the per-frame poll doesn't echo it). See SelectionAnnouncer.
            SelectionAnnouncer.Announce(unit, force: true);
        }
        catch (Exception e)
        {
            Main.Log?.Error("PartyHotkeys.Select failed: " + e);
        }
    }

    // ---- registered handlers (InputCategory.Exploration; see InputBindings) ----

    /// <summary>
    /// Ctrl+A — restore the WHOLE party to the selection. This is the one-press way back to a formation move-to
    /// after <see cref="Select"/> / <see cref="Step"/> (Alt+1..6, Shift+A/D) collapsed the selection to a single
    /// member: <see cref="TileExplorer.MoveToCursor"/>'s real-time branch walks every selected unit, so select-all
    /// is what turns Backspace into a party move rather than a single-character one. Ctrl+A is free for this because
    /// focus mode suppresses the game's own Ctrl+A and the mod's focus toggle is Ctrl+Shift+A.
    /// </summary>
    public static void SelectAll()
    {
        try
        {
            SelectionManagerBase.Instance?.SelectAll();
            int n = Game.Instance?.SelectionCharacter?.SelectedUnits?.Count ?? 0;
            Speaker.Speak(n > 1 ? Loc.T("party.selected_all", new { count = n }) : Loc.T("party.selected_all_one"), interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("PartyHotkeys.SelectAll failed: " + e); }
    }

    /// <summary>H — order the current selection to hold position (the menu's "Hold position" button).</summary>
    public static void Hold()
    {
        try { SelectionManagerBase.Instance?.Hold(); Speaker.Speak(Loc.T("party.holding"), interrupt: true); }
        catch (Exception e) { Main.Log?.Error("PartyHotkeys.Hold failed: " + e); }
    }

    /// <summary>G — stop the current selection's current orders (the menu's "Stop" button).</summary>
    public static void Stop()
    {
        try { SelectionManagerBase.Instance?.Stop(); Speaker.Speak(Loc.T("party.stopped"), interrupt: true); }
        catch (Exception e) { Main.Log?.Error("PartyHotkeys.Stop failed: " + e); }
    }

    /// <summary>
    /// R — one-press status readout of the selected / acting character: name, wounds, and in turn-based combat the
    /// AP / MP / whose-turn tail, without a trip through HUD focus. Speaks the same line the in-game HUD status
    /// element shows (<see cref="RTAccess.Screens.InGameScreen.StatusLine"/>).
    /// </summary>
    public static void CombatStatus()
    {
        try
        {
            var line = RTAccess.Screens.InGameScreen.StatusLine();
            Speaker.Speak(string.IsNullOrWhiteSpace(line) ? Loc.T("status.none") : line, interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("PartyHotkeys.CombatStatus failed: " + e); }
    }
}
