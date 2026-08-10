using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Settings.KeyBindSetupDialog;

namespace RTAccess.Screens
{
    /// <summary>
    /// The key-binding capture dialog (SettingsVM.CurrentKeyBindDialog — the game's own
    /// KeyBindingSetupDialog, opened by a key-binding slot's Enter). Key capture lives in the game's VIEW
    /// (KeyBindingSetupDialogBaseView reads raw Input each frame, Escape included), so this screen sets
    /// <see cref="CapturesRawInput"/> — InputManager stops dispatching while it's on top, so the keys
    /// reach the game's capture routine. We just announce: instructions on open, and the "already in use"
    /// message when a conflict keeps the dialog open. Success/cancel closes the dialog → we pop and the
    /// slot's LIVE combo value announces the new binding.
    /// </summary>
    public sealed class KeyBindCaptureScreen : Screen
    {
        /// <summary>Set by the key-binding slot before opening, so we can name what's being bound.</summary>
        public static string PendingLabel;

        /// <summary>The live dialog VM, or null when no capture is up.</summary>
        public static KeyBindingSetupDialogVM Dialog()
        {
            var cvm = Game.Instance?.RootUiContext?.CommonVM;
            return cvm?.SettingsVM.Value?.CurrentKeyBindDialog.Value;
        }

        public override string Key => "overlay.keybindcapture";
        public override int Layer => 27; // above Settings (25), below the message modal (30)
        public override bool CapturesRawInput => true;
        public override bool IsActive() => Dialog() != null;

        private bool _lastOccupied;
        private Kingmaker.Settings.Entities.KeyBindingData _lastBinding;

        public override void OnPush() { _lastOccupied = false; _lastBinding = default; }

        // A stale label must not name the wrong control if a future open path skips setting it.
        public override void OnPop() { PendingLabel = null; }

        public override void OnFocus()
        {
            // No ScreenName — the prompt IS the announcement (base would speak nothing anyway).
            string what = string.IsNullOrEmpty(PendingLabel) ? "" : PendingLabel + ". ";
            Tts.Speak(Loc.T("bind.prompt", new { what }));
        }

        public override void OnUpdate()
        {
            var dlg = Dialog();
            if (dlg == null) return;
            // Warn on every occupied ATTEMPT, not just the false→true edge: the VM reassigns
            // CurrentKeyBinding per attempt, so a changed combo marks a new attempt even while
            // occupied stays true. (Same occupied chord twice in a row is indistinguishable by
            // value and stays silent — the user just heard that exact warning.)
            if (dlg.CurrentBindingIsOccupied && (!_lastOccupied || !dlg.CurrentKeyBinding.Equals(_lastBinding)))
                Tts.Speak(Loc.T("bind.in_use"), interrupt: true);
            _lastOccupied = dlg.CurrentBindingIsOccupied;
            _lastBinding = dlg.CurrentKeyBinding;
        }
    }
}
