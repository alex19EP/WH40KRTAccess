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
    // TODO(needs-verify): the game populates SurfaceMainInputLayer.m_InteractableObjects only inside its own
    // OnUpdate, gated `!Game.Instance.IsControllerMouse && LayerBinded.Value` (SurfaceMainInputLayer.cs:107 →
    // private UpdateInteractions()). In mouse mode that path never runs, so the interactable set stays empty
    // and OnNext/OnPrevInteractable cycle nothing. Driving UpdateInteractions()/UpdateInteractableSet()
    // ourselves is unverified (private members; LayerBinded gate; gamepad-stick side effects). Until that is
    // settled in-game, keep cycling OFF so this no-ops cleanly rather than speaking "Not in exploration".
    // Fallback if it can't be driven: our own EntityBoundsHelper scan + raise the count handler ourselves.
    private static bool EngineScanEnabled = false;

    public static void Update()
    {
        // Re-gated for mouse mode (was ControllerMode == Gamepad). PageUp/Down/Home/End collide with UI nav
        // (Home/End), so yield to the HUD when it's focused. Interactable cycling is exploration-only.
        if (!RTAccess.Screens.InGameScreen.ExplorationActive || RTAccess.UI.Navigation.HasFocus) return;
        var game = Game.Instance;
        if (game.Player != null && game.Player.IsInCombat) return;

        // Cycling is engine-dead in mouse mode (see EngineScanEnabled above) — gate it off until the
        // self-driven interactable scan is verified in-game. The HUD works regardless.
        if (!EngineScanEnabled) return;

        if (UnityEngine.Input.GetKeyDown(KeyCode.PageUp)) Cycle(prev: true);
        else if (UnityEngine.Input.GetKeyDown(KeyCode.PageDown)) Cycle(prev: false);
        else if (UnityEngine.Input.GetKeyDown(KeyCode.End)) Interact();
        else if (UnityEngine.Input.GetKeyDown(KeyCode.Home)) ExplorationEvents.Instance.ReannounceCurrent();
    }

    private static void Cycle(bool prev)
    {
        var layer = SurfaceMainInputLayer.Instance;
        if (layer == null) { Speaker.Speak("Not in exploration.", interrupt: true); return; }
        try
        {
            // The chosen-object change is voiced by ExplorationEvents on the next interactable-set update; mark
            // this frame as a user cycle so that announce interrupts (you pressed a key) while passive walk-by
            // announces still queue. Per [[rt-interrupt-speech-rule]].
            ExplorationEvents.Instance.MarkUserCycle();
            if (prev) layer.OnPrevInteractable();
            else layer.OnNextInteractable();
        }
        catch (Exception e) { Main.Log?.Error("ExplorationNav.Cycle failed: " + e); }
    }

    private static void Interact()
    {
        var layer = SurfaceMainInputLayer.Instance;
        if (layer == null) return;
        if (Game.Instance.SelectionCharacter?.SelectedUnit?.Value == null)
        {
            Speaker.Speak("No character selected.", interrupt: true);
            return;
        }
        try { layer.OnInteract(); }
        catch (Exception e) { Main.Log?.Error("ExplorationNav.Interact failed: " + e); }
    }
}
