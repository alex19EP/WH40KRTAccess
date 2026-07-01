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
/// <para><see cref="Update"/> is the raw-polled member selector (the gamepad-hold L2 selector's function): the
/// radial itself stays gamepad-only (per the user's decision), but the keyboard player gets the same outcome —
/// pick the active character and hear their name. It reuses the game's own documented combos (inactive in our tree,
/// like the I/C/J/M window keys): <c>Shift+D</c> / <c>Shift+A</c> = next / previous, <c>Alt+1..6</c> = select that
/// party slot directly. <c>Alt+digit</c> does not collide with console nav's <c>Alt+Arrow</c> mapping. Selection
/// goes through <c>Game.Instance.SelectionCharacter.SetSelected</c>.</para>
///
/// <para>The remaining handlers are REGISTERED <see cref="RTAccess.Input.InputCategory.Exploration"/> actions (see
/// <see cref="RTAccess.Input.InputBindings"/>), not raw-polled: <see cref="SelectAll"/> (Ctrl+A),
/// <see cref="Hold"/> (H), <see cref="Stop"/> (G), <see cref="CombatStatus"/> (R). The game's own Select-all /
/// Hold / Stop / status keys live in the PC HUD (dead in our tree), so these drive
/// <see cref="SelectionManagerBase"/> directly — the same calls the in-game menu's "Select whole party" /
/// "Hold position" / "Stop" buttons make — and are the sole handler (focus mode suppresses the game's duplicates).</para>
/// </summary>
internal static class PartyHotkeys
{
    public static void Update()
    {
        // Re-gated for the mouse-mode parallel UI: live whenever the InGameScreen owns the world (was
        // ControllerMode == Gamepad). Shift+D/A + Alt+1-6 don't collide with UI nav, so this runs whether or
        // not the HUD is focused; the game's own duplicate keys are suppressed by FocusMode (Keyboard.Disabled).
        if (!RTAccess.Screens.InGameScreen.ExplorationActive) return;

        bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
        bool alt = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);

        if (shift && UnityEngine.Input.GetKeyDown(KeyCode.D)) Step(next: true);
        else if (shift && UnityEngine.Input.GetKeyDown(KeyCode.A)) Step(next: false);
        else if (alt)
        {
            for (int i = 0; i < 6; i++)
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1 + i)) { SelectIndex(i); break; }
            }
        }
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
            // Key-driven selection — interrupt so stepping through the party stays responsive ([[rt-interrupt-speech-rule]]).
            Speaker.Speak(unit.CharacterName, interrupt: true);
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
            Speaker.Speak(n > 1 ? "Whole party selected, " + n + " characters." : "Party selected.", interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("PartyHotkeys.SelectAll failed: " + e); }
    }

    /// <summary>H — order the current selection to hold position (the menu's "Hold position" button).</summary>
    public static void Hold()
    {
        try { SelectionManagerBase.Instance?.Hold(); Speaker.Speak("Holding position.", interrupt: true); }
        catch (Exception e) { Main.Log?.Error("PartyHotkeys.Hold failed: " + e); }
    }

    /// <summary>G — stop the current selection's current orders (the menu's "Stop" button).</summary>
    public static void Stop()
    {
        try { SelectionManagerBase.Instance?.Stop(); Speaker.Speak("Stopped.", interrupt: true); }
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
            Speaker.Speak(string.IsNullOrWhiteSpace(line) ? "No status." : line, interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("PartyHotkeys.CombatStatus failed: " + e); }
    }
}
