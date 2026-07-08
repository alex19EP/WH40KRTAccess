using RTAccess.Input;
using RTAccess.Screens;

namespace RTAccess.UI
{
    /// <summary>
    /// Holds the active Navigator (swappable by user preference later) and is the
    /// entry point input dispatches into. ScreenManager re-attaches it on screen change.
    /// </summary>
    public static class Navigation
    {
        public static Navigator Active = new GraphNavigator();

        public static void Attach(Screen screen) => Active?.Attach(screen);

        /// <summary>Notify that a screen closed (its per-screen nav state is dropped).</summary>
        public static void ScreenClosed(Screen screen) => Active?.ScreenClosed(screen);

        /// <summary>True when something is focused (the navigator owns the keys). False in an unfocused
        /// screen like exploration, where arrows bubble to the overlay. Delegates to the navigator's own
        /// <see cref="Navigator.HasFocus"/>, so every consumer inherits the right answer.</summary>
        public static bool HasFocus => Active != null && Active.HasFocus;

        public static bool DispatchJustPressed(InputAction action) =>
            Active != null && Active.OnInputJustPressed(action);

        /// <summary>Feed typed characters to the active navigator's type-ahead search (per frame).</summary>
        public static void TickTypeahead() => Active?.TickTypeahead();

        public static void AnnounceCurrent() => Active?.AnnounceCurrent();

        /// <summary>Re-establish initial focus if the focused screen has focusable content but nothing is
        /// focused yet (e.g. a screen that built its content lazily after attach). Ticked each frame.</summary>
        public static void EnsureFocus() => Active?.EnsureFocus();

        /// <summary>Return to the unfocused (exploration) state — see <see cref="Navigator.Blur"/>.</summary>
        public static void Blur() => Active?.Blur();
    }
}
