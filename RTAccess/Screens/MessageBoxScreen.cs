using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.MessageBox;
using RTAccess.UI;
using RTAccess.UI.Proxies;
using TMPro;

namespace RTAccess.Screens
{
    /// <summary>
    /// The game's generic message/confirm modal (CommonVM.MessageBoxVM) — used for the settings
    /// save-changes prompt and confirmations across the game. Reads the message text and exposes the
    /// Accept / Decline buttons, activating them via the VM (OnAcceptPressed / OnDeclinePressed). A
    /// <see cref="DialogMessageBoxBase.BoxType.TextField"/> box (e.g. name-this-save, save-overwrite) also
    /// gets an Edit affordance that hands the box's live input field to <see cref="TextEntry"/> to type;
    /// Accept then forwards the field's value (InputText) to the box's text callback. Layer 30, Exclusive
    /// (owns the keyboard above whatever opened it). Progress / checkbox variants aren't handled yet.
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

            // Text-field box: an Edit affordance types into the box's live field (the label reads the current
            // value so it's heard before Accept). Accept forwards the field's value; Decline cancels.
            if (vm.BoxType == DialogMessageBoxBase.BoxType.TextField)
                Add(new ProxyActionButton(() => Loc.T("modal.edit_text", new { value = vm.InputText.Value ?? "" }),
                    () => true, () => StartEditing(vm), actionVerb: "edit"));

            Add(new ProxyActionButton(vm.AcceptText, () => true, () => vm.OnAcceptPressed()));
            if (vm.ShowDecline.Value)
                Add(new ProxyActionButton(vm.DeclineText, () => true, () => vm.OnDeclinePressed()));
        }

        // Hand the box's own TMP field to TextEntry so Unity/TMP own caret, Unicode and IME; typing routes
        // through the field's game-wired binding to InputText, which Accept then forwards.
        private static void StartEditing(MessageBoxVM box)
        {
            var field = FindInputField(box);
            if (field != null) TextEntry.Begin(field, Loc.T("modal.text"));
            else Tts.Speak(Loc.T("text.unavailable"));
        }

        // A TextField box shows exactly one live TMP field whose text tracks InputText; match by that, with any
        // live interactable field as a fallback (mirrors NameEntryScreen's grab).
        private static TMP_InputField FindInputField(MessageBoxVM box)
        {
            var target = box?.InputText.Value;
            TMP_InputField fallback = null;
            foreach (var f in UnityEngine.Object.FindObjectsByType<TMP_InputField>(UnityEngine.FindObjectsSortMode.None))
            {
                if (f == null || !f.isActiveAndEnabled || !f.IsInteractable()) continue;
                if (!string.IsNullOrEmpty(target) && f.text == target) return f;
                if (fallback == null) fallback = f;
            }
            return fallback;
        }
    }
}
