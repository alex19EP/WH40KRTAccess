using UnityEngine.EventSystems;

namespace RTAccess.UI
{
    /// <summary>
    /// Generalizes the dialogue-only <see cref="Proxies.DialogChoiceGate"/> fix to EVERY mod-owned screen.
    /// The game's own views stay live under our parallel overlay, and a view's EventSystem-SELECTED button
    /// still reacts to Unity's "Submit" (Enter) — a third input path our navigator and keyboard arbitration
    /// both miss (the "parallel-tree screens leak via Unity's EventSystem" gotcha). Dialogue answers needed a
    /// per-VM gate precisely because of this; MessageBox accept/decline, Settings apply/close, loot take-all
    /// and level-up commit all relied on the unverified assumption that their game view held no EventSystem
    /// selection. While the mod's navigator owns focus we keep the game view holding NO selection, so the
    /// Submit path has nothing to fire behind our own activation.
    ///
    /// SKIPPED while a text field is being edited (our <see cref="TextEntry"/> driving one of the game's
    /// TMP_InputFields, or the game's own field selected): a TMP_InputField needs its EventSystem selection to
    /// receive keystrokes, so clearing it there would break typing. SKIPPED when focus mode is off (the mod
    /// isn't driving — the game keeps a vanilla keyboard) or nothing is focused (exploration — the game
    /// legitimately owns Enter). Per-VM gates (DialogChoiceGate) stay valid: they call the VM directly and
    /// never depend on EventSystem selection, so they keep working with or without a live selection.
    /// </summary>
    internal static class EventSystemGate
    {
        public static void Tick()
        {
            if (!FocusMode.Active) return;      // mod not driving — vanilla keyboard owns Submit
            if (!Navigation.HasFocus) return;   // nothing focused (exploration) — the game owns Enter
            // A text field is live: it must keep its EventSystem selection to receive keystrokes.
            if (TextEntry.SuppressInput) return;
            if (Kingmaker.UI.InputSystems.KeyboardAccess.IsInputFieldSelected()) return;

            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null)
                es.SetSelectedGameObject(null);
        }
    }
}
