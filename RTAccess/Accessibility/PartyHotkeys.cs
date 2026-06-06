using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.Common;
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Keyboard character selection while in console (gamepad) UI mode — the function the gamepad-hold party
/// selector (L2) provides. The radial itself stays gamepad-only (per the user's decision); this gives the
/// keyboard player the same outcome: pick the active character and hear their name.
///
/// Reuses the game's own documented combos (inactive in console mode, like the I/C/J/M window keys):
/// <c>Shift+D</c> / <c>Shift+A</c> = next / previous (PrevCharacter/NextCharacter), <c>Alt+1..6</c> = select
/// that party slot directly (SelectCharacter[n]). <c>Alt+digit</c> does not collide with console nav's
/// <c>Alt+Arrow</c> right-stick mapping. Selection goes through
/// <c>Game.Instance.SelectionCharacter.SetSelected</c>; the chosen unit's name is spoken.
/// </summary>
internal static class PartyHotkeys
{
    public static void Update()
    {
        var game = Game.Instance;
        if (game == null || game.ControllerMode != Game.ControllerModeType.Gamepad) return;

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        if (shift && Input.GetKeyDown(KeyCode.D)) Step(next: true);
        else if (shift && Input.GetKeyDown(KeyCode.A)) Step(next: false);
        else if (alt)
        {
            for (int i = 0; i < 6; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { SelectIndex(i); break; }
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
            Speaker.Speak(unit.CharacterName, interrupt: false);
        }
        catch (Exception e)
        {
            Main.Log?.Error("PartyHotkeys.Select failed: " + e);
        }
    }
}
