using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.MessageBox;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's generic message/confirm modal (CommonVM.MessageBoxVM) — used for the settings
    /// save-changes prompt and confirmations across the game. Reads the message text and exposes the
    /// Accept / Decline buttons, activating them via the VM (OnAcceptPressed / OnDeclinePressed). Layer 30,
    /// Exclusive (owns the keyboard above whatever opened it). Text-field / progress / checkbox variants
    /// aren't handled yet.
    /// </summary>
    public sealed class MessageBoxScreen : Screen
    {
        public MessageBoxScreen() { Wrap = true; } // Tab cycles message ↔ buttons

        public override string Key => "overlay.messagebox";
        public override string ScreenName => Loc.T("screen.dialog");
        public override int Layer => 30;
        public override bool Exclusive => true; // a modal owns the keyboard

        public override bool IsActive()
        {
            var vm = Vm();
            return vm != null && !vm.IsProgressBar.Value; // skip non-interactive progress boxes
        }

        private static MessageBoxVM Vm()
        {
            var cvm = Game.Instance?.RootUiContext?.CommonVM;
            return cvm?.MessageBoxVM.Value;
        }

        private MessageBoxVM _builtFrom;

        public override void OnPush() { _builtFrom = null; Rebuild(); }
        public override void OnPop() { Clear(); _builtFrom = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm != null && vm != _builtFrom)
            {
                // Modal VM swapped (one closed, another opened) — re-home focus.
                Rebuild();
                Navigation.Attach(this);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
            }
        }

        private void Rebuild()
        {
            Clear();
            var vm = Vm();
            _builtFrom = vm;
            if (vm == null) return;

            // Message body first (focusable so it can be re-read), then the buttons — all direct children
            // of the root panel, so they're individual Tab-stops.
            if (!string.IsNullOrEmpty(vm.MessageText))
                Add(new TextElement(vm.MessageText));
            Add(new ProxyActionButton(vm.AcceptText, () => true, () => vm.OnAcceptPressed()));
            if (vm.ShowDecline.Value)
                Add(new ProxyActionButton(vm.DeclineText, () => true, () => vm.OnDeclinePressed()));
        }
    }
}
