using Kingmaker;

namespace RTAccess
{
    /// <summary>
    /// When active, holds the game's <c>KeyboardAccess.Disabled</c> guard so the game's own keyboard
    /// shortcuts are suppressed and our navigation owns the keyboard. <c>KeyboardAccess.Tick()</c>
    /// early-returns while Disabled is set, so this is the game's own, fully reversible "mute my shortcuts"
    /// lever.
    ///
    /// RT's <c>CountingGuard</c> exposes <c>SetValue(bool)</c> (the WotR <c>.Scope()</c> overload isn't in
    /// the RT build) — the same lever the game's own ControllerMode windows use. Our own keys are captured
    /// via InputManager's poll, independent of this guard.
    /// </summary>
    public static class FocusMode
    {
        private static bool _active;
        private static object _keyboard; // the KeyboardAccess instance whose guard we set

        public static bool Active => _active;

        public static void Toggle() => Set(!_active);

        public static void Set(bool on)
        {
            if (on == _active) return;
            _active = on;
            var kb = Game.Instance?.Keyboard;
            if (kb == null)
            {
                // Game/Keyboard may not exist extremely early; Tick re-asserts once it does.
                _keyboard = null;
                if (on) Main.Log?.Log("FocusMode: deferred (game not ready).");
                return;
            }
            kb.Disabled.SetValue(on);
            _keyboard = on ? (object)kb : null;
        }

        /// <summary>Per-frame: re-assert suppression when the game rebuilds its keyboard. Returning to the
        /// main menu / loading a save constructs a fresh KeyboardAccess, so a guard set on the old instance
        /// suppresses nothing — the game's own hotkeys would come back alive.</summary>
        public static void Tick()
        {
            if (!_active) return;
            var kb = Game.Instance?.Keyboard;
            if (kb == null || ReferenceEquals(kb, _keyboard)) return;
            kb.Disabled.SetValue(true);
            _keyboard = kb;
            Main.Log?.Log("FocusMode: re-engaged on a fresh KeyboardAccess (scene reload).");
        }
    }
}
