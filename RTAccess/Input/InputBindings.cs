using UnityEngine;
using RTAccess.UI;

namespace RTAccess.Input
{
    /// <summary>
    /// Registers the mod's input actions. Phase 2: the focus-mode toggle (Global) + UI navigation
    /// (dispatched into the active <see cref="Navigator"/>, live only while focus mode owns the keyboard).
    /// Keys are captured via raw <c>UnityEngine.Input</c> (mode-independent). Grows per phase
    /// (exploration, service windows, …) as those screens land.
    /// </summary>
    public static class InputBindings
    {
        public static void RegisterDefaults()
        {
            // ---- Global: always live, even with focus mode off ----
            InputManager.Register("toggle_focus", "Toggle focus mode", InputCategory.Global, () =>
            {
                FocusMode.Toggle();
                Tts.Speak(Loc.T(FocusMode.Active ? "focus.on" : "focus.off"), interrupt: true);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
            }).AddBinding(KeyCode.A, ctrl: true, shift: true);

            // ---- Review buffers (Global, always live in a game): a second navigation axis that queries a
            // unit's live state (HP / AP / defenses / buffs) WITHOUT moving UI focus. Alt+Left/Right switch
            // buffer, Alt+Up/Down step lines. Global so they work in mouse mode and even while a HUD/menu is
            // focused; the handlers stand down out of a game. Alt+arrows don't collide with the bare-arrow UI
            // nav (exact-modifier match) or PartyHotkeys' Alt+digits. See RTAccess.Buffers.
            InputManager.Register("buffer.prev", "Previous review buffer", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Buffers.BufferControls.PrevBuffer(); }).AddBinding(KeyCode.LeftArrow, alt: true).Repeating();
            InputManager.Register("buffer.next", "Next review buffer", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Buffers.BufferControls.NextBuffer(); }).AddBinding(KeyCode.RightArrow, alt: true).Repeating();
            InputManager.Register("buffer.line_prev", "Previous review line", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Buffers.BufferControls.PrevItem(); }).AddBinding(KeyCode.UpArrow, alt: true).Repeating();
            InputManager.Register("buffer.line_next", "Next review line", InputCategory.Global,
                () => { if (InAGame()) RTAccess.Buffers.BufferControls.NextItem(); }).AddBinding(KeyCode.DownArrow, alt: true).Repeating();

            // ---- UI: screen/menu navigation (dispatched into the active navigator) ----
            InputManager.Register("ui.up", "Navigate up", InputCategory.UI).AddBinding(KeyCode.UpArrow).Repeating();
            InputManager.Register("ui.down", "Navigate down", InputCategory.UI).AddBinding(KeyCode.DownArrow).Repeating();
            InputManager.Register("ui.left", "Navigate left", InputCategory.UI).AddBinding(KeyCode.LeftArrow).Repeating();
            InputManager.Register("ui.right", "Navigate right", InputCategory.UI).AddBinding(KeyCode.RightArrow).Repeating();
            InputManager.Register("ui.next", "Next region (Tab)", InputCategory.UI).AddBinding(KeyCode.Tab).Repeating();
            InputManager.Register("ui.prev", "Previous region (Shift+Tab)", InputCategory.UI).AddBinding(KeyCode.Tab, shift: true).Repeating();
            InputManager.Register("ui.activate", "Activate control", InputCategory.UI)
                .AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            InputManager.Register("ui.secondary", "Secondary action", InputCategory.UI).AddBinding(KeyCode.Backspace);
            InputManager.Register("ui.back", "Back / close", InputCategory.UI).AddBinding(KeyCode.Escape);
            InputManager.Register("ui.tooltip", "Read tooltip", InputCategory.UI)
                .AddBinding(KeyCode.Space).AddBinding(KeyCode.F1);
            InputManager.Register("ui.home", "Jump to first item", InputCategory.UI).AddBinding(KeyCode.Home);
            InputManager.Register("ui.end", "Jump to last item", InputCategory.UI).AddBinding(KeyCode.End);
        }

        // The review-buffer keys are Global (always polled), so their handlers stand down when not in a
        // loaded game (no party to read).
        private static bool InAGame() => Kingmaker.Game.Instance?.Player != null;
    }
}
