namespace RTAccess
{
    /// <summary>
    /// The master flag for whether the mod owns the keyboard. It no longer mutes the game keyboard directly:
    /// when active, the <see cref="RTAccess.Accessibility.KeyboardArbitration"/> patch suppresses only the game
    /// keys the mod actually CLAIMS this frame (per-chord), leaving every un-overridden game key live — and a
    /// modal (Exclusive) mod screen mutes the game keyboard entirely. When inactive the game keyboard is fully
    /// vanilla.
    ///
    /// This replaces the old blanket <c>KeyboardAccess.Disabled</c> mute (which killed ALL game hotkeys while
    /// focus mode was on, forcing the mod to re-implement the game's own bindings). See
    /// docs/input-system-architecture-review.md and the <c>rt-input-system-verdict</c> memory.
    /// </summary>
    public static class FocusMode
    {
        private static bool _active;

        public static bool Active => _active;

        public static void Toggle() => Set(!_active);

        public static void Set(bool on) => _active = on;
    }
}
