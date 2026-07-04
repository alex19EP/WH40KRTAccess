using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.MessageBox;
using RTAccess.UI;
using RTAccess.UI.Graph;
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
    ///
    /// Graph-native: declared fresh from the live VM every render. Node keys carry the VM's identity, so a
    /// modal SWAP (one closed, another opened) drops the old keys and focus re-homes to the new message
    /// with a fresh readout — no rebuild bookkeeping.
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

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "msgbox:" + vm.GetHashCode() + ":";

            // Message body first (focusable so it can be re-read), then the buttons — each its own
            // Tab-stop, so Tab cycles message ↔ buttons.
            if (!string.IsNullOrEmpty(vm.MessageText))
                b.BeginStop("msg").AddItem(ControlId.Structural(k + "msg"), GraphNodes.Text(() => vm.MessageText));

            // Text-field box: an Edit affordance types into the box's live field (the label reads the
            // current value live, so it's heard before Accept). Accept forwards the field's value;
            // Decline cancels.
            if (vm.BoxType == DialogMessageBoxBase.BoxType.TextField)
                b.BeginStop("edit").AddItem(ControlId.Structural(k + "edit"), GraphNodes.Button(
                    () => Loc.T("modal.edit_text", new { value = vm.InputText.Value ?? "" }),
                    () => StartEditing(vm)));

            b.BeginStop("accept").AddItem(ControlId.Structural(k + "accept"),
                GraphNodes.Button(() => vm.AcceptText, () => vm.OnAcceptPressed()));
            if (vm.ShowDecline.Value)
                b.BeginStop("decline").AddItem(ControlId.Structural(k + "decline"),
                    GraphNodes.Button(() => vm.DeclineText, () => vm.OnDeclinePressed()));
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
