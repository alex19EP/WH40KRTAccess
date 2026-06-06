using Kingmaker;
using Kingmaker.Code.UI.MVVM.View.Surface.InputLayers;
using Kingmaker.GameModes;
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Keyboard navigation of nearby world interactables while in console (gamepad) UI mode. The game already
/// finds, fog-filters, sorts and cycles interactables through <see cref="SurfaceMainInputLayer"/>, but binds
/// next/prev to gamepad bumpers (actions 14/15) that the keyboard never reaches — so we call those public
/// methods directly, the same pattern as <see cref="WindowHotkeys"/> / <see cref="PartyHotkeys"/>. The chosen
/// object is voiced automatically by <see cref="ExplorationEvents"/>.
///
/// PageUp = previous, PageDown = next, End = interact (walk to &amp; use), Home = re-announce the current pick.
/// Gated to console mode + exploration (GameModeType.Default, not in combat) so it never clashes with mouse
/// play or combat target cycling (a separate input layer, left to the combat subsystem).
/// </summary>
internal static class ExplorationNav
{
    public static void Update()
    {
        var game = Game.Instance;
        if (game == null || game.ControllerMode != Game.ControllerModeType.Gamepad) return;
        if (game.CurrentMode != GameModeType.Default) return;
        if (game.Player != null && game.Player.IsInCombat) return;

        if (Input.GetKeyDown(KeyCode.PageUp)) Cycle(prev: true);
        else if (Input.GetKeyDown(KeyCode.PageDown)) Cycle(prev: false);
        else if (Input.GetKeyDown(KeyCode.End)) Interact();
        else if (Input.GetKeyDown(KeyCode.Home)) ExplorationEvents.Instance.ReannounceCurrent();
    }

    private static void Cycle(bool prev)
    {
        var layer = SurfaceMainInputLayer.Instance;
        if (layer == null) { Speaker.Speak("Not in exploration.", interrupt: false); return; }
        try
        {
            if (prev) layer.OnPrevInteractable();
            else layer.OnNextInteractable();
            // The chosen-object change is voiced by ExplorationEvents on the next interactable-set update.
        }
        catch (Exception e) { Main.Log?.Error("ExplorationNav.Cycle failed: " + e); }
    }

    private static void Interact()
    {
        var layer = SurfaceMainInputLayer.Instance;
        if (layer == null) return;
        if (Game.Instance.SelectionCharacter?.SelectedUnit?.Value == null)
        {
            Speaker.Speak("No character selected.", interrupt: false);
            return;
        }
        try { layer.OnInteract(); }
        catch (Exception e) { Main.Log?.Error("ExplorationNav.Interact failed: " + e); }
    }
}
