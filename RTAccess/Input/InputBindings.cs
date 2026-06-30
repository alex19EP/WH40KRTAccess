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
    }
}
